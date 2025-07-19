using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SignalBus.Models;
using SignalBus.Services;

namespace SignalBus
{
    [JsonSerializable(typeof(SignalMessage))]
    [JsonSerializable(typeof(Envelope))]
    [JsonSerializable(typeof(SyncMessage))]
    [JsonSerializable(typeof(DataMessage))]
    [JsonSerializable(typeof(AssistantRequest))]
    internal partial class SignalMessageJsonContext : JsonSerializerContext
    {
    }

    public class Worker(
        ILogger<Worker> logger,
        IConfiguration configuration,
        IAssistantService assistantService,
        ISignalService signalService,
        ITimescaleDbService timescaleDbService,
        ISignalGroupService signalGroupService,
        IAuthorizationService authorizationService
    ) : BackgroundService
    {
        private readonly ILogger<Worker> _logger = logger;
        private readonly IAssistantService _assistantService = assistantService;
        private readonly ISignalService _signalService = signalService;
        private readonly ISignalGroupService _signalGroupService = signalGroupService;
        private readonly ITimescaleDbService _timescaleDbService = timescaleDbService;
        private readonly IAuthorizationService _authorizationService = authorizationService;

        private readonly string _registeredAccount = configuration["REGISTERED_ACCOUNT"] ?? throw new InvalidOperationException("REGISTERED_ACCOUNT environment variable is required");
        private readonly Uri _socketUri = new(
            $"ws://{configuration["SIGNAL_ENDPOINT"] ?? throw new InvalidOperationException("SIGNAL_ENDPOINT environment variable is required")}/v1/receive/{configuration["REGISTERED_ACCOUNT"] ?? throw new InvalidOperationException("REGISTERED_ACCOUNT environment variable is required")}"
        );
        private ClientWebSocket _ws = new();

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Reconnect loop
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("Connecting to {Uri}", _socketUri);
                    await _ws.ConnectAsync(_socketUri, stoppingToken);

                    // Listen loop
                    var buffer = new byte[4 * 1024];
                    while (_ws.State == WebSocketState.Open && !stoppingToken.IsCancellationRequested)
                    {
                        var result = await _ws.ReceiveAsync(buffer, stoppingToken);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            _logger.LogInformation("Server closed connection: {Status}", result.CloseStatus);
                            await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client closing", stoppingToken);
                            break;
                        }

                        var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        _logger.LogInformation("Received: {Message}", message);
                        await HandleMessageAsync(message, stoppingToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "WebSocket error, will retry in 5s");
                }

                // Dispose old socket and prepare a new one
                _ws?.Dispose();
                _ws = new ClientWebSocket();

                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        private async Task HandleMessageAsync(string jsonPayload, CancellationToken ct)
        {
            try
            {
                var signalMessage = JsonSerializer.Deserialize(jsonPayload, SignalMessageJsonContext.Default.SignalMessage);
                if (signalMessage != null)
                {
                    _logger.LogInformation("Parsed SignalMessage from account: {Account}", signalMessage.Account);
                    _logger.LogInformation("Source: {Source}, SourceNumber: {SourceNumber}",
                        signalMessage.Envelope.Source, signalMessage.Envelope.SourceNumber);

                    if (signalMessage.Envelope.DataMessage != null)
                    {
                        signalMessage.Envelope.DataMessage.Message ??= signalMessage.Envelope.DataMessage.Sticker != null ? "STICKER" : signalMessage.Envelope.DataMessage.Attachments != null ? "ATTACHMENT" : null;
                        _logger.LogDebug("Data message: {Message}", signalMessage.Envelope.DataMessage.Message);

                        if (!_authorizationService.IsAuthorized(signalMessage.Envelope.Source))
                        {
                            _logger.LogWarning("Unauthorized contact: {Source} ignored", signalMessage.Envelope.Source);
                            return;
                        }

                        var source = signalMessage.Envelope.Source;
                        string? group = null;

                        if(signalMessage.Envelope.DataMessage.GroupInfo != null)
                        {
                            group = await _signalGroupService.GetGroupIdAsync(signalMessage.Envelope.DataMessage.GroupInfo.GroupId);
                        }

                        // Queue message for TimescaleDB insertion (fire and forget)
                        try
                        {
                            var messageRecord = MessageRecord.FromSignalMessage(signalMessage, group);
                            await _timescaleDbService.QueueMessageAsync(messageRecord);
                            _logger.LogDebug("Message queued for TimescaleDB insertion");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to queue message for TimescaleDB - continuing with processing");
                        }

                        // For group messages, only process with assistant if mentioned
                        if(signalMessage.Envelope.DataMessage.GroupInfo != null)
                        {
                            if (signalMessage.Envelope.DataMessage.Mentions?.Any(e => e.Name == _registeredAccount) != true)
                            {
                                _logger.LogDebug("Not mentioned in group chat - message logged but not processed by assistant");
                                return;
                            }
                        }

                        _logger.LogInformation("Message sent in chat with {Group}", source);
                        await _signalService.IndicateTypingAsync(group ?? source);

                        try
                        {
                            var userId = group ?? source;
                            var assistantResponse = await _assistantService.SendMessageAsync(
                                signalMessage.Envelope.DataMessage.Message, 
                                userId);
                            
                            _logger.LogDebug("Assistant response for user {UserId}: {Response}", userId, assistantResponse);

                            if (assistantResponse != null && assistantResponse != "")
                            {
                                await _signalService.SendMessageAsync(assistantResponse, source, group);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to send message to assistant service");
                            await _signalService.HideIndicatorAsync(group ?? source);
                        }
                    }
                }
                else
                {
                    _logger.LogWarning("Failed to deserialize message: null result");
                }
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to parse JSON message: {JsonPayload}", jsonPayload);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling message: {JsonPayload}", jsonPayload);
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_ws?.State == WebSocketState.Open)
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Host shutting down", cancellationToken);
            await base.StopAsync(cancellationToken);
        }
    }
}

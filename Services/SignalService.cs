using Microsoft.Extensions.Configuration;
using SignalBus.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace SignalBus.Services;

public class SignalSendRequest
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("number")]
    public string Number { get; set; } = string.Empty;

    [JsonPropertyName("recipients")]
    public required string[] Recipients { get; set; }
}

public class SignalTypeIndicatorRequest
{
    [JsonPropertyName("recipient")]
    public string Recipient { get; set; } = string.Empty;
}

public class SignalSendResponse
{
    [JsonPropertyName("timestamp")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long Timestamp { get; set; }
}

[JsonSerializable(typeof(SignalSendRequest))]
[JsonSerializable(typeof(SignalSendResponse))]
[JsonSerializable(typeof(SignalTypeIndicatorRequest))]
internal partial class SignalJsonContext : JsonSerializerContext
{
}

public interface ISignalService
{
    Task<string> SendMessageAsync(string message, string recipient, string? group);
    Task IndicateTypingAsync(string recipient);
    Task HideIndicatorAsync(string recipient);
}

public class SignalService(HttpClient httpClient, ITimescaleDbService timescaleDbService, ILogger<SignalService> logger, IConfiguration configuration) : ISignalService
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ITimescaleDbService _timescaleDbService = timescaleDbService;
    private readonly ILogger<SignalService> _logger = logger;
    private readonly string _signalEndpoint = configuration["SIGNAL_ENDPOINT"] ?? throw new InvalidOperationException("SIGNAL_ENDPOINT environment variable is missing");
    private readonly string _registered_account = configuration["REGISTERED_ACCOUNT"] ?? throw new InvalidOperationException("REGISTERED_ACCOUNT environment variable is required");

    public async Task<string> SendMessageAsync(string message, string recipient, string? group)
    {
        try
        {
            var requestUrl = $"http://{_signalEndpoint}/v2/send";
            var body = new SignalSendRequest()
            {
                Message = message,
                Number = _registered_account,
                Recipients = [group ?? recipient]
            };

            _logger.LogInformation("Sending message to Signal endpoint: {Endpoint}", _signalEndpoint);

            var jsonContent = JsonSerializer.Serialize(body, SignalJsonContext.Default.SignalSendRequest);
            var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(requestUrl, httpContent);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("Successfully sent message to Signal endpoint");

                if (responseContent != null)
                {
                    var responseModel = JsonSerializer.Deserialize(responseContent, SignalJsonContext.Default.SignalSendResponse);

                    if (responseModel != null)
                    {
                        // Queue message for TimescaleDB insertion (fire and forget)
                        try
                        {
                            body.Recipients = [recipient];
                            var messageRecords = MessageRecord.FromSignalSendRequest(body, responseModel.Timestamp, group);
                            await Task.WhenAll(messageRecords.Select(_timescaleDbService.QueueMessageAsync));
                            _logger.LogDebug("Message queued for TimescaleDB insertion");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to queue message for TimescaleDB - continuing with processing");
                        }
                    }

                    return responseContent;
                }

                return "";
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Signal endpoint returned error status {StatusCode}: {ErrorContent}", response.StatusCode, errorContent);
                throw new HttpRequestException($"Signal endpoint returned error status {response.StatusCode}: {errorContent}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while sending message to Signal endpoint");
            throw;
        }
    }

    public async Task IndicateTypingAsync(string recipient)
    {
        try
        {
            var requestUrl = $"http://{_signalEndpoint}/v1/typing-indicator/{_registered_account}";
            var body = new SignalTypeIndicatorRequest()
            {
                Recipient = recipient
            };

            _logger.LogInformation("Trigger typing indicator at Signal endpoint: {Endpoint}", _signalEndpoint);

            var jsonContent = JsonSerializer.Serialize(body, SignalJsonContext.Default.SignalTypeIndicatorRequest);
            var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");
            var response = await _httpClient.PutAsync(requestUrl, httpContent);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("Successfully triggered typing indicator at Signal endpoint");
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Signal endpoint returned error status {StatusCode}: {ErrorContent}", response.StatusCode, errorContent);
                throw new HttpRequestException($"Signal endpoint returned error status {response.StatusCode}: {errorContent}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while triggering typing indicator at Signal endpoint");
            throw;
        }
    }

    public async Task HideIndicatorAsync(string recipient)
    {
        try
        {
            var number = _registered_account;
            var signalEndpoint = Environment.GetEnvironmentVariable("SIGNAL_ENDPOINT") ?? throw new InvalidOperationException("SIGNAL_ENDPOINT environment variable is missing");
            var requestUrl = $"http://{signalEndpoint}/v1/typing-indicator/{number}";
            var body = new SignalTypeIndicatorRequest()
            {
                Recipient = recipient
            };

            _logger.LogInformation("Hide typing indicator at Signal endpoint: {Endpoint}", signalEndpoint);

            var jsonContent = JsonSerializer.Serialize(body, SignalJsonContext.Default.SignalTypeIndicatorRequest);
            var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");
            
            var request = new HttpRequestMessage(HttpMethod.Delete, requestUrl)
            {
                Content = httpContent
            };
            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("Successfully hidden typing indicator at Signal endpoint");
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Signal endpoint returned error status {StatusCode}: {ErrorContent}", response.StatusCode, errorContent);
                throw new HttpRequestException($"Signal endpoint returned error status {response.StatusCode}: {errorContent}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while hidding typing indicator at Signal endpoint");
            throw;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace SignalBus.Services;

public class AssistantRequest
{
    [JsonPropertyName("chatInput")]
    public string ChatInput { get; set; } = string.Empty;

    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;
}

public interface IAssistantService
{
    Task<string> SendMessageAsync(string message, string userId);
}

public class AssistantService(HttpClient httpClient, ILogger<AssistantService> logger, IConfiguration configuration) : IAssistantService
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ILogger<AssistantService> _logger = logger;
    private readonly string _webhookUrl = configuration["WEBHOOK_URL"] ?? throw new InvalidOperationException("WEBHOOK_URL environment variable is missing");
    private readonly string _authToken = configuration["AUTH_TOKEN"] ?? throw new InvalidOperationException("AUTH_TOKEN environment variable is missing");

    public async Task<string> SendMessageAsync(string message, string userId)
    {
        try
        {
            var requestPayload = new AssistantRequest
            {
                ChatInput = message,
                Action = "sendMessage",
                SessionId = "intelligence-" + userId,
            };

            var jsonContent = JsonSerializer.Serialize(requestPayload, SignalMessageJsonContext.Default.AssistantRequest);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Clear();
            var encodedAuthToken = Convert.ToBase64String(Encoding.UTF8.GetBytes(_authToken));
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Basic {encodedAuthToken}");

            _logger.LogInformation("Sending message to assistant webhook for user {UserId}", userId);

            var response = await _httpClient.PostAsync(_webhookUrl, content);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("Successfully received response from assistant webhook for user {UserId}", userId);
                return responseContent;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Assistant webhook returned error status {StatusCode}: {ErrorContent}", response.StatusCode, errorContent);
                throw new HttpRequestException($"Assistant webhook returned error status {response.StatusCode}: {errorContent}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while sending message to assistant webhook for user {UserId}", userId);
            throw;
        }
    }
}

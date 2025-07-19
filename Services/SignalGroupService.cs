using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace SignalBus.Services;

public class GroupIdResponse
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("internal_id")]
    public string InternalId { get; set; } = string.Empty;

    [JsonPropertyName("members")]
    public List<string> Members { get; set; } = new List<string>();

    [JsonPropertyName("blocked")]
    public bool Blocked { get; set; } = false;

    [JsonPropertyName("pending_invites")]
    public List<string> PendingInvites { get; set; } = new List<string>();

    [JsonPropertyName("pending_requests")]
    public List<string> PendingRequests { get; set; } = new List<string>();

    [JsonPropertyName("invite_link")]
    public string InviteLink { get; set; } = string.Empty;

    [JsonPropertyName("admins")]
    public List<string> Admins { get; set; } = new List<string>();
}

[JsonSerializable(typeof(List<GroupIdResponse>))]
internal partial class SignalGroupJsonContext : JsonSerializerContext
{
}

public interface ISignalGroupService
{
    Task<string> GetGroupIdAsync(string internalId);
}

public class SignalGroupService(HttpClient httpClient, ILogger<SignalGroupService> logger, IConfiguration configuration) : ISignalGroupService
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ILogger<SignalGroupService> _logger = logger;
    private readonly string _signalEndpoint = configuration["SIGNAL_ENDPOINT"] ?? throw new InvalidOperationException("SIGNAL_ENDPOINT environment variable is missing");
    private readonly string _registered_account = configuration["REGISTERED_ACCOUNT"] ?? throw new InvalidOperationException("REGISTERED_ACCOUNT environment variable is required");
    private readonly Dictionary<string, string> _cache = new Dictionary<string, string>();
    private readonly LinkedList<string> _accessOrder = new LinkedList<string>();
    private readonly int _maxCacheSize = int.TryParse(configuration["GROUP_CACHE_SIZE"], out var cacheSize) ? cacheSize : 1000;
    private readonly object _cacheLock = new object();

    public async Task<string> GetGroupIdAsync(string internalId)
    {
        if (string.IsNullOrWhiteSpace(internalId))
        {
            throw new ArgumentException("InternalId cannot be null or empty", nameof(internalId));
        }

        // Check cache first
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(internalId, out var cachedGroupId))
            {
                // Move to end (most recently used)
                _accessOrder.Remove(internalId);
                _accessOrder.AddLast(internalId);
                _logger.LogDebug("Retrieved groupId from cache for internalId: {InternalId}", internalId);
                return cachedGroupId;
            }
        }

        // Cache miss - fetch from endpoint
        try
        {
            var requestUrl = $"http://{_signalEndpoint}/v1/groups/{_registered_account}";
            _logger.LogInformation("Fetching groupId from endpoint: {Endpoint} for internalId: {InternalId}", _signalEndpoint, internalId);

            var response = await _httpClient.GetAsync(requestUrl);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("Successfully retrieved groupId from endpoint for internalId: {InternalId}", internalId);

                if (!string.IsNullOrWhiteSpace(responseContent))
                {
                    var groups = JsonSerializer.Deserialize(responseContent, SignalGroupJsonContext.Default.ListGroupIdResponse);
                    
                    if (groups != null && groups.Count > 0)
                    {
                        // Find the group with matching internal_id
                        var matchingGroup = groups.FirstOrDefault(g => g.InternalId == internalId);
                        
                        if (matchingGroup != null && !string.IsNullOrWhiteSpace(matchingGroup.Id))
                        {
                            var groupId = matchingGroup.Id;
                            
                            // Add to cache
                            lock (_cacheLock)
                            {
                                AddToCache(internalId, groupId);
                            }
                            
                            return groupId;
                        }
                    }
                }

                throw new InvalidOperationException($"Invalid response format or empty groupId for internalId: {internalId}");
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Group endpoint returned error status {StatusCode}: {ErrorContent} for internalId: {InternalId}", 
                    response.StatusCode, errorContent, internalId);
                throw new HttpRequestException($"Group endpoint returned error status {response.StatusCode}: {errorContent}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while fetching groupId for internalId: {InternalId}", internalId);
            throw;
        }
    }

    private void AddToCache(string internalId, string groupId)
    {
        // If already exists, update and move to end
        if (_cache.ContainsKey(internalId))
        {
            _cache[internalId] = groupId;
            _accessOrder.Remove(internalId);
            _accessOrder.AddLast(internalId);
            return;
        }

        // If cache is at capacity, remove least recently used
        if (_cache.Count >= _maxCacheSize)
        {
            var lruKey = _accessOrder.First?.Value;
            if (lruKey != null)
            {
                _cache.Remove(lruKey);
                _accessOrder.RemoveFirst();
                _logger.LogDebug("Evicted LRU entry from cache: {InternalId}", lruKey);
            }
        }

        // Add new entry
        _cache[internalId] = groupId;
        _accessOrder.AddLast(internalId);
        _logger.LogDebug("Added to cache - internalId: {InternalId}, groupId: {GroupId}", internalId, groupId);
    }
}

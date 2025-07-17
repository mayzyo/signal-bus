using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json.Serialization;

namespace SignalBus.Models;

public class SignalMessage
{
    [JsonPropertyName("envelope")]
    public Envelope Envelope { get; set; } = new();

    [JsonPropertyName("account")]
    public string Account { get; set; } = string.Empty;
}

public class Envelope
{
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("sourceNumber")]
    public string SourceNumber { get; set; } = string.Empty;

    [JsonPropertyName("sourceUuid")]
    public string SourceUuid { get; set; } = string.Empty;

    [JsonPropertyName("sourceName")]
    public string SourceName { get; set; } = string.Empty;

    [JsonPropertyName("sourceDevice")]
    public int SourceDevice { get; set; }

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }

    [JsonPropertyName("serverReceivedTimestamp")]
    public long ServerReceivedTimestamp { get; set; }

    [JsonPropertyName("serverDeliveredTimestamp")]
    public long ServerDeliveredTimestamp { get; set; }

    [JsonPropertyName("syncMessage")]
    public SyncMessage? SyncMessage { get; set; }

    [JsonPropertyName("dataMessage")]
    public DataMessage? DataMessage { get; set; }
}

public class SyncMessage
{
    // Empty for now, but can be extended as needed
}

public class DataMessage
{
    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("expiresInSeconds")]
    public int ExpiresInSeconds { get; set; }

    [JsonPropertyName("viewOnce")]
    public bool ViewOnce { get; set; }
}

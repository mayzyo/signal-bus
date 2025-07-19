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
    public string? Message { get; set; }

    [JsonPropertyName("expiresInSeconds")]
    public int ExpiresInSeconds { get; set; }

    [JsonPropertyName("viewOnce")]
    public bool ViewOnce { get; set; }

    [JsonPropertyName("attachments")]
    public List<Attachment>? Attachments { get; set; }

    [JsonPropertyName("sticker")]
    public Sticker? Sticker { get; set; }

    [JsonPropertyName("mentions")]
    public List<Mention>? Mentions { get; set; }

    [JsonPropertyName("groupInfo")]
    public GroupInfo? GroupInfo { get; set; }
}

public class Attachment
{
    [JsonPropertyName("contentType")]
    public string ContentType { get; set; } = string.Empty;

    [JsonPropertyName("filename")]
    public string Filename { get; set; } = string.Empty;

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public int Size { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("caption")]
    public string? Caption { get; set; }

    [JsonPropertyName("uploadTimestamp")]
    public long UploadTimestamp { get; set; }
}

public class Mention
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("number")]
    public string Number { get; set; } = string.Empty;

    [JsonPropertyName("uuid")]
    public string Uuid { get; set; } = string.Empty;

    [JsonPropertyName("start")]
    public int Start { get; set; }

    [JsonPropertyName("length")]
    public int Length { get; set; }
}

public class Sticker
{
    [JsonPropertyName("packId")]
    public string PackId { get; set; } = string.Empty;

    [JsonPropertyName("stickerId")]
    public int StickerId { get; set; }
}

public class GroupInfo
{
    [JsonPropertyName("groupId")]
    public string GroupId { get; set; } = string.Empty;

    [JsonPropertyName("groupName")]
    public string GroupName { get; set; } = string.Empty;

    [JsonPropertyName("revision")]
    public int Revision { get; set; }

    [JsonPropertyName("Type")]
    public string type { get; set; } = string.Empty;
}

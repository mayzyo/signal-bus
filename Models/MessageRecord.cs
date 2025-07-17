using SignalBus.Services;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SignalBus.Models;

[Table("signal_messages")]
public class MessageRecord
{
    [Column("id")]
    public long Id { get; set; }

    [Column("timestamp")]
    public DateTime Timestamp { get; set; }

    [Column("signal_received_timestamp")]
    public DateTime SignalReceivedTimestamp { get; set; }

    [Column("signal_delivered_timestamp")]
    public DateTime? SignalDeliveredTimestamp { get; set; }

    [Column("target")]
    [MaxLength(255)]
    public string Target { get; set; } = string.Empty;

    [Column("source")]
    [MaxLength(255)]
    public string Source { get; set; } = string.Empty;

    [Column("content")]
    public string Content { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public static MessageRecord FromSignalMessage(SignalMessage signalMessage)
    {
        var record = new MessageRecord
        {
            Target = signalMessage.Account,
            Source = signalMessage.Envelope.Source,
            Content = signalMessage.Envelope.DataMessage.Message,
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(signalMessage.Envelope.DataMessage.Timestamp).UtcDateTime,
            SignalReceivedTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(signalMessage.Envelope.ServerReceivedTimestamp).UtcDateTime,
        };

        if (signalMessage.Envelope.ServerDeliveredTimestamp > 0)
        {
            record.SignalDeliveredTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(signalMessage.Envelope.ServerDeliveredTimestamp).UtcDateTime;
        }

        return record;
    }

    public static IEnumerable<MessageRecord> FromSignalSendRequest(SignalSendRequest signalSendRequest, long receivedTimestamp)
    {
        var currentTimestamp = DateTime.UtcNow;

        var records = signalSendRequest.Recipients.Select(recipient => new MessageRecord
        {
            Target = recipient,
            Source = signalSendRequest.Number,
            SignalReceivedTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(receivedTimestamp).UtcDateTime,
            Content = signalSendRequest.Message,
            Timestamp = currentTimestamp
        });

        return records;
    }
}

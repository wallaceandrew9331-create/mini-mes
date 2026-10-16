using System.Text.Json;

namespace MiniMes.Api.Events;

/// <summary>
/// Transactional outbox. Station events are inserted in the same transaction as the state change that
/// caused them, then <see cref="OutboxRelay"/> publishes them to Kafka. No dual-write: an event exists
/// if and only if the change committed. Delivery is at-least-once, so consumers dedupe on <see cref="Id"/>.
/// </summary>
public class OutboxMessage
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public long Id { get; private set; }
    public string Type { get; private set; } = null!;

    /// <summary>Kafka message key. Events for one serial share a partition and so stay ordered.</summary>
    public string Key { get; private set; } = null!;

    public string Payload { get; private set; } = null!;
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset? PublishedAt { get; set; }

    public static OutboxMessage Create(string type, string key, object payload, DateTimeOffset now) => new()
    {
        Type = type,
        Key = key,
        Payload = JsonSerializer.Serialize(payload, Json),
        OccurredAt = now,
    };
}

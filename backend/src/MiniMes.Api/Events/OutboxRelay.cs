using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MiniMes.Api.Data;

namespace MiniMes.Api.Events;

public class KafkaOptions
{
    /// <summary>Unset disables publishing; events still accumulate in the outbox table and at /api/events.</summary>
    public string? BootstrapServers { get; set; }
    public string Topic { get; set; } = "mes.station-events";
}

/// <summary>
/// Polls the outbox and publishes unpublished rows to Kafka in id order. Marks a batch published only after
/// Kafka acknowledged it, so a crash re-sends rather than loses events (at-least-once).
/// Assumes a single API instance; with replicas the batch select would need FOR UPDATE SKIP LOCKED.
/// </summary>
public class OutboxRelay(IServiceScopeFactory scopes, IOptions<KafkaOptions> options, ILogger<OutboxRelay> log) : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        var opts = options.Value;
        if (string.IsNullOrWhiteSpace(opts.BootstrapServers))
        {
            log.LogInformation("Kafka not configured; station events stay in the outbox table");
            return;
        }

        using var producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = opts.BootstrapServers,
            EnableIdempotence = true,
            Acks = Acks.All,
        }).Build();
        log.LogInformation("Publishing station events to {Topic} on {Servers}", opts.Topic, opts.BootstrapServers);

        while (!stop.IsCancellationRequested)
        {
            try
            {
                if (await PublishBatchAsync(producer, opts.Topic, stop) == 0)
                    await Task.Delay(TimeSpan.FromSeconds(1), stop);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Outbox relay failed; retrying in 5s");
                await Task.Delay(TimeSpan.FromSeconds(5), stop);
            }
        }
    }

    private async Task<int> PublishBatchAsync(IProducer<string, string> producer, string topic, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var batch = await db.Outbox.Where(m => m.PublishedAt == null).OrderBy(m => m.Id).Take(100).ToListAsync(ct);

        foreach (var m in batch)
        {
            using var data = JsonDocument.Parse(m.Payload);
            var envelope = JsonSerializer.Serialize(new { m.Id, m.Type, m.OccurredAt, Data = data.RootElement }, Json);
            await producer.ProduceAsync(topic, new Message<string, string>
            {
                Key = m.Key,
                Value = envelope,
                Headers = new Headers { { "type", Encoding.UTF8.GetBytes(m.Type) } },
            }, ct);
            m.PublishedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        return batch.Count;
    }
}

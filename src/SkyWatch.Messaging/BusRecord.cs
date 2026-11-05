namespace SkyWatch.Messaging;

/// <summary>A broker-neutral record. Payload bytes are identical for Kafka and K0sStreams.</summary>
public sealed record BusRecord(
    string Id,
    string Key,
    byte[] Payload,
    DateTimeOffset Timestamp,
    IReadOnlyDictionary<string, string>? Headers = null);

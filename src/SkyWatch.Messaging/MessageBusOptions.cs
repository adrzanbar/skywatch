namespace SkyWatch.Messaging;

public enum MessageBusMode
{
    InMemory,
    Kafka,
    K0sStreams
}

public sealed class MessageBusOptions
{
    public string Mode { get; set; } = nameof(MessageBusMode.InMemory);

    public KafkaBusOptions Kafka { get; set; } = new();

    public K0sStreamsBusOptions K0sStreams { get; set; } = new();

    public MessageBusMode ResolveMode() =>
        Enum.TryParse<MessageBusMode>(Mode, ignoreCase: true, out var mode)
            ? mode
            : throw new InvalidOperationException(
                $"Unknown Messaging:Mode '{Mode}'. Use InMemory, Kafka or K0sStreams.");
}

public sealed class KafkaBusOptions
{
    public string BootstrapServers { get; set; } = "localhost:9092";

    public string ClientId { get; set; } = "skywatch";

    public int MaxAttempts { get; set; } = 5;

    public int RetryDelayMs { get; set; } = 1_000;
}

public sealed class K0sStreamsBusOptions
{
    public string BaseUrl { get; set; } = "http://localhost:9090";

    public int VisibilitySeconds { get; set; } = 60;

    public int HeartbeatSeconds { get; set; } = 10;

    public int PollIntervalMs { get; set; } = 200;

    public int HttpTimeoutSeconds { get; set; } = 15;

    /// <summary>Attempts per publish. K0sStreams can answer 503 while the quorum catches up.</summary>
    public int PublishAttempts { get; set; } = 3;

    public int PublishRetryBackoffMs { get; set; } = 500;
}

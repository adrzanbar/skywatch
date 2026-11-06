namespace SkyWatch.Producer;

public sealed class ProducerOptions
{
    /// <summary>Parallel publishes per snapshot. K0sStreams needs a lower value than Kafka.</summary>
    public int MaxDegreeOfParallelism { get; set; } = 32;
}

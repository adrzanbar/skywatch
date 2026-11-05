using Xunit;
using SkyWatch.Analytics;

namespace SkyWatch.Analytics.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void Analytics_does_not_depend_on_broker_adapters()
    {
        string[] references = typeof(AircraftTracker).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain("Confluent.Kafka", references);
        Assert.DoesNotContain("SkyWatch.Messaging.Kafka", references);
        Assert.DoesNotContain("SkyWatch.Messaging.K0sStreams", references);
    }
}

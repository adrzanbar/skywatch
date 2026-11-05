namespace SkyWatch.Messaging;

/// <summary>A record handed to a consumer, with transport metadata hidden behind simple values.</summary>
public sealed record BusDelivery(
    string Topic,
    int Partition,
    long Offset,
    BusRecord Record,
    int Attempt);

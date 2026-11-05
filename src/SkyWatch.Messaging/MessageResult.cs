namespace SkyWatch.Messaging;

/// <summary>
/// Transient retries belong inside the handler. The bus only needs to know whether
/// the record was handled or should be moved to the dead-letter topic.
/// </summary>
public enum MessageResult
{
    Ack,
    DeadLetter
}

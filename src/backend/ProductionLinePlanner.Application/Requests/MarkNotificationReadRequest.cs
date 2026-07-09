namespace ProductionLinePlanner.Application.Requests;

public sealed class MarkNotificationReadRequest
{
    public DateTime? ReadAtUtc { get; init; }
}

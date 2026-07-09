namespace ProductionLinePlanner.Application.Abstractions;

public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
}

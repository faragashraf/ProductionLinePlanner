namespace ProductionLinePlanner.Application.Services;

/// <summary>
/// Provides the configured Egypt/Cairo time zone for operational date boundaries.
/// </summary>
public interface ICairoTimeZoneProvider
{
    TimeZoneInfo TimeZone { get; }
}

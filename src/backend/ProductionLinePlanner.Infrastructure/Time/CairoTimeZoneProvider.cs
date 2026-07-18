using System.Runtime.InteropServices;
using Microsoft.Extensions.Configuration;
using ProductionLinePlanner.Application.Services;

namespace ProductionLinePlanner.Infrastructure.Time;

/// <summary>
/// Resolves Cairo time from configuration while supporting Windows and IANA identifiers.
/// </summary>
public sealed class CairoTimeZoneProvider : ICairoTimeZoneProvider
{
    private const string WindowsCairoId = "Egypt Standard Time";
    private const string IanaCairoId = "Africa/Cairo";

    public CairoTimeZoneProvider(IConfiguration configuration)
    {
        TimeZone = Resolve(configuration["TimeZones:Cairo"]);
    }

    public TimeZoneInfo TimeZone { get; }

    private static TimeZoneInfo Resolve(string? configuredId)
    {
        var preferredId = OperatingSystem.IsWindows() ? WindowsCairoId : IanaCairoId;
        var alternateId = OperatingSystem.IsWindows() ? IanaCairoId : WindowsCairoId;
        var candidateIds = new List<string>();

        AddCandidate(configuredId);
        AddCandidate(preferredId);
        AddCandidate(alternateId);

        foreach (var candidateId in candidateIds)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(candidateId);
            }
            catch (TimeZoneNotFoundException)
            {
                // Try the platform-compatible alternate ID.
            }
            catch (InvalidTimeZoneException)
            {
                // A malformed local time-zone definition is treated like an unavailable ID.
            }
        }

        throw new InvalidOperationException(
            $"Unable to resolve Cairo time zone. Tried IDs: {string.Join(", ", candidateIds)}. " +
            $"Operating system: {RuntimeInformation.OSDescription}. Configure 'TimeZones:Cairo' with a valid system time-zone ID.");

        void AddCandidate(string? candidateId)
        {
            var normalized = candidateId?.Trim();
            if (!string.IsNullOrWhiteSpace(normalized) && !candidateIds.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                candidateIds.Add(normalized);
            }
        }
    }
}

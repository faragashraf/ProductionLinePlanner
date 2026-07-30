using System.Text.Json;
using ProductionLinePlanner.Domain.Entities;

namespace ProductionLinePlanner.Infrastructure.Attendance.Services;

internal static class AttendancePunchEvidenceMatcher
{
    public static bool IsExact(
        AttendanceRecord record,
        Guid workerId,
        string sourceName,
        DateTime expectedAttendanceTimeUtc,
        bool isCheckIn,
        string? sourceRawId)
    {
        if (record.WorkerId != workerId ||
            !string.Equals(record.Source, sourceName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var window = ReadWindow(record.SourcePayload);
        return isCheckIn
            ? window.FirstInUtc == expectedAttendanceTimeUtc
              && string.Equals(record.SourceRawId, sourceRawId, StringComparison.Ordinal)
            : window.LastOutUtc == expectedAttendanceTimeUtc;
    }

    public static AttendanceEvidenceWindow ReadWindow(string? sourcePayload)
    {
        if (string.IsNullOrWhiteSpace(sourcePayload)) return new(null, null);
        try
        {
            using var json = JsonDocument.Parse(sourcePayload);
            DateTime? first = json.RootElement.TryGetProperty("FirstInUtc", out var firstValue) && firstValue.TryGetDateTime(out var parsedFirst)
                ? DateTime.SpecifyKind(parsedFirst, DateTimeKind.Utc)
                : null;
            DateTime? last = json.RootElement.TryGetProperty("LastOutUtc", out var lastValue) && lastValue.ValueKind != JsonValueKind.Null && lastValue.TryGetDateTime(out var parsedLast)
                ? DateTime.SpecifyKind(parsedLast, DateTimeKind.Utc)
                : null;
            return new(first, last);
        }
        catch (JsonException)
        {
            return new(null, null);
        }
    }
}

internal readonly record struct AttendanceEvidenceWindow(DateTime? FirstInUtc, DateTime? LastOutUtc);

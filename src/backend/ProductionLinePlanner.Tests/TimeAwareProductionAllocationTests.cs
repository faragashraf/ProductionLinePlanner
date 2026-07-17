using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Tests;

public sealed class TimeAwareProductionAllocationTests
{
    private static readonly DateTime Day = new(2026, 7, 17, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Sequential_08_to_13_and_13_to_16_assignments_keep_their_actual_minutes()
    {
        var first = Contribution(8, 13, 8, 16);
        var second = Contribution(13, 16, 8, 16);

        Assert.Equal(300, first.WorkerMinutes);
        Assert.Equal(180, second.WorkerMinutes);
        Assert.Equal(Day.AddHours(13), first.ContributionEndsAtUtc);
        Assert.Equal(Day.AddHours(13), second.ContributionStartsAtUtc);
    }

    [Fact]
    public void Late_arrival_and_early_departure_clip_the_assignment_window()
    {
        var late = Contribution(8, 16, 9, 16);
        var early = Contribution(8, 16, 8, 14);

        Assert.Equal(420, late.WorkerMinutes);
        Assert.Equal(Day.AddHours(9), late.ContributionStartsAtUtc);
        Assert.Equal(360, early.WorkerMinutes);
        Assert.Equal(Day.AddHours(14), early.ContributionEndsAtUtc);
    }

    [Fact]
    public void No_overlap_is_excluded_from_production()
    {
        var result = Contribution(8, 13, 13, 16);

        Assert.False(result.IsProductionReady);
        Assert.Equal(0, result.WorkerMinutes);
        Assert.Equal("NoTemporalIntersection", result.ExclusionReason);
    }

    [Fact]
    public void Simultaneous_workers_allocate_shared_percentage_by_minutes()
    {
        var longWorker = Contribution(Guid.Parse("00000000-0000-0000-0000-000000000001"), 8, 16, 8, 16);
        var shortWorker = Contribution(Guid.Parse("00000000-0000-0000-0000-000000000002"), 8, 16, 12, 16);

        var shares = TimeAwareProductionAllocation.AllocateByMinutes(500m, [longWorker, shortWorker]).OrderBy(share => share.WorkerId).ToArray();

        Assert.Equal(480, shares[0].WorkerMinutes);
        Assert.Equal(240, shares[1].WorkerMinutes);
        Assert.Equal(66.6667m, shares[0].Percentage);
        Assert.Equal(33.3333m, shares[1].Percentage);
        Assert.Equal(500m, shares.Sum(share => share.Quantity));
    }

    [Fact]
    public void Quantity_rounding_is_deterministic_and_never_inflates_stage_output()
    {
        var workers = Enumerable.Range(1, 3)
            .Select(index => Contribution(new Guid(index, 0, 0, new byte[8]), 8, 16, 8, 16))
            .ToArray();

        var shares = TimeAwareProductionAllocation.AllocateByMinutes(1m, workers).OrderBy(share => share.WorkerId).ToArray();

        Assert.Equal([0.334m, 0.333m, 0.333m], shares.Select(share => share.Quantity).ToArray());
        Assert.Equal(1m, shares.Sum(share => share.Quantity));
        Assert.Equal(100m, shares.Sum(share => share.Percentage));
    }

    private static WorkerContributionResult Contribution(int assignmentStart, int assignmentEnd, int attendanceStart, int attendanceEnd) =>
        Contribution(Guid.NewGuid(), assignmentStart, assignmentEnd, attendanceStart, attendanceEnd);

    private static WorkerContributionResult Contribution(Guid workerId, int assignmentStart, int assignmentEnd, int attendanceStart, int attendanceEnd) =>
        TimeAwareProductionAllocation.CalculateContribution(
            workerId,
            [new UtcTimeWindow(Day.AddHours(assignmentStart), Day.AddHours(assignmentEnd))],
            new AttendancePresenceWindowDto(workerId, AttendanceStatus.Present, Day.AddHours(attendanceStart), Day.AddHours(attendanceEnd), true));
}

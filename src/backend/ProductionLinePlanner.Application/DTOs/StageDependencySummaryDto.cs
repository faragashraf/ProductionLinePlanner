namespace ProductionLinePlanner.Application.DTOs;

public sealed class StageDependencySummaryDto
{
    public Guid StageId { get; init; }
    public IReadOnlyList<StageDependencyItemDto> ActiveBlockers { get; init; } = [];
    public IReadOnlyList<StageDependencyItemDto> HistoricalDependencies { get; init; } = [];
    public bool CanDisable => ActiveBlockers.Count == 0;
    public bool CanDelete => ActiveBlockers.Count == 0 && HistoricalDependencies.Count == 0;
    public string DisableMessageAr { get; init; } = string.Empty;
    public string DeleteMessageAr { get; init; } = string.Empty;
}

public sealed class StageDependencyItemDto
{
    public string Key { get; init; } = string.Empty;
    public string LabelAr { get; init; } = string.Empty;
    public int Count { get; init; }
}

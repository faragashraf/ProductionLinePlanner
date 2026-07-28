namespace ProductionLinePlanner.Application.DTOs;

public sealed class CopyProductModelStagesSummaryDto
{
    public Guid SourceFactoryId { get; init; }
    public Guid SourceDepartmentId { get; init; }
    public Guid SourceProductionLineId { get; init; }
    public Guid SourceProductModelId { get; init; }
    public Guid TargetFactoryId { get; init; }
    public Guid TargetDepartmentId { get; init; }
    public Guid TargetProductionLineId { get; init; }
    public Guid TargetProductModelId { get; init; }
    public bool IsPreview { get; init; }
    public int RequestedCount { get; init; }
    public int AddedCount { get; init; }
    public int SkippedCount { get; init; }
    public int FailedCount { get; init; }
    public Guid[] AddedStageIds { get; init; } = [];
    public CopyProductModelStagePlanDto[] PlannedStages { get; init; } = [];
    public CopyProductModelStageSkipDto[] SkippedStages { get; init; } = [];
    public CopyProductModelStageFailureDto[] FailedStages { get; init; } = [];
    public string[] ValidationErrors { get; init; } = [];
}

public sealed class CopyProductModelStagePlanDto
{
    public Guid SourceProductModelStageId { get; init; }
    public Guid SubStageId { get; init; }
    public Guid DepartmentId { get; init; }
    public Guid ProductionLineId { get; init; }
    public string SubStageCode { get; init; } = string.Empty;
    public string SubStageName { get; init; } = string.Empty;
    public int StageOrder { get; init; }
    public int TargetStageOrder { get; init; }
    public bool CreatesTargetStage { get; init; }
    public string StatusLabel { get; init; } = string.Empty;
}

public sealed class CopyProductModelStageSkipDto
{
    public Guid SourceProductModelStageId { get; init; }
    public Guid SubStageId { get; init; }
    public string StageCode { get; init; } = string.Empty;
    public string StageName { get; init; } = string.Empty;
    public string ReasonCode { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}

public sealed class CopyProductModelStageFailureDto
{
    public Guid SourceProductModelStageId { get; init; }
    public Guid SubStageId { get; init; }
    public string StageCode { get; init; } = string.Empty;
    public string StageName { get; init; } = string.Empty;
    public string ReasonCode { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}

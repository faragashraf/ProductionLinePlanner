namespace ProductionLinePlanner.Application.DTOs;

public sealed class AssignmentRecommendationCandidateDto
{
    public Guid WorkerId { get; init; }
    public string WorkerName { get; init; } = string.Empty;
    public decimal Score { get; init; }
    public string[] Reasons { get; init; } = [];
    public string[] RiskWarnings { get; init; } = [];
}

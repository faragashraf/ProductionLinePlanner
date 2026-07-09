namespace ProductionLinePlanner.Application.DTOs;

public sealed class AssignmentRecommendationResultDto
{
    public Guid SubStageId { get; init; }
    public DateTime AsOfUtc { get; init; }
    public int TopCandidates { get; init; }
    public AssignmentRecommendationCandidateDto[] Candidates { get; init; } = [];
}

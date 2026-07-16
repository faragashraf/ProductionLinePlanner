namespace ProductionLinePlanner.Domain.Enums;

/// <summary>
/// Describes whether a temporary assignment adds a participation or temporarily
/// moves one specific participation away from its source stage.
/// </summary>
public enum TemporaryAssignmentMode
{
    TemporaryMove = 0,
    AdditionalParticipation = 1
}

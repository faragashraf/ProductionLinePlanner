namespace ProductionLinePlanner.Application.Requests;

public sealed class AssignWorkerDepartmentRequest
{
    public Guid DepartmentId { get; init; }
    public Guid ConcurrencyToken { get; init; }
}

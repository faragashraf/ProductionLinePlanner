using System;

namespace ProductionLinePlanner.Application.Requests;

public sealed class MoveWorkerToDepartmentRequest
{
    public Guid WorkerId { get; init; }
}

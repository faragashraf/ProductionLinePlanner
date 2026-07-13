using ProductionLinePlanner.Application.Requests;

namespace ProductionLinePlanner.Application.Abstractions;

public interface IIamAuthorizationService
{
    Task<IamAuthorizationUpdateResult> ReplaceAsync(Guid actorUserId, Guid targetUserId, UserAuthorizationUpdateRequest request, string? requestMeta, CancellationToken cancellationToken = default);
}

public sealed record IamAuthorizationUpdateResult(bool Succeeded, int StatusCode, string Code, string Message, Guid[] RoleIds, string[] DirectGrants, string[] DirectDenies);

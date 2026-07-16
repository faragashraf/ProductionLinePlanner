using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Requests;
using ProductionLinePlanner.Domain.Entities;

namespace ProductionLinePlanner.Application.Abstractions;

public interface IUserManagementService
{
    Task<UserManagementResult> CreateAsync(Guid actorUserId, AdminUserCreateRequest request, string? requestMeta, CancellationToken cancellationToken = default);
    Task<UserManagementResult> UpdateAsync(Guid actorUserId, Guid userId, AdminUserUpdateRequest request, string? requestMeta, CancellationToken cancellationToken = default);
}

public interface IUserPasswordHasher
{
    string HashPassword(AppUser user, string password);
}

public sealed record UserManagementResult(
    bool Succeeded,
    int StatusCode,
    string Code,
    string Message,
    AdminUserDetailsDto? User)
{
    public static UserManagementResult Success(AdminUserDetailsDto user, int statusCode = 200) =>
        new(true, statusCode, string.Empty, string.Empty, user);

    public static UserManagementResult Failure(int statusCode, string code, string message) =>
        new(false, statusCode, code, message, null);
}

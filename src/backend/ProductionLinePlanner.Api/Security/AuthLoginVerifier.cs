using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Api.Security;

public static class AuthLoginVerifier
{
    public static async Task<AppUser?> VerifyAsync(
        AppDbContext dbContext,
        IPasswordHasher<AppUser> passwordHasher,
        string loginIdentifier,
        string password,
        CancellationToken cancellationToken = default)
    {
        var normalizedIdentifier = AppUser.NormalizeLoginIdentifier(loginIdentifier);
        var user = await dbContext.AppUsers
            .Include(candidate => candidate.Roles)
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.IsActive && candidate.Email.ToLower() == normalizedIdentifier, cancellationToken);
        if (user is null) return null;

        var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        return verification is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded
            ? user
            : null;
    }
}

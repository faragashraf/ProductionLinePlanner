using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Tests;

public sealed class AuthTokenServiceTests
{
    [Fact]
    public void Renamed_system_roles_keep_their_enum_claims()
    {
        var user = CreateUser();
        user.AssignRole(new AppRole(Guid.NewGuid(), UserRole.Admin, "Operations", isSystemRole: true));
        user.AssignRole(new AppRole(Guid.NewGuid(), UserRole.SuperAdmin, "Platform Owners", isSystemRole: true));

        var claims = GetRoleClaims(user);

        Assert.Contains("Admin", claims);
        Assert.Contains("SuperAdmin", claims);
        Assert.DoesNotContain("Operations", claims);
        Assert.DoesNotContain("Platform Owners", claims);
    }

    [Fact]
    public void Custom_roles_use_their_name_and_mixed_claims_are_unique()
    {
        var user = CreateUser();
        user.AssignRole(new AppRole(Guid.NewGuid(), UserRole.Admin, "Operations", isSystemRole: true));
        user.AssignRole(new AppRole(Guid.NewGuid(), "Shift Lead"));
        user.AssignRole(new AppRole(Guid.NewGuid(), "shift lead"));

        var claims = GetRoleClaims(user);

        Assert.Contains("Admin", claims);
        Assert.Contains("Shift Lead", claims);
        Assert.Equal(2, claims.Count);
    }

    [Fact]
    public void Invalid_custom_role_name_does_not_create_an_empty_claim()
    {
        var user = CreateUser();
        var role = new AppRole(Guid.NewGuid(), "Temporary");
        typeof(AppRole).GetProperty(nameof(AppRole.Name), BindingFlags.Instance | BindingFlags.Public)!.SetValue(role, " ");
        user.AssignRole(role);

        Assert.Empty(GetRoleClaims(user));
    }

    private static AppUser CreateUser() =>
        new(Guid.NewGuid(), "Test User", "test@example.com", "hash");

    private static IReadOnlyList<string> GetRoleClaims(AppUser user)
    {
        var token = AuthTokenService.CreateAccessToken(
            user,
            DateTime.UtcNow,
            DateTime.UtcNow.AddMinutes(5),
            "issuer",
            "audience",
            new SymmetricSecurityKey(new byte[32]));

        return new JwtSecurityTokenHandler()
            .ReadJwtToken(token)
            .Claims
            .Where(claim => claim.Type is ClaimTypes.Role or "role")
            .Select(claim => claim.Value)
            .ToArray();
    }
}

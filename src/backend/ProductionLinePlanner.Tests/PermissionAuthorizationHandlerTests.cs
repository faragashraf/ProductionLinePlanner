using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Api.Authorization;

namespace ProductionLinePlanner.Tests;

public sealed class PermissionAuthorizationHandlerTests
{
    [Fact]
    public async Task Authenticated_user_with_permission_succeeds()
    {
        var context = await AuthorizeAsync(authenticated: true, permissions: ["factory-structure.view"]);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Authenticated_user_without_permission_is_forbidden()
    {
        var context = await AuthorizeAsync(authenticated: true, permissions: []);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Unauthenticated_user_is_not_authorized()
    {
        var context = await AuthorizeAsync(authenticated: false, permissions: ["factory-structure.view"]);

        Assert.False(context.HasSucceeded);
    }

    private static async Task<AuthorizationHandlerContext> AuthorizeAsync(bool authenticated, IReadOnlyCollection<string> permissions)
    {
        var identity = new ClaimsIdentity(
            authenticated ? [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())] : [],
            authenticated ? "test" : null);
        var context = new AuthorizationHandlerContext(
            [new PermissionRequirement("factory-structure.view")],
            new ClaimsPrincipal(identity),
            resource: null);
        var handler = new PermissionAuthorizationHandler(
            new TestCurrentUser(authenticated),
            new TestPermissionService(permissions));

        await handler.HandleAsync(context);
        return context;
    }

    private sealed class TestCurrentUser(bool authenticated) : ICurrentUserService
    {
        public Guid? UserId { get; } = authenticated ? Guid.NewGuid() : null;
        public string? UserName => null;
        public bool IsAuthenticated => authenticated;
        public IReadOnlyCollection<string> Roles => [];
    }

    private sealed class TestPermissionService(IReadOnlyCollection<string> permissions) : IPermissionService
    {
        public Task<IReadOnlyCollection<string>> GetEffectivePermissionsAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(permissions);

        public Task<ProductionLinePlanner.Application.DTOs.PermissionCatalogItemDto[]> GetCatalogAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Array.Empty<ProductionLinePlanner.Application.DTOs.PermissionCatalogItemDto>());
    }
}

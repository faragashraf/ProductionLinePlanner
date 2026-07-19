using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProductionLinePlanner.Api.Authorization;
using ProductionLinePlanner.Api.Endpoints;
using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.Notifications;
using ProductionLinePlanner.Domain.Authorization;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Domain.Notifications;
using ProductionLinePlanner.Infrastructure.Authorization;
using ProductionLinePlanner.Infrastructure.BusinessEngines;
using ProductionLinePlanner.Infrastructure.Data;
using ProductionLinePlanner.Infrastructure.Notifications;

namespace ProductionLinePlanner.Tests;

public sealed class NotificationPolicyPlatformFoundationTests
{
    private readonly NotificationTemplateResolver _templateResolver = new();
    private readonly CodeNotificationEventCatalog _eventCatalog = new();

    [Fact]
    public void Template_parser_extracts_distinct_tokens_and_resolves_known_values()
    {
        var parsed = _templateResolver.ParseTokens("{WorkerName} moved by {ActorName}. {WorkerName} is ready.");
        var resolved = _templateResolver.Resolve(
            "{WorkerName} moved by {ActorName}.",
            ["WorkerName", "ActorName"],
            new Dictionary<string, string>
            {
                ["WorkerName"] = "Mona",
                ["ActorName"] = "Supervisor"
            });

        Assert.True(parsed.IsSuccess);
        Assert.Equal(["WorkerName", "ActorName"], parsed.Value);
        Assert.True(resolved.IsSuccess);
        Assert.Equal("Mona moved by Supervisor.", resolved.Value);
    }

    [Fact]
    public void Template_resolver_rejects_unknown_and_malformed_tokens()
    {
        var unknown = _templateResolver.Resolve(
            "{WorkerName} at {UnknownPlace}",
            ["WorkerName"],
            new Dictionary<string, string> { ["WorkerName"] = "Mona", ["UnknownPlace"] = "Hidden" });
        var malformed = _templateResolver.ParseTokens("{Worker Name}");

        Assert.True(unknown.IsFailure);
        Assert.Equal("UnknownTemplateToken", unknown.Error!.Code);
        Assert.True(malformed.IsFailure);
        Assert.Equal("MalformedTemplate", malformed.Error!.Code);
    }

    [Fact]
    public async Task Recipient_resolver_supports_user_role_creator_and_exclude_actor_rules()
    {
        await using var fixture = await RecipientFixture.CreateAsync();

        var result = await fixture.Resolver.ResolveAsync(
            [
                new(NotificationRecipientKind.User, SubjectId: fixture.Actor.Id),
                new(NotificationRecipientKind.Role, SubjectId: fixture.TargetRole.Id),
                new(NotificationRecipientKind.Creator),
                new(NotificationRecipientKind.ExcludeActor)
            ],
            new NotificationRecipientContext(fixture.Actor.Id, fixture.Creator.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(
            new[] { fixture.RoleRecipient.Id, fixture.Creator.Id }.OrderBy(id => id),
            result.Value);
    }

    [Fact]
    public async Task Recipient_resolver_uses_effective_permissions_including_denies()
    {
        await using var fixture = await RecipientFixture.CreateAsync();

        var result = await fixture.Resolver.ResolveAsync(
            [new(NotificationRecipientKind.Permission, Value: "attendance.view")],
            new NotificationRecipientContext());

        Assert.True(result.IsSuccess);
        Assert.Equal([fixture.PermissionRecipient.Id], result.Value);
        Assert.DoesNotContain(fixture.DeniedPermissionRecipient.Id, result.Value!);
    }

    [Fact]
    public async Task Recipient_resolver_supports_capability_groups()
    {
        await using var fixture = await RecipientFixture.CreateAsync();

        var result = await fixture.Resolver.ResolveAsync(
            [new(NotificationRecipientKind.CapabilityGroup, Value: "workers")],
            new NotificationRecipientContext());

        Assert.True(result.IsSuccess);
        Assert.Equal([fixture.CapabilityRecipient.Id], result.Value);
    }

    [Fact]
    public async Task Disabled_event_short_circuits_without_resolving_recipients()
    {
        var recipients = new RecordingRecipientResolver([Guid.NewGuid()]);
        var engine = CreateEngine(recipients);
        var policy = CreatePolicy(isEnabled: false);

        var result = await engine.EvaluateAsync(policy, CreateContext());

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsEnabled);
        Assert.False(result.Value.ShouldDispatch);
        Assert.Equal(0, recipients.Calls);
    }

    [Theory]
    [InlineData(NotificationSeverity.Information)]
    [InlineData(NotificationSeverity.Success)]
    [InlineData(NotificationSeverity.Warning)]
    [InlineData(NotificationSeverity.Critical)]
    public async Task Enabled_event_preserves_supported_severity(NotificationSeverity severity)
    {
        var engine = CreateEngine(new RecordingRecipientResolver([Guid.NewGuid()]));

        var result = await engine.EvaluateAsync(
            CreatePolicy(isEnabled: true, severity: severity),
            CreateContext());

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.ShouldDispatch);
        Assert.Equal(severity, result.Value.Severity);
        Assert.Equal("Mona assignment changed", result.Value.Title);
        Assert.Contains("Line 1", result.Value.Message);
    }

    [Fact]
    public async Task Invalid_severity_is_rejected()
    {
        var engine = CreateEngine(new RecordingRecipientResolver([Guid.NewGuid()]));

        var result = await engine.EvaluateAsync(
            CreatePolicy(isEnabled: true, severity: (NotificationSeverity)999),
            CreateContext());

        Assert.True(result.IsFailure);
        Assert.Equal("InvalidNotificationSeverity", result.Error!.Code);
    }

    [Fact]
    public async Task Sound_and_inbox_policies_are_independent()
    {
        var engine = CreateEngine(new RecordingRecipientResolver([Guid.NewGuid()]));

        var result = await engine.EvaluateAsync(
            CreatePolicy(isEnabled: true, soundEnabled: true, inboxEnabled: false),
            CreateContext());

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Sound.Enabled);
        Assert.False(result.Value.Inbox.Enabled);
        Assert.True(result.Value.Toast.Enabled);
        Assert.True(result.Value.ShouldDispatch);
    }

    [Fact]
    public void Admin_foundation_exposes_static_events_and_single_sound_constraints()
    {
        var foundation = CreateAdminFoundationService().GetFoundation();

        Assert.Equal(5, foundation.Events.Count);
        Assert.Contains(foundation.Events, item => item.EventKey == NotificationEventKeys.WorkerCreated);
        Assert.Equal(Enum.GetNames<NotificationSeverity>(), foundation.Severities);
        Assert.False(foundation.CanCreateEvents);
        Assert.True(foundation.IsPersistenceAvailable);
        Assert.Equal("default", foundation.Sound.SoundKey);
        Assert.False(foundation.Sound.SupportsMultipleSounds);
        Assert.False(foundation.Sound.SupportsVolume);
        Assert.False(foundation.Sound.SupportsUserPreferences);
    }

    [Fact]
    public async Task Admin_foundation_endpoint_requires_notification_policy_permission()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<INotificationPolicyAdminService>(
            CreateAdminFoundationService());
        builder.Services.AddScoped<ICurrentUserService>(_ => null!);
        await using var app = builder.Build();
        app.MapNotificationPolicyAdminEndpoints();

        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(candidate => candidate.RoutePattern.RawText?.StartsWith("/api/admin/notification-policies", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.True(PermissionCatalog.IsKnown(NotificationPolicyPermissions.Manage));
        Assert.NotEmpty(endpoints);
        Assert.All(endpoints, endpoint => Assert.Contains(
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
            authorization => authorization.Policy == PermissionAuthorizationExtensions.PolicyName(NotificationPolicyPermissions.Manage)));
        Assert.DoesNotContain(endpoints, endpoint => endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Any(method => method is "POST" or "DELETE") == true);
    }

    private NotificationPolicyEngine CreateEngine(INotificationRecipientResolver recipientResolver) =>
        new(_eventCatalog, _templateResolver, recipientResolver);

    private static NotificationPolicyAdminService CreateAdminFoundationService() =>
        new(null!, new CodeNotificationEventCatalog(), new NotificationTemplateResolver(), null!, null!);

    private static NotificationPolicyDefinition CreatePolicy(
        bool isEnabled,
        NotificationSeverity severity = NotificationSeverity.Warning,
        bool soundEnabled = false,
        bool inboxEnabled = true) =>
        new(
            NotificationEventKeys.AssignmentChanged,
            isEnabled,
            severity,
            new NotificationSoundPolicy(soundEnabled),
            new NotificationToastPolicy(Enabled: true),
            new NotificationInboxPolicy(inboxEnabled),
            "{WorkerName} assignment changed",
            "{WorkerName} moved to {LineName} at {FactoryName} by {ActorName}.",
            [new NotificationRecipientRule(NotificationRecipientKind.Creator)]);

    private static NotificationEventContext CreateContext() => new(
        NotificationEventKeys.AssignmentChanged,
        new Dictionary<string, string>
        {
            ["WorkerName"] = "Mona",
            ["ActorName"] = "Supervisor",
            ["LineName"] = "Line 1",
            ["FactoryName"] = "Factory A"
        },
        ActorUserId: Guid.NewGuid(),
        CreatorUserId: Guid.NewGuid());

    private sealed class RecordingRecipientResolver(IReadOnlyCollection<Guid> recipients) : INotificationRecipientResolver
    {
        public int Calls { get; private set; }

        public Task<Result<IReadOnlyCollection<Guid>>> ResolveAsync(
            IReadOnlyCollection<NotificationRecipientRule> rules,
            NotificationRecipientContext context,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(Result<IReadOnlyCollection<Guid>>.Success(recipients));
        }
    }

    private sealed class RecipientFixture : IAsyncDisposable
    {
        private RecipientFixture(
            AppDbContext dbContext,
            NotificationRecipientResolver resolver,
            AppUser actor,
            AppUser creator,
            AppUser roleRecipient,
            AppUser permissionRecipient,
            AppUser deniedPermissionRecipient,
            AppUser capabilityRecipient,
            AppRole targetRole)
        {
            DbContext = dbContext;
            Resolver = resolver;
            Actor = actor;
            Creator = creator;
            RoleRecipient = roleRecipient;
            PermissionRecipient = permissionRecipient;
            DeniedPermissionRecipient = deniedPermissionRecipient;
            CapabilityRecipient = capabilityRecipient;
            TargetRole = targetRole;
        }

        public AppDbContext DbContext { get; }
        public NotificationRecipientResolver Resolver { get; }
        public AppUser Actor { get; }
        public AppUser Creator { get; }
        public AppUser RoleRecipient { get; }
        public AppUser PermissionRecipient { get; }
        public AppUser DeniedPermissionRecipient { get; }
        public AppUser CapabilityRecipient { get; }
        public AppRole TargetRole { get; }

        public static async Task<RecipientFixture> CreateAsync()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var dbContext = new AppDbContext(options);
            var targetRole = new AppRole(Guid.NewGuid(), "Notification target");
            var attendanceRole = new AppRole(Guid.NewGuid(), "Attendance viewers");
            var workersRole = new AppRole(Guid.NewGuid(), "Worker viewers");
            var attendancePermission = new Permission(Guid.NewGuid(), "attendance.view", "attendance");
            var workersPermission = new Permission(Guid.NewGuid(), "workers.view", "workers");
            var actor = User("actor");
            var creator = User("creator");
            var roleRecipient = User("role-recipient");
            var permissionRecipient = User("permission-recipient");
            var deniedPermissionRecipient = User("denied-permission-recipient");
            var capabilityRecipient = User("capability-recipient");

            roleRecipient.AssignRole(targetRole);
            permissionRecipient.AssignRole(attendanceRole);
            deniedPermissionRecipient.AssignRole(attendanceRole);
            capabilityRecipient.AssignRole(workersRole);

            dbContext.AddRange(
                targetRole,
                attendanceRole,
                workersRole,
                attendancePermission,
                workersPermission,
                actor,
                creator,
                roleRecipient,
                permissionRecipient,
                deniedPermissionRecipient,
                capabilityRecipient);
            dbContext.RolePermissions.AddRange(
                new RolePermission(attendanceRole.Id, attendancePermission.Id),
                new RolePermission(workersRole.Id, workersPermission.Id));
            dbContext.UserPermissionOverrides.Add(new UserPermissionOverride(
                deniedPermissionRecipient.Id,
                attendancePermission.Id,
                PermissionEffect.Deny,
                actor.Id));
            await dbContext.SaveChangesAsync();

            return new RecipientFixture(
                dbContext,
                new NotificationRecipientResolver(dbContext, new PermissionService(dbContext)),
                actor,
                creator,
                roleRecipient,
                permissionRecipient,
                deniedPermissionRecipient,
                capabilityRecipient,
                targetRole);
        }

        public ValueTask DisposeAsync() => DbContext.DisposeAsync();

        private static AppUser User(string name) =>
            new(Guid.NewGuid(), name, $"{name}@example.test", "hash");
    }
}

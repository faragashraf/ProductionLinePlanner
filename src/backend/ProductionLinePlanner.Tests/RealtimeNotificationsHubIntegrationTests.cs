using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProductionLinePlanner.Api.Realtime;
using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Realtime;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Realtime;

namespace ProductionLinePlanner.Tests;

public sealed class RealtimeNotificationsHubIntegrationTests
{
    [Fact]
    public async Task Anonymous_connection_is_rejected_and_no_client_group_method_exists()
    {
        await using var fixture = await HubFixture.CreateAsync();
        await using var anonymous = fixture.CreateConnection();

        var exception = await Assert.ThrowsAnyAsync<Exception>(() => anonymous.StartAsync());

        Assert.Contains(
            new[] { HttpStatusCode.Unauthorized, HttpStatusCode.NotFound },
            status => exception.ToString().Contains(((int)status).ToString(), StringComparison.Ordinal));

        var userId = Guid.NewGuid();
        await using var authenticated = fixture.CreateConnection(userId);
        await authenticated.StartAsync();
        await Assert.ThrowsAsync<HubException>(() => authenticated.InvokeAsync("JoinGroup", "capability:audit.view"));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("invalid")]
    public async Task Authenticated_connection_without_a_valid_server_user_id_is_closed(string authMode)
    {
        await using var fixture = await HubFixture.CreateAsync();
        await using var connection = fixture.CreateConnection(authMode: authMode);

        try
        {
            await connection.StartAsync();
            await Task.Delay(150);
            Assert.Equal(HubConnectionState.Disconnected, connection.State);
        }
        catch (Exception)
        {
            Assert.Equal(HubConnectionState.Disconnected, connection.State);
        }
    }

    [Fact]
    public async Task Client_query_values_cannot_impersonate_another_user_or_choose_a_capability_group()
    {
        await using var fixture = await HubFixture.CreateAsync();
        var authenticatedUserId = Guid.NewGuid();
        var impersonatedUserId = Guid.NewGuid();
        fixture.Permissions.Set(authenticatedUserId, []);
        await using var connection = fixture.CreateConnection(
            authenticatedUserId,
            query: $"userId={impersonatedUserId:D}&group=capability%3Aattendance.view");
        var ownDelivery = DeliverySource();
        var receivedCount = 0;
        connection.On<NotificationSummaryDto>("NotificationReceived", notification =>
        {
            Interlocked.Increment(ref receivedCount);
            ownDelivery.TrySetResult(notification);
        });
        await connection.StartAsync();

        await fixture.Dispatcher.SendToUserAsync(impersonatedUserId, Notification());
        await fixture.Dispatcher.SendToCapabilityAsync("attendance.view", Notification());
        await Task.Delay(150);
        Assert.Equal(0, receivedCount);

        await fixture.Dispatcher.SendToUserAsync(authenticatedUserId, Notification());
        await ownDelivery.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, receivedCount);
    }

    [Fact]
    public async Task User_dispatch_reaches_all_live_connections_for_that_user_only()
    {
        await using var fixture = await HubFixture.CreateAsync();
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        await using var firstTab = fixture.CreateConnection(userId);
        await using var secondTab = fixture.CreateConnection(userId);
        await using var otherUser = fixture.CreateConnection(otherUserId);
        var firstDelivery = DeliverySource();
        var secondDelivery = DeliverySource();
        var otherDeliveryCount = 0;
        firstTab.On<NotificationSummaryDto>("NotificationReceived", notification => firstDelivery.TrySetResult(notification));
        secondTab.On<NotificationSummaryDto>("NotificationReceived", notification => secondDelivery.TrySetResult(notification));
        otherUser.On<NotificationSummaryDto>("NotificationReceived", _ => Interlocked.Increment(ref otherDeliveryCount));
        await Task.WhenAll(firstTab.StartAsync(), secondTab.StartAsync(), otherUser.StartAsync());
        var notification = Notification();

        await fixture.Dispatcher.SendToUserAsync(userId, notification);

        Assert.Equal(notification.Id, (await firstDelivery.Task.WaitAsync(TimeSpan.FromSeconds(5))).Id);
        Assert.Equal(notification.Id, (await secondDelivery.Task.WaitAsync(TimeSpan.FromSeconds(5))).Id);
        await Task.Delay(150);
        Assert.Equal(0, otherDeliveryCount);
    }

    [Fact]
    public async Task Disconnecting_one_tab_does_not_break_another_connection_for_the_same_user()
    {
        await using var fixture = await HubFixture.CreateAsync();
        var userId = Guid.NewGuid();
        await using var firstTab = fixture.CreateConnection(userId);
        await using var secondTab = fixture.CreateConnection(userId);
        var secondDelivery = DeliverySource();
        secondTab.On<NotificationSummaryDto>("NotificationReceived", notification => secondDelivery.TrySetResult(notification));
        await Task.WhenAll(firstTab.StartAsync(), secondTab.StartAsync());

        await firstTab.StopAsync();
        var notification = Notification();
        await fixture.Dispatcher.SendToUserAsync(userId, notification);

        Assert.Equal(notification.Id, (await secondDelivery.Task.WaitAsync(TimeSpan.FromSeconds(5))).Id);
        Assert.Equal(HubConnectionState.Connected, secondTab.State);
    }

    [Fact]
    public async Task Capability_dispatch_reaches_only_authorized_connections_and_membership_returns_after_reconnect()
    {
        await using var fixture = await HubFixture.CreateAsync();
        var authorizedUserId = Guid.NewGuid();
        var unauthorizedUserId = Guid.NewGuid();
        fixture.Permissions.Set(authorizedUserId, ["attendance.view"]);
        fixture.Permissions.Set(unauthorizedUserId, ["workers.view"]);
        await using var authorized = fixture.CreateConnection(authorizedUserId);
        await using var unauthorized = fixture.CreateConnection(unauthorizedUserId);
        var authorizedDeliveryCount = 0;
        var unauthorizedDeliveryCount = 0;
        var firstDelivery = DeliverySource();
        authorized.On<NotificationSummaryDto>("NotificationReceived", notification =>
        {
            Interlocked.Increment(ref authorizedDeliveryCount);
            firstDelivery.TrySetResult(notification);
        });
        unauthorized.On<NotificationSummaryDto>("NotificationReceived", _ => Interlocked.Increment(ref unauthorizedDeliveryCount));
        await Task.WhenAll(authorized.StartAsync(), unauthorized.StartAsync());

        await fixture.Dispatcher.SendToCapabilityAsync("attendance.view", Notification());
        await firstDelivery.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(150);
        Assert.Equal(1, authorizedDeliveryCount);
        Assert.Equal(0, unauthorizedDeliveryCount);

        await authorized.StopAsync();
        await authorized.StartAsync();
        var reconnectedDelivery = DeliverySource();
        authorized.On<NotificationSummaryDto>("NotificationReceived", notification => reconnectedDelivery.TrySetResult(notification));
        await fixture.Dispatcher.SendToCapabilityAsync("attendance.view", Notification());

        await reconnectedDelivery.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(150);
        Assert.Equal(2, authorizedDeliveryCount);
        Assert.Equal(0, unauthorizedDeliveryCount);
    }

    private static TaskCompletionSource<NotificationSummaryDto> DeliverySource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static NotificationSummaryDto Notification() => new(
        Guid.NewGuid(),
        "Realtime update",
        "A persisted notification is available.",
        NotificationStatus.Unread,
        false,
        null,
        null,
        DateTime.UtcNow,
        null);

    private sealed class HubFixture : IAsyncDisposable
    {
        private readonly WebApplication app;

        private HubFixture(WebApplication app, PermissionMap permissions)
        {
            this.app = app;
            Permissions = permissions;
            Dispatcher = app.Services.GetRequiredService<INotificationLiveDispatcher>();
        }

        public PermissionMap Permissions { get; }
        public INotificationLiveDispatcher Dispatcher { get; }

        public static async Task<HubFixture> CreateAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "IntegrationTest" });
            builder.WebHost.UseTestServer();
            var permissions = new PermissionMap();
            builder.Services.AddSingleton(permissions);
            builder.Services.AddSingleton<IPermissionService>(permissions);
            builder.Services.AddSingleton<ICapabilityGroupResolver, CapabilityGroupResolver>();
            builder.Services.AddSingleton<IUserIdProvider, AuthenticatedUserIdProvider>();
            builder.Services.AddSingleton<INotificationLiveDispatcher, SignalRNotificationLiveDispatcher>();
            builder.Services.AddSignalR();
            builder.Services.AddAuthentication("test")
                .AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>("test", _ => { });
            builder.Services.AddAuthorization();

            var app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapHub<NotificationsHub>(RealtimeEndpointPaths.NotificationsHub).RequireAuthorization();
            await app.StartAsync();
            return new HubFixture(app, permissions);
        }

        public HubConnection CreateConnection(
            Guid? userId = null,
            string? authMode = null,
            string? query = null)
        {
            var url = $"http://localhost{RealtimeEndpointPaths.NotificationsHub}";
            if (!string.IsNullOrWhiteSpace(query))
            {
                url = $"{url}?{query}";
            }

            return new HubConnectionBuilder()
                .WithUrl(url, options =>
                {
                    options.Transports = HttpTransportType.LongPolling;
                    options.HttpMessageHandlerFactory = _ => app.GetTestServer().CreateHandler();
                    if (userId.HasValue)
                    {
                        options.Headers["X-Test-User"] = userId.Value.ToString();
                    }
                    if (!string.IsNullOrWhiteSpace(authMode))
                    {
                        options.Headers["X-Test-Auth-Mode"] = authMode;
                    }
                })
                .Build();
        }

        public ValueTask DisposeAsync() => app.DisposeAsync();
    }

    public sealed class PermissionMap : IPermissionService
    {
        private readonly Dictionary<Guid, IReadOnlyCollection<string>> permissionsByUser = [];

        public void Set(Guid userId, IReadOnlyCollection<string> permissions) => permissionsByUser[userId] = permissions;

        public Task<IReadOnlyCollection<string>> GetEffectivePermissionsAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(permissionsByUser.GetValueOrDefault(userId, Array.Empty<string>()));

        public Task<PermissionCatalogItemDto[]> GetCatalogAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Array.Empty<PermissionCatalogItemDto>());
    }

    private sealed class HeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var authMode = Request.Headers["X-Test-Auth-Mode"].ToString();
            if (string.Equals(authMode, "missing", StringComparison.Ordinal))
            {
                var missingIdentity = new ClaimsIdentity(
                    [new Claim(ClaimTypes.Name, "Authenticated without user id")],
                    Scheme.Name);
                return Task.FromResult(AuthenticateResult.Success(
                    new AuthenticationTicket(new ClaimsPrincipal(missingIdentity), Scheme.Name)));
            }

            if (string.Equals(authMode, "invalid", StringComparison.Ordinal))
            {
                var invalidIdentity = new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "not-a-guid")],
                    Scheme.Name);
                return Task.FromResult(AuthenticateResult.Success(
                    new AuthenticationTicket(new ClaimsPrincipal(invalidIdentity), Scheme.Name)));
            }

            var rawUserId = Request.Headers["X-Test-User"].ToString();
            if (!Guid.TryParse(rawUserId, out var userId))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
                Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}

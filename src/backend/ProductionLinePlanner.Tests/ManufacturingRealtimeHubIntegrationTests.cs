using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
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
using ProductionLinePlanner.Infrastructure.Realtime;

namespace ProductionLinePlanner.Tests;

public sealed class ManufacturingRealtimeHubIntegrationTests
{
    [Fact]
    public async Task Two_connections_on_the_same_authorized_screen_receive_the_same_compact_change_without_notifying_other_screens()
    {
        await using var fixture = await HubFixture.CreateAsync();
        var userId = Guid.NewGuid();
        fixture.Permissions.Set(userId, ["models.view", "stages.view"]);
        await using var modelTabA = fixture.CreateConnection(userId);
        await using var modelTabB = fixture.CreateConnection(userId);
        await using var stagesTab = fixture.CreateConnection(userId);
        var receivedByA = DeliverySource();
        var receivedByB = DeliverySource();
        var receivedByStages = DeliverySource();
        modelTabA.On<ManufacturingDataChangedMessage>("ManufacturingDataChanged", change => receivedByA.TrySetResult(change));
        modelTabB.On<ManufacturingDataChangedMessage>("ManufacturingDataChanged", change => receivedByB.TrySetResult(change));
        stagesTab.On<ManufacturingDataChangedMessage>("ManufacturingDataChanged", change => receivedByStages.TrySetResult(change));

        await modelTabA.StartAsync();
        await modelTabB.StartAsync();
        await stagesTab.StartAsync();
        await modelTabA.InvokeAsync("JoinManufacturingScreen", "models");
        await modelTabB.InvokeAsync("JoinManufacturingScreen", "models");
        await stagesTab.InvokeAsync("JoinManufacturingScreen", "stages");

        var change = Change();
        await fixture.Publisher.PublishAsync(change);

        var expected = ManufacturingDataChangedMessage.From(change);
        Assert.Equal(expected, await receivedByA.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(expected, await receivedByB.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        await Task.Delay(150);
        Assert.False(receivedByStages.Task.IsCompleted);
    }

    [Fact]
    public async Task Reconnected_tab_can_rejoin_its_screen_and_receive_a_subsequent_change()
    {
        await using var fixture = await HubFixture.CreateAsync();
        var userId = Guid.NewGuid();
        fixture.Permissions.Set(userId, ["models.view"]);
        await using var tab = fixture.CreateConnection(userId);
        var delivery = DeliverySource();
        tab.On<ManufacturingDataChangedMessage>("ManufacturingDataChanged", change => delivery.TrySetResult(change));
        await tab.StartAsync();
        await tab.InvokeAsync("JoinManufacturingScreen", "models");
        await tab.StopAsync();
        await tab.StartAsync();
        await tab.InvokeAsync("JoinManufacturingScreen", "models");

        var change = Change();
        await fixture.Publisher.PublishAsync(change);

        Assert.Equal(ManufacturingDataChangedMessage.From(change), await delivery.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Browser_wire_contract_uses_stable_string_entity_and_change_values_for_factory_and_worker()
    {
        await using var fixture = await HubFixture.CreateAsync();
        var userId = Guid.NewGuid();
        fixture.Permissions.Set(userId, ["factory-structure.view", "workers.view"]);
        await using var tab = fixture.CreateConnection(userId);
        var received = new List<ManufacturingDataChangedMessage>();
        var deliveries = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        tab.On<ManufacturingDataChangedMessage>("ManufacturingDataChanged", change =>
        {
            lock (received)
            {
                received.Add(change);
                if (received.Count == 2) deliveries.TrySetResult(true);
            }
        });
        await tab.StartAsync();
        await tab.InvokeAsync("JoinManufacturingScreen", "factory-structure");
        await tab.InvokeAsync("JoinManufacturingScreen", "employees");

        await fixture.Publisher.PublishAsync(new ManufacturingDataChanged(
            Guid.NewGuid(), ManufacturingEntityType.Factory, ManufacturingChangeType.Updated,
            Guid.NewGuid(), DateTime.UtcNow, null, null));
        await fixture.Publisher.PublishAsync(new ManufacturingDataChanged(
            Guid.NewGuid(), ManufacturingEntityType.Worker, ManufacturingChangeType.Deactivated,
            Guid.NewGuid(), DateTime.UtcNow, null, null));

        await deliveries.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains(received, change => change.EntityType == "Factory" && change.ChangeType == "Updated");
        Assert.Contains(received, change => change.EntityType == "Worker" && change.ChangeType == "Deactivated");
    }

    private static ManufacturingDataChanged Change() => new(
        Guid.NewGuid(), ManufacturingEntityType.ProductModel, ManufacturingChangeType.Updated,
        Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), null, ProductModelId: Guid.NewGuid());

    private static TaskCompletionSource<ManufacturingDataChangedMessage> DeliverySource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class HubFixture : IAsyncDisposable
    {
        private readonly WebApplication app;

        private HubFixture(WebApplication app, PermissionMap permissions)
        {
            this.app = app;
            Permissions = permissions;
            Publisher = app.Services.GetRequiredService<IManufacturingDataChangePublisher>();
        }

        public PermissionMap Permissions { get; }
        public IManufacturingDataChangePublisher Publisher { get; }

        public static async Task<HubFixture> CreateAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "IntegrationTest" });
            builder.WebHost.UseTestServer();
            var permissions = new PermissionMap();
            builder.Services.AddSingleton(permissions);
            builder.Services.AddSingleton<IPermissionService>(permissions);
            builder.Services.AddSingleton<ICapabilityGroupResolver, CapabilityGroupResolver>();
            builder.Services.AddSingleton<IUserIdProvider, AuthenticatedUserIdProvider>();
            builder.Services.AddSingleton<IManufacturingDataChangePublisher, SignalRManufacturingDataChangePublisher>();
            builder.Services.AddSignalR();
            builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>("test", _ => { });
            builder.Services.AddAuthorization();

            var app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapHub<NotificationsHub>(RealtimeEndpointPaths.NotificationsHub).RequireAuthorization();
            await app.StartAsync();
            return new HubFixture(app, permissions);
        }

        public HubConnection CreateConnection(Guid userId) => new HubConnectionBuilder()
            .WithUrl($"http://localhost{RealtimeEndpointPaths.NotificationsHub}", options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => app.GetTestServer().CreateHandler();
                options.Headers["X-Test-User"] = userId.ToString();
            })
            .Build();

        public ValueTask DisposeAsync() => app.DisposeAsync();
    }

    private sealed class PermissionMap : IPermissionService
    {
        private readonly Dictionary<Guid, IReadOnlyCollection<string>> permissions = [];
        public void Set(Guid userId, IReadOnlyCollection<string> values) => permissions[userId] = values;
        public Task<IReadOnlyCollection<string>> GetEffectivePermissionsAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult(permissions.GetValueOrDefault(userId, Array.Empty<string>()));
        public Task<PermissionCatalogItemDto[]> GetCatalogAsync(CancellationToken cancellationToken = default) => Task.FromResult(Array.Empty<PermissionCatalogItemDto>());
    }

    private sealed class HeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Guid.TryParse(Request.Headers["X-Test-User"], out var userId)) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}

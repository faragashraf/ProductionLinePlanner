using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Data.Sqlite;
using ProductionLinePlanner.Api.Realtime;
using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Application.Realtime;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Infrastructure.Data;
using ProductionLinePlanner.Infrastructure.Realtime;

namespace ProductionLinePlanner.Tests;

public sealed class ManufacturingRealtimeFoundationTests
{
    [Fact]
    public async Task Successful_master_data_save_publishes_a_compact_event_after_commit()
    {
        var publisher = new RecordingPublisher();
        var actor = Guid.NewGuid();
        await using var db = CreateDb(publisher, actor);
        var model = new ProductModel(Guid.NewGuid(), "M-001", "Model");

        db.ProductModels.Add(model);
        await db.SaveChangesAsync();

        var change = Assert.Single(publisher.Changes);
        Assert.Equal(ManufacturingEntityType.ProductModel, change.EntityType);
        Assert.Equal(ManufacturingChangeType.Created, change.ChangeType);
        Assert.Equal(model.Id, change.EntityId);
        Assert.Equal(actor, change.ActorUserId);
        Assert.Equal(model.Id, change.ProductModelId);
        Assert.Null(change.CorrelationId);
    }

    [Fact]
    public async Task Daily_production_order_save_publishes_one_contextual_event_after_successful_save()
    {
        var publisher = new RecordingPublisher();
        var actor = Guid.NewGuid();
        await using var db = CreateDb(publisher, actor);
        var factory = new Factory(Guid.NewGuid(), "Factory", "F-001");
        var line = new ProductionLine(Guid.NewGuid(), factory.Id, "Line", 1, "L-001");
        var model = new ProductModel(Guid.NewGuid(), "M-001", "Model");
        db.AddRange(factory, line, model);
        await db.SaveChangesAsync();
        publisher.Changes.Clear();

        var date = new DateOnly(2026, 7, 21);
        var order = new ProductionOrder(Guid.NewGuid(), "DLY-001", model.Id, line.Id, date, 10m, null, actor, DateTime.UtcNow);
        order.MarkDailyOperation("DailyProductionOperations/test", DateTime.UtcNow);
        db.Add(order);
        await db.SaveChangesAsync();

        var change = Assert.Single(publisher.Changes);
        Assert.Equal(ManufacturingEntityType.ProductionOrder, change.EntityType);
        Assert.Equal(ManufacturingChangeType.Created, change.ChangeType);
        Assert.Equal(order.Id, change.EntityId);
        Assert.Equal(factory.Id, change.FactoryId);
        Assert.Equal(line.Id, change.ProductionLineId);
        Assert.Equal(model.Id, change.ProductModelId);
        Assert.Equal(date, change.ProductionDate);
        Assert.Equal(["manufacturing:daily-production-operations"], ManufacturingRealtimeGroups.ForChange(change));
    }

    [Fact]
    public async Task Daily_production_approval_cancellation_publishes_one_updated_contextual_event()
    {
        var publisher = new RecordingPublisher();
        var actor = Guid.NewGuid();
        await using var db = CreateDb(publisher, actor);
        var factory = new Factory(Guid.NewGuid(), "Factory", "F-001");
        var line = new ProductionLine(Guid.NewGuid(), factory.Id, "Line", 1, "L-001");
        var model = new ProductModel(Guid.NewGuid(), "M-001", "Model");
        var date = new DateOnly(2026, 7, 21);
        var order = new ProductionOrder(Guid.NewGuid(), "DLY-001", model.Id, line.Id, date, 10m, null, actor, DateTime.UtcNow);
        order.MarkDailyOperation("DailyProductionOperations/test", DateTime.UtcNow);
        db.AddRange(factory, line, model, order);
        await db.SaveChangesAsync();
        publisher.Changes.Clear();

        order.ApproveDay(actor, DateTime.UtcNow);
        await db.SaveChangesAsync();
        publisher.Changes.Clear();

        order.ReopenDailyOperationAfterApprovalCancellation(actor, DateTime.UtcNow);
        await db.SaveChangesAsync();

        var change = Assert.Single(publisher.Changes);
        Assert.Equal(ManufacturingEntityType.ProductionOrder, change.EntityType);
        Assert.Equal(ManufacturingChangeType.Updated, change.ChangeType);
        Assert.Equal(factory.Id, change.FactoryId);
        Assert.Equal(line.Id, change.ProductionLineId);
        Assert.Equal(model.Id, change.ProductModelId);
        Assert.Equal(date, change.ProductionDate);
    }

    [Fact]
    public async Task Update_activation_and_relationship_changes_are_classified_without_entity_payloads()
    {
        var publisher = new RecordingPublisher();
        await using var db = CreateDb(publisher, Guid.NewGuid());
        var model = new ProductModel(Guid.NewGuid(), "M-001", "Model");
        db.ProductModels.Add(model);
        await db.SaveChangesAsync();
        publisher.Changes.Clear();

        model.Deactivate();
        await db.SaveChangesAsync();

        var deactivated = Assert.Single(publisher.Changes);
        Assert.Equal(ManufacturingChangeType.Deactivated, deactivated.ChangeType);
        Assert.DoesNotContain(typeof(ManufacturingDataChanged).GetProperties(), property => property.Name is "Name" or "Description" or "Code");
    }

    [Fact]
    public async Task Failed_realtime_publish_does_not_undo_a_successful_database_save()
    {
        await using var db = CreateDb(new RecordingPublisher(throwOnPublish: true), Guid.NewGuid());
        var model = new ProductModel(Guid.NewGuid(), "M-001", "Model");

        db.ProductModels.Add(model);
        await db.SaveChangesAsync();

        Assert.True(await db.ProductModels.AnyAsync(item => item.Id == model.Id));
    }

    [Fact]
    public async Task Failed_explicit_transaction_commit_does_not_publish_a_saved_change()
    {
        var publisher = new RecordingPublisher();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        connection.CreateCollation("SQL_Latin1_General_CP1_CI_AS", (left, right) => string.Compare(left, right, StringComparison.OrdinalIgnoreCase));
        await connection.OpenAsync();
        await using (var setup = CreateRelationalDb(connection, publisher))
        {
            await setup.Database.EnsureCreatedAsync();
        }
        await using var db = CreateRelationalDb(connection, publisher, new ThrowOnCommitInterceptor());

        await using var transaction = await db.Database.BeginTransactionAsync();
        db.ProductModels.Add(new ProductModel(Guid.NewGuid(), "M-ROLLBACK", "Rollback model"));
        await db.SaveChangesAsync();

        Assert.Empty(publisher.Changes);
        Assert.Throws<InvalidOperationException>(() => transaction.Commit());
        Assert.Empty(publisher.Changes);
    }

    [Fact]
    public async Task Successful_explicit_transaction_commit_publishes_each_change_once()
    {
        var publisher = new RecordingPublisher();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        connection.CreateCollation("SQL_Latin1_General_CP1_CI_AS", (left, right) => string.Compare(left, right, StringComparison.OrdinalIgnoreCase));
        await connection.OpenAsync();
        await using var db = CreateRelationalDb(connection, publisher);
        await db.Database.EnsureCreatedAsync();

        await using var transaction = await db.Database.BeginTransactionAsync();
        var model = new ProductModel(Guid.NewGuid(), "M-COMMIT", "Committed model");
        db.ProductModels.Add(model);
        await db.SaveChangesAsync();
        Assert.Empty(publisher.Changes);

        await transaction.CommitAsync();

        var change = Assert.Single(publisher.Changes);
        Assert.Equal(model.Id, change.EntityId);
    }

    [Fact]
    public void Groups_are_limited_to_affected_manufacturing_screens()
    {
        var modelGroups = ManufacturingRealtimeGroups.ForChange(new ManufacturingDataChanged(
            Guid.NewGuid(), ManufacturingEntityType.ProductModel, ManufacturingChangeType.Updated, Guid.NewGuid(), DateTime.UtcNow, null, null));
        var departmentGroups = ManufacturingRealtimeGroups.ForChange(new ManufacturingDataChanged(
            Guid.NewGuid(), ManufacturingEntityType.Department, ManufacturingChangeType.Updated, Guid.NewGuid(), DateTime.UtcNow, null, null));
        var workerGroups = ManufacturingRealtimeGroups.ForChange(new ManufacturingDataChanged(
            Guid.NewGuid(), ManufacturingEntityType.Worker, ManufacturingChangeType.Updated, Guid.NewGuid(), DateTime.UtcNow, null, null));
        var dailyGroups = ManufacturingRealtimeGroups.ForChange(new ManufacturingDataChanged(
            Guid.NewGuid(), ManufacturingEntityType.ProductionOrder, ManufacturingChangeType.Updated, Guid.NewGuid(), DateTime.UtcNow, null, null,
            ProductionLineId: Guid.NewGuid(), ProductModelId: Guid.NewGuid(), ProductionDate: new DateOnly(2026, 7, 21)));

        Assert.Equal(["manufacturing:models"], modelGroups);
        Assert.Equal(["manufacturing:factory-structure", "manufacturing:departments", "manufacturing:stages"], departmentGroups);
        Assert.Equal(["manufacturing:employees"], workerGroups);
        Assert.Equal(["manufacturing:daily-production-operations"], dailyGroups);
        Assert.True(ManufacturingRealtimeGroups.TryGetRequiredPermission("models", out var permission));
        Assert.Equal("models.view", permission);
        Assert.True(ManufacturingRealtimeGroups.TryGetRequiredPermission("employees", out permission));
        Assert.Equal("workers.view", permission);
        Assert.True(ManufacturingRealtimeGroups.TryGetRequiredPermission("daily-production-operations", out permission));
        Assert.Equal("production.record", permission);
        Assert.False(ManufacturingRealtimeGroups.TryGetRequiredPermission("reports", out _));
    }

    private static AppDbContext CreateDb(IManufacturingDataChangePublisher publisher, Guid actorUserId)
    {
        var interceptor = new ManufacturingDataChangeSaveChangesInterceptor(
            publisher,
            new CurrentUserStub(actorUserId),
            new CorrelationStub(),
            new ManufacturingDataChangeTransactionCoordinator(publisher, NullLogger<ManufacturingDataChangeTransactionCoordinator>.Instance),
            NullLogger<ManufacturingDataChangeSaveChangesInterceptor>.Instance);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(interceptor)
            .Options;
        return new AppDbContext(options);
    }

    private static AppDbContext CreateRelationalDb(SqliteConnection connection, IManufacturingDataChangePublisher publisher, params IInterceptor[] additionalInterceptors)
    {
        var coordinator = new ManufacturingDataChangeTransactionCoordinator(publisher, NullLogger<ManufacturingDataChangeTransactionCoordinator>.Instance);
        var interceptors = new IInterceptor[]
        {
            new ManufacturingDataChangeSaveChangesInterceptor(publisher, new CurrentUserStub(Guid.NewGuid()), new CorrelationStub(), coordinator, NullLogger<ManufacturingDataChangeSaveChangesInterceptor>.Instance),
            new ManufacturingDataChangeTransactionInterceptor(coordinator)
        }.Concat(additionalInterceptors).ToArray();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptors)
            .Options;
        return new AppDbContext(options);
    }

    private sealed class RecordingPublisher(bool throwOnPublish = false) : IManufacturingDataChangePublisher
    {
        public List<ManufacturingDataChanged> Changes { get; } = [];

        public Task PublishAsync(ManufacturingDataChanged change, CancellationToken cancellationToken = default)
        {
            if (throwOnPublish) throw new InvalidOperationException("SignalR unavailable");
            Changes.Add(change);
            return Task.CompletedTask;
        }
    }

    private sealed class CurrentUserStub(Guid userId) : ICurrentUserService
    {
        public Guid? UserId => userId;
        public string? UserName => null;
        public bool IsAuthenticated => true;
        public IReadOnlyCollection<string> Roles => [];
    }

    private sealed class CorrelationStub : IManufacturingRealtimeCorrelationContext
    {
        public string? CorrelationId => null;
    }

    private sealed class ThrowOnCommitInterceptor : DbTransactionInterceptor
    {
        public override InterceptionResult TransactionCommitting(System.Data.Common.DbTransaction transaction, TransactionEventData eventData, InterceptionResult result) =>
            throw new InvalidOperationException("Commit failed.");
    }
}

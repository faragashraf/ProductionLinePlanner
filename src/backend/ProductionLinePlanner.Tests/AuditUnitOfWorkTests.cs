using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Requests;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.BusinessEngines;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Tests;

public sealed class AuditUnitOfWorkTests
{
    [Fact]
    public async Task Product_model_and_audit_log_persist_in_the_same_successful_save()
    {
        await using var fixture = await AuditFixture.CreateAsync(seedActor: true);
        var result = await fixture.Service.CreateModelAsync(
            new CreateProductModelRequest { Code = "AUD-1", Name = "Audited model" },
            fixture.ActorId);

        Assert.True(result.IsSuccess);
        await using var verification = fixture.CreateVerificationContext();
        Assert.Single(await verification.ProductModels.Where(model => model.Code == "AUD-1").ToListAsync());
        Assert.Single(await verification.AuditLogs.Where(log => log.EntityType == nameof(ProductModel)).ToListAsync());
    }

    [Fact]
    public async Task Product_model_and_audit_log_roll_back_together_when_the_audit_foreign_key_is_invalid()
    {
        await using var fixture = await AuditFixture.CreateAsync(seedActor: false);

        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Service.CreateModelAsync(
            new CreateProductModelRequest { Code = "AUD-ROLLBACK", Name = "Rejected audited model" },
            fixture.ActorId));

        await using var verification = fixture.CreateVerificationContext();
        Assert.Empty(await verification.ProductModels.Where(model => model.Code == "AUD-ROLLBACK").ToListAsync());
        Assert.Empty(await verification.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task Api_style_write_and_audit_log_persist_in_the_same_save()
    {
        await using var fixture = await AuditFixture.CreateAsync(seedActor: true);
        var factory = new Factory(Guid.NewGuid(), "Audited factory", "API-AUDIT");

        fixture.DbContext.Factories.Add(factory);
        await fixture.AuditEngine.RecordAsync(
            fixture.ActorId,
            AuditActionType.Create,
            nameof(Factory),
            factory.Id.ToString(),
            after: new { factory.Id, factory.Name, factory.Code },
            requestMeta: "POST /api/factories");
        await fixture.DbContext.SaveChangesAsync();

        await using var verification = fixture.CreateVerificationContext();
        Assert.Single(await verification.Factories.Where(item => item.Code == "API-AUDIT").ToListAsync());
        Assert.Single(await verification.AuditLogs.Where(log => log.EntityType == nameof(Factory)).ToListAsync());
    }

    [Fact]
    public async Task Api_style_write_and_audit_log_roll_back_together_when_audit_actor_is_invalid()
    {
        await using var fixture = await AuditFixture.CreateAsync(seedActor: false);
        var factory = new Factory(Guid.NewGuid(), "Rejected factory", "API-ROLLBACK");

        fixture.DbContext.Factories.Add(factory);
        await fixture.AuditEngine.RecordAsync(
            fixture.ActorId,
            AuditActionType.Create,
            nameof(Factory),
            factory.Id.ToString(),
            after: new { factory.Id, factory.Name, factory.Code },
            requestMeta: "POST /api/factories");

        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.DbContext.SaveChangesAsync());

        await using var verification = fixture.CreateVerificationContext();
        Assert.Empty(await verification.Factories.Where(item => item.Code == "API-ROLLBACK").ToListAsync());
        Assert.Empty(await verification.AuditLogs.ToListAsync());
    }

    private sealed class AuditFixture : IAsyncDisposable
    {
        private AuditFixture(SqliteConnection connection, DbContextOptions<AppDbContext> options, AppDbContext dbContext, Guid actorId, ProductModelService service)
        {
            Connection = connection;
            Options = options;
            DbContext = dbContext;
            ActorId = actorId;
            Service = service;
        }

        private SqliteConnection Connection { get; }
        private DbContextOptions<AppDbContext> Options { get; }
        public AppDbContext DbContext { get; }
        public Guid ActorId { get; }
        public ProductModelService Service { get; }
        public AuditEngine AuditEngine => new(DbContext);

        public static async Task<AuditFixture> CreateAsync(bool seedActor)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            connection.CreateCollation(
                "SQL_Latin1_General_CP1_CI_AS",
                static (left, right) => string.Compare(left, right, StringComparison.OrdinalIgnoreCase));
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;
            var dbContext = new AppDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();
            var actorId = Guid.NewGuid();
            if (seedActor)
            {
                dbContext.AppUsers.Add(new AppUser(actorId, "Audit Actor", "audit-actor@example.test", "test-hash"));
                await dbContext.SaveChangesAsync();
            }

            return new AuditFixture(connection, options, dbContext, actorId, new ProductModelService(dbContext, new AuditEngine(dbContext)));
        }

        public AppDbContext CreateVerificationContext() => new(Options);

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}

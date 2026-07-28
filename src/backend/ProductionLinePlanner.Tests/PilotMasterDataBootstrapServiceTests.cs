using ClosedXML.Excel;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Bootstrap;
using ProductionLinePlanner.Infrastructure.Data;
using ProductionLinePlanner.Infrastructure.Importing;

namespace ProductionLinePlanner.Tests;

/// <summary>
/// Synthetic fixtures only. These tests never open a supplied pilot workbook.
/// </summary>
public sealed class PilotMasterDataBootstrapServiceTests
{
    [Fact]
    public async Task Bootstrap_generates_stg004_in_row_order_maps_67_stages_and_is_idempotent()
    {
        await using var fixture = await BootstrapFixture.CreateAsync(includeWorker: true);
        var input = fixture.CreateInput(salary: 100m, secondsForFirstStage: 18m);

        var preview = await fixture.Service.PreviewAsync(input);

        Assert.True(preview.CanApply);
        Assert.Equal(67, preview.SourceStageRows);
        Assert.Equal(64, preview.GeneratedCodes.Count);
        Assert.Equal("STG004", preview.GeneratedCodes.First());
        Assert.Equal("STG067", preview.GeneratedCodes.Last());
        Assert.Equal(67, preview.StagesCreated);
        Assert.Equal(67, preview.ProductStageMappingsCreated);

        var applied = await fixture.Service.ApplyAsync(input, fixture.ActorId, confirmed: true);
        Assert.False(applied.WasAlreadyCurrent);
        Assert.Equal(1, await fixture.Db.Factories.CountAsync());
        Assert.Equal(1, await fixture.Db.ProductionLines.CountAsync());
        Assert.Equal(1, await fixture.Db.ProductModels.CountAsync());
        Assert.Equal(67, await fixture.Db.SubStages.CountAsync());
        Assert.Equal(67, await fixture.Db.ProductModelStages.CountAsync());
        var codes = await fixture.Db.SubStages.Select(x => x.Code).OrderBy(x => x).ToArrayAsync();
        Assert.Equal(Enumerable.Range(1, 67).Select(x => $"STG{x:000}"), codes);
        Assert.Empty(await fixture.Db.Set<ProductionOrder>().ToArrayAsync());

        var rerun = await fixture.Service.ApplyAsync(input, fixture.ActorId, confirmed: true);

        Assert.True(rerun.WasAlreadyCurrent);
        Assert.Equal(67, await fixture.Db.SubStages.CountAsync());
        Assert.Equal(67, await fixture.Db.ProductModelStages.CountAsync());
    }

    [Fact]
    public async Task Bootstrap_keeps_missing_seconds_null_and_ignored_columns_do_not_affect_master_data()
    {
        await using var fixture = await BootstrapFixture.CreateAsync(includeWorker: true);
        var input = fixture.CreateInput(salary: 100m, secondsForFirstStage: null);

        var preview = await fixture.Service.PreviewAsync(input);

        Assert.True(preview.CanApply);
        await fixture.Service.ApplyAsync(input, fixture.ActorId, confirmed: true);
        var first = await fixture.Db.ProductModelStages.Include(x => x.SubStage).OrderBy(x => x.SubStage!.Code).FirstAsync();
        Assert.Equal("STG001", first.SubStage!.Code);
        Assert.Null(first.StandardSeconds);
        Assert.Equal(67, await fixture.Db.ProductModelStages.CountAsync());
    }

    [Fact]
    public async Task Bootstrap_matches_only_employee_code_sets_zero_salary_to_null_and_never_creates_a_worker()
    {
        await using var fixture = await BootstrapFixture.CreateAsync(includeWorker: true, currentSalary: 150m);
        var originalAttendanceUserId = fixture.Worker!.AttendanceUserId;
        var originalAttendanceDepartment = fixture.Worker.AttendanceDepartmentId;
        var input = fixture.CreateInput(salary: 0m);

        var preview = await fixture.Service.PreviewAsync(input);

        Assert.True(preview.CanApply);
        Assert.Equal(1, preview.WorkersMatched);
        Assert.Equal(0, preview.WorkersUnmatched);
        Assert.Equal(1, preview.SalariesSetNull);
        await fixture.Service.ApplyAsync(input, fixture.ActorId, confirmed: true);

        var worker = await fixture.Db.Workers.SingleAsync();
        Assert.Equal("قطاع اختبار", worker.LocalDepartmentName);
        Assert.Equal(originalAttendanceUserId, worker.AttendanceUserId);
        Assert.Equal(originalAttendanceDepartment, worker.AttendanceDepartmentId);
        Assert.Empty(await fixture.Db.WorkerSalaryHistories.Where(x => x.WorkerId == worker.Id && x.EffectiveTo == null).ToArrayAsync());
        Assert.Single(await fixture.Db.Workers.ToArrayAsync());
    }

    [Fact]
    public async Task Bootstrap_skips_unmatched_employee_codes_without_creating_workers()
    {
        await using var fixture = await BootstrapFixture.CreateAsync(includeWorker: true);
        var input = fixture.CreateInput(salary: 100m, employeeCode: "9999");

        var preview = await fixture.Service.PreviewAsync(input);

        Assert.True(preview.CanApply);
        Assert.Equal(0, preview.WorkersMatched);
        Assert.Equal(1, preview.WorkersUnmatched);
        Assert.Contains(preview.Issues, x => x.Code == "UnmatchedEmployeeCode");
        Assert.Single(await fixture.Db.Workers.ToArrayAsync());
        Assert.Contains(preview.Issues, x => x.Severity == "warning" && x.Code == "UnmatchedEmployeeCode");
        Assert.Equal(new[] { "9999" }, preview.UnmatchedEmployeeCodes);
        await fixture.Service.ApplyAsync(input, fixture.ActorId, confirmed: true);
        Assert.Single(await fixture.Db.Workers.ToArrayAsync());
        Assert.Single(await fixture.Db.Factories.ToArrayAsync());
    }

    [Fact]
    public async Task Bootstrap_uses_shared_percentage_as_a_provisional_compensation_default()
    {
        await using var fixture = await BootstrapFixture.CreateAsync(includeWorker: true);
        var input = fixture.CreateInput(salary: 100m, compensationMode: null);

        var preview = await fixture.Service.PreviewAsync(input);

        Assert.True(preview.CanApply);
        Assert.Equal(67, preview.ProvisionalCompensationMappingsForReview);
        await fixture.Service.ApplyAsync(input, fixture.ActorId, confirmed: true);
        Assert.Equal(67, await fixture.Db.ProductModelStages.CountAsync(x => x.CompensationMode == CompensationMode.SharedPercentage));
    }

    [Fact]
    public async Task Bootstrap_allows_the_same_stage_code_owned_by_another_production_line()
    {
        await using var fixture = await BootstrapFixture.CreateAsync(includeWorker: true);
        var otherFactory = new Factory(Guid.NewGuid(), "Other factory", "OTHER");
        var otherLine = new ProductionLine(Guid.NewGuid(), otherFactory.Id, "Other line", 1, "OTHER-LINE");
        var otherMainStage = new MainStage(Guid.NewGuid(), otherLine.Id, "Other group", 1);
        fixture.Db.AddRange(
            otherFactory,
            otherLine,
            otherMainStage,
            new SubStage(Guid.NewGuid(), otherMainStage.Id, "Other stage", "STG001", 1, 1, productionLineId: otherLine.Id));
        await fixture.Db.SaveChangesAsync();
        var input = fixture.CreateInput(salary: 100m);

        var preview = await fixture.Service.PreviewAsync(input);
        var applied = await fixture.Service.ApplyAsync(input, fixture.ActorId, confirmed: true);

        Assert.True(preview.CanApply);
        Assert.False(applied.WasAlreadyCurrent);
        var matchingStages = await fixture.Db.SubStages.AsNoTracking()
            .Where(stage => stage.Code == "STG001")
            .ToArrayAsync();
        Assert.Equal(2, matchingStages.Length);
        Assert.Equal(2, matchingStages.Select(stage => stage.ProductionLineId).Distinct().Count());
    }

    [Fact]
    public async Task Bootstrap_rolls_back_all_local_changes_when_auditing_fails()
    {
        await using var fixture = await BootstrapFixture.CreateAsync(includeWorker: true, throwingAudit: true);
        var input = fixture.CreateInput(salary: 100m);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ApplyAsync(input, fixture.ActorId, confirmed: true));

        fixture.Db.ChangeTracker.Clear();
        Assert.Empty(await fixture.Db.Factories.ToArrayAsync());
        Assert.Empty(await fixture.Db.ProductionLines.ToArrayAsync());
        Assert.Empty(await fixture.Db.ProductModels.ToArrayAsync());
        Assert.Empty(await fixture.Db.SubStages.ToArrayAsync());
        Assert.Empty(await fixture.Db.ProductModelStages.ToArrayAsync());
        var worker = await fixture.Db.Workers.SingleAsync();
        Assert.Null(worker.LocalDepartmentName);
        Assert.Empty(await fixture.Db.WorkerSalaryHistories.Where(x => x.WorkerId == worker.Id).ToArrayAsync());
    }

    private sealed class BootstrapFixture : IAsyncDisposable
    {
        private BootstrapFixture(SqliteConnection connection, AppDbContext db, PilotMasterDataBootstrapService service, Guid actorId, Worker? worker)
        {
            Connection = connection;
            Db = db;
            Service = service;
            ActorId = actorId;
            Worker = worker;
        }

        public SqliteConnection Connection { get; }
        public AppDbContext Db { get; }
        public PilotMasterDataBootstrapService Service { get; }
        public Guid ActorId { get; }
        public Worker? Worker { get; }

        public static async Task<BootstrapFixture> CreateAsync(bool includeWorker, decimal? currentSalary = null, bool throwingAudit = false)
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            connection.CreateCollation("SQL_Latin1_General_CP1_CI_AS", (left, right) =>
                string.Compare(left, right, StringComparison.OrdinalIgnoreCase));
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            var db = new AppDbContext(options);
            await db.Database.EnsureCreatedAsync();

            var actorId = Guid.NewGuid();
            var role = new AppRole(Guid.NewGuid(), UserRole.SuperAdmin, UserRole.SuperAdmin.ToString(), isSystemRole: true);
            var actor = new AppUser(actorId, "Bootstrap test actor", "bootstrap@example.test", "hash");
            actor.AssignRole(role);
            db.Add(actor);

            Worker? worker = null;
            if (includeWorker)
            {
                worker = new Worker(Guid.NewGuid(), "1001", "Existing worker projection", "zk-1001", attendanceDepartmentId: 41);
                db.Workers.Add(worker);
                if (currentSalary.HasValue)
                {
                    db.WorkerSalaryHistories.Add(new WorkerSalaryHistory(
                        Guid.NewGuid(), worker.Id, currentSalary.Value, "EGP", DateTime.UtcNow.AddDays(-1)));
                }
            }
            await db.SaveChangesAsync();

            IAuditEngine audit = throwingAudit ? new ThrowingAuditEngine() : new NoOpAuditEngine();
            return new BootstrapFixture(connection, db, new PilotMasterDataBootstrapService(db, new ImportNormalizationService(), audit), actorId, worker);
        }

        public PilotMasterDataBootstrapInput CreateInput(
            decimal salary,
            decimal? secondsForFirstStage = null,
            string employeeCode = "1001",
            CompensationMode? compensationMode = null)
        {
            return new PilotMasterDataBootstrapInput(
                BuildStagesWorkbook(secondsForFirstStage),
                BuildSalaryWorkbook(employeeCode, salary),
                ProductionWorkbookVerified: true,
                ExplicitCompensationMode: compensationMode);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Connection.DisposeAsync();
        }

        private static byte[] BuildStagesWorkbook(decimal? secondsForFirstStage)
        {
            using var workbook = new XLWorkbook();
            var sheet = workbook.AddWorksheet("Stages");
            sheet.Cell(1, 1).Value = "كود المرحلة";
            sheet.Cell(1, 2).Value = "المرحلة الرئيسية";
            sheet.Cell(1, 3).Value = "المرحلة الفرعية";
            sheet.Cell(1, 4).Value = "سعر القطعة";
            sheet.Cell(1, 5).Value = "ثواني القطعة";
            sheet.Cell(1, 6).Value = "Column1";
            sheet.Cell(1, 7).Value = "طريقة الاستخدام";
            for (var index = 1; index <= 67; index++)
            {
                var row = index + 1;
                if (index <= 3) sheet.Cell(row, 1).Value = $"STG{index:000}";
                sheet.Cell(row, 2).Value = "الخياطة";
                sheet.Cell(row, 3).Value = $"مرحلة / {index}";
                sheet.Cell(row, 4).Value = index;
                if (index == 1 && secondsForFirstStage.HasValue) sheet.Cell(row, 5).Value = secondsForFirstStage.Value;
                sheet.Cell(row, 6).Value = 500;
                sheet.Cell(row, 7).Value = "ignored";
            }
            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        private static byte[] BuildSalaryWorkbook(string employeeCode, decimal salary)
        {
            using var workbook = new XLWorkbook();
            var sheet = workbook.AddWorksheet("Salaries");
            sheet.Cell(1, 1).Value = "كود الموظف";
            sheet.Cell(1, 2).Value = "اسم الموظف";
            sheet.Cell(1, 3).Value = "القسم";
            sheet.Cell(1, 4).Value = "الراتب الاساسي";
            sheet.Cell(2, 1).Value = employeeCode;
            sheet.Cell(2, 2).Value = "Name deliberately ignored for identity";
            sheet.Cell(2, 3).Value = "قطاع اختبار";
            sheet.Cell(2, 4).Value = salary;
            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }
    }

    private sealed class NoOpAuditEngine : IAuditEngine
    {
        public Task<Result> RecordAsync(Guid actorUserId, AuditActionType actionType, string entityType, string entityId, object? before = null, object? after = null, string? requestMeta = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());
    }

    private sealed class ThrowingAuditEngine : IAuditEngine
    {
        public Task<Result> RecordAsync(Guid actorUserId, AuditActionType actionType, string entityType, string entityId, object? before = null, object? after = null, string? requestMeta = null, CancellationToken cancellationToken = default) =>
            Task.FromException<Result>(new InvalidOperationException("Audit write failed."));
    }
}

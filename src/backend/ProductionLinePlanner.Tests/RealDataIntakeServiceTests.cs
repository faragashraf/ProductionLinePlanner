using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.BusinessEngines;
using ProductionLinePlanner.Infrastructure.Importing;
using ProductionLinePlanner.Infrastructure.Data;
using ProductionLinePlanner.Tests.TestInfrastructure;

namespace ProductionLinePlanner.Tests;

public sealed class RealDataIntakeServiceTests
{
    [Fact]
    public async Task Preview_and_apply_keep_stage_quantity_separate_from_worker_allocation_and_are_idempotent()
    {
        await using var fixture = await IntakeFixture.CreateAsync(requiredStageCount: 1);
        var upload = fixture.CreateUpload(salary: 0m, seconds: null, allocationQuantity: 17m);

        var preview = await fixture.Service.PreviewAsync(upload);
        Assert.True(preview.CanApply);
        var stage = Assert.Single(preview.Stages);
        Assert.Equal("STG001", stage.StageCode);
        Assert.Null(stage.StandardSeconds);
        Assert.DoesNotContain(preview.Stages.SelectMany(x => x.Issues), x => x.Code is "طريقة الاستخدام" or "Column1");
        Assert.Null(Assert.Single(preview.Workers).IncomingSalary);

        var applied = await fixture.Service.ApplyAsync(upload, fixture.ActorId);
        Assert.False(applied.WasAlreadyApplied);
        Assert.Equal(1, applied.ProductionDaysCreated);
        Assert.Equal(1, applied.StageRecordsCreated);
        Assert.Equal(1, applied.WorkerAllocationsCreated);

        var record = await fixture.Db.Set<StageProductionRecord>().Include(x => x.WorkerAllocations).SingleAsync();
        Assert.Equal(769m, record.ProducedQuantity);
        Assert.Equal(769m, record.AcceptedQuantity);
        Assert.Equal(17m, Assert.Single(record.WorkerAllocations).InputQuantity);
        Assert.Equal(0m, Assert.Single(record.WorkerAllocations).EquivalentQuantity);
        Assert.Null((await fixture.Db.Set<ProductModelStage>().SingleAsync()).StandardSeconds);
        Assert.Equal("الخياطة", (await fixture.Db.Workers.SingleAsync()).LocalDepartmentName);
        Assert.Empty(await fixture.Db.Set<WorkerSalaryHistory>().Where(x => x.WorkerId == fixture.Worker.Id).ToArrayAsync());

        var rerun = await fixture.Service.ApplyAsync(upload, fixture.ActorId);
        Assert.True(rerun.WasAlreadyApplied);
        Assert.Single(await fixture.Db.Set<ProductionOrder>().ToArrayAsync());
        Assert.Single(await fixture.Db.Set<StageProductionRecord>().ToArrayAsync());
    }

    [Fact]
    public async Task Generated_stage_codes_start_at_stg004_and_missing_required_stage_blocks_daily_approval_until_resolved()
    {
        await using var fixture = await IntakeFixture.CreateAsync(requiredStageCount: 2);
        var upload = fixture.CreateUpload(salary: 100m, seconds: 22m, allocationQuantity: 5m, newUnmappedStageCount: 2);

        var preview = await fixture.Service.PreviewAsync(upload);
        Assert.Equal(["STG004", "STG005"], preview.Stages.Where(x => x.Action == "blocked").Select(x => x.StageCode).ToArray());
        var rerunPreview = await fixture.Service.PreviewAsync(upload);
        Assert.Equal(preview.Stages.Select(x => x.StageCode), rerunPreview.Stages.Select(x => x.StageCode));
        Assert.False(preview.CanApply); // a new stage has no existing compensation configuration to invent.

        var safeUpload = fixture.CreateUpload(salary: 100m, seconds: 22m, allocationQuantity: 5m);
        var safePreview = await fixture.Service.PreviewAsync(safeUpload);
        Assert.True(safePreview.CanApply);
        await fixture.Service.ApplyAsync(safeUpload, fixture.ActorId);
        var order = await fixture.Db.Set<ProductionOrder>().SingleAsync();
        var review = await fixture.Service.GetProductionDayReviewAsync(order.Id);
        Assert.Contains(review.Issues, x => x.Status == "Open" && x.StageCode == "STG002");
        await Assert.ThrowsAsync<ProductionConflictException>(() => fixture.Service.ApproveProductionDayAsync(order.Id, fixture.ActorId));

        var missing = review.Issues.Single(x => x.Status == "Open");
        var resolved = await fixture.Service.MarkStageNotOperatedAsync(order.Id, missing.ProductModelStageId, "لم تعمل المرحلة في هذا اليوم", fixture.ActorId);
        Assert.Contains(resolved.Issues, x => x.Status == "Resolved");
        var participant = Assert.Single(resolved.Allocations);
        var withOverride = await fixture.Service.SetParticipantOverrideAsync(order.Id, participant.StageProductionRecordId, participant.WorkerId, "تفويض مدير موثق", fixture.ActorId);
        Assert.Equal("تفويض مدير موثق", Assert.Single(withOverride.Allocations).ManualOverrideReason);
        var approved = await fixture.Service.ApproveProductionDayAsync(order.Id, fixture.ActorId);
        Assert.Equal("Approved", approved.Status);

        var snapshotPrice = (await fixture.Db.Set<StageProductionRecord>().SingleAsync()).SnapshotPiecePrice;
        var mapping = await fixture.Db.Set<ProductModelStage>().OrderBy(x => x.StageOrder).FirstAsync();
        mapping.Update(mapping.SubStageId, mapping.StageOrder, 999m, mapping.StandardSeconds, mapping.CompensationMode, mapping.IsRequired, mapping.IsActive, mapping.EffectiveFrom);
        await fixture.Db.SaveChangesAsync();
        Assert.Equal(snapshotPrice, (await fixture.Db.Set<StageProductionRecord>().SingleAsync()).SnapshotPiecePrice);
        Assert.Equal("تفويض مدير موثق", Assert.Single((await fixture.Db.Set<StageProductionRecord>().Include(x => x.WorkerAllocations).SingleAsync()).WorkerAllocations).ManualOverrideReason);
    }

    [Fact]
    public async Task Preview_uses_production_date_for_attendance_not_the_entry_date()
    {
        await using var fixture = await IntakeFixture.CreateAsync(requiredStageCount: 1);
        fixture.Db.RemoveRange(await fixture.Db.AttendanceRecords.ToArrayAsync());
        fixture.Db.Add(new AttendanceRecord(Guid.NewGuid(), fixture.Worker.Id, new DateTime(2026, 7, 14, 8, 0, 0, DateTimeKind.Utc), AttendanceStatus.Present, "test", sourceRawId: "1:next-day", attendanceUserId: "1"));
        await fixture.Db.SaveChangesAsync();

        var preview = await fixture.Service.PreviewAsync(fixture.CreateUpload(salary: 100m, seconds: 18m, allocationQuantity: 5m));

        Assert.False(preview.CanApply);
        Assert.Contains(preview.ProductionStages.SelectMany(x => x.Issues), issue => issue.Code == "MissingAttendance");
    }

    [Fact]
    public async Task Complete_67_stage_day_passes_completeness_validation_without_worker_quantity_multiplication()
    {
        await using var fixture = await IntakeFixture.CreateAsync(requiredStageCount: 67);

        var preview = await fixture.Service.PreviewAsync(fixture.CreateCompleteUpload());

        Assert.True(preview.CanApply);
        Assert.Equal(67, preview.Stages.Count);
        Assert.Empty(preview.MissingProductStages);
        await fixture.Service.ApplyAsync(fixture.CreateCompleteUpload(), fixture.ActorId);
        var review = await fixture.Service.GetProductionDayReviewAsync((await fixture.Db.Set<ProductionOrder>().SingleAsync()).Id);
        Assert.Equal(67, review.StageRecordCount);
        Assert.Empty(review.Issues);
    }

    private sealed class IntakeFixture : IAsyncDisposable
    {
        private IntakeFixture(AppDbContext db, RealDataIntakeService service, Guid actorId, Worker worker) { Db = db; Service = service; ActorId = actorId; Worker = worker; }
        public AppDbContext Db { get; }
        public RealDataIntakeService Service { get; }
        public Guid ActorId { get; }
        public Worker Worker { get; }

        public static async Task<IntakeFixture> CreateAsync(int requiredStageCount)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"intake-{Guid.NewGuid()}").Options;
            var db = new AppDbContext(options);
            await db.Database.EnsureCreatedAsync();
            var actor = Guid.NewGuid();
            var factory = new Factory(Guid.NewGuid(), "المصنع الرئيسي", "MAIN");
            var line = new ProductionLine(Guid.NewGuid(), factory.Id, "خط الخياطه", 1, "SEW");
            var main = new MainStage(Guid.NewGuid(), line.Id, "الخياطة", 1);
            var firstStage = new SubStage(Guid.NewGuid(), main.Id, "إستلام / 1", "STG001", 0, 1);
            var product = new ProductModel(Guid.NewGuid(), "GEROMAN", "جرومان");
            var firstMapping = new ProductModelStage(Guid.NewGuid(), product.Id, firstStage.Id, 1, 1.25m, 18m, CompensationMode.FullRatePerWorker);
            var worker = new Worker(Guid.NewGuid(), "1001", "عامل اختبار", "1");
            db.AddRange(factory, line, main, firstStage, product, firstMapping, worker);
            for (var number = 2; number <= requiredStageCount; number++)
            {
                var stageName = number == 2 ? "تشطيب / 1" : $"مرحلة / {number}";
                var stage = new SubStage(Guid.NewGuid(), main.Id, stageName, $"STG{number:000}", 0, number);
                db.AddRange(stage, new ProductModelStage(Guid.NewGuid(), product.Id, stage.Id, number, 1m, null, CompensationMode.FullRatePerWorker));
            }
            await db.SaveChangesAsync();
            db.Add(new WorkerDefaultAssignment(Guid.NewGuid(), worker.Id, firstStage.Id, actor, new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), "Pilot"));
            db.Add(new AttendanceRecord(Guid.NewGuid(), worker.Id, new DateTime(2026, 7, 13, 8, 0, 0, DateTimeKind.Utc), AttendanceStatus.Present, "test", sourceRawId: "1:pilot", attendanceUserId: "1"));
            await db.SaveChangesAsync();
            var audit = new AuditEngine(db);
            return new IntakeFixture(db, new RealDataIntakeService(db, new ImportNormalizationService(), new AssignmentEngine(db, audit), audit, TestCairoTimeZoneProvider.Instance), actor, worker);
        }

        public RealDataIntakeUpload CreateUpload(decimal salary, decimal? seconds, decimal allocationQuantity, int newUnmappedStageCount = 0)
        {
            var stages = BuildWorkbook(sheet =>
            {
                sheet.Cell(1, 1).Value = "المرحلة الرئيسية"; sheet.Cell(1, 2).Value = "المرحلة الفرعية"; sheet.Cell(1, 3).Value = "سعر القطعة"; sheet.Cell(1, 4).Value = "ثواني القطعة"; sheet.Cell(1, 5).Value = "طريقة الاستخدام"; sheet.Cell(1, 6).Value = "Column1";
                sheet.Cell(2, 1).Value = "الخياطة"; sheet.Cell(2, 2).Value = "إستلام / 1"; sheet.Cell(2, 3).Value = 1.25m; if (seconds.HasValue) sheet.Cell(2, 4).Value = seconds.Value; sheet.Cell(2, 5).Value = "ignored"; sheet.Cell(2, 6).Value = 500;
                for (var index = 0; index < newUnmappedStageCount; index++)
                {
                    var row = 3 + index;
                    sheet.Cell(row, 1).Value = "الخياطة"; sheet.Cell(row, 2).Value = $"جديد / {index + 1}"; sheet.Cell(row, 3).Value = 2m; sheet.Cell(row, 5).Value = "ignored";
                }
            });
            var salaries = BuildWorkbook(sheet =>
            {
                sheet.Cell(1, 1).Value = "كود الموظف"; sheet.Cell(1, 2).Value = "اسم الموظف"; sheet.Cell(1, 3).Value = "القسم"; sheet.Cell(1, 4).Value = "الراتب الاساسي";
                sheet.Cell(2, 1).Value = "1001"; sheet.Cell(2, 2).Value = "اسم لا يستخدم للمطابقة"; sheet.Cell(2, 3).Value = "الخياطة"; sheet.Cell(2, 4).Value = salary;
            });
            var production = BuildWorkbook(sheet =>
            {
                sheet.Cell(1, 1).Value = "تاريخ الانتاج"; sheet.Cell(1, 2).Value = "العامل"; sheet.Cell(1, 3).Value = "المرحلة"; sheet.Cell(1, 4).Value = "كمية العامل"; sheet.Cell(1, 5).Value = "سعر القطعة"; sheet.Cell(1, 6).Value = "ثواني القطعة";
                sheet.Cell(2, 1).Value = "2026-07-13"; sheet.Cell(2, 2).Value = "1001 - تهجئة مختلفة"; sheet.Cell(2, 3).Value = "الخياطة - إستلام / 1"; sheet.Cell(2, 4).Value = allocationQuantity; sheet.Cell(2, 5).FormulaA1 = "=26/500"; sheet.Cell(2, 6).FormulaA1 = "=500";
            });
            return new RealDataIntakeUpload("المصنع الرئيسي", "خط الخياطه", "جرومان", new IntakeWorkbookFile("المراحل.xlsx", stages), new IntakeWorkbookFile("الراتب.xlsx", salaries), new IntakeWorkbookFile("الإنتاج.xlsx", production), [new ProductionDayQuantityInput(new DateOnly(2026, 7, 13), 769m)]);
        }

        public RealDataIntakeUpload CreateCompleteUpload()
        {
            var stageDefinitions = Db.SubStages.Include(x => x.MainStage).OrderBy(x => x.DefaultOrder)
                .Select(x => new { Main = x.MainStage!.Name, x.Name, x.Code, Price = x.Code == "STG001" ? 1.25m : 1m, Seconds = x.Code == "STG001" ? 18m : (decimal?)null })
                .ToArray();
            var stages = BuildWorkbook(sheet =>
            {
                sheet.Cell(1, 1).Value = "المرحلة الرئيسية"; sheet.Cell(1, 2).Value = "المرحلة الفرعية"; sheet.Cell(1, 3).Value = "كود المرحلة"; sheet.Cell(1, 4).Value = "سعر القطعة"; sheet.Cell(1, 5).Value = "ثواني القطعة";
                foreach (var (stage, index) in stageDefinitions.Select((stage, index) => (stage, index)))
                {
                    var row = index + 2;
                    sheet.Cell(row, 1).Value = stage.Main; sheet.Cell(row, 2).Value = stage.Name; sheet.Cell(row, 3).Value = stage.Code; sheet.Cell(row, 4).Value = stage.Price;
                    if (stage.Seconds.HasValue) sheet.Cell(row, 5).Value = stage.Seconds.Value;
                }
            });
            var salaries = BuildWorkbook(sheet =>
            {
                sheet.Cell(1, 1).Value = "كود الموظف"; sheet.Cell(1, 2).Value = "اسم الموظف"; sheet.Cell(1, 3).Value = "القسم"; sheet.Cell(1, 4).Value = "الراتب الاساسي";
                sheet.Cell(2, 1).Value = "1001"; sheet.Cell(2, 2).Value = "اسم لا يستخدم للمطابقة"; sheet.Cell(2, 3).Value = "الخياطة"; sheet.Cell(2, 4).Value = 100m;
            });
            var production = BuildWorkbook(sheet =>
            {
                sheet.Cell(1, 1).Value = "تاريخ الانتاج"; sheet.Cell(1, 2).Value = "العامل"; sheet.Cell(1, 3).Value = "المرحلة"; sheet.Cell(1, 4).Value = "كمية العامل";
                foreach (var (stage, index) in stageDefinitions.Select((stage, index) => (stage, index)))
                {
                    var row = index + 2;
                    sheet.Cell(row, 1).Value = "2026-07-13"; sheet.Cell(row, 2).Value = "1001 - عامل اختبار"; sheet.Cell(row, 3).Value = $"{stage.Main} - {stage.Name}"; sheet.Cell(row, 4).Value = 5m;
                }
            });
            return new RealDataIntakeUpload("المصنع الرئيسي", "خط الخياطه", "جرومان", new IntakeWorkbookFile("المراحل.xlsx", stages), new IntakeWorkbookFile("الراتب.xlsx", salaries), new IntakeWorkbookFile("الإنتاج.xlsx", production), [new ProductionDayQuantityInput(new DateOnly(2026, 7, 13), 769m)]);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();

        private static byte[] BuildWorkbook(Action<IXLWorksheet> build)
        {
            using var workbook = new XLWorkbook();
            var sheet = workbook.AddWorksheet("Data");
            build(sheet);
            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }
    }
}

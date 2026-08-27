using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Application.Requests;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.BusinessEngines;
using ProductionLinePlanner.Infrastructure.Data;
using ProductionLinePlanner.Tests.TestInfrastructure;

namespace ProductionLinePlanner.Tests;

public sealed class ProductionCostRecordingServiceTests
{
    [Fact]
    public async Task Shared_percentage_keeps_production_at_500_and_splits_earnings()
    {
        await using var fixture = await Fixture.CreateAsync("SharedPercentage", 0.50m, 17m);
        var record = await fixture.CreateAndApproveAsync(500m, [fixture.Allocation(fixture.WorkerA.Id, 50m), fixture.Allocation(fixture.WorkerB.Id, 50m)]);
        var report = await fixture.Service.DailyReportAsync(fixture.Today, fixture.Today, null, null, null, default);

        Assert.Equal(500m, record.ProducedQuantity);
        Assert.Equal(250m, record.Workers.First().EquivalentQuantity);
        Assert.Equal(125m, record.Workers.First().CalculatedEarning);
        Assert.Equal(250m, record.TotalWorkerEarnings);
        Assert.Single(report);
        Assert.Equal(500m, report.Single().ProducedQuantity);
        Assert.Equal(250m, report.Single().StageCost);
        Assert.Equal(250m, report.Single().Workers.Sum(x => x.CalculatedEarning));
    }

    [Fact]
    public async Task Full_rate_per_worker_pays_each_worker_without_inflating_production()
    {
        await using var fixture = await Fixture.CreateAsync("FullRatePerWorker", 0.50m, 17m);
        var record = await fixture.CreateAndApproveAsync(500m, [fixture.Allocation(fixture.WorkerA.Id), fixture.Allocation(fixture.WorkerB.Id)]);

        Assert.Equal(500m, record.ProducedQuantity);
        Assert.All(record.Workers, worker => { Assert.Equal(0m, worker.EquivalentQuantity); Assert.Equal(250m, worker.CalculatedEarning); });
        Assert.Equal(500m, record.TotalWorkerEarnings);
    }

    [Fact]
    public async Task Fixed_amount_does_not_multiply_by_quantity()
    {
        await using var fixture = await Fixture.CreateAsync("FixedAmount", 0.50m, 17m);
        var record = await fixture.CreateAndApproveAsync(500m, [fixture.Allocation(fixture.WorkerA.Id, fixedAmount: 37m), fixture.Allocation(fixture.WorkerB.Id, fixedAmount: 61m)]);

        Assert.Equal(500m, record.ProducedQuantity);
        Assert.Equal(98m, record.TotalWorkerEarnings);
        Assert.Equal([37m, 61m], record.Workers.Select(x => x.CalculatedEarning).Order().ToArray());
    }

    [Fact]
    public async Task One_worker_preview_draft_and_approved_total_equal_the_same_rounded_allocation_amount()
    {
        await using var fixture = await Fixture.CreateAsync("SharedPercentage", 0.38m, 17m);
        var request = new CreateStageProductionRecordRequest(fixture.Order.Id, fixture.Stage.Id, fixture.Today, 500m, 500m, 0m, Guid.NewGuid(), null, [fixture.Allocation(fixture.WorkerA.Id, 100m)]);

        var preview = await fixture.Service.CalculatePreviewAsync(request, fixture.ActorId, default);
        var draft = await fixture.Service.CreateDraftAsync(request with { ClientRequestId = Guid.NewGuid() }, fixture.ActorId, default);
        var approved = await fixture.Service.ApproveAsync(draft.Id, draft.ConcurrencyToken, fixture.ActorId, default);

        Assert.Equal(500m, preview.Workers.Single().EquivalentQuantity);
        Assert.Equal(190m, preview.Workers.Single().CalculatedEarning);
        Assert.Equal(190m, preview.TotalWorkerEarnings);
        Assert.Equal(draft.Workers.Sum(worker => worker.CalculatedEarning), draft.TotalWorkerEarnings);
        Assert.Equal(approved.Workers.Sum(worker => worker.CalculatedEarning), approved.TotalWorkerEarnings);
    }

    [Fact]
    public async Task Saved_totals_sum_exact_rounded_allocations_for_shared_full_rate_and_fixed_amount_modes()
    {
        await using var shared = await Fixture.CreateAsync("SharedPercentage", 0.3333m, 17m);
        var sharedDraft = await shared.CreateDraftAsync(1m, 1m, 0m, [shared.Allocation(shared.WorkerA.Id, 33.3333m), shared.Allocation(shared.WorkerB.Id, 66.6667m)]);
        Assert.Equal(sharedDraft.Workers.Sum(worker => worker.CalculatedEarning), sharedDraft.TotalWorkerEarnings);
        Assert.Equal(0.3333m, sharedDraft.TotalWorkerEarnings);

        await using var fullRate = await Fixture.CreateAsync("FullRatePerWorker", 0.38m, 17m);
        var fullRateDraft = await fullRate.CreateDraftAsync(500m, 500m, 0m, [fullRate.Allocation(fullRate.WorkerA.Id), fullRate.Allocation(fullRate.WorkerB.Id)]);
        Assert.Equal(fullRateDraft.Workers.Sum(worker => worker.CalculatedEarning), fullRateDraft.TotalWorkerEarnings);
        Assert.Equal(380m, fullRateDraft.TotalWorkerEarnings);

        await using var fixedAmount = await Fixture.CreateAsync("FixedAmount", 0.38m, 17m);
        var fixedDraft = await fixedAmount.CreateDraftAsync(500m, 500m, 0m, [fixedAmount.Allocation(fixedAmount.WorkerA.Id, fixedAmount: 37.12345m), fixedAmount.Allocation(fixedAmount.WorkerB.Id, fixedAmount: 61.23456m)]);
        Assert.Equal(fixedDraft.Workers.Sum(worker => worker.CalculatedEarning), fixedDraft.TotalWorkerEarnings);
        Assert.Equal(98.3581m, fixedDraft.TotalWorkerEarnings);
    }

    [Fact]
    public async Task Approval_rejects_a_persisted_total_that_does_not_equal_its_saved_allocations()
    {
        await using var fixture = await Fixture.CreateAsync("SharedPercentage", 0.38m, 17m);
        var draft = await fixture.CreateDraftAsync(500m, 500m, 0m, [fixture.Allocation(fixture.WorkerA.Id, 100m)]);
        var persisted = await fixture.Db.Set<StageProductionRecord>().SingleAsync(record => record.Id == draft.Id);
        fixture.Db.Entry(persisted).Property(nameof(StageProductionRecord.TotalWorkerEarnings)).CurrentValue = 0m;
        await fixture.Db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<ProductionConflictException>(() => fixture.Service.ApproveAsync(draft.Id, draft.ConcurrencyToken, fixture.ActorId, default));

        Assert.Contains("إجمالي المستحقات", exception.Message);
        var unchanged = await fixture.Service.GetRecordAsync(draft.Id, default);
        Assert.Equal("Draft", unchanged.Status);
        Assert.Equal(0m, unchanged.TotalWorkerEarnings);
        Assert.Equal(190m, unchanged.Workers.Single().CalculatedEarning);
        Assert.Empty(await fixture.Service.DailyReportAsync(fixture.Today, fixture.Today, null, null, null, default));
    }

    [Theory]
    [InlineData(40, 40, 30)]
    [InlineData(40, 40, 0)]
    public async Task Shared_percentage_rejects_totals_other_than_100(decimal a, decimal b, decimal c)
    {
        await using var fixture = await Fixture.CreateAsync("SharedPercentage", 0.50m, 17m);
        var allocations = new[] { fixture.Allocation(fixture.WorkerA.Id, a), fixture.Allocation(fixture.WorkerB.Id, b), fixture.Allocation(fixture.WorkerC.Id, c) };
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => fixture.CreateDraftAsync(500m, 500m, 0m, allocations));
    }

    [Fact]
    public async Task Validation_rejects_duplicate_left_employment_invalid_quantities_and_wrong_mode_fields()
    {
        await using var fixture = await Fixture.CreateAsync("SharedPercentage", 0.50m, 17m);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => fixture.CreateDraftAsync(500m, 500m, 0m, [fixture.Allocation(fixture.WorkerA.Id, 50m), fixture.Allocation(fixture.WorkerA.Id, 50m)]));
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => fixture.CreateDraftAsync(500m, 500m, 0m, [fixture.Allocation(fixture.LeftWorker.Id, 100m)]));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.CreateDraftAsync(500m, 490m, 20m, [fixture.Allocation(fixture.WorkerA.Id, 100m)]));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.CreateDraftAsync(-1m, 0m, 0m, [fixture.Allocation(fixture.WorkerA.Id, 100m)]));

        await using var fullRateFixture = await Fixture.CreateAsync("FullRatePerWorker", 0.50m, 17m);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => fullRateFixture.CreateDraftAsync(500m, 500m, 0m, [fullRateFixture.Allocation(fullRateFixture.WorkerA.Id, 50m)]));
        await using var fixedFixture = await Fixture.CreateAsync("FixedAmount", 0.50m, 17m);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => fixedFixture.CreateDraftAsync(500m, 500m, 0m, [fixedFixture.Allocation(fixedFixture.WorkerA.Id)]));
    }

    [Fact]
    public async Task Snapshot_uses_model_specific_rate_and_is_unchanged_after_configuration_edit()
    {
        await using var fixture = await Fixture.CreateAsync("FullRatePerWorker", 0.50m, 17m);
        var first = await fixture.CreateDraftAsync(500m, 500m, 0m, [fixture.Allocation(fixture.WorkerA.Id)]);
        fixture.Stage.Update(fixture.SubStage.Id, 1, 0.70m, 25m, fixture.Stage.CompensationMode, true, true, null);
        await fixture.Db.SaveChangesAsync();
        var second = await fixture.CreateDraftAsync(500m, 500m, 0m, [fixture.Allocation(fixture.WorkerA.Id)]);

        Assert.Equal(0.50m, first.PiecePrice); Assert.Equal(17m, first.StandardSeconds);
        Assert.Equal(0.70m, second.PiecePrice); Assert.Equal(25m, second.StandardSeconds);
        var persistedFirst = await fixture.Service.GetRecordAsync(first.Id, default);
        Assert.Equal(0.50m, persistedFirst.PiecePrice); Assert.Equal(17m, persistedFirst.StandardSeconds);
    }

    [Fact]
    public async Task Calculation_preview_uses_only_current_request_participants_and_never_rewrites_an_approved_snapshot()
    {
        await using var fixture = await Fixture.CreateAsync("SharedPercentage", 0.50m, 17m);
        var firstPreview = await fixture.Service.CalculatePreviewAsync(
            new CreateStageProductionRecordRequest(fixture.Order.Id, fixture.Stage.Id, fixture.Today, 10m, 10m, 0m, Guid.NewGuid(), null, [fixture.Allocation(fixture.WorkerA.Id, 100m)]),
            fixture.ActorId,
            default);
        var approved = await fixture.CreateAndApproveAsync(10m, [fixture.Allocation(fixture.WorkerA.Id, 100m)]);
        var replacementPreview = await fixture.Service.CalculatePreviewAsync(
            new CreateStageProductionRecordRequest(fixture.Order.Id, fixture.Stage.Id, fixture.Today, 10m, 10m, 0m, Guid.NewGuid(), null, [fixture.Allocation(fixture.WorkerB.Id, 100m)]),
            fixture.ActorId,
            default);

        Assert.Equal([fixture.WorkerA.Id], firstPreview.Workers.Select(x => x.WorkerId));
        Assert.Equal([fixture.WorkerB.Id], replacementPreview.Workers.Select(x => x.WorkerId));
        Assert.Equal(1, await fixture.Db.Set<StageProductionRecord>().CountAsync());

        var historical = await fixture.Service.GetRecordAsync(approved.Id, default);
        Assert.Equal([fixture.WorkerA.Id], historical.Workers.Select(x => x.WorkerId));
    }

    [Fact]
    public async Task Lifecycle_locks_approved_excludes_cancelled_and_rejects_closed_orders()
    {
        await using var fixture = await Fixture.CreateAsync("SharedPercentage", 0.50m, 17m);
        var draft = await fixture.CreateDraftAsync(500m, 500m, 0m, [fixture.Allocation(fixture.WorkerA.Id, 100m)]);
        var approved = await fixture.Service.ApproveAsync(draft.Id, draft.ConcurrencyToken, fixture.ActorId, default);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => fixture.Service.UpdateDraftAsync(draft.Id, new UpdateStageProductionRecordRequest(fixture.Today, 400m, 400m, 0m, approved.ConcurrencyToken, null, [fixture.Allocation(fixture.WorkerA.Id, 100m)]), fixture.ActorId, default));
        await fixture.Service.CancelProductionApprovalAsync(draft.Id, approved.ConcurrencyToken, "تصحيح اعتماد الإنتاج", fixture.ActorId, default);
        Assert.Empty(await fixture.Service.DailyReportAsync(fixture.Today, fixture.Today, null, null, null, default));
        await fixture.Service.TransitionOrderAsync(fixture.Order.Id, ProductionOrderStatus.Cancelled, fixture.ActorId, default);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => fixture.CreateDraftAsync(1m, 1m, 0m, [fixture.Allocation(fixture.WorkerA.Id, 100m)]));
    }

    [Fact]
    public async Task Audit_records_business_events_and_audit_failure_blocks_operation()
    {
        await using var fixture = await Fixture.CreateAsync("SharedPercentage", 0.50m, 17m, useRealAudit: true);
        var draft = await fixture.CreateDraftAsync(500m, 500m, 0m, [fixture.Allocation(fixture.WorkerA.Id, 100m)]);
        var approved = await fixture.Service.ApproveAsync(draft.Id, draft.ConcurrencyToken, fixture.ActorId, default);
        await fixture.Service.CancelProductionApprovalAsync(draft.Id, approved.ConcurrencyToken, "تصحيح اعتماد الإنتاج", fixture.ActorId, default);
        var logs = await fixture.Db.AuditLogs.ToListAsync();
        Assert.Contains(logs, x => x.EntityType == "StageProductionRecord" && x.ActionType == AuditActionType.Create);
        Assert.Contains(logs, x => x.EntityType == "StageProductionWorkerAllocation");
        Assert.Contains(logs, x => x.EntityType == "StageProductionRecord" && x.ActionType == AuditActionType.Cancel);
        Assert.All(logs, x => Assert.Equal(fixture.ActorId, x.ActorUserId));

        await using var failed = await Fixture.CreateAsync("SharedPercentage", 0.50m, 17m);
        var failingService = failed.CreateService(new FailingAuditEngine());
        await Assert.ThrowsAsync<InvalidOperationException>(() => failingService.CreateOrderAsync(new CreateProductionOrderRequest("AUDIT-FAIL", failed.Model.Id, failed.Line.Id, failed.Today, 1m, null), failed.ActorId, default));
    }

    [Fact]
    public async Task Production_approval_cancellation_requires_a_reason_preserves_snapshots_and_writes_auditable_metadata()
    {
        await using var fixture = await Fixture.CreateAsync("SharedPercentage", 0.38m, 17m, useRealAudit: true);
        var draft = await fixture.CreateDraftAsync(500m, 500m, 0m, [fixture.Allocation(fixture.WorkerA.Id, 100m)]);
        var approved = await fixture.Service.ApproveAsync(draft.Id, draft.ConcurrencyToken, fixture.ActorId, default);

        await Assert.ThrowsAsync<ProductionConflictException>(() => fixture.Service.CancelProductionApprovalAsync(approved.Id, approved.ConcurrencyToken, " ", fixture.ActorId, default));
        var cancelled = await fixture.Service.CancelProductionApprovalAsync(approved.Id, approved.ConcurrencyToken, "إدخال كمية غير صحيحة", fixture.ActorId, default);

        Assert.Equal("Cancelled", cancelled.Status);
        Assert.Equal(approved.TotalWorkerEarnings, cancelled.TotalWorkerEarnings);
        Assert.Equal(approved.Workers.Select(worker => worker.CalculatedEarning), cancelled.Workers.Select(worker => worker.CalculatedEarning));
        Assert.Equal(approved.ApprovedByUserId, cancelled.ApprovedByUserId);
        Assert.Equal(approved.ApprovedAtUtc, cancelled.ApprovedAtUtc);
        Assert.Equal(fixture.ActorId, cancelled.ApprovalCancelledByUserId);
        Assert.NotNull(cancelled.ApprovalCancelledAtUtc);
        Assert.Equal("إدخال كمية غير صحيحة", cancelled.ApprovalCancellationReason);
        Assert.Empty(await fixture.Service.DailyReportAsync(fixture.Today, fixture.Today, null, null, null, default));
        await Assert.ThrowsAsync<ProductionConflictException>(() => fixture.Service.CancelProductionApprovalAsync(cancelled.Id, cancelled.ConcurrencyToken, "تكرار", fixture.ActorId, default));
        await Assert.ThrowsAsync<ProductionConflictException>(() => fixture.Service.ApproveAsync(cancelled.Id, cancelled.ConcurrencyToken, fixture.ActorId, default));

        var audit = (await fixture.Db.AuditLogs.ToListAsync()).Single(log => log.EntityType == "StageProductionRecord" && log.ActionType == AuditActionType.Cancel);
        Assert.Contains("ApprovalCancellationReason", audit.EntityAfterJson);
        using var auditDocument = JsonDocument.Parse(audit.EntityAfterJson!);
        Assert.Equal("إدخال كمية غير صحيحة", auditDocument.RootElement.GetProperty("ApprovalCancellationReason").GetString());
        Assert.Contains("ApprovedAtUtc", audit.EntityAfterJson);
    }

    [Fact]
    public async Task Approved_quantity_is_capped_idempotent_and_cancelled_records_release_capacity()
    {
        await using var fixture = await Fixture.CreateAsync("SharedPercentage", 0.50m, 17m);
        var first = await fixture.CreateDraftAsync(500m, 500m, 0m, [fixture.Allocation(fixture.WorkerA.Id, 100m)]);
        var approved = await fixture.Service.ApproveAsync(first.Id, first.ConcurrencyToken, fixture.ActorId, default);
        var repeated = await fixture.Service.ApproveAsync(first.Id, approved.ConcurrencyToken, fixture.ActorId, default);
        Assert.Equal(approved.Id, repeated.Id);
        var extra = await fixture.CreateDraftAsync(1m, 1m, 0m, [fixture.Allocation(fixture.WorkerA.Id, 100m)]);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => fixture.Service.ApproveAsync(extra.Id, extra.ConcurrencyToken, fixture.ActorId, default));
        var cancelled = await fixture.Service.CancelProductionApprovalAsync(first.Id, approved.ConcurrencyToken, "تصحيح اعتماد الإنتاج", fixture.ActorId, default);
        Assert.Equal("Cancelled", cancelled.Status);
        var accepted = await fixture.Service.ApproveAsync(extra.Id, extra.ConcurrencyToken, fixture.ActorId, default);
        Assert.Equal("Approved", accepted.Status);
    }

    [Fact]
    public async Task Daily_operations_expand_one_line_quantity_once_per_stage_and_save_an_idempotent_draft()
    {
        await using var fixture = await Fixture.CreateAsync("SharedPercentage", 0.50m, 17m, useRealAudit: true);
        var productionDate = fixture.Today.AddDays(1);
        var operations = await fixture.Service.LoadDailyOperationsAsync(fixture.Factory.Id, fixture.Line.Id, fixture.Model.Id, productionDate, default);
        var stage = Assert.Single(operations.Stages);

        Assert.Equal(3, stage.Workers.Count);
        Assert.Equal(100m, stage.Workers.Sum(worker => worker.SuggestedPercentage));
        Assert.Equal([33.3333m, 33.3333m, 33.3334m], stage.Workers.Select(worker => worker.SuggestedPercentage!.Value).Order().ToArray());

        var request = new DailyProductionOperationRequest(
            fixture.Factory.Id,
            fixture.Line.Id,
            fixture.Model.Id,
            productionDate,
            500m,
            Guid.NewGuid(),
            "daily pilot",
            null,
            [new DailyProductionStageRequest(stage.ProductModelStageId, stage.Workers.Select(worker => new WorkerAllocationRequest(worker.WorkerId, worker.SuggestedPercentage, null, null)).ToArray())]);

        var preview = await fixture.Service.PreviewDailyOperationsAsync(request, fixture.ActorId, default);
        Assert.Single(preview.Stages);
        Assert.Equal(500m, preview.Stages.Single().StageQuantity);
        Assert.Equal(preview.TotalWorkerEntitlements, preview.Stages.Single().Workers.Sum(worker => worker.CalculatedEarning));

        var saved = await fixture.Service.CreateDailyDraftAsync(request with { PreviewToken = preview.PreviewToken }, fixture.ActorId, default);
        var retry = await fixture.Service.CreateDailyDraftAsync(request with { PreviewToken = preview.PreviewToken }, fixture.ActorId, default);

        Assert.False(saved.WasAlreadySaved);
        Assert.True(retry.WasAlreadySaved);
        Assert.Equal(saved.ProductionOrderId, retry.ProductionOrderId);
        Assert.Single(saved.Stages);
        Assert.Equal(500m, saved.Stages.Single().ProducedQuantity);
        Assert.Equal(500m, saved.Stages.Single().AcceptedQuantity);
        Assert.Equal(3, saved.Stages.Single().Workers.Count);
        Assert.Equal(saved.Stages.Single().TotalWorkerEarnings, saved.Stages.Single().Workers.Sum(worker => worker.CalculatedEarning));
        Assert.Equal(1, await fixture.Db.Set<StageProductionRecord>().CountAsync(record => record.ProductionDate == productionDate));
        Assert.NotEqual(saved.ProductionDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), saved.RecordedAtUtc);

        var reloaded = await fixture.Service.LoadDailyOperationsAsync(fixture.Factory.Id, fixture.Line.Id, fixture.Model.Id, productionDate, default);
        Assert.NotNull(reloaded.ExistingDraft);
        Assert.Equal(saved.ProductionOrderId, reloaded.ExistingDraft!.ProductionOrderId);
        Assert.Equal(
            Assert.Single(reloaded.Stages).Workers.Count,
            reloaded.Stages.Single().Workers.Select(worker => worker.WorkerId).Distinct().Count());
    }

    [Fact]
    public async Task Daily_draft_can_be_approved_cancelled_saved_and_approved_again_with_each_authoritative_token()
    {
        await using var fixture = await Fixture.CreateAsync("SharedPercentage", 0.50m, 17m, useRealAudit: true);
        var productionDate = fixture.Today.AddDays(2);
        var (request, created) = await CreateDailyDraftAsync(fixture, productionDate);
        var createdStage = Assert.Single(created.Stages);

        var firstApproval = await fixture.Service.ApproveDailyOperationAsync(
            created.ProductionOrderId,
            ApprovalRequest(created),
            fixture.ActorId,
            default);
        Assert.Equal("Completed", firstApproval.OrderStatus);

        var approved = (await fixture.Service.LoadDailyOperationsAsync(
            fixture.Factory.Id, fixture.Line.Id, fixture.Model.Id, productionDate, default)).ExistingDraft;
        Assert.NotNull(approved);
        var approvedStage = Assert.Single(approved.Stages);
        Assert.Equal("Approved", approvedStage.Status);
        Assert.NotEqual(created.ConcurrencyToken, approved.ConcurrencyToken);
        Assert.NotEqual(createdStage.ConcurrencyToken, approvedStage.ConcurrencyToken);

        var cancelled = await fixture.Service.CancelDailyOperationApprovalAsync(
            approved.ProductionOrderId,
            new DailyProductionApprovalCancellationRequest("تصحيح تشغيل اليوم", ApprovalRequest(approved).StageApprovals),
            fixture.ActorId,
            default);
        var cancelledStage = Assert.Single(cancelled.Stages);
        Assert.Equal("Draft", cancelled.OrderStatus);
        Assert.Equal("Cancelled", cancelledStage.Status);
        Assert.NotEqual(approved.ConcurrencyToken, cancelled.ConcurrencyToken);
        Assert.NotEqual(approvedStage.ConcurrencyToken, cancelledStage.ConcurrencyToken);

        var updatePreview = await fixture.Service.PreviewDailyOperationsAsync(request, fixture.ActorId, default);
        var saved = await fixture.Service.UpdateDailyDraftAsync(
            cancelled.ProductionOrderId,
            UpdateRequest(request, cancelled, updatePreview.PreviewToken),
            fixture.ActorId,
            default);
        var savedStage = Assert.Single(saved.Stages);
        Assert.Equal("Draft", saved.OrderStatus);
        Assert.Equal("Draft", savedStage.Status);
        Assert.NotEqual(cancelled.ConcurrencyToken, saved.ConcurrencyToken);
        Assert.NotEqual(cancelledStage.ConcurrencyToken, savedStage.ConcurrencyToken);

        var secondApproval = await fixture.Service.ApproveDailyOperationAsync(
            saved.ProductionOrderId,
            ApprovalRequest(saved),
            fixture.ActorId,
            default);
        Assert.Equal("Completed", secondApproval.OrderStatus);
        var reapproved = (await fixture.Service.LoadDailyOperationsAsync(
            fixture.Factory.Id, fixture.Line.Id, fixture.Model.Id, productionDate, default)).ExistingDraft;
        Assert.NotNull(reapproved);
        Assert.All(reapproved.Stages, stage => Assert.Equal("Approved", stage.Status));
    }

    [Fact]
    public async Task Saving_a_cancelled_daily_draft_reopens_every_stage_to_draft()
    {
        await using var fixture = await Fixture.CreateAsync("SharedPercentage", 0.50m, 17m);
        var productionDate = fixture.Today.AddDays(2);
        var (request, created) = await CreateDailyDraftAsync(fixture, productionDate);
        await fixture.Service.ApproveDailyOperationAsync(created.ProductionOrderId, ApprovalRequest(created), fixture.ActorId, default);
        var approved = (await fixture.Service.LoadDailyOperationsAsync(
            fixture.Factory.Id, fixture.Line.Id, fixture.Model.Id, productionDate, default)).ExistingDraft;
        Assert.NotNull(approved);
        var cancelled = await fixture.Service.CancelDailyOperationApprovalAsync(
            approved.ProductionOrderId,
            new DailyProductionApprovalCancellationRequest("إعادة فتح للتصحيح", ApprovalRequest(approved).StageApprovals),
            fixture.ActorId,
            default);
        Assert.All(cancelled.Stages, stage => Assert.Equal("Cancelled", stage.Status));

        var preview = await fixture.Service.PreviewDailyOperationsAsync(request, fixture.ActorId, default);
        var reopened = await fixture.Service.UpdateDailyDraftAsync(
            cancelled.ProductionOrderId,
            UpdateRequest(request, cancelled, preview.PreviewToken),
            fixture.ActorId,
            default);

        Assert.All(reopened.Stages, stage => Assert.Equal("Draft", stage.Status));
        Assert.All(reopened.Stages, stage => Assert.NotEqual(cancelled.Stages.Single(old => old.Id == stage.Id).ConcurrencyToken, stage.ConcurrencyToken));
    }

    [Fact]
    public async Task Cancelled_daily_records_cannot_be_approved_without_an_intervening_save()
    {
        await using var fixture = await Fixture.CreateAsync("SharedPercentage", 0.50m, 17m);
        var productionDate = fixture.Today.AddDays(2);
        var (_, created) = await CreateDailyDraftAsync(fixture, productionDate);
        await fixture.Service.ApproveDailyOperationAsync(created.ProductionOrderId, ApprovalRequest(created), fixture.ActorId, default);
        var approved = (await fixture.Service.LoadDailyOperationsAsync(
            fixture.Factory.Id, fixture.Line.Id, fixture.Model.Id, productionDate, default)).ExistingDraft;
        Assert.NotNull(approved);
        var cancelled = await fixture.Service.CancelDailyOperationApprovalAsync(
            approved.ProductionOrderId,
            new DailyProductionApprovalCancellationRequest("اختبار منع الاعتماد المباشر", ApprovalRequest(approved).StageApprovals),
            fixture.ActorId,
            default);

        await Assert.ThrowsAsync<ProductionConflictException>(() => fixture.Service.ApproveDailyOperationAsync(
            cancelled.ProductionOrderId,
            ApprovalRequest(cancelled),
            fixture.ActorId,
            default));

        var persisted = (await fixture.Service.LoadDailyOperationsAsync(
            fixture.Factory.Id, fixture.Line.Id, fixture.Model.Id, productionDate, default)).ExistingDraft;
        Assert.NotNull(persisted);
        Assert.Equal("Draft", persisted.OrderStatus);
        Assert.All(persisted.Stages, stage => Assert.Equal("Cancelled", stage.Status));
    }

    [Fact]
    public async Task Daily_draft_update_rejects_stale_order_and_stage_tokens_without_mutation()
    {
        await using var fixture = await Fixture.CreateAsync("SharedPercentage", 0.50m, 17m);
        var productionDate = fixture.Today.AddDays(2);
        var (request, created) = await CreateDailyDraftAsync(fixture, productionDate);
        var preview = await fixture.Service.PreviewDailyOperationsAsync(request, fixture.ActorId, default);
        var validUpdate = UpdateRequest(request, created, preview.PreviewToken);

        await Assert.ThrowsAsync<ProductionConflictException>(() => fixture.Service.UpdateDailyDraftAsync(
            created.ProductionOrderId,
            validUpdate with { ConcurrencyToken = Guid.NewGuid() },
            fixture.ActorId,
            default));
        var staleStageUpdate = validUpdate with
        {
            Stages = validUpdate.Stages.Select(stage => stage with { ConcurrencyToken = Guid.NewGuid() }).ToArray()
        };
        await Assert.ThrowsAsync<ProductionConflictException>(() => fixture.Service.UpdateDailyDraftAsync(
            created.ProductionOrderId,
            staleStageUpdate,
            fixture.ActorId,
            default));

        var persisted = (await fixture.Service.LoadDailyOperationsAsync(
            fixture.Factory.Id, fixture.Line.Id, fixture.Model.Id, productionDate, default)).ExistingDraft;
        Assert.NotNull(persisted);
        Assert.Equal(created.ConcurrencyToken, persisted.ConcurrencyToken);
        Assert.Equal(created.Stages.Select(stage => stage.ConcurrencyToken), persisted.Stages.Select(stage => stage.ConcurrencyToken));
    }

    [Fact]
    public async Task Daily_draft_update_rejects_missing_or_mismatched_stage_records_without_positional_pairing()
    {
        await using var fixture = await Fixture.CreateAsync("SharedPercentage", 0.50m, 17m);
        var productionDate = fixture.Today.AddDays(2);
        var (request, created) = await CreateDailyDraftAsync(fixture, productionDate);
        var preview = await fixture.Service.PreviewDailyOperationsAsync(request, fixture.ActorId, default);
        var validUpdate = UpdateRequest(request, created, preview.PreviewToken);
        var mismatched = validUpdate with
        {
            Stages = validUpdate.Stages.Select(stage => stage with { StageProductionRecordId = Guid.NewGuid() }).ToArray()
        };

        await Assert.ThrowsAsync<ProductionConflictException>(() => fixture.Service.UpdateDailyDraftAsync(
            created.ProductionOrderId,
            mismatched,
            fixture.ActorId,
            default));
        await Assert.ThrowsAsync<ProductionConflictException>(() => fixture.Service.UpdateDailyDraftAsync(
            created.ProductionOrderId,
            validUpdate with { Stages = [] },
            fixture.ActorId,
            default));

        var persistedRecord = Assert.Single(await fixture.Db.Set<StageProductionRecord>()
            .Where(record => record.ProductionOrderId == created.ProductionOrderId)
            .ToArrayAsync());
        Assert.Equal(Assert.Single(created.Stages).Id, persistedRecord.Id);
        Assert.Equal(StageProductionRecordStatus.Draft, persistedRecord.Status);
    }

    [Fact]
    public async Task Daily_draft_save_preserves_original_stage_set_when_a_model_stage_is_added_later()
    {
        await using var fixture = await Fixture.CreateAsync("SharedPercentage", 0.50m, 17m);
        var productionDate = fixture.Today.AddDays(2);
        var stageB = await AddDailyStageAsync(fixture, "B", 2);
        var stageC = await AddDailyStageAsync(fixture, "C", 3);
        var (request, created) = await CreateDailyDraftAsync(fixture, productionDate);
        var originalByStage = created.Stages.ToDictionary(stage => stage.ProductModelStageId);
        var stageD = await AddDailyStageAsync(fixture, "D", 4);

        var saved = await fixture.Service.UpdateDailyDraftAsync(
            created.ProductionOrderId,
            UpdateRequest(request with { LineQuantity = 450m }, created, null),
            fixture.ActorId,
            default);

        Assert.Equal(3, saved.Stages.Count);
        Assert.Equal(
            new[] { fixture.Stage.Id, stageB.Id, stageC.Id }.Order(),
            saved.Stages.Select(stage => stage.ProductModelStageId).Order());
        Assert.DoesNotContain(saved.Stages, stage => stage.ProductModelStageId == stageD.Id);
        Assert.All(saved.Stages, stage =>
        {
            Assert.Equal(originalByStage[stage.ProductModelStageId].Id, stage.Id);
            Assert.NotEqual(Guid.Empty, stage.ConcurrencyToken);
        });
        var loaded = await fixture.Service.LoadDailyOperationsAsync(
            fixture.Factory.Id, fixture.Line.Id, fixture.Model.Id, productionDate, default);
        Assert.Equal(4, loaded.Stages.Count);
        Assert.Equal(3, loaded.ExistingDraft!.Stages.Count);
        Assert.Equal(3, await fixture.Db.Set<StageProductionRecord>()
            .CountAsync(record => record.ProductionOrderId == created.ProductionOrderId));
    }

    [Fact]
    public async Task Daily_draft_save_preserves_a_historical_stage_after_current_configuration_deactivation()
    {
        await using var fixture = await Fixture.CreateAsync("SharedPercentage", 0.50m, 17m);
        var productionDate = fixture.Today.AddDays(2);
        var stageB = await AddDailyStageAsync(fixture, "B", 2);
        var stageC = await AddDailyStageAsync(fixture, "C", 3);
        var (request, created) = await CreateDailyDraftAsync(fixture, productionDate);
        var historicalB = created.Stages.Single(stage => stage.ProductModelStageId == stageB.Id);
        fixture.Stage.Deactivate();
        stageB.Deactivate();
        stageC.Deactivate();
        await fixture.Db.SaveChangesAsync();

        var driftedLoad = await fixture.Service.LoadDailyOperationsAsync(
            fixture.Factory.Id, fixture.Line.Id, fixture.Model.Id, productionDate, default);
        Assert.Empty(driftedLoad.Stages);
        Assert.Equal(3, driftedLoad.ExistingDraft!.Stages.Count);

        var saved = await fixture.Service.UpdateDailyDraftAsync(
            created.ProductionOrderId,
            UpdateRequest(request, created, null),
            fixture.ActorId,
            default);

        var savedB = saved.Stages.Single(stage => stage.ProductModelStageId == stageB.Id);
        Assert.Equal(historicalB.Id, savedB.Id);
        Assert.Equal(historicalB.StageCode, savedB.StageCode);
        Assert.Equal(3, saved.Stages.Count);
        Assert.Equal(3, await fixture.Db.StageProductionRecords.CountAsync(record => record.ProductionOrderId == created.ProductionOrderId));
    }

    [Fact]
    public async Task Operationally_incomplete_daily_draft_can_be_saved_but_cannot_be_approved()
    {
        await using var fixture = await Fixture.CreateAsync("SharedPercentage", 0.50m, 17m);
        var productionDate = fixture.Today.AddDays(2);
        var (request, created) = await CreateDailyDraftAsync(fixture, productionDate);
        var incompleteUpdate = UpdateRequest(request, created, null) with
        {
            Stages = created.Stages.Select(stage => new DailyProductionStageDraftUpdateRequest(
                stage.Id,
                stage.ProductModelStageId,
                stage.ConcurrencyToken,
                [])).ToArray()
        };

        var saved = await fixture.Service.UpdateDailyDraftAsync(
            created.ProductionOrderId,
            incompleteUpdate,
            fixture.ActorId,
            default);

        Assert.Empty(Assert.Single(saved.Stages).Workers);
        var conflict = await Assert.ThrowsAsync<ProductionConflictException>(() => fixture.Service.ApproveDailyOperationAsync(
            saved.ProductionOrderId,
            ApprovalRequest(saved),
            fixture.ActorId,
            default));
        Assert.Contains("بلا عامل مشارك", conflict.Message);
        Assert.All((await fixture.Service.LoadDailyOperationsAsync(
            fixture.Factory.Id, fixture.Line.Id, fixture.Model.Id, productionDate, default)).ExistingDraft!.Stages,
            stage => Assert.Equal("Draft", stage.Status));
    }

    [Fact]
    public async Task Attendance_source_failure_is_a_non_blocking_draft_warning()
    {
        await using var fixture = await Fixture.CreateAsync("SharedPercentage", 0.50m, 17m);
        var productionDate = fixture.Today.AddDays(2);
        var service = fixture.CreateService(new RecordingAuditEngine(), new FailingDailyAttendanceEngine());
        var operations = await service.LoadDailyOperationsAsync(
            fixture.Factory.Id, fixture.Line.Id, fixture.Model.Id, productionDate, default);
        var stage = Assert.Single(operations.Stages);
        Assert.Equal("AttendanceUnavailable", stage.AttendanceStatus);
        Assert.All(stage.Workers, worker => Assert.False(worker.IsProductionReady));
        var request = new DailyProductionOperationRequest(
            fixture.Factory.Id,
            fixture.Line.Id,
            fixture.Model.Id,
            productionDate,
            500m,
            Guid.NewGuid(),
            "attendance unavailable draft",
            null,
            [new DailyProductionStageRequest(stage.ProductModelStageId, [])]);

        var saved = await service.CreateDailyDraftAsync(request, fixture.ActorId, default);

        Assert.Empty(Assert.Single(saved.Stages).Workers);
        await Assert.ThrowsAsync<ProductionConflictException>(() => service.ApproveDailyOperationAsync(
            saved.ProductionOrderId,
            ApprovalRequest(saved),
            fixture.ActorId,
            default));
    }

    [Fact]
    public async Task Daily_draft_update_rejects_a_stage_record_and_token_mapped_to_another_persisted_stage()
    {
        await using var fixture = await Fixture.CreateAsync("SharedPercentage", 0.50m, 17m);
        var productionDate = fixture.Today.AddDays(2);
        await AddDailyStageAsync(fixture, "B", 2);
        var (request, created) = await CreateDailyDraftAsync(fixture, productionDate);
        var valid = UpdateRequest(request, created, null);
        var first = valid.Stages.First();
        var second = valid.Stages.Last();
        var tampered = valid with
        {
            Stages = valid.Stages.Select(stage => stage.ProductModelStageId == first.ProductModelStageId
                ? stage with
                {
                    StageProductionRecordId = second.StageProductionRecordId,
                    ConcurrencyToken = second.ConcurrencyToken
                }
                : stage).ToArray()
        };

        await Assert.ThrowsAsync<ProductionConflictException>(() => fixture.Service.UpdateDailyDraftAsync(
            created.ProductionOrderId,
            tampered,
            fixture.ActorId,
            default));

        var persisted = (await fixture.Service.LoadDailyOperationsAsync(
            fixture.Factory.Id, fixture.Line.Id, fixture.Model.Id, productionDate, default)).ExistingDraft!;
        Assert.Equal(created.Stages.Select(stage => stage.Id).Order(), persisted.Stages.Select(stage => stage.Id).Order());
    }

    [Fact]
    public async Task Daily_operations_keep_no_source_check_in_distinct_from_explicit_absence()
    {
        await using var fixture = await Fixture.CreateAsync("SharedPercentage", 0.50m, 17m);
        var noCheckIn = fixture.CreateService(new RecordingAuditEngine(), new NoCheckInAttendanceEngine());
        var absent = fixture.CreateService(new RecordingAuditEngine(), new AbsentAttendanceEngine());

        var noCheckInResult = await noCheckIn.LoadDailyOperationsAsync(fixture.Factory.Id, fixture.Line.Id, fixture.Model.Id, fixture.Today, default);
        var absentResult = await absent.LoadDailyOperationsAsync(fixture.Factory.Id, fixture.Line.Id, fixture.Model.Id, fixture.Today, default);

        Assert.True(Assert.Single(noCheckInResult.Stages).HasNoSourceCheckInWorkers);
        Assert.False(Assert.Single(noCheckInResult.Stages).HasAbsentWorkers);
        Assert.True(Assert.Single(absentResult.Stages).HasAbsentWorkers);
        Assert.False(Assert.Single(absentResult.Stages).HasNoSourceCheckInWorkers);
        Assert.All(Assert.Single(noCheckInResult.Stages).Workers, worker =>
        {
            Assert.True(worker.IsAssignedWorker);
            Assert.False(worker.IsProductionReady);
        });
        Assert.All(Assert.Single(absentResult.Stages).Workers, worker =>
        {
            Assert.True(worker.IsAssignedWorker);
            Assert.False(worker.IsProductionReady);
            Assert.Equal("Absent", worker.ExclusionReason);
        });
    }

    [Fact]
    public async Task Daily_operations_import_permanent_staffing_without_using_assignment_audit_time()
    {
        await using var fixture = await Fixture.CreateAsync("SharedPercentage", 0.50m, 17m);
        var productionDateBeforeAssignmentsWereCreated = fixture.Today.AddDays(-2);

        var operations = await fixture.Service.LoadDailyOperationsAsync(
            fixture.Factory.Id,
            fixture.Line.Id,
            fixture.Model.Id,
            productionDateBeforeAssignmentsWereCreated,
            default);

        var workers = Assert.Single(operations.Stages).Workers;
        Assert.Equal(3, workers.Count);
        Assert.All(workers, worker =>
        {
            Assert.True(worker.IsAssignedWorker);
            Assert.True(worker.IsProductionReady);
            Assert.True(worker.WorkerMinutes > 0);
        });
    }

    [Fact]
    public async Task Daily_operations_keep_an_assigned_worker_with_missing_last_out_visible_and_not_ready()
    {
        await using var fixture = await Fixture.CreateAsync("SharedPercentage", 0.50m, 17m);
        var service = fixture.CreateService(new RecordingAuditEngine(), new IncompleteAttendanceEngine());

        var operations = await service.LoadDailyOperationsAsync(
            fixture.Factory.Id,
            fixture.Line.Id,
            fixture.Model.Id,
            fixture.Today,
            default);

        Assert.All(Assert.Single(operations.Stages).Workers, worker =>
        {
            Assert.True(worker.IsAssignedWorker);
            Assert.False(worker.IsProductionReady);
            Assert.Equal("IncompleteAttendance", worker.ExclusionReason);
            Assert.Equal(0, worker.WorkerMinutes);
        });
    }

    [Fact]
    public async Task Daily_operations_keep_assigned_workers_visible_when_attendance_has_no_temporal_intersection()
    {
        await using var fixture = await Fixture.CreateAsync("SharedPercentage", 0.50m, 17m);
        var cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
        var nextLocalDay = fixture.Today.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var outsideStart = TimeZoneInfo.ConvertTimeToUtc(nextLocalDay.AddHours(1), cairo);
        var windows = new[] { fixture.WorkerA.Id, fixture.WorkerB.Id, fixture.WorkerC.Id }
            .ToDictionary(
                workerId => workerId,
                workerId => new AttendancePresenceWindowDto(workerId, AttendanceStatus.Present, outsideStart, outsideStart.AddHours(2), true));

        var operations = await fixture.CreateService(new RecordingAuditEngine(), new WindowAttendanceEngine(windows))
            .LoadDailyOperationsAsync(fixture.Factory.Id, fixture.Line.Id, fixture.Model.Id, fixture.Today, default);

        Assert.All(Assert.Single(operations.Stages).Workers, worker =>
        {
            Assert.True(worker.IsAssignedWorker);
            Assert.False(worker.IsProductionReady);
            Assert.Equal("NoTemporalIntersection", worker.ExclusionReason);
        });
    }

    [Fact]
    public async Task Daily_participant_overrides_do_not_mutate_permanent_or_temporary_staffing()
    {
        await using var fixture = await Fixture.CreateAsync("SharedPercentage", 0.50m, 17m);
        var dailyOverride = new Worker(Guid.NewGuid(), "DAILY", "Daily override");
        fixture.Db.Add(dailyOverride);
        await fixture.Db.SaveChangesAsync();
        var productionDate = fixture.Today.AddDays(1);
        var operations = await fixture.Service.LoadDailyOperationsAsync(fixture.Factory.Id, fixture.Line.Id, fixture.Model.Id, productionDate, default);
        var stage = Assert.Single(operations.Stages);
        var participants = stage.Workers.Select(worker =>
                new WorkerAllocationRequest(worker.WorkerId, worker.SuggestedPercentage, null, null))
            .Append(new WorkerAllocationRequest(dailyOverride.Id, 1m, null, null, "بديل لهذا اليوم فقط"))
            .ToArray();
        var request = new DailyProductionOperationRequest(
            fixture.Factory.Id, fixture.Line.Id, fixture.Model.Id, productionDate, 500m, Guid.NewGuid(), null, null,
            [new DailyProductionStageRequest(stage.ProductModelStageId, participants)]);

        var preview = await fixture.Service.PreviewDailyOperationsAsync(request, fixture.ActorId, default);

        Assert.Contains(Assert.Single(preview.Stages).Workers, worker => worker.WorkerId == dailyOverride.Id);
        Assert.False(await fixture.Db.Set<WorkerDefaultAssignment>().AnyAsync(assignment => assignment.WorkerId == dailyOverride.Id));
        Assert.False(await fixture.Db.Set<WorkerTemporaryAssignment>().AnyAsync(assignment => assignment.WorkerId == dailyOverride.Id));
    }

    [Fact]
    public async Task Daily_operations_use_cairo_day_boundaries_for_overlapping_and_expired_temporary_staffing()
    {
        await using var fixture = await Fixture.CreateAsync("SharedPercentage", 0.50m, 17m);
        var boundaryWorker = new Worker(Guid.NewGuid(), "BOUNDARY", "Boundary worker");
        var expiredWorker = new Worker(Guid.NewGuid(), "EXPIRED", "Expired worker");
        var cancelledWorker = new Worker(Guid.NewGuid(), "CANCELLED", "Cancelled worker");
        var cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
        var localStart = fixture.Today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        DateTime Utc(DateTime local) => TimeZoneInfo.ConvertTimeToUtc(local, cairo);
        fixture.Db.AddRange(
            boundaryWorker,
            expiredWorker,
            cancelledWorker,
            new WorkerTemporaryAssignment(
                Guid.NewGuid(), boundaryWorker.Id, null, fixture.SubStage.Id,
                Utc(localStart.AddMinutes(-30)), Utc(localStart.AddMinutes(30)), fixture.ActorId,
                "Crosses local midnight", participationMode: TemporaryAssignmentMode.AdditionalParticipation, status: "Active"),
            new WorkerTemporaryAssignment(
                Guid.NewGuid(), expiredWorker.Id, null, fixture.SubStage.Id,
                Utc(localStart.AddHours(-6)), Utc(localStart.AddHours(-4)), fixture.ActorId,
                "Expired before production day", participationMode: TemporaryAssignmentMode.AdditionalParticipation, status: "Active"),
            new WorkerTemporaryAssignment(
                Guid.NewGuid(), cancelledWorker.Id, null, fixture.SubStage.Id,
                Utc(localStart.AddHours(8)), Utc(localStart.AddHours(10)), fixture.ActorId,
                "Cancelled before daily load", participationMode: TemporaryAssignmentMode.AdditionalParticipation, status: "Cancelled"));
        await fixture.Db.SaveChangesAsync();
        var attendance = new WindowAttendanceEngine(new Dictionary<Guid, AttendancePresenceWindowDto>
        {
            [boundaryWorker.Id] = new(boundaryWorker.Id, AttendanceStatus.Present, Utc(localStart.AddMinutes(15)), Utc(localStart.AddMinutes(45)), true),
            [cancelledWorker.Id] = new(cancelledWorker.Id, AttendanceStatus.Present, Utc(localStart.AddHours(8)), Utc(localStart.AddHours(10)), true)
        });

        var operations = await fixture.CreateService(new RecordingAuditEngine(), attendance)
            .LoadDailyOperationsAsync(fixture.Factory.Id, fixture.Line.Id, fixture.Model.Id, fixture.Today, default);
        var workers = Assert.Single(operations.Stages).Workers;

        var boundary = Assert.Single(workers, worker => worker.WorkerId == boundaryWorker.Id);
        Assert.True(boundary.IsProductionReady);
        Assert.Equal(15, boundary.WorkerMinutes);
        Assert.DoesNotContain(workers, worker => worker.WorkerId == expiredWorker.Id);
        Assert.DoesNotContain(workers, worker => worker.WorkerId == cancelledWorker.Id);
    }

    [Fact]
    public async Task Daily_operations_intersect_sequential_assignment_windows_with_actual_presence_and_allocate_equally_by_default()
    {
        await using var fixture = await Fixture.CreateAsync("SharedPercentage", 1m, 17m);
        var first = new Worker(Guid.NewGuid(), "T1", "Morning worker");
        var second = new Worker(Guid.NewGuid(), "T2", "Afternoon worker");
        var localStart = fixture.Today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
        DateTime At(int hour) => TimeZoneInfo.ConvertTimeToUtc(localStart.AddHours(hour), cairo);
        fixture.Db.AddRange(
            first,
            second,
            new WorkerTemporaryAssignment(Guid.NewGuid(), first.Id, null, fixture.SubStage.Id, At(8), At(13), fixture.ActorId, "Morning coverage", participationMode: TemporaryAssignmentMode.AdditionalParticipation, status: "Active"),
            new WorkerTemporaryAssignment(Guid.NewGuid(), second.Id, null, fixture.SubStage.Id, At(13), At(16), fixture.ActorId, "Afternoon coverage", participationMode: TemporaryAssignmentMode.AdditionalParticipation, status: "Active"));
        await fixture.Db.SaveChangesAsync();
        var attendance = new WindowAttendanceEngine(new Dictionary<Guid, AttendancePresenceWindowDto>
        {
            [first.Id] = new(first.Id, AttendanceStatus.Present, At(8), At(13), true),
            [second.Id] = new(second.Id, AttendanceStatus.Present, At(13), At(16), true)
        });
        var service = fixture.CreateService(new RecordingAuditEngine(), attendance);

        var operations = await service.LoadDailyOperationsAsync(fixture.Factory.Id, fixture.Line.Id, fixture.Model.Id, fixture.Today, default);
        var stage = Assert.Single(operations.Stages);
        var ready = stage.Workers.Where(worker => worker.IsProductionReady).OrderBy(worker => worker.WorkerCode).ToArray();

        Assert.Equal([300, 180], ready.Select(worker => worker.WorkerMinutes).ToArray());
        Assert.Equal([50m, 50m], ready.Select(worker => worker.SuggestedPercentage!.Value).ToArray());
        var request = new DailyProductionOperationRequest(
            fixture.Factory.Id, fixture.Line.Id, fixture.Model.Id, fixture.Today, 500m, Guid.NewGuid(), null, null,
            [new DailyProductionStageRequest(stage.ProductModelStageId, ready.Select(worker => new WorkerAllocationRequest(worker.WorkerId, worker.SuggestedPercentage, null, null)).ToArray())]);
        var preview = await service.PreviewDailyOperationsAsync(request, fixture.ActorId, default);

        Assert.Equal(500m, preview.Stages.Single().StageQuantity);
        Assert.Equal(500m, preview.Stages.Single().Workers.Sum(worker => worker.EquivalentQuantity));
        Assert.Equal([250m, 250m], preview.Stages.Single().Workers.OrderBy(worker => worker.WorkerCode).Select(worker => worker.EquivalentQuantity).ToArray());
    }

    [Fact]
    public async Task Daily_operations_preserve_every_stage_allocation_for_a_worker_with_multiple_participations()
    {
        await using var fixture = await Fixture.CreateAsync("SharedPercentage", 1m, 17m, useRealAudit: true);
        var extraSubStage = new SubStage(Guid.NewGuid(), fixture.MainStage.Id, "Inspect", "INS", 1, 2);
        var extraStage = new ProductModelStage(Guid.NewGuid(), fixture.Model.Id, fixture.Line.Id, extraSubStage.Id, 2, 1m, 17m, CompensationMode.SharedPercentage);
        fixture.Db.AddRange(extraSubStage, extraStage, new WorkerDefaultAssignment(Guid.NewGuid(), fixture.WorkerA.Id, extraSubStage.Id, fixture.ActorId, DateTime.UtcNow, productionLineId: fixture.Line.Id));
        await fixture.Db.SaveChangesAsync();
        var productionDate = fixture.Today.AddDays(1);

        var operations = await fixture.Service.LoadDailyOperationsAsync(fixture.Factory.Id, fixture.Line.Id, fixture.Model.Id, productionDate, default);
        Assert.Equal(2, operations.Stages.Count);
        Assert.Contains(operations.Stages.Single(stage => stage.ProductModelStageId == fixture.Stage.Id).Workers, worker => worker.WorkerId == fixture.WorkerA.Id);
        Assert.Contains(operations.Stages.Single(stage => stage.ProductModelStageId == extraStage.Id).Workers, worker => worker.WorkerId == fixture.WorkerA.Id);

        var request = new DailyProductionOperationRequest(
            fixture.Factory.Id, fixture.Line.Id, fixture.Model.Id, productionDate, 500m, Guid.NewGuid(), null, null,
            operations.Stages.Select(stage => new DailyProductionStageRequest(
                stage.ProductModelStageId,
                stage.Workers.Select(worker => new WorkerAllocationRequest(worker.WorkerId, worker.SuggestedPercentage, null, null)).ToArray())).ToArray());
        var preview = await fixture.Service.PreviewDailyOperationsAsync(request, fixture.ActorId, default);
        var workerStageAllocations = preview.Stages.SelectMany(stage => stage.Workers.Where(worker => worker.WorkerId == fixture.WorkerA.Id)).ToArray();
        var workerTotal = preview.WorkerTotals.Single(total => total.WorkerId == fixture.WorkerA.Id);

        Assert.Equal(2, workerStageAllocations.Length);
        Assert.Equal(workerStageAllocations.Sum(allocation => allocation.CalculatedEarning), workerTotal.TotalEntitlement);
        Assert.All(preview.Stages, stage => Assert.Equal(500m, stage.StageQuantity));
        var saved = await fixture.Service.CreateDailyDraftAsync(request with { PreviewToken = preview.PreviewToken }, fixture.ActorId, default);
        Assert.Equal(2, saved.Stages.Count);
        Assert.Equal(workerTotal.TotalEntitlement, saved.Stages.SelectMany(stage => stage.Workers).Where(worker => worker.WorkerId == fixture.WorkerA.Id).Sum(worker => worker.CalculatedEarning));
    }

    [Fact]
    public async Task Daily_operations_preview_handles_a_full_day_of_66_stages_and_75_allocations_without_dropping_repeated_workers()
    {
        await using var fixture = await Fixture.CreateAsync("SharedPercentage", 1m, 17m, useRealAudit: true);
        var additionalStages = Enumerable.Range(1, 65)
            .Select(index => new
            {
                SubStage = new SubStage(Guid.NewGuid(), fixture.MainStage.Id, $"Stage {index:00}", $"S{index:00}", index + 1, index + 1),
            })
            .ToArray();
        var productStages = additionalStages.Select((item, index) => new ProductModelStage(
            Guid.NewGuid(), fixture.Model.Id, fixture.Line.Id, item.SubStage.Id, index + 2, 1m, 17m, CompensationMode.SharedPercentage)).ToArray();
        fixture.Db.AddRange(additionalStages.Select(item => item.SubStage));
        fixture.Db.AddRange(productStages);
        fixture.Db.AddRange(productStages.Select(stage => new WorkerDefaultAssignment(Guid.NewGuid(), fixture.WorkerA.Id, stage.SubStageId, fixture.ActorId, DateTime.UtcNow, productionLineId: fixture.Line.Id)));
        // The original stage has three workers. Adding A to every additional stage
        // plus B to seven stages produces 66 stages and exactly 75 allocations.
        fixture.Db.AddRange(productStages.Take(7).Select(stage => new WorkerDefaultAssignment(Guid.NewGuid(), fixture.WorkerB.Id, stage.SubStageId, fixture.ActorId, DateTime.UtcNow, productionLineId: fixture.Line.Id)));
        await fixture.Db.SaveChangesAsync();

        var productionDate = fixture.Today.AddDays(1);
        var operations = await fixture.Service.LoadDailyOperationsAsync(fixture.Factory.Id, fixture.Line.Id, fixture.Model.Id, productionDate, default);
        Assert.Equal(66, operations.Stages.Count);

        var request = new DailyProductionOperationRequest(
            fixture.Factory.Id,
            fixture.Line.Id,
            fixture.Model.Id,
            productionDate,
            500m,
            Guid.NewGuid(),
            "full-day preview",
            null,
            operations.Stages.Select(stage => new DailyProductionStageRequest(
                stage.ProductModelStageId,
                stage.Workers.Select(worker => new WorkerAllocationRequest(worker.WorkerId, worker.SuggestedPercentage, null, null)).ToArray())).ToArray());

        var preview = await fixture.Service.PreviewDailyOperationsAsync(request, fixture.ActorId, default);

        Assert.Equal(66, preview.Stages.Count);
        Assert.Equal(75, preview.Stages.Sum(stage => stage.Workers.Count));
        Assert.All(preview.Stages, stage => Assert.Equal(500m, stage.StageQuantity));
        Assert.Equal(preview.Stages.SelectMany(stage => stage.Workers).Sum(worker => worker.CalculatedEarning), preview.TotalWorkerEntitlements);
        var repeatedWorkerAllocations = preview.Stages.SelectMany(stage => stage.Workers).Where(worker => worker.WorkerId == fixture.WorkerA.Id).ToArray();
        Assert.Equal(66, repeatedWorkerAllocations.Length);
        Assert.Equal(repeatedWorkerAllocations.Sum(worker => worker.CalculatedEarning), preview.WorkerTotals.Single(total => total.WorkerId == fixture.WorkerA.Id).TotalEntitlement);
        Assert.Empty(await fixture.Db.Set<StageProductionRecord>().Where(record => record.ProductionDate == productionDate).ToArrayAsync());

        var saved = await fixture.Service.CreateDailyDraftAsync(request with { PreviewToken = preview.PreviewToken }, fixture.ActorId, default);
        Assert.Equal(66, saved.Stages.Count);
        Assert.Equal(75, saved.Stages.Sum(stage => stage.Workers.Count));
        Assert.All(saved.Stages, stage => Assert.Equal(500m, stage.ProducedQuantity));
        Assert.Equal(preview.TotalWorkerEntitlements, saved.Stages.SelectMany(stage => stage.Workers).Sum(worker => worker.CalculatedEarning));
    }

    [Fact]
    public async Task Relational_clients_allow_first_mutation_reject_stale_requests_and_allow_retry_after_refresh()
    {
        await using var fixture = await Fixture.CreateAsync("SharedPercentage", 0.50m, 17m, useRealAudit: true);
        var draft = await fixture.CreateDraftAsync(10m, 10m, 0m, [fixture.Allocation(fixture.WorkerA.Id, 100m)]);
        StageProductionRecordDto aRead;
        await using (var clientARead = fixture.CreateClient())
            aRead = await clientARead.Service.GetRecordAsync(draft.Id, default);
        await using var clientA = fixture.CreateClient();
        await using var clientB = fixture.CreateClient();
        var bRead = await clientB.Service.GetRecordAsync(draft.Id, default);
        var bUpdated = await clientB.Service.UpdateDraftAsync(draft.Id, new UpdateStageProductionRecordRequest(fixture.Today, 9m, 9m, 0m, bRead.ConcurrencyToken, null, [fixture.Allocation(fixture.WorkerA.Id, 100m)]), fixture.ActorId, default);
        Assert.NotEqual(bRead.ConcurrencyToken, bUpdated.ConcurrencyToken);

        await Assert.ThrowsAsync<ProductionConflictException>(() => clientA.Service.UpdateDraftAsync(draft.Id, new UpdateStageProductionRecordRequest(fixture.Today, 8m, 8m, 0m, aRead.ConcurrencyToken, null, [fixture.Allocation(fixture.WorkerA.Id, 100m)]), fixture.ActorId, default));
        await Assert.ThrowsAsync<ProductionConflictException>(() => clientA.Service.ApproveAsync(draft.Id, aRead.ConcurrencyToken, fixture.ActorId, default));
        Assert.Equal("Draft", (await fixture.Service.GetRecordAsync(draft.Id, default)).Status);
        Assert.DoesNotContain(await fixture.Db.AuditLogs.ToListAsync(), x => x.EntityType == "StageProductionRecord" && x.EntityAfterJson!.Contains("Approved"));

        var refreshed = await clientA.Service.GetRecordAsync(draft.Id, default);
        var approved = await clientA.Service.ApproveAsync(draft.Id, refreshed.ConcurrencyToken, fixture.ActorId, default);
        await using var clientC = fixture.CreateClient();
        var approvalAuditCount = (await fixture.Db.AuditLogs.ToListAsync()).Count(x => x.EntityType == "StageProductionRecord" && x.EntityAfterJson!.Contains("\"Status\":1"));
        await Assert.ThrowsAsync<ProductionConflictException>(() => clientC.Service.ApproveAsync(draft.Id, refreshed.ConcurrencyToken, fixture.ActorId, default));
        Assert.Equal(approvalAuditCount, (await fixture.Db.AuditLogs.ToListAsync()).Count(x => x.EntityType == "StageProductionRecord" && x.EntityAfterJson!.Contains("\"Status\":1")));
        var repeatedApproval = await clientC.Service.ApproveAsync(draft.Id, approved.ConcurrencyToken, fixture.ActorId, default);
        Assert.Equal(approved.TotalWorkerEarnings, repeatedApproval.TotalWorkerEarnings);
        await Assert.ThrowsAsync<ProductionConflictException>(() => clientC.Service.CancelProductionApprovalAsync(draft.Id, refreshed.ConcurrencyToken, "تصحيح اعتماد الإنتاج", fixture.ActorId, default));
        var cancelled = await clientC.Service.CancelProductionApprovalAsync(draft.Id, approved.ConcurrencyToken, "تصحيح اعتماد الإنتاج", fixture.ActorId, default);
        Assert.Equal("Cancelled", cancelled.Status);
    }

    [Fact]
    public async Task Historical_snapshot_and_financial_audit_remain_reviewable()
    {
        await using var fixture = await Fixture.CreateAsync("SharedPercentage", 0.50m, 17m, useRealAudit: true);
        var draft = await fixture.CreateDraftAsync(10m, 10m, 0m, [fixture.Allocation(fixture.WorkerA.Id, 100m)]);
        await fixture.Service.ApproveAsync(draft.Id, draft.ConcurrencyToken, fixture.ActorId, default);
        fixture.Db.Entry(fixture.Model).Property(nameof(ProductModel.Code)).CurrentValue = "MODEL-CHANGED";
        fixture.Db.Entry(fixture.Model).Property(nameof(ProductModel.Name)).CurrentValue = "Changed model";
        fixture.Db.Entry(fixture.Factory).Property(nameof(Factory.Code)).CurrentValue = "FACTORY-CHANGED";
        fixture.Db.Entry(fixture.Factory).Property(nameof(Factory.Name)).CurrentValue = "Changed factory";
        fixture.Db.Entry(fixture.Line).Property(nameof(ProductionLine.Name)).CurrentValue = "Changed line";
        fixture.Db.Entry(fixture.MainStage).Property(nameof(MainStage.Name)).CurrentValue = "Changed main stage";
        fixture.Db.Entry(fixture.SubStage).Property(nameof(SubStage.Code)).CurrentValue = "STAGE-CHANGED";
        fixture.Db.Entry(fixture.SubStage).Property(nameof(SubStage.Name)).CurrentValue = "Changed sub stage";
        fixture.Db.Entry(fixture.WorkerA).Property(nameof(Worker.EmployeeCode)).CurrentValue = "WORKER-CHANGED";
        fixture.Db.Entry(fixture.WorkerA).Property(nameof(Worker.FullName)).CurrentValue = "Changed worker";
        var movedSubStage = new SubStage(Guid.NewGuid(), fixture.MainStage.Id, "Moved stage", "MOVED", 1, 2);
        var originalAssignment = await fixture.Db.WorkerDefaultAssignments.SingleAsync(x => x.WorkerId == fixture.WorkerA.Id && x.IsActive);
        fixture.Db.Add(movedSubStage);
        await fixture.Db.SaveChangesAsync();
        originalAssignment.Deactivate(DateTime.UtcNow);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.Add(new WorkerDefaultAssignment(Guid.NewGuid(), fixture.WorkerA.Id, movedSubStage.Id, fixture.ActorId, DateTime.UtcNow, "Moved after record", productionLineId: fixture.Line.Id));
        await fixture.Db.SaveChangesAsync();
        var report = await fixture.Service.DailyReportAsync(fixture.Today, fixture.Today, null, null, null, default);
        Assert.Equal("MODEL-A", report.Single().ModelCode);
        var historical = await fixture.Service.GetRecordAsync(draft.Id, default);
        Assert.Equal("FIX", historical.FactoryCode);
        Assert.Equal("Fixture Factory", historical.FactoryName);
        Assert.Equal("Fixture Line", historical.ProductionLineName);
        Assert.Equal("Fixture Main", historical.MainStageName);
        Assert.Equal("SEW", historical.StageCode);
        Assert.Equal("Sew", historical.StageName);
        Assert.Equal("A", historical.Workers.Single().WorkerCode);
        Assert.Equal("Worker A", historical.Workers.Single().WorkerName);
        var approval = (await fixture.Db.AuditLogs.ToListAsync()).Single(x => x.EntityType == "StageProductionRecord" && x.ActionType == AuditActionType.Update && x.EntityAfterJson!.Contains("Allocations"));
        Assert.Contains("CalculatedEarning", approval.EntityAfterJson);
    }

    [Fact]
    public async Task Production_stage_must_belong_to_the_order_line()
    {
        await using var fixture = await Fixture.CreateAsync("SharedPercentage", 0.50m, 17m);
        var otherMain = new MainStage(Guid.NewGuid(), fixture.MainStage.DepartmentId, "Other main", 2);
        var otherSubStage = new SubStage(Guid.NewGuid(), otherMain.Id, "Other sub", "OTHER", 1, 1);
        var otherStage = new ProductModelStage(Guid.NewGuid(), fixture.Model.Id, fixture.Line.Id, otherSubStage.Id, 2, 0.50m, 17m, CompensationMode.SharedPercentage);
        fixture.Db.AddRange(otherMain, otherSubStage, otherStage);
        await fixture.Db.SaveChangesAsync();

        var valid = await fixture.Service.CreateDraftAsync(
            new CreateStageProductionRecordRequest(fixture.Order.Id, otherStage.Id, fixture.Today, 10m, 10m, 0m, Guid.NewGuid(), null, [fixture.Allocation(fixture.WorkerA.Id, 100m, manualOverrideReason: "اختبار مرحلة أخرى" )]),
            fixture.ActorId,
            default);
        Assert.Equal("OTHER", valid.StageCode);

        var otherFactory = new Factory(Guid.NewGuid(), "Other factory", "OTHER-F");
        var otherDepartment = new Department(Guid.NewGuid(), otherFactory.Id, "OTHER", "قسم آخر", "Other", 1);
        var otherLine = new ProductionLine(Guid.NewGuid(), otherFactory.Id, "Other line", 1, departmentId: otherDepartment.Id);
        var unrelatedMain = new MainStage(Guid.NewGuid(), otherDepartment.Id, "Unrelated main", 1);
        var unrelatedSub = new SubStage(Guid.NewGuid(), unrelatedMain.Id, "Unrelated sub", "UNRELATED", 1, 1);
        var unrelatedStage = new ProductModelStage(Guid.NewGuid(), fixture.Model.Id, otherLine.Id, unrelatedSub.Id, 3, 0.50m, 17m, CompensationMode.SharedPercentage);
        fixture.Db.AddRange(otherFactory, otherDepartment, otherLine, unrelatedMain, unrelatedSub, unrelatedStage);
        await fixture.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<ProductionConflictException>(() => fixture.Service.CreateDraftAsync(
            new CreateStageProductionRecordRequest(fixture.Order.Id, unrelatedStage.Id, fixture.Today, 10m, 10m, 0m, Guid.NewGuid(), null, [fixture.Allocation(fixture.WorkerA.Id, 100m)]),
            fixture.ActorId,
            default));
    }

    [Fact]
    public async Task One_model_loads_its_own_stage_subset_for_each_selected_operating_line()
    {
        await using var fixture = await Fixture.CreateAsync("SharedPercentage", 0.50m, 17m);
        var secondLine = new ProductionLine(Guid.NewGuid(), fixture.Factory.Id, "Second fixture line", 2, departmentId: fixture.MainStage.DepartmentId);
        var secondMainStage = new MainStage(Guid.NewGuid(), fixture.MainStage.DepartmentId, "Second fixture main", 2);
        var secondSubStage = new SubStage(Guid.NewGuid(), secondMainStage.Id, "Second fixture sub", "SECOND", 1, 1, departmentId: secondMainStage.DepartmentId);
        var secondModelStage = new ProductModelStage(Guid.NewGuid(), fixture.Model.Id, secondLine.Id, secondSubStage.Id, 2, 0.50m, 17m, CompensationMode.SharedPercentage);
        fixture.Db.AddRange(secondLine, secondMainStage, secondSubStage, secondModelStage);
        await fixture.Db.SaveChangesAsync();

        var firstLineOperations = await fixture.Service.LoadDailyOperationsAsync(
            fixture.Factory.Id,
            fixture.Line.Id,
            fixture.Model.Id,
            fixture.Today,
            default);
        var secondLineOperations = await fixture.Service.LoadDailyOperationsAsync(
            fixture.Factory.Id,
            secondLine.Id,
            fixture.Model.Id,
            fixture.Today,
            default);

        Assert.Equal(fixture.Model.Id, firstLineOperations.ProductModelId);
        Assert.Equal(fixture.Model.Id, secondLineOperations.ProductModelId);
        Assert.Equal(fixture.Line.Id, firstLineOperations.ProductionLineId);
        Assert.Equal(secondLine.Id, secondLineOperations.ProductionLineId);
        Assert.Equal([fixture.Stage.Id], firstLineOperations.Stages.Select(stage => stage.ProductModelStageId));
        Assert.Equal([secondModelStage.Id], secondLineOperations.Stages.Select(stage => stage.ProductModelStageId));
    }

    [Fact]
    public async Task Production_participants_are_checked_against_active_worker_attendance_and_current_assignment()
    {
        await using var fixture = await Fixture.CreateAsync("SharedPercentage", 0.50m, 17m);
        var unassigned = new Worker(Guid.NewGuid(), "UNASSIGNED", "Unassigned Worker");
        fixture.Db.Add(unassigned);
        await fixture.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<ProductionConflictException>(() => fixture.Service.CreateDraftAsync(
            new CreateStageProductionRecordRequest(fixture.Order.Id, fixture.Stage.Id, fixture.Today, 1m, 1m, 0m, Guid.NewGuid(), null, [fixture.Allocation(unassigned.Id, 100m)]),
            fixture.ActorId,
            default));

        var absentService = fixture.CreateService(new RecordingAuditEngine(), new AbsentAttendanceEngine(), new AssignmentOverridePermissionService());
        await Assert.ThrowsAsync<ProductionConflictException>(() => absentService.CreateDraftAsync(
            new CreateStageProductionRecordRequest(fixture.Order.Id, fixture.Stage.Id, fixture.Today, 1m, 1m, 0m, Guid.NewGuid(), null, [fixture.Allocation(fixture.WorkerA.Id, 100m)]),
            fixture.ActorId,
            default));

        var deniedOverride = fixture.CreateService(new RecordingAuditEngine(), new AbsentAttendanceEngine(), new NoOverridePermissionService());
        await Assert.ThrowsAsync<ProductionConflictException>(() => deniedOverride.CreateDraftAsync(
            new CreateStageProductionRecordRequest(fixture.Order.Id, fixture.Stage.Id, fixture.Today, 1m, 1m, 0m, Guid.NewGuid(), null, [fixture.Allocation(fixture.WorkerA.Id, 100m, manualOverrideReason: "تشغيل معتمد")]),
            fixture.ActorId,
            default));

        var permittedOverride = await absentService.CreateDraftAsync(
            new CreateStageProductionRecordRequest(fixture.Order.Id, fixture.Stage.Id, fixture.Today, 1m, 1m, 0m, Guid.NewGuid(), null, [fixture.Allocation(fixture.WorkerA.Id, 100m, manualOverrideReason: "تشغيل معتمد")]),
            fixture.ActorId,
            default);
        Assert.Equal("تشغيل معتمد", permittedOverride.Workers.Single().ManualOverrideReason);
    }

    [Fact]
    public async Task Client_request_id_is_idempotent_while_a_new_guid_allows_a_separate_batch()
    {
        await using var fixture = await Fixture.CreateAsync("SharedPercentage", 0.50m, 17m);
        var clientRequestId = Guid.NewGuid();
        var request = new CreateStageProductionRecordRequest(fixture.Order.Id, fixture.Stage.Id, fixture.Today, 1m, 1m, 0m, clientRequestId, null, [fixture.Allocation(fixture.WorkerA.Id, 100m)]);

        var first = await fixture.Service.CreateDraftAsync(request, fixture.ActorId, default);
        var replay = await fixture.Service.CreateDraftAsync(request, fixture.ActorId, default);
        var laterBatch = await fixture.Service.CreateDraftAsync(request with { ClientRequestId = Guid.NewGuid() }, fixture.ActorId, default);

        Assert.Equal(first.Id, replay.Id);
        Assert.NotEqual(first.Id, laterBatch.Id);
        Assert.Equal(2, await fixture.Db.Set<StageProductionRecord>().CountAsync());
    }

    [Fact]
    public async Task Approval_uses_rounded_persisted_allocations_for_the_persisted_total()
    {
        await using var fixture = await Fixture.CreateAsync("SharedPercentage", 1.23456m, 17m);
        var draft = await fixture.CreateDraftAsync(1.0014m, 1.0014m, 0m, [fixture.Allocation(fixture.WorkerA.Id, 50m), fixture.Allocation(fixture.WorkerB.Id, 50m)]);
        var approved = await fixture.Service.ApproveAsync(draft.Id, draft.ConcurrencyToken, fixture.ActorId, default);

        Assert.All(approved.Workers, worker => Assert.Equal(decimal.Round(worker.CalculatedEarning, 4, MidpointRounding.AwayFromZero), worker.CalculatedEarning));
        Assert.Equal(approved.Workers.Sum(worker => worker.CalculatedEarning), approved.TotalWorkerEarnings);
        Assert.Equal(1.2346m, approved.PiecePrice);
    }

    [Fact]
    public async Task Order_lifecycle_blocks_completion_with_drafts_and_cancellation_with_approved_records()
    {
        await using var fixture = await Fixture.CreateAsync("SharedPercentage", 0.50m, 17m);
        var draft = await fixture.CreateDraftAsync(10m, 10m, 0m, [fixture.Allocation(fixture.WorkerA.Id, 100m)]);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => fixture.Service.TransitionOrderAsync(fixture.Order.Id, ProductionOrderStatus.Completed, fixture.ActorId, default));
        var approved = await fixture.Service.ApproveAsync(draft.Id, draft.ConcurrencyToken, fixture.ActorId, default);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => fixture.Service.TransitionOrderAsync(fixture.Order.Id, ProductionOrderStatus.Cancelled, fixture.ActorId, default));
        await fixture.Service.CancelProductionApprovalAsync(draft.Id, approved.ConcurrencyToken, "تصحيح اعتماد الإنتاج", fixture.ActorId, default);
        var cancelledOrder = await fixture.Service.TransitionOrderAsync(fixture.Order.Id, ProductionOrderStatus.Cancelled, fixture.ActorId, default);
        Assert.Equal("Cancelled", cancelledOrder.Status);
    }

    private static async Task<(DailyProductionOperationRequest Request, DailyProductionDraftDto Draft)> CreateDailyDraftAsync(
        Fixture fixture,
        DateOnly productionDate)
    {
        var operations = await fixture.Service.LoadDailyOperationsAsync(
            fixture.Factory.Id,
            fixture.Line.Id,
            fixture.Model.Id,
            productionDate,
            default);
        var request = new DailyProductionOperationRequest(
            fixture.Factory.Id,
            fixture.Line.Id,
            fixture.Model.Id,
            productionDate,
            500m,
            Guid.NewGuid(),
            "daily lifecycle test",
            null,
            operations.Stages.Select(stage => new DailyProductionStageRequest(
                stage.ProductModelStageId,
                stage.Workers
                    .Where(worker => worker.IsProductionReady)
                    .Select(worker => new WorkerAllocationRequest(worker.WorkerId, worker.SuggestedPercentage, null, null))
                    .ToArray()))
                .ToArray());
        var preview = await fixture.Service.PreviewDailyOperationsAsync(request, fixture.ActorId, default);
        var draft = await fixture.Service.CreateDailyDraftAsync(
            request with { PreviewToken = preview.PreviewToken },
            fixture.ActorId,
            default);
        return (request, draft);
    }

    private static DailyProductionApprovalRequest ApprovalRequest(DailyProductionDraftDto draft) => new(
        draft.Stages.Select(stage => new DailyStageApprovalRequest(stage.Id, stage.ConcurrencyToken)).ToArray());

    private static DailyProductionDraftUpdateRequest UpdateRequest(
        DailyProductionOperationRequest request,
        DailyProductionDraftDto draft,
        string? previewToken)
    {
        var persistedByStage = draft.Stages.ToDictionary(stage => stage.ProductModelStageId);
        return new DailyProductionDraftUpdateRequest(
            request.FactoryId,
            request.ProductionLineId,
            request.ProductModelId,
            request.ProductionDate,
            request.LineQuantity,
            request.ClientRequestId,
            draft.ConcurrencyToken,
            request.Notes,
            previewToken,
            request.Stages.Select(stage =>
            {
                var persisted = persistedByStage[stage.ProductModelStageId];
                return new DailyProductionStageDraftUpdateRequest(
                    persisted.Id,
                    stage.ProductModelStageId,
                    persisted.ConcurrencyToken,
                    stage.Workers);
            }).ToArray());
    }

    private static async Task<ProductModelStage> AddDailyStageAsync(Fixture fixture, string code, int order)
    {
        var subStage = new SubStage(Guid.NewGuid(), fixture.MainStage.Id, $"Stage {code}", code, order, order);
        var modelStage = new ProductModelStage(
            Guid.NewGuid(), fixture.Model.Id, fixture.Line.Id, subStage.Id, order, 0.50m, 17m, CompensationMode.SharedPercentage);
        fixture.Db.AddRange(
            subStage,
            modelStage,
            new WorkerDefaultAssignment(
                Guid.NewGuid(), fixture.WorkerA.Id, subStage.Id, fixture.ActorId, DateTime.UtcNow.AddMinutes(-1),
                "Daily stage regression", productionLineId: fixture.Line.Id));
        await fixture.Db.SaveChangesAsync();
        return modelStage;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(SqliteConnection connection, AppDbContext db, Factory factory, ProductionLine line, MainStage mainStage, ProductModel model, SubStage subStage, ProductModelStage stage, Worker a, Worker b, Worker c, Worker left, ProductionCostRecordingService service, Guid actorId, DateOnly today)
        { Connection = connection; Db = db; Factory = factory; Line = line; MainStage = mainStage; Model = model; SubStage = subStage; Stage = stage; WorkerA = a; WorkerB = b; WorkerC = c; LeftWorker = left; Service = service; ActorId = actorId; Today = today; }
        private SqliteConnection Connection { get; }
        public AppDbContext Db { get; } public Factory Factory { get; } public ProductionLine Line { get; } public MainStage MainStage { get; } public ProductModel Model { get; } public SubStage SubStage { get; } public ProductModelStage Stage { get; } public Worker WorkerA { get; } public Worker WorkerB { get; } public Worker WorkerC { get; } public Worker LeftWorker { get; } public ProductionCostRecordingService Service { get; } public Guid ActorId { get; } public DateOnly Today { get; } public ProductionOrderDto Order { get; private set; } = null!;
        public static async Task<Fixture> CreateAsync(string mode, decimal price, decimal seconds, IAuditEngine? audit = null, bool useRealAudit = false)
        {
            var connection = new SqliteConnection("Data Source=:memory:"); connection.CreateCollation("SQL_Latin1_General_CP1_CI_AS", (left, right) => string.Compare(left, right, StringComparison.OrdinalIgnoreCase)); await connection.OpenAsync(); var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options); await db.Database.EnsureCreatedAsync();
            var factory = new Factory(Guid.NewGuid(), "Fixture Factory", "FIX"); var department = new Department(Guid.NewGuid(), factory.Id, "OPS", "التشغيل", "Operations", 1); var line = new ProductionLine(Guid.NewGuid(), factory.Id, "Fixture Line", 1, departmentId: department.Id); var mainStage = new MainStage(Guid.NewGuid(), department.Id, "Fixture Main", 1); var model = new ProductModel(Guid.NewGuid(), "MODEL-A", "Model A"); var subStage = new SubStage(Guid.NewGuid(), mainStage.Id, "Sew", "SEW", 1, 1);
            var stage = new ProductModelStage(Guid.NewGuid(), model.Id, line.Id, subStage.Id, 1, price, seconds, Enum.Parse<CompensationMode>(mode));
            var a = new Worker(Guid.NewGuid(), "A", "Worker A"); var b = new Worker(Guid.NewGuid(), "B", "Worker B"); var c = new Worker(Guid.NewGuid(), "C", "Worker C"); var left = new Worker(Guid.NewGuid(), "L", "Left Worker", employmentStatus: EmploymentStatus.LeftEmployment);
            var actor = Guid.NewGuid();
            db.AddRange(factory, department, line, mainStage, model, subStage, stage, a, b, c, left, new AppUser(actor, "Audit User", $"audit-{actor:N}@example.test", "hash"));
            await db.SaveChangesAsync();
            var assignedAt = DateTime.UtcNow.AddMinutes(-1);
            db.AddRange(
                new WorkerDefaultAssignment(Guid.NewGuid(), a.Id, subStage.Id, actor, assignedAt, "Fixture assignment", productionLineId: line.Id),
                new WorkerDefaultAssignment(Guid.NewGuid(), b.Id, subStage.Id, actor, assignedAt, "Fixture assignment", productionLineId: line.Id),
                new WorkerDefaultAssignment(Guid.NewGuid(), c.Id, subStage.Id, actor, assignedAt, "Fixture assignment", productionLineId: line.Id));
            await db.SaveChangesAsync();
            var service = CreateService(db, audit ?? (useRealAudit ? new AuditEngine(db) : new RecordingAuditEngine()));
            var fixture = new Fixture(connection, db, factory, line, mainStage, model, subStage, stage, a, b, c, left, service, actor, DateOnly.FromDateTime(DateTime.UtcNow));
            fixture.Order = await service.CreateOrderAsync(new CreateProductionOrderRequest("PO-" + Guid.NewGuid().ToString("N"), model.Id, line.Id, fixture.Today, 500m, null), actor, default);
            await service.TransitionOrderAsync(fixture.Order.Id, ProductionOrderStatus.Active, actor, default);
            return fixture;
        }
        public WorkerAllocationRequest Allocation(Guid workerId, decimal? percentage = null, decimal? fixedAmount = null, string? manualOverrideReason = null) => new(workerId, percentage, fixedAmount, null, manualOverrideReason);
        public Client CreateClient()
        {
            var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(Connection).Options);
            return new Client(context, CreateService(context, new AuditEngine(context)));
        }
        public Task<StageProductionRecordDto> CreateDraftAsync(decimal produced, decimal accepted, decimal rejected, IReadOnlyCollection<WorkerAllocationRequest> workers) => Service.CreateDraftAsync(new CreateStageProductionRecordRequest(Order.Id, Stage.Id, Today, produced, accepted, rejected, Guid.NewGuid(), null, workers), ActorId, default);
        public async Task<StageProductionRecordDto> CreateAndApproveAsync(decimal accepted, IReadOnlyCollection<WorkerAllocationRequest> workers) { var draft = await CreateDraftAsync(accepted, accepted, 0m, workers); return await Service.ApproveAsync(draft.Id, draft.ConcurrencyToken, ActorId, default); }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await Connection.DisposeAsync(); }

        public ProductionCostRecordingService CreateService(IAuditEngine audit, IAttendanceEngine? attendance = null, IPermissionService? permissions = null) => CreateService(Db, audit, attendance, permissions);

        private static ProductionCostRecordingService CreateService(AppDbContext db, IAuditEngine audit, IAttendanceEngine? attendance = null, IPermissionService? permissions = null) =>
            new(db, audit, new AssignmentEngine(db, audit), attendance ?? new PresentAttendanceEngine(), permissions ?? new AssignmentOverridePermissionService(), TestCairoTimeZoneProvider.Instance);
    }
    private sealed class Client(AppDbContext db, ProductionCostRecordingService service) : IAsyncDisposable
    {
        public ProductionCostRecordingService Service { get; } = service;
        public ValueTask DisposeAsync() => db.DisposeAsync();
    }
    private sealed class FailingAuditEngine : IAuditEngine
    { public Task<Result> RecordAsync(Guid actorUserId, AuditActionType actionType, string entityType, string entityId, object? before = null, object? after = null, string? requestMeta = null, CancellationToken cancellationToken = default) => Task.FromResult(Result.Failure(new Error("AuditFailed", "Audit persistence failed."))); }

    private class PresentAttendanceEngine : IAttendanceEngine
    {
        public Task<Result<AttendanceWorkerStateDto[]>> GetTodayAttendanceAsync(Guid? factoryId, Guid? lineId, DateTime? dateUtc, CancellationToken cancellationToken = default) => Task.FromResult(Result<AttendanceWorkerStateDto[]>.Success([]));
        public Task<Result<AttendanceRecordDto[]>> GetWorkerAttendanceAsync(Guid workerId, DateTime? fromDateUtc, DateTime? toDateUtc, CancellationToken cancellationToken = default) => Task.FromResult(Result<AttendanceRecordDto[]>.Success([]));
        public Task<Result<AttendanceSubStageAttendanceDto>> GetSubStageAttendanceAsync(Guid subStageId, DateTime? dateUtc, CancellationToken cancellationToken = default) => Task.FromResult(Result<AttendanceSubStageAttendanceDto>.Success(new AttendanceSubStageAttendanceDto()));
        public Task<Result<AttendanceSyncResultDto>> SyncTodayAsync(CancellationToken cancellationToken = default) => Task.FromResult(Result<AttendanceSyncResultDto>.Success(new AttendanceSyncResultDto()));
        public Task<Result<AttendanceSyncResultDto>> SyncForProductionDateAsync(DateOnly productionDate, CancellationToken cancellationToken = default) => Task.FromResult(Result<AttendanceSyncResultDto>.Success(new AttendanceSyncResultDto()));
        public virtual Task<Result<Dictionary<Guid, AttendanceStatusRecord>>> GetLatestAttendanceStatusByWorkerAsync(IEnumerable<Guid> workerIds, DateTime? asOfUtc = null, CancellationToken cancellationToken = default) => Task.FromResult(Result<Dictionary<Guid, AttendanceStatusRecord>>.Success(workerIds.Distinct().ToDictionary(id => id, id => new AttendanceStatusRecord(id, AttendanceStatus.Present, asOfUtc ?? DateTime.UtcNow, "test"))));
        public virtual Task<Result<Dictionary<Guid, AttendancePresenceWindowDto>>> GetPresenceWindowsByWorkerAsync(IEnumerable<Guid> workerIds, DateOnly productionDate, CancellationToken cancellationToken = default)
        {
            var start = DateTime.SpecifyKind(productionDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            return Task.FromResult(Result<Dictionary<Guid, AttendancePresenceWindowDto>>.Success(workerIds.Distinct().ToDictionary(
                id => id,
                id => new AttendancePresenceWindowDto(id, AttendanceStatus.Present, start, start.AddDays(1), true))));
        }
    }

    private sealed class AssignmentOverridePermissionService : IPermissionService
    {
        public Task<IReadOnlyCollection<string>> GetEffectivePermissionsAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<string>>(["assignments.manage"]);
        public Task<PermissionCatalogItemDto[]> GetCatalogAsync(CancellationToken cancellationToken = default) => Task.FromResult<PermissionCatalogItemDto[]>([]);
    }

    private sealed class NoOverridePermissionService : IPermissionService
    {
        public Task<IReadOnlyCollection<string>> GetEffectivePermissionsAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<string>>([]);
        public Task<PermissionCatalogItemDto[]> GetCatalogAsync(CancellationToken cancellationToken = default) => Task.FromResult<PermissionCatalogItemDto[]>([]);
    }

    private sealed class AbsentAttendanceEngine : PresentAttendanceEngine
    {
        public override Task<Result<Dictionary<Guid, AttendanceStatusRecord>>> GetLatestAttendanceStatusByWorkerAsync(IEnumerable<Guid> workerIds, DateTime? asOfUtc = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<Dictionary<Guid, AttendanceStatusRecord>>.Success(workerIds.Distinct().ToDictionary(id => id, id => new AttendanceStatusRecord(id, AttendanceStatus.Absent, asOfUtc ?? DateTime.UtcNow, "test"))));
        public override Task<Result<Dictionary<Guid, AttendancePresenceWindowDto>>> GetPresenceWindowsByWorkerAsync(IEnumerable<Guid> workerIds, DateOnly productionDate, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<Dictionary<Guid, AttendancePresenceWindowDto>>.Success(workerIds.Distinct().ToDictionary(id => id, id => new AttendancePresenceWindowDto(id, AttendanceStatus.Absent, null, null, false))));
    }

    private sealed class NoCheckInAttendanceEngine : PresentAttendanceEngine
    {
        public override Task<Result<Dictionary<Guid, AttendanceStatusRecord>>> GetLatestAttendanceStatusByWorkerAsync(IEnumerable<Guid> workerIds, DateTime? asOfUtc = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<Dictionary<Guid, AttendanceStatusRecord>>.Success(new Dictionary<Guid, AttendanceStatusRecord>()));
        public override Task<Result<Dictionary<Guid, AttendancePresenceWindowDto>>> GetPresenceWindowsByWorkerAsync(IEnumerable<Guid> workerIds, DateOnly productionDate, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<Dictionary<Guid, AttendancePresenceWindowDto>>.Success(new Dictionary<Guid, AttendancePresenceWindowDto>()));
    }

    private sealed class IncompleteAttendanceEngine : PresentAttendanceEngine
    {
        public override Task<Result<Dictionary<Guid, AttendancePresenceWindowDto>>> GetPresenceWindowsByWorkerAsync(IEnumerable<Guid> workerIds, DateOnly productionDate, CancellationToken cancellationToken = default)
        {
            var firstIn = DateTime.SpecifyKind(productionDate.ToDateTime(new TimeOnly(8, 0)), DateTimeKind.Utc);
            return Task.FromResult(Result<Dictionary<Guid, AttendancePresenceWindowDto>>.Success(workerIds
                .Distinct()
                .ToDictionary(
                    workerId => workerId,
                    workerId => new AttendancePresenceWindowDto(workerId, AttendanceStatus.Present, firstIn, null, true))));
        }
    }

    private sealed class FailingDailyAttendanceEngine : PresentAttendanceEngine
    {
        public override Task<Result<Dictionary<Guid, AttendancePresenceWindowDto>>> GetPresenceWindowsByWorkerAsync(
            IEnumerable<Guid> workerIds,
            DateOnly productionDate,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<Dictionary<Guid, AttendancePresenceWindowDto>>.Failure(
                new Error("AttendanceUnavailable", "Synthetic attendance source failure.")));
    }

    private sealed class WindowAttendanceEngine(IReadOnlyDictionary<Guid, AttendancePresenceWindowDto> windows) : PresentAttendanceEngine
    {
        public override Task<Result<Dictionary<Guid, AttendancePresenceWindowDto>>> GetPresenceWindowsByWorkerAsync(IEnumerable<Guid> workerIds, DateOnly productionDate, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<Dictionary<Guid, AttendancePresenceWindowDto>>.Success(workerIds
                .Where(windows.ContainsKey)
                .ToDictionary(workerId => workerId, workerId => windows[workerId])));
    }
}

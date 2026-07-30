using Microsoft.AspNetCore.SignalR;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Application.Realtime;

namespace ProductionLinePlanner.Api.Realtime;

public sealed class SignalRManufacturingDataChangePublisher(
    IHubContext<NotificationsHub, IRealtimeClient> hubContext,
    IServiceScopeFactory scopeFactory,
    ILogger<SignalRManufacturingDataChangePublisher> logger) : IManufacturingDataChangePublisher
{
    public async Task PublishAsync(ManufacturingDataChanged change, CancellationToken cancellationToken = default)
    {
        if (change.EntityType == ManufacturingEntityType.AttendanceRecord &&
            change.AddedAttendanceCount <= 0 &&
            change.UpdatedAttendanceCount <= 0)
        {
            logger.LogDebug("Skipped empty manufacturing attendance notification {EventId}.", change.EventId);
            return;
        }

        var groups = ManufacturingRealtimeGroups.ForChange(change);
        if (groups.Count == 0) return;

        OperationalReadinessDeltaDto? readiness = null;
        if (groups.Contains(ManufacturingRealtimeGroups.ForScreen(ManufacturingRealtimeGroups.FactoryReadiness)))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var engine = scope.ServiceProvider.GetService<IOperationalReadinessEngine>();
                if (engine is not null)
                {
                    var result = await engine.GetDeltaAsync(change, cancellationToken);
                    if (result.IsSuccess) readiness = result.Value;
                    else logger.LogWarning("Operational readiness delta calculation failed for event {EventId}: {ErrorCode}.", change.EventId, result.Error?.Code);
                }
            }
            catch (Exception exception)
            {
                // Readiness enrichment must never suppress the existing generic
                // manufacturing invalidation delivered to other screens.
                logger.LogWarning(exception, "Unable to enrich manufacturing realtime event {EventId} with readiness deltas.", change.EventId);
            }
        }

        try
        {
            await hubContext.Clients.Groups(groups).ManufacturingDataChanged(ManufacturingDataChangedMessage.From(change, readiness));
        }
        catch (Exception exception)
        {
            // Realtime is an invalidation hint. The database transaction has
            // already committed and must not be reported as failed here.
            logger.LogWarning(exception, "Unable to publish manufacturing realtime event {EventId}.", change.EventId);
        }
    }
}

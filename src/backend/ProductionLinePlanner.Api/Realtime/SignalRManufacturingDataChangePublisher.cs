using Microsoft.AspNetCore.SignalR;
using ProductionLinePlanner.Application.Realtime;

namespace ProductionLinePlanner.Api.Realtime;

public sealed class SignalRManufacturingDataChangePublisher(
    IHubContext<NotificationsHub, IRealtimeClient> hubContext,
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

        try
        {
            await hubContext.Clients.Groups(groups).ManufacturingDataChanged(ManufacturingDataChangedMessage.From(change));
        }
        catch (Exception exception)
        {
            // Realtime is an invalidation hint. The database transaction has
            // already committed and must not be reported as failed here.
            logger.LogWarning(exception, "Unable to publish manufacturing realtime event {EventId}.", change.EventId);
        }
    }
}

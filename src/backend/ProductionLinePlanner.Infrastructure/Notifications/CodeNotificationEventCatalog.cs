using ProductionLinePlanner.Application.Notifications;
using ProductionLinePlanner.Domain.Notifications;

namespace ProductionLinePlanner.Infrastructure.Notifications;

public sealed class CodeNotificationEventCatalog : INotificationEventCatalog
{
    public IReadOnlyList<NotificationEventDefinition> GetAll() => NotificationEventCatalog.All;

    public NotificationEventDefinition? Find(string eventKey) => NotificationEventCatalog.Find(eventKey);
}

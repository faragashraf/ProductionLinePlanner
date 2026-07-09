using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;

namespace ProductionLinePlanner.Application.Abstractions;

public interface INotificationService
{
    Task<Result> SendAsync(NotificationDto notification, CancellationToken cancellationToken = default);
}

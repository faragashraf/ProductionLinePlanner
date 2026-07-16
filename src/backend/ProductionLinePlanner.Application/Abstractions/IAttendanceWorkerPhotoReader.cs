using ProductionLinePlanner.Application.Common;

namespace ProductionLinePlanner.Application.Abstractions;

/// <summary>
/// Read-only access to ZKTime worker photos. Implementations must never mutate USERINFO.PHOTO.
/// </summary>
public interface IAttendanceWorkerPhotoReader
{
    Task<Result<AttendanceWorkerPhotoRecord[]>> GetAllCurrentPhotosAsync(CancellationToken cancellationToken = default);

    Task<Result<AttendanceWorkerPhotoRecord?>> GetPhotoByAttendanceUserIdAsync(string attendanceUserId, CancellationToken cancellationToken = default);
}

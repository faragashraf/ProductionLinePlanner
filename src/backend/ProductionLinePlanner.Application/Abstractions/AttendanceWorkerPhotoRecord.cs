namespace ProductionLinePlanner.Application.Abstractions;

/// <summary>
/// Binary content is used only inside the protected synchronization/photo pipeline and is never serialized in worker DTOs.
/// </summary>
public sealed record AttendanceWorkerPhotoRecord(string AttendanceUserId, byte[] Photo);

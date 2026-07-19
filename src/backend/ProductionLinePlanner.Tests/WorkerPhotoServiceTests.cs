using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProductionLinePlanner.Application.Abstractions;
using ProductionLinePlanner.Application.Workers;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Data;
using ProductionLinePlanner.Infrastructure.Workers;

namespace ProductionLinePlanner.Tests;

public sealed class WorkerPhotoServiceTests
{
    [Fact]
    public async Task Upload_stores_a_local_hash_versioned_photo_and_audits_without_binary_content()
    {
        await using var fixture = await Fixture.CreateAsync();
        var photo = WorkerPhotoTestData.CreateBitmap();

        var result = await fixture.UploadAsync(photo, "image/bmp");

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Created);
        Assert.False(result.Value.Replaced);
        Assert.False(result.Value.Unchanged);
        Assert.Equal(64, result.Value.Photo.Version.Length);
        Assert.Equal($"/api/workers/{fixture.Worker.Id:D}/photo?v={result.Value.Photo.Version}", result.Value.Photo.PhotoReference);
        Assert.Equal(result.Value.Photo.PhotoReference, fixture.Worker.PhotoReference);
        Assert.Equal(1, fixture.Storage.StoreCalls);
        var audit = Assert.Single(fixture.Audit.Calls);
        Assert.Equal(AuditActionType.Create, audit.ActionType);
        Assert.Equal("WorkerPhoto", audit.EntityType);
        Assert.NotNull(audit.After);
        Assert.DoesNotContain(Convert.ToBase64String(photo), audit.After!.ToString(), StringComparison.Ordinal);

        var download = await fixture.Service.DownloadAsync(fixture.Worker.Id, result.Value.Photo.Version);
        Assert.True(download.IsSuccess);
        Assert.Equal(photo, download.Value!.Content);
        Assert.Equal("image/bmp", download.Value.ContentType);
    }

    [Fact]
    public async Task Upload_accepts_the_allowed_jpeg_png_and_bmp_formats()
    {
        var photos = new[]
        {
            (Content: WorkerPhotoTestData.CreateJpeg(), ContentType: "image/jpeg"),
            (Content: WorkerPhotoTestData.CreatePng(), ContentType: "image/png"),
            (Content: WorkerPhotoTestData.CreateBitmap(), ContentType: "image/bmp")
        };

        foreach (var photo in photos)
        {
            await using var fixture = await Fixture.CreateAsync();
            var result = await fixture.UploadAsync(photo.Content, photo.ContentType);
            Assert.True(result.IsSuccess);
            Assert.Equal(photo.ContentType, result.Value!.Photo.ContentType);
        }
    }

    [Fact]
    public async Task Upload_rejects_invalid_or_unsupported_content_before_storage()
    {
        await using var fixture = await Fixture.CreateAsync();
        var htmlDisguisedAsImage = "<html>not an image</html>"u8.ToArray();

        var result = await fixture.UploadAsync(htmlDisguisedAsImage, "image/png");

        Assert.True(result.IsFailure);
        Assert.Equal("UnsupportedPhotoType", result.Error!.Code);
        Assert.Equal(0, fixture.Storage.StoreCalls);
        Assert.Empty(fixture.Audit.Calls);
        Assert.Null(fixture.Worker.PhotoReference);
    }

    [Fact]
    public async Task Upload_rejects_declared_content_type_that_does_not_match_magic_bytes()
    {
        await using var fixture = await Fixture.CreateAsync();
        var jpeg = WorkerPhotoTestData.CreateJpeg();

        var result = await fixture.UploadAsync(jpeg, "image/png");

        Assert.True(result.IsFailure);
        Assert.Equal("UnsupportedPhotoType", result.Error!.Code);
        Assert.Equal(0, fixture.Storage.StoreCalls);
    }

    [Fact]
    public async Task Upload_rejects_oversized_input_before_reading_or_storage()
    {
        await using var fixture = await Fixture.CreateAsync();
        var photo = WorkerPhotoTestData.CreateJpeg();
        await using var content = new MemoryStream(photo);

        var result = await fixture.Service.UploadAsync(
            fixture.Worker.Id,
            content,
            WorkerPhotoFormat.MaximumBytes + 1L,
            "image/jpeg",
            fixture.ActorUserId);

        Assert.True(result.IsFailure);
        Assert.Equal("PhotoTooLarge", result.Error!.Code);
        Assert.Equal(0, content.Position);
        Assert.Equal(0, fixture.Storage.StoreCalls);
    }

    [Fact]
    public async Task Upload_stops_a_stream_that_exceeds_the_limit_even_when_declared_length_lies()
    {
        await using var fixture = await Fixture.CreateAsync();
        var oversized = new byte[WorkerPhotoFormat.MaximumBytes + 1];
        await using var content = new MemoryStream(oversized);

        var result = await fixture.Service.UploadAsync(
            fixture.Worker.Id,
            content,
            declaredLength: 1,
            declaredContentType: "image/jpeg",
            actorUserId: fixture.ActorUserId);

        Assert.True(result.IsFailure);
        Assert.Equal("PhotoTooLarge", result.Error!.Code);
        Assert.Equal(0, fixture.Storage.StoreCalls);
        Assert.Empty(fixture.Audit.Calls);
    }

    [Fact]
    public async Task Replace_switches_the_db_pointer_then_removes_the_obsolete_version_and_busts_cache()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.UploadAsync(WorkerPhotoTestData.CreateJpeg(0x01), "image/jpeg");
        var second = await fixture.UploadAsync(WorkerPhotoTestData.CreateJpeg(0x02), "image/jpeg");

        Assert.True(second.IsSuccess);
        Assert.True(second.Value!.Replaced);
        Assert.NotEqual(first.Value!.Photo.Version, second.Value.Photo.Version);
        Assert.NotEqual(first.Value.Photo.PhotoReference, second.Value.Photo.PhotoReference);
        Assert.Equal(second.Value.Photo.PhotoReference, fixture.Worker.PhotoReference);
        Assert.Equal(1, fixture.Storage.DeleteCalls);
        Assert.Equal(2, fixture.Audit.Calls.Count);
        Assert.Equal("NotFound", (await fixture.Service.DownloadAsync(fixture.Worker.Id, first.Value.Photo.Version)).Error!.Code);
        Assert.True((await fixture.Service.DownloadAsync(fixture.Worker.Id, second.Value.Photo.Version)).IsSuccess);
    }

    [Fact]
    public async Task Reuploading_identical_content_is_unchanged_and_does_not_duplicate_audit()
    {
        await using var fixture = await Fixture.CreateAsync();
        var photo = WorkerPhotoTestData.CreatePng();
        var first = await fixture.UploadAsync(photo, "image/png");
        var second = await fixture.UploadAsync(photo, "application/octet-stream");

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.True(second.Value!.Unchanged);
        Assert.False(second.Value.Created);
        Assert.False(second.Value.Replaced);
        Assert.Single(fixture.Audit.Calls);
        Assert.Equal(0, fixture.Storage.DeleteCalls);
    }

    [Fact]
    public async Task Delete_clears_the_authoritative_reference_audits_and_makes_download_missing()
    {
        await using var fixture = await Fixture.CreateAsync();
        var upload = await fixture.UploadAsync(WorkerPhotoTestData.CreateBitmap(), "image/bmp");

        var result = await fixture.Service.DeleteAsync(fixture.Worker.Id, fixture.ActorUserId, "DELETE test");

        Assert.True(result.IsSuccess);
        Assert.Null(fixture.Worker.PhotoReference);
        Assert.Equal(1, fixture.Storage.DeleteCalls);
        Assert.Equal(AuditActionType.Delete, fixture.Audit.Calls.Last().ActionType);
        var missing = await fixture.Service.DownloadAsync(fixture.Worker.Id, upload.Value!.Photo.Version);
        Assert.True(missing.IsFailure);
        Assert.Equal("NotFound", missing.Error!.Code);
    }

    [Fact]
    public async Task Missing_photo_returns_not_found_without_touching_storage_for_placeholder_fallback()
    {
        await using var fixture = await Fixture.CreateAsync();

        var download = await fixture.Service.DownloadAsync(fixture.Worker.Id);
        var delete = await fixture.Service.DeleteAsync(fixture.Worker.Id, fixture.ActorUserId);

        Assert.Equal("NotFound", download.Error!.Code);
        Assert.Equal("NotFound", delete.Error!.Code);
        Assert.Equal(0, fixture.Storage.ReadCalls);
        Assert.Equal(0, fixture.Storage.DeleteCalls);
        Assert.Empty(fixture.Audit.Calls);
    }

    [Fact]
    public async Task First_local_upload_replaces_an_untrusted_legacy_reference_without_any_zktime_dependency()
    {
        var worker = new Worker(Guid.NewGuid(), "119", "Worker 119", photoReference: "zktime://USERINFO/119/PHOTO");
        await using var fixture = await Fixture.CreateAsync(worker);

        var result = await fixture.UploadAsync(WorkerPhotoTestData.CreateJpeg(), "image/jpeg");

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Created);
        Assert.StartsWith($"/api/workers/{worker.Id:D}/photo?v=", worker.PhotoReference, StringComparison.Ordinal);
        var constructorDependencies = typeof(WorkerPhotoService).GetConstructors().Single().GetParameters().Select(x => x.ParameterType);
        Assert.DoesNotContain(typeof(IAttendanceWorkerPhotoReader), constructorDependencies);
    }

    [Fact]
    public async Task Write_operations_require_an_actor_context()
    {
        await using var fixture = await Fixture.CreateAsync();
        var photo = WorkerPhotoTestData.CreateJpeg();
        await using var content = new MemoryStream(photo);

        var upload = await fixture.Service.UploadAsync(
            fixture.Worker.Id,
            content,
            photo.Length,
            "image/jpeg",
            Guid.Empty);
        var delete = await fixture.Service.DeleteAsync(fixture.Worker.Id, Guid.Empty);

        Assert.Equal("Unauthorized", upload.Error!.Code);
        Assert.Equal("Unauthorized", delete.Error!.Code);
        Assert.Equal(0, fixture.Storage.StoreCalls);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(
            AppDbContext db,
            Worker worker,
            InMemoryWorkerPhotoStorage storage,
            RecordingAuditEngine audit)
        {
            Db = db;
            Worker = worker;
            Storage = storage;
            Audit = audit;
            Service = new WorkerPhotoService(db, storage, audit, NullLogger<WorkerPhotoService>.Instance);
        }

        public AppDbContext Db { get; }
        public Worker Worker { get; }
        public InMemoryWorkerPhotoStorage Storage { get; }
        public RecordingAuditEngine Audit { get; }
        public WorkerPhotoService Service { get; }
        public Guid ActorUserId { get; } = Guid.NewGuid();

        public static async Task<Fixture> CreateAsync(Worker? worker = null)
        {
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options);
            worker ??= new Worker(Guid.NewGuid(), "119", "Worker 119", attendanceUserId: "1", badgeNumber: "119");
            db.Workers.Add(worker);
            await db.SaveChangesAsync();
            return new Fixture(db, worker, new InMemoryWorkerPhotoStorage(), new RecordingAuditEngine());
        }

        public async Task<ProductionLinePlanner.Application.Common.Result<ProductionLinePlanner.Application.Services.WorkerPhotoChangeResult>> UploadAsync(
            byte[] photo,
            string contentType)
        {
            await using var content = new MemoryStream(photo);
            return await Service.UploadAsync(
                Worker.Id,
                content,
                photo.LongLength,
                contentType,
                ActorUserId,
                "PUT test");
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}

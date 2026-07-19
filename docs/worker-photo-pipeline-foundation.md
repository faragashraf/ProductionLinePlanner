# Worker Photo Pipeline Foundation

## Ownership and source boundary

- ProductionLinePlanner is the source of truth and sole owner of every approved local worker photo.
- ZKTime `USERINFO.PHOTO` remains read-only. The local pipeline has no ZKTime dependency and no write path to the attendance database.
- A future explicitly approved first import may read a ZKTime photo, but approval must persist it through `IWorkerPhotoService` as a local object. Later source reads must never overwrite it.

## Storage decision

The foundation uses a hybrid model without a schema migration:

- `Workers.PhotoReference` is the authoritative DB pointer and local URL.
- `IWorkerPhotoStorage` owns binary content outside `wwwroot`.
- New objects are content-addressed with the full SHA-256 hash and stored under generated worker/version keys.
- The default root is `<application-base>/App_Data/worker-photos`. `WorkerPhotos:RootPath` can set the parent data directory. The provider refuses a configured path under the application `wwwroot`; the previous `WorkerPhotoCache:RootPath` key remains a read-compatible fallback.
- In production, set `WorkerPhotos:RootPath` to a durable, private filesystem location and grant only the API process read/write access. The default application-base directory is suitable for local development, not for disposable deployment storage.
- Legacy single-file cache objects and 16-character hash references remain readable only when the file hash matches the reference.

The local URL is:

`/api/workers/{workerId}/photo?v={sha256}`

No static-file middleware serves the storage directory. This keeps authorization on every download.

## API and authorization

| Method | Route | Permission | Purpose |
| --- | --- | --- | --- |
| `GET` | `/api/workers/{id}/photo?v={version}` | `workers.view` | Protected inline download |
| `PUT` | `/api/workers/{id}/photo` | `workers.manage` | Create or replace from multipart field `photo` |
| `DELETE` | `/api/workers/{id}/photo` | `workers.manage` | Clear the DB pointer and remove the current local object |

Reads and writes have separate rate-limit policies. Uploads also have request/form size limits.

## Validation and security

- Maximum file size: 5 MiB, enforced at request metadata and while reading the stream.
- Allowed formats: JPEG, PNG, and BMP.
- File names and extensions are never trusted or used in storage paths.
- Declared content type must match detected content; `application/octet-stream` is accepted only when the binary format is valid.
- PNG chunk structure/CRC, JPEG marker structure, and BMP header bounds are validated.
- SVG and GIF are not accepted.
- SHA-256 is recomputed during storage and reads; corrupt or mismatched content is not served.
- Missing workers and missing photos use the same not-found response to limit enumeration detail.
- Audit records contain actor, action, worker id, reference, version, type, length, source, and request metadata; binary content is never audited.

## Replace, delete, cache, and placeholder behavior

- Upload stores an immutable version first, then atomically saves the DB pointer and audit row through the application DbContext.
- A failed DB write can leave an unreachable object, but cannot create a DB pointer to missing content. This favors availability and integrity over unsafe rollback across DB/filesystem boundaries.
- Replace switches the pointer before best-effort cleanup of the previous version.
- Delete clears and audits the DB pointer before best-effort physical cleanup, so deleted content cannot be downloaded even if cleanup is interrupted.
- Writes for the same worker are serialized inside the application process. The local provider and in-memory lock are therefore a single-instance foundation; before horizontal scale, replace the provider with shared/managed storage and coordinate per-worker writes with a distributed lock.
- Versioned URLs provide cache busting. Downloads return a strong hash ETag with `private, no-cache, must-revalidate`, requiring authorization revalidation before browser reuse.
- Missing or corrupt content returns `404` with `Cache-Control: no-store`; DTOs expose `HasPhoto=false`/no reference when there is no managed photo, allowing the existing local avatar placeholder.

## Deliberately excluded

- Crop, resize, compression, and background optimization.
- Worker Sync changes or automatic source-photo replacement.
- Static/public photo URLs.
- Frontend photo editing or redesign.

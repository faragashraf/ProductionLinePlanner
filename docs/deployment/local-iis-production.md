# Local IIS production deployment

This runbook deploys the current application to a private LAN IIS server. It is **not** an Internet-facing HTTPS deployment.

## Confirmed IIS topology

| Workload | IIS site | HTTP binding | Physical path | App pool |
| --- | --- | --- | --- | --- |
| Angular frontend | `Dayoub` | `http://192.168.1.99:8000` | `C:\inetpub\wwwroot\Dayoub\app` | `Dayoub` |
| ASP.NET Core API | `DayoubApi` | `http://192.168.1.99:9000` | `C:\inetpub\wwwroot\Dayoub\api` | `DayoubBackend` |

`app` and `api` are filesystem paths only. They are not URL prefixes.

- Frontend URL: `http://192.168.1.99:8000/`
- Backend URL: `http://192.168.1.99:9000/`
- Angular base href: `/`
- Production API and hub origin: `http://192.168.1.99:9000`
- CORS origin: `http://192.168.1.99:8000` only

## Server prerequisites

1. Install IIS with **Static Content**.
2. Install the IIS **URL Rewrite Module**. It is required for Angular route refreshes.
3. Install the .NET 8 ASP.NET Core Hosting Bundle matching the published runtime.
4. Configure `DayoubBackend` as **No Managed Code**, Integrated pipeline, and 64-bit enabled.
5. Configure both sites with their confirmed binding, physical path, and app pool names above.
6. Allow inbound TCP `8000` and `9000` only from the required LAN range.
7. Grant the `DayoubBackend` app-pool identity Read/Execute on `api`; grant write only to a proven runtime folder. Grant the `Dayoub` identity Read/Execute on `app`. Do not grant `Everyone` Full Control.

The current repository has no `AddSignalR`/`MapHub` usage, so WebSocket Protocol is not currently required. Enable it before introducing SignalR.

## Production configuration and secrets

`appsettings.Production.json` is published and deliberately contains no secrets. It configures the exact LAN CORS origin and disables HSTS/HTTPS redirection for this HTTP-only local deployment.

Before the first deployment, configure these values outside Git as machine or IIS environment variables for `DayoubBackend`:

- `ConnectionStrings__AppDatabase`
- `ConnectionStrings__AttendanceDatabase` when attendance integration is enabled
- `AttendanceSource__ConnectionString` when attendance integration is enabled
- `Authentication__Jwt__SigningKey` (minimum 64 bytes)
- `Bootstrap__Secret` when the bootstrap endpoint is used

Never put these values in `appsettings.Production.json`, package scripts, or the deployment script. ASP.NET Core defaults to the `Production` environment when no development environment variable is configured. Ensure IIS does not set `ASPNETCORE_ENVIRONMENT=Development`.

The application does not execute database migrations at startup. Database changes remain an explicit, separately approved operation. Do not run the migration scripts as part of deployment.

Swagger is Development-only. The API uses its generic exception handler in Production and does not expose developer exception pages.

## Build artifacts

Run from any development machine with .NET 8 SDK, Node.js, and the locked frontend dependencies:

```bash
cd src/frontend
npm ci
npm run build:prod:iis
npm run verify:prod:iis
```

Artifacts are ignored by Git:

- `artifacts/iis/frontend`: the contents to deploy to `...\Dayoub\app`, including Angular `web.config`.
- `artifacts/iis/backend`: framework-dependent .NET 8 publish output for `...\Dayoub\api`, including generated `web.config`.

The frontend build stages the contents of Angular's `browser` output directly into `artifacts/iis/frontend`; do not deploy an extra `browser` directory.

### Manual transfer package

For a macOS build host that does not have IIS access, build and verify the artifacts as above, then package the two deployable directories only:

```bash
cd <repository-root>/artifacts/iis
zip -r ../Dayoub-IIS-Production.zip frontend backend
shasum -a 256 ../Dayoub-IIS-Production.zip > ../Dayoub-IIS-Production.sha256
```

Transfer `artifacts/Dayoub-IIS-Production.zip` and its `.sha256` file to the Windows Server. After validating the checksum, extract it and copy the **contents** of `frontend` to `C:\inetpub\wwwroot\Dayoub\app` and the **contents** of `backend` to `C:\inetpub\wwwroot\Dayoub\api`. Back up both IIS targets, stop the application pools, configure the required external secrets, copy the contents, then start the pools and run the smoke checklist. The archive is intentionally ignored by Git and contains no deployment secrets.

## IIS deployment and rollback

Copy the repository/artifacts to the Windows IIS server, then run an elevated PowerShell session from the repository root:

```powershell
cd <repository-root>\src\frontend
npm run deploy:prod:iis
```

The script validates IIS site names, bindings, physical paths, app pools, and artifacts. It creates a timestamped backup below:

```text
C:\inetpub\wwwroot\Dayoub\backups\<timestamp>\app
C:\inetpub\wwwroot\Dayoub\backups\<timestamp>\api
```

It stops both pools, places `app_offline.htm` for the API, mirrors only into the confirmed `app` and `api` targets, starts the pools, and runs frontend and health smokes. It never deletes the `backups` directory. Ignored `appsettings.*.local.json` files are preserved, but they are not loaded by the current application unless explicitly added to configuration later.

Dry-run validation:

```powershell
.\scripts\deploy-iis-production.ps1 -DryRun
```

Rollback to a known backup:

```powershell
.\scripts\deploy-iis-production.ps1 -RollbackTo 'C:\inetpub\wwwroot\Dayoub\backups\20260718-120000'
```

## Smoke checklist

1. `http://192.168.1.99:9000/api/health` returns `200`.
2. `http://192.168.1.99:8000/` serves the Angular app.
3. Refresh an authenticated internal Angular route; it must not return IIS `404`.
4. Log in, open Daily Production, Reports quantities, and Financial mode.
5. Confirm browser requests use `http://192.168.1.99:9000/api/...`, CORS permits the frontend origin, and there are no console/network errors.

HTTP is acceptable only for this private LAN deployment. Configure a certificate, HTTPS bindings, HSTS, and `Hosting:EnableHttpsRedirection=true` before any external exposure.

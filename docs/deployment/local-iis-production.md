# Local IIS production deployment

This runbook deploys the current application to a private LAN IIS server. It is **not** an Internet-facing HTTPS deployment.

## Target IIS topology

| Workload | IIS site | HTTP binding | Physical path | App pool |
| --- | --- | --- | --- | --- |
| Angular frontend | Chosen after port-80 inspection | `http://dayoub.local/app/` | IIS application `/app` at `C:\inetpub\wwwroot\app` | Selected application's pool |
| ASP.NET Core API | `DayoubApi` | `http://192.168.1.99:9000` | `C:\inetpub\wwwroot\Dayoub\api` | `DayoubBackend` |

`api` is a filesystem path only. `app` is an IIS application and browser URL prefix at `/app/`.

- Frontend URL: `http://dayoub.local/app/`
- Backend URL: `http://192.168.1.99:9000/`
- Angular base href: `/app/`
- Production API and hub origin: `http://192.168.1.99:9000`
- CORS origin: `http://dayoub.local` only

`app` is both the IIS application path and the filesystem folder. The browser URL is `/app/`, while its IIS application root is `C:\inetpub\wwwroot\app`; do not deploy an extra `browser` or `frontend` directory below it.

## Resolve port-80 ownership before deployment

The previous server inspection showed `Default Web Site` on `192.168.1.99:80` and `Dayoub` on port 8000. Do not move the `Dayoub` binding blindly. Run this read-only inspection as Administrator on the Windows Server and preserve its output with the deployment record:

```powershell
cd <repository-root>
.\scripts\inspect-iis-frontend-binding.ps1 -FrontendSiteName 'Dayoub'
```

The repository cannot establish whether `Default Web Site` hosts another application, so it does not select a final IIS site automatically.

Before deployment, verify host resolution and the exact IIS binding on the Windows Server:

```powershell
Resolve-DnsName dayoub.local
Get-WebBinding -Name 'Dayoub' -Protocol http | Select-Object protocol, bindingInformation
Invoke-WebRequest http://dayoub.local/app/ -UseBasicParsing
```

The selected frontend site must expose an HTTP binding whose host header is `dayoub.local` (or a deliberate port-80 catch-all binding that is confirmed to serve this host). Do not remove another site's port-80 binding until these checks pass.

### Scenario A: add `/app` to `Dayoub`

Only after the owner confirms that `Dayoub` should own the hostname/binding on port 80:

```powershell
Import-Module WebAdministration
New-WebBinding -Name 'Dayoub' -Protocol http -Port 80 -HostHeader 'dayoub.local'
New-Item -ItemType Directory -Path 'C:\inetpub\wwwroot\app' -Force
New-WebApplication -Site 'Dayoub' -Name 'app' -PhysicalPath 'C:\inetpub\wwwroot\app' -ApplicationPool 'Dayoub'
```

Keep the previous frontend binding until the `/app/` login and deep-route smoke tests pass.

### Scenario B: add `/app` to `Default Web Site`

Use this only after the owner confirms that an application under `/app` will not conflict with an existing `/app` application:

```powershell
Import-Module WebAdministration
New-Item -ItemType Directory -Path 'C:\inetpub\wwwroot\app' -Force
New-WebApplication -Site 'Default Web Site' -Name 'app' -PhysicalPath 'C:\inetpub\wwwroot\app' -ApplicationPool 'DefaultAppPool'
```

A hostname-based site is valid only if `dayoub.local` resolves to this IIS server. It must expose the `/app` application; it is not a replacement for that application.

## Server prerequisites

1. Install IIS with **Static Content**.
2. Install the IIS **URL Rewrite Module**. It is required for Angular route refreshes.
3. Install the .NET 8 ASP.NET Core Hosting Bundle matching the published runtime.
4. Configure `DayoubBackend` as **No Managed Code**, Integrated pipeline, and 64-bit enabled.
5. Configure both sites with their confirmed binding, physical path, and app pool names above.
6. Allow inbound TCP `80` and `9000` only from the required LAN range. Retire port 8000 only after the `/app/` URL succeeds.
7. Grant the `DayoubBackend` app-pool identity Read/Execute on `api`; grant write only to a proven runtime folder. Grant the selected frontend pool identity Read/Execute on `C:\inetpub\wwwroot\app`. Do not grant `Everyone` Full Control.

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

- `artifacts/iis/frontend`: the contents to deploy to `C:\inetpub\wwwroot\app`, including Angular `web.config`.
- `artifacts/iis/backend`: framework-dependent .NET 8 publish output for `...\Dayoub\api`, including generated `web.config`.

The frontend build stages the contents of Angular's `browser` output directly into `artifacts/iis/frontend`; do not deploy an extra `browser` directory.

### Manual transfer package

For a macOS build host that does not have IIS access, build and verify the artifacts as above, then package the two deployable directories only:

```bash
cd <repository-root>/artifacts/iis
zip -r ../Dayoub-IIS-Production.zip frontend backend
shasum -a 256 ../Dayoub-IIS-Production.zip > ../Dayoub-IIS-Production.sha256
```

Transfer `artifacts/Dayoub-IIS-Production.zip` and its `.sha256` file to the Windows Server. After validating the checksum, extract it and copy the **contents** of `frontend` to `C:\inetpub\wwwroot\app` and the **contents** of `backend` to `C:\inetpub\wwwroot\Dayoub\api`. Back up both IIS targets, stop only the selected frontend pool and `DayoubBackend`, configure the required external secrets, copy the contents, then start the pools and run the smoke checklist. The archive is intentionally ignored by Git and contains no deployment secrets.

## IIS deployment and rollback

Copy the repository/artifacts to the Windows IIS server, then run an elevated PowerShell session from the repository root:

```powershell
cd <repository-root>
.\scripts\deploy-iis-production.ps1 -FrontendSiteName 'Dayoub'
```

The script requires the selected frontend site name. It validates its HTTP port-80 binding, IIS application `/app`, application pool, and artifacts. Configure the IIS `/app` application through one approved scenario above before deployment. It creates a timestamped backup below:

```text
C:\inetpub\wwwroot\Dayoub\backups\<timestamp>\app
C:\inetpub\wwwroot\Dayoub\backups\<timestamp>\api
```

It stops both pools, places `app_offline.htm` for the API, mirrors only into the confirmed `app` and `api` targets, starts the pools, and runs frontend and health smokes. It never deletes the `backups` directory. Ignored `appsettings.*.local.json` files are preserved, but they are not loaded by the current application unless explicitly added to configuration later.

Dry-run validation:

```powershell
.\scripts\deploy-iis-production.ps1 -FrontendSiteName 'Dayoub' -DryRun
```

Rollback to a known backup:

```powershell
.\scripts\deploy-iis-production.ps1 -FrontendSiteName 'Dayoub' -RollbackTo 'C:\inetpub\wwwroot\Dayoub\backups\20260718-120000'
```

## Smoke checklist

1. `http://192.168.1.99:9000/api/health` returns `200`.
2. `http://dayoub.local/app/` serves the Angular app with `<base href="/app/">`.
3. Request deployed `main-*.js`, `polyfills-*.js`, `styles-*.css`, and `assets/brand/manifest.webmanifest` below `http://dayoub.local/app/`; all must return `200`.
4. Refresh an authenticated internal Angular route; it must not return IIS `404`.
5. Log in, open Daily Production, Reports quantities, and Financial mode.
6. Confirm browser requests use `http://192.168.1.99:9000/api/...`, CORS permits the frontend origin, and there are no console/network errors.
7. Confirm no application was deployed into `C:\inetpub\wwwroot\app\browser` or `C:\inetpub\wwwroot\app\frontend`.

## Binding and content rollback

The deployment script restores frontend/backend contents from the selected backup. If the `/app` IIS application itself must be removed or re-created, reverse the approved scenario only after confirming the old application content exists:

```powershell
Import-Module WebAdministration
Remove-WebApplication -Site 'Dayoub' -Name 'app'
```

If Scenario B created the application under `Default Web Site`, remove only that `/app` application or restore its prior physical path, then restart its application pool. Do not remove unrelated bindings or sites.

HTTP is acceptable only for this private LAN deployment. Configure a certificate, HTTPS bindings, HSTS, and `Hosting:EnableHttpsRedirection=true` before any external exposure.

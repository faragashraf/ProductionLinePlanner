# Production Line Planner

## Backend setup

### Prerequisites
- .NET 8 SDK (or compatible runtime installed)
- Git

### Run backend

```bash
cd src/backend
dotnet restore
dotnet build ProductionLinePlanner.sln
dotnet run --project ProductionLinePlanner.Api
```

- API: `http://localhost:5xxx` (as printed by ASP.NET Core hosting)
- Health check: `/api/health`
- Swagger: available in Development only.

### Folder structure

- `src/backend/ProductionLinePlanner.sln`
- `src/backend/ProductionLinePlanner.Api/`
- `src/backend/ProductionLinePlanner.Application/`
- `src/backend/ProductionLinePlanner.Domain/`
- `src/backend/ProductionLinePlanner.Infrastructure/`

## Secret handling

لا تضع أي secrets حقيقية داخل Git.

- استخدم placeholders في الملفات التالية:
  - `src/backend/ProductionLinePlanner.Api/appsettings.json`
  - `src/backend/ProductionLinePlanner.Api/appsettings.Development.json`
- القيم المطلوبة في الأساس:
  - قاعدة بيانات نظام العمل: `FactoryPlannerDB` باستخدام `ConnectionStrings:AppDatabase`
    (`ConnectionStrings:AppDatabase = REPLACE_WITH_USER_SECRET`)
  - إعدادات قاعدة بيانات الحضور غير مفعلة بهذا المِلْف في هذه المرحلة

ملاحظات:
- احتفظ بملفات secrets محليًا فقط (أو عبر Secret Manager / environment variables).
- لا ترفع ملفات مثل `appsettings.*.local.json` أو ملفات user-specific إلى المستودع.

لتفعيل الاتصال الفعلي بقاعدة بيانات البرنامج بدون إضافة secrets في Git:

```bash
dotnet user-secrets set "ConnectionStrings:AppDatabase" "<real-app-db-connection-string>"
```

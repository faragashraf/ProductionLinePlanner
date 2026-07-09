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

## إعداد FactoryPlannerDB migration (آمن، بدون سر موجود في Git)

### 1) إنشاء قاعدة FactoryPlannerDB

شغّل على سيرفر SQL Server الخاص بك:

```sql
CREATE DATABASE [FactoryPlannerDB];
```

### 2) إعداد `ConnectionStrings:AppDatabase` عبر user-secrets أو environment

يفضل وضع سلسلة الاتصال من خارج الملفات المدفوعة.

- user-secrets (موصى به):

```bash
cd src/backend/ProductionLinePlanner.Api
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:AppDatabase" "<connection_string>"
```

- environment variable (بديل سريع):

```bash
export ConnectionStrings__AppDatabase="<connection_string>"
```

> ملاحظة: لا تضع قيم مثل `Server=`, `User=`, `Password=` في الملفات الملحقة بالمشروع.

### 3) تشغيل migration على FactoryPlannerDB باستخدام dotnet ef

#### فحص أدوات dotnet EF

```bash
cd .
dotnet tool restore
```

#### تحديث قاعدة FactoryPlannerDB

```bash
cd src/backend
dotnet tool run dotnet-ef database update \
  --project ProductionLinePlanner.Infrastructure \
  --startup-project ProductionLinePlanner.Api \
  --context AppDbContext
```

أو عبر السكربت الآمن:

```bash
cd .
bash scripts/backend/appdb-migrate.sh
```

### 4) تشغيل SQL سكربت يدويًا (بديل)

إذا أردت تشغيل نص SQL مباشرة:

```bash
cd .
sqlcmd -S "<server>" -d "FactoryPlannerDB" -E \
  -i "src/backend/database/scripts/FactoryPlannerDB_InitialCreate.sql"
```

بدل `-E` بـ خيارات المستخدم/الرقم السري المناسبة في بيئتك.

### 5) تحذير مهم: Attendance DB read-only

- قاعدة `Attendance`/`AttendanceDatabase` للقراءة فقط حسب المرحلة الحالية.
- ممنوع تمامًا تشغيل أي migration أو `dotnet ef` عليها في هذا المسار.
- السكربتات/الأوامر أعلاه تقصد فقط `FactoryPlannerDB` و`AppDbContext`.

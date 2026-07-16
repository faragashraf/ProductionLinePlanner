# حزمة مخطط SQL Server 2016

هذه الحزمة مخصصة لتهيئة **مخطط قاعدة البيانات فقط** لتطبيق ProductionLinePlanner على SQL Server 2016. لا تنقل هذه المرحلة بيانات الإنتاج أو صور العمال أو كلمات المرور.

## الملفات

- `001-create-schema.sql`: سكربت EF Core idempotent من أول migration إلى آخر migration. ينشئ الجداول والعلاقات والفهارس وسجل `__EFMigrationsHistory`، ولا ينشئ قاعدة بيانات.
- `002-verify-schema.sql`: فحوص قراءة فقط للجداول والعلاقات والفهارس وسجل migrations وأنواع الأعمدة والعلاقات اليتيمة.
- `../../scripts/sql2016/apply-schema.sh`: مشغّل متحفظ (fail-closed) يقرأ سر الهدف من User Secrets، ويتحقق من SQL Server 2016 ومن فراغ قاعدة البيانات قبل التطبيق.

## حفظ سر قاعدة الهدف محلياً

مشروع API هو مالك `UserSecretsId`. نفّذ الأمر محلياً من جذر المستودع، ثم اكتب كلمة المرور في مطالبة مخفية. لا تضعها في المحفوظات أو في ملفات المصدر أو في المحادثة.

```bash
cd /Users/ashraffarag/Repo/ProductionLinePlanner-sql2016-bootstrap
printf 'SQL Server 2016 password: '
read -s DB_PASSWORD
printf '\n'
dotnet user-secrets set \
  'ConnectionStrings:Sql2016Target' \
  "Data Source=<SQL_SERVER_HOST>;Initial Catalog=<EMPTY_DATABASE_NAME>;User Id=<SQL_LOGIN>;Password=${DB_PASSWORD};Encrypt=True;TrustServerCertificate=True;" \
  --project src/backend/ProductionLinePlanner.Api/ProductionLinePlanner.Api.csproj
unset DB_PASSWORD
```

استخدم قيمة `<SQL_PASSWORD>` الفعلية فقط عبر المتغير `DB_PASSWORD` في المطالبة المخفية، ولا تكتبها داخل الملف أو الأمر المحفوظ في shell history.

لا تنفذ `dotnet user-secrets list` بشكل يطبع قيمة السر. عند الحاجة إلى تأكيد المفاتيح فقط، اطبع أسماء المفاتيح دون قيمها.

## فحص الهدف قبل التطبيق

شغّل الاستعلامات التالية بصورة قراءة فقط عبر أداة اتصال محلية تقرأ `ConnectionStrings:Sql2016Target` من User Secrets:

```sql
SELECT
    CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(100)) AS ProductVersion,
    CAST(SERVERPROPERTY('ProductLevel') AS nvarchar(100)) AS ProductLevel,
    CAST(SERVERPROPERTY('Edition') AS nvarchar(200)) AS Edition,
    DB_NAME() AS DatabaseName,
    SUSER_SNAME() AS LoginName;

SELECT compatibility_level
FROM sys.databases
WHERE name = DB_NAME();

SELECT COUNT(*) AS UserTableCount
FROM sys.tables
WHERE is_ms_shipped = 0;
```

أو شغّل أداة الفحص الجاهزة (لا تعدّل البيانات أو المخطط):

```bash
cd /Users/ashraffarag/Repo/ProductionLinePlanner-sql2016-bootstrap
bash scripts/sql2016/probe-target.sh
```

توقف فوراً إذا وُجدت جداول تطبيقية أو صفوف غير متوقعة في `__EFMigrationsHistory`. لا تحاول اختبار الصلاحيات بإنشاء أو حذف أي كائن. يقيس المشغّل صلاحية إنشاء الجداول وصلاحية تعديل مخطط `dbo`، ويجري transaction تجريبية تُلغى فوراً.

## توليد السكربت مرة أخرى

يجب تشغيل الأمر من جذر المستودع وباستخدام مشروع Infrastructure والسياق ومشروع API الفعليين. لا يحتاج التوليد إلى اتصال بقاعدة الإنتاج:

```bash
cd /Users/ashraffarag/Repo/ProductionLinePlanner-sql2016-bootstrap
ConnectionStrings__AppDatabase='Server=(localdb)\\MSSQLLocalDB;Database=EFDesignTimeOnly;Trusted_Connection=True;TrustServerCertificate=True' \
dotnet ef migrations script --idempotent \
  --context AppDbContext \
  --project src/backend/ProductionLinePlanner.Infrastructure/ProductionLinePlanner.Infrastructure.csproj \
  --startup-project src/backend/ProductionLinePlanner.Api/ProductionLinePlanner.Api.csproj \
  --output database/sql2016/001-create-schema.sql
```

## التطبيق الآمن

راجع نتيجة الفحص أولاً. لا يُطبَّق المخطط إلا إذا كان الإصدار 13.x، وكانت قاعدة الهدف خالية من جداول التطبيق وصفوف migrations، والصلاحيات كافية، والهدف ليس قاعدة المصدر. عند استيفاء الشروط فقط:

```bash
cd /Users/ashraffarag/Repo/ProductionLinePlanner-sql2016-bootstrap
bash scripts/sql2016/apply-schema.sh --apply
```

سيعرض المشغّل الخادم وقاعدة البيانات وعدد الجداول وعدد migrations المتوقعة، ويؤكد أن المرحلة لا تدخل أي بيانات تشغيلية. اكتب `APPLY-SCHEMA` فقط بعد مراجعة ذلك. لا يطبع المشغّل كلمة المرور ولا يطلبها كوسيط سطر أوامر.

يتضمن SQL الناتج من EF migrations بعض أوامر `UPDATE` لملء أعمدة أضيفت في migrations سابقة. المسار المعتمد لهذه الحزمة يطبقها فقط على قاعدة تطبيق جديدة وفارغة، ولا يُعد هذا السكربت مسار ترقية معتمداً لقاعدة إنتاج مأهولة.

لم يتم تنفيذ Production Data Bootstrap بعد. لم تُستورد أو تُحوّل أو تُطبّق أي بيانات إنتاج ضمن هذه المرحلة.

## التحقق بعد التطبيق

المشغّل ينفّذ `002-verify-schema.sql` تلقائياً. وللتنفيذ اليدوي استخدم أداة اتصال تقرأ السر من User Secrets، ثم تأكد من:

- وجود الجداول التطبيقية السبعة والعشرين و17 migration.
- وجود 35 علاقة خارجية والفهارس الفريدة/المفلترة الحرجة.
- توافق مستوى قاعدة البيانات مع SQL Server 2016 (`130` أو أعلى وفق سياسة الاستضافة).
- أن كل فحص orphan يعيد صفراً.
- أن عدادات بيانات التشغيل صفر في هذه المرحلة.

لا توجه وقت تشغيل التطبيق الاعتيادي إلى الهدف بشكل دائم في هذه المرحلة.

## الإيقاف الآمن

لا تطبّق أي migration إذا لم تكن القاعدة فارغة، أو كان الإصدار ليس SQL Server 2016، أو ظهرت migrations غير معروفة، أو لم تكن الصلاحيات كافية. احتفظ بحزمة المخطط للمراجعة فقط، وسجّل حالة الهدف، ثم عالج السبب قبل المحاولة التالية.

**ممنوع نهائياً**: commit لكلمات المرور أو connection strings ذات كلمات المرور أو نسخ قواعد البيانات أو صور العمال أو بيانات الحضور/الإنتاج الحساسة.

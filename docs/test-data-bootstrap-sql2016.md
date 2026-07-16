# ترحيل بيانات الاختبار إلى SQL Server 2016

هذه الأداة مخصصة لترحيل **بيانات الاختبار الموجودة** من قاعدة التطبيق الحالية إلى قاعدة SQL Server 2016 التي تم تجهيز مخططها سابقاً. هذه ليست Production Data Bootstrap ولا تتعامل مع قاعدة الحضور الخام.

## مفاتيح الإعداد

- المصدر: `ConnectionStrings:AppDatabase`
- الهدف: `ConnectionStrings:Sql2016Target`

تُقرأ القيم من User Secrets أو إعدادات .NET المعتادة. لا تقبل الأداة كلمات مرور أو connection strings عبر وسيطات سطر الأوامر، ولا تطبع أسماء الخوادم أو قواعد البيانات أو المستخدمين أو أي أسرار.

## الأوامر

```bash
cd /Users/ashraffarag/Repo/ProductionLinePlanner-sql2016-bootstrap
scripts/sql2016/test-data-bootstrap.sh --preflight
scripts/sql2016/test-data-bootstrap.sh --apply
scripts/sql2016/test-data-bootstrap.sh --verify
```

أو مباشرة:

```bash
dotnet run --project src/backend/ProductionLinePlanner.Tooling -- test-data preflight
dotnet run --project src/backend/ProductionLinePlanner.Tooling -- test-data apply
dotnet run --project src/backend/ProductionLinePlanner.Tooling -- test-data verify
```

## بوابات السلامة

- يجب أن يكون الهدف SQL Server 2016 أو متوافقاً معه.
- يجب أن يحتوي الهدف على 27 جدول تطبيق و17 migration records.
- يجب أن تكون جداول بيانات التطبيق في الهدف فارغة قبل الترحيل.
- يجب أن تتطابق بصمات الأعمدة والفهارس والعلاقات والقيود بين المصدر والهدف.
- ترفض الأداة العمل إذا كان المصدر والهدف نفس قاعدة البيانات.
- لا يتم تشغيل migrations ولا تطبيق schema scripts.
- لا يتم تعطيل foreign keys أو checks أو triggers.
- لا يتم استخدام `IDENTITY_INSERT` لأن الجداول التطبيقية لا تحتوي identity columns.
- يحتوي تقرير preflight على بصمة خطة/أداة؛ بعد أي تعديل على الأداة يجب تشغيل preflight جديد، ويرفض `apply` التقارير القديمة أو غير المطابقة.

## البيانات المضمنة

- `Factories`
- `ProductionLines`
- `MainStages`
- `SubStages`
- `ProductModels`
- `ProductModelStages`
- `Workers`
- `WorkerSalaryHistories`
- `WorkerDefaultAssignments`
- `WorkerTemporaryAssignments`
- `AttendanceRecords`
- `ProductionOrders`
- `StageProductionRecords`
- `StageProductionWorkerAllocations`
- `AppUsers`
- `UserRoles` مع ربط الأدوار بمفاتيح طبيعية

## البيانات المستبعدة أو المُعاد توليدها

- `RefreshTokens`: مستبعدة لأنها جلسات مؤقتة.
- `AuditLogs`: مستبعدة من الترحيل الأولي السريع.
- `AssignmentTimelineEntries`: مستبعدة لأنها غير مطلوبة لسلامة العلاقات في الترحيل الأولي.
- `Permissions` وsystem `AppRoles` و`RolePermissions`: تُعاد مطابقتها من كتالوج المنتج، ويتحقق `verify` من وجود baseline IAM المطلوب بعد الترحيل.
- `__EFMigrationsHistory`: لا تُنسخ من المصدر نهائياً.
- `Notifications` و`StageReadinessSnapshots` والجداول الفارغة/المشتقة: مستبعدة أو تُعاد توليدها.

## التقارير

ينتج الفحص:

```text
artifacts/test-data-bootstrap/preflight-report.json
```

وينتج التحقق:

```text
artifacts/test-data-bootstrap/verification-report.json
```

التقارير تستخدم التسميات `SOURCE_TEST_DB` و`TARGET_SQL2016_DB` فقط ولا تحتوي connection strings أو معرفات بيئة حقيقية.

مجلد التقارير محلي ومُتجاهل من Git، ولا يجب رفعه للمستودع.

## سلوك الاسترداد

`apply` يرفض التشغيل ما لم يوجد تقرير preflight ناجح وحديث يطابق حالة المصدر والهدف الحالية وبصمة خطة الأداة. يتم النسخ على مراحل، وكل مرحلة داخل transaction مستقل. تشمل مرحلة IAM مطابقة كتالوج الصلاحيات والأدوار ونسخ الأدوار المخصصة الآمنة ضمن نفس transaction. عند أول خطأ يتم rollback للمرحلة الحالية ثم تتوقف الأداة.

لا تشغّل `--apply` قبل مراجعة تقرير preflight ناجح والتأكد من أن القرارات التشغيلية مناسبة.

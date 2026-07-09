# 10 - خطة تشديد الأمن للباك إند (قبل التنفيذ)

## الغرض
توثيق خطة أمنية تشغيلية قوية للبنيّة الخلفية في الفرع الحالي قبل تطبيق تغييرات الكود.

## مصادر المراجعة
- `docs/03-backend-scope.md`
- `docs/08-backend-api-contracts.md`
- `README.md`
- `src/backend/ProductionLinePlanner.Api/Program.cs`

## ملاحظة الحالة الحالية (كما في كود الحالي)
- هناك إعداد أولي لـ `AddAuthentication()` و`AddAuthorization()` فقط (بدون سياسات/مزودات/Claims فعّالة بعد).
- لا يوجد الآن `RequireAuthorization` على نهاية API الحالية في `Program.cs`.
- Swagger مفعل فقط عند `IsDevelopment()`.
- `UseHttpsRedirection()` موجود.
- إعداد CORS موجود لكنه يعتمد على `Cors:AllowedOrigins` وقد يقبل قيمًا غير محكمة إذا لم يتم تنقيح الإعدادات.
- لم تُطبّق بعد طبقة موحدة لمعالجة الاستثناءات أو ProblemDetails.
- لا توجد ملاحظات أمان تشغيلية أخرى ضمن `Program.cs`.

## 1) Authentication
### المتطلبات
- اعتماد JWT access token قصير العمر + Refresh token طويل العمر.
- التحقق من:
  - التوقيع
  - `issuer` / `audience`
  - `exp` و`nbf`
  - `jti` لمنع التكرار
- فحص المستخدم أولًا في أي endpoint حساس من خلال token واحد (الآن في العقود: `SuperAdmin`, `Admin`).

### التنفيذ المقترح قبل التنفيذ
1. تعريف `JwtBearer` بشكل صريح في إعدادات التشغيل.
2. وضع حدود لعدد محاولات تسجيل الدخول (`/api/auth/login`) عبر Rate limit + lockout تدريجي.
3. تخزين refresh tokens بشكل hashed داخل تخزين آمن (عدم حفظ plaintext).
4. استخدام `iat`, `exp`, `sid` و`jti` لأغراض التتبع والإبطال.

### سياسات إضافية
- endpointات `/api/auth/login`, `/api/auth/refresh`, `/api/auth/logout` تكون Rate-limited ومحصورة في سياق IP/User-Agent.
- استخدام HTTPS-only و`SameSite`/`Secure` لو استُخدمت ملفات تعريف الارتباط في أي مرحلة لاحقة.

---

## 2) Authorization / Roles
### نموذج الأدوار المقترح
- `SuperAdmin`: صلاحية شاملة تشمل إدارة المستخدمين/الأدوار/الإعدادات الحساسة.
- `Admin`: إدارة تشغيل المصنع، خطوط، مراحل، وتعديلات التسكين (غير شامل إدارة الأدوار).

### قواعد مطلوبة
- كل endpoint كتابة/إدارة يطبق `RequireRole` مناسبًا.
- جميع الـ read-only endpoints الإنتاجية في حدود الصلاحية التجارية (`Admin` على الأقل) مع عدم فتح `GET` عام.
- منع تسرب مصنع/خط بين السياقات عبر فرض `factoryId` من token/claim + تحقق في الخدمة.

### نمط المصادقة التفاضلي (أمن أفضل)
- وضع سياسة عامة للـ API (GenericAuthGuard)
- سياسات أدق حسب الكيان (`Factories`, `Attendance`, `AuditLogs`).

---

## 3) Generic Auth Guard policy
### التعريف المقترح
- إنشاء سياسة واحدة عامة: `GenericAuthGuard`
  - `RequireAuthenticatedUser()`
  - تحقق وجود Claim نوع `sub` أو `userId`
  - تحقق تاريخ صلاحية الـToken الحالي
  - reject إذا غاب Claim `token_version` غير مطابق لقائمة الإبطال (اختياري)

### طريقة التطبيق المقترحة
- ربط المجموعة الأساسية للـ routes بهذه السياسة:
  - `app.MapGroup("/api").RequireAuthorization("GenericAuthGuard")`
- استثناءات مقصودة: `health`, `swagger` (حسب السياسة البيئية) و`auth` endpoints.

---

## 4) Rate Limiting
### أهداف
- حماية نقطة الدخول ضد Brute force وAPI abuse.
- حماية قاعدة البيانات من هجمات الاستنزاف.

### السياسات الموصى بها
- login/refresh: أقصى مثلاً `5 req / 1 min` لكل IP.
- write endpoints: `60 req / 1 min` لكل مستخدم.
- read endpoints كثيفة: `120 req / 1 min` لكل مستخدم (مع نافذة مرنة).
- health/read endpoints عامة: حدود أخف.

### التنفيذ
- استخدام `AddRateLimiter` + `UseRateLimiter`.
- قياس `Retry-After` في response.

---

## 5) CORS restrictions
### القاعدة الأساسية
- لا يوجد `AllowAnyOrigin` إذا كانت هناك credentials.
- حصر origins في environment-specific allowlist.
- استخدام HTTPS فقط في Origins.

### أفضل الممارسات
- ترتيب منفصل لقائمة origins حسب البيئة:
  - Development: localhost فقط
  - Staging: نطاقات اختبار محددة
  - Production: نطاقات رسمية فقط
- `AllowCredentials` فقط إذا مطلوبًا فعلًا.
- تحديد `WithHeaders` و`WithMethods` حسب الحاجة.

---

## 6) HTTPS enforcement
### المطلوب
- فرض TLS 1.2+ على كل اتصال خارجي.
- إضافة HSTS في الإنتاج.
- تشغيل `UseHttpsRedirection()` (موجود بالفعل).

### البيئة الخلفية
- لو التطبيق خلف reverse proxy، تفعيل `UseForwardedHeaders` قبل التحقق الأمني للحفاظ على `Request.Scheme` صحيح.
- منع downgrade عبر ضبط load balancer/Ingress.

---

## 7) Secure headers
### مجموعة رؤوس إلزامية
- `Strict-Transport-Security` (HSTS)
- `X-Content-Type-Options: nosniff`
- `Referrer-Policy: strict-origin-when-cross-origin`
- `Permissions-Policy` بقيم مقيدة (تعطيل الكاميرا/الميكروفون/الجيو لو غير مستخدمة)
- `Cache-Control`/`Pragma` على responses حساسة إن لزم.
- `Content-Security-Policy` إذا كان هناك صفحات/HTML في نفس المنصة (وإلا يمكن إجراؤها على حد أدنى).

### ملاحظات
- يمكن إضافة middleware بسيط داخل pipeline دون إدخال مكتبات جديدة.

---

## 8) Swagger exposure policy
### الحالي
- Swagger مفعل فقط في Development (`Program.cs`).

### قبل التنفيذ
- الإبقاء على هذا السلوك في البداية.
- في Staging/Prod:
  - إتاحة `/swagger` فقط من داخل VPC/VPN أو Basic Auth/Internal identity.
  - تعطيل عرض الـschemas الحساسة أو إخفاء أمثلة السرية.
  - تسجيل من يفتح Swagger.

---

## 9) Global exception handling
### المطلوب
- توحيد جميع الاستثناءات في نقطة واحدة للـ middleware قبل التوجيه النهائي للـ responses.
- منع تسريب تفاصيل البنية الداخلية إلى العميل.

### أنواع الاستثناءات التي يجب معالجتها
- `UnauthorizedAccessException` => 401
- `ValidationException` => 400
- `KeyNotFoundException` => 404
- `DbUpdateConcurrencyException` => 409
- استثناءات غير متوقعة => 500

### المتطلبات التشغيلية
- إضافة correlation id في كل response وخطوط log.
- تسجيل stack-trace فقط في log المركزي، وليس في الـ payload.

---

## 10) ProblemDetails
### الهدف
- توحيد شكل أخطاء API على `application/problem+json`.

### معايير النموذج المقترح
- `type`, `title`, `status`, `detail`, `instance`, `traceId`
- `extensions` إضافية عند الحاجة: `code`, `errors`, `resource`, `actorId`

### علاقة بالعقود
- توثيق أخطاء موحدة بجانب نماذج الـ success في `docs/08-backend-api-contracts.md`.

---

## 11) Input validation
### المطلوب
- التحقق الهرمي للمدخلات قبل أي عملية منطقية.
- منع over-posting/under-posting.

### خطة التنفيذ
- قيود `required/length/range` لجميع DTOs.
- sanitize للحقول النصية (trim/normalize) قبل الحفظ.
- تحقق منطقي للتواريخ، أزمنة البداية/النهاية، Page/Size وsearch query bounds.
- رفض payload كبير جدًا لعمليات القراءة/الكتابة.
- إرجاع أخطاء تحقق بنمط ProblemDetails موحد.

---

## 12) Audit logging
### ما يجب تسجيله
- من قام بالفعل (`actorId`, `roles`)
- نوع الإجراء (`CREATE`, `UPDATE`, `DELETE`, `AUTH`, `AUTHZ`)
- الهدف (`entityType`, `entityId`)
- الطلب (`httpMethod`, `path`, `ip`, `userAgent`, `correlationId`)
- نتيجة التنفيذ (`Succeeded`, `Failed`, `Reason`)

### النقاط الحرجة
- التعديلات على التسكين المؤقت والثابت.
- CRUD المصانع والخطوط والمراحل.
- إعادة تعيين جلسات/توكنات.
- أي قراءة/فشل مصطنع على Attendance.

### الاحتياطات
- لا يتم تسجيل كلمات مرور، tokens raw، أو Personal Identifiable Data غير ضروري.
- حماية سجل التدقيق ضد التعديل غير المصرح.

---

## 13) Secrets management
### متوافق مع README
- لا توجد Secrets في Git (`appsettings.*` placeholders فقط).
- استخدام User Secrets محليًا، أو environment variables.
- لبيئات الإنتاج: Secret Store/Key Vault/Vault Manager.

### اشتراطات
- تدوير دوري لسلاسل الاتصال/المفاتيح.
- تقييد صلاحيات secret access للمستخدم/ الخدمة فقط.
- منع copy/paste secrets في محادثات التوثيق.

---

## 14) SQL injection protection
### المتطلبات
- اعتماد EF Core/parameterized queries بشكل افتراضي.
- منع Dynamic SQL غير معلمن.
- أي raw SQL يجب أن يستخدم معاملات parameters صريحة.

### قواعد قاعدة البيانات
- صلاحيات DB role: minimum privilege لكل service.
- تفكيك صلاحيات القراءة والكتابة لكل DB context إن أمكن.
- تعطيل أو تقنين صلاحيات `ALTER`, `DROP`, `GRANT` على حساب التشغيل.

---

## 15) Online hosting hardening
### شبكات وتشغيل
- وضع خدمة خلف reverse proxy (Nginx/IIS/Cloud LB).
- جدار ناري + allowlist للـ subnets/ports.
- مراقبة معدل الطلبات والخروج (outbound) للتحقق من سلوك غير طبيعي.
- فحص الصور/المحتوى إذا كانت مرفوعة (عبر pipeline).

### السجلات والمراقبة
- مراقبة Health endpoints:
  - `/api/health`
  - `readiness`/`liveness` endpoints إضافية.
- تنبيهات CPU/Mem/DB latency/Rate-limit hits.

---

## 16) Factory hosting deployment considerations
### البنية المقترحة
- فصل بيئات:
  - development
  - staging
  - production
- إعدادات CORS/secrets/policies per-environment.
- HPA/autoscaling أو replication إذا وُجدت الأحمال المرتفعة.
- حماية pipeline:
  - فحص ثبات migration على نفس DB target.
  - فحص أمني قبل النشر (secrets scan + config scan).

### نشر آمن
- نشر تدريجي (blue/green or canary).
- Backup + rollback plan قبل كل نسخة.
- التحقق من health قبل و بعد النقلة.

---

## 17) Attendance DB read-only protection
### القاعدة الصارمة
- قاعدة `Attendance` (أو `AttendanceDatabase`) يجب أن تُعامل كـ read-only.

### الحوكمة
- حسابيًا: سياق اتصال attendance بيوفر صلاحية Select فقط.
- هندسيًا:
  - حساب role مخصص `AttendanceReadOnly`.
  - إيقاف أي migration أو EF migrations على context تلك القاعدة.
  - التأكد من عدم استدعاء `SaveChanges`/commands على هذا المسار.
- مراجعة عقود Integration من `docs/08...` لتأكيد أنها تقرأ فقط.

## مصفوفة مخاطر التنفيذ
| الخطر | التأثير | التخفيف | المرحلة |
|---|---|---|---|
| فتح CORS على Wildcard | تسريب session/بيانات | قفل origins + no credentials wildcard | 1 |
| نقص global validation | أخطاء إدخال تؤدي للـ inconsistency | middleware تحقق + DTO constraints | 2 |
| لا يوجد central exception | إفشاء stack trace/حجم أخطاء متباين | global handler + ProblemDetails | 2 |
| ضعف audit | صعوبة التحقيق | middleware audit endpoints الحساسة | 3 |
| صلاحية ضعيفة للتوكن | hijack/ reuse | jti + token rotation + short TTL | 1 |

## خطة التنفيذ المقترحة (مرتبة)
1. Security foundation: HTTPS/HSTS + headers + CORS + exception/ProblemDetails.
2. AuthN/AuthZ: token model + GenericAuthGuard + role mapping.
3. حماية التشغيل: rate limiting + audit + secrets.
4. حماية البيانات: SQL injection controls + Attendance read-only enforcement.
5. hardening للإنتاج: WAF/monitoring/deployment guardrails.


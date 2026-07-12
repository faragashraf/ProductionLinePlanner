# Product Bible V1 - Production Line Planner

## الرابط المركزي

يعمل هذا الدليل كمرجع معماري طويل الأجل للمنتج ومرتكز للقرارات. الهدف: توحيد اللغة، مصادر الحقيقة، وطرق صنع القرار بعيدًا عن الأكواد المحددة للنسخة الحالية.

- [00-product-vision.md](00-product-vision.md)
- [01-status-and-evidence.md](01-status-and-evidence.md)
- [02-ubiquitous-language.md](02-ubiquitous-language.md)
- [03-capability-map.md](03-capability-map.md)
- [04-bounded-contexts.md](04-bounded-contexts.md)
- [05-source-of-truth.md](05-source-of-truth.md)
- [06-master-data-architecture.md](06-master-data-architecture.md)
- [07-employee-administration.md](07-employee-administration.md)
- [08-attendance-integration.md](08-attendance-integration.md)
- [09-factory-structure-and-planning.md](09-factory-structure-and-planning.md)
- [10-production-engineering.md](10-production-engineering.md)
- [11-production-execution.md](11-production-execution.md)
- [12-compensation.md](12-compensation.md)
- [13-identity-access-and-permissions.md](13-identity-access-and-permissions.md)
- [14-security-and-threat-boundaries.md](14-security-and-threat-boundaries.md)
- [15-audit-and-observability.md](15-audit-and-observability.md)
- [16-api-guidelines.md](16-api-guidelines.md)
- [17-frontend-guidelines.md](17-frontend-guidelines.md)
- [18-excel-import-export.md](18-excel-import-export.md)
- [19-testing-strategy.md](19-testing-strategy.md)
- [20-delivery-roadmap.md](20-delivery-roadmap.md)
- [21-future-expansion.md](21-future-expansion.md)
- ADRs: [adrs](adrs)
- Diagrams: [diagrams](diagrams)

## ارتباط بالوثائق القديمة

الوثائق في `docs/*.md` الحالية بقيت مرجعًا تاريخيًا، ويفضل اعتبار هذا الـProduct Bible هو المرجع التشغيلي الرسمي الحالي.

- `docs/08-backend-api-contracts.md`
- `docs/09-frontend-ux-contracts.md`
- `docs/10-backend-security-hardening.md`
- `docs/11-product-vision.md`

## مبدأ تصنيف القرارات

في كل وثيقة: استخدم التسميات

- **Confirmed**: موجود فعليًا في الكود/الوثائق الحالية
- **Approved**: قرار معماري جديد معتمد
- **Planned**: مطلوب مستقبلاً
- **Deferred**: مؤجل بوضوح
- **Rejected**: خيار رفض
- **Architecture Conflict**: سلوك حالي يتعارض مع الاتجاه المعتمد

## حالة البداية (Baseline)

- الفرع الحالي: `architecture/master-data-foundation-v1`
- النطاق الفعلي للمرحلة: **وثائق فقط (No runtime changes).**

## أدلة سريعة (Quick anchors)

- Backend code snapshots: `src/backend/ProductionLinePlanner.Api/Program.cs`
- Domain entities: `src/backend/ProductionLinePlanner.Domain/Entities/*`
- App shell routes/guards: `src/frontend/src/app/app-routing.module.ts`
- Auth service: `src/frontend/src/app/core/services/auth.service.ts`

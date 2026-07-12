# 21 - Future Expansion

## Why

- Product Bible V1 محدد، لكن roadmap يحتاج مسارات نمو.

## Scope

- اتجاهات مستقبلية مقبولة دون توظيفها كـDone في V1.

## Deferred Architecture Work

- Multi-tenant factories.
- Time-based OT rules.
- Advanced BI and ML readiness.
- ABAC (scope aware permissions by factory/line).
- Biometric re-sync webhooks.
- Payroll compliance packs per region.

## Owned by Contexts

- IAM: delegated permission admins + approval workflow.
- Production: workflow orchestration + scheduler.
- Compensation: tax integration + payroll export.

## Risks Ahead

- توسيع الصلاحيات قد يسبب combinatorial policy complexity.
- توسع cross-factory requires stronger tenant isolation.

## Alternatives Kept for Future

- **Rejected for V1**: GraphQL/BFF replacement.
- **Deferred**: external event bus for everything.

## Open Questions

- هل `attendance.user.sync` يجب أن تكون periodic job أو webhook؟
- هل نضيف `roles.permissions` nested groups؟
- هل نسمح `workers.import` في V1 أم نضيفه لاحقًا؟

## Acceptance Guidance

- أي توسع قادم لا يجب أن يخلّ بالقرارات الأساسية:
  - source-of-truth واضح.
  - no production inflation.
  - deny override remains highest priority.

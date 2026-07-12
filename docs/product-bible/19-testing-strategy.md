# 19 - Testing Strategy

## Why

- وثيقة Product Bible بدون اختبارات واضحة لن تكون قابلة للتحول بأمان.
- **Approved**: خطة اختبار على 4 طبقات:
  - الوحدة
  - التكامل
  - الأمن
  - مراجعات E2E + smoke.

## Scope

- تغطية الوظائف الجديدة المقترحة (IAM, production execution, compensation, import/export, frontend auth).
- لا يشمل إعادة كتابة test infra الحالي.

## Ownership

- Backend QA: API + engines.
- Frontend QA: auth guards + routing + directive.
- Security QA: threat-oriented tests.

## Source of Truth

- **Confirmed**: توجد بنية اختبار جزئية للمشروع (لا تفصيل هنا).
- **Planned**: إضافة suites جديدة.

## Entities

- Tests target:
  - permission resolver
  - payroll calculator
  - execution snapshot
  - role override conflict rules
  - route guards.

## Business Rules

- أولوية: tests على قواعد تمنع تضخيم الإنتاج أولًا.
- كل قرار architecture في this Bible يرتبط بالاختبار.

## Validation

- Unit tests:
  - `PermissionResolver`
  - `Attendance identity resolution`
  - `Salary overlap validation`.
- Integration tests:
  - `/api/auth/me`
  - guarded endpoints.
  - sync flow returns 409 on conflict.
- Security tests:
  - 401/403 boundaries.
  - deny override beats role grant.
- Frontend tests:
  - no flicker after session restore.
  - canMatch prevents loading unauthorized modules.

## Permissions

- `permissions.assign` tests require dedicated security test account.
- `audit.view` tests ensure unauthorized denied.

## Audit

- test logs record security-sensitive test cases.

## API Direction

- Add contract tests to ensure endpoint permissions map doesn’t drift.
- Ensure response contract from `/api/auth/me` includes version/policies.

## UI Direction

- Playwright/Cypress route guard tests:
  - unauthorized navigation redirects to 403.
  - sidebar filtered before render.

## Failure Handling

- test matrix:
  - hard fail on security misses.
  - soft warning on accessibility/ux style.

## Deferred Work

- performance load tests على import/export large files.
- chaos testing لِ refresh token invalidation.

## Risks

- زيادة زمن CI بسبب زيادة الاختبارات.
- صعوبة mock attendance db بسبب dual DB.

## Acceptance Criteria

- كل capability جديدة لها اختبار نجاح وفشل.
- اختبارات security تمنع role checks داخل الخدمات الحساسة بدون permission.
- وثائق الاختبار مرتبطة مباشرة بمرجع ADR.

## Alternatives Considered

- **Rejected**: no tests for auth before feature rollout.
- **Rejected**: تشغيل integration tests فقط على happy path.

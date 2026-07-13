# 20 - Delivery Roadmap

## Why

- لتثبيت V1 عمليًا يجب تنفيذ التنفيذ على مراحل مرتبة وفق المخاطر.
- **Approved**: تنفيذ Phase 1 كمرحلة تأسيس قبل أي feature شاشة.

## Scope

- خمس قدرات تنفيذية كبيرة + مهام دمج وتحقق.

## Ownership

- Product + Architecture + Security + Frontend leads.

## Commit Checkpoints / Merge Gates

- كل مرحلة تنتهي بـ merge checkpoint:
  - docs updated
  - migration files not required في هذه المرحلة الـdocs
  - acceptance checklist passed
  - manual smoke.

## Cross-Phase Frontend Requirements

- **Approved**: سياسة `Skeleton/Shimmer Loading` مطبقة كمكوّنات مشتركة عبر كل phases التي تحتوي واجهات.
- لا تُبنى صفحة جديدة بدون:
  - shared skeleton primitives من Phase 1.
  - حالة loading/empty/error/unauthorized واضحة.
  - RTL + responsive placeholder layout.
- ليست ميزة منفصلة تُكرر لكل phase؛ `Shimmer` و`Skeleton` محور تقني واحد لكل الواجهات القادمة.

## Phase 1: Identity and Access Management Foundation

Enterprise hardening work deferred from this V1 checkpoint is tracked in [IAM Enterprise Hardening Backlog](22-iam-enterprise-hardening-backlog.md).

- Scope:
  - Permission catalog + domain entities.
  - role permissions.
  - user overrides (grant/deny).
  - backend endpoint policies.
  - `auth/me` returns effective permissions.
  - Angular permission state/guards/directive.
  - 403 route.
  - admin permission UI shell.
- Out of scope:
  - payroll, production output, model stage pricing.
- Backend:
  - new entities, policy provider, seed.
- Frontend:
  - PermissionService + guards + sidebar filtering.
  - shared skeleton primitives (foundation).
- Database:
  - permission tables only.
- Security:
  - deny override and token version strategy.
- Tests:
  - authz unit/integration/security.
- Manual smoke:
  - admin/user role scenarios + denied route.
- Merge gate:
  - any privileged endpoint has policy.
- Risks:
  - breaking existing admin flow during migration.
- Recommended model/effort:
  - GPT-5.3-Codex-Spark / High / Architecture-first.

## Phase 2: Employee and Department Master Data

- Scope:
  - controlled worker write API.
  - worker create/update with identity-key validation.
  - department capability.
- Out of scope:
  - payroll, production execution models.
- Backend:
  - update endpoints and domain rules.
- Frontend:
  - worker edit forms + status filters.
  - use shared skeleton components for all reads.
- Security:
  - `workers.manage`, `departments.manage`.
- Tests:
  - overlap mapping tests.
- Merge gate:
  - no duplicate identity links.
- Recommended model/effort:
  - GPT-5.3-Codex-Spark / High / Docs-to-engineering handoff.

## Phase 3: Worker Compensation

- Scope:
  - `WorkerSalaryHistory`.
- permissions:
  - `compensation.manage`, `compensation.view`, import/export.
- Frontend:
  - salary timeline.
  - shared skeleton/shimmer for timelines and lists.
- Tests:
  - overlap and overlap-fix cases.
- Risks:
  - historical rollback correctness.
- Recommended model/effort:
  - GPT-5.3-Codex-Spark / High / Capability-first implementation.
- Out of scope:
  - payroll tax/legal modules.

## Phase 4: Production Stage Catalog

- Scope:
  - product stages & catalog operations.
- APIs:
  - stage catalog CRUD + import/export.
 - Frontend:
   - shared skeleton placeholders for catalog tables/forms.
- Security:
  - `stages.manage`.
- Acceptance:
  - no pricing in `MainStage/SubStage`.
- Recommended model/effort:
  - GPT-5.3-Codex-Spark / High / UI + engine parity.
- Out of scope:
  - allocation/compensation engine changes.

## Phase 5: Product Models and Stage Configuration

- Scope:
  - `ProductModel`, `ProductModelStage` (model-level pricing/time).
  - `CompensationMode`.
- Backend:
  - snapshot model config at execution.
- Frontend:
  - model-stage matrix and pricing screen.
  - shared skeleton/shimmer for matrix and export previews.
- Risks:
  - model pricing isolation conflicts between stage history and model override.
- Acceptance:
  - stage time/price can differ by model.
- Out of scope:
  - payroll execution engine migration.
- Recommended model/effort:
  - GPT-5.3-Codex-Spark / High / Shared model layer.
- Tests:
  - pricing isolation by model.
- Merge gate:
  - no duplicate pricing in SubStage.

## Phase 6: Production Output and Worker Allocation

- Scope:
  - `ProductionStageOutput` + `WorkerAllocation`.
- Rules:
  - quantity once.
- Compensation:
  - payroll uses snapshot + allocation mode.
- Security:
  - `production.record`, `production.approve`.
- Acceptance:
  - duplicate production prevented.
- Out of scope:
  - BI reporting deep charts.
- Recommended model/effort:
  - GPT-5.3-Codex-Spark / High / Engine + API + route guard wiring.
- Backend:
  - execute-safe aggregate constraints.
- Frontend:
  - production entry + allocation UX.
  - shared skeleton/shimmer for batch and line-item forms.
- Tests:
  - no-inflation scenario tests.

## Cross-Phase Security Gates

- No phase can merge without:
  - `/api/auth/me` contract updated.
  - permission mapping table updated.
  - audit updates for sensitive writes.
  - smoke for one unauthorized scenario + one allowed scenario.

## Dependencies

- Phase 2 depends on Phase 1.
- Phase 3 depends on Phase 1.
- Phase 5/6 depends على Product Model foundations.

## Risks

- Incomplete token strategy causing stale permissions UI.
- migration of existing data to new IDs.
- Excel error semantics too strict delaying adoption.

# AI Prompt Template

## Purpose

Use this template قبل طلب المساعدة من AI لمهام ضمن Product OS.

## Prompt Structure

**Context**
- Capability:
- Current status (Planned / In Progress / Checkpoint / Hardening):
- Relevant docs:
- Selected model and reason (Spark / Terra / Sol / Luna):

**Task**
- What needs to be done (concise):
- Scope:
- Constraints:

**Quality Requirements**
- Must follow Product OS workflow:
  - Architecture -> DoD -> Bible -> Implementation -> Review -> Validation -> Checkpoint -> Merge
- No source code changes here unless explicitly requested.
- Keep backend as security authority.
- Keep endpoints thin.

**Expected Output**
- Deliverable format:
- Validation evidence required:
- Risks:

**Approval Rule**
- Follow the Model Selection Policy in `08-ai-development-playbook.md`.
- If model detects high-risk security or architecture ambiguity, escalate to Terra protocol.

# 22 - IAM Enterprise Hardening Backlog

## IAM Foundation V1 guarantees

- Product-controlled permission catalog, system-role reconciliation, custom roles, role grants, and user grant/deny overrides.
- Backend permission enforcement, delegation checks, self-promotion prevention, and transactional user-authorization replacement with an audit record.
- Startup IAM reconciliation fails closed; V1 protects the last SuperAdmin with database transactions.
- Refresh tokens are hashed and V1 rotation uses atomic compare-and-revoke. Reuse revokes the currently active refresh session tokens for that user.

## High priority before public internet exposure

| Item | Security impact | Trigger and required evidence |
|---|---|---|
| Frontend refresh single-flight | Prevents parallel client refresh storms | Interceptor/session tests for concurrent 401 responses and login fallback |
| Token-family/session model | Stronger reuse containment and device revocation | SQL Server integration tests for rotation and reuse |
| HTTP IAM integration suite | Proves 401/403, delegation, atomicity, and audit contracts | WebApplicationFactory or equivalent relational test host |
| SQL Server race tests | Verifies last-SuperAdmin and rotation isolation semantics | Concurrent SQL Server test execution |
| Session revocation and HttpOnly-cookie evaluation | Limits token theft and access-token lifetime risk | Threat-model review and browser security tests |

## Required before large multi-factory rollout

- Server-side pagination/search for IAM lists.
- Permission cache/version invalidation with measurable consistency rules.
- Factory/department data scopes.
- Distributed startup coordination for reconciliation across instances.

## Future enterprise capabilities

- MFA, SSO/LDAP/Azure AD, service/device account model, and approval/separation-of-duties workflows.

## Future merge criteria

Do not claim Enterprise Hardening complete until the public-internet and SQL Server test items above are implemented and passing. These items are deliberately outside IAM Foundation V1.

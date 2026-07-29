# Realtime Notifications Foundation

## Delivery contract

- The authenticated `AppUser.Id` claim is the only SignalR user identity.
- Hub clients cannot provide user IDs, permission names, or group names.
- Each connection resolves effective permissions from the server and joins `capability:<permission>` groups.
- `Clients.User` intentionally reaches every live tab/device connection for the same user.
- A user notification is persisted before live dispatch. `Notification.Id` is the caller-provided idempotency key.
- Repeating the same ID and payload is a no-op; reusing an ID with a different payload is rejected.
- SignalR is best-effort. Login/reconnect reloads the persisted inbox and unread count through authorized HTTP APIs.
- Capability-group notifications are explicitly ephemeral and must not be used where offline delivery is required.

## In-app presentation

- The transport emits a typed event; inbox state accepts each `Notification.Id` once per authenticated tab session, then `NotificationPresentationService` owns toast and sound decisions.
- Historical inbox loads, unread-count refreshes and reconnect duplicates do not enter the live-presentation stream.
- The default sound is a short Web Audio tone, so no externally licensed asset is required.
- A single pointer/keyboard listener pair primes audio after a user gesture. Autoplay rejection is silent, is never queued for replay, and leaves future application use unaffected.
- `enabled`, `soundKey` and `volume` are in-memory extension points only. There is no preferences UI, persistence or schema change.
- Logout/user change clears inbox and presentation deduplication, cancels session HTTP requests and tears down audio listeners/context.

## Security boundary

- JWT query-string tokens are accepted only below `/hubs/notifications`.
- The Hub exposes no client-callable business methods.
- Notification list and read operations always scope by the authenticated recipient.
- Realtime payloads exclude recipient/sender identities and must not contain secrets or biometric data.
- Logs contain lifecycle/count metadata only; notification message content and tokens are not logged.

## Deferred infrastructure

- Web Push, FCM, APNs and user device registrations.
- Notification Center redesign.
- Business-screen invalidation events.
- Delivery-attempt history, payload versioning, retention jobs and an Outbox.
- Redis backplane or managed SignalR. Hub/business boundaries allow these transports later without changing callers.
- Until a backplane is introduced, user/capability live dispatch reaches connections on the current server instance only; the persisted inbox remains the durable source of truth.

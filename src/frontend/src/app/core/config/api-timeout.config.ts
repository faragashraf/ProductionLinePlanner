export const AUTH_API_TIMEOUT_MS = 10_000;
export const STANDARD_API_TIMEOUT_MS = 10_000;
// ZKTime/database synchronization can legitimately outlast normal API reads.
// Keep the request bounded while avoiding a client-side abort during a valid sync.
export const ATTENDANCE_SYNC_TIMEOUT_MS = 90_000;

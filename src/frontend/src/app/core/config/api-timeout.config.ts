export const AUTH_API_TIMEOUT_MS = 10_000;
export const STANDARD_API_TIMEOUT_MS = 10_000;
// A unified daily preview validates every mapped stage and its allocations in
// one server-side read. It must remain bounded, but it is intentionally not a
// normal single-record request that should be aborted after ten seconds.
export const DAILY_PRODUCTION_OPERATION_TIMEOUT_MS = 60_000;
// ZKTime/database synchronization can legitimately outlast normal API reads.
// Keep the request bounded while avoiding a client-side abort during a valid sync.
export const ATTENDANCE_SYNC_TIMEOUT_MS = 90_000;

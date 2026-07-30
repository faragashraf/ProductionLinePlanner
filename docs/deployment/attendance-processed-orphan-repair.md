# Processed attendance orphan repair

This runbook repairs only durable ZK attendance inbox rows that are `Processed` but have no exact Dayoub attendance evidence. It never inserts `AttendanceRecords` directly and never reopens all processed history.

## Prerequisites

- Deploy the corrected backend before executing a repair.
- Confirm the API is using `AttendanceSource:Mode=Staging`.
- Use an authenticated administrator with the `attendance.sync` permission.
- Start with a narrow operational-date range and `maximumRows` no greater than 100.

## 1. Preview

```bash
curl --fail-with-body \
  --request POST \
  --url "$DAYOUB_API/api/attendance/processed-orphans/preview" \
  --header "Authorization: Bearer $DAYOUB_ACCESS_TOKEN" \
  --header "Content-Type: application/json" \
  --data '{
    "fromOperationalDate": "2026-07-29",
    "toOperationalDate": "2026-07-30",
    "maximumRows": 100
  }'
```

Save the response in the approved operational change record. Review `count`, `scanLimitReached`, `groups`, worker mappings, dates, and every `inboxId`. Optional `sourceUserId` or `badgeNumber` filters can narrow the preview.

## 2. Execute one controlled batch

Pass only IDs from the reviewed preview. Execution is rejected without the exact confirmation value.

```bash
curl --fail-with-body \
  --request POST \
  --url "$DAYOUB_API/api/attendance/processed-orphans/repair" \
  --header "Authorization: Bearer $DAYOUB_ACCESS_TOKEN" \
  --header "Content-Type: application/json" \
  --data '{
    "fromOperationalDate": "2026-07-29",
    "toOperationalDate": "2026-07-30",
    "maximumRows": 100,
    "execute": true,
    "confirmation": "REPAIR-PROCESSED-ATTENDANCE-ORPHANS",
    "inboxIds": [10001, 10002, 10003]
  }'
```

The capability rechecks every row under a serializable transaction, uses row-version concurrency, requeues only true orphans, records an audit entry, and invokes the corrected attendance processor for affected operational dates. Per-row results are `Repaired`, `AlreadyImported`, `Skipped`, `Failed`, `NoWorkerMapping`, or `NoLongerOrphan`.

## 3. Verify and repeat

1. Run the same preview again. Repaired IDs must no longer appear.
2. Run `database/tools/Attendance-Diagnostics.sql` for representative badges. `ProcessedOrphanCount` must be zero and no row may report `ProcessedWithoutAttendance`.
3. Verify every repaired punch is either represented exactly once in `AttendanceRecords` or has an explicit non-processed business outcome.
4. Investigate `Failed`, `Skipped`, and `NoWorkerMapping` individually; do not bulk-requeue them.
5. Repeat with the next reviewed batch until preview count is zero, then retain the final reconciliation output with the deployment record.

Do not use `usp_ZkSyncExecuteManual`, `usp_ZkInboxRequeueFailed`, or `usp_ZkInboxRequeueSkipped` for processed orphans. Those procedures do not satisfy this repair contract.

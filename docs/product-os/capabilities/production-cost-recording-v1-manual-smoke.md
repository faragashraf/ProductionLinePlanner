# Production Cost Recording V1 Manual Smoke Checklist

Run this checklist against a Development environment after the reviewed migration has been applied by the environment owner. Do not run it against Production as part of this checklist.

## Master data

- [ ] Open `/manufacturing/stages` as a user with `stages.manage`.
- [ ] Create a Main Stage and verify it appears as active.
- [ ] Create a SubStage under it with a unique code and sequence order.
- [ ] Open `/manufacturing/models` as a user with `models.manage`.
- [ ] Create a Product Model.
- [ ] Add the SubStage to the model with Piece Price `0.50`, Standard Seconds `17`, and `SharedPercentage`.

## Production recording

- [ ] Create a Production Order for the model with planned quantity `500`.
- [ ] Activate the order.
- [ ] Open `/manufacturing/production-recording` and select the active order.
- [ ] Select the model stage from the selector; do not enter GUID values.
- [ ] Enter Produced `500`, Accepted `500`, and Rejected `0`.
- [ ] Select two workers and enter `50%` for each.
- [ ] Run Preview and verify production quantity `500`, equivalent quantity `250` for each worker, and earning `125` for each worker.
- [ ] Save the Draft and approve it as a user with `production.approve`.
- [ ] Open the daily report and verify production remains `500`, not `1000`, and total earnings are `250`.
- [ ] Cancel the approved record and verify it is excluded from the default report.

## Access and presentation

- [ ] Verify Desktop layout for Stages, Models, Orders, and Production Recording.
- [ ] Verify mobile layout for the same pages.
- [ ] Verify a `production.view` user can read orders, records, and reports but cannot create or approve.
- [ ] Verify a `production.record` user can create/edit Draft records but cannot approve or cancel.
- [ ] Verify a `production.approve` user can approve and cancel records.

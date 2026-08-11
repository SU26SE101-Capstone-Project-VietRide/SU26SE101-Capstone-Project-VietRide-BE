# Resource reservation rollout

This rollout is fail-closed. Do not apply `AddResourceReservations` to production until the
legacy audit is clean and every different-location adjacency has been checked with Google Routes
`DRIVE`. There is no conflict override, automatic cancellation, or grandfathering.

## Pre-migration gate

Run `resource-reservation-legacy-audit.sql` against `vietride_trip`. The report builds the same
Driver/Assistant/Vehicle assignment stream from active main Trips and ShuttleTrips that the
migration will backfill.

- `TIME_OVERLAP` and `TURNAROUND_REQUIRED` must be resolved by an operator before migration.
- `LOCATION_DATA_MISSING` requires Station/manifest coordinates to be repaired.
- Every `REPOSITION_REVIEW` row requires Google Routes `DRIVE` validation. It is clean only when
  `next_start_at >= previous_end_at + 30 minutes + returned travel duration`.
- Record the chosen resource or schedule correction. Do not auto-cancel either source.
- Re-run until the query returns zero rows after the Google-reviewed rows are accounted for.

Take a database backup, deploy code that understands reservations, then apply both Trip
migrations in order. `AddResourceReservations` backfills before adding the GiST exclusion
constraint; an overlooked overlap aborts and rolls back the migration instead of leaving a
partial backfill.

## Post-migration checks

Confirm exactly one reservation per assigned role and active source, no forbidden overlap, and no
source missing reservations:

```sql
SELECT left_row.id AS left_reservation_id, right_row.id AS right_reservation_id
FROM vietride_trip.resource_reservations AS left_row
JOIN vietride_trip.resource_reservations AS right_row
  ON right_row.id > left_row.id
 AND right_row.resource_type = left_row.resource_type
 AND right_row.resource_id = left_row.resource_id
 AND tstzrange(right_row.planned_start_at, right_row.planned_end_at, '[)')
     && tstzrange(left_row.planned_start_at, left_row.planned_end_at, '[)')
WHERE left_row.status IN ('RESERVED', 'ACTIVE')
  AND right_row.status IN ('RESERVED', 'ACTIVE');

SELECT trip_id, resource_role, count(*)
FROM vietride_trip.resource_reservations
WHERE trip_id IS NOT NULL
GROUP BY trip_id, resource_role
HAVING count(*) > 1;
```

Finally verify one main→main, main→shuttle, and shuttle→shuttle conflict through the preview APIs,
then verify cancellation/completion releases reservations and an `ACTIVE` assignment blocks the
next start with one `trip.assignment.start_blocked` Outbox event.

## Rollback

Stop Trip writers before rollback. The alert migration removes
`ASSIGNMENT_START_BLOCKED` markers before restoring the old alert column/check. The reservation
migration drops only the derived reservation table; it never changes or cancels Trip/ShuttleTrip
business rows.

# Sprint 4 — Demo script (Day 30)

This reviewer-facing script closes the Sprint-4 Trip automation and Parcel flow. The
authoritative run is the self-seeding Day-30 harness and its redacted evidence; the Postman
folder is an importable manual companion.

## 1. Local prerequisites

- Run from the repository root with Docker Desktop running and the local dependencies installed.
- Start or refresh the application profile, waiting for health checks to pass:

```powershell
docker compose --env-file .env -f infra/docker/docker-compose.yml --profile app up -d --wait --wait-timeout 180
```

- Do not paste or commit JWTs, private keys, `.env` values, customer data, or raw
  `Idempotency-Key` values. The Day-30 runner creates short-lived credentials and isolated
  fixtures at runtime, then removes them.
- For a manual view of the same flow, import the [Day-30 Postman folder](../api/postman/vietride.postman_collection.json)
  and provide disposable runtime placeholders from the local environment file.

## 2. Reviewer command and expected evidence

Run this exact command from the repository root:

```powershell
npm run e2e:day30
```

Expected result is exit code `0` and a `DAY30_RUN=PASS` line. The run must report a generated
Trip with `source=AUTO_FROM_SCHEDULE` and the matching `driver_schedule_id`, then prove the
Trip and Parcel state sequences, the required Outbox rows, completion replay, and cleanup. The
redacted evidence is recorded in [Day-30 evidence](day-30-sprint4-evidence.md); it contains no
credentials or raw idempotency keys. The [Day-30 Postman folder](../api/postman/vietride.postman_collection.json)
is a manual/importable companion, not a replacement for this self-seeding command.

## 3. Demo sequence

Narrate the ordered journey exactly as follows:

`Operator → DriverSchedule → AUTO_FROM_SCHEDULE Trip → load → start → arrival → unload → complete`

1. The operator creates one active DriverSchedule through Gateway. The runner selects a single
   future ICT service date inside the generation horizon and waits for exactly one linked Trip;
   it verifies the schedule id, operator, route, vehicle, assigned driver/assistant, generated
   stop, and seats before any fixture-only time adjustment.
2. The generated Trip starts at `SCHEDULED`. The existing scheduler moves it to `BOARDING` and
   emits `trip.trip.boarding_started`; the runner does not insert or directly mutate a Trip
   status.
3. The assigned assistant loads the generated Parcel through Gateway. The response is
   `LOADED`; the `trip.trip.started` consumer then moves cargo to `IN_TRANSIT`.
4. The assigned driver starts the Trip through Gateway (`IN_PROGRESS`), and the assigned crew
   marks the selected TripStop arrived. The runner correlates `trip.stop.arrived` evidence.
5. The assigned assistant unloads the Parcel at the arrived stop (`UNLOADED`), producing
   `parcel.parcel.unloaded` and releasing the cargo state.
6. The assigned driver completes the Trip (`COMPLETED`), producing exactly one
   `trip.trip.completed`. Replaying completion with the same runtime key is byte-identical and
   does not create a duplicate transition or Outbox row.

The redacted evidence records Trip states `SCHEDULED → BOARDING → IN_PROGRESS → COMPLETED`,
Parcel states `PENDING → LOADED → IN_TRANSIT → UNLOADED`, one of each required routing key,
and zero duplicate transition/Outbox rows.

## 4. Fixture and security boundary

- All public business actions use Gateway `:3000`; fixture-only setup, bounded evidence reads,
  and cleanup use the local service databases. No application cross-database query is part of
  the demo.
- The runner proves the Trip was generated from the operator-created schedule
  (`AUTO_FROM_SCHEDULE`) before applying the disclosed **Fixture-only time advance**. That
  helper changes only the generated Trip departure timestamp to reach the existing T-30
  auto-boarding threshold; it does not write Trip status, actor, Outbox, or idempotency data.
- The existing scheduler owns `SCHEDULED → BOARDING`; no production time-control endpoint,
  backdoor, or direct Trip status mutation is used. There is **no public/manual Trip-create endpoint**
  in this demo.
- Operator, driver, and assistant identities are generated for one isolated tenant. JWTs are
  short-lived and generated in memory; UUID-v4 idempotency keys are generated per mutation and
  are never printed or persisted in this handoff.
- Both the failure-injection and normal paths clean only their tracked generated IDs and verify
  zero residue. Evidence is redacted before it is written.

## 5. Sprint 5 prep / spillover

The Day-30 evidence reports `Final result: PASS`, including `Cleanup verified`, completion replay,
and all required Trip/Parcel states and Outbox events. Day 29 is independently marked `READY` in
the prior checklist. **No known spillover.**

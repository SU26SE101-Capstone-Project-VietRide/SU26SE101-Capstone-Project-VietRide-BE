# Day-29 Sprint-4 E2E evidence

- Fixture trip: `ab6d57b9-1273-4b5b-8142-2e4e21e6d044` (isolated generated IDs only).
- Loaded Outbox ids: `def785ee-8c07-47b9-9ae5-15d67c1abd4a`, `dab65b10-7f82-4d11-bf7e-d9c63f2aa6bc`, `42cc3df6-82d7-4ed1-922f-2564ac8df607`.
- Trip-start Outbox id: `3964acac-6f41-4a06-bea3-2e5aa4d0fec4`; API/Outbox actual-departure timestamps matched.
- Cargo Outbox/event/RabbitMQ MessageId: `735b2ed7-aba1-478e-a97c-e3a6095dd086`; exact seven-field payload matched at Outbox, broker probe, and Notification.
- Cargo Notification id: `ddd7faa0-0b63-4afc-b822-f6276ec45a40`; recipient `9c9078cd-6788-4eab-a1db-9fae5d14ee84`; canonical dedupe matched.
- Unload Outbox id: `add59bfa-d268-46e2-9908-526362967884`; both direct-recipient Notification dedupe keys matched.
- Lifecycle: 3 parcels loaded, Trip started, wrong-stop unload rejected, selected stop arrived, exactly 1 parcel unloaded, Trip completed by `ce54136b-5cce-4e27-8d50-a755a5922e8e`.
- Authorization: unassigned assistant, foreign-tenant assistant, and unassigned driver were denied without resource data disclosure.
- Credentials/tokens are never written to this file or stdout.

Assertions passed: 277

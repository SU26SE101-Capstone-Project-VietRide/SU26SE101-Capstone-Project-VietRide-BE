# Focused Parcel Compensation E2E

Result: PASS

Scope: Simulated users/one trip/accepted lost parcels; real Gateway, JWT, HTTP, PostgreSQL, Redis, RabbitMQ and Payment payout.

Excluded: Registration/email, booking, carriage/check-in/search SLA, VNPay, Notification, full-day suites and Docker builds.

- PASS: gateway live health
- PASS: identity live health
- PASS: trip live health
- PASS: payment live health
- PASS: parcel live health
- PASS: Gateway -> identity health
- PASS: Gateway -> trip health
- PASS: Gateway -> payment health
- PASS: Gateway -> parcel health
- PASS: Parcel rejects a correctly shaped Internal JWT with a forged signature
- PASS: RabbitMQ management reachable; isolated vietride.events topic exchange exists
- PASS: Minimal users, assigned driver/assistant and ONE simulated completed trip seeded
- PASS: Gateway role/tenant fences, proof matrix, wrong/duplicate evidence; failed decisions remain SUBMITTED
- PASS: undeclared: preview = mutation = PAID = wallet/ledger; replay safe
- PASS: declared: preview = mutation = PAID = wallet/ledger; replay safe
- PASS: inflated: preview = mutation = PAID = wallet/ledger; replay safe
- PASS: verified: preview = mutation = PAID = wallet/ledger; replay safe
- PASS: verified-null: preview = mutation = PAID = wallet/ledger; replay safe
- PASS: part-refunded: preview = mutation = PAID = wallet/ledger; replay safe
- PASS: Fully refunded + no verified proof: preview 200/zero; approval 422, no payout, transaction rolled back
- PASS: Appeal: no-proof cannot refund twice; VERIFIED delta 100000 paid exactly once; original award unchanged
- PASS: Passenger wallet API exposes compensation transactions; balance equals all awards + appeal delta
- PASS: FE read APIs: Admin sees six holding debits, Operator sees one post-settlement debit and all seven ledger entries

- Stopped parcel PID 20348
- Stopped payment PID 24236
- Stopped trip PID 31292
- Stopped identity PID 33356
- Stopped gateway PID 29100
- Dropped isolated pcl_e2e_8978aec38a_identity
- Dropped isolated pcl_e2e_8978aec38a_trip
- Dropped isolated pcl_e2e_8978aec38a_payment
- Dropped isolated pcl_e2e_8978aec38a_parcel
- Dropped test DB role
- Deleted isolated test vhost/queues
- Deleted test RabbitMQ user

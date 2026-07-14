# Sprint 3 — Demo script (Day 20)

> Sprint objective: a passenger can search a trip, book a seat, pay, cancel, and receive a wallet refund; an operator can monitor only bookings in its own tenant. This script is backed by the Day-20 local E2E evidence, not by manually prepared demo data.

## 1. Local prerequisites

- Docker Desktop is running and the repository dependencies have been installed.
- Start the local application profile from the repository root:

```powershell
docker compose --env-file .env -f infra/docker/docker-compose.yml --profile app up -d --build
```

- Do not paste JWTs, merchant keys, or production/customer data into the terminal, collection, or review notes. The local harness creates short-lived test identities and cleans its fixtures.

## 2. Reviewer command and expected evidence

Run this exact command from the repository root:

```powershell
npm run postman:full:local
```

Expected result: exit code `0`, with all required named D11–D19 coverage passing; the runner reports `14/14` execution steps, including setup/reset and the D18 cross-day check. The command orchestrates deterministic fixture creation and reverse-order cleanup; no pre-seeded booking, trip, wallet, token, or Postman environment value is required from the reviewer. The authoritative stage/seam list and final evidence are in [day-20-e2e-matrix.md](day-20-e2e-matrix.md).

## 3. Passenger journey

1. **Register, verify, and log in through the Gateway.** The Day-20 journey verifies the ADR 0004 response envelope and obtains a short-lived passenger token only at runtime.
2. **Top up the local Wallet.** Day 15 sends a signed local VNPay IPN and proves that the wallet is credited once; a replay is idempotent.
3. **Search and inspect a trip.** Day 11 activates a deterministic schedule, searches the generated trip through the Gateway, and reads its detail and seat map.
4. **Book and pay.** Day 12 proves atomic seat holds and the `HELD → BOOKED` transition. Day 16 proves Wallet payment and VNPay payment both eventually confirm the booking.
5. **Cancel and refund.** Day 16 cancels the Wallet-paid booking, verifies the cancellation response, and polls the wallet refund until the booking reaches `REFUNDED`.

## 4. VNPay boundary

The VNPay leg is a **local signed-IPN simulation**, not a real bank transaction and not a connection to a VNPay merchant sandbox. The signed IPN is the business confirmation signal; the runner then polls the Booking-owned resource until it reaches `CONFIRMED`. No merchant credential, banking account, payment reference, or customer data is stored in this repository or shown in the demo.

## 5. Operator monitor

Day 19 creates isolated tenant fixtures through the real Gateway/Booking/Identity seams. It proves that an operator sees its own booking list/detail and that access to a foreign-tenant booking is denied. This is the monitor segment to show after the passenger journey.

## 6. Review scope and known exclusions

- The green matrix contains no approved Sprint-3 stage exclusion or carry-over: D11 through D19 are required.
- Google OAuth is outside the Sprint-3 matrix because it requires an externally supplied Google ID token.
- The local VNPay IPN simulation does not demonstrate a real bank transaction, real merchant sandbox account, or production payment settlement.
- The command is intended for the local Docker environment; it is not a production-data or production-payment test.

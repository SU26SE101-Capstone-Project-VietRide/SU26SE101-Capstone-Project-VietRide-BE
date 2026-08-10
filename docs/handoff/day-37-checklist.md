# Day 37 — Independent audit checklist

> Re-audited on an isolated dynamically allocated port on 2026-08-02.

- **Status**: ✅ READY
- [x] `ACTIVE | PENDING_PAYMENT` quota and operator-user creation use `activePlan` atomically.
- [x] Parcel keeps the active plan during `PENDING_PAYMENT` and enforces `enableParcel`.
- [x] RAG verifies operator subscription through Internal JWT and enforces `enableRag`; global admin behavior is unaffected.
- [x] Pre-expiry warning is scheduled daily at 09:00 Asia/Ho_Chi_Minh; expiry/reset and event-driven 80% warning remain separate.
- [x] Public `POST /v1/operator/trips` is deferred consistently in SOT/API inventory.

## Verification run

| Command/check | Result | Evidence |
|---|---:|---|
| `npm run e2e:day37` | PASS | 13/13; dynamic port `50593`; quota hard limit and disabled Parcel/RAG flags pass; cleanup removes volumes. |
| `PendingPaymentEntitlementTests` + RAG entitlement specs | PASS | Non-zero .NET and Jest coverage; RAG full suite included in Nx test. |
| Identity + Parcel + RAG build/test | PASS | Identity 339+177; Parcel 448+83; Nx RAG tests green. |

Known gaps: none blocking Days 44–46.

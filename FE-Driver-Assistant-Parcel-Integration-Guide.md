# FE Driver/Assistant — Parcel API Integration Guide

> Source-verified against the current backend working tree on 2026-08-29, including the JWT-based stop-departure approval flow. Do not invent request fields or derive a separate FE state machine.

## Đọc nhanh trước khi nối API

Luồng happy case bắt buộc theo đúng thứ tự:

```text
Driver mở BOARDING
→ Assistant lấy manifest
→ quét QR để tra cứu
→ check-in
→ cân/đo bằng reweigh
→ chờ thanh toán bổ sung nếu có
→ load hàng lên xe
→ Driver start chuyến
→ Parcel sang IN_TRANSIT
→ Driver arrive đúng stop
→ Assistant unload
→ deliver
→ đối soát stop
→ Driver depart
```

Luồng sai bến/thất lạc:

```text
Quét sai nhưng hàng còn trên xe
→ không tạo sự cố, giữ hàng trên xe và đi tiếp.

Hàng đã nằm sai vị trí vật lý
→ Assistant custody-exception
→ chờ Driver/Operator duyệt bằng JWT của người duyệt
→ Operator tìm kiếm
→ tìm thấy trên xe thì tiếp tục chuyến cũ
→ tìm thấy ở bến sai thì Operator chọn chuyến forwarding
→ crew chuyến mới confirm-transfer.
```

Ba lỗi FE tuyệt đối không được lặp lại:

1. Không lấy `currentOperationalLocation` làm vị trí kiện hàng; vị trí kiện nằm ở `currentCustody`.
2. Đường dẫn stop hiện tại là `tripContext.currentOperationalLocation.location.id`, không phải `currentOperationalLocation.id`.
3. Sau check-in không gọi thêm `custody-scan`; bước kế tiếp là `reweigh`.

Các phần bên dưới giữ tên field, enum và error code bằng tiếng Anh đúng như source để agent FE có thể copy trực tiếp.

## Table of contents

1. [Purpose and non-negotiable rules](#1-purpose-and-non-negotiable-rules)
2. [Base URL, authentication, headers and response envelope](#2-base-url-authentication-headers-and-response-envelope)
3. [Role boundaries](#3-role-boundaries)
4. [Parcel states and action mapping](#4-parcel-states-and-action-mapping)
5. [Location fields — use the correct source](#5-location-fields--use-the-correct-source)
6. [Endpoint overview](#6-endpoint-overview)
7. [Flow A — origin happy path: receive, weigh and load](#7-flow-a--origin-happy-path-receive-weigh-and-load)
8. [Flow B — trip start and route-stop delivery](#8-flow-b--trip-start-and-route-stop-delivery)
9. [Flow C — delivery at destination station](#9-flow-c--delivery-at-destination-station)
10. [Flow D — wrong QR or wrong stop while the package remains on vehicle](#10-flow-d--wrong-qr-or-wrong-stop-while-the-package-remains-on-vehicle)
11. [Flow E — package was physically unloaded at the wrong stop](#11-flow-e--package-was-physically-unloaded-at-the-wrong-stop)
12. [Flow F — stop reconciliation finds an unresolved package](#12-flow-f--stop-reconciliation-finds-an-unresolved-package)
13. [Flow G — package has no readable QR or is unidentified](#13-flow-g--package-has-no-readable-qr-or-is-unidentified)
14. [Flow H — found package is forwarded to another trip](#14-flow-h--found-package-is-forwarded-to-another-trip)
15. [Direct custody scan — when it is and is not appropriate](#15-direct-custody-scan--when-it-is-and-is-not-appropriate)
16. [Delivery confirmation and fallback](#16-delivery-confirmation-and-fallback)
17. [Response handling and local state updates](#17-response-handling-and-local-state-updates)
18. [Idempotency and retry rules](#18-idempotency-and-retry-rules)
19. [Error handling](#19-error-handling)
20. [Suggested FE state and API client](#20-suggested-fe-state-and-api-client)
21. [Integration acceptance checklist](#21-integration-acceptance-checklist)
22. [Known backend contract gaps](#22-known-backend-contract-gaps)

## 1. Purpose and non-negotiable rules

This guide is for the Driver/Assistant Mobile team. It explains the exact order of actions and APIs from receiving a Parcel at the origin station until delivery, including wrong QR, wrong stop, missing scan, custody exception approval and forwarding.

The FE must follow these rules:

1. `currentOperationalLocation` is not the origin station, vehicle GPS or current Parcel custody.
2. `currentOperationalLocation = null` is valid before the trip starts, between stops and after a stop has departed.
3. Check-in, reweigh and load do not require `currentOperationalLocation`.
4. Check-in automatically creates `CHECKED_IN` custody at `ORIGIN_STATION`; do not call `custody-scan` immediately afterward.
5. Load is performed before Driver starts the trip. Driver start changes `LOADED` Parcels to `IN_TRANSIT` asynchronously through `trip.started`.
6. Normal unload always requires the real Parcel QR and the actual operational stop/destination.
7. A rejected unload does not mean the package was physically moved. If it remains on the vehicle, keep it there and continue to the correct stop.
8. Use `custody-exception` only when physical custody no longer matches the normal system flow, for example the package is already at the wrong stop.
9. Assistant reports a custody exception; Driver or Operator approves/rejects with the approver's own JWT. Never send reviewer UUID in the exception request.
10. Use backend `availableActions` as the source for available operations, but select the primary CTA by Parcel status. `CUSTODY_SCAN` is an optional supporting action, not the primary action for every card.

## 2. Base URL, authentication, headers and response envelope

### Base URLs

```text
Production Gateway: https://api.vietride.online
Local Gateway:      http://localhost:3000
Swagger:            https://api.vietride.online/docs
```

All mobile calls must go through Gateway. Do not call Parcel Service port `5005` or Trip Service port `5002` directly from the app.

### Authentication

```http
Authorization: Bearer <accessToken>
```

The token role must match the endpoint:

```text
ASSISTANT → /v1/assistant/**
DRIVER    → Driver-only /v1/driver/** and custody-exception decision
DRIVER or ASSISTANT → permitted /v1/crew/** endpoints
```

When the API returns `401`, the FE must run the application's existing token refresh/login flow and must not retry indefinitely with the expired token.

### Idempotency

Every mutation in this guide requires this header except QR lookup:

```http
Idempotency-Key: <uuid-v4>
```

Rules:

- generate one UUID for one user action;
- reuse that UUID when retrying the same uncertain request;
- generate a different UUID for a different action;
- do not generate a new key on every automatic network retry.

`POST /v1/assistant/trips/{tripId}/parcels/qr-scan` is read-only and does not require `Idempotency-Key`.

### Response envelope

Success:

```json
{
  "success": true,
  "statusCode": 200,
  "data": {},
  "meta": {
    "traceId": "...",
    "timestamp": "2026-08-29T05:00:00Z"
  }
}
```

Error:

```json
{
  "success": false,
  "statusCode": 409,
  "error": {
    "code": "PARCEL_CUSTODY_LOCATION_MISMATCH",
    "message": "...",
    "fields": [
      {
        "field": "expectedStop",
        "message": "uuid"
      },
      {
        "field": "actualStop",
        "message": "uuid"
      },
      {
        "field": "requiredAction",
        "message": "KEEP_ON_VEHICLE_OR_REPORT_CUSTODY_EXCEPTION"
      }
    ]
  },
  "meta": {
    "traceId": "...",
    "timestamp": "2026-08-29T05:00:00Z"
  }
}
```

`error.fields` is an array of `{ field, message }`, not a JSON object. Convert it to a map only inside the FE adapter if convenient. Always log `meta.traceId` in FE diagnostics.

## 3. Role boundaries

| Action | Assistant | Driver | Operator Web |
|---|:---:|:---:|:---:|
| Read Parcel manifest for assigned trip | Yes | Yes, through `/v1/crew/trips/{tripId}/parcels` | No |
| QR lookup, check-in, reweigh, load, unload, deliver | Yes | No | No |
| Start boarding/start trip | No | Yes | Separate Operator APIs |
| Arrive/depart/complete trip | Allowed by current Trip controller for assigned crew | Yes | Separate Operator APIs |
| Report Parcel custody exception | Yes | No | No |
| Approve/reject Assistant exception | No | Yes, assigned Driver | Yes, Staff/Admin |
| Record Operator search result/mark found/choose forwarding trip | No | No | Yes |
| Confirm receiving a forwarded Parcel | Yes, target crew | Yes, target crew | No |
| Register/match unidentified package | No | No | Operator Staff/Admin |

Important: Driver cannot call `/v1/assistant/**` with Driver JWT. Assistant cannot approve their own custody exception.

## 4. Parcel states and action mapping

### Happy-path state sequence

```text
RESERVED
  → CHECKED_IN
  → READY_TO_LOAD or PENDING_FINAL_PAYMENT
  → READY_TO_LOAD
  → LOADED
  → IN_TRANSIT
  → UNLOADED
  → DELIVERED_PENDING_CONFIRM
  → DELIVERY_CONFIRMED
```

### Primary FE action by status

| Parcel status | Primary UI action | API/action |
|---|---|---|
| `RESERVED` | Receive/check-in | `POST .../check-in` |
| `CHECKED_IN` | Enter actual dimensions/weight | `POST .../reweigh` |
| `PENDING_FINAL_PAYMENT` | Waiting for sender payment | No Assistant mutation |
| `READY_TO_LOAD` | Load onto vehicle | `POST .../load` |
| `LOADED` | Show “On vehicle” | Wait for Driver start; exception only if needed |
| `IN_TRANSIT` | Unload at correct location | `POST .../unload` |
| `UNLOADED` | Handoff/deliver | `POST .../deliver` |
| `DELIVERED_PENDING_CONFIRM` | Waiting for recipient confirmation | Resend email or manual confirm only when required |
| `PENDING_TRANSFER_CONFIRM` | Target crew confirms transfer | `POST /v1/crew/.../confirm-transfer` |
| `PENDING_OPERATOR_ACTION` | Show incident/approval state | Do not continue normal flow |

The backend currently calculates Assistant actions as follows:

```text
RESERVED: CHECK_IN
CHECKED_IN: REWEIGH
READY_TO_LOAD: LOAD
LOADED: CUSTODY_EXCEPTION
IN_TRANSIT: UNLOAD, CUSTODY_EXCEPTION
UNLOADED: DELIVER
Active incident: VIEW_INCIDENT
Valid supplemental physical scan at the current arrived stop: CUSTODY_SCAN
Eligible MISSING/MISSING_AFTER_DEPARTURE/UNSCANNED_HANDOFF: CONFIRM_FOUND_ON_VEHICLE
```

Do not display `CUSTODY_SCAN` unless it is present in `availableActions`. Check-in, load, unload,
deliver, and confirm-found already write their own custody facts, so FE must not call a second
custody scan after those mutations.

## 5. Location fields — use the correct source

### Exact manifest shape

```json
{
  "tripContext": {
    "trip": {
      "tripId": "uuid",
      "status": "SCHEDULED",
      "departureAt": "2026-08-29T02:00:00Z",
      "eta": "2026-08-29T08:00:00Z",
      "route": {
        "routeId": "uuid",
        "name": "HCMC - Can Tho",
        "origin": {
          "type": "ORIGIN_STATION",
          "id": "uuid",
          "name": "Ben xe Mien Tay",
          "orderIndex": null,
          "eta": null
        },
        "destination": {
          "type": "DESTINATION_STATION",
          "id": "uuid",
          "name": "Ben xe Can Tho",
          "orderIndex": null,
          "eta": null
        }
      },
      "vehicle": {
        "vehicleId": "uuid",
        "licensePlate": "51B-12345",
        "status": "ACTIVE"
      },
      "stops": []
    },
    "currentOperationalLocation": {
      "location": {
        "type": "ROUTE_STOP",
        "id": "uuid",
        "name": "Stop B",
        "orderIndex": 2,
        "eta": "2026-08-29T05:00:00Z"
      },
      "status": "ARRIVED",
      "actualArrivalAt": "2026-08-29T05:02:00Z",
      "actualDepartureAt": null
    },
    "orderedStops": []
  }
}
```

The correct path is:

```text
tripContext.currentOperationalLocation.location.id
tripContext.currentOperationalLocation.location.name
```

The following old access is wrong:

```text
tripContext.currentOperationalLocation.id
tripContext.currentOperationalLocation.name
```

### Location source matrix

| Business meaning | Correct data source |
|---|---|
| Origin station for check-in/load display | `tripContext.trip.route.origin` |
| Destination station | `tripContext.trip.route.destination` |
| Route stop where the vehicle is currently arrived and not departed | `tripContext.currentOperationalLocation.location` |
| Last confirmed physical location of one Parcel | `item.currentCustody.lastConfirmedLocation` |
| Expected Parcel drop-off | `item.dropoffLocation` |
| Vehicle GPS | Tracking feature only; never custody proof |

### What `currentOperationalLocation = null` means

It can mean any of these valid conditions:

- trip is still `SCHEDULED`/boarding at origin;
- vehicle is travelling between two route stops;
- current route stop has already departed;
- no route stop is currently in `ARRIVED` without departure state.

It does not mean that the Parcel location is unknown. Use `currentCustody` for Parcel tracking.

## 6. Endpoint overview

| Order/context | Role | Method and path | Body | Idempotency |
|---:|---|---|---|---|
| 1 | Assistant | `GET /v1/assistant/trips/{tripId}/parcels` | None | No |
| 1 | Driver | `GET /v1/crew/trips/{tripId}/parcels` | None | No |
| 2 | Driver | `POST /v1/driver/trips/{tripId}/boarding` | No body | Yes |
| 3 | Assistant | `POST /v1/assistant/trips/{tripId}/parcels/qr-scan` | `parcelCode` | No |
| 4 | Assistant | `POST /v1/assistant/parcels/{parcelId}/check-in` | `tripId`, `parcelCode`, `photoUrls` | Yes |
| 5 | Assistant | `POST /v1/assistant/parcels/{parcelId}/reweigh` | actual dimensions/weight | Yes |
| 6 | Assistant | `POST /v1/assistant/parcels/{parcelId}/load` | `tripId`, `parcelCode` | Yes |
| 7 | Driver | `POST /v1/driver/trips/{tripId}/start` | No body | Yes |
| 8 | Driver/assigned crew | `POST /v1/driver/trips/{tripId}/stops/{stopId}/arrive` | No body | Yes |
| 9 | Assistant | `POST /v1/assistant/parcels/{parcelId}/unload` | QR, actual location, photos | Yes |
| 10 | Assistant | `POST /v1/assistant/parcels/{parcelId}/deliver` | photos or empty body | Yes |
| 11 | Assistant | `POST /v1/assistant/trips/{tripId}/stops/{stopId}/reconcile` | scanned/manual IDs, override fields | Yes |
| 12 | Driver/assigned crew | `POST /v1/driver/trips/{tripId}/stops/{stopId}/depart` | No body | Yes |
| Destination | Driver/assigned crew | `POST /v1/driver/trips/{tripId}/destination/arrive` | No body | Yes |
| Exception report | Assistant | `POST /v1/assistant/parcels/{parcelId}/custody-exception` | incident and actual location | Yes |
| Exception review | Driver | `GET /v1/crew/parcels/{parcelId}/custody-exception` | None | No |
| Exception decision | Driver | `POST /v1/crew/parcels/{parcelId}/custody-exception-decision` | decision, note | Yes |
| Stop-departure approval read | Driver | `GET /v1/crew/parcel-stop-departure-approvals/{requestId}` | None | No |
| Stop-departure approval decision | Driver | `POST /v1/crew/parcel-stop-departure-approvals/{requestId}/decision` | decision, note | Yes |
| Transfer receive | Driver/Assistant target crew | `POST /v1/crew/parcels/{parcelId}/confirm-transfer` | `parcelCode` | Yes |
| Optional custody | Assistant | `POST /v1/assistant/parcels/{parcelId}/custody-scan` | event/location | Yes |
| Manual delivery confirmation | Driver/Assistant | `POST /v1/crew/parcels/{parcelId}/manual-confirm` | note | Yes |

## 7. Flow A — origin happy path: receive, weigh and load

```text
Driver opens boarding
  → Assistant loads manifest
  → QR lookup
  → check-in
  → reweigh
  → wait for final payment when required
  → load
  → Parcel LOADED
```

### A1. Driver opens boarding

```http
POST /v1/driver/trips/{tripId}/boarding
Authorization: Bearer <driverAccessToken>
Idempotency-Key: <uuid-v4>
```

No request body.

This is a Trip operation. Current Parcel backend does not require Trip `BOARDING` for check-in/reweigh/load, but this is the expected operational order for the app.

### A2. Assistant loads the whole manifest screen

```http
GET /v1/assistant/trips/{tripId}/parcels?page=1&pageSize=100
Authorization: Bearer <assistantAccessToken>
```

Optional filters:

| Query | Type | Default | Description |
|---|---|---:|---|
| `stopId` | UUID | `null` | Parcels expected at a stop |
| `status` | string | `null` | Parcel status filter |
| `hasException` | boolean | `null` | Filter active exception |
| `search` | string | `null` | Search supported by backend read query |
| `page` | integer >= 1 | `1` | Page number |
| `pageSize` | integer 1–100 | `20` | Page size |

Use this one response to render trip, route, vehicle, ordered stops, counts and Parcel cards. Do not call Parcel detail/trace once per row.

Driver can load the same screen-ready manifest with Driver JWT through:

```http
GET /v1/crew/trips/{tripId}/parcels?page=1&pageSize=100
Authorization: Bearer <driverAccessToken>
```

The Driver endpoint uses the same filters and response type but authorizes the caller as assigned
Driver instead of assigned Assistant. Driver rows do not receive check-in/load/unload/deliver
actions. A pending Assistant report is discoverable directly on its Parcel row.

Each item contains:

```text
parcelId, parcelCode, status
recipientName, recipientPhone
dropoffStopId, dropoffLocation
sizeCategory, estimatedSizeCategory, actualSizeCategory
estimatedWeightKg, actualWeightKg
balanceRequiredVnd, balancePaidVnd, finalPaymentDeadline
description, photoUrl
currentCustody, activeIncident
paymentState, identityCheckHints
availableActions
custodyExceptionApproval
```

### A3. QR lookup

```http
POST /v1/assistant/trips/{tripId}/parcels/qr-scan
Authorization: Bearer <assistantAccessToken>
Content-Type: application/json
```

```json
{
  "parcelCode": "VR-PCL-20260829-ABCDEFG2"
}
```

Accepted Parcel code formats in current validator:

```text
VR-PCL-YYYYMMDD-8 characters excluding I/O/1/0
VRP-YYYYMMDD-8 uppercase alphanumeric characters
```

This endpoint is lookup-only. It does not check in, weigh, load or create a custody event.

Use its `data.parcelState.parcelId` as the path ID for the next action. Never derive `parcelId` from the QR string.

### A4. Check-in at origin

Precondition:

```text
Parcel status = RESERVED
Caller = assigned ASSISTANT
Check-in deadline has not passed
```

```http
POST /v1/assistant/parcels/{parcelId}/check-in
Authorization: Bearer <assistantAccessToken>
Idempotency-Key: <uuid-v4>
Content-Type: application/json
```

```json
{
  "tripId": "89136f0c-2f83-479a-9009-e92bf7a6c755",
  "parcelCode": "VR-PCL-20260829-ABCDEFG2",
  "photoUrls": [
    "https://<firebase-storage>/parcel-ops/<operatorId>/<assistantUserId>/<parcelId>/check-in-1.jpg"
  ]
}
```

Validation:

- `tripId` required UUID;
- `parcelCode` required;
- `photoUrls` optional, maximum 3;
- every photo URL must be an owned Firebase Parcel evidence URL under `parcel-ops/{operatorId}/{assistantUserId}/{parcelId}/`.

Success:

```text
RESERVED → CHECKED_IN
Custody event → CHECKED_IN at ORIGIN_STATION
```

Apply the returned `AssistantParcelActionResponse` directly to the card. Do not call `custody-scan` for `CHECKED_IN`.

### A5. Reweigh and measure

Precondition:

```text
Parcel status = CHECKED_IN
Load cutoff has not passed
```

```http
POST /v1/assistant/parcels/{parcelId}/reweigh
Authorization: Bearer <assistantAccessToken>
Idempotency-Key: <uuid-v4>
Content-Type: application/json
```

```json
{
  "actualLengthCm": 42.5,
  "actualWidthCm": 31,
  "actualHeightCm": 21,
  "actualWeightKg": 6.2
}
```

All four decimal values are required and must be greater than `0`.

This endpoint does not return `AssistantParcelActionResponse`. It returns:

```json
{
  "parcelId": "uuid",
  "parcelCode": "VR-PCL-20260829-ABCDEFG2",
  "status": "PENDING_FINAL_PAYMENT",
  "actualSizeCategory": "MEDIUM",
  "actualChargeableWeightKg": 6.2,
  "finalGrossPriceVnd": 180000,
  "discountAmountVnd": 0,
  "finalTotalPriceVnd": 180000,
  "depositPaidVnd": 30000,
  "balanceRequiredVnd": 150000,
  "refundDueVnd": 0,
  "finalPaymentDeadline": "2026-08-29T01:40:00Z"
}
```

FE branches:

```text
status = PENDING_FINAL_PAYMENT
  → disable LOAD
  → show balanceRequiredVnd and finalPaymentDeadline
  → wait for Passenger payment/event or refresh manifest

status = READY_TO_LOAD
  → show LOAD

status = PENDING_OPERATOR_ACTION
  → capacity exception; show operator handling state
```

Do not pass this response through the `AssistantParcelActionResponse` mapper; the shapes are different.

### A6. Load onto vehicle

Precondition:

```text
Parcel status = READY_TO_LOAD
Caller = assigned ASSISTANT
```

```http
POST /v1/assistant/parcels/{parcelId}/load
Authorization: Bearer <assistantAccessToken>
Idempotency-Key: <uuid-v4>
Content-Type: application/json
```

```json
{
  "tripId": "89136f0c-2f83-479a-9009-e92bf7a6c755",
  "parcelCode": "VR-PCL-20260829-ABCDEFG2"
}
```

The request disallows unknown JSON fields. Do not send location, reviewer ID or client-computed status.

Success:

```text
READY_TO_LOAD → LOADED
Trip cargo → loaded
Custody event → LOADED at origin
```

Driver does not need to start the trip before Assistant loads the package.

## 8. Flow B — trip start and route-stop delivery

```text
All expected packages LOADED
  → Driver starts trip
  → Parcel IN_TRANSIT
  → Driver arrives at route stop
  → currentOperationalLocation becomes available
  → Assistant unloads correct Parcel
  → Assistant delivers/handoffs
  → Assistant reconciles stop
  → Driver departs stop
```

### B1. Driver starts the trip

```http
POST /v1/driver/trips/{tripId}/start
Authorization: Bearer <driverAccessToken>
Idempotency-Key: <uuid-v4>
```

No request body.

Trip Service emits `trip.started`. Parcel Service consumes that event and moves loaded Parcels:

```text
LOADED → IN_TRANSIT
```

The change is asynchronous. The FE should refresh manifest/state after the trip start response or when returning to the Parcel tab; do not assume the Parcel event has already been consumed in the same millisecond.

### B2. Driver arrives at route stop

```http
POST /v1/driver/trips/{tripId}/stops/{stopId}/arrive
Authorization: Bearer <driverAccessToken>
Idempotency-Key: <uuid-v4>
```

No request body.

After success, reload the manifest once. The expected operational location is then:

```text
tripContext.currentOperationalLocation.location.id = stopId
tripContext.currentOperationalLocation.status = ARRIVED
tripContext.currentOperationalLocation.actualDepartureAt = null
```

### B3. Assistant unloads at the correct route stop

Preconditions:

```text
Parcel status = IN_TRANSIT
Parcel dropoffStopId = current stopId
Trip current stop = ARRIVED
actualDepartureAt = null
Caller = assigned ASSISTANT
```

```http
POST /v1/assistant/parcels/{parcelId}/unload
Authorization: Bearer <assistantAccessToken>
Idempotency-Key: <uuid-v4>
Content-Type: application/json
```

```json
{
  "parcelCode": "VR-PCL-20260829-ABCDEFG2",
  "actualLocation": {
    "kind": "ROUTE_STOP",
    "id": "<tripContext.currentOperationalLocation.location.id>"
  },
  "photoUrls": [
    "https://..."
  ]
}
```

Success:

```text
IN_TRANSIT → UNLOADED
Trip cargo released
Custody event → UNLOADED at ROUTE_STOP
```

Never send `tripContext.currentOperationalLocation.id`; that path does not exist.

### B4. Assistant hands the package to recipient

```http
POST /v1/assistant/parcels/{parcelId}/deliver
Authorization: Bearer <assistantAccessToken>
Idempotency-Key: <uuid-v4>
Content-Type: application/json
```

Body may be omitted, `{}`, or:

```json
{
  "photoUrls": [
    "https://<firebase-storage>/parcel-ops/<operatorId>/<assistantUserId>/<parcelId>/delivery-1.jpg"
  ]
}
```

`photoUrls` is optional, maximum 3, and must follow the same owned Firebase prefix rule.

Success:

```text
UNLOADED → DELIVERED_PENDING_CONFIRM
Custody event → HANDOFF
```

Recipient confirmation is a Passenger/public delivery-token flow, not the next normal Assistant scan.

### B5. Reconcile before departure

Call the reconciliation flow in section 12. Only show the Driver depart CTA when the FE has a successful reconciliation result with `canDepart = true`.

### B6. Driver departs route stop

```http
POST /v1/driver/trips/{tripId}/stops/{stopId}/depart
Authorization: Bearer <driverAccessToken>
Idempotency-Key: <uuid-v4>
```

No request body.

After departure, `currentOperationalLocation` can become `null`; that is expected.

## 9. Flow C — delivery at destination station

This applies when the Parcel was created with `dropoffStopId = null`.

### C1. Driver marks destination arrival

```http
POST /v1/driver/trips/{tripId}/destination/arrive
Authorization: Bearer <driverAccessToken>
Idempotency-Key: <uuid-v4>
```

No request body.

### C2. Assistant unloads at destination station

```http
POST /v1/assistant/parcels/{parcelId}/unload
Authorization: Bearer <assistantAccessToken>
Idempotency-Key: <uuid-v4>
Content-Type: application/json
```

```json
{
  "parcelCode": "VR-PCL-20260829-ABCDEFG2",
  "actualLocation": {
    "kind": "DESTINATION_STATION",
    "id": "<tripContext.trip.route.destination.id>"
  },
  "photoUrls": []
}
```

Backend requires the Trip destination to have actually arrived. Do not use `currentOperationalLocation` for destination-station unload.

Then call `/deliver` as described in B4.

### C3. Reconcile terminal cargo before completing the Trip

After all terminal unload attempts, Assistant calls the bodyless reconciliation endpoint:

```http
POST /v1/assistant/trips/{tripId}/destination/reconcile
Authorization: Bearer <assistantAccessToken>
Idempotency-Key: <uuid-v4>
```

Do not send `scannedParcelIds` or `manualExceptionParcelIds`. Backend reads persisted custody
events and returns `expectedCount`, `scannedCount`, `manualExceptionCount`,
`unresolvedParcels[]`, `canComplete`, and `requiresDriverCompletion`. The Driver completes the Trip
only after this reconciliation; unresolved cargo remains in search and is not declared lost
automatically.

## 10. Flow D — wrong QR or wrong stop while the package remains on vehicle

### Case D1. Scanned QR belongs to another Parcel

The lookup/action can return:

```text
SCAN_IDENTITY_MISMATCH
requiredAction = VERIFY_PARCEL_IDENTITY
```

FE behavior:

1. do not mutate the local card;
2. show the identity mismatch;
3. compare `identityCheckHints.photoUrl`, description, weight and dimensions;
4. keep the physical package under current custody;
5. scan the correct QR.

Do not automatically create an incident merely because the operator scanned the wrong label once.

### Case D2. Correct Parcel is scanned at the wrong stop

Unload returns `409 PARCEL_CUSTODY_LOCATION_MISMATCH` with:

```text
expectedStop
actualStop
requiredAction = KEEP_ON_VEHICLE_OR_REPORT_CUSTODY_EXCEPTION
```

If the package is still on the vehicle:

```text
do not call custody-exception
do not call unload again with a fake stop
keep the Parcel IN_TRANSIT
continue to expected stop
unload normally at the expected stop
```

This is a prevented mistake, not yet a physical custody exception.

## 11. Flow E — package was physically unloaded at the wrong stop

```text
Normal unload rejected or was bypassed physically
  → package is already at wrong stop
  → Assistant reports custody exception
  → PENDING_APPROVAL
  → assigned Driver or Operator approves/rejects
  → approved: SEARCHING and search tasks start
  → Operator marks found and chooses recovery/forwarding
```

### E1. Assistant reports the physical exception

```http
POST /v1/assistant/parcels/{parcelId}/custody-exception
Authorization: Bearer <assistantAccessToken>
Idempotency-Key: <uuid-v4>
Content-Type: application/json
```

```json
{
  "incidentType": "WRONG_STOP",
  "actualLocationType": "ROUTE_STOP",
  "actualLocationId": "wrong-stop-uuid",
  "locationSnapshot": "Ben B",
  "temporaryExceptionTag": null,
  "description": "Package was physically removed from the vehicle at Ben B",
  "observedWeightKg": 6.2,
  "evidenceUrls": [
    "https://..."
  ],
  "reason": "Physical custody no longer matches the normal unload flow"
}
```

Request validation:

| Field | Required | Rule |
|---|:---:|---|
| `incidentType` | Yes | One current `ParcelIncidentType` enum value |
| `actualLocationType` | Yes | `ORIGIN_STATION`, `DESTINATION_STATION`, `ROUTE_STOP`, `VEHICLE`, `WAREHOUSE` |
| `actualLocationId` | No | Nullable UUID; current exception validator does not enforce a per-location ID rule |
| `locationSnapshot` | No | string or `null` |
| `temporaryExceptionTag` | No | maximum 100 characters |
| `description` | No | maximum 2000 characters |
| `observedWeightKg` | No | greater than `0` when present |
| `evidenceUrls` | No | string array |
| `reason` | Yes | non-empty, maximum 1000 characters |

Supported incident types in current domain:

```text
MISSING
WRONG_STOP
DELIVERY_NOT_RECEIVED
PARTIAL_LOSS
DAMAGED
SCAN_IDENTITY_MISMATCH
PACKAGE_IDENTITY_MISMATCH
UNSCANNED_HANDOFF
MISSING_AFTER_DEPARTURE
```

Do not send any of these fields:

```text
supervisorApprovalUserId
reviewedByUserId
reviewerUserId
approvedByUserId
```

The backend gets `reportedByUserId` from Assistant JWT.

Success is HTTP `202`:

```text
request.status = PENDING_APPROVAL
incidentStatus = OPEN
searchDeadline = null
availableActions = [WAIT_FOR_APPROVAL]
Parcel = PENDING_OPERATOR_ACTION
```

At this stage:

- no manual custody event has been approved yet;
- search tasks have not started;
- FE must not show the 72-hour search SLA;
- FE must not allow mark found, forwarding or lost actions.

### E2. Driver reads the pending request

The Driver needs the `parcelId`. Current backend does not provide a Driver approval queue.

```http
GET /v1/crew/parcels/{parcelId}/custody-exception
Authorization: Bearer <driverAccessToken>
```

Only the Driver assigned to the Parcel's Trip can read it.

Pending response includes:

```text
requestId, parcelId, incidentId
incidentType, incidentStatus, status
actualLocationType, actualLocationId, locationSnapshot
temporaryExceptionTag, description, observedWeightKg
evidenceReferences, reason
reportedByUserId, reportedByRole, reportedAt
reviewedByUserId, reviewedAt, reviewedByRole, reviewNote
approvedCustodyEventId, searchDeadline
availableActions = [APPROVE, REJECT]
```

### E3. Driver approves or rejects

```http
POST /v1/crew/parcels/{parcelId}/custody-exception-decision
Authorization: Bearer <driverAccessToken>
Idempotency-Key: <uuid-v4>
Content-Type: application/json
```

Approve:

```json
{
  "decision": "APPROVE",
  "note": "Verified the package and wrong-stop evidence"
}
```

Reject:

```json
{
  "decision": "REJECT",
  "note": "Vehicle inspection confirms the package is still onboard"
}
```

Validation:

- `decision`: exactly `APPROVE` or `REJECT` after backend normalization;
- `note`: optional, maximum 2000 characters;
- reviewer identity and role come from Driver JWT.

Approve result:

```text
approval status = APPROVED
incident status = SEARCHING
approvedCustodyEventId != null
searchDeadline != null
custody event = MANUAL_CUSTODY_EXCEPTION
availableActions = [CONTINUE_SEARCH]
```

Reject result:

```text
approval status = REJECTED
incident status = RESOLVED
approvedCustodyEventId = null
no MANUAL_CUSTODY_EXCEPTION event
availableActions = []
```

The Driver app stops here. Incident assignment, search results, mark-found and forwarding selection belong to Operator Web.

## 12. Flow F — stop reconciliation finds an unresolved package

Run this after the expected unload operations and before presenting the Driver depart action.

```http
POST /v1/assistant/trips/{tripId}/stops/{stopId}/reconcile
Authorization: Bearer <assistantAccessToken>
Idempotency-Key: <uuid-v4>
Content-Type: application/json
```

Normal request:

```json
{}
```

Meaning:

- FE does not send `scannedParcelIds` or `manualExceptionParcelIds`;
- backend derives both counts from persisted `UNLOADED` and approved
  `MANUAL_CUSTODY_EXCEPTION` custody events;
- `{}` and an omitted body are both valid for normal reconciliation.

Response:

```json
{
  "expectedCount": 3,
  "scannedCount": 2,
  "manualExceptionCount": 0,
  "unresolvedParcels": [
    {
      "parcelId": "uuid",
      "parcelCode": "VR-PCL-20260829-ABCDEFG2",
      "photoUrl": "https://...",
      "expectedDropoff": {
        "type": "ROUTE_STOP",
        "id": "uuid",
        "name": "Stop B",
        "orderIndex": 2,
        "eta": "2026-08-29T05:00:00Z"
      },
      "lastCustody": null,
      "incidentId": "uuid",
      "incidentType": "UNSCANNED_HANDOFF",
      "reason": "No verified unload or manual custody event exists for this stop.",
      "recommendedAction": "SEARCH_VEHICLE_OR_STATION"
    }
  ],
  "canDepart": false,
  "requiresSupervisorApproval": true,
  "departureOverrideRequest": null,
  "unresolvedParcelIds": [
    "uuid"
  ]
}
```

If unresolved Parcels exist, backend opens `UNSCANNED_HANDOFF` incidents and creates vehicle/station search tasks.

### F1. Normal reconciliation result

```text
canDepart = true
  → enable Driver depart CTA

canDepart = false
  → show unresolvedParcels[]
  → direct crew to search vehicle/station
  → do not send client-asserted scan IDs
  → do not call Driver depart yet
```

If the missing Parcel is found and a verified `UNLOADED` or approved `MANUAL_CUSTODY_EXCEPTION` event is created, run reconciliation again with a new idempotency key. The new response can return `canDepart = true`.

### F2. Assistant requests permission to depart with unresolved Parcels

Only use this branch when the operation must depart before the unresolved Parcels are physically resolved.

Assistant calls the same endpoint again with a reason:

```http
POST /v1/assistant/trips/{tripId}/stops/{stopId}/reconcile
Authorization: Bearer <assistantAccessToken>
Idempotency-Key: <new-uuid-v4>
Content-Type: application/json
```

```json
{
  "departureOverrideReason": "Vehicle must leave the stop; unresolved package search continues at station"
}
```

Do not send `supervisorApprovalUserId`. The field no longer exists in this request contract.

When unresolved Parcels remain, backend creates or replays a departure approval request and returns it in:

```json
{
  "canDepart": false,
  "requiresSupervisorApproval": true,
  "departureOverrideRequest": {
    "requestId": "uuid",
    "tripId": "uuid",
    "stopId": "uuid",
    "operatorId": "uuid",
    "unresolvedParcelIds": ["uuid"],
    "departureOverrideReason": "Vehicle must leave the stop; unresolved package search continues at station",
    "status": "PENDING_APPROVAL",
    "requestedByUserId": "assistant-uuid",
    "requestedByRole": "ASSISTANT",
    "requestedAt": "2026-08-29T05:10:00Z",
    "reviewedByUserId": null,
    "reviewedByRole": null,
    "reviewedAt": null,
    "reviewNote": null,
    "availableActions": ["APPROVE", "REJECT"]
  }
}
```

Assistant stores `departureOverrideRequest.requestId` and passes that ID to the Driver UI. It must not pass a user UUID as approval.

### F3. Driver reads the departure request

```http
GET /v1/crew/parcel-stop-departure-approvals/{requestId}
Authorization: Bearer <driverAccessToken>
```

Only the Driver assigned to that Trip can read the request.

### F4. Driver approves or rejects departure

```http
POST /v1/crew/parcel-stop-departure-approvals/{requestId}/decision
Authorization: Bearer <driverAccessToken>
Idempotency-Key: <uuid-v4>
Content-Type: application/json
```

Approve:

```json
{
  "decision": "APPROVE",
  "note": "Approved departure; station staff continue the Parcel search"
}
```

Reject:

```json
{
  "decision": "REJECT",
  "note": "Vehicle must remain until the cargo compartment is checked again"
}
```

Reviewer identity comes from Driver JWT. Supported decisions are `APPROVE` and `REJECT`.

### F5. Driver departs only after clearance

After approval, Driver can call:

```http
POST /v1/driver/trips/{tripId}/stops/{stopId}/depart
Authorization: Bearer <driverAccessToken>
Idempotency-Key: <uuid-v4>
```

Trip Service asks Parcel Service for stop-departure clearance. Outcomes:

```text
No unresolved Parcel
  → CLEAR
  → departure allowed

Unresolved Parcels + matching approved request
  → APPROVED_OVERRIDE
  → departure allowed

Unresolved Parcels without approval or rejected request
  → BLOCKED_PENDING_APPROVAL
  → 409 PARCEL_STOP_RECONCILIATION_REQUIRED
```

The `409` fields can contain:

```text
approvalRequestId
unresolvedParcelIds
requiredAction = RECONCILE_OR_APPROVE_STOP_DEPARTURE
```

FE must open the existing approval request from `approvalRequestId`; it must not create a fake local approval.

## 13. Flow G — package has no readable QR or is unidentified

### Known Parcel but unreadable QR

Normal unload requires `parcelCode`; an empty code returns `PARCEL_SCAN_REQUIRED`.

If the package can be tied to a known `parcelId` but physical custody is abnormal, Assistant can report `/custody-exception` with:

```json
{
  "incidentType": "PACKAGE_IDENTITY_MISMATCH",
  "actualLocationType": "WAREHOUSE",
  "actualLocationId": "station-uuid",
  "locationSnapshot": "Lost-and-found warehouse",
  "temporaryExceptionTag": "TEMP-B-001",
  "description": "QR label is damaged",
  "observedWeightKg": 6.2,
  "evidenceUrls": ["https://..."],
  "reason": "Package cannot use the normal QR unload flow"
}
```

### Unknown package with no known `parcelId`

Driver/Assistant cannot register it through an Assistant endpoint. Operator Staff/Admin must use:

```text
POST /v1/stations/parcels/unidentified
GET  /v1/operator/unidentified-packages/{packageId}/match-candidates
POST /v1/stations/parcels/unidentified/{packageId}/match
```

Driver/Assistant UI must:

1. assign/display a temporary physical tag according to operator procedure;
2. capture description, weight and photo evidence;
3. send the case to Operator Staff/Admin;
4. not guess a Parcel match locally;
5. not call normal unload with another Parcel's QR.

## 14. Flow H — found package is forwarded to another trip

Operator Web performs search, marks the Parcel found, gets forwarding options and selects a target Trip. Driver/Assistant app only handles the target-crew confirmation step.

Expected state before crew confirmation:

```text
Incident = FORWARDING
Parcel status = PENDING_TRANSFER_CONFIRM
Parcel transferTargetTripId = target Trip
```

Target Driver or Assistant scans the physical QR:

```http
POST /v1/crew/parcels/{parcelId}/confirm-transfer
Authorization: Bearer <targetDriverOrAssistantAccessToken>
Idempotency-Key: <uuid-v4>
Content-Type: application/json
```

```json
{
  "parcelCode": "VR-PCL-20260829-ABCDEFG2"
}
```

Rules:

- caller must be Driver/Assistant assigned to target Trip;
- scanned code must match the pending transfer;
- confirmation window is 30 minutes from transfer request;
- wrong target/QR/status returns `PARCEL_NOT_TRANSFERABLE`;
- expired confirmation returns `PARCEL_TRANSFER_CONFIRMATION_DEADLINE_PASSED`.

Success response data:

```json
{
  "parcelId": "uuid",
  "parcelCode": "VR-PCL-20260829-ABCDEFG2",
  "status": "LOADED",
  "tripId": "target-trip-uuid",
  "transferTargetTripId": "target-trip-uuid",
  "transferConfirmedAt": "2026-08-29T06:00:00Z",
  "returnReason": null,
  "returnedAt": null,
  "refundChoice": null,
  "refundAmount": null
}
```

Backend records:

```text
old leg → FORWARDED
custody → FORWARDED_OUT
new leg → ACTIVE
custody → FORWARDED_IN on target vehicle
Parcel → LOADED on target Trip
```

When the target Trip starts, Parcel becomes `IN_TRANSIT`, then follows the normal unload/deliver flow.

## 15. Direct custody scan — when it is and is not appropriate

```http
POST /v1/assistant/parcels/{parcelId}/custody-scan
Authorization: Bearer <assistantAccessToken>
Idempotency-Key: <uuid-v4>
Content-Type: application/json
```

```json
{
  "parcelCode": "VR-PCL-20260829-ABCDEFG2",
  "eventType": "ARRIVED_AT_STOP",
  "actualLocationType": "ROUTE_STOP",
  "actualLocationId": "stop-uuid",
  "locationSnapshot": "Stop B",
  "evidenceReferences": [],
  "reason": "Inventory scan at stop"
}
```

Allowed direct event types:

```text
ACCEPTED
ARRIVED_AT_STOP
HANDOFF
RETURNED_TO_STATION
```

Allowed location types:

```text
ORIGIN_STATION
DESTINATION_STATION
ROUTE_STOP
VEHICLE
WAREHOUSE
```

`actualLocationId` is required except when `actualLocationType = VEHICLE`.

Use direct custody scan only for a real additional custody observation. Do not use it instead of:

```text
check-in → creates CHECKED_IN
load → creates LOADED
unload → creates UNLOADED
deliver → creates HANDOFF
confirm-transfer → creates FORWARDED_OUT and FORWARDED_IN
recipient/manual confirmation → completes delivery
```

## 16. Delivery confirmation and fallback

### Normal path

After Assistant calls `/deliver`, the Parcel becomes `DELIVERED_PENDING_CONFIRM`. Recipient uses the delivery token flow outside Driver/Assistant app.

### Resend recipient email

```http
POST /v1/crew/parcels/{parcelId}/resend-delivery-email
Authorization: Bearer <driverOrAssistantAccessToken>
Idempotency-Key: <uuid-v4>
```

No request body.

### Manual fallback confirmation

Use only when the recipient cannot use the normal token flow and operator procedure permits manual evidence.

```http
POST /v1/crew/parcels/{parcelId}/manual-confirm
Authorization: Bearer <driverOrAssistantAccessToken>
Idempotency-Key: <uuid-v4>
Content-Type: application/json
```

```json
{
  "confirmNote": "Recipient ID checked and signed at Stop B"
}
```

The backend also accepts legacy field `note`, but FE should standardize on `confirmNote`.

Validation:

- resolved note is required and not whitespace;
- maximum 500 characters;
- Parcel must be `DELIVERED_PENDING_CONFIRM`;
- caller must be assigned Driver/Assistant for the Trip.

Success:

```text
DELIVERED_PENDING_CONFIRM → DELIVERY_CONFIRMED
```

## 17. Response handling and local state updates

### Screen-ready action response

QR lookup, check-in, load, unload, custody-scan and deliver return:

```ts
type AssistantParcelActionResponse = {
  parcelState: {
    parcelId: string;
    parcelCode: string;
    status: string;
    dropoffLocation: ReliabilityLocation;
    paymentState: AssistantParcelPaymentState;
    identityCheckHints: AssistantParcelIdentityHints;
  };
  currentCustody: ReliabilityCustodySummary | null;
  activeIncident: ReliabilityIncidentSummary | null;
  createdCustodyEvent: {
    eventId: string;
    eventType: string;
    actualLocationType: string | null;
    actualLocationId: string | null;
    locationSnapshot: string | null;
    occurredAt: string;
    sequence: number;
  } | null;
  availableActions: string[];
  warning: string | null;
};
```

Update the current card without refetching the whole manifest:

```ts
function applyActionResponse(
  card: AssistantTripParcel,
  data: AssistantParcelActionResponse,
): AssistantTripParcel {
  return {
    ...card,
    parcelId: data.parcelState.parcelId,
    parcelCode: data.parcelState.parcelCode,
    status: data.parcelState.status,
    dropoffLocation: data.parcelState.dropoffLocation,
    paymentState: data.parcelState.paymentState,
    identityCheckHints: data.parcelState.identityCheckHints,
    currentCustody: data.currentCustody,
    activeIncident: data.activeIncident,
    availableActions: data.availableActions,
  };
}
```

### Reweigh response is different

`reweigh` returns price/recalculation fields, not `parcelState/currentCustody/availableActions`. Use a separate DTO and update at least:

```text
status
actualSizeCategory
actualChargeableWeightKg
finalTotalPriceVnd
balanceRequiredVnd
refundDueVnd
finalPaymentDeadline
```

If the payment completes asynchronously, refresh the manifest/card once to discover `READY_TO_LOAD`.

### Custody exception response is different

Use a separate `CustodyExceptionApproval` DTO. Do not pass it to the Parcel card action mapper.

## 18. Idempotency and retry rules

Recommended client behavior:

```ts
type PendingMutation = {
  operation: string;
  resourceId: string;
  idempotencyKey: string;
  bodyHash: string;
};
```

Examples:

```text
User taps Check in once
  → create key K1
  → request times out
  → retry same check-in body with K1

User later taps Reweigh
  → create key K2

Driver approves exception
  → create key K3

Driver changes mind and tries Reject
  → this is not a retry; backend will reject because decision already exists
```

Never reuse one idempotency key across different endpoints or different request bodies.

## 19. Error handling

| Error code | FE action |
|---|---|
| `FORBIDDEN` | User is not assigned/tenant-scoped; close action and refresh permissions/manifest |
| `PARCEL_NOT_FOUND` | Remove stale local result or refetch once; do not reveal cross-tenant data |
| `TRIP_NOT_FOUND` | Trip no longer exists/visible; leave Parcel flow and refresh schedule |
| `INVALID_STATUS` | Local state is stale or API order is wrong; refetch manifest once |
| `PARCEL_CHECK_IN_CLOSED` | Check-in deadline passed; show Operator escalation |
| `PARCEL_LOAD_CUTOFF_PASSED` | Reweigh/load cutoff passed; show Operator escalation |
| `PARCEL_SCAN_REQUIRED` | Normal unload requires QR; do not send an empty code |
| `SCAN_IDENTITY_MISMATCH` | Stop mutation, retain custody, verify physical package |
| `PARCEL_CUSTODY_LOCATION_REQUIRED` | Request location kind/id missing or invalid |
| `PARCEL_CUSTODY_LOCATION_MISMATCH` | Show expected/actual; keep on vehicle or report physical exception |
| `DROP_OFF_STOP_NOT_ARRIVED` | Driver must arrive at the expected stop first |
| `DESTINATION_TERMINAL_NOT_ARRIVED` | Driver must call destination arrival first |
| `PARCEL_CUSTODY_EVENT_NOT_FOUND` | Reconciliation IDs do not have matching verified custody events |
| `PARCEL_STOP_RECONCILIATION_REQUIRED` | Driver departure is blocked; open `approvalRequestId` or reconcile unresolved Parcels |
| `PARCEL_STOP_DEPARTURE_APPROVAL_NOT_FOUND` | Approval request is missing, wrong tenant or unavailable to this Driver |
| `PARCEL_STOP_DEPARTURE_ALREADY_DECIDED` | Another reviewer decided; GET the request once and use server state |
| `PARCEL_INCIDENT_ALREADY_OPEN` | Show existing active incident instead of creating another report |
| `PARCEL_CUSTODY_EXCEPTION_REQUEST_NOT_FOUND` | No readable pending request for this Driver/Parcel |
| `PARCEL_CUSTODY_EXCEPTION_ALREADY_DECIDED` | Another reviewer won; GET latest request once |
| `PARCEL_CUSTODY_EXCEPTION_APPROVAL_REQUIRED` | Exception is still pending; do not continue search/forward/lost actions |
| `PARCEL_NOT_TRANSFERABLE` | Wrong QR/target/status for transfer; refresh assigned target Trip |
| `PARCEL_TRANSFER_CONFIRMATION_DEADLINE_PASSED` | 30-minute transfer window expired; Operator must replan |
| `TRIP_CARGO_CAPACITY_EXCEEDED` | Target vehicle capacity exceeded; Operator must select another option |
| `TRIP_SERVICE_UNAVAILABLE` | Temporary upstream failure; allow retry using the same key |
| `RACE_LOST` / `RESOURCE_CONFLICT` | Concurrent change; refetch once and rebuild CTA from server state |
| `VALIDATION_ERROR` / `VALIDATION_FAILED` | Highlight returned fields; do not retry unchanged body |

For `PARCEL_CUSTODY_LOCATION_MISMATCH`, render `error.fields` instead of replacing it with the generic message “vehicle location unknown”.

## 20. Suggested FE state and API client

### Correct operational-location helper

```ts
type ReliabilityLocation = {
  type: string | null;
  id: string | null;
  name: string | null;
  orderIndex: number | null;
  eta: string | null;
};

function getCurrentRouteStop(manifest: AssistantTripManifest) {
  const operational = manifest.tripContext.currentOperationalLocation;

  if (
    !operational ||
    operational.status !== 'ARRIVED' ||
    operational.actualDepartureAt !== null ||
    !operational.location.id
  ) {
    return null;
  }

  return operational.location;
}
```

### Primary CTA resolver

```ts
function getPrimaryParcelAction(parcel: AssistantTripParcel) {
  const actions = new Set(parcel.availableActions ?? []);

  if (parcel.activeIncident) return 'VIEW_INCIDENT';
  if (parcel.status === 'RESERVED' && actions.has('CHECK_IN')) return 'CHECK_IN';
  if (parcel.status === 'CHECKED_IN' && actions.has('REWEIGH')) return 'REWEIGH';
  if (parcel.status === 'PENDING_FINAL_PAYMENT') return 'WAIT_FOR_PAYMENT';
  if (parcel.status === 'READY_TO_LOAD' && actions.has('LOAD')) return 'LOAD';
  if (parcel.status === 'IN_TRANSIT' && actions.has('UNLOAD')) return 'UNLOAD';
  if (parcel.status === 'UNLOADED' && actions.has('DELIVER')) return 'DELIVER';
  if (parcel.status === 'LOADED') return 'ON_VEHICLE';

  return 'NONE';
}
```

### Build unload request

```ts
function buildUnloadRequest(
  parcel: AssistantTripParcel,
  manifest: AssistantTripManifest,
  parcelCode: string,
  photoUrls: string[],
) {
  if (parcel.dropoffStopId) {
    const currentStop = getCurrentRouteStop(manifest);
    if (!currentStop?.id) throw new Error('NO_CURRENT_ROUTE_STOP');

    return {
      parcelCode,
      actualLocation: {
        kind: 'ROUTE_STOP',
        id: currentStop.id,
      },
      photoUrls,
    };
  }

  const destination = manifest.tripContext.trip.route?.destination;
  if (!destination?.id) throw new Error('NO_DESTINATION_STATION');

  return {
    parcelCode,
    actualLocation: {
      kind: 'DESTINATION_STATION',
      id: destination.id,
    },
    photoUrls,
  };
}
```

The FE may pre-check location for UX, but backend response remains authoritative. Never change Parcel status locally before API success.

## 21. Integration acceptance checklist

### Origin flow

- [ ] A `SCHEDULED` Trip with `currentOperationalLocation = null` opens without an error dialog.
- [ ] `RESERVED` card shows check-in.
- [ ] QR lookup does not mutate Parcel state.
- [ ] Check-in uses `tripId`, `parcelCode`, maximum 3 owned Firebase photos.
- [ ] Check-in response moves card to `CHECKED_IN` without an extra custody scan.
- [ ] `CHECKED_IN` card shows reweigh, not “scan location”.
- [ ] Reweigh uses a separate response DTO.
- [ ] `PENDING_FINAL_PAYMENT` disables load and shows real deadline/balance.
- [ ] `READY_TO_LOAD` allows load before Driver starts Trip.
- [ ] Load sends only `tripId` and `parcelCode`.

### Route operation

- [ ] Driver start uses no request body and an idempotency key.
- [ ] FE allows asynchronous delay before Parcel becomes `IN_TRANSIT`.
- [ ] Driver arrive is called before route-stop unload.
- [ ] Unload reads stop ID from `currentOperationalLocation.location.id`.
- [ ] Destination unload reads station ID from `trip.route.destination.id`.
- [ ] Wrong-stop error leaves Parcel unchanged and presents expected/actual locations.
- [ ] Deliver is available only from `UNLOADED`.
- [ ] Reconciliation displays full `unresolvedParcels[]`.
- [ ] FE never sends `scannedParcelIds` or `manualExceptionParcelIds`; backend derives them.
- [ ] Reconciliation request no longer sends `supervisorApprovalUserId`.
- [ ] When override is needed, Assistant stores `departureOverrideRequest.requestId`.
- [ ] Driver reads/decides stop departure approval using Driver JWT.
- [ ] Driver departure handles `PARCEL_STOP_RECONCILIATION_REQUIRED` and opens the returned request ID.
- [ ] After depart, a null operational location is treated as normal.

### Exception flow

- [ ] Assistant report contains no reviewer/supervisor UUID.
- [ ] HTTP `202` is handled as success.
- [ ] Pending report shows `WAIT_FOR_APPROVAL` and no search deadline.
- [ ] Assistant cannot approve their own report.
- [ ] Driver GET/decision uses Driver JWT and assigned Trip authorization.
- [ ] Approve creates real search state; reject resolves without custody event.
- [ ] Concurrent decision error reloads the request once.
- [ ] Driver app does not call Operator search/mark-found/forward endpoints.

### Forwarding and delivery

- [ ] Target crew confirms transfer using physical QR within 30 minutes.
- [ ] Successful transfer updates Parcel to `LOADED` on target Trip.
- [ ] Manual delivery confirmation requires a non-empty note up to 500 characters.
- [ ] Every retry uses the original idempotency key for that same action.

## 22. Known backend contract gaps

These are current implementation facts that FE must not hide with invented logic:

1. Driver has no separate custody-exception queue endpoint. Use the shared crew manifest as the
   queue: a pending row contains `custodyExceptionApproval` plus
   `APPROVE_CUSTODY_EXCEPTION|REJECT_CUSTODY_EXCEPTION` in `availableActions`; pass that row's
   `parcelId` to the existing Driver GET/decision endpoint.
2. Driver stop-departure approval requires a known `requestId`; there is no list/queue endpoint for pending departure approvals. The ID currently comes from reconciliation response or the `PARCEL_STOP_RECONCILIATION_REQUIRED` error fields.
3. `reweigh` does not return the common `AssistantParcelActionResponse`. Keep a dedicated DTO and refresh the manifest only when asynchronous payment state must be observed.
4. Trip start and Parcel `LOADED → IN_TRANSIT` are connected by an integration event, so a short eventual-consistency delay is valid.
5. Existing legacy custody rows can have a location snapshot with `id = null`. If `trackingConfidence = CONFIRMED_SCAN`, display the snapshot/time; do not force another scan solely to repair historical data.
6. `CUSTODY_SCAN` is intentionally absent before Trip operation and after normal automatic custody
   mutations. Its absence is not an error and must not block check-in/load.

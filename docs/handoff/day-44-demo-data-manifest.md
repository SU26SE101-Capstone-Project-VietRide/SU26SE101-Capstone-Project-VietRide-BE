# Day 44 deterministic demo-data manifest

This document is the durable, fail-closed contract for the Day 44 demo fixture. It describes
fixture state only; it does not change production policy, schemas, canonical system seeds, jobs,
events, or API contracts.

## Root contract

```yaml
schemaVersion: 1
namespace: day44-v1
timezone: Asia/Ho_Chi_Minh
uuidNamespace: 44000000-0000-5000-8000-000000000001
startDate: runtime-required
```

- `startDate` is an ICT calendar date and must be at least one day after the current ICT date.
  Invalid or missing input fails before any write; the seed never shifts the date silently.
- Day 44-owned UUID primary keys are the UUIDv5/SHA-1 result of
  `uuidNamespace + canonicalFixtureKey`. Runtime code may use built-in Node crypto only to verify
  a listed mapping. It must never generate or substitute a fixture ID.
- Catalog anchors are explicit SOT exceptions to the UUIDv5 rule: existing Starter plan
  `00000000-0000-0000-0000-000000000001`, existing VehicleTypes
  `00000000-0000-0000-0000-000000000101` through `...103`, existing PlatformWallet
  `00000000-0000-0000-0000-000000000001`, and Business Demo plan
  `44000000-0000-4000-8000-000000000001`. They are listed constants, not generated IDs.
- The bootstrap System Admin is an external prerequisite selected only by role from the existing
  `SYSTEM_ADMIN_BOOTSTRAP_*` lifecycle. Day 44 neither assigns nor changes its UUID.
- All timestamps below are formulas over `T0`, ICT midnight at `startDate`. Persisted
  `TIMESTAMPTZ` values use the UTC instant represented by the ICT formula. `D+n` means
  `startDate+n` calendar days in ICT; `M+1` means one ICT calendar month after `startDate`.
- Unless a row below overrides it: `createdAt=T0-2d`, `updatedAt=createdAt`, nullable lifecycle
  fields are null, `deletedAt=null`, activation flags are true, monetary values are integer VND,
  and arrays are stored in the listed order.

## Ownership and collision gate

The owned natural-key namespace is limited to emails ending in `@demo.vietride.local`, operator
names beginning `Day44 `, station slugs beginning `day44-`, route/vehicle names and plates listed
here, voucher codes beginning `D44`, RAG storage paths beginning `day44-v1/rag/`, and every exact
UUID or composite key in the registries below.

For every write, the seed first loads all rows matching either an owned ID or an owned natural key.
An existing row is adoptable only when both identify the same listed fixture and every expected
field matches. Any natural key owned by a different ID, any listed ID carrying a different natural
key, any cross-tenant reference, or any unlisted child fails the whole preflight before mutation.
The seed never updates, deletes, reparents, or adopts a foreign row. Reruns compare complete state;
money, inbox, Outbox, and ledger evidence are insert-once by their listed identity.

## Relative timestamp formulas

| Name | Exact formula |
|---|---|
| `T0` | ICT midnight on `startDate` |
| `subscriptionEndStarter` | ICT midnight on `D+30` |
| `subscriptionEndBusiness` | ICT midnight on `M+1` |
| `topUpAt` | `D-1 09:00 ICT` |
| `voucherValidFrom` | `D-7 00:00 ICT` |
| `voucherValidUntil` | `D+60 23:59:59 ICT` |
| `approvedAt` | `T0-2d+10h` |
| `ingestedAt` | `approvedAt+5m` |
| `paidAt` | `T0-1d+10h` |
| `invoiceIssuedAt` | `paidAt` |
| `invoicePdfCompletedAt` | `paidAt+1m` |
| `tripDeparture(op,r,d)` | `D+d` at R1 `08:00`, R2 `14:00`, or R3 `10:00` ICT |
| `tripEta(op,r,d)` | departure plus route duration: R1/R2 `240m`, R3 `150m` |

## Identity fixture state

Plan aliases exist only in this manifest and are not domain enums.

| Alias | ID | Name | Monthly/yearly VND | Limits vehicles/drivers/assistants/operator users/routes/trips per month | parcel/shuttle/RAG | State |
|---|---|---|---|---|---|---|
| `STARTER_TRIAL` | `00000000-0000-0000-0000-000000000001` | Starter (Free Trial) | `0/0` | `3/5/5/3/5/100` | `false/false/true` | active, unchanged canonical seed |
| `BUSINESS` | `44000000-0000-4000-8000-000000000001` | Business (Demo) | `2,000,000/20,000,000` | `20/40/40/20/30/2,000` | `true/true/true` | active; commercial pricing is demo-only and non-canonical |

The persisted Business Demo description is exactly
`Day 44 demo-only Business plan; commercial pricing is non-canonical and must not be used as production policy.`

Operators A and B use Business Demo; C uses Starter. All three are active and `APPROVED`.
Fixture business-registration/tax/phone values are test-only natural keys:
`D44-BRN-{A|B|C}`, `D44-TAX-{A|B|C}`, and `+8490444000{1|2|3}`. Contact email equals the
Operator Admin email. Address is `44 Demo Street`, ward `Demo Ward`, district `Demo District`,
province `Hồ Chí Minh`; representative name is `Day44 Operator {A|B|C} Admin` and representative
phone equals the operator phone. Policy/bank fields are null.

Every non-bootstrap account is login-ready with `status=ACTIVE`, no OAuth identity, and no
persisted refresh/verification/device row. Its credential is supplied only at runtime through
`DEMO_SEED_ACCOUNT_PASSWORD` and hashed through the existing BCrypt cost-12 Identity lifecycle;
the manifest contains no credential value or hash. Operator Admin/Driver/Assistant users carry
their listed Operator ID; Passengers carry `operatorId=null`.

Account profile natural keys are exact: display names are `Day44 Operator {A|B|C} Admin`,
`Day44 Driver {A|B|C}{1|2|3}`, `Day44 Assistant {A|B|C}`, and `Day44 Passenger {01..10}`.
Phones are `+849044401{01|02|03}` for Operator Admins, `+84904441{A-index}{driver-index}` for
Drivers (A/B/C indexes `1/2/3`), `+849044402{01|02|03}` for Assistants, and
`+849044403{01..10}` for Passengers. All email/phone pairs are unique; avatar, date-of-birth,
gender, and OAuth fields are null.

Subscriptions are one-per-Operator. C is `ACTIVE`, starts `T0`, expires
`subscriptionEndStarter`, and has null billing period/payment method. A/B are
`ACTIVE/MONTHLY/VNPAY`, start `T0`, expire `subscriptionEndBusiness`, and have
`currentTripsThisMonth=count(departures among that Operator's 42 generated Trips whose ICT
year/month equals startDate's ICT year/month)`. Other usage counters equal the manifest row
counts: A/B/C each have vehicles `3`, drivers `3`, assistants `1`, operator users `1`, routes `3`.

## Trip topology and state

### Stations

| Key | Name | City | Ward | Latitude | Longitude | State |
|---|---|---|---|---:|---:|---|
| `station:mien-tay` | Bến xe Miền Tây | Hồ Chí Minh | An Lạc | `10.741037` | `106.618980` | slug `day44-ben-xe-mien-tay`, active |
| `station:mien-dong-moi` | Bến xe Miền Đông mới | Hồ Chí Minh | Long Bình | `10.879550` | `106.816190` | slug `day44-ben-xe-mien-dong-moi`, active |
| `station:can-tho` | Bến xe Trung tâm TP Cần Thơ | Cần Thơ | Cái Răng | `10.005200` | `105.772310` | slug `day44-ben-xe-trung-tam-can-tho`, active |
| `station:long-chau` | Bến xe khách Phường Long Châu | Vĩnh Long | Long Châu | `10.238230` | `105.957730` | slug `day44-ben-xe-khach-phuong-long-chau`, active |
| `station:ben-tre` | Bến xe Bến Tre | Vĩnh Long | Sơn Đông | `10.267025` | `106.359834` | slug `day44-ben-xe-ben-tre`, active |

Station address/contact/operating-hours/facilities are null, `supportsShuttle=false`, and no
geocoding occurs. Every Operator has one active `OperatorStation` link to all five Stations;
optional override/counter/contact/instruction fields are null.

### Routes, stops, alternatives, vehicles, and schedules

For each Operator letter `o` in A/B/C:

- R1 is `D44 o R1 Miền Tây - Cần Thơ`, Miền Tây to Cần Thơ, returnRoute=R2,
  `08:00`, `240m`, `170.00km`, `180000` VND, no intermediate Stop.
- R2 is `D44 o R2 Cần Thơ - Miền Tây`, Cần Thơ to Miền Tây, returnRoute=R1,
  `14:00`, `240m`, `170.00km`, `180000` VND, no intermediate Stop.
- R3 is `D44 o R3 Miền Tây - Bến Tre`, Miền Tây to Bến Tre, no return route,
  `10:00`, `150m`, `90.00km`, `120000` VND. Its operator-owned Stops copy Stations 2/3/4
  exactly (name, six-decimal coordinates, and composed city/ward address), with
  `googlePlaceId=null`, `sharedSuggestion=false`, `replacedByStopId=null`.
- R3 RouteStops are ordered Station-copy `[2,3,4]`; their exact
  `(estimatedDurationFromOriginMinutes,distanceFromOriginKm)` values are
  `[(35,30.00),(75,65.00),(115,80.00)]`; all allow pickup and dropoff.
- R3 path polyline is `ozp\`As_wiSu\`Zqoe@twiDf{jEmol@{ec@_sDcpmA`. Its single active
  AlternativeRoute is named `D44 o R3 Alternative`, has the same Bến Tre destination,
  duration/distance `150m/90.00km`, order `[4,2,3]`, timing/distance
  `[(35,30.00),(75,65.00),(115,80.00)]`, and polyline
  `ozp\`As_wiSpeaBxc\`Cgg|BktfDtwiDf{jEmcr@_wqB`.
- Vehicles R1/R2/R3 respectively use canonical VehicleTypes STANDARD_BUS/LIMOUSINE/SLEEPER_BUS,
  plates from the table below, status `ACTIVE`, and null images. Cargo snapshots are
  `1000.00kg/10.0000m3`.
- Schedules R1/R2/R3 use Driver 1/2/3 respectively and the matching vehicle. Only R1 uses that
  Operator's Assistant. Every schedule has `dayOfWeek=[1,2,3,4,5,6,7]`, `validFrom=startDate`,
  `validUntil=D+29`, active.

| Operator | R1 | R2 | R3 |
|---|---|---|---|
| A | `51B-440.01` | `51B-440.02` | `51B-440.03` |
| B | `51B-441.01` | `51B-441.02` | `51B-441.03` |
| C | `51B-442.01` | `51B-442.02` | `51B-442.03` |

Exact seat layouts are deterministic: STANDARD_BUS has `S01..S45` type `STANDARD`;
LIMOUSINE has `V01..V09` type `VIP`; SLEEPER_BUS has lower deck `L01..L20` type
`SLEEPER_LOWER` and upper deck `U01..U20` type `SLEEPER_UPPER`. Layout JSON is version 1 with
the exact ordered seat list, `vehicleTypeCode`, `totalSeats`, decks `1/1/2`, columns `5/3/4`,
rows `9/3/5`, and aisle columns `[3]/[2]/[2]` respectively. Trip snapshots equal their
Vehicle layout byte-for-byte.

Each schedule materializes only offsets `d=00..13`, producing exactly 126 `SCHEDULED`,
`AUTO_FROM_SCHEDULE` Trips. `alternativeRouteId=null`, `actual*`, cancellation, completion,
disruption and notes fields are null; `hasSubstitution=false`; reserved/loaded cargo counters are
zero. Base fare, vehicle cargo, crew, schedule, and seat layout are immutable snapshots. Estimated
passenger luggage is `450.00`, `135.00`, or `800.00kg` for R1/R2/R3. All 3,948 TripSeats are
`AVAILABLE`, with null disabled reason. Only R3 Trips have TripStops: three per Trip, producing
exactly 126; they snapshot the RouteStop order/allow/distance, status `PENDING`, null actual times,
and ETA `tripDeparture + 35/75/115m`. There are no TripStopFare rows.

## Commerce fixture state

Each Passenger has a Wallet natural-keyed by its User ID with `balance=2000000` and initial
`rowVersion=0`; one `SUCCEEDED` TopUpRequest at `topUpAt` for `2000000`; and one immutable
WalletTransaction `CREDIT/TOP_UP`, amount `2000000`, balance before/after `0/2000000`,
`referenceId=TopUpRequest.id`. No `MANUAL_ADJUSTMENT` row is owned.

The two Business sagas use `referenceType=SUBSCRIPTION`, `method=VNPAY`, `status=SUCCEEDED`,
amount `2000000`, `paidAt`, and the corresponding subscription as reference. Each has one
`SUCCEEDED` Identity upgrade attempt, processed Identity inbox evidence, processed Payment event
evidence, one `PUBLISHED` Payment Outbox row using the existing
`payment.subscription.payment_succeeded` key and the same event ID/MessageId, one `ISSUED`
Invoice whose PDF status is `COMPLETED` at `invoicePdfCompletedAt`, and one PlatformWallet
`CREDIT/SUBSCRIPTION_PAYMENT` transaction for `2000000`. Invoice object path is
`invoices/{operatorId}/{invoiceId}.pdf`; protected download path is
`/v1/operator/invoices/{invoiceId}/download`. No operator wallet debit is created for these demo
VNPay upgrades.

Saga natural keys are `D44-SUB-A`/`D44-SUB-B` for VNPay transaction references and
`day44-v1:subscription:{a|b}` for idempotency keys. Invoice numbers are
`VR-INV-{startDate:yyyyMM}-440001` for A and `...-440002` for B; period is
`T0..subscriptionEndBusiness`, subtotal/total are `2000000`, tax and discount are zero, and PDF
attempt count is one with no retry/error. Platform credits apply in A-then-B order with balance
before/after `0/2000000` then `2000000/4000000`, reference the corresponding Payment, use
`actorType=SYSTEM`, and have null actor/note fields.

All Vouchers are active, not soft-deleted, `validFrom=voucherValidFrom`,
`validUntil=voucherValidUntil`, `totalUsageLimit=10000`, `perUserLimit=100`,
`newUserOnly=false`, and have zero usage. Service/payment method arrays preserve the listed order.
Their exact names are `Day44 Ride 10`, `Day44 Booking 50K`, `Day44 Partner 15`,
`Day44 Operator A 30K`, and `Day44 Operator B Parcel 20`, in code order below. Platform rows are
created by the existing bootstrap System Admin; operator-owned rows are created by the matching
Operator Admin.

| Code | Owner/funding | Type/value | Min/max VND | Services | Operator/route scope | Methods | Consent |
|---|---|---|---|---|---|---|---|
| `D44RIDE10` | platform/VIETRIDE_FUNDED | PERCENT_OFF/10 | `100000/50000` | BOOKING, PARCEL | all/all | WALLET, VNPAY | none |
| `D44BOOK50` | platform/VIETRIDE_FUNDED | FIXED_AMOUNT/50000 | `200000/null` | BOOKING | A/A-R1 | WALLET | none |
| `D44PARTNER15` | platform/OPERATOR_FUNDED | PERCENT_OFF/15 | `100000/75000` | BOOKING, PARCEL | A+B/A-R1+B-R1 | WALLET, VNPAY | exactly A+B ACCEPTED |
| `D44OPA30` | A/OPERATOR_FUNDED | FIXED_AMOUNT/30000 | `150000/null` | BOOKING | server-forced A/A-R1 | WALLET | none (self-owned) |
| `D44OPBPARCEL20` | B/OPERATOR_FUNDED | PERCENT_OFF/20 | `100000/100000` | PARCEL | server-forced B/B-R1 | WALLET, VNPAY | none (self-owned) |

The two D44PARTNER15 consents are `ACCEPTED`, requested at `T0-2d`, responded at
`T0-2d+1h`, and responded by the corresponding Operator Admin. Reject reason is null. There are
exactly two effective ParcelRouteFare rows, composite-keyed by A-R1/B-R1 plus `SMALL`. Each has
`priceVnd=50000`, `pricePerChargeableKgVnd=0`, `minimumPriceVnd=0`, `effectiveFrom=T0`, and
`effectiveUntil=null`. `ParcelRouteFare` has no activation column. Starter Operator C owns no
Parcel fare.

## RAG fixture state

Default seed and E2E are offline: they never call Cloudinary or OpenRouter. The committed fixture
contains one attested vector for each canonical document, generated only by Task 44.6's explicit
command using runtime `OPENROUTER_API_KEY`. Provenance records schema/generator version 1,
provider `openrouter`, endpoint `https://openrouter.ai/api/v1/embeddings`, model
`nvidia/llama-nemotron-embed-vl-1b-v2:free`, dimension 2048, three content SHA256 values, and
the final fixture SHA256. Offline verification rejects any drift.

All documents have `storageProvider=CLOUDINARY` as required by the existing schema but no upload
occurs, `mimeType=text/plain`, `fileType=TXT`, `language=vi`, `status=APPROVED`,
`ingestStatus=COMPLETED`, `ingestError=null`, `chunkCount=1`,
`embeddingModel=nvidia/llama-nemotron-embed-vl-1b-v2:free`, and
`embeddingDimensions=2048`. They are uploaded/approved by the existing bootstrap System Admin,
with `approvedAt`/`ingestedAt`, `archivedAt=null`, and `description=null`. `fileSize` is the exact
positive UTF-8 byte length of the corresponding canonical LF source file after Task 44.6 freezes
that file; the seed recomputes and compares it before writes rather than recording an OS file
metadata value.

Each document owns exactly chunk index 0. Chunk `content` is the complete corresponding canonical
LF source-file text with no trimming or line-ending conversion. `tokenCount` is the positive
ingest-time whitespace word count, exactly `content.trim().split(/\s+/u).length`.
`searchVector` is exactly PostgreSQL `to_tsvector('simple', content)`. The chunk snapshots
`documentTitle`, `documentType`, and `operatorId` equal the parent values; `sectionHeader=null`.
The attested `embedding` is the document's exact 2,048-value vector stored as `halfvec(2048)`.

| Key | Title/storage path | Exact `fileName` | Access/category/type | Operator | Audience roles |
|---|---|---|---|---|---|
| `rag:document:public-passenger-guide` | Day44 Public Passenger Guide / `day44-v1/rag/public-passenger-guide.txt` | `vietride-public-demo-knowledge-base.txt` | PUBLIC/CUSTOMER_SUPPORT/GUIDE | null | PASSENGER, DRIVER, ASSISTANT, OPERATOR_STAFF, OPERATOR_ADMIN, SYSTEM_ADMIN |
| `rag:document:operator-a-policy` | Day44 Operator A Policy / `day44-v1/rag/operator-a-policy.txt` | `vietride-operator-demo-knowledge-base.txt` | OPERATOR/OPERATOR_POLICY/POLICY | A | DRIVER, ASSISTANT, OPERATOR_STAFF, OPERATOR_ADMIN |
| `rag:document:system-admin-runbook` | Day44 System Admin Runbook / `day44-v1/rag/system-admin-runbook.txt` | `vietride-admin-demo-knowledge-base.txt` | ADMIN/PLATFORM_ADMIN/SOP | null | SYSTEM_ADMIN |

## Fixed UUID registry

Every UUID-bearing Day 44 row is listed below. Composite-key children are frozen in the following
section.

| Row | Canonical fixture key | Fixed UUID | Exact discriminator/state |
|---|---|---|---|
| Operator | `identity:operator:a` | `6276b48c-3984-582b-9c35-0c2fbe20baa7` | Day44 Business Operator A; APPROVED |
| Operator | `identity:operator:b` | `d63b3c32-8c12-5130-a347-0ef8df286605` | Day44 Business Operator B; APPROVED |
| Operator | `identity:operator:c` | `8554beea-8b1b-57c5-bb87-8d1f136654a3` | Day44 Starter Operator C; APPROVED |
| User | `identity:user:operator-admin:a` | `9c90f052-9323-5c47-9402-ad100db3dec9` | `operator.a@demo.vietride.local`; OPERATOR_ADMIN/ACTIVE |
| User | `identity:user:operator-admin:b` | `65cfe24b-a43e-5dad-b43d-c6bf1b3cd914` | `operator.b@demo.vietride.local`; OPERATOR_ADMIN/ACTIVE |
| User | `identity:user:operator-admin:c` | `e21cf2e5-c8fc-5155-a8bb-345a4e6f3f8b` | `operator.c@demo.vietride.local`; OPERATOR_ADMIN/ACTIVE |
| User | `identity:user:driver:a:1` | `6a61b1d5-4c98-5f40-8e0f-494651deebfa` | `driver.a1@demo.vietride.local`; DRIVER/ACTIVE |
| User | `identity:user:driver:a:2` | `1432b243-ab2b-5a33-8db5-5441efd4d489` | `driver.a2@demo.vietride.local`; DRIVER/ACTIVE |
| User | `identity:user:driver:a:3` | `67086aa7-71f3-5f60-9d13-f7f30bb8c7c8` | `driver.a3@demo.vietride.local`; DRIVER/ACTIVE |
| User | `identity:user:driver:b:1` | `ea9c2b90-c811-5281-9793-4722253b5b17` | `driver.b1@demo.vietride.local`; DRIVER/ACTIVE |
| User | `identity:user:driver:b:2` | `aeebce20-d2d9-525c-9394-8c43c6cf8800` | `driver.b2@demo.vietride.local`; DRIVER/ACTIVE |
| User | `identity:user:driver:b:3` | `f55eadcb-f314-5e35-898a-6d5ddad291aa` | `driver.b3@demo.vietride.local`; DRIVER/ACTIVE |
| User | `identity:user:driver:c:1` | `6e236fff-7856-51c4-917c-89c6724b7d60` | `driver.c1@demo.vietride.local`; DRIVER/ACTIVE |
| User | `identity:user:driver:c:2` | `a052ed42-ef29-5180-b92e-317b01b92b65` | `driver.c2@demo.vietride.local`; DRIVER/ACTIVE |
| User | `identity:user:driver:c:3` | `04ebbfdc-c20c-5f1c-b145-030eb9e247d4` | `driver.c3@demo.vietride.local`; DRIVER/ACTIVE |
| User | `identity:user:assistant:a` | `316ba0dc-6bea-5173-858d-4c9c3cde50de` | `assistant.a@demo.vietride.local`; ASSISTANT/ACTIVE |
| User | `identity:user:assistant:b` | `2b7ae533-41e1-5fb6-9875-76e8923c4916` | `assistant.b@demo.vietride.local`; ASSISTANT/ACTIVE |
| User | `identity:user:assistant:c` | `f0931d74-4698-59a6-8eb6-de775b44e6fe` | `assistant.c@demo.vietride.local`; ASSISTANT/ACTIVE |
| User | `identity:user:passenger:01` | `167b6f1c-e47d-56cd-9715-1d9b75637cd3` | `passenger01@demo.vietride.local`; PASSENGER/ACTIVE |
| User | `identity:user:passenger:02` | `c251549f-b0d5-5d73-9e36-50ff74bf69f2` | `passenger02@demo.vietride.local`; PASSENGER/ACTIVE |
| User | `identity:user:passenger:03` | `6288dc1d-ac87-50b6-8b85-f45e7852ea50` | `passenger03@demo.vietride.local`; PASSENGER/ACTIVE |
| User | `identity:user:passenger:04` | `b5ec73ed-ae93-5fb7-b0fe-c61ada94d4ba` | `passenger04@demo.vietride.local`; PASSENGER/ACTIVE |
| User | `identity:user:passenger:05` | `fc58a993-6184-5cf1-971d-c38118fbbee7` | `passenger05@demo.vietride.local`; PASSENGER/ACTIVE |
| User | `identity:user:passenger:06` | `b41d9085-e396-5014-ab7a-67e6b2d6fd88` | `passenger06@demo.vietride.local`; PASSENGER/ACTIVE |
| User | `identity:user:passenger:07` | `4ca78bdc-23ba-5a01-b40a-49e2d84f69c5` | `passenger07@demo.vietride.local`; PASSENGER/ACTIVE |
| User | `identity:user:passenger:08` | `1fcc1bb2-20fb-5c8f-bea4-41f319ed885f` | `passenger08@demo.vietride.local`; PASSENGER/ACTIVE |
| User | `identity:user:passenger:09` | `99aa3004-333a-5105-8fd4-09d8f366de92` | `passenger09@demo.vietride.local`; PASSENGER/ACTIVE |
| User | `identity:user:passenger:10` | `820ece02-0f0c-5bb4-90d4-0d5bbf0962ec` | `passenger10@demo.vietride.local`; PASSENGER/ACTIVE |
| OperatorSubscription | `identity:subscription:a` | `9b7f508d-7215-5228-af11-f3d29ff5e14b` | BUSINESS; ACTIVE |
| OperatorSubscription | `identity:subscription:b` | `fe24eec8-2cbd-523b-8710-5e4276541ab0` | BUSINESS; ACTIVE |
| OperatorSubscription | `identity:subscription:c` | `5d5879bb-7e22-5bc2-97e4-bbf923dd4739` | STARTER_TRIAL; ACTIVE |
| SubscriptionUpgradeAttempt | `identity:subscription-upgrade-attempt:a` | `74b73558-f03e-5a68-aaf3-edf1563f61de` | SUCCEEDED |
| Identity inbox | `identity:inbox:subscription-payment:a` | `6f1a2f10-d9ca-5d89-8d55-7194dae1364d` | processed once |
| Payment | `payment:subscription:a` | `9c10727f-749d-56c2-bbd9-e981b996d699` | SUBSCRIPTION/VNPAY/SUCCEEDED/2000000 |
| Payment processed event | `payment:processed-event:subscription:a` | `496209ea-4358-5d81-a91e-33704ed81c77` | processed once |
| Payment Outbox/event | `payment:event:subscription-payment-succeeded:a` | `3ddf16ca-8deb-5719-83b7-b3683392b782` | PUBLISHED; payment.subscription.payment_succeeded |
| Invoice | `payment:invoice:subscription:a` | `5f61025c-d8e3-5a2e-865d-a992ed3d27d7` | ISSUED; PDF COMPLETED |
| PlatformWalletTransaction | `payment:platform-wallet-transaction:subscription:a` | `f43385d5-7142-5f8b-be72-a1b67ec0004f` | CREDIT/SUBSCRIPTION_PAYMENT/2000000 |
| SubscriptionUpgradeAttempt | `identity:subscription-upgrade-attempt:b` | `a9755051-3e91-5618-be34-b5a9b63180e3` | SUCCEEDED |
| Identity inbox | `identity:inbox:subscription-payment:b` | `ce48381f-919e-5222-a900-b645b00578be` | processed once |
| Payment | `payment:subscription:b` | `bac61192-d30c-5029-acf2-167bae06a9f0` | SUBSCRIPTION/VNPAY/SUCCEEDED/2000000 |
| Payment processed event | `payment:processed-event:subscription:b` | `6fcceb19-f24c-5e0e-8bc3-59351df2da68` | processed once |
| Payment Outbox/event | `payment:event:subscription-payment-succeeded:b` | `a213a3e7-d834-5897-a404-9b2c883afd00` | PUBLISHED; payment.subscription.payment_succeeded |
| Invoice | `payment:invoice:subscription:b` | `01c5dcff-bbbe-558a-aaea-52b75b723a2a` | ISSUED; PDF COMPLETED |
| PlatformWalletTransaction | `payment:platform-wallet-transaction:subscription:b` | `372d57c2-56c8-5de6-b6e2-b18f5ff28edd` | CREDIT/SUBSCRIPTION_PAYMENT/2000000 |
| TopUpRequest | `payment:top-up:01` | `4d9b721c-6912-557e-9a3d-61facdeb1374` | SUCCEEDED/2000000 |
| WalletTransaction | `payment:wallet-transaction:top-up:01` | `2a92330c-c88e-538b-9d44-45375a2b9d18` | CREDIT/TOP_UP/2000000 |
| TopUpRequest | `payment:top-up:02` | `a52f2599-315f-57b5-b8ef-7c5b3c658611` | SUCCEEDED/2000000 |
| WalletTransaction | `payment:wallet-transaction:top-up:02` | `b03eef06-6237-555f-95e6-d1e6ecd932ad` | CREDIT/TOP_UP/2000000 |
| TopUpRequest | `payment:top-up:03` | `509e1500-83a0-506d-8bf6-b573013dbfd2` | SUCCEEDED/2000000 |
| WalletTransaction | `payment:wallet-transaction:top-up:03` | `17c64fbc-b0cf-5c58-94a2-421338755ccf` | CREDIT/TOP_UP/2000000 |
| TopUpRequest | `payment:top-up:04` | `fdd195b7-89ec-5f0d-b69b-4be96a106be9` | SUCCEEDED/2000000 |
| WalletTransaction | `payment:wallet-transaction:top-up:04` | `de6ae2dd-77c3-5da7-ad61-99c48c2d51e2` | CREDIT/TOP_UP/2000000 |
| TopUpRequest | `payment:top-up:05` | `81082ea4-f4cb-5349-867f-5c25eb53aeb5` | SUCCEEDED/2000000 |
| WalletTransaction | `payment:wallet-transaction:top-up:05` | `ed0da330-d327-5a71-b73f-4a07dc993d17` | CREDIT/TOP_UP/2000000 |
| TopUpRequest | `payment:top-up:06` | `f5cbb7a0-3268-534c-8fc3-53aa73c821e9` | SUCCEEDED/2000000 |
| WalletTransaction | `payment:wallet-transaction:top-up:06` | `305ec29f-1c38-5d9b-8171-4d3f2034f7ca` | CREDIT/TOP_UP/2000000 |
| TopUpRequest | `payment:top-up:07` | `08c9e6a2-7530-50f0-b257-11b8d93629e9` | SUCCEEDED/2000000 |
| WalletTransaction | `payment:wallet-transaction:top-up:07` | `59c598ed-fe4f-53fe-82b5-50c650e2fbc1` | CREDIT/TOP_UP/2000000 |
| TopUpRequest | `payment:top-up:08` | `5c588db3-35a4-5d13-85f5-2c4870c4e1fc` | SUCCEEDED/2000000 |
| WalletTransaction | `payment:wallet-transaction:top-up:08` | `412eb268-5b6f-54eb-b2d6-746c61b4bb76` | CREDIT/TOP_UP/2000000 |
| TopUpRequest | `payment:top-up:09` | `027ad379-42bc-5808-b4d6-d2c8add12624` | SUCCEEDED/2000000 |
| WalletTransaction | `payment:wallet-transaction:top-up:09` | `360f61dc-98e2-5b12-8dbf-f05fa865d133` | CREDIT/TOP_UP/2000000 |
| TopUpRequest | `payment:top-up:10` | `86717bbe-bcd0-5ac9-9c73-47dc1bfc94cb` | SUCCEEDED/2000000 |
| WalletTransaction | `payment:wallet-transaction:top-up:10` | `bd78d2ad-64dd-529e-b9e3-b5c44e6efd0d` | CREDIT/TOP_UP/2000000 |
| Voucher | `booking:voucher:d44ride10` | `8d0fa121-27f3-5239-aa2c-894541991249` | `D44RIDE10`; active |
| Voucher | `booking:voucher:d44book50` | `556d31a1-21ba-534a-8440-b2db3dc77179` | `D44BOOK50`; active |
| Voucher | `booking:voucher:d44partner15` | `84e96b26-d4b1-55d0-8f5a-46750b58ce89` | `D44PARTNER15`; active |
| Voucher | `booking:voucher:d44opa30` | `10671adf-d61c-563e-a49d-669077c57f99` | `D44OPA30`; active |
| Voucher | `booking:voucher:d44opbparcel20` | `e96a29bf-f8e9-593d-8d8c-89408533ffe6` | `D44OPBPARCEL20`; active |
| OperatorVoucherConsent | `booking:voucher-consent:d44partner15:a` | `9696626f-c0de-590b-be11-a36160137e17` | ACCEPTED |
| OperatorVoucherConsent | `booking:voucher-consent:d44partner15:b` | `2e3c1e47-9318-59a5-ac29-63847e5a9551` | ACCEPTED |
| Station | `trip:station:mien-tay` | `a05da7cf-042d-5471-864b-b7eff4c25fe3` | active |
| Station | `trip:station:mien-dong-moi` | `59cd9fcc-b45a-55f0-9297-da73ebcb81c9` | active |
| Station | `trip:station:can-tho` | `13a5c957-daf7-5efd-97e1-4adf94985dbc` | active |
| Station | `trip:station:long-chau` | `d828ca67-fff1-5d2a-9f32-5f4cd4686229` | active |
| Station | `trip:station:ben-tre` | `4b80a62b-752a-5518-afb5-e2807e47a011` | active |
| OperatorStation | `trip:operator-station:a:mien-tay` | `f0d0e979-8f8b-5d8c-a91a-24c2f89ea2c0` | active |
| OperatorStation | `trip:operator-station:a:mien-dong-moi` | `0e427d28-a574-53e5-912a-08fe49399748` | active |
| OperatorStation | `trip:operator-station:a:can-tho` | `f83de8b3-934f-5eb3-b234-0e29e1a035ba` | active |
| OperatorStation | `trip:operator-station:a:long-chau` | `aedd3632-1664-5078-bddd-adb8ecf69055` | active |
| OperatorStation | `trip:operator-station:a:ben-tre` | `09227b66-0d28-5712-a093-bc3faa3419cd` | active |
| OperatorStation | `trip:operator-station:b:mien-tay` | `baeefe04-172f-5262-93c1-a111ddb115b5` | active |
| OperatorStation | `trip:operator-station:b:mien-dong-moi` | `06c6894b-03b3-5e8b-b0e6-5003aec576ad` | active |
| OperatorStation | `trip:operator-station:b:can-tho` | `7894b0f2-3967-58d5-94c4-72682fa57f31` | active |
| OperatorStation | `trip:operator-station:b:long-chau` | `7d88f92d-56a8-5735-89f1-33a81ab343bd` | active |
| OperatorStation | `trip:operator-station:b:ben-tre` | `9d7bf611-0f39-5364-9932-4f4682f6389d` | active |
| OperatorStation | `trip:operator-station:c:mien-tay` | `eb895a91-0ec0-506a-8854-148db625ca6d` | active |
| OperatorStation | `trip:operator-station:c:mien-dong-moi` | `b0396ac5-2eb5-55f1-a84e-796b103d1d38` | active |
| OperatorStation | `trip:operator-station:c:can-tho` | `c661fbc8-68aa-5893-a8bd-0de2dedf49a9` | active |
| OperatorStation | `trip:operator-station:c:long-chau` | `6d58ff6f-d6a4-57da-93f4-8d434f693ef0` | active |
| OperatorStation | `trip:operator-station:c:ben-tre` | `636cb77d-f020-569a-a86a-ceeec1a71c7e` | active |
| Stop | `trip:stop:a:station-copy:2` | `1ace61d6-f914-5d11-a242-d69bbb4c13c4` | active; operator-owned |
| Stop | `trip:stop:a:station-copy:3` | `07182f5b-714b-504a-9a60-94d2b165fd79` | active; operator-owned |
| Stop | `trip:stop:a:station-copy:4` | `0231e70c-dcfe-5951-aa8d-60ad8900b313` | active; operator-owned |
| Stop | `trip:stop:b:station-copy:2` | `45bac395-9783-5e50-a278-3912535daded` | active; operator-owned |
| Stop | `trip:stop:b:station-copy:3` | `f1fc929c-1989-5553-8d55-a01f59f98933` | active; operator-owned |
| Stop | `trip:stop:b:station-copy:4` | `cb6f1e02-2a87-5618-ad75-a60363885984` | active; operator-owned |
| Stop | `trip:stop:c:station-copy:2` | `2ffffab1-9398-5d75-a957-0c328668e6f3` | active; operator-owned |
| Stop | `trip:stop:c:station-copy:3` | `8ca82c0e-c89d-5f55-9ec3-d4fc90a3d8a3` | active; operator-owned |
| Stop | `trip:stop:c:station-copy:4` | `8b5cfaf2-ef55-5af5-834f-274c9595f2ca` | active; operator-owned |
| Route | `trip:route:a:r1` | `c908c072-337a-526e-bf89-27254cae8e8f` | active |
| Route | `trip:route:a:r2` | `34682c9a-90e4-5541-9c7f-b870c142cd4d` | active |
| Route | `trip:route:a:r3` | `059ccdba-c397-5213-81d7-8baaaf1fef9d` | active |
| Route | `trip:route:b:r1` | `67db3832-0894-5afc-94ab-ea73b3dd8671` | active |
| Route | `trip:route:b:r2` | `ec978401-d529-5a9d-b018-892ea5170c20` | active |
| Route | `trip:route:b:r3` | `b99d9a47-0cdf-5c2c-a9a0-89933a22c623` | active |
| Route | `trip:route:c:r1` | `f4e6e507-6aaf-531e-97a0-c32f79e695e6` | active |
| Route | `trip:route:c:r2` | `453e3e57-2f56-5849-bfdc-81e5cdeadf0f` | active |
| Route | `trip:route:c:r3` | `08a8f325-cce9-5f73-ae64-84329e84526d` | active |
| AlternativeRoute | `trip:alternative-route:a:r3:1` | `9d72b698-30be-5a14-bd5f-fcfc2b21b36f` | active |
| AlternativeRoute | `trip:alternative-route:b:r3:1` | `031f1a57-67f0-5b3a-b9c6-294b207b9555` | active |
| AlternativeRoute | `trip:alternative-route:c:r3:1` | `eccde21c-b120-51e3-9a1c-bc66be9952dd` | active |
| Vehicle | `trip:vehicle:a:r1` | `8c3b3486-f500-5b85-8945-251bc77ef726` | ACTIVE |
| Vehicle | `trip:vehicle:a:r2` | `7dd53b0a-e5d3-5b64-b55d-8ca77cd3961c` | ACTIVE |
| Vehicle | `trip:vehicle:a:r3` | `a228fee8-30e0-536e-a226-5ad221fc1a37` | ACTIVE |
| Vehicle | `trip:vehicle:b:r1` | `b6cc926d-1f3a-5277-9fa7-abc9aa633f28` | ACTIVE |
| Vehicle | `trip:vehicle:b:r2` | `5fddd260-8b3e-5471-ab92-8da8d36377a8` | ACTIVE |
| Vehicle | `trip:vehicle:b:r3` | `3cf0f63e-660b-5636-a424-dd9b7e8e68e2` | ACTIVE |
| Vehicle | `trip:vehicle:c:r1` | `2594ad0e-636a-53db-9a38-be66d979f7da` | ACTIVE |
| Vehicle | `trip:vehicle:c:r2` | `85d2c267-397d-57fc-b7a9-bd683adddc10` | ACTIVE |
| Vehicle | `trip:vehicle:c:r3` | `85b8ded7-dfde-5599-ab5d-5fa0acbd3006` | ACTIVE |
| DriverSchedule | `trip:driver-schedule:a:r1` | `90dfa003-c9c6-5c22-a568-c4c9581c3f24` | active; all days |
| DriverSchedule | `trip:driver-schedule:a:r2` | `975622be-5b57-53a2-aeb3-e035219f3145` | active; all days |
| DriverSchedule | `trip:driver-schedule:a:r3` | `9d1b4f6e-708b-5dda-81ef-338a5b3522ca` | active; all days |
| DriverSchedule | `trip:driver-schedule:b:r1` | `4790e503-601b-5345-b9a6-347a4d00396d` | active; all days |
| DriverSchedule | `trip:driver-schedule:b:r2` | `01f1442b-b507-58b4-a7f1-54eb98a52f57` | active; all days |
| DriverSchedule | `trip:driver-schedule:b:r3` | `8b953f4b-6597-5fe9-96ed-023d6ba75259` | active; all days |
| DriverSchedule | `trip:driver-schedule:c:r1` | `0e78b10d-fa6e-51dc-a408-13d1ca2b192e` | active; all days |
| DriverSchedule | `trip:driver-schedule:c:r2` | `e763b0ad-4da9-567d-b7c7-a6898b8fa9f3` | active; all days |
| DriverSchedule | `trip:driver-schedule:c:r3` | `9c55a283-0521-56bd-92e3-e2924e198b4f` | active; all days |
| Trip | `trip:trip:a:r1:d00` | `41558278-9727-5e2d-86d9-4b0bc4c00fb2` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 00 |
| TripSeat | `trip:trip:a:r1:d00:seat:S01` | `c9956626-f693-567e-984e-132ceec97056` | `S01`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d00:seat:S02` | `81a55f3c-a44d-552d-a39f-f744d55ef9b3` | `S02`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d00:seat:S03` | `9d759be2-dc24-513b-977f-3a362f7e5401` | `S03`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d00:seat:S04` | `1ccaf00e-fba8-5394-8d28-5ddaa8eafa45` | `S04`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d00:seat:S05` | `230b7d52-d196-5048-9415-cd1b91e0e666` | `S05`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d00:seat:S06` | `9d33ce51-6a83-5bd5-995b-5ed2a00654d5` | `S06`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d00:seat:S07` | `25a5a85b-64f1-5495-8970-f8a3cbf9958c` | `S07`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d00:seat:S08` | `7ceac7cd-550b-58a2-b4f5-84846baec79e` | `S08`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d00:seat:S09` | `6e7fd810-124e-52af-bc94-28fdfffdcea7` | `S09`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d00:seat:S10` | `11e609f3-4607-5c78-8cf7-e9a78ac6b58c` | `S10`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d00:seat:S11` | `d086f77e-b062-5c88-aeaa-24d679541857` | `S11`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d00:seat:S12` | `1b4b770b-c966-535e-ad59-9e276d7c939d` | `S12`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d00:seat:S13` | `f10b0b0c-c423-5356-be9c-24db2568afa7` | `S13`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d00:seat:S14` | `bdb3a5ec-d539-585c-932f-1f701832e7d4` | `S14`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d00:seat:S15` | `c457d1b0-4077-5383-b198-c7a639641b9e` | `S15`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d00:seat:S16` | `97227d03-43e7-559b-b0a0-5bf210319d8a` | `S16`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d00:seat:S17` | `41786d56-8835-5d36-b404-13000cd0a821` | `S17`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d00:seat:S18` | `65b30ed8-edfb-59e9-81c6-1d5a5edcbb92` | `S18`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d00:seat:S19` | `a8c8f0d3-aad7-51f5-ad9e-92875d7c5a51` | `S19`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d00:seat:S20` | `422ce1ab-0b9e-5c6c-9b2c-39e33737f978` | `S20`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d00:seat:S21` | `548d644f-3da8-5f91-8a0b-47ba5490ab03` | `S21`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d00:seat:S22` | `2dddb2c9-8606-5cac-ab05-adafa7c93f0c` | `S22`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d00:seat:S23` | `8398f8f1-3f0c-5d60-8440-bacfea1d580c` | `S23`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d00:seat:S24` | `4e1ded79-589c-5d6c-b422-84c8167bf1d4` | `S24`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d00:seat:S25` | `6e4e444e-5b0f-5880-89a6-ac90c5e95ccf` | `S25`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d00:seat:S26` | `fc0db0e5-4ee9-5d79-9074-ff36885b84e3` | `S26`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d00:seat:S27` | `93c63838-9164-56d0-9204-9c1523521d82` | `S27`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d00:seat:S28` | `82334490-e8e7-5d83-9de2-e1c6acd40698` | `S28`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d00:seat:S29` | `1f88cdfc-9a49-5ffc-a980-5aa130c0ec15` | `S29`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d00:seat:S30` | `ce29b6c8-5e2e-53cd-b678-2563cc2ff3b8` | `S30`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d00:seat:S31` | `4b14c6ab-2e7c-5188-8ad4-d317c4d40652` | `S31`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d00:seat:S32` | `6773c7f0-b128-5081-a7b7-0015b2de8098` | `S32`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d00:seat:S33` | `ea1e2ee1-2f6a-5b28-ba4a-c783c6d3f6f3` | `S33`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d00:seat:S34` | `5189f5ab-0d38-5b3f-8a89-fe735e5a4967` | `S34`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d00:seat:S35` | `da25427f-7c8e-5bdf-ad02-10c84bbf0778` | `S35`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d00:seat:S36` | `5023c44e-0e34-50e2-8ede-589cfe53f0a6` | `S36`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d00:seat:S37` | `062eca01-4e2d-51c3-af5d-12ba2791827f` | `S37`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d00:seat:S38` | `296c5bb6-844e-5088-8de6-babab826a843` | `S38`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d00:seat:S39` | `08273e62-58c1-5d10-868a-f012ea991278` | `S39`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d00:seat:S40` | `9bad1972-1eea-506b-b39a-52462a60605d` | `S40`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d00:seat:S41` | `01c9f247-4870-57d6-a9ec-393c47acfb42` | `S41`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d00:seat:S42` | `09d09ed3-bbec-5842-9727-2610642e610b` | `S42`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d00:seat:S43` | `95a22483-7da3-5abb-860c-7f6b70373f72` | `S43`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d00:seat:S44` | `c382303d-9c7f-5477-b491-7d64dc9a9ce2` | `S44`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d00:seat:S45` | `23ad1377-31ed-57b6-8300-c69a99b4b10e` | `S45`; STANDARD/AVAILABLE |
| Trip | `trip:trip:a:r1:d01` | `06fef28a-816f-53e4-b07c-9a9a99278fa7` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 01 |
| TripSeat | `trip:trip:a:r1:d01:seat:S01` | `3b0cd88c-a15c-5f89-9dcb-7c9e97069452` | `S01`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d01:seat:S02` | `80e145dc-0b22-58be-a4b2-0e81387639cd` | `S02`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d01:seat:S03` | `7b26f1e1-03dc-56ec-878b-1c3417c62c7d` | `S03`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d01:seat:S04` | `9ade9b6c-19f9-54bf-abb1-28942f094312` | `S04`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d01:seat:S05` | `37cb99e9-d116-50aa-9b09-39c021592440` | `S05`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d01:seat:S06` | `069b4257-c04a-5843-86bc-27bbbd455d32` | `S06`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d01:seat:S07` | `b118788a-0f48-558d-aa60-e6ac23a9dd75` | `S07`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d01:seat:S08` | `bdc00e47-124b-5718-b7d0-ad411f395b70` | `S08`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d01:seat:S09` | `6ecec572-f42b-5eed-a20c-7d84fa493d1d` | `S09`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d01:seat:S10` | `80458558-9d7a-5069-8d4f-96c936a126d2` | `S10`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d01:seat:S11` | `77a23934-31a8-5698-a272-7a20f36f2d60` | `S11`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d01:seat:S12` | `03d476fa-5c3f-5566-b05f-0354d2bd370b` | `S12`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d01:seat:S13` | `491376fe-df52-56e3-ad16-a11848a0eff8` | `S13`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d01:seat:S14` | `ca173cb0-0355-5ec9-91b2-eb3f6f2484f5` | `S14`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d01:seat:S15` | `c38d0318-8390-5e28-9d04-5478c702001c` | `S15`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d01:seat:S16` | `9fc7074f-9cfa-520b-b64e-259c9937a1c1` | `S16`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d01:seat:S17` | `fb50b40f-8563-5847-8619-fe1fd8829f37` | `S17`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d01:seat:S18` | `3302ff4d-8a40-5831-bf30-2c85a1cd512b` | `S18`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d01:seat:S19` | `5c3cdb65-5eab-506e-8b25-ab5fc54611a0` | `S19`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d01:seat:S20` | `226a0e76-0947-5622-a056-67b04ff506fe` | `S20`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d01:seat:S21` | `0a681485-55b4-58ba-90a9-f36468901e92` | `S21`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d01:seat:S22` | `26458d6f-8528-5e89-ba07-3626a3a64183` | `S22`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d01:seat:S23` | `2548423a-cbcf-57df-9d23-4c7d4fef7c7a` | `S23`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d01:seat:S24` | `4208cb8e-95a4-5d7b-89d6-5f72dcddfcce` | `S24`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d01:seat:S25` | `711b03f3-3d8f-56fc-b84b-c9521db50186` | `S25`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d01:seat:S26` | `a52c8fdc-83d6-5b3a-ac90-5e8ea9732481` | `S26`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d01:seat:S27` | `67f17429-bab7-53e7-b509-34a072f70aac` | `S27`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d01:seat:S28` | `8edfa8cb-5ac1-5c5c-b0e6-1cef83f0c8c1` | `S28`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d01:seat:S29` | `20176eed-735f-531a-9be9-cc5f37d42b6d` | `S29`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d01:seat:S30` | `eb5b85f8-e645-5e51-b888-ba15a8bc2d93` | `S30`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d01:seat:S31` | `36d12344-c0ee-557e-b2e3-645d6d17e267` | `S31`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d01:seat:S32` | `a14d6e04-6ee8-5b11-9e17-b23657c030bf` | `S32`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d01:seat:S33` | `514f4d61-3d67-5b95-ba0b-bcb314bdeecb` | `S33`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d01:seat:S34` | `e2897748-0d29-5594-bcf6-3eca662dc279` | `S34`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d01:seat:S35` | `35e044c4-e8ed-5566-a5c4-d4c9cdd7b629` | `S35`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d01:seat:S36` | `ef5f7a7f-2732-52f9-bce0-2cd331ed5707` | `S36`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d01:seat:S37` | `2fe9476b-5f42-563d-aa82-d16d8283fa5d` | `S37`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d01:seat:S38` | `37bb3550-4cfc-52cd-8720-fddc20f7b9bd` | `S38`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d01:seat:S39` | `6b7ffeb9-a5e6-556d-927b-dadd339f28c7` | `S39`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d01:seat:S40` | `04e1a894-28b4-5450-80b9-87e142634f9f` | `S40`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d01:seat:S41` | `4e33998e-16b3-50d4-a1de-9880c8894b5a` | `S41`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d01:seat:S42` | `b6add33e-fcfb-55d1-88a8-170eff5c98e1` | `S42`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d01:seat:S43` | `9041c067-e340-59c4-b9d6-7136b9e0f8b8` | `S43`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d01:seat:S44` | `e9adc879-e686-586e-a2f2-bc44f65138ad` | `S44`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d01:seat:S45` | `afe96fd5-31cd-54a4-85dd-a7a7175dee4b` | `S45`; STANDARD/AVAILABLE |
| Trip | `trip:trip:a:r1:d02` | `f96bf0c0-46ac-5327-9a7b-f6ca444f743b` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 02 |
| TripSeat | `trip:trip:a:r1:d02:seat:S01` | `d9c3e095-1313-54df-89b8-6f7fb3e196d8` | `S01`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d02:seat:S02` | `27bc751f-875e-5b57-91c0-e42dfc15e6fd` | `S02`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d02:seat:S03` | `2cafd2fa-8da7-53d8-be03-95a31e179c3c` | `S03`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d02:seat:S04` | `6c6dadbd-8f64-5f02-9ced-f7f027fcf64e` | `S04`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d02:seat:S05` | `c54f316a-4083-560e-916c-d8b600a0d630` | `S05`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d02:seat:S06` | `3119cea4-b315-5f1f-b3b6-3805144a9e00` | `S06`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d02:seat:S07` | `8c1d0f1f-bc01-5754-8751-a9763bc455cf` | `S07`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d02:seat:S08` | `005d2041-06c9-53de-abae-a98b1f8023bd` | `S08`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d02:seat:S09` | `0c929458-2765-5166-9871-00bdaf055816` | `S09`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d02:seat:S10` | `e5275be9-1589-5c5f-bd49-c78034500b67` | `S10`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d02:seat:S11` | `051c65ab-3032-5d67-be8e-7b1713e1e641` | `S11`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d02:seat:S12` | `e24e8bca-9a0c-59a5-b116-3c564a847ada` | `S12`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d02:seat:S13` | `8624c7bc-2074-50ae-9b85-3c8759bba72f` | `S13`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d02:seat:S14` | `b9cc7691-f424-5613-a336-0eea1409f3bd` | `S14`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d02:seat:S15` | `94601536-8001-559e-9309-6ab0e58ee180` | `S15`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d02:seat:S16` | `46f5d65c-2b96-5f62-a4ae-c16f1004752e` | `S16`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d02:seat:S17` | `23d4f4bf-0699-5cc9-9fa8-f234870ce8e5` | `S17`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d02:seat:S18` | `4e20ec26-5948-5871-91d3-0590829a943f` | `S18`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d02:seat:S19` | `8bb77a1f-ae6f-5060-ac83-8011350585f1` | `S19`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d02:seat:S20` | `e94cbd33-d8e0-5b78-89eb-4280c4180f1c` | `S20`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d02:seat:S21` | `69c952bf-7f9d-54a5-ac04-44e9d3fec647` | `S21`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d02:seat:S22` | `af36b9b1-27fb-503d-a8b5-2ef36fe87191` | `S22`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d02:seat:S23` | `252682f1-8f58-5b4e-b2e9-17f8be580826` | `S23`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d02:seat:S24` | `ef31df9b-f8cb-5d80-8424-b0b7a87dbd71` | `S24`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d02:seat:S25` | `09d2b875-c6dc-5aa4-908f-05b3651eb113` | `S25`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d02:seat:S26` | `8d175acf-4d6a-5953-9d8a-6db5486a8857` | `S26`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d02:seat:S27` | `57719f7a-750a-53fb-8141-10465291e4c1` | `S27`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d02:seat:S28` | `16147b63-7d7d-5840-a854-787af97904cf` | `S28`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d02:seat:S29` | `9650144f-fb95-55b9-a0b0-3e2fee694c2b` | `S29`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d02:seat:S30` | `d251286f-12d4-5595-9ad3-4f4bf588b531` | `S30`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d02:seat:S31` | `04db0b12-1041-56f9-9389-e330f32d3af3` | `S31`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d02:seat:S32` | `c2829b78-8e5d-56db-85f9-0b2d49b39171` | `S32`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d02:seat:S33` | `ffe13364-c75a-5635-9531-c5ba37807319` | `S33`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d02:seat:S34` | `41053097-4798-5808-9882-750a822e0928` | `S34`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d02:seat:S35` | `3ec9fa52-480d-5f99-bba7-aef1c974aa4c` | `S35`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d02:seat:S36` | `20d015a2-ed31-5774-a157-85392fc4aa59` | `S36`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d02:seat:S37` | `b42334fd-363d-5610-a831-df6ae93707a0` | `S37`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d02:seat:S38` | `8db81cff-dc11-5b96-a24a-7d779bd2dd93` | `S38`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d02:seat:S39` | `1b6cf896-11eb-5943-88f7-2c88843ee2b8` | `S39`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d02:seat:S40` | `f9944f3c-b46c-50d9-9676-ab39bac8fd76` | `S40`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d02:seat:S41` | `a39e2e8a-da63-59e4-a859-2a72fe05ab11` | `S41`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d02:seat:S42` | `8d4af618-b311-5386-a207-d928937a6b78` | `S42`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d02:seat:S43` | `e247d4c8-b798-5c59-86d8-c451f68b317b` | `S43`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d02:seat:S44` | `5a1e17f7-0660-5429-8eff-b893a8c7045d` | `S44`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d02:seat:S45` | `e9d50ff0-08f6-5569-bb66-82d372111654` | `S45`; STANDARD/AVAILABLE |
| Trip | `trip:trip:a:r1:d03` | `a28b1a43-a465-5c1a-b646-ebfa6c727a72` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 03 |
| TripSeat | `trip:trip:a:r1:d03:seat:S01` | `1f3780d0-a204-54af-8351-3a4280b49ff6` | `S01`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d03:seat:S02` | `7d5b3752-69e7-526b-99e7-f1445b926020` | `S02`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d03:seat:S03` | `358feef0-380d-50fd-b55e-c5c79ae454ca` | `S03`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d03:seat:S04` | `da1ef967-cba8-5da4-a1d3-69b82b174efe` | `S04`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d03:seat:S05` | `c4c8e388-b912-5c90-8419-a65af3d22f3a` | `S05`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d03:seat:S06` | `767e68a0-9b19-5c58-b392-c727c7ee2c30` | `S06`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d03:seat:S07` | `a51f3bf3-1a15-5a25-9a9d-3ff01cae15c1` | `S07`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d03:seat:S08` | `9ebb26e3-f9d0-560b-965e-759feb5d8037` | `S08`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d03:seat:S09` | `b4394e48-592d-522c-95e1-72d2fe2658a2` | `S09`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d03:seat:S10` | `fa3190fb-9893-5563-a6f3-b1b7071f67f1` | `S10`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d03:seat:S11` | `8de70b94-7b3f-574f-89ac-3347d19a579e` | `S11`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d03:seat:S12` | `40653d33-d446-50e1-9e96-c031023de0af` | `S12`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d03:seat:S13` | `89ec7829-e274-5ebd-afc9-df69e747f203` | `S13`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d03:seat:S14` | `a6ac9ce2-9c0d-5d58-8a69-f3928d807658` | `S14`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d03:seat:S15` | `a47a5df9-57bd-5be5-954c-b2a6937078ec` | `S15`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d03:seat:S16` | `7da81b0a-74ec-5034-83d1-c5b661c6ea5e` | `S16`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d03:seat:S17` | `9cceb731-cd15-545c-8029-103984800b6d` | `S17`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d03:seat:S18` | `a66056f3-8fb3-55ac-823b-e0caa2e67858` | `S18`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d03:seat:S19` | `231867cc-efa9-52e5-accf-6d059cb8e3cf` | `S19`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d03:seat:S20` | `8d561797-3460-53d4-ad9a-f09f3e1c6ead` | `S20`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d03:seat:S21` | `ca528450-30b2-5206-ab1c-1e9004f2250e` | `S21`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d03:seat:S22` | `b565f58a-cab4-501c-9692-4fef846799c3` | `S22`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d03:seat:S23` | `7c2deb3a-7ceb-5562-9ec4-71efffb7747b` | `S23`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d03:seat:S24` | `27d084fb-574a-5bc6-8603-fe99a2cdf6f6` | `S24`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d03:seat:S25` | `53a898b8-3362-5aa3-ad0e-f68c10eb604a` | `S25`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d03:seat:S26` | `93b660a3-f548-5971-a894-242858e8601e` | `S26`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d03:seat:S27` | `a4450904-5948-5dca-8df4-0b7bd81fbdb5` | `S27`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d03:seat:S28` | `7ddcc1b8-641d-5d98-981e-8548fa659a52` | `S28`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d03:seat:S29` | `67fe22a8-3ef7-522d-9de8-8a24bfd7909e` | `S29`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d03:seat:S30` | `e8e41c2a-fd31-5a1f-8904-df25af7180b4` | `S30`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d03:seat:S31` | `e05dfe16-7b9f-5734-96d0-0caad642074e` | `S31`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d03:seat:S32` | `64e63762-4d40-53a0-a3db-01fd77c715dd` | `S32`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d03:seat:S33` | `d4d2dfaf-cb3e-5608-b605-0733097ca83d` | `S33`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d03:seat:S34` | `e8c20401-555a-527c-95ae-a64c1e2cf56e` | `S34`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d03:seat:S35` | `41626468-6ec1-5d5d-87cf-dc8ccdf820de` | `S35`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d03:seat:S36` | `f294fd0a-bd1b-5a03-b44e-bafe5a52ca0e` | `S36`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d03:seat:S37` | `ce0b6b1f-48a7-513c-83b6-6045c411fe38` | `S37`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d03:seat:S38` | `f166f914-e025-5b21-871a-ff1b9e0a2a2e` | `S38`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d03:seat:S39` | `b6014216-1cd2-5980-b258-6993bee99733` | `S39`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d03:seat:S40` | `e62af7a4-ea96-509f-816e-1b86bcbeac3a` | `S40`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d03:seat:S41` | `05dea7d6-621c-598b-b704-355c19e659d8` | `S41`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d03:seat:S42` | `c64afca6-99c9-56bc-9107-b04e25811548` | `S42`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d03:seat:S43` | `6ca91160-2d8c-55b3-8422-577a7ca11111` | `S43`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d03:seat:S44` | `6f6f0ea3-458b-5142-bf09-408b5e8dd48e` | `S44`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d03:seat:S45` | `08245596-c38d-5b9e-83d8-bacfe1a37368` | `S45`; STANDARD/AVAILABLE |
| Trip | `trip:trip:a:r1:d04` | `2e47ba86-0b59-5c6c-a824-5a1427ede3b7` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 04 |
| TripSeat | `trip:trip:a:r1:d04:seat:S01` | `f666b4ad-b294-5ece-9391-06bdc5d54eaa` | `S01`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d04:seat:S02` | `6f8e5e68-0c26-5df7-865d-867bd0be22c8` | `S02`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d04:seat:S03` | `17797317-e002-5fd1-af9e-f658a8e1ce2a` | `S03`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d04:seat:S04` | `5f58e4f1-44b6-5a5f-b067-e1f63cd67d3a` | `S04`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d04:seat:S05` | `499b38e8-5460-5499-b9bc-b5d341cbfd5e` | `S05`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d04:seat:S06` | `cfa955e7-b334-554b-9a5b-fb976eb36ba4` | `S06`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d04:seat:S07` | `f0557749-536f-5364-ad11-a0b65904adf4` | `S07`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d04:seat:S08` | `e2f95a06-c5d7-5561-a6f4-2bcb6e709042` | `S08`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d04:seat:S09` | `f27dc703-6c17-5d2f-984e-20ff61c6cf0f` | `S09`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d04:seat:S10` | `03cafbe3-7061-5574-8326-f225f2259416` | `S10`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d04:seat:S11` | `83de6ef8-cdbe-52f0-8248-a45b856d436f` | `S11`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d04:seat:S12` | `98140e2e-468c-5423-8524-849644f7b330` | `S12`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d04:seat:S13` | `d0dda849-0300-5874-bde0-5e263b30f1da` | `S13`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d04:seat:S14` | `e5651c7b-cda8-531f-b456-0284085a352c` | `S14`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d04:seat:S15` | `683c7d37-99e4-5f9c-bfeb-6eeaa3416ce1` | `S15`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d04:seat:S16` | `d4b3f989-7a90-5011-90ee-a743d09eb592` | `S16`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d04:seat:S17` | `ac8f39ca-532c-5576-9eb5-e07d685d6be6` | `S17`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d04:seat:S18` | `3cb7c8c2-3ead-5734-bce8-a6ebf50b4110` | `S18`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d04:seat:S19` | `f6517414-dc3b-516c-800c-82396e779615` | `S19`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d04:seat:S20` | `5b842d02-b1b4-57c9-80d0-c44e58dc5d3e` | `S20`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d04:seat:S21` | `383224a5-7c2f-531e-974e-a6c7fcbbc566` | `S21`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d04:seat:S22` | `b4e62666-ad77-5b89-85b2-3d909181f03d` | `S22`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d04:seat:S23` | `48d47517-a6dd-5b35-8a3a-81d5f23f018e` | `S23`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d04:seat:S24` | `56b0e12a-8c0a-5898-abf4-ca9c6a67c397` | `S24`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d04:seat:S25` | `c3663525-98e3-52ee-bea3-7be6fa7b3640` | `S25`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d04:seat:S26` | `56c50fa1-016a-5e89-a55d-eee8278e2dd7` | `S26`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d04:seat:S27` | `cc095417-1b77-571e-964e-78cf3aa54090` | `S27`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d04:seat:S28` | `af3d600e-29c2-5367-b01c-1422b105db81` | `S28`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d04:seat:S29` | `4eaadd1e-209a-53eb-b52c-e67b15e06a9d` | `S29`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d04:seat:S30` | `7961e337-c249-5022-ba89-bfbd58e4bce7` | `S30`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d04:seat:S31` | `b8118c5b-d669-5d32-9b2c-fc83cbfab788` | `S31`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d04:seat:S32` | `8b07acc8-2497-52ef-8c52-61fb8d1e0814` | `S32`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d04:seat:S33` | `519af1ea-fe72-55f1-9dd3-dbdd0e283e94` | `S33`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d04:seat:S34` | `bb72a6c4-1c38-5243-9a85-88c20ce707a0` | `S34`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d04:seat:S35` | `09243ae7-ffbb-5148-85dc-8ad4df2b546c` | `S35`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d04:seat:S36` | `cdb725d3-91be-53c4-83cf-2ecec5bc694f` | `S36`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d04:seat:S37` | `582b0c2e-9a8b-51c1-8180-8140d93e60a1` | `S37`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d04:seat:S38` | `3c59f27a-9578-59c9-8a46-d79517d42715` | `S38`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d04:seat:S39` | `cc7754d8-53d7-5646-978e-e7255a9ea4c2` | `S39`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d04:seat:S40` | `0adda11f-7570-562e-a9a6-df3265b743e6` | `S40`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d04:seat:S41` | `46f12e5f-6f82-58a6-94a1-271380609bad` | `S41`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d04:seat:S42` | `a50ca411-0ad1-5ec3-8669-47a6d3c00a1d` | `S42`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d04:seat:S43` | `a81293fe-c14a-5c45-b32d-a16d925ee7b0` | `S43`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d04:seat:S44` | `c8738e98-ef4f-5398-bfac-29bff5d1c222` | `S44`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d04:seat:S45` | `fd79cac7-9335-5248-bad3-3fe1e073e4fd` | `S45`; STANDARD/AVAILABLE |
| Trip | `trip:trip:a:r1:d05` | `1983c8e4-7b34-5da4-828b-90e39523c62a` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 05 |
| TripSeat | `trip:trip:a:r1:d05:seat:S01` | `9438edae-78b8-56b1-bad3-beae3ba2de56` | `S01`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d05:seat:S02` | `7c3a70f9-9c4d-5fb8-96fa-e72744370d35` | `S02`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d05:seat:S03` | `01f342b6-0a56-5370-9200-25e9ae851e1d` | `S03`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d05:seat:S04` | `6fd45ce1-046c-535f-a686-953a96420f31` | `S04`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d05:seat:S05` | `8e3e1c95-5afb-5626-9536-acfee9f522a7` | `S05`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d05:seat:S06` | `e7fcb65f-d94d-5552-a176-c23104a99192` | `S06`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d05:seat:S07` | `3206c553-ea0a-5f4e-8a63-0217f24b4116` | `S07`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d05:seat:S08` | `c25d271e-e500-5848-9d15-fcaa854f4dab` | `S08`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d05:seat:S09` | `3e5dba43-2c35-5692-8b5b-f33c246759cc` | `S09`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d05:seat:S10` | `eef1f8b0-036b-5d30-a9c4-cbb19595884f` | `S10`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d05:seat:S11` | `771b7fb6-499a-5e54-94c9-e52717f4fa3a` | `S11`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d05:seat:S12` | `14fe68c3-03ed-59ff-820a-e1844710a20e` | `S12`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d05:seat:S13` | `e1aad44e-5dd5-562d-b1e9-5494a7625f87` | `S13`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d05:seat:S14` | `32151297-ad6f-5733-a5d3-5653ca07ae31` | `S14`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d05:seat:S15` | `21efd9bb-a8e0-53fb-9aca-76d18b3f0dd3` | `S15`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d05:seat:S16` | `bade1160-aa2a-544d-a28b-6ef8c65be640` | `S16`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d05:seat:S17` | `045736de-bd90-5616-b298-8cfa99206804` | `S17`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d05:seat:S18` | `eaa422b0-f22b-59b2-bb4f-42e3cfcfb41a` | `S18`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d05:seat:S19` | `116ec901-dfe4-5787-b806-7f0757ac243f` | `S19`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d05:seat:S20` | `ce77ee9c-8711-5f30-aee1-b20baf0e1588` | `S20`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d05:seat:S21` | `571ff96c-67b4-5c0e-9067-c2bb80cdc027` | `S21`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d05:seat:S22` | `28cb3310-6896-5244-a986-84a1fca22332` | `S22`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d05:seat:S23` | `83a7497d-1d55-5ab1-a6cc-4df0880545eb` | `S23`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d05:seat:S24` | `0e1b7d7d-d9e4-5341-b3f6-849e5c5f4668` | `S24`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d05:seat:S25` | `1fab4a0a-04bd-5a09-b5b5-7a788cb92b9e` | `S25`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d05:seat:S26` | `eba0739e-5d42-539e-ae60-d06bca2a1607` | `S26`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d05:seat:S27` | `d0922468-21dd-55e1-b292-576a74a9f3ef` | `S27`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d05:seat:S28` | `6c700da1-1c22-52e7-b1d2-da5b85217e2f` | `S28`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d05:seat:S29` | `8ad78e65-e08b-5b48-86ad-26cc902f3841` | `S29`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d05:seat:S30` | `dce6998b-4a9d-5370-98a0-4398f0bc35bd` | `S30`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d05:seat:S31` | `7e150429-042b-5080-8eba-1b2b716d7428` | `S31`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d05:seat:S32` | `1f06baf7-0b88-508e-992d-1eb6d76cb8d1` | `S32`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d05:seat:S33` | `6a5ce37b-9d09-530c-9e8d-68d1e3d99137` | `S33`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d05:seat:S34` | `ba76a778-b958-5692-876c-5e027dfee4e3` | `S34`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d05:seat:S35` | `38cbdb5f-ff3c-5a2e-a251-a707be044e6e` | `S35`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d05:seat:S36` | `04960755-2bed-5415-8b4d-b84cf7e5e9d4` | `S36`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d05:seat:S37` | `ee71fa6d-f674-524c-b7cb-2625d256e24a` | `S37`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d05:seat:S38` | `9e0b8dc0-29a2-563c-9d10-780e2977bc20` | `S38`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d05:seat:S39` | `7219cf36-bde3-516f-8626-7b871a8f3a70` | `S39`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d05:seat:S40` | `74a3442b-6b94-5877-969a-6d8faa25e4f0` | `S40`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d05:seat:S41` | `33ebb0c2-3815-5bc7-a459-7629c46f796f` | `S41`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d05:seat:S42` | `b353712e-558d-522d-b943-dcd85349c47d` | `S42`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d05:seat:S43` | `586b3083-9c84-5774-9c6f-568914059c05` | `S43`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d05:seat:S44` | `52f881cb-091e-54d0-8f4f-27dd39671589` | `S44`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d05:seat:S45` | `c4e52ef7-55b5-51e6-b487-a3165d4053cf` | `S45`; STANDARD/AVAILABLE |
| Trip | `trip:trip:a:r1:d06` | `54819d17-7968-5a81-bbe5-7def90add522` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 06 |
| TripSeat | `trip:trip:a:r1:d06:seat:S01` | `3a1b7aaf-ee37-5f78-8b2e-eea98113243f` | `S01`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d06:seat:S02` | `41b928a9-df79-5c08-aa9d-94543c5a5a31` | `S02`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d06:seat:S03` | `432f8b63-1e84-516c-83d0-b577730cf93a` | `S03`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d06:seat:S04` | `57860fd1-521f-5655-8bde-d406fb1c378c` | `S04`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d06:seat:S05` | `6209c09b-5a3a-5f8e-8078-5fc175afe271` | `S05`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d06:seat:S06` | `6de3bdc9-b7f8-5997-a631-fcaa0486bfe8` | `S06`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d06:seat:S07` | `e132c061-2c4e-5ac2-ae89-80a2724e81c7` | `S07`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d06:seat:S08` | `fd44be32-2b51-58d2-9ac4-a16308305b3a` | `S08`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d06:seat:S09` | `fc30cd46-0a61-5edb-af47-5b561f484537` | `S09`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d06:seat:S10` | `3d30fdf2-91ae-5cd9-ab91-ef0c855c780e` | `S10`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d06:seat:S11` | `d4a172c4-9f19-557a-822a-b8c3d9ac74a8` | `S11`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d06:seat:S12` | `21b5ef60-de66-502a-bb68-f18f3a9e4d0a` | `S12`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d06:seat:S13` | `57be1ff1-ed86-5f7a-8c53-fa56a7f00dd5` | `S13`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d06:seat:S14` | `e51505f4-1599-5214-9acd-1c2d76d5fe7f` | `S14`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d06:seat:S15` | `7bcfa8e0-1526-50e4-8787-3775e2af0394` | `S15`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d06:seat:S16` | `fdef2c2b-4326-51c3-ae14-027bc3029bc5` | `S16`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d06:seat:S17` | `4aadef63-cb4f-5219-8cf1-81eefb126cc6` | `S17`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d06:seat:S18` | `0aa272bc-76e8-5e58-9755-06d48ee6e0a0` | `S18`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d06:seat:S19` | `d4071702-7ffe-5256-ad33-1f86d4bc3668` | `S19`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d06:seat:S20` | `597664cb-ff67-5fd3-a116-f49138f377d9` | `S20`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d06:seat:S21` | `8201777b-6782-5054-b318-ed6239f7b10c` | `S21`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d06:seat:S22` | `71c5ceee-f60b-5ef2-b921-20a2848d28a4` | `S22`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d06:seat:S23` | `87d0db81-8672-5abb-84d5-0092b8468ffc` | `S23`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d06:seat:S24` | `b5f3d7f9-c8f2-5ded-88ab-7f331e3960cf` | `S24`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d06:seat:S25` | `079639e1-2869-500c-b611-7ba93387041f` | `S25`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d06:seat:S26` | `b21e5f0d-7995-532f-9c89-e2e4a608adfc` | `S26`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d06:seat:S27` | `0ec40f5c-0f89-5d72-b74d-cbdae3c39911` | `S27`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d06:seat:S28` | `b936dd66-b84d-58bb-b1bb-7a97af8d731a` | `S28`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d06:seat:S29` | `53d60caf-90af-523e-9d14-6204a0d26ec5` | `S29`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d06:seat:S30` | `992c429b-4c6a-51b8-94de-0c91565832a4` | `S30`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d06:seat:S31` | `8ca4147b-1b33-5989-8716-b182f90d405b` | `S31`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d06:seat:S32` | `a30f0a14-47d6-53a1-b922-2e0e4b060ba4` | `S32`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d06:seat:S33` | `fdda7f4d-9e2e-5aa0-a5a5-9698fdbe4f9f` | `S33`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d06:seat:S34` | `1e762542-0d97-5e12-af0a-aa2027a6d098` | `S34`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d06:seat:S35` | `105f2fa5-5f9f-56f2-b016-fa1ff3e8ee53` | `S35`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d06:seat:S36` | `c159a220-b408-5bfd-93c3-fdcbd9e8e59a` | `S36`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d06:seat:S37` | `86e6ff95-588b-53cd-8ea0-a128e8daff20` | `S37`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d06:seat:S38` | `1d74aae0-3e66-5806-b5e6-4de9b9cb4790` | `S38`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d06:seat:S39` | `6c74ed5c-ae46-53b6-951a-0f26f4d56da5` | `S39`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d06:seat:S40` | `a5798c86-b0f1-5536-8287-8e918e5078af` | `S40`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d06:seat:S41` | `28e3d7e9-d021-53c5-8ec3-380c760276ff` | `S41`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d06:seat:S42` | `59eeb2b5-3901-5c2a-81c8-52f6a70b1822` | `S42`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d06:seat:S43` | `dbf01832-91c3-527b-819e-8e9b4609ad58` | `S43`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d06:seat:S44` | `283cb747-7160-5261-aede-dbdbca1e70ba` | `S44`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d06:seat:S45` | `ab7b2c3f-f781-5bb2-b608-94ec8141337e` | `S45`; STANDARD/AVAILABLE |
| Trip | `trip:trip:a:r1:d07` | `774c55bf-2c8e-5229-a1f8-3a17daff4fbd` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 07 |
| TripSeat | `trip:trip:a:r1:d07:seat:S01` | `45c690c7-1046-51b4-b73e-4dd17326dab4` | `S01`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d07:seat:S02` | `96fbf026-ba76-5053-b249-0b0a6d707891` | `S02`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d07:seat:S03` | `6f8a390e-c88b-5455-87e8-8414108ad5a0` | `S03`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d07:seat:S04` | `4eb36525-d7d1-5a6e-b07b-cd0851478355` | `S04`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d07:seat:S05` | `0b8533ba-2c2c-5fb7-a934-3662e77b5e3c` | `S05`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d07:seat:S06` | `bdf88acc-6d48-5d27-9d67-451b8d2ada2a` | `S06`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d07:seat:S07` | `1d4ff1ce-730e-5274-9a34-00b2ac8a1eae` | `S07`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d07:seat:S08` | `d3a00153-db9c-5c0e-a80a-d5cfc39c0c41` | `S08`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d07:seat:S09` | `dbb3ab68-1e68-5e9a-a58c-a967eceae567` | `S09`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d07:seat:S10` | `f906a34d-21fc-5010-b636-2aa6835abe96` | `S10`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d07:seat:S11` | `7b67ff12-60e8-592d-af09-ed7030f00777` | `S11`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d07:seat:S12` | `bc208471-34f3-53f2-b930-2c7c3aecbeb7` | `S12`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d07:seat:S13` | `2e93b8d6-8251-5410-900b-41673a05633c` | `S13`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d07:seat:S14` | `428e00c1-a136-5b52-b06e-69119df4c3e9` | `S14`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d07:seat:S15` | `e1c307ad-1504-5e98-8fc5-2165862b4277` | `S15`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d07:seat:S16` | `8e169539-ec46-5a12-80af-18951fb5d161` | `S16`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d07:seat:S17` | `bb23610b-1ccb-5a46-b70d-2708d33e0281` | `S17`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d07:seat:S18` | `4311ff1b-fe3c-5e13-a73b-6c668041a1a6` | `S18`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d07:seat:S19` | `5ea8bd53-8e20-5239-808d-f7be4cf39baf` | `S19`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d07:seat:S20` | `bf300390-bc0b-5998-853a-0972dc8a245e` | `S20`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d07:seat:S21` | `60cfba29-ec7a-5cfa-bb96-ecdbdbf6410d` | `S21`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d07:seat:S22` | `18904021-3b18-5094-b991-80f8b48b548e` | `S22`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d07:seat:S23` | `735b8b53-1a80-5ef6-ad6f-dc9116f7c069` | `S23`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d07:seat:S24` | `8e0165f7-70c1-5f96-906d-ea82ed06022c` | `S24`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d07:seat:S25` | `9a4b8ad9-5d40-565b-80e1-a9699ffc68bd` | `S25`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d07:seat:S26` | `51c0d362-41d4-5ccf-9911-17f0eed8d5f0` | `S26`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d07:seat:S27` | `a3336659-39c0-52db-8811-fc1e4345d2b7` | `S27`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d07:seat:S28` | `f383b91c-0bb3-5234-ae55-007ccac25e42` | `S28`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d07:seat:S29` | `f8fcdf3f-2ef1-5beb-a21f-791a726e3e58` | `S29`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d07:seat:S30` | `f83acafd-d34a-5533-94ec-5077b7e6dfef` | `S30`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d07:seat:S31` | `e860c7c4-95c5-544f-93d2-9443bc5cbd8b` | `S31`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d07:seat:S32` | `1b1b4768-28ee-56fe-96b2-e066f06441f2` | `S32`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d07:seat:S33` | `e995007a-a92c-533b-b933-d3bb22a21cb7` | `S33`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d07:seat:S34` | `49a91406-1ccf-5103-877f-650f84616303` | `S34`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d07:seat:S35` | `39b6d153-04c0-5134-9020-126b2ff0c92a` | `S35`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d07:seat:S36` | `45cbedb8-196a-596f-9885-f7236bd302a4` | `S36`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d07:seat:S37` | `d4c08a5b-8ae9-5b2b-b94f-fb2d60c2b6d6` | `S37`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d07:seat:S38` | `c574d74d-bbd2-5975-9d54-ce62cae9f4fb` | `S38`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d07:seat:S39` | `bf4d3523-1006-5947-b06d-8e7e250d6e9e` | `S39`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d07:seat:S40` | `ce6b0c45-a223-54c9-a922-9eac7657e02b` | `S40`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d07:seat:S41` | `d2d39635-d9a2-5b16-ada7-068073bfe6b3` | `S41`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d07:seat:S42` | `460d093e-9081-5dc5-a3ca-d8bdec4a2cdb` | `S42`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d07:seat:S43` | `7c6ccd2e-c884-5c2e-879a-438b2807df77` | `S43`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d07:seat:S44` | `5b759a7c-b47e-5c26-8191-42ac63360f03` | `S44`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d07:seat:S45` | `be949335-8cd7-5c2f-a55e-7f5eed8fc9af` | `S45`; STANDARD/AVAILABLE |
| Trip | `trip:trip:a:r1:d08` | `57afd3b3-ac8e-5c8a-a846-59fc1e9fa547` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 08 |
| TripSeat | `trip:trip:a:r1:d08:seat:S01` | `c0c7a8c1-c202-59da-ad44-96064b9e096c` | `S01`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d08:seat:S02` | `be15bada-f470-5308-b4a6-79ae0302ca8e` | `S02`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d08:seat:S03` | `bad62a07-c25f-5167-900d-c02a6bc19bc8` | `S03`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d08:seat:S04` | `05747342-2140-5f23-97d2-f556f23099de` | `S04`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d08:seat:S05` | `ba6d79ee-54ee-5e62-ba99-2e20e8156306` | `S05`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d08:seat:S06` | `cece2099-eaa4-545b-b88a-f8d3f3a7de30` | `S06`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d08:seat:S07` | `9f42757c-bbef-56fa-a30b-3fc08c5e3d17` | `S07`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d08:seat:S08` | `57e269b3-67ad-5158-a773-b0726f62210a` | `S08`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d08:seat:S09` | `c0ae9b5d-8a22-59da-9aee-e75ca1f0fa25` | `S09`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d08:seat:S10` | `ba57f576-c3d0-5ffc-8667-b1c742e931fd` | `S10`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d08:seat:S11` | `a93689e7-7a09-5c57-bea0-d7176dc44166` | `S11`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d08:seat:S12` | `00b146f9-1564-587d-85c6-50f7dac0f2d8` | `S12`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d08:seat:S13` | `460c3a71-29ce-5a95-84a5-9695de1cfb33` | `S13`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d08:seat:S14` | `d1ce90c6-2d65-5270-8762-36bc82cfa772` | `S14`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d08:seat:S15` | `558d6579-ed84-5f1e-b0c6-eb8e1e744920` | `S15`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d08:seat:S16` | `c44464c3-e1d8-5436-aec0-cb72b795cb2c` | `S16`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d08:seat:S17` | `404ba0c2-2795-5bc9-a117-019628375294` | `S17`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d08:seat:S18` | `fead5b68-3bb2-5089-abbe-c1aaddcf8771` | `S18`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d08:seat:S19` | `8bfd70e3-f37f-544b-8797-ac703f38b057` | `S19`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d08:seat:S20` | `7859b87a-3c18-525f-902f-655d5353bc31` | `S20`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d08:seat:S21` | `34bb3f35-0537-5497-8eaf-95004a80ac65` | `S21`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d08:seat:S22` | `9d546db0-3073-554a-b8e2-397c3fd59db7` | `S22`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d08:seat:S23` | `3bf29ed8-0e1c-575a-bcf1-c0fddcc52508` | `S23`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d08:seat:S24` | `6bab9b41-766a-5187-9ad6-2a22c22f43d3` | `S24`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d08:seat:S25` | `8ade212f-ca16-557b-96b0-70934b90c1b8` | `S25`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d08:seat:S26` | `64df565c-2ca8-59ef-a3e7-8535e752d5ec` | `S26`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d08:seat:S27` | `2e33b07d-6585-5b8d-82c3-297973530fc9` | `S27`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d08:seat:S28` | `da99ad08-65b6-5fea-9edc-97fe0e65c74f` | `S28`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d08:seat:S29` | `9e887de1-41f8-57b7-b150-a0d597de5c57` | `S29`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d08:seat:S30` | `8d4805bd-4a2c-54dc-9d82-9e70b608f677` | `S30`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d08:seat:S31` | `d26ca902-1a8f-5275-9f11-02a62cc2b892` | `S31`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d08:seat:S32` | `a61ca206-6187-57d4-b3b9-50e7a1939f92` | `S32`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d08:seat:S33` | `db152be1-5f9a-51e5-8abb-e12af3f286f5` | `S33`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d08:seat:S34` | `6f38c2f7-53a1-5b6f-bcb8-b709f0b2bee3` | `S34`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d08:seat:S35` | `553b45d9-953e-5d75-b1e5-bdfb015c3e0c` | `S35`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d08:seat:S36` | `0a47a3d6-ee9c-552b-9234-ef79901dc09f` | `S36`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d08:seat:S37` | `fcd22124-5a90-5941-84ad-11cf3672a994` | `S37`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d08:seat:S38` | `b3784060-0111-55eb-879a-364bdc78b5b9` | `S38`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d08:seat:S39` | `dd2a58fd-e684-5231-8598-ab2a0dc0b60a` | `S39`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d08:seat:S40` | `9c1dcf65-7bd3-5005-8ad9-881a255d521f` | `S40`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d08:seat:S41` | `7cddda58-932b-50e3-869a-7db24241058f` | `S41`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d08:seat:S42` | `dbe05d6f-ca76-5f0a-84c7-da8b66ded3f1` | `S42`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d08:seat:S43` | `7932252d-14ff-53dd-9deb-da8afd0923f7` | `S43`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d08:seat:S44` | `999aa987-f65c-51c4-bfaf-d50dc9a344bf` | `S44`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d08:seat:S45` | `bba8e620-202e-56ba-b50b-967ec8d97bd6` | `S45`; STANDARD/AVAILABLE |
| Trip | `trip:trip:a:r1:d09` | `a9d78f21-e44d-5565-9bb7-89e18e6dec07` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 09 |
| TripSeat | `trip:trip:a:r1:d09:seat:S01` | `00efb999-59c7-5dfa-9418-1516268005b1` | `S01`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d09:seat:S02` | `13f4e5aa-270d-56a7-9e1d-ab6aabeaf42a` | `S02`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d09:seat:S03` | `c3542f15-4cde-5ee7-8c6a-24bf76ce585b` | `S03`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d09:seat:S04` | `fb698d6a-1be4-5de1-bdfb-6e7774c6106f` | `S04`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d09:seat:S05` | `0d37bc1b-2673-5b1e-86e5-c2443bc3449c` | `S05`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d09:seat:S06` | `e53dcf92-eef6-57f3-b67b-55e6f81078cc` | `S06`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d09:seat:S07` | `f3e6a165-6795-50d8-9e39-e604d1a34185` | `S07`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d09:seat:S08` | `64caa6ae-e05c-5d45-b149-c44919cfd433` | `S08`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d09:seat:S09` | `b6ffcb62-2d58-5f9e-9ed2-225626508668` | `S09`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d09:seat:S10` | `a29c0c51-401e-5b9b-bc53-1098632bc2e5` | `S10`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d09:seat:S11` | `ec01fa2a-d83f-5d71-8f34-029dab538641` | `S11`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d09:seat:S12` | `679cbae9-85ed-5aea-a88c-e2183f3eedc9` | `S12`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d09:seat:S13` | `4a6904ff-4102-5f02-ae36-2e3d93e59a90` | `S13`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d09:seat:S14` | `d42b61c0-3e89-5ced-bc61-495f6f8649ce` | `S14`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d09:seat:S15` | `7b741c6e-2ba4-5f5a-9ce9-a042fd57b230` | `S15`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d09:seat:S16` | `aef25d49-294b-538c-8417-fef8851c44c2` | `S16`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d09:seat:S17` | `188650c0-394d-5ee2-9ca0-bfc1e2f29b6f` | `S17`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d09:seat:S18` | `93900084-b162-51af-905b-bf7b963d1046` | `S18`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d09:seat:S19` | `36d112d7-e2cf-5da1-b957-fd8606590f8c` | `S19`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d09:seat:S20` | `f49363a1-9cce-56b3-86aa-d89520955792` | `S20`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d09:seat:S21` | `41700455-a3ff-5d2e-b328-95c53affd58d` | `S21`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d09:seat:S22` | `8899096f-34d3-51b0-9689-7abd0551136d` | `S22`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d09:seat:S23` | `899cc5e8-be15-544d-9ffa-b7c127fac909` | `S23`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d09:seat:S24` | `bf90414f-a7d4-5af9-88b9-c9aa7d2da444` | `S24`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d09:seat:S25` | `401641c7-de35-56a9-bfb7-b994cb2741dc` | `S25`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d09:seat:S26` | `b54df1e0-baa6-5f0a-bbfe-eb22c05cffcc` | `S26`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d09:seat:S27` | `49cb9bdb-e984-5817-b656-596217c04073` | `S27`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d09:seat:S28` | `13b137ba-0e1e-51a4-b297-0162e6d62ff9` | `S28`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d09:seat:S29` | `92829778-4ece-5beb-b2bd-9d4fb94aba86` | `S29`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d09:seat:S30` | `af95a91c-3a4e-5d7b-8853-6bfc6a78973b` | `S30`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d09:seat:S31` | `406ce481-1802-579d-b83f-e4b8b004b82d` | `S31`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d09:seat:S32` | `eb8606ca-9ecf-53d3-9c9c-684b2ba67735` | `S32`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d09:seat:S33` | `0cbc9e47-ad7a-5db3-8112-e6506d5b623c` | `S33`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d09:seat:S34` | `db7f6e0f-e347-5ea6-a4e6-a9a6f3e9f68b` | `S34`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d09:seat:S35` | `486462af-e2df-5e3b-878c-442a83a727c8` | `S35`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d09:seat:S36` | `3c2d205a-1746-53f1-9b1f-115808aaeb28` | `S36`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d09:seat:S37` | `6a1840af-231b-5084-8abf-61f8131b6d43` | `S37`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d09:seat:S38` | `7f5ed177-364c-59b4-884c-c1197339cff5` | `S38`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d09:seat:S39` | `f658e236-3da9-5847-a94b-d87d080b4978` | `S39`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d09:seat:S40` | `e8321796-31e1-5ece-bdac-274a81025545` | `S40`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d09:seat:S41` | `ff1b43bf-0f41-5a9e-80cc-f8bf78ddb11a` | `S41`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d09:seat:S42` | `32ebbc60-a3f3-5372-adc7-03b66c08cb7a` | `S42`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d09:seat:S43` | `90b9f8d5-54bb-5148-9c28-acd2a21c1de7` | `S43`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d09:seat:S44` | `e1bc15d4-40b0-5de1-bf18-fb2e70bfe54a` | `S44`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d09:seat:S45` | `972ac789-4baf-58cd-8c00-ce778141171b` | `S45`; STANDARD/AVAILABLE |
| Trip | `trip:trip:a:r1:d10` | `b47b935d-4c2f-5c30-82e4-bdf989f075b5` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 10 |
| TripSeat | `trip:trip:a:r1:d10:seat:S01` | `9f33cd57-d181-52b5-9000-f48ae8852fa6` | `S01`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d10:seat:S02` | `94039711-eb1e-5d24-a4bf-33d711815bf2` | `S02`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d10:seat:S03` | `1d9c430f-e4d3-52cb-847e-8db6c8ab5bac` | `S03`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d10:seat:S04` | `67ad7229-5dae-52e7-9cac-91bb6cb66dc5` | `S04`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d10:seat:S05` | `c6719b18-640e-5bd5-a097-8044f68e4d29` | `S05`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d10:seat:S06` | `a9a1d8e6-1b68-53d7-a886-2827abf6c261` | `S06`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d10:seat:S07` | `43b97772-4ccd-5bdc-9a80-3de4eb0789f2` | `S07`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d10:seat:S08` | `bcd98986-7614-5092-aec5-533b5df5f67b` | `S08`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d10:seat:S09` | `e7c23123-0290-5f8f-b82d-4affff5deb97` | `S09`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d10:seat:S10` | `310be0b6-dd84-5ed3-bcc9-47266b01c362` | `S10`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d10:seat:S11` | `e26c7406-bed3-59b8-a46c-7ed05c57528e` | `S11`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d10:seat:S12` | `554677e1-902b-5ae3-aa24-126af4d74735` | `S12`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d10:seat:S13` | `1d84cc1c-3dc5-598c-8b89-aa3dca8b5d3a` | `S13`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d10:seat:S14` | `ed9492e8-554b-5cb7-b70f-0b9bc0bf7e4f` | `S14`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d10:seat:S15` | `94a70323-a385-5406-9c39-7a21fba0f80a` | `S15`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d10:seat:S16` | `913b1ecd-2b93-52dc-b195-64c907cb5437` | `S16`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d10:seat:S17` | `ff88578d-383b-5450-95e2-f906c7e0535e` | `S17`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d10:seat:S18` | `fd8fa1c9-2524-5583-b749-8d74adb7da84` | `S18`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d10:seat:S19` | `472849ba-8aa8-5e39-a111-b57c05b3446d` | `S19`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d10:seat:S20` | `05acd18f-b8b7-5663-8aea-db78731ef8f5` | `S20`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d10:seat:S21` | `432365a1-6ceb-57d7-a6a4-a9156ee04259` | `S21`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d10:seat:S22` | `522fc9db-d7c0-57f5-8b45-808bde33c05a` | `S22`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d10:seat:S23` | `3b2a937f-7997-599b-8079-2b345e5904e1` | `S23`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d10:seat:S24` | `4cdf95f5-2572-535a-9c25-a783cbd3594c` | `S24`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d10:seat:S25` | `0c95a594-b800-5717-b86b-39e48dfc3941` | `S25`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d10:seat:S26` | `53b87201-6f75-5820-b1dc-8a93f13477bf` | `S26`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d10:seat:S27` | `88ffe067-37af-5e7e-b767-fff55e7bd7eb` | `S27`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d10:seat:S28` | `6a849cc8-3765-5f34-9283-64e49b5ff8d6` | `S28`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d10:seat:S29` | `71b7d03c-9ef4-50d9-83a6-bb4a8720fb5c` | `S29`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d10:seat:S30` | `a1d74a1f-f15e-552c-ae0f-52f7318f9150` | `S30`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d10:seat:S31` | `20e35e6d-0d89-57da-8d5d-00d19637d008` | `S31`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d10:seat:S32` | `b6eee9ce-7385-5146-89ab-76f94fd104c3` | `S32`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d10:seat:S33` | `6f387be5-0baf-52d8-a647-dfb409433e82` | `S33`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d10:seat:S34` | `5b58ea6f-f59b-5d18-a0b5-6d54fe65e08b` | `S34`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d10:seat:S35` | `94a13cca-533b-50b4-a110-59bcf92fb8d8` | `S35`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d10:seat:S36` | `ed1f2fd6-3c16-5a52-b2b6-aed73a332474` | `S36`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d10:seat:S37` | `8bb88909-bf63-5f6c-91a4-5e7e3d537d25` | `S37`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d10:seat:S38` | `9a2736b8-40c0-5e71-a356-b1b1a1d6371b` | `S38`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d10:seat:S39` | `850fcbcc-66c1-5c88-918b-7157409998dc` | `S39`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d10:seat:S40` | `77906d9d-e128-530a-91f4-ca4a8cb2da66` | `S40`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d10:seat:S41` | `279c5350-0120-5a61-8730-22cfc6ea803a` | `S41`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d10:seat:S42` | `150e0e69-3dc4-5ecc-842c-00349052bfa3` | `S42`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d10:seat:S43` | `64b5c8b8-c224-5fbf-a50d-53ca601af87f` | `S43`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d10:seat:S44` | `55f63a88-4314-5532-8705-c14d4523f3e1` | `S44`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d10:seat:S45` | `a155671c-e99a-5ffa-98aa-60c423d260b0` | `S45`; STANDARD/AVAILABLE |
| Trip | `trip:trip:a:r1:d11` | `6c18b650-00fc-57ed-9558-c7ab53cdbd11` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 11 |
| TripSeat | `trip:trip:a:r1:d11:seat:S01` | `52978375-3c47-502e-9cd0-5cf1facb036d` | `S01`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d11:seat:S02` | `999d52c4-7ef7-58cb-b18d-15851be44328` | `S02`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d11:seat:S03` | `d29283f8-4181-5500-924a-fb4361d19bca` | `S03`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d11:seat:S04` | `30245eaa-4dba-599d-88d5-8b7b8855c888` | `S04`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d11:seat:S05` | `12f6c288-e10a-5fa6-b41b-5db91a01ba15` | `S05`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d11:seat:S06` | `2cc0b947-524e-5924-ad3a-6ef99b03259b` | `S06`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d11:seat:S07` | `9189007a-54ff-5154-a7fe-244bd189aac2` | `S07`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d11:seat:S08` | `b59c483a-fc05-5da6-88b6-0c9153db4f8c` | `S08`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d11:seat:S09` | `f0acb723-cbc5-5208-8ac9-f4e733e54463` | `S09`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d11:seat:S10` | `3edd78af-b562-57b5-9fdf-e74c8e95add7` | `S10`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d11:seat:S11` | `96ba4901-8003-5b27-9501-d9aaf2a18835` | `S11`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d11:seat:S12` | `c681bc2c-3144-5c21-a987-1915231aa9ff` | `S12`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d11:seat:S13` | `d76dc586-409e-5167-bdc6-4dc5dfe79a3b` | `S13`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d11:seat:S14` | `e2f1c518-49b2-5ad9-a79d-4d7f927cacf1` | `S14`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d11:seat:S15` | `e8ad4f30-fd1e-54e5-b9d5-f9bfd516ddd8` | `S15`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d11:seat:S16` | `3830623c-a78f-50b2-83dd-895b54fa61ca` | `S16`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d11:seat:S17` | `4eebbfc5-b924-560b-8ff0-83bcb969eb51` | `S17`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d11:seat:S18` | `d7103fad-151b-5c07-8f25-d70b14409333` | `S18`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d11:seat:S19` | `6605c368-91e7-5465-bdd6-c9ce2111fddd` | `S19`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d11:seat:S20` | `d2fd77d1-9078-5b30-9fbc-c0c9cbbd5dfa` | `S20`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d11:seat:S21` | `252a9c92-b5e2-5797-bcac-7c3ab05c762c` | `S21`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d11:seat:S22` | `4bf56307-62b8-5d2e-ad7b-50525606a487` | `S22`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d11:seat:S23` | `f6f55bd0-fed6-5eed-b6f1-2a7699ceac0f` | `S23`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d11:seat:S24` | `108f40c5-5e46-50a9-b950-067653b99994` | `S24`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d11:seat:S25` | `713bcbf3-99b1-5682-944f-767cabfb78a8` | `S25`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d11:seat:S26` | `e8cadd63-34ee-5e5c-b722-2cabe276c1c6` | `S26`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d11:seat:S27` | `ffbc584c-ca7a-59b5-8008-dc017c13e7b6` | `S27`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d11:seat:S28` | `62a3faee-ef44-5fa6-9fab-2080290842d0` | `S28`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d11:seat:S29` | `a15f4660-b5c3-5910-804a-6563eec59da5` | `S29`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d11:seat:S30` | `7814b903-a5fe-5dda-87ce-9e72c09dd930` | `S30`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d11:seat:S31` | `bcb1978c-9e3c-563d-8c1f-1cf7b4367ee9` | `S31`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d11:seat:S32` | `9e0edb2d-43d4-5e29-9617-e866e5e6222e` | `S32`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d11:seat:S33` | `1ff3b2ad-c979-5523-ad79-9c53f58bd43f` | `S33`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d11:seat:S34` | `dd224985-82d2-5f82-adc3-729414ecbd31` | `S34`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d11:seat:S35` | `d0702f80-b5b9-52a3-9614-886d38d4c15b` | `S35`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d11:seat:S36` | `b1d18be8-01a0-56a1-891f-bd46e6c496e4` | `S36`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d11:seat:S37` | `682b9329-a72a-55ae-bfc7-8f9e31c8a8f1` | `S37`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d11:seat:S38` | `3d535452-6aca-5a87-aa61-c5637faa196a` | `S38`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d11:seat:S39` | `2903a750-3288-5bcd-b3d3-e5380b0929ec` | `S39`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d11:seat:S40` | `5492378e-f28c-52b8-b211-53ebc274cfb1` | `S40`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d11:seat:S41` | `d5881e1e-5ec6-52c4-8d9a-c8a574e2752b` | `S41`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d11:seat:S42` | `5a087c8c-98fb-5d51-a7cd-95198b228625` | `S42`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d11:seat:S43` | `5bb522e4-133b-551b-a61a-8749bef5c6b6` | `S43`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d11:seat:S44` | `6625fc24-2c0b-5ea8-b9b8-9de820705d09` | `S44`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d11:seat:S45` | `033a1da8-512b-54bc-a731-34d05224399f` | `S45`; STANDARD/AVAILABLE |
| Trip | `trip:trip:a:r1:d12` | `bef2e022-287e-5de0-a517-fc9e6f1e5711` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 12 |
| TripSeat | `trip:trip:a:r1:d12:seat:S01` | `3badd64c-d6c0-5005-9ca1-b750495d4f13` | `S01`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d12:seat:S02` | `b4643d7e-1fc2-515e-9d9a-6bd5c92a0db5` | `S02`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d12:seat:S03` | `772ede88-1f4a-5be8-ac2a-eac8270e75b6` | `S03`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d12:seat:S04` | `3aca0668-b47c-57d8-aee5-bcd2f163b684` | `S04`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d12:seat:S05` | `06bc642d-b21e-541c-8884-6230c83020fd` | `S05`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d12:seat:S06` | `aefb455e-e25f-5f73-adad-a0f8bbc006df` | `S06`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d12:seat:S07` | `1be992ba-309f-50f1-aff1-66235f8e259f` | `S07`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d12:seat:S08` | `13438aa3-099e-538c-b2e7-78aed930e98c` | `S08`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d12:seat:S09` | `5cbb5b0d-fee9-5da5-aaac-a1f8fb784b1c` | `S09`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d12:seat:S10` | `35620c9d-c78f-5cd1-aec2-0b68db0920d9` | `S10`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d12:seat:S11` | `6303bf39-dc61-5c25-a7ac-6ca2ed3358ca` | `S11`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d12:seat:S12` | `85c40e6a-80f7-54d0-9d5e-3814c59862e3` | `S12`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d12:seat:S13` | `095b752a-91d2-50dd-84a5-0e9776f1e4bb` | `S13`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d12:seat:S14` | `fb79ce5a-f11c-5a12-bd47-b8bd29ccce2f` | `S14`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d12:seat:S15` | `21ff163d-6e11-5db0-be62-c67bc0398477` | `S15`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d12:seat:S16` | `655da6b1-ce32-5c7a-bd52-d4c51fce26ab` | `S16`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d12:seat:S17` | `f661d4d7-1b3f-59cc-aa43-6bdbeedd9e01` | `S17`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d12:seat:S18` | `226f0683-aec7-5322-aab7-a7232762dde5` | `S18`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d12:seat:S19` | `a8e03c00-b3d2-5bfb-9a4a-907912276cef` | `S19`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d12:seat:S20` | `41a6ebfd-77eb-50bc-886d-3ffae00d55e9` | `S20`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d12:seat:S21` | `3abdaafc-4cf1-5692-a622-ed24034ee974` | `S21`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d12:seat:S22` | `fa407609-5dca-5ea7-8064-c2afa6a83751` | `S22`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d12:seat:S23` | `c22f2697-d000-5fa0-98fa-00b47d392a90` | `S23`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d12:seat:S24` | `ab24e26b-8635-5819-b3d9-c3314ff67bc8` | `S24`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d12:seat:S25` | `2a4f2459-42b9-5974-8ba5-0ef43015ea23` | `S25`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d12:seat:S26` | `3d7c47be-8748-5024-9bb4-0f77bdfa4cc9` | `S26`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d12:seat:S27` | `1b5caba3-9754-5e1d-8610-e9352a91707f` | `S27`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d12:seat:S28` | `1353f8dc-7df5-531a-8344-a5c71c30f035` | `S28`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d12:seat:S29` | `741d4045-5851-5f66-b11b-db60375b6a62` | `S29`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d12:seat:S30` | `b58f60bd-c69a-5533-ac2c-7ef3949ef7ca` | `S30`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d12:seat:S31` | `ba1a5b10-ad2e-5014-b7e2-bde5063181f0` | `S31`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d12:seat:S32` | `4aea134d-a866-5182-9971-67bbe968bd13` | `S32`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d12:seat:S33` | `685aa900-8a8a-52fe-a773-2e47ad19da9d` | `S33`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d12:seat:S34` | `87fcf094-26c8-5cbc-bc7c-dd58bd95d238` | `S34`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d12:seat:S35` | `0e7bab86-a53f-5462-9b3a-67f7517f27c9` | `S35`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d12:seat:S36` | `84edb025-c2f8-5c13-bb39-705e09f85798` | `S36`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d12:seat:S37` | `60af02a2-5aa5-5188-97b6-61ab71a9cc02` | `S37`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d12:seat:S38` | `1ba29f7c-1d61-5016-9b55-05b73b9c039c` | `S38`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d12:seat:S39` | `c694e308-0255-5afd-b544-2079e5e87e17` | `S39`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d12:seat:S40` | `4b8c7135-c94e-56c6-b6ce-46f67c6fff09` | `S40`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d12:seat:S41` | `8a81fda8-7eca-59e5-9b99-aa5092d74079` | `S41`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d12:seat:S42` | `985e4385-f39e-5dc9-aece-f02a667c7fb4` | `S42`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d12:seat:S43` | `d35d070c-38f8-542b-9af1-c65bbc4e9598` | `S43`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d12:seat:S44` | `ead26069-fd55-5892-ac88-5f7d283ec428` | `S44`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d12:seat:S45` | `4d876d8e-0a17-5a9b-9d99-3070647792df` | `S45`; STANDARD/AVAILABLE |
| Trip | `trip:trip:a:r1:d13` | `468f59de-4618-56a8-8269-75d03bf1ddf6` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 13 |
| TripSeat | `trip:trip:a:r1:d13:seat:S01` | `10c09855-5688-5d59-b96a-5491aa68f157` | `S01`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d13:seat:S02` | `84b3941e-8d39-53fe-bb8a-99e4f3a4df04` | `S02`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d13:seat:S03` | `be449285-febc-5ae8-bc62-1cbfe45efb31` | `S03`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d13:seat:S04` | `ed30de39-7fd0-58ae-ae40-bd3a4e440c8a` | `S04`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d13:seat:S05` | `32618fa9-b3fd-57db-bacb-1512af4118ea` | `S05`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d13:seat:S06` | `db772669-b452-5978-aea1-86e4862ee10f` | `S06`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d13:seat:S07` | `b83d033c-20a6-5881-ada8-ede47ec7b8eb` | `S07`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d13:seat:S08` | `2d938cd0-f598-5023-8b85-e093eb65ffc9` | `S08`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d13:seat:S09` | `184f0f74-7d8f-54e9-83df-be416f027ac2` | `S09`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d13:seat:S10` | `6d32ea26-e387-5161-a9e8-4c921aad3f98` | `S10`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d13:seat:S11` | `537a5891-8bc5-53af-864c-600c9e4f2496` | `S11`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d13:seat:S12` | `e4de9fed-99fb-5380-a74a-1ddba0edc15d` | `S12`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d13:seat:S13` | `039cfdb9-39e8-553b-bd30-c6990ae493fe` | `S13`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d13:seat:S14` | `bc88381b-7036-52ae-af92-1bb5b5a1ef73` | `S14`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d13:seat:S15` | `e8c63869-7e48-500c-a3de-4a31a0b31617` | `S15`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d13:seat:S16` | `2f94a2b8-a538-5d3f-bb88-0225cf45de2c` | `S16`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d13:seat:S17` | `485c65e8-c853-514a-8a8a-ae2d1e5b9ba4` | `S17`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d13:seat:S18` | `1ac783e3-f9d7-57cb-bc8c-e4daa77759ac` | `S18`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d13:seat:S19` | `42d1fdcc-d2ae-5e95-ba74-3a3f8bab5955` | `S19`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d13:seat:S20` | `b1ced331-6f2b-5ac5-9b47-0c85c888be81` | `S20`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d13:seat:S21` | `5cf08c67-ddef-5688-88ea-5eda66b51c91` | `S21`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d13:seat:S22` | `f5ed3d08-3beb-5193-b13a-40cf0c52cd46` | `S22`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d13:seat:S23` | `a83a5307-7220-5dd4-8936-6673ae193b0e` | `S23`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d13:seat:S24` | `2cb4c9ef-64ed-57b5-a8fb-db18d8a84b3d` | `S24`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d13:seat:S25` | `a6b38ff2-6a88-5269-acad-4d30b6c7bf23` | `S25`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d13:seat:S26` | `c37d5533-82a8-5a40-b723-3a57d4e9dc9d` | `S26`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d13:seat:S27` | `a5916dea-db31-56de-96df-48637cd6fe23` | `S27`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d13:seat:S28` | `f20fba97-ad4f-53dd-8953-718063e447f0` | `S28`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d13:seat:S29` | `7741aadb-8223-56da-9c6d-d9e8e73d0e6a` | `S29`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d13:seat:S30` | `4070a265-d3ed-5129-958b-937819d62d47` | `S30`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d13:seat:S31` | `e6fa9e8d-efc2-556f-9a75-202aed3cb98b` | `S31`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d13:seat:S32` | `201a0222-e68f-5790-b9fe-7c45def5671b` | `S32`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d13:seat:S33` | `9bf86c82-7808-50d4-92c8-95d8f80dcfa4` | `S33`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d13:seat:S34` | `2899e352-b6b1-561c-a193-c14249b90a18` | `S34`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d13:seat:S35` | `1da03c6a-d0bb-5f04-a567-62f75d9ecf8f` | `S35`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d13:seat:S36` | `544a9be1-b911-5596-b89b-a38117bf264e` | `S36`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d13:seat:S37` | `561c6fd1-26fe-5fc3-ab00-ee7a3c1fb78a` | `S37`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d13:seat:S38` | `67b837db-36c2-5caf-9637-481b52d5f707` | `S38`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d13:seat:S39` | `1a2d27fc-4511-5a7b-ab73-3e6653f23398` | `S39`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d13:seat:S40` | `49881527-b4d6-5021-9cef-5dc5de97e9f1` | `S40`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d13:seat:S41` | `5867eb3d-7bf2-5f29-b6cd-a39c5fdbf268` | `S41`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d13:seat:S42` | `9ca86814-7e0f-5219-9b85-c187c31270ad` | `S42`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d13:seat:S43` | `3d6c68d9-8ba5-5bc3-92c1-4648112ff2b2` | `S43`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d13:seat:S44` | `922d0f6a-27fe-5b8c-8a5e-9e752febd28a` | `S44`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:a:r1:d13:seat:S45` | `c522c07a-94fd-5e04-8302-9aec7ffc45c5` | `S45`; STANDARD/AVAILABLE |
| Trip | `trip:trip:a:r2:d00` | `1d841a41-dabb-53af-a5e7-95364e5709ea` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 00 |
| TripSeat | `trip:trip:a:r2:d00:seat:V01` | `77cec037-0a83-5515-8839-8766da1134ad` | `V01`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d00:seat:V02` | `18f5fa6d-fb3d-5b55-b39e-ced73d8dfecd` | `V02`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d00:seat:V03` | `aa66b06a-0a44-5abc-b499-3e087d49a925` | `V03`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d00:seat:V04` | `5f589beb-444e-5d80-83b7-17054333ad8b` | `V04`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d00:seat:V05` | `80cd8aa0-ec26-5406-8d61-690646680258` | `V05`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d00:seat:V06` | `634b6838-8e3a-5f92-a0f9-ab666a0985af` | `V06`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d00:seat:V07` | `06ba82ec-4225-50b0-9d24-87e5723dcdb8` | `V07`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d00:seat:V08` | `d78a97dd-a0b7-5af0-acb5-354661e0fdf3` | `V08`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d00:seat:V09` | `6d5e63f8-a9b8-58d6-8488-1abe04b25707` | `V09`; VIP/AVAILABLE |
| Trip | `trip:trip:a:r2:d01` | `28e3e178-97c9-5fd6-ba40-aaa1a475bd41` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 01 |
| TripSeat | `trip:trip:a:r2:d01:seat:V01` | `e68d2437-1e73-5f5a-b0d0-5355828b13d4` | `V01`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d01:seat:V02` | `2cf75c64-f3b4-5f11-af5e-125c36ea0fe6` | `V02`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d01:seat:V03` | `0c29f975-53a2-5f92-a644-9ae727dca293` | `V03`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d01:seat:V04` | `5370d726-e971-5b72-b146-820b6aa8b110` | `V04`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d01:seat:V05` | `e9bd522c-edf8-58ad-9150-3aa7e073f203` | `V05`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d01:seat:V06` | `a8b7c445-d669-5d61-8034-cb14bec70bdb` | `V06`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d01:seat:V07` | `5b8cc5be-18ac-5e4e-947e-2bada174a735` | `V07`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d01:seat:V08` | `3a887767-ebc9-59aa-930a-ffc61e2a9245` | `V08`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d01:seat:V09` | `7868822f-43b8-5cbf-97e8-cbe5b3a00217` | `V09`; VIP/AVAILABLE |
| Trip | `trip:trip:a:r2:d02` | `5b557d07-b5b6-54bb-8d3a-644ec0bbbc0e` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 02 |
| TripSeat | `trip:trip:a:r2:d02:seat:V01` | `277cdd42-9a74-5615-8b4f-0abbc0486093` | `V01`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d02:seat:V02` | `9a01df5c-8e01-53ce-9ee3-dc01c9d28d32` | `V02`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d02:seat:V03` | `4353ef5d-8534-527e-85e9-32f69ea98aec` | `V03`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d02:seat:V04` | `2c71b0bb-5aed-5617-a600-8d3970503eae` | `V04`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d02:seat:V05` | `218e8e1d-fc8d-5a25-96bd-763297daa0ac` | `V05`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d02:seat:V06` | `d454a263-123b-5f05-ac6e-69a10815a627` | `V06`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d02:seat:V07` | `ecd1960f-3416-5d55-bb68-2c76f9edae5d` | `V07`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d02:seat:V08` | `b5bd3081-79dd-59a7-bf7d-942a72d867b6` | `V08`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d02:seat:V09` | `e51a5790-f4d1-548d-b661-5f5ea9fd2a4a` | `V09`; VIP/AVAILABLE |
| Trip | `trip:trip:a:r2:d03` | `1172ee31-b854-53ce-a9b9-545388c75c80` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 03 |
| TripSeat | `trip:trip:a:r2:d03:seat:V01` | `62b4b0cc-d5dd-5175-83b0-b6f5d21b6f77` | `V01`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d03:seat:V02` | `02637f40-38d9-5b73-a6d6-81ebe613611c` | `V02`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d03:seat:V03` | `092f82cc-f9ea-5e3b-8612-894d542778cf` | `V03`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d03:seat:V04` | `227f18f6-a41a-5590-b227-02b1ae883615` | `V04`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d03:seat:V05` | `c63cd3d8-c3f6-5030-99b5-59ee1d62ee87` | `V05`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d03:seat:V06` | `aaccd833-6c85-5055-a93e-214b5f666abd` | `V06`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d03:seat:V07` | `85814dc3-271d-5eb7-9f95-c6ba22cdde88` | `V07`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d03:seat:V08` | `10f786bb-da3a-57d0-bd2f-f107f3e032e7` | `V08`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d03:seat:V09` | `a79a8f8a-e680-5419-ba2d-9393a24ca7ce` | `V09`; VIP/AVAILABLE |
| Trip | `trip:trip:a:r2:d04` | `2e4939a7-b09f-5978-9bcd-d892570e22a0` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 04 |
| TripSeat | `trip:trip:a:r2:d04:seat:V01` | `fb856e46-9a45-5cbc-858e-ed4748b1f294` | `V01`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d04:seat:V02` | `e4368202-54b8-5e5f-9bbe-8e7c3f4b16e0` | `V02`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d04:seat:V03` | `b64647a1-6d97-5317-a495-d6b2155d87d3` | `V03`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d04:seat:V04` | `9e668eae-1cc3-58db-9384-42cdfc253291` | `V04`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d04:seat:V05` | `32f1012a-7f70-500e-868d-1e7c46e1588c` | `V05`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d04:seat:V06` | `9e62483f-7491-51f6-b60e-e14ba169f2c3` | `V06`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d04:seat:V07` | `86aaca2a-a2df-5568-99dc-271ad41b6909` | `V07`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d04:seat:V08` | `8f6a352b-02e0-599b-b0c6-fd71c7cd9f6a` | `V08`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d04:seat:V09` | `d1e82667-db3a-5e14-8d9e-bc91e58230c7` | `V09`; VIP/AVAILABLE |
| Trip | `trip:trip:a:r2:d05` | `cbc3b8c5-a21a-55a5-bfe1-61674a32adef` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 05 |
| TripSeat | `trip:trip:a:r2:d05:seat:V01` | `18d51ad8-7d60-5ec3-bcaf-7527beedb3a0` | `V01`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d05:seat:V02` | `95f33961-4a46-50e5-81da-17aa1ef9ded5` | `V02`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d05:seat:V03` | `47db6393-020a-53c3-a15b-447e9cc19258` | `V03`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d05:seat:V04` | `730408a7-8737-5443-a615-6fad00a71999` | `V04`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d05:seat:V05` | `d6a7034d-f4f8-5c3e-b14c-88fd834736a8` | `V05`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d05:seat:V06` | `9f12285f-d568-591d-8474-8372f0ee4ebe` | `V06`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d05:seat:V07` | `4588f2bd-03c4-5c3d-91a6-48317b831090` | `V07`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d05:seat:V08` | `94e48b36-6ee5-572a-9de4-d452a36a5c1a` | `V08`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d05:seat:V09` | `cfc95819-4a04-543d-b3ce-7ba440e6f0f5` | `V09`; VIP/AVAILABLE |
| Trip | `trip:trip:a:r2:d06` | `bcf02c72-9429-5a8a-a8f3-e71c98c1f872` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 06 |
| TripSeat | `trip:trip:a:r2:d06:seat:V01` | `fcf11dce-d7e7-5474-8f1a-6c2204fb5ed3` | `V01`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d06:seat:V02` | `3c8fc9f7-db57-5b75-8186-eeb0033c58a4` | `V02`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d06:seat:V03` | `bc935549-869b-5a82-a83f-fdac11913ab7` | `V03`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d06:seat:V04` | `526560f0-56c4-5045-a645-0e10359bc5b9` | `V04`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d06:seat:V05` | `4eff6893-801b-57cf-80cf-3f0d482734d6` | `V05`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d06:seat:V06` | `efc33b8c-7a84-5e89-900c-0ba04fb9c3af` | `V06`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d06:seat:V07` | `96f53fd2-717b-5fa7-a9a2-dbfae9709c74` | `V07`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d06:seat:V08` | `c40de84d-1590-593f-8d15-bdabe408133b` | `V08`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d06:seat:V09` | `e6deb012-974a-5c4e-92d9-82051e04e9e7` | `V09`; VIP/AVAILABLE |
| Trip | `trip:trip:a:r2:d07` | `29e94535-bcf3-52f7-b0f1-d935c6385727` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 07 |
| TripSeat | `trip:trip:a:r2:d07:seat:V01` | `dddc8dbd-c7a6-5ff6-a892-d90e90899c4c` | `V01`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d07:seat:V02` | `6fbd7e36-6224-5c0f-a4b5-0a72cb7d90e8` | `V02`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d07:seat:V03` | `7ffd2fb9-6551-570d-bb69-ce26f31fcce8` | `V03`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d07:seat:V04` | `6b5a1933-9f29-571d-ab09-4fcd14b076d3` | `V04`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d07:seat:V05` | `a0a739c1-10d1-524a-b094-d1551413d49f` | `V05`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d07:seat:V06` | `475e4c9e-42ab-5c89-b7a4-adf28dde698f` | `V06`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d07:seat:V07` | `adcb6265-218f-51c9-b65a-b4c28ba3622f` | `V07`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d07:seat:V08` | `38149e51-4b67-5f4c-b9b4-7d25c1d3687e` | `V08`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d07:seat:V09` | `b43bfff1-2e7b-5387-85d3-4cde0559cc63` | `V09`; VIP/AVAILABLE |
| Trip | `trip:trip:a:r2:d08` | `df1f8066-d68e-5310-b897-6521df2ef965` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 08 |
| TripSeat | `trip:trip:a:r2:d08:seat:V01` | `af80a61e-a151-5fe5-bf3c-cb61d8aa5ace` | `V01`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d08:seat:V02` | `4e2bb559-2161-56dc-a4da-15988a1c986b` | `V02`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d08:seat:V03` | `a30d3f42-5784-5925-addb-315f41d495b4` | `V03`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d08:seat:V04` | `37e019c7-a0a0-50b0-baf5-1630c1942ed5` | `V04`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d08:seat:V05` | `562bc4e1-20a1-5224-bf4d-38d2d11cb216` | `V05`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d08:seat:V06` | `23e5add5-449e-56c7-a7cf-4cb299a4147d` | `V06`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d08:seat:V07` | `6fcb2607-86e8-5063-804b-3d1a0675900d` | `V07`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d08:seat:V08` | `987f54b4-34d4-5224-b5ab-8182776d72cd` | `V08`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d08:seat:V09` | `ed4a3cfd-6d7e-57f4-936c-dbc08cbe9e3e` | `V09`; VIP/AVAILABLE |
| Trip | `trip:trip:a:r2:d09` | `bf7711a0-a231-5443-b173-932cbcbd6eeb` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 09 |
| TripSeat | `trip:trip:a:r2:d09:seat:V01` | `a6b97f6a-b0b4-5b18-bef6-8d232585c109` | `V01`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d09:seat:V02` | `0c121910-b176-5e02-a5f8-b0465d720588` | `V02`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d09:seat:V03` | `e17c022c-651f-5a3c-a07d-5d272e37ba12` | `V03`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d09:seat:V04` | `d151d6df-bf84-5860-84e5-b315a62430f6` | `V04`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d09:seat:V05` | `c421cb64-277d-5c45-9abe-f51e9934e187` | `V05`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d09:seat:V06` | `4cb57c78-e816-5ecf-83e3-e1a77b7b77df` | `V06`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d09:seat:V07` | `e27df922-6803-5d95-8352-2c134da05085` | `V07`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d09:seat:V08` | `411867dd-b0c1-5a8f-8daf-1623867f13c2` | `V08`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d09:seat:V09` | `9b064986-7cb5-5fee-9486-cf45a1c8e74a` | `V09`; VIP/AVAILABLE |
| Trip | `trip:trip:a:r2:d10` | `f441aa67-01f7-5663-83f5-2ebe3238b523` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 10 |
| TripSeat | `trip:trip:a:r2:d10:seat:V01` | `0c3b88f1-6665-57dc-b0da-9248eca412fc` | `V01`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d10:seat:V02` | `801ad415-3528-54ac-a217-193446b59beb` | `V02`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d10:seat:V03` | `43e0cab2-7781-58a0-b0c7-01d5a5abb2ef` | `V03`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d10:seat:V04` | `c923dc1c-1eb5-54ee-977f-236e3f2ff5f3` | `V04`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d10:seat:V05` | `7eb3d0b0-160a-5a10-8a18-48cf9c245b3f` | `V05`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d10:seat:V06` | `66c71e9a-8d13-5bf5-be45-f5de2d706e8e` | `V06`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d10:seat:V07` | `317c85ae-158f-5036-a715-1a24e46bc576` | `V07`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d10:seat:V08` | `b26bb226-2d63-5b66-a7ab-6ad861e10581` | `V08`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d10:seat:V09` | `c5307d08-7153-58be-8b55-10d7cc4279c8` | `V09`; VIP/AVAILABLE |
| Trip | `trip:trip:a:r2:d11` | `9a44c424-8ce2-54f6-8a26-e9d787550018` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 11 |
| TripSeat | `trip:trip:a:r2:d11:seat:V01` | `9b836a71-3ddd-5b59-a77d-ca8f398dcc70` | `V01`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d11:seat:V02` | `00d963dd-556f-5038-b163-867e1d6fe8b8` | `V02`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d11:seat:V03` | `4bf3c9ad-cad8-5c76-959f-7f82c5f032af` | `V03`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d11:seat:V04` | `fc8db8f3-63da-5c23-adf3-403caf2e0683` | `V04`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d11:seat:V05` | `c6abe394-e3eb-5c2a-99a0-44037ce6f5fc` | `V05`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d11:seat:V06` | `e178190c-996a-5ede-b678-efcd983edcd6` | `V06`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d11:seat:V07` | `546c5b78-056c-530f-a109-a3f29243e4bb` | `V07`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d11:seat:V08` | `6b82adb9-0fe5-5f3e-bb1f-d1de1c13ef35` | `V08`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d11:seat:V09` | `27ca6212-75da-5f8d-8e38-ebec18d5d23b` | `V09`; VIP/AVAILABLE |
| Trip | `trip:trip:a:r2:d12` | `1c15123a-4d41-5f42-8ed9-ed14696a0174` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 12 |
| TripSeat | `trip:trip:a:r2:d12:seat:V01` | `6462fe40-5a79-572f-8632-cdfbe6fc0b9a` | `V01`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d12:seat:V02` | `1ed01219-0cbc-5d62-9e9f-0510c28011ab` | `V02`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d12:seat:V03` | `589c2e38-6b8e-50d4-8ed1-327b46dd0f3b` | `V03`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d12:seat:V04` | `e28c72a8-7c9f-530b-af4a-9e29d57bcf7c` | `V04`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d12:seat:V05` | `ba545faa-ee79-587c-ade4-bce1c64c3cea` | `V05`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d12:seat:V06` | `51a25bb2-e94f-53ad-89db-17d22416d319` | `V06`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d12:seat:V07` | `29ba3b4c-525c-525c-b609-d9ccaccb08d5` | `V07`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d12:seat:V08` | `1b83f2aa-2b23-5ea5-8d1d-ec2e60c7d2df` | `V08`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d12:seat:V09` | `25927e1a-c534-5a19-8019-acc8c2ec9fb6` | `V09`; VIP/AVAILABLE |
| Trip | `trip:trip:a:r2:d13` | `1c7f1984-40e1-5d1c-bb2c-cdde406b2998` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 13 |
| TripSeat | `trip:trip:a:r2:d13:seat:V01` | `a959654c-dd0e-510e-b225-4634fda3d203` | `V01`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d13:seat:V02` | `75990e23-c7db-5aed-8220-157f8e99336e` | `V02`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d13:seat:V03` | `1c321022-9317-5a5d-bee2-d5d30a5e202f` | `V03`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d13:seat:V04` | `62910d6b-87da-5c07-9ac3-1b38f11b3c03` | `V04`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d13:seat:V05` | `aba0d79b-def6-5314-b4c9-e254ac8b326a` | `V05`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d13:seat:V06` | `0e3feed4-6db9-56c7-a5fe-7dc1d2691e09` | `V06`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d13:seat:V07` | `d6450e79-d2ef-54c4-b729-795351bc7b6a` | `V07`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d13:seat:V08` | `366be358-c6ca-5d8a-8c0e-9885f6fbae38` | `V08`; VIP/AVAILABLE |
| TripSeat | `trip:trip:a:r2:d13:seat:V09` | `db282dd4-61f8-5ada-a6c7-5e7832db5696` | `V09`; VIP/AVAILABLE |
| Trip | `trip:trip:a:r3:d00` | `edfa1ba9-d88f-5ea8-ae89-ac350508f866` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 00 |
| TripSeat | `trip:trip:a:r3:d00:seat:L01` | `76da87be-3d7a-52bf-a6b5-5285f0904944` | `L01`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d00:seat:L02` | `a3d2acb7-f37f-5673-8a5c-49fa22651b87` | `L02`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d00:seat:L03` | `498d1ac5-5dde-5359-9b3e-c751189f4f15` | `L03`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d00:seat:L04` | `002d817b-8e17-5eb6-995e-514cb18c76a9` | `L04`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d00:seat:L05` | `d4ae36e6-c74c-5fae-b7d9-247f888d077d` | `L05`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d00:seat:L06` | `ebdafffd-cb20-5c1b-8dc3-e8fc3c0db05f` | `L06`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d00:seat:L07` | `72ff7eff-17e0-505e-9fc3-cee319b95702` | `L07`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d00:seat:L08` | `0ca72cf5-2696-55fc-8001-292fcdce1f66` | `L08`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d00:seat:L09` | `b88c0f88-4a38-5fe6-a528-06d614e8720c` | `L09`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d00:seat:L10` | `254e43ad-030b-5b8c-8556-f85df332b4c4` | `L10`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d00:seat:L11` | `51f9c644-63f7-5b61-b2dd-f4b6943efd33` | `L11`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d00:seat:L12` | `323c2474-dbb0-5cb7-bdc0-f7af0836deff` | `L12`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d00:seat:L13` | `cc73c9d2-9740-5f98-809e-50ef56da6dbf` | `L13`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d00:seat:L14` | `22cff981-22ec-5ba0-969f-dfcebc6be785` | `L14`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d00:seat:L15` | `026a6e4c-b0bf-5f5d-9182-de83e493c109` | `L15`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d00:seat:L16` | `747548e1-822b-5cf6-b5b9-5f7a95860aab` | `L16`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d00:seat:L17` | `0493b987-3621-557f-9ec0-5f2077b99474` | `L17`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d00:seat:L18` | `661d7673-f2db-53be-a451-7cd796ed7b40` | `L18`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d00:seat:L19` | `fd31d30d-ae79-5e28-bfe3-b32ba3d4089c` | `L19`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d00:seat:L20` | `d86c0cdd-88d7-5d83-a349-902ce387eb59` | `L20`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d00:seat:U01` | `ff71b169-6cc6-5a98-a4cd-22bdea134a28` | `U01`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d00:seat:U02` | `584bd5e8-0e13-57cf-a0b6-b70af8088104` | `U02`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d00:seat:U03` | `e8a8ba78-3865-5b22-9e58-1b91e59a4539` | `U03`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d00:seat:U04` | `98fe6b26-b93a-5320-81d7-22a7e5c48996` | `U04`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d00:seat:U05` | `ec6231ce-bc3e-518e-9bb8-74ccf86fc77a` | `U05`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d00:seat:U06` | `a99a77c1-11e4-5df3-95d3-afb44492b873` | `U06`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d00:seat:U07` | `4625ce4c-5306-5821-add9-4c83310212d4` | `U07`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d00:seat:U08` | `a941bc38-6a51-5a8e-9520-4da9521ce12f` | `U08`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d00:seat:U09` | `f3899cbb-85dc-5632-9f6b-9666732e94f5` | `U09`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d00:seat:U10` | `e3ad0d50-9456-5b3a-9b42-62a2d1244c03` | `U10`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d00:seat:U11` | `060cde2d-883c-5a9b-9a38-814972ae3b78` | `U11`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d00:seat:U12` | `42f3c742-7c59-5eae-864b-c4023fdf01bb` | `U12`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d00:seat:U13` | `eb477cf2-7ba9-5b51-b3a7-364cfedae963` | `U13`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d00:seat:U14` | `2bde3887-fae5-5650-99da-6dbedc05d626` | `U14`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d00:seat:U15` | `d2248ddd-6239-5c1f-9d61-2f1dadcebe32` | `U15`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d00:seat:U16` | `5fae583c-3cd1-52de-befb-1ad459368303` | `U16`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d00:seat:U17` | `a69d5437-a6d8-5b77-951a-c4e6be3b156f` | `U17`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d00:seat:U18` | `f37e5d82-632a-566a-a9af-6083c1976260` | `U18`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d00:seat:U19` | `59179649-bb68-5a3c-946a-06940269c8a3` | `U19`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d00:seat:U20` | `f94b2aa7-3c24-5a91-bf4d-5c174a945fef` | `U20`; SLEEPER_UPPER/AVAILABLE |
| Trip | `trip:trip:a:r3:d01` | `0326c73f-3744-535d-b948-dff792950900` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 01 |
| TripSeat | `trip:trip:a:r3:d01:seat:L01` | `4f11cdfb-959d-5391-8753-ac47b746dd3f` | `L01`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d01:seat:L02` | `461064ee-8701-5e38-a986-c41dc9879508` | `L02`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d01:seat:L03` | `ba13e802-ded3-553c-ab1d-69d5e6b1dfb4` | `L03`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d01:seat:L04` | `45205548-0b5a-5181-8b07-9a9393a8cf9f` | `L04`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d01:seat:L05` | `3960c764-b066-5d8c-b894-c5ec09480415` | `L05`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d01:seat:L06` | `09c73745-b70b-507d-8d9e-e3c0f9d390fe` | `L06`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d01:seat:L07` | `3eb2ff8f-adac-5493-999d-c65f2b4e5d84` | `L07`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d01:seat:L08` | `b8d4e497-345a-51ff-8fa5-830376fef524` | `L08`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d01:seat:L09` | `3782eac1-cf12-559a-93f9-6b39fd0b7030` | `L09`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d01:seat:L10` | `7d0ebe71-61d1-55c3-947e-0fd5282a0fac` | `L10`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d01:seat:L11` | `ada98366-c3b1-5ffd-8031-5dee093c91a7` | `L11`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d01:seat:L12` | `838433a1-508f-5886-a386-6d8017751426` | `L12`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d01:seat:L13` | `80c361ef-2892-59ec-ab1d-a453f9fa9c88` | `L13`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d01:seat:L14` | `f20135dc-54f1-5af2-97fb-9c715edfdf55` | `L14`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d01:seat:L15` | `6a60acf4-d16e-5bbe-b12f-626e515c40e6` | `L15`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d01:seat:L16` | `170cf357-b6b7-5c0b-84cf-00d1fa4e5e58` | `L16`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d01:seat:L17` | `947f03fe-4751-505f-942b-404e25215f92` | `L17`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d01:seat:L18` | `59b7ec4f-8d85-57b3-831d-ea7b017c552b` | `L18`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d01:seat:L19` | `94221fe2-19b6-56e0-83fe-7ad78a89fd69` | `L19`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d01:seat:L20` | `2c8f03da-5721-50cd-9a08-b45cdb8e3fdb` | `L20`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d01:seat:U01` | `18edf9a0-7dc7-5382-80a4-b445339b69bd` | `U01`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d01:seat:U02` | `4d2b2092-61df-586c-b08a-39335705db95` | `U02`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d01:seat:U03` | `7f35fe28-ec35-5883-925f-741ee6e34ae3` | `U03`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d01:seat:U04` | `df055237-e02f-54ac-9bca-603bad9efcca` | `U04`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d01:seat:U05` | `2c569cd3-4117-5257-88fb-df9cb05e869a` | `U05`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d01:seat:U06` | `4ec73956-2ec0-5ecc-afe6-8b7912c54e12` | `U06`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d01:seat:U07` | `49847df5-490e-5089-be7a-d63eeb46bc11` | `U07`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d01:seat:U08` | `6dd5b5ad-7b27-5415-848c-6e0cea476914` | `U08`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d01:seat:U09` | `02b52c6c-d8db-5bf8-a0ca-e705d3e3a264` | `U09`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d01:seat:U10` | `10139bbc-e053-57eb-8bc6-97fd94975daa` | `U10`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d01:seat:U11` | `c8dded42-c5f8-5354-adea-6e4b387a61b8` | `U11`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d01:seat:U12` | `c1d603ab-5711-5d17-a3e8-ce1d47805ea9` | `U12`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d01:seat:U13` | `d5b4f660-0973-53f0-a7fb-f9ddbc83c37a` | `U13`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d01:seat:U14` | `1b37de6f-d9bc-5e42-a04a-4b4d35efcdcf` | `U14`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d01:seat:U15` | `523257cf-cc1c-5bb2-9a34-d15a1a3e6fac` | `U15`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d01:seat:U16` | `57d2ac81-7129-518c-bc54-204f1e51ad1f` | `U16`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d01:seat:U17` | `e2d7642e-e5a4-5638-baa9-3bfcaeca5501` | `U17`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d01:seat:U18` | `c59ce01f-5a08-5d4b-bea4-3647ccafb5c6` | `U18`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d01:seat:U19` | `3a6d0d90-15a6-5771-8742-2b6889831f01` | `U19`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d01:seat:U20` | `6652d4e5-b67d-5c06-b505-45cc009f8f44` | `U20`; SLEEPER_UPPER/AVAILABLE |
| Trip | `trip:trip:a:r3:d02` | `ced69318-4769-5f09-a14e-a94b1f276108` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 02 |
| TripSeat | `trip:trip:a:r3:d02:seat:L01` | `ab4b51d9-7d5b-59da-99a9-7e451ae20646` | `L01`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d02:seat:L02` | `78fb8c02-a80d-553b-8a0d-6841294d7a3c` | `L02`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d02:seat:L03` | `70661f48-6f62-5c7f-8c48-fcd3408185cc` | `L03`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d02:seat:L04` | `15828735-df40-59aa-a679-7b1e2ce2f321` | `L04`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d02:seat:L05` | `8a6cc60b-f466-5520-a35f-4aa91d8c561e` | `L05`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d02:seat:L06` | `7dd54de4-4fd8-5a82-8654-9ffe9b9710ec` | `L06`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d02:seat:L07` | `58b387ba-1599-53ca-95c7-7f2465dda709` | `L07`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d02:seat:L08` | `ea61c20c-ff0f-57b1-a248-f55ed20cc745` | `L08`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d02:seat:L09` | `4299964e-01a4-5130-bc80-629838c1acdc` | `L09`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d02:seat:L10` | `eb2e4fe2-33ab-5edd-b691-91e41a2a4e0c` | `L10`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d02:seat:L11` | `a47c6856-ac37-5ad4-bb6c-f26c2daddbce` | `L11`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d02:seat:L12` | `39d57610-d658-5cac-8bbb-29d8ac4cf8f7` | `L12`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d02:seat:L13` | `6f50f6dc-8579-50e4-b597-002dff000ffa` | `L13`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d02:seat:L14` | `a3880d58-0ec3-524b-bc7c-c362956acc3e` | `L14`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d02:seat:L15` | `27547198-1e57-5c69-a991-434f876ec10e` | `L15`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d02:seat:L16` | `b4927f40-5d04-50df-bf10-10060caa10eb` | `L16`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d02:seat:L17` | `7e0d34e1-8645-5631-8128-6fdf884cf2b1` | `L17`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d02:seat:L18` | `e470001e-2c28-59b6-9ca4-96211a4b090c` | `L18`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d02:seat:L19` | `4ff31ecb-cd97-5b33-953e-e5561483564e` | `L19`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d02:seat:L20` | `fe8ffbf8-0209-5943-a627-633d165049c9` | `L20`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d02:seat:U01` | `5f5552b8-230d-5a11-87c3-77a03679ca6e` | `U01`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d02:seat:U02` | `e97e7e3d-6143-5285-949c-ac08d6e9a8dd` | `U02`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d02:seat:U03` | `0bbb65e9-489a-5ef1-93c1-973c7f63de04` | `U03`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d02:seat:U04` | `8fd88c50-9ee7-53a8-8403-7ad0ff0d03a4` | `U04`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d02:seat:U05` | `3791394f-17d6-50a2-8294-1271044644d4` | `U05`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d02:seat:U06` | `ac844e3c-9895-5c85-8ddb-0c647c84686b` | `U06`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d02:seat:U07` | `b2054375-3fd3-5260-a9f6-ecb0a1a511e3` | `U07`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d02:seat:U08` | `f6c685be-2dc2-5f38-b14d-7bee09fcb17f` | `U08`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d02:seat:U09` | `e39d98e5-e962-53da-b337-00fdd6dcac0e` | `U09`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d02:seat:U10` | `dc3ca6e2-5aaa-5cd2-b4d3-2f16434bee9f` | `U10`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d02:seat:U11` | `a3a32c42-7457-53d8-be80-ba4ec43d9483` | `U11`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d02:seat:U12` | `0ab8af5b-0e1a-5e37-8300-6ee6c1dca646` | `U12`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d02:seat:U13` | `56c2f7ed-a3f6-5770-aa46-377cf1b7d6d3` | `U13`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d02:seat:U14` | `94a02009-fd6c-542e-bdd5-3449af4dd4aa` | `U14`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d02:seat:U15` | `0dfa54fb-fbf4-58e2-a38a-59df69797d5b` | `U15`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d02:seat:U16` | `dc433645-80ff-506b-a8cb-8317624be8ea` | `U16`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d02:seat:U17` | `a2a1a41d-5358-518e-8f2f-d05147c6bcd1` | `U17`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d02:seat:U18` | `a36b9e9d-a17d-54b2-be46-b36fd71be72b` | `U18`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d02:seat:U19` | `550859b1-3bc5-55dd-97a6-16661916e10c` | `U19`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d02:seat:U20` | `d8ccedd4-863a-5cf0-a3c4-f0df11c926ac` | `U20`; SLEEPER_UPPER/AVAILABLE |
| Trip | `trip:trip:a:r3:d03` | `a1621824-13b0-5068-8b78-3e670691f2bb` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 03 |
| TripSeat | `trip:trip:a:r3:d03:seat:L01` | `9e6422a5-57be-5c11-a025-97d77ed22fcd` | `L01`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d03:seat:L02` | `3e69eee3-e536-5b02-8d33-516dc2eed9b4` | `L02`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d03:seat:L03` | `be007871-f7ea-5d2f-b15f-8d63d61de75b` | `L03`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d03:seat:L04` | `4dbe0a37-ff98-59a8-a4b7-3cc254c52d47` | `L04`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d03:seat:L05` | `2d186515-26ef-5b9c-b69b-3202cac11ad6` | `L05`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d03:seat:L06` | `c5b78b00-1e02-5523-a3f0-8bb5c31b9977` | `L06`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d03:seat:L07` | `23db7c6d-946d-58f9-9bac-09d2ac5afc77` | `L07`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d03:seat:L08` | `4e96a839-81a9-5d54-986a-04e012c0cb7d` | `L08`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d03:seat:L09` | `0a3cb3e2-d8a3-5b1a-a324-e8db14ce3004` | `L09`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d03:seat:L10` | `2d04a54d-d3dc-52c9-a34f-583f8a681d5f` | `L10`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d03:seat:L11` | `2fb29a6c-484c-5b06-96e2-c38918dd3d15` | `L11`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d03:seat:L12` | `3f771d1a-5fce-5097-8008-3a3cd32dffa6` | `L12`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d03:seat:L13` | `0b5462dc-56d2-5f15-b342-9ceb13ee4050` | `L13`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d03:seat:L14` | `6af96ed5-090b-580d-936e-9e7c954c9cda` | `L14`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d03:seat:L15` | `2ce40103-bf97-5c73-8a47-59050b049e80` | `L15`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d03:seat:L16` | `e90bd761-82ba-5531-98f6-b8f8e51d5fea` | `L16`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d03:seat:L17` | `3532ba6d-7161-5948-a7bf-801290e2fbdc` | `L17`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d03:seat:L18` | `11d99f19-5102-5127-a436-05e5d0cda069` | `L18`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d03:seat:L19` | `3f353682-5ff2-55e7-80b8-27a243c8ac66` | `L19`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d03:seat:L20` | `36e86b26-7032-555e-89ea-1f402c1db29b` | `L20`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d03:seat:U01` | `248274fd-a9d1-5fd8-874f-b7267dbd4e75` | `U01`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d03:seat:U02` | `bf4f4990-8ab8-59c4-9695-3fb50ceb7bf2` | `U02`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d03:seat:U03` | `2c542bd3-0cf7-5bd8-84ea-b22cd861b33a` | `U03`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d03:seat:U04` | `51d23e24-1c61-5dc8-a8e7-b1101fa3ec1c` | `U04`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d03:seat:U05` | `9d1de740-8c4b-582b-b9fa-d35aee57ac63` | `U05`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d03:seat:U06` | `e00fe67b-57b5-57f8-b3a8-8c03a23ff1fd` | `U06`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d03:seat:U07` | `758bcfe5-1387-5224-8362-c9d40be5a126` | `U07`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d03:seat:U08` | `4bfef044-52dc-529a-9b1f-05d6d1de43a4` | `U08`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d03:seat:U09` | `8d07fff1-dbce-5f1d-86e3-d76702dee2a4` | `U09`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d03:seat:U10` | `41cf8591-2352-530e-b488-122afb966793` | `U10`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d03:seat:U11` | `64b344be-15a5-506f-81f4-5d488e96227a` | `U11`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d03:seat:U12` | `cbd65395-d81c-573a-aac1-af1c8374a959` | `U12`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d03:seat:U13` | `04832b79-a2b5-54f1-93cf-1ba324c756b9` | `U13`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d03:seat:U14` | `a42f25a3-7b48-5a98-9d52-5332abedbff5` | `U14`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d03:seat:U15` | `d5a3334d-0fd8-5fff-ad6c-da2efbd44d78` | `U15`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d03:seat:U16` | `e358d0bd-2266-5e33-9fa0-18b0d2cf4975` | `U16`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d03:seat:U17` | `bac1387a-f4a5-5439-aa2e-dd09786ce703` | `U17`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d03:seat:U18` | `8f58431a-3250-5413-b6f2-e29436333b69` | `U18`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d03:seat:U19` | `2d8a49cc-0f2e-560e-b734-25e1917d1073` | `U19`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d03:seat:U20` | `e0f26252-adaf-5efc-a059-0254c57e83cd` | `U20`; SLEEPER_UPPER/AVAILABLE |
| Trip | `trip:trip:a:r3:d04` | `65dba6b7-09bc-5cc0-a081-c395696ee4cc` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 04 |
| TripSeat | `trip:trip:a:r3:d04:seat:L01` | `e1c0d1df-da42-57e1-a1f7-88fc3410a27c` | `L01`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d04:seat:L02` | `da519194-fb35-5f77-abea-693bc4c55028` | `L02`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d04:seat:L03` | `c4413a67-536b-5a82-a6de-d1a638774ec7` | `L03`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d04:seat:L04` | `50e4695a-495d-5fd1-b8a8-7bf6bb1380f5` | `L04`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d04:seat:L05` | `eb362472-7896-5d04-be5e-6dd7bd1c1bcd` | `L05`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d04:seat:L06` | `a7efe29e-84f7-5b09-b6e2-5434838fd965` | `L06`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d04:seat:L07` | `848b22cf-7a7a-5b72-97bc-d5aef2cb7f82` | `L07`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d04:seat:L08` | `193b8e6e-ac49-5872-82d7-e34bf8671815` | `L08`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d04:seat:L09` | `0eba8177-ee2d-5efd-9022-de4f09392b3b` | `L09`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d04:seat:L10` | `52a86384-b647-5222-a85f-567a41781ef6` | `L10`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d04:seat:L11` | `32e56f8a-de0d-5601-a212-534fd946d0bf` | `L11`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d04:seat:L12` | `14e62caa-9235-5178-b207-b9ab5b269634` | `L12`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d04:seat:L13` | `becf0928-d86b-5f6a-9c4a-7893b23d1e67` | `L13`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d04:seat:L14` | `e1b686dd-c186-5ddd-85e3-558ae6ba6000` | `L14`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d04:seat:L15` | `511af879-ff00-567f-868e-abb87e9b8e9d` | `L15`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d04:seat:L16` | `36e6d13f-7517-50a0-b058-c131fa4f64ef` | `L16`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d04:seat:L17` | `3da48d71-389d-5ff3-89cd-b2c0e143e535` | `L17`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d04:seat:L18` | `01820ade-14d2-5739-81ba-efeb083a7c16` | `L18`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d04:seat:L19` | `b8d7bb4f-9aee-5542-8374-417454136913` | `L19`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d04:seat:L20` | `c0ba440f-3878-5285-a826-671ceb382ee6` | `L20`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d04:seat:U01` | `06741443-a62b-57ee-843c-b67c3ab7eb31` | `U01`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d04:seat:U02` | `7aba8dca-3586-5253-9356-585a60294c78` | `U02`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d04:seat:U03` | `91bf7ec4-128b-59b3-9e7e-bd9b2518c9ea` | `U03`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d04:seat:U04` | `da2daee2-6ff0-54ce-9586-6754141fa442` | `U04`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d04:seat:U05` | `8ac9fe41-c45e-53df-a597-22e545a1781b` | `U05`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d04:seat:U06` | `98f8d720-0bbf-55e6-9821-1035238dfdde` | `U06`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d04:seat:U07` | `ccc550be-8bd8-5461-8d4e-0a8debc89738` | `U07`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d04:seat:U08` | `4e533a9d-9fda-5db2-ac22-f0c83f86647e` | `U08`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d04:seat:U09` | `e3eb344d-1dff-5ba8-92fd-fdebb6145c4f` | `U09`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d04:seat:U10` | `21a00296-ffbd-5c13-bb1a-5801243ffdda` | `U10`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d04:seat:U11` | `8bfc53bf-e9f2-55ac-95a5-873ff4b29484` | `U11`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d04:seat:U12` | `50f73ee1-47c4-5de6-93e8-588c653366c7` | `U12`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d04:seat:U13` | `55e2b0fa-c60d-5a74-af6b-2a85110718aa` | `U13`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d04:seat:U14` | `e311838e-6c8c-5054-99ca-b1252315f505` | `U14`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d04:seat:U15` | `e9fe33a6-75be-5817-b5e4-1313e5c8ce18` | `U15`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d04:seat:U16` | `5142f451-04ff-50f3-83ad-9afdb7a4a2b2` | `U16`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d04:seat:U17` | `c6b50d30-b072-5bfc-a35e-a3ac12d7040e` | `U17`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d04:seat:U18` | `261b89ba-aa63-5968-9b9a-a7edf2c91d16` | `U18`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d04:seat:U19` | `1d4fb831-77e4-531b-9a42-73df722eac3a` | `U19`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d04:seat:U20` | `fabe7ecd-b38a-504e-af7b-b256b6d28e74` | `U20`; SLEEPER_UPPER/AVAILABLE |
| Trip | `trip:trip:a:r3:d05` | `390bf8f3-2060-5a7a-9711-a680eaf642f3` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 05 |
| TripSeat | `trip:trip:a:r3:d05:seat:L01` | `4090f513-1dc3-56f5-8576-cabbb883e678` | `L01`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d05:seat:L02` | `b4e8a53c-bf73-5939-9509-b548489f12ab` | `L02`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d05:seat:L03` | `78d507cd-7acd-5fbe-ba8c-52ab664293ad` | `L03`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d05:seat:L04` | `f7d1a015-9826-584a-96ad-83669589cd00` | `L04`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d05:seat:L05` | `83fc8da8-8b3a-58ea-958f-68114a5aecf5` | `L05`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d05:seat:L06` | `464ff95b-d5ef-59b7-a273-51c8acc4f66c` | `L06`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d05:seat:L07` | `e2aa9197-894f-5a1b-96e8-9dc17cdf3cfb` | `L07`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d05:seat:L08` | `f0a2010d-d4f7-5e24-b49e-fbca8d7bbd7a` | `L08`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d05:seat:L09` | `52af3fa6-6854-582f-9105-338dac6d7761` | `L09`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d05:seat:L10` | `2f3a0a51-2f68-59f1-a7ec-5057834d3b16` | `L10`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d05:seat:L11` | `4bc906cd-9b0d-56cf-9a75-29e6162dd8da` | `L11`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d05:seat:L12` | `ada1c734-bb37-5154-b642-55223d5862fc` | `L12`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d05:seat:L13` | `14e21510-1e69-5595-b5d1-d2147d7eb350` | `L13`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d05:seat:L14` | `ff58fe2f-6991-59e6-ac6b-edd83f6f7935` | `L14`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d05:seat:L15` | `96758e0a-742b-5a69-ba76-564ece439643` | `L15`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d05:seat:L16` | `cc4bb121-3140-5965-9907-15323cc6be43` | `L16`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d05:seat:L17` | `4bff813d-8ce1-537f-bd31-853dc325eef5` | `L17`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d05:seat:L18` | `1f92ae89-9b56-54a3-859e-4f227342b96c` | `L18`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d05:seat:L19` | `9bc1a3fe-98f9-5e7e-88be-423a73a5318f` | `L19`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d05:seat:L20` | `7f6caa17-13ee-5042-bc56-292e46f38982` | `L20`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d05:seat:U01` | `f7d8a3c7-8dc3-53ed-b699-e7b37da0306d` | `U01`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d05:seat:U02` | `1a30496b-3984-57a7-a21b-1eae674d17f9` | `U02`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d05:seat:U03` | `b1671add-fe1a-5a4b-9d9b-02a5c6dce93b` | `U03`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d05:seat:U04` | `31e006f9-d5fd-555b-a4ce-9bea46d2aec6` | `U04`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d05:seat:U05` | `320bdbf1-887f-592e-a1dd-d77117fe1505` | `U05`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d05:seat:U06` | `38c8c72f-9b3a-54b8-9304-8c04c4a07c15` | `U06`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d05:seat:U07` | `e417efbc-057b-5419-a46f-9c280b186a7c` | `U07`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d05:seat:U08` | `37f9456b-3617-5d1b-adf8-3427247e26f5` | `U08`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d05:seat:U09` | `76e45267-baca-5d34-b48c-05eb741dba94` | `U09`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d05:seat:U10` | `49d61e8e-a7c8-56ee-9d9f-5ddf6aea8567` | `U10`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d05:seat:U11` | `1cebde26-0ff9-5df8-9e6d-86d82c94be3e` | `U11`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d05:seat:U12` | `fb9acb9c-7f05-5a00-9fb8-fe9739e9f7aa` | `U12`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d05:seat:U13` | `dc94f670-cfca-58b0-b9be-328b24ed1cc1` | `U13`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d05:seat:U14` | `34bc7937-8195-58cc-9a6a-ed6b7b985bfe` | `U14`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d05:seat:U15` | `39bca377-84e9-56cc-b586-1baae583ae46` | `U15`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d05:seat:U16` | `9ceadaa0-3247-5f50-9f11-7b9283fc20a5` | `U16`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d05:seat:U17` | `b205f320-ef9f-5f54-87e9-d5f826efcd74` | `U17`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d05:seat:U18` | `fb091c88-fac4-55dc-8efa-4b66e9efd03c` | `U18`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d05:seat:U19` | `7217f755-81ba-5952-8a75-658d01dc3b3c` | `U19`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d05:seat:U20` | `780cb915-7f4c-5819-bb09-79576dd5fc9a` | `U20`; SLEEPER_UPPER/AVAILABLE |
| Trip | `trip:trip:a:r3:d06` | `efadb172-0ce7-576e-ba0f-01380faa12c7` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 06 |
| TripSeat | `trip:trip:a:r3:d06:seat:L01` | `30ddc99b-ca04-5785-9d25-7b7b9bbed98a` | `L01`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d06:seat:L02` | `0d332666-a5df-50c8-8f49-e9d115eb03f0` | `L02`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d06:seat:L03` | `a3c51ad5-ce12-599b-800a-857f5fc83abf` | `L03`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d06:seat:L04` | `1c7a518d-30b8-590d-bef0-363a9c0f2762` | `L04`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d06:seat:L05` | `ee63b006-1dda-5e4b-9d1c-5c91d8569905` | `L05`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d06:seat:L06` | `ff5a341d-38eb-5df3-8598-e85d16a110a9` | `L06`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d06:seat:L07` | `ca79d567-231f-5fce-ab5a-12472053dbf4` | `L07`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d06:seat:L08` | `be267d82-e57a-5723-a8e5-18ebccd3dc8d` | `L08`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d06:seat:L09` | `108334bc-6dc1-558e-9ea3-6d399f322c9f` | `L09`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d06:seat:L10` | `a2b8f8f0-ce2d-536f-aeb4-a22f2340580f` | `L10`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d06:seat:L11` | `f25599eb-f256-58b5-afd7-6d47a5e1e60f` | `L11`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d06:seat:L12` | `17b71510-f11f-5d3b-8fe1-110d5bd6cb8c` | `L12`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d06:seat:L13` | `13e2bd53-1e2a-5137-90b9-b6bdffe0ab26` | `L13`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d06:seat:L14` | `7c3b1b46-43fe-5846-b279-c797c24b0093` | `L14`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d06:seat:L15` | `3675d3e5-9943-5962-8056-f3a558041536` | `L15`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d06:seat:L16` | `20384101-2890-52fc-87a0-4e1333f17849` | `L16`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d06:seat:L17` | `5393dbbf-ad84-59b6-9626-c9b65500f98b` | `L17`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d06:seat:L18` | `0648b40f-3d9d-5ea2-85c4-728c93ddaa3b` | `L18`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d06:seat:L19` | `0b526e61-f2a6-565a-8480-c5a24ef0e4a3` | `L19`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d06:seat:L20` | `c1613adc-2502-5ccf-a642-9d9851c114a6` | `L20`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d06:seat:U01` | `652129ae-3d95-5725-9130-e5a46cf7a6fa` | `U01`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d06:seat:U02` | `8fb5128b-d4f4-582a-a85e-dff8d1b1bccf` | `U02`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d06:seat:U03` | `d4686277-8ff3-5195-901f-43a9a96fb547` | `U03`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d06:seat:U04` | `8b369221-be26-5754-80d1-8b7c2d8766cf` | `U04`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d06:seat:U05` | `597121f3-fbb5-561b-b7bb-0c9af236aff8` | `U05`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d06:seat:U06` | `2e654623-b2d8-5f12-8c05-b24827fe3efc` | `U06`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d06:seat:U07` | `5f8f4fc2-fde4-5f11-8fce-760ce8bdf276` | `U07`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d06:seat:U08` | `9ecd10e5-8bd9-55e2-be18-0b5ab2799d70` | `U08`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d06:seat:U09` | `fda9fabc-6833-585f-85c5-164e465db221` | `U09`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d06:seat:U10` | `918fe312-5113-57df-a9ae-11019944c9d0` | `U10`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d06:seat:U11` | `34cb68c8-313c-5461-b300-611f696c413c` | `U11`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d06:seat:U12` | `3320539c-7a2c-596f-a5e3-9f29c48467ea` | `U12`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d06:seat:U13` | `56660796-4d57-57b3-bc85-c792a14fb19e` | `U13`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d06:seat:U14` | `28f810b7-7b5e-5392-bb96-acd4112cd613` | `U14`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d06:seat:U15` | `d3752631-24e2-5b2a-b8b2-5c1106ea4a77` | `U15`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d06:seat:U16` | `6afc6e5c-e1eb-5644-8b84-781cb7b3b543` | `U16`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d06:seat:U17` | `646927c3-e173-5528-8f66-8bd82c78142c` | `U17`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d06:seat:U18` | `66a7059a-f457-53ea-83cc-2e6287ce15ef` | `U18`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d06:seat:U19` | `987385eb-0694-5360-90a1-f6f8f1616b3f` | `U19`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d06:seat:U20` | `220b0e1f-bd39-5459-b006-2997bcfdb9a3` | `U20`; SLEEPER_UPPER/AVAILABLE |
| Trip | `trip:trip:a:r3:d07` | `5cc30e8f-0f19-5ed8-aab7-7e876620cd3f` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 07 |
| TripSeat | `trip:trip:a:r3:d07:seat:L01` | `819afc6a-3739-5639-9dba-9ebb85dcf09b` | `L01`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d07:seat:L02` | `20bb53e8-ef46-5831-a71d-d674b7a3a547` | `L02`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d07:seat:L03` | `f7725f5a-f1a2-538d-a3ef-b347e029c590` | `L03`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d07:seat:L04` | `6158d93b-7eff-50ad-95ad-81bd23bd908a` | `L04`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d07:seat:L05` | `47427b0e-6802-5049-a4ef-df6aad9bcab8` | `L05`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d07:seat:L06` | `39484968-b616-50a8-ac03-c326a38c828e` | `L06`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d07:seat:L07` | `d7b834b4-ce30-56a2-8906-127d05762c4b` | `L07`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d07:seat:L08` | `557b9f6e-14a9-56a8-9e1c-42aa91218bd5` | `L08`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d07:seat:L09` | `de9ff2af-8d62-5197-998b-c7ed372cd21d` | `L09`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d07:seat:L10` | `fffe91bd-01cc-5c3c-b84d-37dd56945f65` | `L10`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d07:seat:L11` | `35e94638-af9d-5c7e-afd9-79dfe2fc8124` | `L11`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d07:seat:L12` | `eaa7e6df-0deb-5240-b9f9-66364ff7882d` | `L12`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d07:seat:L13` | `2d141db0-a261-50c5-a0f5-78180a110bda` | `L13`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d07:seat:L14` | `6fc17917-e5e8-5a5f-8cbe-5f88f5f39919` | `L14`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d07:seat:L15` | `8783a2fb-0b30-58f2-b4e0-0d8b9c5df823` | `L15`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d07:seat:L16` | `8c0db4f7-afd1-5cbd-a32a-cab0dbafcc6d` | `L16`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d07:seat:L17` | `08316c40-0c27-526a-b865-84e5a284ffe7` | `L17`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d07:seat:L18` | `93a16191-5316-51e6-a2d9-11bb0b58a2d7` | `L18`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d07:seat:L19` | `d336f79b-0eb7-578a-a7c4-3f9912fcee7d` | `L19`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d07:seat:L20` | `f787357f-86a9-52e1-aab1-884e55b480b4` | `L20`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d07:seat:U01` | `949bc977-d626-51db-bdfd-07105bbf4d39` | `U01`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d07:seat:U02` | `79a54cd7-4786-5ef9-9786-44ee5fe625d4` | `U02`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d07:seat:U03` | `6d5d3c72-6682-58c4-870b-30ba87e24187` | `U03`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d07:seat:U04` | `e4e3be6d-ce37-5e3f-ac0d-cae8d7cc7f2b` | `U04`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d07:seat:U05` | `fb7ba46a-b49d-531e-9005-9fc7ca76eb00` | `U05`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d07:seat:U06` | `e75f8a70-0803-56d3-bc95-d0d692eda4b2` | `U06`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d07:seat:U07` | `9c88ec2a-074a-5a64-b892-ce774ec93f72` | `U07`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d07:seat:U08` | `ff538bc9-72e1-5127-9761-eac65343677c` | `U08`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d07:seat:U09` | `02a9cff2-6553-5e83-8290-5eee7d345e1b` | `U09`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d07:seat:U10` | `8ecc8841-98a3-551b-9d2f-7f5e8a232b9f` | `U10`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d07:seat:U11` | `5057b8c8-ce81-5936-8819-a003921d12e3` | `U11`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d07:seat:U12` | `a16363cf-983c-5d23-b690-46322793d13b` | `U12`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d07:seat:U13` | `f336008a-14f9-5eb4-992b-bc2d590d3b93` | `U13`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d07:seat:U14` | `0f859de3-b8e3-5d7b-9d72-247e80bfc971` | `U14`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d07:seat:U15` | `29409b6e-9758-55af-ba84-ab53639f608b` | `U15`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d07:seat:U16` | `881b57bb-7f48-5c79-9d55-d8b22e12d6d6` | `U16`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d07:seat:U17` | `850d74f2-e3f8-5d2b-8a13-850a3647b71b` | `U17`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d07:seat:U18` | `1e9b71f4-cdb8-5ae0-88ba-725ce7f58a94` | `U18`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d07:seat:U19` | `c7db17f2-c556-5a4b-b6c7-00558ca9101a` | `U19`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d07:seat:U20` | `8d126cdb-0f81-589c-8eba-afe679770266` | `U20`; SLEEPER_UPPER/AVAILABLE |
| Trip | `trip:trip:a:r3:d08` | `b3ce59ec-e680-5be2-b02d-80900f8e6133` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 08 |
| TripSeat | `trip:trip:a:r3:d08:seat:L01` | `b78f88f9-d7a0-5407-98ee-2bb285d031fd` | `L01`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d08:seat:L02` | `ec1ba9b0-d946-5c7d-ab62-cb61f47953d7` | `L02`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d08:seat:L03` | `acd90aa5-db3b-5313-bc03-322b8ee9f94e` | `L03`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d08:seat:L04` | `aaae6f89-8aa1-59a0-aac1-f0f64ea47fe3` | `L04`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d08:seat:L05` | `158e6856-c8f8-5de5-861b-0f6308997ea8` | `L05`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d08:seat:L06` | `490f57ae-fc5b-5dfa-9cbb-c741a8707418` | `L06`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d08:seat:L07` | `e4ab651b-90e3-5396-8c22-38d0c99e9c12` | `L07`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d08:seat:L08` | `97bb2059-733e-53d1-852e-49a3b8a8c2b1` | `L08`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d08:seat:L09` | `4dae9a25-7bf9-51d5-abcb-8c342b57bf07` | `L09`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d08:seat:L10` | `9a04fb5b-3933-5e62-894d-e343bc76d524` | `L10`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d08:seat:L11` | `b3589d6f-c581-5edf-9dec-77b804de85ff` | `L11`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d08:seat:L12` | `e911cd6c-fd34-50fb-8e92-2762077d91c9` | `L12`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d08:seat:L13` | `921093ff-194e-5170-b022-a8a73c926ab7` | `L13`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d08:seat:L14` | `1bc5e0f4-4696-515a-9f9c-9f687cbf8c90` | `L14`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d08:seat:L15` | `a9b75d0e-0db6-50a1-a23e-5033bf582864` | `L15`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d08:seat:L16` | `a28acc14-e07b-574b-86d9-66a2a67b00f3` | `L16`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d08:seat:L17` | `1f4456bd-4c8f-549e-96e8-50eaaacd1255` | `L17`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d08:seat:L18` | `3ae25696-042f-5890-82f7-b7a5657e2ad6` | `L18`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d08:seat:L19` | `1233978d-05cf-516a-87d9-d65813449cba` | `L19`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d08:seat:L20` | `01f3d12e-23b6-5064-81f2-c95f619fe1ef` | `L20`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d08:seat:U01` | `51d6726b-7225-528a-b327-01d8184fb662` | `U01`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d08:seat:U02` | `3679e97f-85de-507e-8d48-1ea95521f23c` | `U02`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d08:seat:U03` | `04db24bb-db73-510a-b3f3-8ec3ae10a921` | `U03`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d08:seat:U04` | `de5ceab7-044c-50bc-b186-8fe60b318ce9` | `U04`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d08:seat:U05` | `3a46fdb4-bf1a-549b-b647-dfc4dd189f52` | `U05`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d08:seat:U06` | `17c22a1d-c329-5457-b58c-5e5a1beadbe5` | `U06`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d08:seat:U07` | `4961d021-7ea8-5df9-9f4c-ff731114fc91` | `U07`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d08:seat:U08` | `9dae000e-7548-5c6f-9def-1f47a37180ef` | `U08`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d08:seat:U09` | `cb6e8b06-e1a5-591b-88d3-905c8ebb49a6` | `U09`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d08:seat:U10` | `40c93939-43e0-5811-81ff-a1d3b0ac8807` | `U10`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d08:seat:U11` | `03134587-9a1b-5512-ad69-0ac1133ccf22` | `U11`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d08:seat:U12` | `57ac815c-f34a-57ab-b9bf-2f5e451e1335` | `U12`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d08:seat:U13` | `3526dacb-9c63-5441-8f0d-4b1212bc6242` | `U13`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d08:seat:U14` | `92d24f09-2dc0-5757-a6cb-9ff920e9c66e` | `U14`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d08:seat:U15` | `c755aeda-fa32-5f7a-ba3e-683eb7e20736` | `U15`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d08:seat:U16` | `ae0671be-d880-55f2-861f-11f02a349c27` | `U16`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d08:seat:U17` | `0a27d989-cbba-5dad-9235-8901750f4bf1` | `U17`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d08:seat:U18` | `8239dbaf-ea68-5bb3-bbf8-1a15fad8c059` | `U18`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d08:seat:U19` | `92a0e633-c0be-56ac-8255-ee1c815e1bf0` | `U19`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d08:seat:U20` | `63ac2de0-2866-55ad-868c-7f9cfe49720a` | `U20`; SLEEPER_UPPER/AVAILABLE |
| Trip | `trip:trip:a:r3:d09` | `a3fb999b-d938-5f53-a3a6-8811d3c21aba` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 09 |
| TripSeat | `trip:trip:a:r3:d09:seat:L01` | `9a66e1dc-7f3f-5030-99b5-715ed78b60b7` | `L01`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d09:seat:L02` | `709113ad-b9d9-5a4b-8ea6-ba460d64a565` | `L02`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d09:seat:L03` | `d54d4372-f03e-54fd-8c26-bc55dc66ac96` | `L03`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d09:seat:L04` | `81a959b9-3898-523d-b013-9c4bc3a48b50` | `L04`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d09:seat:L05` | `e337510b-8e31-5878-9969-fb3ad36141df` | `L05`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d09:seat:L06` | `1f3ca6d7-ea77-5fe2-bbcd-b0eae02cde8c` | `L06`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d09:seat:L07` | `b319324d-9b04-507a-961c-596186ca17aa` | `L07`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d09:seat:L08` | `fab74d73-2fc8-542d-bbf0-ba781a74f02f` | `L08`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d09:seat:L09` | `2b7fdb1b-1311-5f41-ad8c-60091f8a34e0` | `L09`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d09:seat:L10` | `a7bc6132-0432-5ed9-a8ba-5e5e599c4b49` | `L10`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d09:seat:L11` | `6701872d-9748-5204-a042-2d285aca032e` | `L11`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d09:seat:L12` | `480f02a6-d4ed-588c-85ce-b4c519b1a97e` | `L12`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d09:seat:L13` | `0219d871-730f-57d5-9cf7-3968c23587a9` | `L13`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d09:seat:L14` | `689cfc75-763e-5aab-8a8f-68ed0a526d07` | `L14`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d09:seat:L15` | `98bb0e2f-9020-51c4-bfa5-2f7f3492ad01` | `L15`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d09:seat:L16` | `b8d039d9-5e59-5eb2-af6f-c582ab90ee0c` | `L16`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d09:seat:L17` | `496c83ae-11ce-590f-b4e0-b73b9cf381fd` | `L17`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d09:seat:L18` | `8f511cac-b5cd-584c-9d02-ea69e1699376` | `L18`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d09:seat:L19` | `81ecb0d9-ed41-5666-b58a-55827b6bedf8` | `L19`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d09:seat:L20` | `82d1c0c8-e287-58ba-a697-22169ddbab7e` | `L20`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d09:seat:U01` | `bae3223a-0678-55ac-b65a-6cb464acad0d` | `U01`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d09:seat:U02` | `25c9cc64-41df-568e-8f21-841de5e0379e` | `U02`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d09:seat:U03` | `54abd91e-96e7-5992-9074-38bc7f58d661` | `U03`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d09:seat:U04` | `772e4a6f-ea22-56f7-8e1a-a495241a42d2` | `U04`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d09:seat:U05` | `a945cdc1-36da-566a-a48d-c850b5f9604d` | `U05`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d09:seat:U06` | `9c6f41b0-1a0d-5703-a43b-e9f8be115d6c` | `U06`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d09:seat:U07` | `e99caf5b-3586-59bc-bca0-394ff8df3ef3` | `U07`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d09:seat:U08` | `d2ef7f03-90f5-5200-8427-7ab9f4cc2b4c` | `U08`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d09:seat:U09` | `cd091295-71f1-5946-a2e9-06ab15155185` | `U09`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d09:seat:U10` | `c2cd0371-9778-5386-a31c-33309af03cbe` | `U10`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d09:seat:U11` | `984d49b8-16a6-55ac-ba7f-9e9e366d9210` | `U11`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d09:seat:U12` | `7731a32a-4fff-5f91-b2f7-27d36f60cd03` | `U12`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d09:seat:U13` | `6e05ce33-ade6-5dd4-af2c-ae8aaabc2b1f` | `U13`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d09:seat:U14` | `3c506a6b-196d-51d6-a66d-9ff8f8635066` | `U14`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d09:seat:U15` | `0c4d368c-8af0-5cc8-a114-a118032943c5` | `U15`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d09:seat:U16` | `f898fc52-be46-5019-b8ca-ee256adeafd0` | `U16`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d09:seat:U17` | `75e57a70-7973-53f2-9821-157876a74811` | `U17`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d09:seat:U18` | `da674652-9cb5-5820-8f2f-1a55d57b7085` | `U18`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d09:seat:U19` | `6434bb69-1b06-5993-b149-75bf97e2bc7b` | `U19`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d09:seat:U20` | `9d35e765-be28-508c-92b6-f00d08cdfb12` | `U20`; SLEEPER_UPPER/AVAILABLE |
| Trip | `trip:trip:a:r3:d10` | `bac62ea4-8671-5f19-9ac2-0d844d24f939` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 10 |
| TripSeat | `trip:trip:a:r3:d10:seat:L01` | `fad312ef-0b35-5b58-852d-663203253675` | `L01`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d10:seat:L02` | `f0112e7f-b9f6-5261-be13-f514695de2fb` | `L02`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d10:seat:L03` | `4905777c-e859-5cc5-953c-65d000765325` | `L03`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d10:seat:L04` | `58978106-541e-5db4-a234-cd5d4505b83f` | `L04`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d10:seat:L05` | `20ece1f4-ac51-5548-8f88-01d3ff526173` | `L05`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d10:seat:L06` | `1d2250e9-e836-547e-836b-34d0e767f11d` | `L06`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d10:seat:L07` | `1b08a360-c200-5e5b-bef9-3c0964eebaf9` | `L07`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d10:seat:L08` | `38b7362c-aeaa-58c4-8733-39072822f065` | `L08`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d10:seat:L09` | `a92abb18-69b6-534b-92b5-401ca7050214` | `L09`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d10:seat:L10` | `7bf07d3f-74c5-55c3-ac74-aa9af006a178` | `L10`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d10:seat:L11` | `3a905949-d418-5c54-89a4-bd85f6bb50e7` | `L11`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d10:seat:L12` | `0cf87a46-241c-592f-8c40-1ddc65a72d1d` | `L12`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d10:seat:L13` | `d498bc83-3914-5f7c-8baa-e860177539cd` | `L13`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d10:seat:L14` | `908f80c4-d4a5-584a-9686-94d6ab9ba4fa` | `L14`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d10:seat:L15` | `03aff181-31a1-5e9f-8d32-9d55c01c3033` | `L15`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d10:seat:L16` | `25ed1eb3-fc1b-5d1e-933a-574a27752de3` | `L16`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d10:seat:L17` | `ca2d0761-f2b4-5b12-8a76-f5fa1c45d296` | `L17`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d10:seat:L18` | `43654e39-2c34-5a20-8b11-c800755c9617` | `L18`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d10:seat:L19` | `efd170ab-0937-53b4-8afc-f35267f0cf55` | `L19`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d10:seat:L20` | `251768a0-d379-5880-b0e8-50cf8ef5e6af` | `L20`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d10:seat:U01` | `1037e986-3130-5f8d-b2da-b62ac1860069` | `U01`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d10:seat:U02` | `26e88105-60a8-54c2-982c-6fb006d7d05c` | `U02`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d10:seat:U03` | `85d1cdb7-ab75-58d3-9d6b-e499a8119ecd` | `U03`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d10:seat:U04` | `f52178ae-e8b0-5e3f-af1c-dcad3f205a5f` | `U04`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d10:seat:U05` | `3f50a843-60ea-5bfd-b45c-e19601d18c6e` | `U05`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d10:seat:U06` | `da86a71c-761e-55b2-85b4-734f66839fec` | `U06`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d10:seat:U07` | `bb3ce04b-58d2-5657-9461-019c05afa0af` | `U07`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d10:seat:U08` | `b12085fe-6cb2-59dc-8488-2e82c94b1ce7` | `U08`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d10:seat:U09` | `8ae101cd-0589-5f68-a915-03119e0db9a4` | `U09`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d10:seat:U10` | `c5c144be-7265-5db9-ad26-73930c1059b0` | `U10`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d10:seat:U11` | `2f6dbd57-6584-5623-ac42-50ccad7f2708` | `U11`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d10:seat:U12` | `205259f6-b4a3-5ad0-a57c-23e645a45a88` | `U12`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d10:seat:U13` | `9e3bdfd9-652c-53e0-8282-6c0ba04cbf1b` | `U13`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d10:seat:U14` | `c3fe6057-483f-5000-8d55-964ddf25dc6c` | `U14`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d10:seat:U15` | `197ff391-e606-5d22-bc62-94524a62dca8` | `U15`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d10:seat:U16` | `f63a6b09-c61d-52ee-9020-4b8080bf6a46` | `U16`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d10:seat:U17` | `e6360132-17c8-5eb2-b46d-a14c1a668fdf` | `U17`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d10:seat:U18` | `80dcf4d4-661c-51d9-adca-2dd4a2d79d97` | `U18`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d10:seat:U19` | `41f3138b-3953-5abe-8de4-4f818acf959d` | `U19`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d10:seat:U20` | `8c61fa3e-5586-52e5-b567-e73ba0d7bc47` | `U20`; SLEEPER_UPPER/AVAILABLE |
| Trip | `trip:trip:a:r3:d11` | `9cac0fde-c243-5850-ae65-a7a946f7076b` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 11 |
| TripSeat | `trip:trip:a:r3:d11:seat:L01` | `dc47b6b9-2407-57fd-86ed-99f23504d9d0` | `L01`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d11:seat:L02` | `df94392b-9693-533e-8cbf-1fe5b0ee1061` | `L02`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d11:seat:L03` | `d519d05d-e06e-599e-ba8d-0afdfef1b101` | `L03`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d11:seat:L04` | `2b13ad1e-9eda-5136-8c14-b226aaf370eb` | `L04`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d11:seat:L05` | `c17f54dd-0329-5d8f-961f-369db69d92ec` | `L05`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d11:seat:L06` | `7a3cc22b-93be-5602-9b81-f7c2d7b2b736` | `L06`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d11:seat:L07` | `692446ef-850d-5d69-96e1-b6c19796ac5b` | `L07`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d11:seat:L08` | `20637493-de3a-524d-9bf6-1afadc68b717` | `L08`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d11:seat:L09` | `58593b98-6dcc-5129-b5ff-b823d5cb31c1` | `L09`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d11:seat:L10` | `c2f9af35-3cf0-5a79-9912-2c3daa2a0ca6` | `L10`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d11:seat:L11` | `52154b10-abbb-52e4-96d2-83d318fd20b5` | `L11`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d11:seat:L12` | `62e2aaed-7cf6-530f-bffb-f5980d1c2091` | `L12`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d11:seat:L13` | `1a2c42b1-689b-5244-bb86-e49722f7b0fe` | `L13`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d11:seat:L14` | `88cead1b-0511-5415-b272-c3ad2336612f` | `L14`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d11:seat:L15` | `0bdba3d8-f909-5da0-af27-1cde1361932a` | `L15`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d11:seat:L16` | `8c91190b-6ca3-50fd-ab07-3b9e18d76768` | `L16`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d11:seat:L17` | `df8557ed-b972-5640-9bf4-d4f5dfae6f10` | `L17`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d11:seat:L18` | `76844fb2-57de-5b1b-bdc3-23c137b7f5c9` | `L18`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d11:seat:L19` | `806b81c7-99d7-5f9d-8655-f4b62a02ffb1` | `L19`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d11:seat:L20` | `5335b396-4948-5c48-be29-8989818d212b` | `L20`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d11:seat:U01` | `e4662c28-b8fc-551d-90ae-beb3717ba148` | `U01`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d11:seat:U02` | `d02effd3-db4a-587f-b922-5f2c0bc117c0` | `U02`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d11:seat:U03` | `89ff688a-c85b-5599-852f-5aaff1c0e8be` | `U03`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d11:seat:U04` | `9ad04108-add3-5dab-9d13-772136bfdd41` | `U04`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d11:seat:U05` | `928751c1-28d5-5929-8ae4-5c0cec7e246b` | `U05`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d11:seat:U06` | `27bccdf2-1367-5a16-be44-7ff60de0e036` | `U06`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d11:seat:U07` | `be6fa60f-10ba-59a7-b56c-4e51481fc5e3` | `U07`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d11:seat:U08` | `2b185ffc-4906-5444-9971-429b236238da` | `U08`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d11:seat:U09` | `f9128eea-13dc-591e-b231-d050bfdfd21c` | `U09`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d11:seat:U10` | `e5385a23-3e34-5467-82f6-c965da8a00f4` | `U10`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d11:seat:U11` | `302131a1-a5f9-5089-8fbe-7563063cd96b` | `U11`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d11:seat:U12` | `882688c7-d891-5c8f-9f04-a252597ab967` | `U12`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d11:seat:U13` | `448f6c39-c2d6-5836-b711-c6000eadf66f` | `U13`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d11:seat:U14` | `23321145-81a9-5480-a7fc-737e47b6e944` | `U14`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d11:seat:U15` | `a9a7a9c6-7b52-5f7b-bd62-61d6f1caca84` | `U15`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d11:seat:U16` | `435e34e4-1f25-51dd-af86-a07e1d32c1ab` | `U16`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d11:seat:U17` | `0ef91a75-9de4-51d0-8d37-826f46d4a627` | `U17`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d11:seat:U18` | `9d22495a-5bea-5cf1-92ba-c56478b8a255` | `U18`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d11:seat:U19` | `29ebd330-236f-5dac-8587-5666a4b885f5` | `U19`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d11:seat:U20` | `c8a208b4-f23b-54a6-93ba-59dc109b43bd` | `U20`; SLEEPER_UPPER/AVAILABLE |
| Trip | `trip:trip:a:r3:d12` | `1cd9eb58-5d28-5e87-baed-54cf9c5d5c25` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 12 |
| TripSeat | `trip:trip:a:r3:d12:seat:L01` | `5604eaad-b62a-50d2-8e20-d7a5ad2136ed` | `L01`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d12:seat:L02` | `12d04a88-4c3a-5ed6-84c0-1b67ff8e12f7` | `L02`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d12:seat:L03` | `6b09a19a-7d39-51ed-83e6-de832baaec16` | `L03`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d12:seat:L04` | `ce16150a-aeb1-5ec9-aece-9597534d564c` | `L04`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d12:seat:L05` | `bb5b07a4-e8df-56a9-8428-933bcb00c83d` | `L05`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d12:seat:L06` | `312a58e3-c44b-5095-9cdb-cebd28eae519` | `L06`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d12:seat:L07` | `a2fa2007-e0c2-5233-9eec-e9e545cce799` | `L07`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d12:seat:L08` | `42708147-7616-5392-988f-c8a09c680a80` | `L08`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d12:seat:L09` | `a46d3e52-55a1-5933-a742-03836bdb20f3` | `L09`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d12:seat:L10` | `ff0ce82f-9df8-51c0-8c2d-6ec16ebce221` | `L10`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d12:seat:L11` | `11a570d7-6428-53bf-a4e7-186c04428168` | `L11`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d12:seat:L12` | `f1457e69-99c7-583a-b6e4-dd07bf757045` | `L12`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d12:seat:L13` | `60e41e79-756c-57b8-b375-284a134a12ac` | `L13`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d12:seat:L14` | `1dc3ac6c-7df6-5771-badc-cbf3c7813e7c` | `L14`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d12:seat:L15` | `2db7dd9a-448a-52bb-8631-4d6c99be7ec3` | `L15`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d12:seat:L16` | `876999d7-5a01-5817-b64a-827af28ada98` | `L16`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d12:seat:L17` | `39dc5aaa-83d9-5734-9ce9-6715690ab735` | `L17`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d12:seat:L18` | `893d2837-f87c-5a87-ba3c-98407f9c6e1b` | `L18`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d12:seat:L19` | `ef451e4b-b537-5ee7-9974-81e4e63145ea` | `L19`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d12:seat:L20` | `5b90fd49-1f60-5bfb-ae48-48229507f7db` | `L20`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d12:seat:U01` | `3cdf60c4-23c2-5dc1-8385-fc1a73421001` | `U01`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d12:seat:U02` | `544d8ab2-21b2-568f-a05c-749ef76c3dbe` | `U02`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d12:seat:U03` | `e09397b3-f16e-5be5-85b4-6ef798c46e4d` | `U03`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d12:seat:U04` | `120adfbe-7a1e-591a-ba8f-96aeaf5515a7` | `U04`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d12:seat:U05` | `c47290dd-dc64-5b56-8eff-70e699c2a154` | `U05`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d12:seat:U06` | `2a6bce13-1f2c-50ae-81a7-65696c315a88` | `U06`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d12:seat:U07` | `95b67d24-fc5f-54c7-92e5-fe8e1ae9a030` | `U07`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d12:seat:U08` | `d7649c4a-c7dc-566d-ac07-3bcb9b94107d` | `U08`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d12:seat:U09` | `96e17b5b-042b-5210-b4ab-515c379233f1` | `U09`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d12:seat:U10` | `41d6c64f-4267-5bf3-8c95-6e135ba55739` | `U10`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d12:seat:U11` | `700398ad-69e5-5274-bfdc-e82d9626d645` | `U11`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d12:seat:U12` | `b38bdf36-cb75-523a-af84-2aeaa5aaec84` | `U12`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d12:seat:U13` | `7243f4fa-f5f6-52fd-a08c-05d0e7b7caea` | `U13`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d12:seat:U14` | `f81b8251-dce7-5aa5-a09a-b48c1d64a0a0` | `U14`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d12:seat:U15` | `33f438b3-1d30-5693-b07a-820a2ea5d65d` | `U15`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d12:seat:U16` | `d4f541dc-d365-56b3-a2ed-f88d55cf95bb` | `U16`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d12:seat:U17` | `2d194dec-9c78-56ea-ab34-f10de668ceb2` | `U17`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d12:seat:U18` | `5bc0442a-78a3-5c41-88dd-79878cb5f92e` | `U18`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d12:seat:U19` | `eb17fece-488c-5159-aaed-b5b2e0277bcf` | `U19`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d12:seat:U20` | `c8dbe568-1365-5270-b7ce-8f5c3d1c4adf` | `U20`; SLEEPER_UPPER/AVAILABLE |
| Trip | `trip:trip:a:r3:d13` | `5903c28e-8e34-5854-a2ee-5cae5766f964` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 13 |
| TripSeat | `trip:trip:a:r3:d13:seat:L01` | `d43016c2-31d7-5b17-926c-bce58736aad0` | `L01`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d13:seat:L02` | `66794617-a60a-55f9-b811-a031563627d7` | `L02`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d13:seat:L03` | `3cd5ea92-34b3-5053-9be6-20707426a92b` | `L03`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d13:seat:L04` | `32738901-07a0-586a-8f6f-4c6bffcd7d7d` | `L04`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d13:seat:L05` | `f8ce6af4-d233-5cf3-bc2d-df3c14dbf7f5` | `L05`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d13:seat:L06` | `357af90c-4c7c-5b00-a3c8-1842cf2b3253` | `L06`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d13:seat:L07` | `afc05437-0001-519a-b197-0cc95d0c0765` | `L07`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d13:seat:L08` | `bf62aa61-46cd-5f82-9dfa-c21412d0d964` | `L08`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d13:seat:L09` | `3ea4be59-80cd-5218-94dd-429a031f72ad` | `L09`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d13:seat:L10` | `f763b62b-9ada-5abc-9caa-d55715ac9852` | `L10`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d13:seat:L11` | `cd98ac71-0b28-58f2-a122-0c2afe469aeb` | `L11`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d13:seat:L12` | `0a0850f1-7207-5da3-a658-b29bd97275e9` | `L12`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d13:seat:L13` | `8b512c39-4ecc-574f-8b72-56a71f4f4d82` | `L13`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d13:seat:L14` | `090d69db-fe63-51f5-b35b-984c19709b4f` | `L14`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d13:seat:L15` | `17af3f2e-d082-565f-9be6-c4a4084ae73a` | `L15`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d13:seat:L16` | `700b8a5b-9087-5875-9980-a5012e2ad72b` | `L16`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d13:seat:L17` | `80167fad-d675-59f5-ba25-176969439cf0` | `L17`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d13:seat:L18` | `40d8494a-77ab-5d39-acc7-e0f19749d1c0` | `L18`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d13:seat:L19` | `13486d71-4eeb-527c-b47a-e891db056daa` | `L19`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d13:seat:L20` | `6a4b37c3-f770-5a7e-8b28-84661aef55ad` | `L20`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d13:seat:U01` | `c8a57589-eee1-5c0c-ba04-17ac130f3854` | `U01`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d13:seat:U02` | `204da967-09cc-5086-a24c-f7fa44b9dbfd` | `U02`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d13:seat:U03` | `41da11d3-6af7-53ae-8ee8-2af429257ce2` | `U03`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d13:seat:U04` | `6eb83408-d442-552a-93fe-34f7b8541964` | `U04`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d13:seat:U05` | `4724cddd-0b35-50fd-8a5c-9b10410dab83` | `U05`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d13:seat:U06` | `611b91f2-b9d7-5ebe-ba97-d3442b1a13ed` | `U06`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d13:seat:U07` | `2f76f8cd-4879-5ad9-a2c7-c0fcc5b6539d` | `U07`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d13:seat:U08` | `e9745fef-107f-52a5-a1e4-e99f74fd2541` | `U08`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d13:seat:U09` | `1df8f05f-e72f-5de5-8178-18dd900a09a1` | `U09`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d13:seat:U10` | `396a5f83-ba52-539e-801a-e8d9e94808e0` | `U10`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d13:seat:U11` | `2aac0f35-6dc8-543e-bcb1-a21efcb7fc63` | `U11`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d13:seat:U12` | `536fa998-b42e-5dcc-a86a-322bd0a999ed` | `U12`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d13:seat:U13` | `ce88d20a-10d7-50ef-b933-18bf21f586f9` | `U13`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d13:seat:U14` | `07d82550-189e-5719-bfda-364166131cd4` | `U14`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d13:seat:U15` | `4d52918f-b89a-5518-b684-0116c03bb753` | `U15`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d13:seat:U16` | `32438f92-abaa-5f26-8871-725dc283239a` | `U16`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d13:seat:U17` | `a67c00aa-b60d-51e2-bf4f-bf0fb42d8e6a` | `U17`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d13:seat:U18` | `d7e190da-8f03-54cf-bfb0-4415a4dcab87` | `U18`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d13:seat:U19` | `a8c2b702-5f8a-5b58-850a-bd88c9ccff7b` | `U19`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:a:r3:d13:seat:U20` | `b1b81cfe-4fbc-5861-b938-6b9fe7612a92` | `U20`; SLEEPER_UPPER/AVAILABLE |
| Trip | `trip:trip:b:r1:d00` | `c475c6d9-0ace-59c3-8e60-4ca2396f8b4b` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 00 |
| TripSeat | `trip:trip:b:r1:d00:seat:S01` | `4e63b713-fcd0-55ae-9b10-dd0ad2a530a8` | `S01`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d00:seat:S02` | `239ef636-43ed-54c9-890e-64690298916f` | `S02`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d00:seat:S03` | `60f0c2e8-1caa-50bd-b5df-833771761c20` | `S03`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d00:seat:S04` | `db5b297c-a5f8-556a-8853-1af429f74818` | `S04`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d00:seat:S05` | `1600c61f-49e2-5129-9f20-7470dbd65cd3` | `S05`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d00:seat:S06` | `483cb54d-cc13-5065-88a2-d6afb3e210ab` | `S06`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d00:seat:S07` | `a8f6958a-bab3-541c-8a33-144cac56a4a8` | `S07`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d00:seat:S08` | `577658a2-9dd6-5f5c-a28a-b38ad3f4b1b1` | `S08`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d00:seat:S09` | `b36879c1-e8ff-5b1c-a640-33beed711c73` | `S09`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d00:seat:S10` | `67c24c97-2d9f-5216-8e4c-7a421987dfc2` | `S10`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d00:seat:S11` | `61448953-0e4f-58ec-a04f-25f73c90ed12` | `S11`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d00:seat:S12` | `c1abe5d0-75e2-5ed1-b54a-48e4183110fd` | `S12`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d00:seat:S13` | `053b12b8-31f7-5580-bffb-eb273e6fb08a` | `S13`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d00:seat:S14` | `b8d90aa0-0ea1-5d7a-a754-8d02893f0654` | `S14`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d00:seat:S15` | `e340ebd9-66d2-5407-ba13-4fac63a65d11` | `S15`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d00:seat:S16` | `dcc992bc-301d-5847-bde4-467d4f6c478b` | `S16`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d00:seat:S17` | `84d1061d-680a-5101-b971-260a50b2286e` | `S17`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d00:seat:S18` | `b4b71b95-6f8f-5744-9180-d43f77dfe29b` | `S18`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d00:seat:S19` | `925d1de8-8685-5fad-82c9-fb5686acacc2` | `S19`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d00:seat:S20` | `fc7ce0fb-5f5d-5733-aae0-d1a78c15ae5e` | `S20`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d00:seat:S21` | `3cbdabc0-3d95-508a-a117-816b5cf72183` | `S21`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d00:seat:S22` | `b40ff2af-166e-5064-bdae-3c868253e1e0` | `S22`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d00:seat:S23` | `441f30c9-b3d2-5fd5-890c-a5609b1c5e4b` | `S23`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d00:seat:S24` | `d5a894ba-422b-53fd-91aa-50648425992f` | `S24`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d00:seat:S25` | `bc207655-0162-56a5-8b34-bdc691c61a20` | `S25`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d00:seat:S26` | `f3c51836-d31e-5dfa-8eb8-4d1df1385e64` | `S26`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d00:seat:S27` | `36240d26-8034-5c59-8ca9-a735a1cb1621` | `S27`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d00:seat:S28` | `65f0154c-2485-5f3e-bb09-524b0192b635` | `S28`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d00:seat:S29` | `25e5335a-bb1f-5008-9285-87d278c49470` | `S29`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d00:seat:S30` | `2eaafda3-be0b-59bc-8a8b-fe3d05d9e100` | `S30`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d00:seat:S31` | `2ca8a3df-4393-54bc-9fd8-629524b9cfb6` | `S31`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d00:seat:S32` | `b4ff186b-48d2-5f1b-a1b1-c110c53515c5` | `S32`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d00:seat:S33` | `9fb47275-c27d-5324-8603-2672da306d6e` | `S33`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d00:seat:S34` | `25a9d649-76cf-5228-b51f-52864db2a5ff` | `S34`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d00:seat:S35` | `8f4b0e66-f9dd-5793-8eb5-52a2ddc92f7b` | `S35`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d00:seat:S36` | `d79b2c32-43f4-58ac-af50-2f62f98b9ef9` | `S36`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d00:seat:S37` | `f7f0ac0b-136d-5f53-ac1b-4ebe7e0095ad` | `S37`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d00:seat:S38` | `cf5d8902-2ff5-599f-995a-757cd1c99d3a` | `S38`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d00:seat:S39` | `2c720a7c-cab9-5089-bcf2-f21b6199207f` | `S39`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d00:seat:S40` | `9d1a6d66-7ac7-56a2-bac4-0853d97efca6` | `S40`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d00:seat:S41` | `f60dfd83-c003-5f4e-987c-1606f46d0bf9` | `S41`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d00:seat:S42` | `852686c0-8525-58c1-9270-9a8442531907` | `S42`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d00:seat:S43` | `4c5863f9-8412-52b7-a368-58cdcd886bdd` | `S43`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d00:seat:S44` | `6a69366f-02a3-5633-814c-94222723059f` | `S44`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d00:seat:S45` | `944ecd36-1a3f-59c2-84a0-f37d1d009486` | `S45`; STANDARD/AVAILABLE |
| Trip | `trip:trip:b:r1:d01` | `bef3c1e3-198f-5e14-b7ec-6d08662bbbf8` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 01 |
| TripSeat | `trip:trip:b:r1:d01:seat:S01` | `47a64af6-cf20-5225-b93a-1b0c11a8ba3c` | `S01`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d01:seat:S02` | `64dadcae-0640-5a83-ad4d-806a591873f2` | `S02`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d01:seat:S03` | `dc7fb618-08fb-512a-9fba-7b0e207de549` | `S03`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d01:seat:S04` | `0859d840-1284-5a36-83ed-235c587b0b6c` | `S04`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d01:seat:S05` | `869907d3-f58f-5eda-9fdd-c7f9d6a9db5d` | `S05`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d01:seat:S06` | `0baa7a27-2249-5970-bc70-09ef302db17f` | `S06`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d01:seat:S07` | `a9f89c13-eb32-5c8f-9e16-d71983fcccc0` | `S07`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d01:seat:S08` | `e0705e61-4245-556d-b05d-7b2dbb42cbec` | `S08`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d01:seat:S09` | `14539321-e0c2-52fd-b40e-e7dc7090160e` | `S09`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d01:seat:S10` | `31c52c39-cc62-5b3b-97f7-5b2c1a826fa6` | `S10`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d01:seat:S11` | `af7eec9b-94e6-5657-b924-71ff14d04fc1` | `S11`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d01:seat:S12` | `c0c5b015-df92-5e61-8373-2aa6e772fb52` | `S12`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d01:seat:S13` | `e4a53db9-ab98-5866-8c9f-8f949a94a07a` | `S13`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d01:seat:S14` | `414cd238-12a5-52c2-a604-5589cad6befc` | `S14`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d01:seat:S15` | `de869dd5-c765-5d98-b57a-cd40fc384e54` | `S15`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d01:seat:S16` | `881ce461-ac9c-5aa7-b7d3-e7792438be25` | `S16`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d01:seat:S17` | `bb534daa-b53a-5e54-b5b0-707c5a896be2` | `S17`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d01:seat:S18` | `c4aafb96-2b15-585c-b812-9fad7f85d0cb` | `S18`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d01:seat:S19` | `b130e4a1-885b-5ec9-8b6f-0fbe763612e0` | `S19`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d01:seat:S20` | `8ba9e754-a582-5a60-8225-28cbba494142` | `S20`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d01:seat:S21` | `106d5a24-e6c7-5a4e-8d58-93347c636e20` | `S21`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d01:seat:S22` | `960c6583-579d-5be7-a0ef-4079c1372359` | `S22`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d01:seat:S23` | `0a9dfe51-f08b-5457-988e-dac288894c61` | `S23`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d01:seat:S24` | `9bd4c0b5-6788-5b7c-8d15-552e39b44eb6` | `S24`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d01:seat:S25` | `cb772254-aff1-54e1-9d61-b61dd8c5c6cf` | `S25`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d01:seat:S26` | `17f82029-ff54-5936-9056-43bf97df082f` | `S26`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d01:seat:S27` | `3f979889-3d3b-5374-b827-f08d3dab635b` | `S27`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d01:seat:S28` | `7181faf5-2af7-50e7-a1e6-0d738fa7fcaf` | `S28`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d01:seat:S29` | `9dca9815-af42-546d-a532-3516480d20fe` | `S29`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d01:seat:S30` | `95e8d256-02fb-5167-8c48-ed891f20cb47` | `S30`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d01:seat:S31` | `c4bc9cac-d2d2-59fd-972a-f964ff4ccb65` | `S31`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d01:seat:S32` | `2d48012d-78df-5050-89fc-7b9b29a13611` | `S32`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d01:seat:S33` | `8866cd09-2279-5952-bef1-93f6f6f8d330` | `S33`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d01:seat:S34` | `f80e1f39-2167-5669-ab36-757b782babf8` | `S34`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d01:seat:S35` | `d1865713-2138-5595-9331-5c278b4f277d` | `S35`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d01:seat:S36` | `1f7c26b5-21af-59b5-957a-79fff819484b` | `S36`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d01:seat:S37` | `2c5bfdb1-8fa0-5964-8d58-fe85265e27bd` | `S37`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d01:seat:S38` | `ad1bacf6-1811-522d-9f75-1e2b391ad364` | `S38`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d01:seat:S39` | `9462c501-b6b3-55df-86f0-b71c38860fe8` | `S39`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d01:seat:S40` | `cdf4e446-0952-5598-8d8e-3889c5ad7457` | `S40`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d01:seat:S41` | `1b41d1a7-dd5b-5893-aec0-6d8c59eee2fe` | `S41`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d01:seat:S42` | `ce4a154a-b874-523c-bdb5-419faf96d1f6` | `S42`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d01:seat:S43` | `82088524-968f-5ba4-b081-46df8acbe25a` | `S43`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d01:seat:S44` | `6ed4ad7e-99ce-5806-99b2-9b7b17107904` | `S44`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d01:seat:S45` | `ca95e3e0-d689-53b7-8712-b573c5d68934` | `S45`; STANDARD/AVAILABLE |
| Trip | `trip:trip:b:r1:d02` | `f54adbae-a757-5b38-ab03-2fa70feab715` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 02 |
| TripSeat | `trip:trip:b:r1:d02:seat:S01` | `4551dccc-cbfd-525e-b0de-b4f744cb375c` | `S01`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d02:seat:S02` | `ff627262-a34c-5e3c-9fbf-b060f46c4104` | `S02`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d02:seat:S03` | `8bb81dc4-d852-56e1-b2cc-a4137c9203bd` | `S03`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d02:seat:S04` | `e689199b-b3b0-54e2-a963-345434bc9539` | `S04`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d02:seat:S05` | `5252a0f1-a437-5d8c-9f36-d8e7a510b566` | `S05`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d02:seat:S06` | `a8308ccd-ada5-5b21-b0d2-152cabff0097` | `S06`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d02:seat:S07` | `14c8fb76-539d-5e27-92fb-9ca728d870ed` | `S07`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d02:seat:S08` | `2158c024-e78d-5bc4-8e72-dbeab18cdf6d` | `S08`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d02:seat:S09` | `b2d0e815-1612-5eaf-9759-2ccb8350a6bc` | `S09`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d02:seat:S10` | `85cbecef-d165-51f8-802b-bdcd7498d069` | `S10`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d02:seat:S11` | `efac5eac-933a-52f0-8a58-e7a1b36b8dfd` | `S11`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d02:seat:S12` | `398c45fc-0752-570f-acee-9883fd5cf3a5` | `S12`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d02:seat:S13` | `5be9bee9-33a2-5d83-9dc3-1bc0c04f19b4` | `S13`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d02:seat:S14` | `c0ea68f9-e420-555a-888c-b368a54f003f` | `S14`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d02:seat:S15` | `b7324ff7-9088-5e6c-a22c-9dafd9e6c32b` | `S15`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d02:seat:S16` | `1a42bf96-5db0-5c6e-8563-e4803cef6eb5` | `S16`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d02:seat:S17` | `8e7354a7-a268-5589-9b71-ee856f513277` | `S17`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d02:seat:S18` | `0c3081a3-568b-52ff-88e0-35a9c0bc6a7a` | `S18`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d02:seat:S19` | `b42bf571-adfd-5c0a-9bf3-e827255d2266` | `S19`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d02:seat:S20` | `a3961d1e-3fec-5a88-8b53-5347a1b9f5e7` | `S20`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d02:seat:S21` | `ca098f63-7886-543a-9483-77e79396e9f0` | `S21`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d02:seat:S22` | `b97e602c-ccc8-57ca-b2e9-e2495a56993b` | `S22`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d02:seat:S23` | `e593373c-8b8a-5172-9b06-5ded83a929e2` | `S23`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d02:seat:S24` | `86df038f-5823-5e89-9f12-a6751afe820e` | `S24`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d02:seat:S25` | `bccee485-abec-5ba7-a956-20d12f2621bc` | `S25`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d02:seat:S26` | `41752d2b-2ff4-54c7-8c94-369b46566f84` | `S26`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d02:seat:S27` | `3faa2795-e38c-5f75-b736-326cf54d14ee` | `S27`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d02:seat:S28` | `f89af404-59ca-58ca-99a4-42330b6e2f71` | `S28`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d02:seat:S29` | `3866c388-a2f9-5a5a-a052-91e255da11cb` | `S29`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d02:seat:S30` | `48917cb5-5299-5c52-ac15-b2f1b3b70cd1` | `S30`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d02:seat:S31` | `003461bb-5c1b-5e4a-8efa-0d762dfc0b0f` | `S31`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d02:seat:S32` | `66474ee5-6b06-580a-9822-6284769e5f58` | `S32`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d02:seat:S33` | `94a2b514-ce2f-57e0-9577-6fc1de06d472` | `S33`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d02:seat:S34` | `21af1c72-90e8-58f3-912a-307712173040` | `S34`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d02:seat:S35` | `02653285-f263-5a77-8b52-de4b9af4b618` | `S35`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d02:seat:S36` | `affbb15a-8561-5355-8934-4c78a78c5bfe` | `S36`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d02:seat:S37` | `68e1872b-4bef-53f9-952d-c963e97fb19f` | `S37`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d02:seat:S38` | `d2c7f481-960c-5d2e-b2b2-6f6a9568e30b` | `S38`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d02:seat:S39` | `b81bd55f-bfa8-582e-88f6-9adc3609dab9` | `S39`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d02:seat:S40` | `e916ff99-f796-5560-b397-a89ae62d1bba` | `S40`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d02:seat:S41` | `3622e2d2-ee79-550a-81ac-ebacdb86d464` | `S41`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d02:seat:S42` | `7b0295b8-e049-5ce8-990d-494ed33f3480` | `S42`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d02:seat:S43` | `45fd2fc3-17fa-56c6-89b7-c2f6da5567c9` | `S43`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d02:seat:S44` | `c9dd3815-e9b4-5e9e-a679-d19c97592faf` | `S44`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d02:seat:S45` | `8115486a-13ef-530c-96eb-62eb15e52114` | `S45`; STANDARD/AVAILABLE |
| Trip | `trip:trip:b:r1:d03` | `feba3836-b808-53cf-b7de-9ba6d3da0d4e` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 03 |
| TripSeat | `trip:trip:b:r1:d03:seat:S01` | `7723ad79-ecde-5afc-b599-0032528ca482` | `S01`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d03:seat:S02` | `fd67fa3a-15b8-5db2-84ea-37a542318498` | `S02`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d03:seat:S03` | `bb8ecff6-5872-5195-b09d-8826a4461ad3` | `S03`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d03:seat:S04` | `179f06a6-f222-597e-b811-77395d37594b` | `S04`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d03:seat:S05` | `b5b178b5-9875-5164-9e68-7f189a69648d` | `S05`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d03:seat:S06` | `e0c53077-0265-5ee3-8c81-f2c784364d75` | `S06`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d03:seat:S07` | `0d8f3a49-d286-54d6-bcce-3ce78db309a3` | `S07`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d03:seat:S08` | `2af933c3-784c-55c5-9f4e-df311fd495d3` | `S08`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d03:seat:S09` | `fe667adb-569b-5151-b7f8-173f5ea4a7aa` | `S09`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d03:seat:S10` | `9ca9de59-badd-5467-b712-7cb55ad9fda5` | `S10`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d03:seat:S11` | `75fab3b1-f464-5177-997e-3618af914c54` | `S11`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d03:seat:S12` | `1e9dcf76-4809-5c06-bf52-3047209d5a76` | `S12`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d03:seat:S13` | `fce2ed56-b4f8-5a34-892a-d36d27918913` | `S13`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d03:seat:S14` | `5a2001d4-8d9b-5b32-b845-e4dfe61348a2` | `S14`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d03:seat:S15` | `527a875a-2350-5254-9dfa-9b200268af6b` | `S15`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d03:seat:S16` | `f4aed820-4785-5f62-944d-11ca3df2ab59` | `S16`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d03:seat:S17` | `d000c8e4-45a4-5fdd-82a2-bd7e6173bb45` | `S17`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d03:seat:S18` | `221cb57f-7e66-5718-8ee8-f1334746a58a` | `S18`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d03:seat:S19` | `183359ed-1efc-5c63-a043-373d2a6a02e7` | `S19`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d03:seat:S20` | `01d7c57d-cf5a-58e1-bd5f-7f1695e31768` | `S20`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d03:seat:S21` | `ae79732d-95e8-58b0-8f29-06096d233013` | `S21`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d03:seat:S22` | `91b06673-7ccd-52f8-bac8-54fbd9850037` | `S22`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d03:seat:S23` | `5b84bc1b-3062-5bcc-9313-f07be09c062f` | `S23`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d03:seat:S24` | `8a20b7f1-010f-5dad-90ab-9754a11a3a5e` | `S24`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d03:seat:S25` | `336a90e4-1eec-5647-9016-0465fc36d685` | `S25`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d03:seat:S26` | `7028221e-c730-5e32-b592-5bf327f8cc34` | `S26`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d03:seat:S27` | `6e46e8b0-215d-5431-8f4e-3da2b5c16b19` | `S27`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d03:seat:S28` | `d4f35d77-bb7a-54d4-b7a7-2cdb884aec95` | `S28`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d03:seat:S29` | `e7a7238a-df91-5bed-bc25-7e0ab0dc13dd` | `S29`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d03:seat:S30` | `f7ea7ca1-2f28-507e-bbc3-a118602397d6` | `S30`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d03:seat:S31` | `19947255-f970-5adb-9b86-8e6577418bf3` | `S31`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d03:seat:S32` | `46a1129c-8a1a-5e00-8f33-0f86788f01d2` | `S32`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d03:seat:S33` | `23142a17-0074-5027-8848-ae250d316362` | `S33`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d03:seat:S34` | `55a02dfc-4d31-58f5-87f9-39334d662ed9` | `S34`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d03:seat:S35` | `135c72e0-1a3e-5ba9-9b20-9b2b707068dc` | `S35`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d03:seat:S36` | `94761158-0f08-541e-8d8b-71271d7f699e` | `S36`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d03:seat:S37` | `11fa3c0f-5334-52de-85bf-56f5519a7819` | `S37`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d03:seat:S38` | `6588c8a8-689d-56bf-be4f-a7fc105dc0d0` | `S38`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d03:seat:S39` | `ba7604a1-f9e8-5231-a0a9-123c9b86a516` | `S39`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d03:seat:S40` | `d0e1e07b-d15b-5c13-9f64-bd1b23284d00` | `S40`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d03:seat:S41` | `f3237a05-ba75-5577-a46f-c31a465607f6` | `S41`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d03:seat:S42` | `79e24f36-ffeb-5792-89e8-6d47b79c00e4` | `S42`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d03:seat:S43` | `e5d6d861-a47d-5a76-a9f3-92b261b2c629` | `S43`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d03:seat:S44` | `732abb23-039f-5bf2-9e86-3a5d1e303101` | `S44`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d03:seat:S45` | `3704f30d-7cae-56f5-9349-481b609a3d18` | `S45`; STANDARD/AVAILABLE |
| Trip | `trip:trip:b:r1:d04` | `f1865e41-2123-504d-a8fc-d6112a4cd506` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 04 |
| TripSeat | `trip:trip:b:r1:d04:seat:S01` | `5fbb583d-efec-53dd-aa7e-d755dcc360fb` | `S01`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d04:seat:S02` | `580bc7f0-06de-50ac-9a17-9bf2d2a5a3d7` | `S02`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d04:seat:S03` | `7281d3ff-240b-55a6-9f7a-575d7c24e697` | `S03`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d04:seat:S04` | `04149823-ea9a-5dbf-91d2-bcd1d9e2c764` | `S04`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d04:seat:S05` | `1e54cb6e-a16a-58f5-9a57-7dcd950909e0` | `S05`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d04:seat:S06` | `587d1af0-875d-5f0f-8bed-aa651c59c96a` | `S06`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d04:seat:S07` | `1d4435b5-83af-571e-9fbe-78b4cd75c7f4` | `S07`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d04:seat:S08` | `8c0db03a-ebfe-5e21-b825-6bad2310fbaa` | `S08`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d04:seat:S09` | `ece7650b-ad72-5057-b7b0-5ce8906711f5` | `S09`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d04:seat:S10` | `9b8a3529-aa97-5e28-886f-e63f0009fb09` | `S10`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d04:seat:S11` | `deca7b6f-8df4-5c74-a86c-889b0d6fb248` | `S11`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d04:seat:S12` | `7fd57faf-20cf-50da-8db1-b78f0e93cb6e` | `S12`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d04:seat:S13` | `95e03a85-bc55-50b2-b1e8-9d9d09ec88aa` | `S13`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d04:seat:S14` | `ef8b73b1-b0bc-5948-85f5-2449898fafdf` | `S14`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d04:seat:S15` | `d85dfb74-bf8d-5932-bd11-cd01e8d7790a` | `S15`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d04:seat:S16` | `ac4ca298-8a9f-5c7f-bfd9-f0165c44426a` | `S16`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d04:seat:S17` | `d3f5a92f-b0ca-5bde-b823-ae55589bf9e8` | `S17`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d04:seat:S18` | `d90ce0c4-d058-5446-94c8-c0817a66044a` | `S18`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d04:seat:S19` | `dbbd00fc-e148-57bf-876e-29eca8c44f99` | `S19`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d04:seat:S20` | `3365aae7-30f2-542a-92ca-809d286f3dcd` | `S20`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d04:seat:S21` | `45772b34-1c10-5d60-89ad-bcb70bb8953e` | `S21`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d04:seat:S22` | `72610ce2-7672-5a8f-a9e7-3cfc323a4e31` | `S22`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d04:seat:S23` | `a1de5c40-a612-529b-8151-35005a51fca8` | `S23`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d04:seat:S24` | `52f06492-5bdb-5ca5-afca-c33390302697` | `S24`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d04:seat:S25` | `449805d5-7a92-5db8-af50-1443f0155158` | `S25`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d04:seat:S26` | `1416d90c-24da-566c-90e4-9dcf97c117e9` | `S26`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d04:seat:S27` | `dfb49d14-d942-59f8-8047-072ad148494a` | `S27`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d04:seat:S28` | `71560009-a54b-50a0-a248-7e2fcb4a1d5a` | `S28`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d04:seat:S29` | `66014999-d7e9-57c9-b7a1-639024e2a9eb` | `S29`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d04:seat:S30` | `72576a0f-b9cb-569c-9d4f-3820bbbd3188` | `S30`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d04:seat:S31` | `a2a13c6b-76ca-576c-9258-ff89c6bcb594` | `S31`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d04:seat:S32` | `3a66f198-eef1-58c3-ad00-d2d139c40a66` | `S32`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d04:seat:S33` | `96e8a727-d227-5dd4-bcde-f5cf4eae1118` | `S33`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d04:seat:S34` | `b87c1105-f7cb-595a-a245-e6451435a704` | `S34`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d04:seat:S35` | `a62a090b-03bc-5c0c-9d4e-acb8dff16ce1` | `S35`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d04:seat:S36` | `3af32f6c-7936-5854-abcd-e66d324f1a22` | `S36`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d04:seat:S37` | `4035f9d6-fc93-5581-8223-2ee2181adbfb` | `S37`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d04:seat:S38` | `e5097d36-4642-5ff7-957d-53fe05bb58ee` | `S38`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d04:seat:S39` | `41a7b546-99ff-5e16-8b3a-3385f3ff3df8` | `S39`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d04:seat:S40` | `27ab7640-87b0-5fd1-acc2-b44a4a46f9d6` | `S40`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d04:seat:S41` | `8a27ff21-7243-5cfa-ab80-93792e083db5` | `S41`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d04:seat:S42` | `b59405f9-317d-54be-8b50-b8c891bd774f` | `S42`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d04:seat:S43` | `44a3e596-3367-5332-8b69-b1cd7ea75da8` | `S43`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d04:seat:S44` | `1df23d6e-198a-509f-8d36-150406db365d` | `S44`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d04:seat:S45` | `4fc62c63-a77b-54b6-ae3f-9b6436c1c419` | `S45`; STANDARD/AVAILABLE |
| Trip | `trip:trip:b:r1:d05` | `c22d0576-4651-5f60-9b4a-f4010e8aeb05` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 05 |
| TripSeat | `trip:trip:b:r1:d05:seat:S01` | `205c87eb-2f4e-5441-9041-96ef997342a4` | `S01`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d05:seat:S02` | `cbb5d34f-90f1-5813-be1b-f384cf2ed393` | `S02`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d05:seat:S03` | `0bbef99e-018f-5a33-a4f4-cbf3294437f3` | `S03`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d05:seat:S04` | `4fa3454b-748f-50ef-b731-a6af2a3b07e6` | `S04`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d05:seat:S05` | `d614a228-5425-5a7b-98e3-5619cc37fa54` | `S05`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d05:seat:S06` | `cd5d63de-6f3e-5469-9ef2-9000a83cde7b` | `S06`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d05:seat:S07` | `68b9bc6c-fee0-5452-8231-00e7d1dcd56b` | `S07`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d05:seat:S08` | `b05f7a66-f0ba-5f61-9200-963d4941b274` | `S08`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d05:seat:S09` | `031f0d8a-840f-55f7-ad25-2be5c4576fe7` | `S09`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d05:seat:S10` | `e2efd41f-9d63-5b86-b952-ed21bfbd720d` | `S10`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d05:seat:S11` | `8e7fa130-fca1-59ec-b12e-e34ca7e852d9` | `S11`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d05:seat:S12` | `6c8033e8-7ca3-5bd2-a482-c4ea552ae0bd` | `S12`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d05:seat:S13` | `7737cd23-14e1-560d-9f03-49f244910492` | `S13`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d05:seat:S14` | `eaab58d3-3e1e-51f9-be4c-d8c6d433ce0c` | `S14`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d05:seat:S15` | `3f6d7095-08b8-5e0f-acd6-ad17a2b83b4c` | `S15`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d05:seat:S16` | `b36e7fa7-ecc9-52cc-b0b2-0217eb07ae72` | `S16`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d05:seat:S17` | `46ff740c-a2cb-54f6-a779-45cdfd3c8ace` | `S17`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d05:seat:S18` | `7dbc4369-836b-5854-8941-f2701be803bf` | `S18`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d05:seat:S19` | `710477aa-753e-58dc-9833-25e71166e8d7` | `S19`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d05:seat:S20` | `3f21d11c-731b-585c-a200-b1eff97bfcb3` | `S20`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d05:seat:S21` | `7f4265b0-c18e-59e1-bd0d-8ba4628ac766` | `S21`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d05:seat:S22` | `9d9e014c-6aeb-571d-97d3-90b11388c71e` | `S22`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d05:seat:S23` | `f84a13ee-4423-55e7-b653-cc73f8c38f05` | `S23`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d05:seat:S24` | `af16b07b-371d-5f3a-857f-89dcf1bd9db5` | `S24`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d05:seat:S25` | `456ab024-d008-5a32-a375-ec08a5d751e4` | `S25`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d05:seat:S26` | `1da7ca85-5280-5dee-94f9-794e184146e2` | `S26`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d05:seat:S27` | `de747a43-a6c5-5bd0-b37a-c6465ba811de` | `S27`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d05:seat:S28` | `abb70002-211b-55fd-b944-dbf435396d24` | `S28`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d05:seat:S29` | `f612c0d9-6871-57b0-acca-129605daa2b7` | `S29`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d05:seat:S30` | `9a675479-62a2-5101-a8c2-42afb3f26341` | `S30`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d05:seat:S31` | `28c41ed8-aaf1-576f-9eeb-b7b7eddb89bb` | `S31`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d05:seat:S32` | `33bb5b8c-64b9-5f8b-82e0-290237f20188` | `S32`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d05:seat:S33` | `f71bdc52-35cd-58b7-a9eb-6006ebde2b9c` | `S33`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d05:seat:S34` | `416d94d8-d4f8-5ea4-8451-f4dcee7782e2` | `S34`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d05:seat:S35` | `c9fe64c9-055a-5fd6-87c4-5f1f8d49cad2` | `S35`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d05:seat:S36` | `c32b04b7-5d56-5232-a2da-77059671b0aa` | `S36`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d05:seat:S37` | `44aadbf4-f456-5623-b8c1-d4ea230230db` | `S37`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d05:seat:S38` | `62022696-0a0f-5166-80b8-214e95473b25` | `S38`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d05:seat:S39` | `d39494ec-0847-5c77-bc43-389c2e2e0e85` | `S39`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d05:seat:S40` | `22881967-1b5b-5636-b8fd-94cb5a8f2d23` | `S40`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d05:seat:S41` | `ace92a8c-16a1-567b-8a0c-30fab72555f1` | `S41`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d05:seat:S42` | `cf05ed50-4764-5a42-af4b-47768d3cf47c` | `S42`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d05:seat:S43` | `c053b8d6-cadb-5999-95a9-cd1f4c28e02f` | `S43`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d05:seat:S44` | `cc00b741-fbca-5448-a62f-36c8c10266ae` | `S44`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d05:seat:S45` | `64a02489-981d-58d3-9583-79101c53581f` | `S45`; STANDARD/AVAILABLE |
| Trip | `trip:trip:b:r1:d06` | `ff740a59-2428-500f-a58b-32b6bd71bdf8` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 06 |
| TripSeat | `trip:trip:b:r1:d06:seat:S01` | `343c133c-06b4-57c3-bbfe-16213647f6e2` | `S01`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d06:seat:S02` | `3c386ea8-b285-585d-b9fb-84e7bb6c49db` | `S02`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d06:seat:S03` | `4bb12012-f632-5eaa-872a-87e8cb1eac1c` | `S03`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d06:seat:S04` | `23ff09b9-7ee0-5745-b0bd-be78d7fe9126` | `S04`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d06:seat:S05` | `c248bd3b-aab5-549b-922d-b102bced447b` | `S05`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d06:seat:S06` | `7cf70638-ad4d-5d62-b714-aee837fdea69` | `S06`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d06:seat:S07` | `20e56f51-b79b-561e-b257-e23a46792c5e` | `S07`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d06:seat:S08` | `eccc5169-f25a-53f7-b9ca-78e61e3c7503` | `S08`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d06:seat:S09` | `dc83e57e-2cbb-5ca6-bcac-ae08c351758b` | `S09`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d06:seat:S10` | `8aa4d391-4045-52f8-90c3-5fa05856974b` | `S10`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d06:seat:S11` | `6d36d9db-80ac-529d-a724-705e849c286d` | `S11`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d06:seat:S12` | `af86ffe9-b90b-54c4-a38d-5ac22df6600e` | `S12`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d06:seat:S13` | `ded4ea56-2550-5e3d-a5d2-bac68cb39a6f` | `S13`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d06:seat:S14` | `f28c1298-44e9-5726-be54-0cb2bb71849d` | `S14`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d06:seat:S15` | `001a1947-80bd-573e-997a-bb3c0c6753f2` | `S15`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d06:seat:S16` | `a0401fa1-ccee-5845-89ba-7417880d293d` | `S16`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d06:seat:S17` | `50ad9ba4-7f65-5ded-8bfd-a47ca55c5b7d` | `S17`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d06:seat:S18` | `bc247fe8-3f32-5f3b-821c-f2deeac49665` | `S18`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d06:seat:S19` | `f4ea8328-a3e7-5f16-84cc-14e91404ccbf` | `S19`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d06:seat:S20` | `5f7298fb-1661-589b-a9af-39706d41c226` | `S20`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d06:seat:S21` | `83598777-c749-5a00-ae38-eb61fde844b9` | `S21`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d06:seat:S22` | `22984b8b-0d6b-5a3d-a3fa-bb6ee4ff52bc` | `S22`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d06:seat:S23` | `009f8d0c-76e7-583d-83f8-77f0e5dc2369` | `S23`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d06:seat:S24` | `f42ce838-b3ea-5eb3-b054-f87199d92591` | `S24`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d06:seat:S25` | `627dbf2e-ae6d-54ac-83c5-055531c9740e` | `S25`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d06:seat:S26` | `5da41d30-7f47-5e7e-aff6-23fc9820b10c` | `S26`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d06:seat:S27` | `78a396a5-a95d-5fb2-8554-855a53aacc3f` | `S27`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d06:seat:S28` | `dbe5bbda-3c69-5e2f-bbf4-c98cb2d5b405` | `S28`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d06:seat:S29` | `5c5ac07c-8fb4-5f1c-a8ef-5c7c2078f03a` | `S29`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d06:seat:S30` | `34edf7ff-b5d4-537c-a3ca-5f0dcfaa818c` | `S30`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d06:seat:S31` | `f5956a19-5960-5f8a-95cb-f00530469a40` | `S31`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d06:seat:S32` | `7a77cc70-b741-5c03-8efc-906dac8cd080` | `S32`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d06:seat:S33` | `366cddcb-fcc6-5e62-901d-43e74d9abcab` | `S33`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d06:seat:S34` | `4271432e-6e7b-5380-997d-90301f60b529` | `S34`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d06:seat:S35` | `c942667d-3657-5cc3-86eb-5d0753f47bf2` | `S35`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d06:seat:S36` | `c46cc747-1515-5fa4-ab07-71e546791d07` | `S36`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d06:seat:S37` | `df9dd68d-816f-5404-8ca1-60c32e1fae56` | `S37`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d06:seat:S38` | `bfe91a30-64f1-5646-a0ae-58c64e790f71` | `S38`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d06:seat:S39` | `b5134b30-fbfc-545d-8dd3-c260d985d72c` | `S39`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d06:seat:S40` | `b9e7bc18-2762-5c2d-8a76-8bbf143e795d` | `S40`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d06:seat:S41` | `d3602a9f-a846-570b-95a8-617ce030e364` | `S41`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d06:seat:S42` | `52a5ceba-a2af-53fd-9dbb-00ef95a40d3e` | `S42`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d06:seat:S43` | `20700b57-44d5-5503-b8a3-719d08aa572e` | `S43`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d06:seat:S44` | `1c5985ec-452b-5718-b5bb-7a59e3274b0e` | `S44`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d06:seat:S45` | `86321af8-ef1d-52d7-95e7-da0259984341` | `S45`; STANDARD/AVAILABLE |
| Trip | `trip:trip:b:r1:d07` | `9f8a781b-c2ad-51b5-874f-ec45d1474eca` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 07 |
| TripSeat | `trip:trip:b:r1:d07:seat:S01` | `a1097773-eabd-54b6-aa82-bc13532360ab` | `S01`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d07:seat:S02` | `b633da5e-2bce-5f32-8253-658c0c46f7fb` | `S02`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d07:seat:S03` | `393d5e8c-dbe0-5866-acb3-ba98da0b516c` | `S03`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d07:seat:S04` | `6c09d09a-e896-597f-9b58-5bf636d6f01c` | `S04`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d07:seat:S05` | `fb4a41cf-3dd1-5355-bd6e-a18dcb8426d2` | `S05`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d07:seat:S06` | `a46b85e7-550e-5845-87d1-08e5474a9d1f` | `S06`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d07:seat:S07` | `88012a17-5dee-57e1-bb61-d47421e64f22` | `S07`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d07:seat:S08` | `464b90ea-dea6-5ee3-a1ce-ad6f0047dc0b` | `S08`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d07:seat:S09` | `4c83a499-8fa5-5324-89dd-238f159dc2e0` | `S09`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d07:seat:S10` | `1a7a7086-ff3a-5c08-a9ea-11910c1c0434` | `S10`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d07:seat:S11` | `029f6adb-2266-5627-b710-9ba1a7191808` | `S11`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d07:seat:S12` | `3d087e4b-be37-5f5b-9da9-3b5a3c363945` | `S12`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d07:seat:S13` | `ff6ead15-f4cb-53ef-98c6-06e5f45dd713` | `S13`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d07:seat:S14` | `74e21c9f-7b2f-5972-bb6b-969bb7e2691d` | `S14`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d07:seat:S15` | `5f2fb4be-7ce5-5c48-9f6a-09881fe0cc3d` | `S15`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d07:seat:S16` | `04ef3432-efa1-59a3-8d8e-b710ac3f3165` | `S16`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d07:seat:S17` | `b2958789-0749-563a-b655-97b0a23b2fe7` | `S17`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d07:seat:S18` | `b472da23-0991-557b-bb2a-7d3ba60d491b` | `S18`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d07:seat:S19` | `03838f45-f4e7-51bb-bcce-6c6d4be2c0f0` | `S19`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d07:seat:S20` | `c4ee973c-011c-530d-800b-b21376bff74b` | `S20`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d07:seat:S21` | `a97805b9-4ffb-5610-a7dd-d996b4070cfe` | `S21`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d07:seat:S22` | `eb1a3990-7313-535f-97ad-a8109a5730d4` | `S22`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d07:seat:S23` | `add4a52d-334c-5a75-8d17-add4d4b3acef` | `S23`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d07:seat:S24` | `704a79a2-d96b-5ad4-b47d-16c6498c2e76` | `S24`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d07:seat:S25` | `cd5e57e7-52bf-5527-a601-c0abfab2000f` | `S25`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d07:seat:S26` | `04ce2169-683c-53e1-bf50-b143263fbf00` | `S26`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d07:seat:S27` | `7059ee12-6d91-57fe-be8a-dfe69e44f60b` | `S27`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d07:seat:S28` | `a77fb351-d815-5a05-b0c0-99334ec68335` | `S28`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d07:seat:S29` | `eb3bb033-b53e-5b61-a12c-dda5b7fa06c1` | `S29`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d07:seat:S30` | `4739009a-723f-5232-9c5d-0f524dab6e4c` | `S30`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d07:seat:S31` | `df74fd55-220c-5c9b-9bd1-e8924d7340e5` | `S31`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d07:seat:S32` | `b84520a6-36d7-524e-8bc9-56410b65c441` | `S32`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d07:seat:S33` | `20d0fa61-5c02-5ddd-beec-2d7299454042` | `S33`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d07:seat:S34` | `5151c277-5479-507e-b2ba-4b72f98721e2` | `S34`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d07:seat:S35` | `3ed7b979-cf4f-5ba5-a2b5-9e004b268615` | `S35`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d07:seat:S36` | `544139da-71a7-5b80-af58-ca8e706a9874` | `S36`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d07:seat:S37` | `f041104c-62cc-5491-80d3-44db2e76bfc7` | `S37`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d07:seat:S38` | `a93e1d6c-5d50-59e0-9794-e6bf9af9bb85` | `S38`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d07:seat:S39` | `4e1e7140-a605-5aa3-9c8b-0527a646c210` | `S39`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d07:seat:S40` | `56066155-8d96-5a80-b575-a7574e91b365` | `S40`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d07:seat:S41` | `cff97e75-5288-54cc-8676-f58e698aa285` | `S41`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d07:seat:S42` | `6e577119-e811-5886-9972-813d26e04e12` | `S42`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d07:seat:S43` | `55ff1f83-9277-56cc-b808-1045e9e2ac19` | `S43`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d07:seat:S44` | `e1d1ac81-b4e7-53dc-916f-c750e474b30b` | `S44`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d07:seat:S45` | `4d523b97-226f-552f-a1eb-d4d1086f7243` | `S45`; STANDARD/AVAILABLE |
| Trip | `trip:trip:b:r1:d08` | `a7240a37-d7a3-56ed-9b6f-6d13fcd7e69c` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 08 |
| TripSeat | `trip:trip:b:r1:d08:seat:S01` | `f2f2b932-0a58-5b29-a1be-5a6a8e33694c` | `S01`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d08:seat:S02` | `5ff4d74e-720b-5871-82e1-e1bdc32e76ca` | `S02`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d08:seat:S03` | `733d55bb-a167-59ec-97fe-51124db42593` | `S03`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d08:seat:S04` | `65369c23-6649-5949-9d80-a8b233039ab9` | `S04`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d08:seat:S05` | `1da40311-4426-51ce-a7e8-ed88d93f541f` | `S05`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d08:seat:S06` | `37c92aaa-4d32-5e64-b8c7-0c241a11096d` | `S06`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d08:seat:S07` | `27da9acc-a620-55c5-a39a-4784bb1d9ac7` | `S07`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d08:seat:S08` | `1edfe341-1185-5736-b26f-231570ae1012` | `S08`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d08:seat:S09` | `bddfa2bc-efa6-513f-9ac2-c510ffe2774b` | `S09`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d08:seat:S10` | `7f325355-dcd5-5dfb-900b-2ce6ac62f43e` | `S10`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d08:seat:S11` | `28205631-8ea4-5a7a-bed5-fe00c19bca95` | `S11`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d08:seat:S12` | `5a35c364-b5f6-5180-a8b0-c91f755c79f7` | `S12`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d08:seat:S13` | `ce1ee037-e8ce-5e2d-beba-eb18c584bc03` | `S13`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d08:seat:S14` | `425c0618-4a2b-5aa2-994d-1275fcaf3b5c` | `S14`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d08:seat:S15` | `db362694-9048-5a96-a7da-84964d73d4d7` | `S15`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d08:seat:S16` | `c14a3522-777e-5e55-aa5f-c528f561c66f` | `S16`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d08:seat:S17` | `49910cc5-6611-5c4a-bb85-0b8823f95d2a` | `S17`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d08:seat:S18` | `6d4b40c1-c285-5c75-a89d-cb23f445bb70` | `S18`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d08:seat:S19` | `df829063-f8e4-5030-8514-0f488683553d` | `S19`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d08:seat:S20` | `0a83056e-d20c-5264-a6b4-ed3056ae106a` | `S20`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d08:seat:S21` | `6a71a73a-0f50-5050-93e5-3601ae48caef` | `S21`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d08:seat:S22` | `cc07e491-1743-536c-847c-d43973e75307` | `S22`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d08:seat:S23` | `ba44c56c-915a-5d05-b438-4b39cb48dc83` | `S23`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d08:seat:S24` | `7994c457-def2-54ce-825c-19657a31eb0d` | `S24`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d08:seat:S25` | `4ef49399-c2f1-53f0-9a69-31824d7e4c5e` | `S25`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d08:seat:S26` | `cddec1b2-b11e-5bde-b9eb-e08ec180466b` | `S26`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d08:seat:S27` | `e98b40d7-987c-5514-8bc7-aebe87e03e4a` | `S27`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d08:seat:S28` | `5279edea-d466-5d07-b408-21c116cbf96a` | `S28`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d08:seat:S29` | `eb68d64a-9e47-582c-9bd4-f5bfc434995a` | `S29`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d08:seat:S30` | `5eedc552-dc31-5f03-8022-965918e106a9` | `S30`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d08:seat:S31` | `1a8aa815-9bbe-5fcb-a122-3d05a2fab252` | `S31`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d08:seat:S32` | `0efe00c3-63a8-57e4-9ae0-2c85512a9a5b` | `S32`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d08:seat:S33` | `6f100f25-22d1-5a80-8d13-a560a7e396f9` | `S33`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d08:seat:S34` | `8eb31638-347e-532b-ab2d-d1c83c9ed0a2` | `S34`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d08:seat:S35` | `ac02822b-1239-51d5-8da7-30ff28459fd4` | `S35`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d08:seat:S36` | `d3144406-bece-5884-95d1-495680400a8c` | `S36`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d08:seat:S37` | `01a751f0-40aa-5364-a7b8-9a763e3ef9da` | `S37`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d08:seat:S38` | `ed294b50-c647-5782-8b45-3b24bf46e79e` | `S38`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d08:seat:S39` | `a9959a6a-2696-5ffd-b196-97e52ce00d7b` | `S39`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d08:seat:S40` | `24f9815b-ca71-5482-8559-de25732e3a22` | `S40`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d08:seat:S41` | `85e13e9b-36f1-5620-8104-07c5f8253797` | `S41`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d08:seat:S42` | `584e2e9d-7944-5bd9-b735-31507feb9e4e` | `S42`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d08:seat:S43` | `48d8ad40-9b17-521f-989c-44b01bb128b4` | `S43`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d08:seat:S44` | `bf36e605-cdf9-52aa-83ae-99e96068c498` | `S44`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d08:seat:S45` | `de2eb90b-6bd6-5cb9-8a0a-beceb19ce155` | `S45`; STANDARD/AVAILABLE |
| Trip | `trip:trip:b:r1:d09` | `e3a9721c-813a-56fb-a3ad-76c2b4608b2e` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 09 |
| TripSeat | `trip:trip:b:r1:d09:seat:S01` | `3a6cd818-c98b-552a-a4db-26aacec5acc7` | `S01`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d09:seat:S02` | `80f85276-b63b-5a32-bcc5-b9bdcb80f8d5` | `S02`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d09:seat:S03` | `78a92f81-9960-5cbc-8185-19c2b475f3a9` | `S03`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d09:seat:S04` | `170047f9-c2cc-5a49-b3a6-4bf0d011926b` | `S04`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d09:seat:S05` | `e99dc2fd-8188-52cd-83a1-50355202a813` | `S05`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d09:seat:S06` | `291a7207-9aba-5162-954f-3c1ec9650edd` | `S06`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d09:seat:S07` | `20d1efcd-657a-54bb-adb7-5a176f8ce500` | `S07`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d09:seat:S08` | `3400af38-0fbf-5e66-b106-d7bc7eeca7e0` | `S08`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d09:seat:S09` | `1a1a3121-9ee1-5f59-9e9c-36f921bf5156` | `S09`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d09:seat:S10` | `e996186e-5864-5a01-93af-81ce4cae92bb` | `S10`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d09:seat:S11` | `e6fb729b-4709-546c-8a01-30b7658cdda4` | `S11`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d09:seat:S12` | `ee6d9454-21bb-55d7-ad08-7101043e41b9` | `S12`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d09:seat:S13` | `5ca403dd-c405-55fa-a3d0-9416a52b4a6a` | `S13`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d09:seat:S14` | `3757a3d9-46af-5e37-a201-f4c44db5b2f6` | `S14`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d09:seat:S15` | `a3434b78-9229-5ad7-873c-dfeedf53a051` | `S15`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d09:seat:S16` | `43eb523e-1e60-570d-a08b-c45deca2d8c9` | `S16`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d09:seat:S17` | `f3e37ed6-46a1-5255-8bd4-416fff34db0c` | `S17`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d09:seat:S18` | `4947b3a2-032b-5c0e-8474-708e2046b864` | `S18`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d09:seat:S19` | `b32a40ce-0cb6-50ec-8c81-dfe35d4ce1ff` | `S19`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d09:seat:S20` | `8c92908a-98bb-5612-b771-db79fc035142` | `S20`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d09:seat:S21` | `8eed1f62-0b62-52d4-8b2d-5d39062f4292` | `S21`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d09:seat:S22` | `a8ba362d-10c0-5a15-99da-9b02c594d566` | `S22`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d09:seat:S23` | `e5cf6d11-851d-565e-a797-21ba5ea60330` | `S23`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d09:seat:S24` | `80dc4765-a350-51a2-9505-0753c0dd8cbd` | `S24`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d09:seat:S25` | `e0763b01-7dcc-5837-89b8-e0be3222ad68` | `S25`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d09:seat:S26` | `56808118-682b-5de2-b75a-096522b76b51` | `S26`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d09:seat:S27` | `c20efaeb-fa23-54cc-ba68-1cfecef69589` | `S27`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d09:seat:S28` | `43e4b065-f970-5b6c-849b-2d6ce96b26f0` | `S28`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d09:seat:S29` | `6c83444c-1d54-50e7-8b50-275a80519f5d` | `S29`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d09:seat:S30` | `6eff7f00-42f7-571c-9243-0007ecc4964a` | `S30`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d09:seat:S31` | `fb57208b-1802-5e33-8b7f-800cb205147c` | `S31`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d09:seat:S32` | `4f9dbf3a-85e8-5a2e-ab35-40c0de3c8928` | `S32`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d09:seat:S33` | `1acfa835-d288-51e4-9b39-7ee2116f6501` | `S33`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d09:seat:S34` | `d429e87c-d62b-5ff6-b8cb-78856b33b311` | `S34`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d09:seat:S35` | `c44a771e-7664-5efc-bb70-2bbe492cc135` | `S35`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d09:seat:S36` | `0d5fd222-01d6-5a80-ad1d-0de43620f9fa` | `S36`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d09:seat:S37` | `5c2d7cff-9696-5693-8cb0-b2f5b67b55e7` | `S37`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d09:seat:S38` | `61837039-bdbf-523e-b1da-7b89b844d301` | `S38`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d09:seat:S39` | `7d271351-86a2-5ab5-a503-5b9526ad8234` | `S39`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d09:seat:S40` | `7dbbe780-4f81-5108-b569-01e5b9bbec73` | `S40`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d09:seat:S41` | `9c7d6b22-0fb5-592a-abd0-85c435b3d64e` | `S41`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d09:seat:S42` | `e4bc1680-8d82-521f-b2a5-228c6e4107f2` | `S42`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d09:seat:S43` | `d20fda65-bc94-5e7a-beb6-a11fdc379a2d` | `S43`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d09:seat:S44` | `76e775fb-2def-55b1-bcfb-50512cc672ca` | `S44`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d09:seat:S45` | `1933ebe6-e933-5beb-956a-56b8193c13f2` | `S45`; STANDARD/AVAILABLE |
| Trip | `trip:trip:b:r1:d10` | `23ffc69e-a209-55ed-95d5-3aab6d8c7fb5` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 10 |
| TripSeat | `trip:trip:b:r1:d10:seat:S01` | `003a1d57-8445-54e1-b0ed-8e6be3c4fab3` | `S01`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d10:seat:S02` | `6e32b6f6-da23-5245-b86c-9b16e34380f9` | `S02`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d10:seat:S03` | `63dfd6fc-12f2-59ee-b81d-2499d02ddc0b` | `S03`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d10:seat:S04` | `250f7ade-3257-5dd9-b678-e9537fc608c2` | `S04`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d10:seat:S05` | `a3e5325e-aff6-51d7-830d-95d822f9efd8` | `S05`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d10:seat:S06` | `dafc7c2a-84d1-5bd2-b99d-cc46c2e21136` | `S06`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d10:seat:S07` | `5c55951c-916b-58a7-8dc9-c50905748791` | `S07`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d10:seat:S08` | `4779159b-ee67-527b-b06a-fee46d98fafd` | `S08`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d10:seat:S09` | `9eab2a4b-a506-573b-9634-df27f191e364` | `S09`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d10:seat:S10` | `a6eb8657-c658-514a-ac68-267cafcd66fb` | `S10`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d10:seat:S11` | `a4f212f0-8bb9-597b-b821-0713aec5c429` | `S11`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d10:seat:S12` | `5025536c-221d-5404-b9bd-518db63b34c6` | `S12`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d10:seat:S13` | `e0a41911-7dcd-5fe1-a078-dbf46ac43150` | `S13`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d10:seat:S14` | `95c02afb-0437-5dfa-81da-c92a267b95cd` | `S14`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d10:seat:S15` | `95b87d69-534f-51f3-9477-704ffccad722` | `S15`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d10:seat:S16` | `37190485-618c-5765-8daf-2f961762addc` | `S16`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d10:seat:S17` | `4dc08044-cd7b-55c9-b108-20f28f63930d` | `S17`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d10:seat:S18` | `e053c03d-9bba-5ebe-bc06-055d8bc26474` | `S18`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d10:seat:S19` | `5becda9a-3020-5a73-b0d4-a686082754ab` | `S19`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d10:seat:S20` | `e0e73c79-cd65-5eca-a656-be845ef769e0` | `S20`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d10:seat:S21` | `f317701e-9797-5f3b-997d-6c43ada19fd2` | `S21`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d10:seat:S22` | `e055ac0b-e76e-515b-abce-2ea69b75818a` | `S22`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d10:seat:S23` | `d6d555ed-4c2a-573f-a55e-03bb70b4a2e6` | `S23`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d10:seat:S24` | `63b4eeec-ef79-5d13-9b24-cb8238b87f3c` | `S24`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d10:seat:S25` | `11f12d59-1255-5742-b0eb-46e81f7322c0` | `S25`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d10:seat:S26` | `04110e28-8e2e-5721-aeac-4b4a8db6cef2` | `S26`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d10:seat:S27` | `9ee3c66f-e3c6-59d6-9941-6a1cc3d105dc` | `S27`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d10:seat:S28` | `36f32f98-ccb4-56b2-b68d-f957589dd469` | `S28`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d10:seat:S29` | `d5837d23-3852-588d-90fa-037cf9b59cbc` | `S29`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d10:seat:S30` | `3a44148f-3fa5-5203-8a4e-7f43c85c9a14` | `S30`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d10:seat:S31` | `9402e300-5905-591b-82b0-b8d24d11468a` | `S31`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d10:seat:S32` | `b201c790-4cab-518c-9217-6a644dc257a4` | `S32`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d10:seat:S33` | `9a284b1a-a4e0-56d6-9fac-4b5eaed68a86` | `S33`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d10:seat:S34` | `cddddd73-830e-59b3-b208-05d669198b2a` | `S34`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d10:seat:S35` | `599bfa19-cbe6-5641-b6bc-4c46722bc563` | `S35`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d10:seat:S36` | `b63c257e-f987-534a-90fc-0b93b8cc7c10` | `S36`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d10:seat:S37` | `11bc7e31-2ea9-54e0-b6cf-fa1c0c74de5d` | `S37`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d10:seat:S38` | `0be639b6-1b43-5f91-8bb8-553a28f50dc5` | `S38`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d10:seat:S39` | `08aeb612-c150-5806-a8c1-081dc9bbbe31` | `S39`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d10:seat:S40` | `77f951ef-2040-50c9-b65b-a96e3c90ec2f` | `S40`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d10:seat:S41` | `c34db894-df96-5a17-8e18-22702d4430a3` | `S41`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d10:seat:S42` | `6b0b1798-fd13-5eb3-ab6e-9fe236cbe906` | `S42`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d10:seat:S43` | `6a610211-6b25-5e8b-8aff-a6e8db97f2fb` | `S43`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d10:seat:S44` | `f1bf2078-a573-5763-af3e-76b02a9d88e4` | `S44`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d10:seat:S45` | `afcb3b19-0ab3-576a-a028-8a73c92eefd0` | `S45`; STANDARD/AVAILABLE |
| Trip | `trip:trip:b:r1:d11` | `4ccab73d-ecfa-5993-aa1a-d3d0511e4d7d` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 11 |
| TripSeat | `trip:trip:b:r1:d11:seat:S01` | `205fbd1f-07ed-57ba-a380-a93171a314f7` | `S01`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d11:seat:S02` | `d29acd13-f42e-5e17-ae80-30d1a5a814bb` | `S02`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d11:seat:S03` | `d8663b92-30ef-55ac-94d3-d357795c2140` | `S03`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d11:seat:S04` | `74ea348b-70c7-5d4f-b814-4a58ed845be9` | `S04`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d11:seat:S05` | `f27bbce0-5127-5ae6-86e6-54f183598c5a` | `S05`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d11:seat:S06` | `a0c6d84f-4857-5d7a-a826-e4e9f3027e16` | `S06`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d11:seat:S07` | `b7fd27cc-dd31-56f9-89a2-f95728b9cc9f` | `S07`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d11:seat:S08` | `f6c63974-f159-5dc2-b8dc-3e956c071828` | `S08`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d11:seat:S09` | `b39f4d71-cd3a-517a-96f7-329af75e73cf` | `S09`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d11:seat:S10` | `befa20ea-75ee-5352-87e7-0497b792e975` | `S10`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d11:seat:S11` | `11eaea13-f232-5935-9687-327e623b78c9` | `S11`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d11:seat:S12` | `8c46241e-9460-5002-9f53-2a1550fedd32` | `S12`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d11:seat:S13` | `92b9044d-4fda-566e-901d-8a6c8d04375f` | `S13`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d11:seat:S14` | `45cd2a86-f39d-5e76-8b2e-ee52f97dc08d` | `S14`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d11:seat:S15` | `fbdc7bdd-c7c8-54f0-be56-ac5a17178c73` | `S15`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d11:seat:S16` | `e13e75c6-8a4a-52c6-a2b6-88eb601e0a96` | `S16`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d11:seat:S17` | `033ed92c-3954-5c45-b183-39180914ff8e` | `S17`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d11:seat:S18` | `da1033e8-ee6d-534f-bad1-edb482777cac` | `S18`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d11:seat:S19` | `3d37ab88-d921-5af9-8d10-da810ebd4e55` | `S19`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d11:seat:S20` | `949f8004-b160-51f7-abf1-0c7a989202aa` | `S20`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d11:seat:S21` | `ac5be289-ef9d-5556-a9b4-5168270a25ac` | `S21`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d11:seat:S22` | `eb7c41cb-070f-5a93-ac21-794546155cfd` | `S22`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d11:seat:S23` | `43d28791-51e7-5cb6-b02d-9b643077d45d` | `S23`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d11:seat:S24` | `00c238c8-176f-5d65-af50-3b02dce831f7` | `S24`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d11:seat:S25` | `d50ad3a2-25b6-5539-a090-b20d84cf6134` | `S25`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d11:seat:S26` | `21a58812-9345-5446-a22b-7896ed884d06` | `S26`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d11:seat:S27` | `f7b63e30-0484-578c-ba6b-25040cf9ec6b` | `S27`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d11:seat:S28` | `1e43ca5c-ed14-548d-a4c2-788da0cefed6` | `S28`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d11:seat:S29` | `b9cb7529-89a8-5da4-a47a-b027389e99dc` | `S29`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d11:seat:S30` | `99b0b623-6559-553f-b23d-2a37f9e57b46` | `S30`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d11:seat:S31` | `71c370c0-c330-594f-a758-c7f8fca8d41d` | `S31`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d11:seat:S32` | `1e346c1e-7e0a-5573-b277-aba5787f614c` | `S32`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d11:seat:S33` | `ae3ebc3d-c0b4-5250-8f8f-41e444301d01` | `S33`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d11:seat:S34` | `089f7dcc-da1d-5bf1-bed5-6f93798bdaff` | `S34`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d11:seat:S35` | `61df5ab4-36b4-55ef-8d4c-6f5a7c0c19a4` | `S35`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d11:seat:S36` | `4d6142c0-422c-5d3a-a73b-69618a63d9d9` | `S36`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d11:seat:S37` | `a9b993a3-cbe9-51ad-a1f0-48f2f45e10fe` | `S37`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d11:seat:S38` | `a8260710-6cdb-5430-a179-554ac21e48a0` | `S38`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d11:seat:S39` | `17d48478-cde8-5bb8-8233-2a1576d4f3c6` | `S39`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d11:seat:S40` | `baf3577a-8079-566f-aedb-78f2c26cc036` | `S40`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d11:seat:S41` | `db33c4d1-ffcd-5972-bc9e-d57ac9cf0cda` | `S41`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d11:seat:S42` | `57502408-5fb2-51a1-8f9a-67d8cb26bccf` | `S42`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d11:seat:S43` | `5d176979-1716-5273-87ca-cbb12ca0fb3e` | `S43`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d11:seat:S44` | `7ae4c19d-ff55-51f4-9814-9e5f8f7adec3` | `S44`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d11:seat:S45` | `6fe3a1a9-c3c0-564e-babb-481b3801a053` | `S45`; STANDARD/AVAILABLE |
| Trip | `trip:trip:b:r1:d12` | `c5c9974a-abba-5482-94cb-c25bd2fb0a9e` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 12 |
| TripSeat | `trip:trip:b:r1:d12:seat:S01` | `cd30324f-5cc7-5170-8026-39ce06bf895b` | `S01`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d12:seat:S02` | `ab952048-df40-5173-93fa-3b8c889eef85` | `S02`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d12:seat:S03` | `01937fc2-511a-575b-9f3a-f12301062bc6` | `S03`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d12:seat:S04` | `3568b5c4-2d87-52af-bf21-e1cf197d324c` | `S04`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d12:seat:S05` | `18e27f48-da39-51b6-a367-b8da8ca5e518` | `S05`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d12:seat:S06` | `7b326016-5b84-53b8-99f7-1ccecc47fe53` | `S06`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d12:seat:S07` | `4244a5be-265e-5749-9214-d5236dc19e7a` | `S07`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d12:seat:S08` | `6a459016-fefd-59ec-ac26-3e197be6b8f4` | `S08`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d12:seat:S09` | `942cfc89-8526-5b99-b593-52e883f60ed1` | `S09`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d12:seat:S10` | `c8c58348-400b-5699-ac2b-48e55bc2d539` | `S10`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d12:seat:S11` | `ffcf48c6-12b1-5196-b659-af7aecc297f7` | `S11`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d12:seat:S12` | `fd28cf7d-719d-5a37-87b2-214d32145825` | `S12`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d12:seat:S13` | `7b98fb0e-0299-5816-9ac8-aecd1024ff89` | `S13`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d12:seat:S14` | `69fda151-fe91-5937-9462-9cfa15edb636` | `S14`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d12:seat:S15` | `d5184eef-7430-5efc-acd5-4de50f7228d6` | `S15`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d12:seat:S16` | `805288a0-69e1-5782-bbc8-185c153750db` | `S16`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d12:seat:S17` | `80e01088-cfb7-56a0-8b26-bf4d16e2a347` | `S17`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d12:seat:S18` | `52494e9e-5cd7-5e43-a555-fca724d749e0` | `S18`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d12:seat:S19` | `147540aa-df3f-56bb-af9f-118fad85a194` | `S19`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d12:seat:S20` | `f59f2eac-2766-5dca-83ca-b4972768da4a` | `S20`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d12:seat:S21` | `7ef221bc-b073-5225-8c9d-b7121f8b9b69` | `S21`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d12:seat:S22` | `a4df7da5-90b7-5c68-8568-44205425a309` | `S22`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d12:seat:S23` | `8aae7b40-6ed4-57f5-9b43-471c9c6f2c94` | `S23`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d12:seat:S24` | `2c5711ef-8063-5c62-874c-37b2b6d3aee2` | `S24`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d12:seat:S25` | `3fec6a60-33ce-55aa-88b3-6f029bf098ee` | `S25`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d12:seat:S26` | `93a5b44f-919e-57c1-add5-9e2138dacf21` | `S26`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d12:seat:S27` | `2fec96a7-8de2-5971-ae7c-585098f8ce89` | `S27`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d12:seat:S28` | `2642fcad-805a-5ad9-81e7-473aa78bbd4a` | `S28`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d12:seat:S29` | `b8e4df8e-c65b-5c9f-9de5-267d17382ed3` | `S29`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d12:seat:S30` | `e4e66176-15e4-5ac3-b1b2-1b4932bd2d25` | `S30`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d12:seat:S31` | `501739c4-a0f0-50d9-b990-844ce718a787` | `S31`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d12:seat:S32` | `f3f0d8e0-1271-5d0a-96c7-7c075d65eea0` | `S32`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d12:seat:S33` | `161e230c-1eed-52fd-bbeb-b106fc0a4d3c` | `S33`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d12:seat:S34` | `9b7f5616-f4fa-5b33-bd64-24d7a79adfce` | `S34`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d12:seat:S35` | `6d5c19d1-506b-5165-be4e-034f64f74e82` | `S35`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d12:seat:S36` | `19c33d94-9c91-548a-b6ae-a927da0b9f75` | `S36`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d12:seat:S37` | `be0279eb-fdbf-59e4-87d3-9e1534200833` | `S37`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d12:seat:S38` | `4b2fe151-ecb1-5ab3-9741-d2115d401389` | `S38`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d12:seat:S39` | `3ce61d7b-b3af-5076-aa70-bd86591f3bcc` | `S39`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d12:seat:S40` | `66d15147-55d3-59d9-9eaf-11204f1cb385` | `S40`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d12:seat:S41` | `9d603b78-6ae7-58dd-af3c-e2fdf9596cbf` | `S41`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d12:seat:S42` | `f118a356-66ae-5e8a-9ffc-43b49280831d` | `S42`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d12:seat:S43` | `4124a924-84ef-5e44-ab99-4563cb3f9816` | `S43`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d12:seat:S44` | `e4e9bd73-3d64-56f6-99db-8994255c37b2` | `S44`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d12:seat:S45` | `94d7d6e0-7f3b-5ae3-9887-0396fc94e1ee` | `S45`; STANDARD/AVAILABLE |
| Trip | `trip:trip:b:r1:d13` | `257e4d9e-fb01-5f02-9149-3b229546b31d` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 13 |
| TripSeat | `trip:trip:b:r1:d13:seat:S01` | `277fd129-47d3-5e37-ae74-0ea0c3e0a54c` | `S01`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d13:seat:S02` | `1d135161-28c0-545e-9001-0f22e3dfe701` | `S02`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d13:seat:S03` | `f13646a9-03cf-518f-8859-fa502ece5a04` | `S03`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d13:seat:S04` | `aaa056eb-4010-515b-868d-36efb0be3ff5` | `S04`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d13:seat:S05` | `c7acb074-ea88-5cd7-8035-f44d3df850c6` | `S05`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d13:seat:S06` | `2afd1c78-b6e6-5272-a8a0-d3f362e4621c` | `S06`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d13:seat:S07` | `0a22864e-6d93-558a-a300-958c8eb2863b` | `S07`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d13:seat:S08` | `d852432f-b32f-58df-99e4-3d0c1ebc9a59` | `S08`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d13:seat:S09` | `92eaddc7-7327-56c4-860e-fb29fb614d83` | `S09`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d13:seat:S10` | `bf2b6967-88ec-5a1b-9863-8ebd34616770` | `S10`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d13:seat:S11` | `eaf4b302-564e-5037-82d7-644b28691d95` | `S11`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d13:seat:S12` | `8d804e15-9fce-5433-b623-d0086c04a63e` | `S12`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d13:seat:S13` | `e09be136-f56c-5a67-a8c2-0bb5f7aba25d` | `S13`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d13:seat:S14` | `e0e5794a-dc43-5c4a-ae74-3b197e353af6` | `S14`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d13:seat:S15` | `d84cd072-8017-561f-8d33-4a578253b895` | `S15`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d13:seat:S16` | `8f6f1339-8050-5e60-9102-7d3af7667660` | `S16`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d13:seat:S17` | `97919244-3a49-5065-8538-5c8719bc3fa8` | `S17`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d13:seat:S18` | `e700e1d9-5a67-5a72-a2d1-8e3658e934eb` | `S18`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d13:seat:S19` | `1eef1db2-0a35-5d3d-9fc1-c8f1929ccda3` | `S19`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d13:seat:S20` | `e7686cbf-48ff-576c-884e-a24cb955831e` | `S20`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d13:seat:S21` | `32924c52-ff5a-536e-82b3-df2c7691be13` | `S21`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d13:seat:S22` | `72c2eee8-d2bf-59e7-a192-016af4715a74` | `S22`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d13:seat:S23` | `0e975f86-7702-57d4-b570-7dbb9dd7eae6` | `S23`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d13:seat:S24` | `8d5da551-b870-51d4-8533-4d7a5f41852b` | `S24`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d13:seat:S25` | `f2da4938-4815-5c13-8352-e290f39e12ab` | `S25`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d13:seat:S26` | `4666a3df-8aba-5c45-869f-b870bbc61b19` | `S26`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d13:seat:S27` | `51969054-69d8-5a11-9f79-9f87231a44c3` | `S27`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d13:seat:S28` | `92dcb849-0832-5a63-854a-3385d3c99f0c` | `S28`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d13:seat:S29` | `6edf062e-04a5-52f7-9990-414425e0b3e4` | `S29`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d13:seat:S30` | `e4e8aa2f-982f-5d35-b2a5-5f79469cb7a6` | `S30`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d13:seat:S31` | `6e983a89-7946-5499-aca1-f5bded9b801b` | `S31`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d13:seat:S32` | `eaef5015-000a-57c4-b1b0-184078d29e00` | `S32`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d13:seat:S33` | `0119a20a-0ebd-58e4-b606-d69577667daf` | `S33`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d13:seat:S34` | `09deeb78-397d-5046-89dd-12eef22e9177` | `S34`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d13:seat:S35` | `f36ff3c3-52af-5826-94b6-47045d3e6d70` | `S35`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d13:seat:S36` | `01c4b0a1-acaa-5ef6-9db9-1be90b914913` | `S36`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d13:seat:S37` | `cce2c5a4-3842-5e11-a1e8-a3b3afc5a69d` | `S37`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d13:seat:S38` | `12e56ed3-ea1e-5c91-9bb8-96b215dabdb9` | `S38`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d13:seat:S39` | `8d1c0a61-c824-59c3-bd3e-4054fd70532a` | `S39`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d13:seat:S40` | `83943dd4-858f-52d6-8eda-d7598e09c498` | `S40`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d13:seat:S41` | `5941e325-72e0-52af-974e-89d0e4e48b99` | `S41`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d13:seat:S42` | `4cb43977-058a-599a-810a-807b2c043dd1` | `S42`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d13:seat:S43` | `fa3db15f-ab5a-5d7a-8c22-664554475ff8` | `S43`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d13:seat:S44` | `45ab32f0-f24b-5f8e-8402-bc26459ef17e` | `S44`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:b:r1:d13:seat:S45` | `c6bcc140-39a0-59e4-95d7-62752f6a7b62` | `S45`; STANDARD/AVAILABLE |
| Trip | `trip:trip:b:r2:d00` | `bbf6cd42-3fdb-5a1e-b7a4-d01f6c5b5a67` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 00 |
| TripSeat | `trip:trip:b:r2:d00:seat:V01` | `65524784-30a1-5b40-af70-0e3b7b090019` | `V01`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d00:seat:V02` | `da37d40c-1e7f-521f-9443-83fc1222882d` | `V02`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d00:seat:V03` | `b8bdbfbd-b0c9-5010-b48f-9ff06086c6eb` | `V03`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d00:seat:V04` | `3eb6910e-f70b-509f-88f6-684c7144aefd` | `V04`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d00:seat:V05` | `73360392-718f-562e-b08f-b862155d659d` | `V05`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d00:seat:V06` | `f530304b-14e1-5e7e-91ad-ab2612add6f9` | `V06`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d00:seat:V07` | `e4d6b334-eee9-54cc-8305-31068eb6ae43` | `V07`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d00:seat:V08` | `32b9b97f-5ab4-5b95-998f-53eacfbfdc5f` | `V08`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d00:seat:V09` | `4bfc3b92-1cca-5dd0-907e-a8dd775d4570` | `V09`; VIP/AVAILABLE |
| Trip | `trip:trip:b:r2:d01` | `191dd58e-a9a2-53ee-8652-39d96431610f` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 01 |
| TripSeat | `trip:trip:b:r2:d01:seat:V01` | `33d615b4-2fd6-54aa-89c9-e7f2ec12fb24` | `V01`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d01:seat:V02` | `a6d3aebd-8440-501c-b2dc-69f7330e86eb` | `V02`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d01:seat:V03` | `6446e122-d396-509e-a9be-2b993316c62f` | `V03`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d01:seat:V04` | `7544c9c2-42be-5be9-9dea-65e76f2f3982` | `V04`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d01:seat:V05` | `283ba052-875c-521d-a547-689da434b205` | `V05`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d01:seat:V06` | `251574f6-7055-595e-b149-3d166ef36d8e` | `V06`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d01:seat:V07` | `7172bcac-b143-5e83-90be-88bfb7a28264` | `V07`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d01:seat:V08` | `57cd931d-14ca-5106-bfd4-e308944cd14e` | `V08`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d01:seat:V09` | `ce743d50-9da9-5cdc-9399-8a48cae08551` | `V09`; VIP/AVAILABLE |
| Trip | `trip:trip:b:r2:d02` | `90e9f91b-7fd3-50eb-964e-a48b5f4516e0` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 02 |
| TripSeat | `trip:trip:b:r2:d02:seat:V01` | `59df1f7e-c7b8-5383-b9b6-5c85b7e6a561` | `V01`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d02:seat:V02` | `a40a1e95-8fb0-524c-9235-4f684174c4f0` | `V02`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d02:seat:V03` | `377ddeee-cef2-592d-b03c-581e147bbf52` | `V03`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d02:seat:V04` | `3c49b7b9-74b7-59c6-9f7f-dd4b1a2c7e3c` | `V04`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d02:seat:V05` | `a142b094-dba6-5024-8e13-cd2e2e2c1c51` | `V05`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d02:seat:V06` | `34c46b89-777e-560b-97bd-84dd03b78ed8` | `V06`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d02:seat:V07` | `a69f522d-ffec-503a-9d35-80ec5df8c5e7` | `V07`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d02:seat:V08` | `05d01807-6e07-58ef-9ca7-a52064e7124e` | `V08`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d02:seat:V09` | `9bdd8a90-8add-51b9-8773-27640cc48607` | `V09`; VIP/AVAILABLE |
| Trip | `trip:trip:b:r2:d03` | `85a500df-5a2f-501b-a2a8-a5fec4f5916b` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 03 |
| TripSeat | `trip:trip:b:r2:d03:seat:V01` | `ba638ba5-eb22-5cdd-9b4c-8fbf1888d7bf` | `V01`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d03:seat:V02` | `d8a80f84-67f1-55e1-beae-308dcc3f1d5a` | `V02`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d03:seat:V03` | `2237b5ba-ac30-53a8-b226-6efce50fa128` | `V03`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d03:seat:V04` | `922f295d-3a1e-5d9d-b551-1b7b701f8f31` | `V04`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d03:seat:V05` | `7ad88484-84cf-544a-b162-1e1627488edf` | `V05`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d03:seat:V06` | `ea597cf9-e80d-522c-b8fc-07e78cc04aac` | `V06`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d03:seat:V07` | `bcea186a-b390-5f61-902b-800fce8a5107` | `V07`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d03:seat:V08` | `b2c4597c-8039-5ea1-992a-5ccb927da54c` | `V08`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d03:seat:V09` | `7c4d4726-44c2-5b4e-8f78-8b947b6f81bf` | `V09`; VIP/AVAILABLE |
| Trip | `trip:trip:b:r2:d04` | `f4cdc66a-fa94-534d-8696-47b8fd17c1d9` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 04 |
| TripSeat | `trip:trip:b:r2:d04:seat:V01` | `c0390324-ee6b-53d0-b17f-b434400757eb` | `V01`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d04:seat:V02` | `d49dc076-a8f4-5cc9-a646-1571216b8f3b` | `V02`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d04:seat:V03` | `2c9091c1-ac3a-5e5f-ad61-97b758fa43a9` | `V03`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d04:seat:V04` | `59b735ee-6cc5-5aa3-ae43-5f6e835b4364` | `V04`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d04:seat:V05` | `77f845dd-f6c9-54e2-8190-94502ad3b996` | `V05`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d04:seat:V06` | `1b0fd840-8698-53d9-a550-00f427f85dcc` | `V06`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d04:seat:V07` | `e5d52c05-7277-5880-bb23-bd4d9fc1eedd` | `V07`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d04:seat:V08` | `5861136c-d31d-58fd-be89-69edc3709698` | `V08`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d04:seat:V09` | `b36d3982-8440-5e62-99f2-a2210ba5a8b0` | `V09`; VIP/AVAILABLE |
| Trip | `trip:trip:b:r2:d05` | `6ee0689f-b18d-51e9-891a-85c2f435bf1e` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 05 |
| TripSeat | `trip:trip:b:r2:d05:seat:V01` | `612d20a4-4a0c-5100-9de5-c744b2f1db01` | `V01`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d05:seat:V02` | `07927a4f-9ebb-533d-8e04-fee20d66cca3` | `V02`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d05:seat:V03` | `f7bcae4f-f119-5f2f-81a5-2172ad64958f` | `V03`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d05:seat:V04` | `4220d339-92a7-54c0-973c-c5995ec65d73` | `V04`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d05:seat:V05` | `544b4e63-5a29-5843-91da-d03fa0e4e408` | `V05`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d05:seat:V06` | `7f920134-228a-5973-a3d0-59f5303ac1f5` | `V06`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d05:seat:V07` | `3654473d-5b87-5e85-979a-d74290db9cde` | `V07`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d05:seat:V08` | `3fe3870a-34b2-542e-b80b-5e85029a90e9` | `V08`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d05:seat:V09` | `6cd6158d-2505-597e-b8b1-53ecfc8e320b` | `V09`; VIP/AVAILABLE |
| Trip | `trip:trip:b:r2:d06` | `8c7a3953-35af-5d25-8bb2-bf97fd7f45d4` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 06 |
| TripSeat | `trip:trip:b:r2:d06:seat:V01` | `f6c778e3-a2f5-5a6f-8be9-9b3a5ba85afd` | `V01`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d06:seat:V02` | `4e3bcc5e-6aad-55b5-9e37-b0550874244a` | `V02`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d06:seat:V03` | `9b7c84dd-f2b5-5d00-8cdd-4996c6c948f7` | `V03`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d06:seat:V04` | `2b1317fe-583e-5736-9d8b-3d19ef7d278d` | `V04`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d06:seat:V05` | `3102d93f-1c53-5d44-a3e5-927b3e54b907` | `V05`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d06:seat:V06` | `e9045ffb-da02-5ac9-a79c-fdf05461c9e2` | `V06`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d06:seat:V07` | `ec72de2f-512c-5f4c-b3d4-fd3369410504` | `V07`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d06:seat:V08` | `d6b1dfe1-c291-549a-9b83-fc304b96077b` | `V08`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d06:seat:V09` | `bba25afb-a8bd-5e42-80fa-f48544284233` | `V09`; VIP/AVAILABLE |
| Trip | `trip:trip:b:r2:d07` | `45f49db1-887b-5f46-bb03-43d1ee4f41ea` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 07 |
| TripSeat | `trip:trip:b:r2:d07:seat:V01` | `4e3cc44a-513a-5bad-b173-ae9ed47bffef` | `V01`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d07:seat:V02` | `a493b190-8cdc-5008-ad49-de1025dc5683` | `V02`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d07:seat:V03` | `0a17c569-2954-53a6-8ea0-21c521487bb2` | `V03`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d07:seat:V04` | `281e97bc-0002-5247-932f-4117e0285572` | `V04`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d07:seat:V05` | `25bf883f-ebcc-5001-9ea4-f1d80e2fe27f` | `V05`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d07:seat:V06` | `c7da1b99-4aab-53ad-a47a-af49a8871fef` | `V06`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d07:seat:V07` | `86095e10-a54b-53d8-b366-1c4e02c8970f` | `V07`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d07:seat:V08` | `3ec4b6c1-4e13-5af0-b874-225cb783deb8` | `V08`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d07:seat:V09` | `4e8c25f2-c4ee-5023-bbe7-f011738fdd12` | `V09`; VIP/AVAILABLE |
| Trip | `trip:trip:b:r2:d08` | `49d53b63-aec0-58c0-99fe-f3c5f9453ae6` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 08 |
| TripSeat | `trip:trip:b:r2:d08:seat:V01` | `c0c24e81-7f80-5b58-a298-35793de4594c` | `V01`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d08:seat:V02` | `22ea0e36-fd2d-5e9e-9c75-3b2ebe6e7e45` | `V02`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d08:seat:V03` | `cc1c9488-4799-52fa-9ee3-4d5ba34e9140` | `V03`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d08:seat:V04` | `09c33f57-7750-554f-8620-6c35e2e3510d` | `V04`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d08:seat:V05` | `905a8dc1-bd7e-5c5d-a9c5-1d6d12f52148` | `V05`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d08:seat:V06` | `d712496a-5fda-5805-8411-d93b6ccfe5db` | `V06`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d08:seat:V07` | `03fedf37-987d-5d91-a2c3-b33938bcfbfc` | `V07`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d08:seat:V08` | `ab5080d0-45ff-5b22-8c48-37e2bb4908a9` | `V08`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d08:seat:V09` | `d156824a-57d2-5e51-a347-023652465cb4` | `V09`; VIP/AVAILABLE |
| Trip | `trip:trip:b:r2:d09` | `38d78645-1233-5e62-82ea-9eeb97ebadcd` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 09 |
| TripSeat | `trip:trip:b:r2:d09:seat:V01` | `5bec8986-d883-5b45-aedb-18e36cc22203` | `V01`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d09:seat:V02` | `87f77322-7422-5e95-8685-b9ece2998203` | `V02`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d09:seat:V03` | `b90dd78e-f882-55c6-8031-a887bef1fd49` | `V03`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d09:seat:V04` | `5e097e11-adb3-56a5-91d8-ac570cd02bc8` | `V04`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d09:seat:V05` | `05c63b72-3610-51e1-9436-fe75b7b9cc98` | `V05`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d09:seat:V06` | `e3c81dad-a35f-594b-b2ef-f0a6728e9f2a` | `V06`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d09:seat:V07` | `004570ad-8068-5d81-8d47-6fb21e1a5ec1` | `V07`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d09:seat:V08` | `c7a07bcd-b7c6-5a7a-b3a2-5d2346a0594c` | `V08`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d09:seat:V09` | `feebc9e9-39c9-5ebd-99c0-d1aadb2c9c60` | `V09`; VIP/AVAILABLE |
| Trip | `trip:trip:b:r2:d10` | `620efd5d-1e43-5800-9160-fe0ca9917a8e` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 10 |
| TripSeat | `trip:trip:b:r2:d10:seat:V01` | `3083b6e9-b16e-5242-bdc9-8fd9927f6d4b` | `V01`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d10:seat:V02` | `415f6e6c-61ef-51e2-8c59-69272c9cf246` | `V02`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d10:seat:V03` | `82f6dc90-336d-5382-8c77-715ccb7dfb07` | `V03`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d10:seat:V04` | `4c6fac0d-1497-5043-902d-a177a9f55a1c` | `V04`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d10:seat:V05` | `cefa7fad-a3fd-5a99-a491-a1fa256ee3b9` | `V05`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d10:seat:V06` | `733adb4d-5e3c-5ba9-95fa-08d727f1309a` | `V06`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d10:seat:V07` | `f480ca5d-a0b1-5262-916f-f04a20c83f5a` | `V07`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d10:seat:V08` | `effae18c-8849-5595-a648-c57eb7fff6b0` | `V08`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d10:seat:V09` | `2235a44c-6593-5105-acd6-4b98d27274ef` | `V09`; VIP/AVAILABLE |
| Trip | `trip:trip:b:r2:d11` | `674e11cc-2578-50d6-91fc-559b99e40f3b` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 11 |
| TripSeat | `trip:trip:b:r2:d11:seat:V01` | `c65d7437-a05d-51d1-891a-2e8fc05c6f1a` | `V01`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d11:seat:V02` | `df7c907c-e80e-54c9-8018-5da913296294` | `V02`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d11:seat:V03` | `e6f91807-9d0f-5160-8ad3-b886652b7781` | `V03`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d11:seat:V04` | `17939a22-80af-5397-a88d-740bd009f231` | `V04`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d11:seat:V05` | `93631a66-fba3-5851-a782-9a34b9b58162` | `V05`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d11:seat:V06` | `6b2f3e1a-f8e3-526c-93f7-7db683495c36` | `V06`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d11:seat:V07` | `bf5bb28c-913f-5837-a563-12a5d167208d` | `V07`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d11:seat:V08` | `443c4881-a834-5fe2-ac11-725872f480bf` | `V08`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d11:seat:V09` | `edc2dd17-fc66-5a30-87e8-fc4d488516e3` | `V09`; VIP/AVAILABLE |
| Trip | `trip:trip:b:r2:d12` | `89c3e9c8-bc49-527d-9d15-03c2d01fae35` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 12 |
| TripSeat | `trip:trip:b:r2:d12:seat:V01` | `4488d314-8641-50f0-bb94-1f1c4b7265a1` | `V01`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d12:seat:V02` | `3e54d624-3cf0-59e5-9cfe-0b02ad568384` | `V02`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d12:seat:V03` | `091c2c12-8d8e-5092-a7b1-13f65cf4628b` | `V03`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d12:seat:V04` | `2dc1a0d0-b324-5a2f-b3a3-2780ee00e184` | `V04`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d12:seat:V05` | `0ae40eb1-f9c9-50ac-8bf1-56ff38b9261b` | `V05`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d12:seat:V06` | `d9493118-8538-57c0-a558-9978983a5578` | `V06`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d12:seat:V07` | `4c7f3646-1f29-5c24-a635-97fcf832ef50` | `V07`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d12:seat:V08` | `fc363b22-2eb5-5544-9fb3-8c0ed1005f57` | `V08`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d12:seat:V09` | `c75c208e-fcfe-5105-b063-ae0e99944366` | `V09`; VIP/AVAILABLE |
| Trip | `trip:trip:b:r2:d13` | `358791ae-9b39-5dd8-8c40-d3454a5d2c05` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 13 |
| TripSeat | `trip:trip:b:r2:d13:seat:V01` | `8b0dec63-883a-5b89-b65a-ed3a34ddb09f` | `V01`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d13:seat:V02` | `e760b7ac-a4d7-5492-9f65-af2037ce1414` | `V02`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d13:seat:V03` | `8436e320-c014-5d81-a776-6646832b532a` | `V03`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d13:seat:V04` | `8c097843-e6be-5098-82d7-dc1768d238b2` | `V04`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d13:seat:V05` | `efce8b5e-27cc-58c6-a127-50c1f9efe7ab` | `V05`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d13:seat:V06` | `5543a6fd-addb-5b6e-a35d-6118f2e47009` | `V06`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d13:seat:V07` | `38c3e2dd-59cb-5ae4-824e-45eeca43e15c` | `V07`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d13:seat:V08` | `a8a0b735-6051-5703-8a83-0ff47f3bcc2c` | `V08`; VIP/AVAILABLE |
| TripSeat | `trip:trip:b:r2:d13:seat:V09` | `8a836cff-230c-5aca-ab02-c06fc84f060f` | `V09`; VIP/AVAILABLE |
| Trip | `trip:trip:b:r3:d00` | `ef93427d-f9a3-5d99-be36-26e27c0bfd33` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 00 |
| TripSeat | `trip:trip:b:r3:d00:seat:L01` | `a67056f5-8622-5328-8217-3f8828f6a21b` | `L01`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d00:seat:L02` | `bb3a7fd8-fc0f-50d1-9b93-dc0a4af3543d` | `L02`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d00:seat:L03` | `c520cc84-5ba6-5789-bc2f-2b4ea33c0f67` | `L03`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d00:seat:L04` | `2e1b57d8-3b24-582e-9fff-39024bfe3be4` | `L04`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d00:seat:L05` | `5ec56810-b598-5505-a581-5b5d8dc2bcb3` | `L05`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d00:seat:L06` | `f2760c9c-2248-584e-bf27-66e74a3f9be8` | `L06`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d00:seat:L07` | `9189758e-e666-5b2e-bd5f-ac60afb110e6` | `L07`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d00:seat:L08` | `f8734232-355a-5aa1-8956-6fc1e130938b` | `L08`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d00:seat:L09` | `5d900a53-6c8c-5294-bc50-45f050ef2f05` | `L09`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d00:seat:L10` | `d8973820-d0c4-5fb8-a4d7-579168a1f162` | `L10`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d00:seat:L11` | `29bd8930-00e4-5c68-910b-288a33f4f618` | `L11`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d00:seat:L12` | `9a83429d-1356-5ea7-a00d-ce9b520c1e1c` | `L12`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d00:seat:L13` | `379526e9-8113-50e7-91c8-8d60046fd768` | `L13`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d00:seat:L14` | `bad6cb35-ea4e-50be-abfa-d66f766e6a00` | `L14`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d00:seat:L15` | `4f33b96c-398f-58dd-961a-3aa28b101298` | `L15`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d00:seat:L16` | `ed87e767-c5a3-5147-aa40-ed3253da67e3` | `L16`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d00:seat:L17` | `07516c44-450f-557b-bdac-a6fd8681c34d` | `L17`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d00:seat:L18` | `fc880b15-56ac-5c16-8286-20b20b039922` | `L18`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d00:seat:L19` | `0a2a4650-cf84-596f-a2da-aa66e22055f1` | `L19`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d00:seat:L20` | `eed030da-e481-59fa-9880-b2203ad5333a` | `L20`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d00:seat:U01` | `79dec19a-2ee0-55b3-b1c4-52ba5328ccc9` | `U01`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d00:seat:U02` | `4c96d9dc-dca1-5c3e-a953-c398162aaabd` | `U02`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d00:seat:U03` | `9c4b0257-9344-5806-8eb0-e1f2cb87764b` | `U03`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d00:seat:U04` | `6b5a572c-78bd-5aa9-b6dc-8e50c3e969de` | `U04`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d00:seat:U05` | `743f265c-8b2f-5058-bdbf-b986aa10bca9` | `U05`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d00:seat:U06` | `59923fcf-7f42-5781-b758-55aee5fba94b` | `U06`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d00:seat:U07` | `e7e7e24a-ee67-5b29-8648-32cc1ddb6e61` | `U07`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d00:seat:U08` | `f9c916cb-85ca-58f8-8cb8-c879a0a1bea1` | `U08`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d00:seat:U09` | `706c3ed6-0eb2-572e-852e-bdcaa9b0cbf4` | `U09`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d00:seat:U10` | `429d08fa-b2f2-5783-923d-f549e8535ae3` | `U10`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d00:seat:U11` | `41c8a559-a77c-54d2-86af-39c3c17bcf0c` | `U11`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d00:seat:U12` | `f6700d96-c292-52c5-b4db-e46d29dd77ea` | `U12`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d00:seat:U13` | `cfc1fab0-584b-5aa6-929e-c42a69022af0` | `U13`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d00:seat:U14` | `7fe3b186-c658-58a5-9c3a-e2bece334f92` | `U14`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d00:seat:U15` | `5e446b10-1c9e-5747-8acf-aee5840852e6` | `U15`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d00:seat:U16` | `b830ddbd-ad8e-55b6-b4b0-c2afe403025e` | `U16`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d00:seat:U17` | `7d8bb9d0-b0bc-581f-8229-0f9082c98fd4` | `U17`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d00:seat:U18` | `40d95927-c544-58d1-b020-035e4ac8f8b0` | `U18`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d00:seat:U19` | `728a87d0-401a-5f8b-8662-0c016905edc7` | `U19`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d00:seat:U20` | `f372cbaf-120f-5b67-a2f3-ced7d584c2f9` | `U20`; SLEEPER_UPPER/AVAILABLE |
| Trip | `trip:trip:b:r3:d01` | `a2424e44-8da2-575e-a944-6e31244a749b` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 01 |
| TripSeat | `trip:trip:b:r3:d01:seat:L01` | `72b40435-9d20-5a2c-ba93-af21aabf95b2` | `L01`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d01:seat:L02` | `14249445-66b3-5970-bcae-347b168a8519` | `L02`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d01:seat:L03` | `bd636140-d0cf-5737-9027-137e1f059696` | `L03`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d01:seat:L04` | `523a346b-6c1d-5909-9555-c1d7593a22d4` | `L04`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d01:seat:L05` | `666d1bba-c371-598b-81f5-5865b9c8bc22` | `L05`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d01:seat:L06` | `27bbf1e4-f2cb-5113-b7d4-7b5feb313428` | `L06`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d01:seat:L07` | `573b3024-657b-5d0a-99f6-c06799d00066` | `L07`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d01:seat:L08` | `54611aa6-2c8e-50f8-b455-3b86fa6bb63b` | `L08`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d01:seat:L09` | `2b911356-ee31-51f3-a1d1-39260daf00dd` | `L09`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d01:seat:L10` | `ab735d0b-d050-584c-ac3c-bf3344a4d56b` | `L10`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d01:seat:L11` | `1d66c1e7-7804-5da9-9b3a-237f6b005351` | `L11`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d01:seat:L12` | `4669a8d6-2a60-5a62-8657-7f1626ff0f94` | `L12`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d01:seat:L13` | `fbac693f-6009-525d-b82c-bf8716b03b1e` | `L13`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d01:seat:L14` | `30e46d49-ad92-5cfa-a5bd-bbeb929894c5` | `L14`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d01:seat:L15` | `882c8709-cef7-5d25-844f-21386d2cd373` | `L15`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d01:seat:L16` | `beaa7030-7140-5c63-92cc-703d2e6bd718` | `L16`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d01:seat:L17` | `61ee6303-faa5-5099-b361-252af2d87316` | `L17`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d01:seat:L18` | `bcdc5ae9-8021-5e9d-8154-88d34cb8bc87` | `L18`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d01:seat:L19` | `be95b9e7-1c9c-57ad-919f-2db006e11ca7` | `L19`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d01:seat:L20` | `1a379429-1fd2-532d-ba26-4148c3556190` | `L20`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d01:seat:U01` | `87292ce0-11eb-5ff8-b5bd-0c96e6374100` | `U01`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d01:seat:U02` | `5c78edb7-f215-54c9-bdde-696905b4b1c8` | `U02`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d01:seat:U03` | `bdc7eb0b-a202-587b-b959-ecfcc2b507fe` | `U03`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d01:seat:U04` | `a6f4530a-d19f-56b3-a608-f91a65f2fb43` | `U04`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d01:seat:U05` | `47087809-88a8-5d6f-a24a-139f1233f17c` | `U05`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d01:seat:U06` | `097e6930-81ed-58eb-b33b-99ecfac4ea18` | `U06`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d01:seat:U07` | `22cd6270-085d-5460-ab1c-608c6c2c4b80` | `U07`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d01:seat:U08` | `c8e95302-7a68-58e5-8b67-e5f4d2089ac1` | `U08`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d01:seat:U09` | `3f33972a-5f3b-533b-8a6c-479b9466359b` | `U09`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d01:seat:U10` | `87a70f4f-0cf9-5a1e-b19c-fe63387d5b31` | `U10`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d01:seat:U11` | `9c161cbf-5ac5-5daf-bac9-169eb8a376ef` | `U11`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d01:seat:U12` | `8df56f57-757b-55ca-87f0-789363d9ef4b` | `U12`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d01:seat:U13` | `6adf9812-2b23-53bd-b4cd-e2737d3f4209` | `U13`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d01:seat:U14` | `125a1cd0-16a6-555a-86f8-147ce7f90705` | `U14`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d01:seat:U15` | `f337d11a-0150-536c-8ef0-5de7ffc501ee` | `U15`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d01:seat:U16` | `56f3cca8-0fc1-5935-9b34-22c1c5abd27e` | `U16`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d01:seat:U17` | `8ba523e1-ac18-546b-a0bc-52138d33d241` | `U17`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d01:seat:U18` | `2438d30e-82ce-555b-a5f4-be00d086bf8c` | `U18`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d01:seat:U19` | `e8e722f5-804a-5393-8387-7ba2d5094337` | `U19`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d01:seat:U20` | `7f00fbf4-e6d6-5056-ac86-7cc4d5b5b73f` | `U20`; SLEEPER_UPPER/AVAILABLE |
| Trip | `trip:trip:b:r3:d02` | `491b2a6d-8b68-5450-aeaa-f7aed08e33d3` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 02 |
| TripSeat | `trip:trip:b:r3:d02:seat:L01` | `8e08406f-585f-59ea-9a39-015697f8b34c` | `L01`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d02:seat:L02` | `f40b6026-5650-572a-963c-4ac992e39e76` | `L02`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d02:seat:L03` | `76dc49bf-148b-5d03-8276-bd95b0144d78` | `L03`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d02:seat:L04` | `38b4ed48-4593-5029-b8e4-f5fbf566e0f4` | `L04`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d02:seat:L05` | `9d42b1fd-994e-5ebe-95d1-fd812f1b8633` | `L05`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d02:seat:L06` | `cf477ae6-1ba9-504f-b55a-630843b5d13c` | `L06`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d02:seat:L07` | `9f02c05a-e053-57c2-b8b0-a89f227e743c` | `L07`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d02:seat:L08` | `cdb3dc52-891e-5531-80cd-407d7a7942b1` | `L08`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d02:seat:L09` | `214ae795-c125-55e0-9ec7-5c6e7967655b` | `L09`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d02:seat:L10` | `92e2e14e-da6b-5831-950d-a77a610161db` | `L10`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d02:seat:L11` | `1cd2dced-28d2-55c5-a2eb-0208b8ad3de8` | `L11`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d02:seat:L12` | `4cc81ce4-7ba0-5cb3-8626-da1ad41b0c4d` | `L12`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d02:seat:L13` | `2dc60366-79d3-5913-a9db-c3ede14430fd` | `L13`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d02:seat:L14` | `30af20b9-feda-5df8-a9fb-a7b89aad83a2` | `L14`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d02:seat:L15` | `7eb18aa0-d699-5c9a-b8dd-2409a98d755a` | `L15`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d02:seat:L16` | `7638bee8-4bf0-529b-a244-526f087b2a1d` | `L16`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d02:seat:L17` | `1a466cec-c09b-55f4-bbe9-a833b9956941` | `L17`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d02:seat:L18` | `d4955f80-513d-5a99-b177-84f55f2b137c` | `L18`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d02:seat:L19` | `4038296a-5293-5c47-9e05-e3cf8e4cc7de` | `L19`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d02:seat:L20` | `a91b8f17-bbc5-5c97-9938-7e14c838abf3` | `L20`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d02:seat:U01` | `57657a3c-07a8-5ab0-a2a0-92e750cbe756` | `U01`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d02:seat:U02` | `e0a0fa12-2c52-505f-85ed-7bad9bfa5162` | `U02`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d02:seat:U03` | `ed8f4fc6-b9f8-54fb-92f7-f1b6c5f6d1c1` | `U03`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d02:seat:U04` | `333de4f9-edc8-5380-a348-078cd1777994` | `U04`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d02:seat:U05` | `af57c12a-4caf-5b56-ab68-d3dcd19d2062` | `U05`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d02:seat:U06` | `fd457745-f116-5dbb-8de9-daffb5976edd` | `U06`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d02:seat:U07` | `68d38848-b77e-5b34-be38-20696d157be9` | `U07`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d02:seat:U08` | `b37830b7-d144-5c38-ae15-04848a8d590c` | `U08`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d02:seat:U09` | `b7fde7f9-f6b7-5336-b3f6-a1917e2a397b` | `U09`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d02:seat:U10` | `9cdfdcab-ca3a-56b6-907a-0999e32f63f0` | `U10`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d02:seat:U11` | `07c50a13-33f9-5dba-87db-0c83393701f6` | `U11`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d02:seat:U12` | `5123e0fb-3b01-5a1a-8311-ea3660a606c7` | `U12`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d02:seat:U13` | `350263bc-545d-55e7-935b-5620c7d70025` | `U13`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d02:seat:U14` | `adeaf878-87cb-56af-94c6-2d44d1fa5a0e` | `U14`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d02:seat:U15` | `5ae884f3-ec51-51e9-82da-9f3bee0b0583` | `U15`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d02:seat:U16` | `e4b65c5e-c14a-5f42-ad6a-edbe438c92bc` | `U16`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d02:seat:U17` | `b0a68551-2e47-5901-a427-af077763b789` | `U17`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d02:seat:U18` | `9387aec7-c5be-5e7b-8f62-ad6a9c2b8b3c` | `U18`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d02:seat:U19` | `771e31b2-6148-5ad0-98b5-3f8c970eb470` | `U19`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d02:seat:U20` | `1562b1e2-a27c-5a01-802a-08586ed980fb` | `U20`; SLEEPER_UPPER/AVAILABLE |
| Trip | `trip:trip:b:r3:d03` | `cd4f3530-c8fc-5bf5-a9c1-f526c0c66b3f` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 03 |
| TripSeat | `trip:trip:b:r3:d03:seat:L01` | `563c0222-e9cc-5ed3-b227-137ecbd37b0f` | `L01`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d03:seat:L02` | `bdfa5243-4e00-5bd4-8562-6f04fde8130e` | `L02`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d03:seat:L03` | `3efbb654-746f-556f-be78-d6bf6fe02adb` | `L03`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d03:seat:L04` | `3d889b9d-5aff-5d4c-a457-afccedc73eab` | `L04`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d03:seat:L05` | `227c2ee1-77a8-5469-903e-f5a7d780e3ac` | `L05`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d03:seat:L06` | `a7cf695a-c8b8-5403-8c88-daea04072120` | `L06`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d03:seat:L07` | `352acbfd-d0f3-51b1-8cc7-72ffe20eae14` | `L07`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d03:seat:L08` | `d5468f9b-97a4-50b3-8b2d-ccb1ceab77d3` | `L08`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d03:seat:L09` | `449a304c-87c7-5720-92d4-355463f53d05` | `L09`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d03:seat:L10` | `41d920b0-e331-512b-91ba-f54abef4f926` | `L10`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d03:seat:L11` | `23b88634-5241-5bdc-905a-4d20a0bce6e8` | `L11`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d03:seat:L12` | `c997d358-947c-596b-80e9-871b24fcd6cf` | `L12`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d03:seat:L13` | `8ad7a222-d049-5d2a-9f42-db361683c888` | `L13`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d03:seat:L14` | `486dbacd-a50c-5815-9f18-ae60941080f5` | `L14`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d03:seat:L15` | `cd8ed00a-dfa8-5d5b-91c9-e46d5d67a49f` | `L15`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d03:seat:L16` | `41c36172-c7d2-50bd-b11b-41cdc28098ce` | `L16`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d03:seat:L17` | `b985812d-896d-59f4-972d-549bf552b7aa` | `L17`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d03:seat:L18` | `154a607b-158e-5ed0-9316-169747d959cf` | `L18`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d03:seat:L19` | `7a3c95fb-83be-5531-8b78-81d08e912059` | `L19`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d03:seat:L20` | `8ada7075-0aea-51bb-846a-00ff7e8acc29` | `L20`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d03:seat:U01` | `27e2f85a-eeb0-5efe-8993-84f3298e86f6` | `U01`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d03:seat:U02` | `7779c43a-be87-5784-b4e2-81df7efbd9f4` | `U02`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d03:seat:U03` | `9d0128e9-edf4-500b-b0f6-27b7dfdd11cd` | `U03`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d03:seat:U04` | `d49a5dc0-731b-5317-8f68-9bd38fe4364f` | `U04`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d03:seat:U05` | `57c7d60d-af13-5c30-885d-57d3975c0988` | `U05`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d03:seat:U06` | `a6ad3456-878a-53f0-9536-112c07092c3a` | `U06`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d03:seat:U07` | `2188748c-16e6-5095-85ec-6e1899e4c0c3` | `U07`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d03:seat:U08` | `4575c47b-3bbb-523c-b42e-d952a6253c8e` | `U08`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d03:seat:U09` | `53e322ca-4541-5806-8346-1972600b6949` | `U09`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d03:seat:U10` | `b45a9d7c-86e5-55a2-9028-88c63d215fcc` | `U10`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d03:seat:U11` | `29f1fa7a-9f84-5af9-b1f7-accdb955078e` | `U11`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d03:seat:U12` | `2f93af53-ffd9-5b04-8e6d-96b7fa30b406` | `U12`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d03:seat:U13` | `6e887276-546a-5345-9fe7-46c7a16f2bcd` | `U13`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d03:seat:U14` | `9097caf9-f222-5d9b-a82d-59cb5ddae2e1` | `U14`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d03:seat:U15` | `60c37aa3-830e-5e16-9536-f8fd3e5a268a` | `U15`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d03:seat:U16` | `76085ea1-60b9-5202-9c28-53cf9aa9e09f` | `U16`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d03:seat:U17` | `c71ac295-8928-559a-b2a3-869a8f1e8c29` | `U17`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d03:seat:U18` | `3c09d78c-4ff0-506e-a828-03ca11f93bba` | `U18`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d03:seat:U19` | `864909ad-d35b-5009-9972-4680a9178111` | `U19`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d03:seat:U20` | `6d8f8b7d-5e9b-55b2-9c3c-6f149a5d047e` | `U20`; SLEEPER_UPPER/AVAILABLE |
| Trip | `trip:trip:b:r3:d04` | `642366b1-de24-550c-beb5-5fa43a8d5154` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 04 |
| TripSeat | `trip:trip:b:r3:d04:seat:L01` | `7f082ae8-70a7-514b-89de-d62a9cf4fd3a` | `L01`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d04:seat:L02` | `91e5355b-3acf-579a-9552-289a1d768901` | `L02`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d04:seat:L03` | `8274aab0-bd49-5f77-9bf8-40b72097cd3e` | `L03`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d04:seat:L04` | `5c303b79-ff4e-550d-af4c-04fcfc0ef9d0` | `L04`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d04:seat:L05` | `9799eb7d-900d-5809-986c-d0697a5cce76` | `L05`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d04:seat:L06` | `aebc1e61-2fe0-5440-b9ac-4679f3ea20aa` | `L06`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d04:seat:L07` | `a62275cc-dab1-52fb-8e9e-0d86a94766a8` | `L07`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d04:seat:L08` | `2ab6eb87-1983-5ad4-82fd-c087778dd95c` | `L08`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d04:seat:L09` | `1e49a618-81ac-508a-ab64-a4788aa87540` | `L09`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d04:seat:L10` | `c30507f9-4cf9-5794-ba85-c25770879944` | `L10`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d04:seat:L11` | `1c91a941-3f13-5a04-8004-c2a89b8728cd` | `L11`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d04:seat:L12` | `624430fd-9a55-5057-a5b5-701aff36c649` | `L12`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d04:seat:L13` | `dd2c564f-7e50-5ed8-b582-57e9f2c964fa` | `L13`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d04:seat:L14` | `26c59f14-9493-5456-9dd8-644ed78e80cc` | `L14`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d04:seat:L15` | `6164c7d1-9716-5250-86bb-2bc84a6a0791` | `L15`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d04:seat:L16` | `aa2fa570-ab30-5fea-9403-bf4e4fd85917` | `L16`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d04:seat:L17` | `d50a9885-c43e-54df-9ef3-589914949c62` | `L17`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d04:seat:L18` | `6de9d910-1e4c-5581-bcf2-8dd41212a084` | `L18`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d04:seat:L19` | `d715f5ed-cd4c-52df-8f70-d7b21be3037e` | `L19`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d04:seat:L20` | `676fd594-fa38-5032-bb56-84590a2d2996` | `L20`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d04:seat:U01` | `8678d18e-8b17-5410-8d17-b7eb2d441a3e` | `U01`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d04:seat:U02` | `dd607585-d275-5d5f-bf6c-be1c5f0db0f7` | `U02`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d04:seat:U03` | `af63dc70-6628-5ef4-bf83-aabf5966c058` | `U03`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d04:seat:U04` | `5b85213a-7980-51e4-b263-c3c1a99d62f2` | `U04`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d04:seat:U05` | `1e5141ce-fb51-51b0-bdd5-58e18a647444` | `U05`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d04:seat:U06` | `eb98d2e1-f6d9-591d-bfe1-688951431883` | `U06`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d04:seat:U07` | `9972232f-deff-59be-8ad2-f00ad01324e8` | `U07`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d04:seat:U08` | `29f5a9a5-f23e-5e83-9ad1-d5ac9cc82f35` | `U08`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d04:seat:U09` | `e360f1e8-01c6-5a6b-8efa-820f578466b1` | `U09`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d04:seat:U10` | `5dc3c91a-ef30-5d0a-8dbf-ba387d708019` | `U10`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d04:seat:U11` | `7c84be41-222f-5245-a7a2-0c9191ea00b6` | `U11`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d04:seat:U12` | `238b11df-5ab5-53f3-9374-8d7fa3db4b14` | `U12`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d04:seat:U13` | `a62217b4-e919-577c-a575-e5fe9396d059` | `U13`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d04:seat:U14` | `b1cc11e9-e756-5ab8-b6de-456df9ebccbb` | `U14`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d04:seat:U15` | `eaa68cb5-87af-524a-89a4-4e8417772e21` | `U15`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d04:seat:U16` | `156ea07a-6b76-5fdc-9b55-839c54c1a991` | `U16`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d04:seat:U17` | `a6b0da52-c4a1-56e6-9ea0-4ac8fad890cf` | `U17`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d04:seat:U18` | `f1283fab-6fc7-573e-ab6e-6853f1d314c3` | `U18`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d04:seat:U19` | `23e85682-6dea-5777-8e64-8a2a2b98e2c0` | `U19`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d04:seat:U20` | `6d4c0ab9-8524-541f-ac31-692ff8e1d7de` | `U20`; SLEEPER_UPPER/AVAILABLE |
| Trip | `trip:trip:b:r3:d05` | `ece7583c-cfc4-5669-a19f-7e18310d951d` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 05 |
| TripSeat | `trip:trip:b:r3:d05:seat:L01` | `b68bdc79-c445-5e70-ac2f-8c4d0ef70aac` | `L01`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d05:seat:L02` | `df61c46f-5c82-5a36-a4b8-51917b1912f3` | `L02`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d05:seat:L03` | `c34538d6-7bd8-51c6-a3ba-579467840454` | `L03`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d05:seat:L04` | `d79c5244-26bf-5600-b536-5483d38788d5` | `L04`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d05:seat:L05` | `4c2f9dc6-e0b4-5305-bd60-9e1102334ad6` | `L05`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d05:seat:L06` | `9a1235af-b3db-5603-a60e-a3b0f8a2221f` | `L06`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d05:seat:L07` | `1e963490-72e3-54a1-a7a8-8da23765e7a7` | `L07`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d05:seat:L08` | `6b5fc618-1ea5-54b8-b8ae-d551c0edca62` | `L08`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d05:seat:L09` | `394d3158-8c13-5fc8-bc44-6cc6486179a8` | `L09`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d05:seat:L10` | `2055c2a3-c5b4-59fb-aee7-a3f2a578c466` | `L10`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d05:seat:L11` | `f02f4f12-0cee-5e0b-98a0-c71cffaa0640` | `L11`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d05:seat:L12` | `3341198f-efa3-5fc2-ad88-39563226d014` | `L12`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d05:seat:L13` | `c00f0572-05a7-5d4b-91ff-f9a2328facac` | `L13`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d05:seat:L14` | `b364d6d4-ed82-5421-b1d9-5dbdfad64952` | `L14`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d05:seat:L15` | `a17699c7-65f9-570d-80f7-ffb341bdcefd` | `L15`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d05:seat:L16` | `df9997f4-5120-5ac8-af6f-d98474a0c14f` | `L16`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d05:seat:L17` | `8069d1ab-3ed6-5d7b-8219-9909e5f2a56b` | `L17`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d05:seat:L18` | `04661860-550c-5378-a881-bb7c5d4c7849` | `L18`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d05:seat:L19` | `6237e0bb-e7f4-56ac-89f8-4a4dcf8712ee` | `L19`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d05:seat:L20` | `a3401c16-1432-5f8b-a09d-ff9f799299cb` | `L20`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d05:seat:U01` | `7b507aa4-6468-5791-97b1-bddb71e2ea30` | `U01`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d05:seat:U02` | `65081608-700e-5585-87e9-b7a74b04637b` | `U02`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d05:seat:U03` | `52299c0f-c224-588f-8510-65e9f1494c3d` | `U03`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d05:seat:U04` | `63602e89-8d91-5567-9ef3-23ddf51f4dec` | `U04`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d05:seat:U05` | `48ccae4d-2712-5d18-b110-05e0c121ff51` | `U05`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d05:seat:U06` | `256bafe3-8c68-506a-93b8-989abc0aa357` | `U06`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d05:seat:U07` | `5f70af2c-f6ad-5daa-9037-d632816014c9` | `U07`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d05:seat:U08` | `7be80d17-445c-5b8b-a2b3-60d0113bab3a` | `U08`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d05:seat:U09` | `dafa8386-9e8c-5b23-8913-08c9d2b8e321` | `U09`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d05:seat:U10` | `4440cd60-c887-5a7f-a366-9bc5115aae9e` | `U10`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d05:seat:U11` | `bb86c12f-84b8-58a0-8339-d54e92b186d3` | `U11`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d05:seat:U12` | `d4cbec7a-f960-5863-b309-8539f24acdba` | `U12`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d05:seat:U13` | `f45d4652-54b0-5ed6-b1a2-278c1109589b` | `U13`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d05:seat:U14` | `ab9f1e21-265b-58f6-99f7-dd1815ad6160` | `U14`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d05:seat:U15` | `096c4edb-9f9c-58ef-9ddd-a909ec2e0976` | `U15`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d05:seat:U16` | `a05cff0d-972d-5ef6-b345-c0ea4aac7eaa` | `U16`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d05:seat:U17` | `c282ba42-e802-5ddd-adea-65d07f437fbc` | `U17`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d05:seat:U18` | `d2458fe5-5f23-524d-9059-be0228734e82` | `U18`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d05:seat:U19` | `61c5c5f2-9c43-5e21-8481-b87f486c3d53` | `U19`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d05:seat:U20` | `e5dce8a1-11c3-538a-9851-a02ae0cd2c91` | `U20`; SLEEPER_UPPER/AVAILABLE |
| Trip | `trip:trip:b:r3:d06` | `a28b2fb8-b364-5eb7-b979-948b8a9fa00c` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 06 |
| TripSeat | `trip:trip:b:r3:d06:seat:L01` | `b62bbf22-6763-56bc-a887-0b7cd05fdff1` | `L01`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d06:seat:L02` | `c4875ec8-02d7-5a83-94c6-21762ff0ec0c` | `L02`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d06:seat:L03` | `f283bd8b-9364-52b3-9a9e-97ce63be9caf` | `L03`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d06:seat:L04` | `4c6f144b-0bd6-5426-95c3-16972b81a709` | `L04`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d06:seat:L05` | `97314d25-4052-5cf8-af82-64cc4077ab0c` | `L05`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d06:seat:L06` | `98869540-bdd1-588a-9e4f-53247dc04ea1` | `L06`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d06:seat:L07` | `e96a9418-2135-5362-ac2a-903ff1d8424d` | `L07`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d06:seat:L08` | `f8e8a9e7-50ff-54aa-a5a3-b37a212095e5` | `L08`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d06:seat:L09` | `b75033c3-4988-5c30-832c-f86ded6f8e75` | `L09`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d06:seat:L10` | `a2d8ab71-9435-5cdc-9bab-5fa9d8e462ae` | `L10`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d06:seat:L11` | `f15ef43a-a834-5ea0-ba95-30ded79a9d2e` | `L11`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d06:seat:L12` | `8f9f5953-ec47-5afd-81e6-2672f4465f83` | `L12`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d06:seat:L13` | `2cb03c1c-0b09-59b1-8510-94533c744531` | `L13`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d06:seat:L14` | `59bc4f35-7765-591b-a4c7-cea80d21dce8` | `L14`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d06:seat:L15` | `2617251c-b327-5119-b50e-97c424a17068` | `L15`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d06:seat:L16` | `0524210f-abc8-5329-ae53-8809ad278406` | `L16`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d06:seat:L17` | `1d824820-e155-5393-bc9e-e8b992a15a5e` | `L17`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d06:seat:L18` | `97467f08-d897-52ce-9f70-745369828a6b` | `L18`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d06:seat:L19` | `83fa0884-49cb-5fdb-9e56-1307209570c2` | `L19`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d06:seat:L20` | `b807c674-fd12-59a8-b44f-d5067c73c62e` | `L20`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d06:seat:U01` | `02d42093-c038-55b0-9da0-dfcee81f8956` | `U01`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d06:seat:U02` | `591e42b3-20ce-5d0f-93f1-d9f1acee7488` | `U02`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d06:seat:U03` | `4379a5a4-4e56-5c01-acc3-2fc48f64ae5b` | `U03`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d06:seat:U04` | `761c6e8d-ddfc-5e22-a830-c6a17e508e72` | `U04`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d06:seat:U05` | `0dbe6f5c-c793-5757-a1b8-8d9fd7b67555` | `U05`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d06:seat:U06` | `4201caf1-4aee-597c-98d4-83d429f1125e` | `U06`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d06:seat:U07` | `f50e1a28-cf6f-5fdd-81be-310a0dd44241` | `U07`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d06:seat:U08` | `4ea399fe-7c35-5124-a9dc-dc04967d9977` | `U08`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d06:seat:U09` | `07641d27-5a1b-53b0-94db-00fefc057e92` | `U09`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d06:seat:U10` | `56c4dfe2-8c14-5789-977d-1606e61a6ddd` | `U10`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d06:seat:U11` | `068e0d5d-91cf-50c0-a445-ac08eb74452c` | `U11`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d06:seat:U12` | `37b9f50c-c4d2-538e-941a-18414bca5763` | `U12`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d06:seat:U13` | `75696840-7381-5590-87e7-4cd29fdd4abd` | `U13`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d06:seat:U14` | `180e4c2c-a9e5-5d8a-a56f-73aab9460280` | `U14`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d06:seat:U15` | `e86311c7-1e83-517b-9c82-80c710dfa1f3` | `U15`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d06:seat:U16` | `04ad322f-a6ac-5fff-9563-8b106072f300` | `U16`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d06:seat:U17` | `c5243b16-817b-5fe3-8428-11c191abbeae` | `U17`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d06:seat:U18` | `b4af2a8b-d70d-5fb1-b208-8b30b0ac6c9f` | `U18`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d06:seat:U19` | `11239da5-ddc3-5c3a-ba2c-0b043d570b0a` | `U19`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d06:seat:U20` | `df292a5a-5282-573b-b728-33ad87cd51b5` | `U20`; SLEEPER_UPPER/AVAILABLE |
| Trip | `trip:trip:b:r3:d07` | `b606947d-0a25-5cca-a859-77ce305ffadc` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 07 |
| TripSeat | `trip:trip:b:r3:d07:seat:L01` | `8c4985bf-cf8c-558f-8dda-b6d3bef36696` | `L01`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d07:seat:L02` | `99ef1558-35c4-5baa-8471-4bc7776eb3cb` | `L02`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d07:seat:L03` | `407d2aaf-3769-56d4-8d37-d0c832dbd66f` | `L03`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d07:seat:L04` | `bb638e9b-f7ea-5a32-8ce7-5698000d2d16` | `L04`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d07:seat:L05` | `e2ab5739-ba3d-52bc-9b82-035e7d4878da` | `L05`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d07:seat:L06` | `2f9e5bf9-14d1-558e-953b-3025aed83234` | `L06`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d07:seat:L07` | `5bd64677-8b33-5593-99b7-df281937733f` | `L07`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d07:seat:L08` | `94632a4a-d6b0-5fb1-a845-7445e1ec0d34` | `L08`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d07:seat:L09` | `07f39196-4f13-5720-8bbd-08a3d4302b22` | `L09`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d07:seat:L10` | `15072cdf-08e2-5ff5-abe4-581c04dc36c1` | `L10`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d07:seat:L11` | `9ca189dc-a2af-590c-aa17-bbd42f6bca69` | `L11`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d07:seat:L12` | `ca3d8a7c-840a-57b1-849f-ecc4171aec97` | `L12`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d07:seat:L13` | `831b8312-6190-5357-bb40-ffa72436fcba` | `L13`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d07:seat:L14` | `984c5d5b-5cfa-53c2-bdb5-ea49008788e3` | `L14`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d07:seat:L15` | `8e431853-4f9a-5048-a44c-991980022818` | `L15`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d07:seat:L16` | `41b7fe73-839a-5209-9b42-a9f6136c41f7` | `L16`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d07:seat:L17` | `bf76ca8e-b9be-54d3-ad75-9786c79d7cc1` | `L17`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d07:seat:L18` | `fcd83f59-16ba-599d-acc4-056c29883b44` | `L18`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d07:seat:L19` | `d7d44380-4bcc-5e35-80f5-5659f4512888` | `L19`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d07:seat:L20` | `7192e871-08a7-52ee-b50c-2a5e0ed59209` | `L20`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d07:seat:U01` | `21a11331-5e79-5300-9a48-d2d73f20f114` | `U01`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d07:seat:U02` | `f23c775b-b1cb-5a75-bb57-f2d80f34bac8` | `U02`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d07:seat:U03` | `4be0b64a-33e6-559a-9da8-abaff38bebf6` | `U03`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d07:seat:U04` | `b96cc729-7c19-52ef-bdd9-028a9bf8d261` | `U04`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d07:seat:U05` | `676eed99-343f-5c0a-b3c3-b2def5275dd7` | `U05`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d07:seat:U06` | `6382e649-f8c3-5d31-8bf8-f14cbacee9f4` | `U06`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d07:seat:U07` | `939ea832-8b0f-5799-ac1b-3b9e7a98ad5f` | `U07`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d07:seat:U08` | `7d13b36d-5bdc-5909-a868-b41aa82ca68e` | `U08`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d07:seat:U09` | `fa3f14ee-1e22-518e-b326-b9f2fef5a872` | `U09`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d07:seat:U10` | `0b90c4cf-92f6-5fda-b9f7-ed6f4ac51ebf` | `U10`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d07:seat:U11` | `022136d8-581a-5c51-a0aa-43e6d46b45bf` | `U11`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d07:seat:U12` | `7189d32e-3f07-5e96-beae-76d50c812dd2` | `U12`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d07:seat:U13` | `4cc9a06b-bb8d-5756-b140-408c7eaa249d` | `U13`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d07:seat:U14` | `2d617da5-8902-5313-bd42-ce822cd465dc` | `U14`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d07:seat:U15` | `7143725c-1c3c-5316-91da-da00cfb6ab91` | `U15`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d07:seat:U16` | `e24afd86-9ee0-5d47-adba-71a845aba31f` | `U16`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d07:seat:U17` | `b88df1ff-6c74-5d9f-916b-41d3d7d2cca7` | `U17`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d07:seat:U18` | `1e3c736e-f298-5670-a8f7-6b663783fcf2` | `U18`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d07:seat:U19` | `d5e2b964-6fea-518b-a1ca-09a88f6b9837` | `U19`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d07:seat:U20` | `9e6d6a35-a375-5140-94e1-ebf2d67539d1` | `U20`; SLEEPER_UPPER/AVAILABLE |
| Trip | `trip:trip:b:r3:d08` | `636e1e11-a5f8-58ff-8af1-b5f800a16d72` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 08 |
| TripSeat | `trip:trip:b:r3:d08:seat:L01` | `176f763b-49e5-580f-a1d1-b00308b7e9c8` | `L01`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d08:seat:L02` | `b49d1fb9-10df-539c-976a-eaecaf1243ae` | `L02`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d08:seat:L03` | `acc0834d-b87e-5fda-9144-441574ae8187` | `L03`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d08:seat:L04` | `683d2199-e025-5393-b0c8-3f36f9c1b4b9` | `L04`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d08:seat:L05` | `edb4a963-4c72-5cc5-882c-6b543a00d69f` | `L05`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d08:seat:L06` | `360867dc-2a30-501b-a117-24ebfc0ccead` | `L06`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d08:seat:L07` | `3954a5f8-72d3-579f-bdf2-f85dd763e1b1` | `L07`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d08:seat:L08` | `a61a5e74-07c6-56b1-8887-7878bf18fc7b` | `L08`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d08:seat:L09` | `a98088d2-094f-5e12-a52f-cf820e4cd6ee` | `L09`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d08:seat:L10` | `d5dc12d8-65e6-5d73-b109-36de7f229bc0` | `L10`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d08:seat:L11` | `600451c1-500e-575d-bc7e-b04328757750` | `L11`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d08:seat:L12` | `b3518633-25db-5612-b56d-40245fd80177` | `L12`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d08:seat:L13` | `125f8a8c-75f9-589e-9135-68893b80b831` | `L13`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d08:seat:L14` | `5842d0a3-60d9-51d6-b3a0-15f396c031cc` | `L14`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d08:seat:L15` | `fc347d94-6703-50dd-8b4a-5fec0a6a3c35` | `L15`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d08:seat:L16` | `e37113ea-e19f-5ff0-bc50-a708e0fc80e8` | `L16`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d08:seat:L17` | `9b4bb11c-1233-52cc-9080-481ea8c8998e` | `L17`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d08:seat:L18` | `19d5eba1-8d01-5ecb-aa49-8a6e00f3a6db` | `L18`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d08:seat:L19` | `c78ee219-5ff3-5481-a1d4-e1745670e5d1` | `L19`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d08:seat:L20` | `f4796786-0143-5333-903b-a5f05cfde884` | `L20`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d08:seat:U01` | `8cc19361-7c5b-517d-a7f8-43ad2082f2e4` | `U01`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d08:seat:U02` | `fca83b31-bd0e-59d4-be7c-825810f9ecdc` | `U02`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d08:seat:U03` | `37b20045-a43b-572e-a302-16d8cea301a5` | `U03`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d08:seat:U04` | `9b031785-348e-5f2c-a902-e1a11216377b` | `U04`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d08:seat:U05` | `ba3644c7-902c-5743-b911-9f7c6892f4a0` | `U05`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d08:seat:U06` | `087a6970-60e0-5a7e-ab5c-d0b2da722a81` | `U06`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d08:seat:U07` | `24376ce8-0ea3-53b9-874e-b1336296ebef` | `U07`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d08:seat:U08` | `0cc655c2-4586-5831-b8a0-3f2fce3fd645` | `U08`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d08:seat:U09` | `c356504b-aeee-56a8-b7b9-f1c91b6bf744` | `U09`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d08:seat:U10` | `568dcb46-fe55-5f14-9246-643783b96d94` | `U10`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d08:seat:U11` | `ea970851-0a33-5e89-aab0-b3560bccd437` | `U11`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d08:seat:U12` | `94828c0b-61a4-5d56-ab92-0543b6aee235` | `U12`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d08:seat:U13` | `22953784-f346-5d26-8b3e-561117a7ac4a` | `U13`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d08:seat:U14` | `bdc57624-8877-58b1-aed0-df05b15c64c6` | `U14`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d08:seat:U15` | `f0dbc828-6b46-5750-9318-5ae0a6a7e597` | `U15`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d08:seat:U16` | `ec616677-29ac-5185-8f74-949874a5e537` | `U16`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d08:seat:U17` | `44980311-3590-5812-9fbc-4c58dac4bbc5` | `U17`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d08:seat:U18` | `b6e65ee1-89ea-5309-bea8-72c621a3c808` | `U18`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d08:seat:U19` | `744ab3d9-66b3-59b1-8b33-5037c64a3335` | `U19`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d08:seat:U20` | `6fda902e-564d-595f-abbf-e0f8435bb500` | `U20`; SLEEPER_UPPER/AVAILABLE |
| Trip | `trip:trip:b:r3:d09` | `23c2cc02-dd09-54b1-ae1a-f201c542937a` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 09 |
| TripSeat | `trip:trip:b:r3:d09:seat:L01` | `9b625be1-db3e-5156-a240-edcff4c0f015` | `L01`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d09:seat:L02` | `5c8bef0d-b7b6-5697-b074-89fd7b632487` | `L02`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d09:seat:L03` | `e88d7961-5aea-547b-b999-e43dec9a5f40` | `L03`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d09:seat:L04` | `3912ff9f-53ff-56b4-9bcd-8a7e5b8dcafa` | `L04`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d09:seat:L05` | `952f9c6d-d34d-555e-a991-910b1897fc04` | `L05`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d09:seat:L06` | `cc99528b-71e0-55e5-8956-eea1612677a3` | `L06`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d09:seat:L07` | `69aaa2ba-1f93-5eb2-a223-ceee821cbff9` | `L07`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d09:seat:L08` | `c4f1ab0a-cfd3-508c-8e95-ba806056f537` | `L08`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d09:seat:L09` | `71430988-4cd9-5ee4-8c97-07c82ed98d9f` | `L09`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d09:seat:L10` | `9a641f62-17a2-583b-99f5-844c63e0bf63` | `L10`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d09:seat:L11` | `5f8a9abd-dd94-5cab-95ff-4d222844b4a9` | `L11`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d09:seat:L12` | `81615903-bd2f-50f5-a994-7eb957a29d65` | `L12`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d09:seat:L13` | `452954a8-34c2-5452-925e-0ce9be63d207` | `L13`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d09:seat:L14` | `14b90d5a-4f1e-5268-9eac-ceedcaae440b` | `L14`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d09:seat:L15` | `90b0f8be-70a5-50bb-bbc4-aab5313c3dbd` | `L15`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d09:seat:L16` | `6fd22d88-e4db-546a-8668-e37b7f55e751` | `L16`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d09:seat:L17` | `364b998e-848b-5745-904f-adf3c347871b` | `L17`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d09:seat:L18` | `10e016f2-1125-56f1-99f9-b6078bc65a2c` | `L18`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d09:seat:L19` | `6a85c1d9-06fa-59b0-9f9a-fafa81de264b` | `L19`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d09:seat:L20` | `5cdb6bc1-9ccf-5273-beb4-3b57765c2ed7` | `L20`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d09:seat:U01` | `4fb37796-8a26-591e-9407-ebc2544ff43c` | `U01`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d09:seat:U02` | `26ea8cce-a954-5134-a919-2e5a4d5066e2` | `U02`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d09:seat:U03` | `df9b45e0-4369-5979-881a-a69b83937bf9` | `U03`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d09:seat:U04` | `1d35d88d-e3db-5493-b074-aa15f29a65f2` | `U04`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d09:seat:U05` | `b6896b63-2094-504f-910e-4c80e746fda1` | `U05`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d09:seat:U06` | `826ac979-f9c0-5412-b274-f2d43c968840` | `U06`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d09:seat:U07` | `263aa943-ee3b-537b-95ef-846839c1d041` | `U07`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d09:seat:U08` | `a15bc9c4-b6ad-58a8-96d5-9f6fdc59299a` | `U08`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d09:seat:U09` | `a0323486-42f8-56d6-a3d4-6630c62287b6` | `U09`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d09:seat:U10` | `ae08c9d3-2c3b-5454-b13d-9e17a1c411b1` | `U10`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d09:seat:U11` | `7deaee4a-a67c-54c1-b590-310fca421649` | `U11`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d09:seat:U12` | `a91ee87d-d7bc-55e6-8b4e-dabe09007584` | `U12`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d09:seat:U13` | `eab18f1e-00db-54ec-aa23-1d00ddeabd0b` | `U13`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d09:seat:U14` | `8ccc9564-41b8-57d7-a111-c031d41473fb` | `U14`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d09:seat:U15` | `8baa1e76-22f2-5273-9b5b-6cb4a008ee1d` | `U15`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d09:seat:U16` | `eeccbb50-acf3-59b6-a330-6da944cb1e0a` | `U16`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d09:seat:U17` | `4a87455c-21ec-56b0-8a56-64e44fe5577b` | `U17`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d09:seat:U18` | `e7c2b006-b722-58d1-8135-5f4315d17081` | `U18`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d09:seat:U19` | `e4fa0363-fe8b-5f33-948a-c4e33dd77ad8` | `U19`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d09:seat:U20` | `a5827803-f49f-5508-8467-f591c8c1459c` | `U20`; SLEEPER_UPPER/AVAILABLE |
| Trip | `trip:trip:b:r3:d10` | `8af525d7-1e3e-5eae-aa09-87174406e6a1` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 10 |
| TripSeat | `trip:trip:b:r3:d10:seat:L01` | `a49acd5c-b045-5e3f-995d-5ccb782449d7` | `L01`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d10:seat:L02` | `9f0b93c9-2b4a-528c-ad5d-908f191dca6a` | `L02`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d10:seat:L03` | `cffe4fb3-1d0a-5b5b-9b55-29709e21b4a7` | `L03`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d10:seat:L04` | `26feb78d-6efe-5762-9a07-058e8549b17e` | `L04`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d10:seat:L05` | `a20d9509-bccf-52b7-8138-2e6b8aa58f75` | `L05`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d10:seat:L06` | `587fc550-ce56-5ae7-a34b-cbdbc895eb58` | `L06`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d10:seat:L07` | `6aec8ac6-a6f1-5716-990c-29bc1ec7a626` | `L07`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d10:seat:L08` | `be2ff5f2-cd9e-5320-88c7-4b8fe1317904` | `L08`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d10:seat:L09` | `b0f22943-412c-524d-863c-ee95761d6b2d` | `L09`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d10:seat:L10` | `5177af52-e4db-5715-b72f-df30bf157502` | `L10`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d10:seat:L11` | `cc7504bf-df55-53fd-a8a5-19fc4e063ad6` | `L11`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d10:seat:L12` | `eadec8f9-b0c9-5b9b-b641-cfc2112dba72` | `L12`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d10:seat:L13` | `d24b5948-39fd-57b8-8dde-ce67e13332ed` | `L13`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d10:seat:L14` | `f62f2930-d2f0-5108-aa0e-72426d36b5e6` | `L14`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d10:seat:L15` | `f8f02dda-cfa8-5c45-96a5-8e702834db2a` | `L15`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d10:seat:L16` | `3344d518-2d18-53b2-ba8b-5882552ef6e2` | `L16`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d10:seat:L17` | `00259960-07f8-5c40-a7ad-0576c39d1dfa` | `L17`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d10:seat:L18` | `e6c04861-ecce-596b-b3ac-caf2790b19a6` | `L18`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d10:seat:L19` | `25ecd54b-e2a4-5cc8-b515-09c7486c8db2` | `L19`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d10:seat:L20` | `bfbff67a-ca65-5b30-b375-26c35db5356d` | `L20`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d10:seat:U01` | `985c99f3-5a4e-5d1a-a593-15fc4b4c958c` | `U01`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d10:seat:U02` | `03df9534-68ed-527f-be10-f0d0831add08` | `U02`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d10:seat:U03` | `ff072e19-02e9-533a-8374-97a52b6dde75` | `U03`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d10:seat:U04` | `cab2c081-f50c-5e6e-a660-02424a864b18` | `U04`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d10:seat:U05` | `ed2b6344-8075-517f-a445-99fe0a11110a` | `U05`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d10:seat:U06` | `c37475f9-8e95-5d45-8ce5-128442f52127` | `U06`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d10:seat:U07` | `2261426c-f730-5854-a64e-8c5ac32cc124` | `U07`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d10:seat:U08` | `e0e61cc9-391f-53f4-9623-01e14bcfad2e` | `U08`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d10:seat:U09` | `ea6427b0-f0a2-59e4-a393-d661fe35cf17` | `U09`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d10:seat:U10` | `fd0d5f27-e994-564f-86bb-302011dac5cc` | `U10`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d10:seat:U11` | `a3d1b5b9-ff4f-5062-8aab-ef86efece2a5` | `U11`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d10:seat:U12` | `ac5790ee-6699-5798-bd85-836d0f0a0abb` | `U12`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d10:seat:U13` | `598d118f-a3d6-53b7-bb46-31dc94b4c228` | `U13`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d10:seat:U14` | `f9395cb1-0404-59cc-8507-3a3f9a4ba406` | `U14`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d10:seat:U15` | `be4bd9ee-4993-5ce5-8614-71e570516bed` | `U15`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d10:seat:U16` | `2a5bef5a-425e-54a1-aa52-4b7cf7e673ae` | `U16`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d10:seat:U17` | `6c1bb47b-834b-5d71-bb16-1b55eb597e8a` | `U17`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d10:seat:U18` | `9abcfeb1-ee10-56d0-bfa0-923fe36247b1` | `U18`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d10:seat:U19` | `4fb44de7-0058-5e96-849a-09eb3fcd67df` | `U19`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d10:seat:U20` | `34408fd7-fa70-562f-b82e-606485c9d3f3` | `U20`; SLEEPER_UPPER/AVAILABLE |
| Trip | `trip:trip:b:r3:d11` | `cf2a2f2c-9889-594f-bf71-c6d183dd4a97` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 11 |
| TripSeat | `trip:trip:b:r3:d11:seat:L01` | `04f31950-6dad-574d-af4d-b96da4fd4c48` | `L01`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d11:seat:L02` | `8353f59b-d618-5383-9ac0-7a3d048166f5` | `L02`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d11:seat:L03` | `775f2a58-2c2e-53e3-95e4-cdd152e91b56` | `L03`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d11:seat:L04` | `6e716396-3080-5bec-9542-b9cec0f5c5cf` | `L04`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d11:seat:L05` | `bf03eb86-2f76-5028-ade8-8b3110657ece` | `L05`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d11:seat:L06` | `454a7a37-e399-50b2-ba38-97b291a18d5f` | `L06`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d11:seat:L07` | `9c11bf65-c208-57a1-9cd7-9d1f605d2ab9` | `L07`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d11:seat:L08` | `f6f78bc9-90c7-5c9e-bfae-4564d9f24316` | `L08`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d11:seat:L09` | `02ffc6bd-afe3-5169-bf3c-d31180a9f6d8` | `L09`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d11:seat:L10` | `b25ba395-8948-5be4-8f0b-6dac1737e991` | `L10`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d11:seat:L11` | `89930fd6-29af-5d9b-823a-7eb7c7e33ffe` | `L11`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d11:seat:L12` | `dbc0f5d1-5db0-5255-9129-605102c7e372` | `L12`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d11:seat:L13` | `82e6b691-b5aa-5409-bb4a-0df613497d35` | `L13`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d11:seat:L14` | `fe72abd3-821e-51b2-bb53-d73f9d6b4fde` | `L14`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d11:seat:L15` | `add714a7-9f9d-5292-a33e-283c0608c78e` | `L15`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d11:seat:L16` | `3892e3be-1685-5878-990c-e34339a491fb` | `L16`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d11:seat:L17` | `51ab9e73-b7c6-5c58-ab08-3e595d575c03` | `L17`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d11:seat:L18` | `208e898d-1b9e-5f50-880d-5668d24141aa` | `L18`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d11:seat:L19` | `9f80d173-9f06-5b1e-a313-20155da002b8` | `L19`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d11:seat:L20` | `0d589399-5c74-5e3f-9184-8f107186c631` | `L20`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d11:seat:U01` | `6248b5fb-baba-5c8e-b4c1-0e8294c29bbb` | `U01`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d11:seat:U02` | `ad8f8185-b018-5f49-b08f-756829120fc1` | `U02`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d11:seat:U03` | `c46fded3-5785-5983-9f81-8e091cb8d560` | `U03`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d11:seat:U04` | `eda620d6-108b-59df-b60b-3c1c8b829f5b` | `U04`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d11:seat:U05` | `58c8cb2d-45b5-5fe4-9cdb-dc57746526f6` | `U05`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d11:seat:U06` | `36fa099c-d7cf-59b8-a946-30e806a404ce` | `U06`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d11:seat:U07` | `e3a075be-e799-5694-a5eb-2b3942bb32a2` | `U07`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d11:seat:U08` | `38347504-c790-58b6-9e2b-98028ed78d94` | `U08`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d11:seat:U09` | `96ea3fca-44b9-5818-b095-38873359ca0d` | `U09`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d11:seat:U10` | `0b155328-8545-5a1e-a80e-a761e32b9b1e` | `U10`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d11:seat:U11` | `6d023417-8b7b-5e9d-87bc-00a54eacf674` | `U11`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d11:seat:U12` | `4c288b41-a58e-5bef-b9d4-caaea2d89326` | `U12`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d11:seat:U13` | `524783a8-1a93-56e2-9907-67d8bd1c1f76` | `U13`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d11:seat:U14` | `9bd62fc8-4640-545e-b6f8-c843c0952ccc` | `U14`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d11:seat:U15` | `52baadc7-2547-593b-bc99-5a3ae18a6a30` | `U15`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d11:seat:U16` | `e5a00864-3b42-5eb8-8109-770d7ffd9d05` | `U16`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d11:seat:U17` | `c4a42aff-c261-5931-a3d2-b0cf09dcff62` | `U17`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d11:seat:U18` | `49097d8f-b9a8-5f96-aabd-728b7674168d` | `U18`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d11:seat:U19` | `6639076f-1e15-5000-9c31-adb5e2d021ca` | `U19`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d11:seat:U20` | `8dd74754-89ec-59f4-ab99-e73251060869` | `U20`; SLEEPER_UPPER/AVAILABLE |
| Trip | `trip:trip:b:r3:d12` | `a74b1d11-3790-5f9d-8719-36d570462dd0` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 12 |
| TripSeat | `trip:trip:b:r3:d12:seat:L01` | `94c6a7e0-65f9-5ef4-9020-239f6cad1f7e` | `L01`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d12:seat:L02` | `17d53106-6008-55fe-afac-c1e17c7b7508` | `L02`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d12:seat:L03` | `de8d2ede-dd17-51e1-9120-6f1aa4146e43` | `L03`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d12:seat:L04` | `f0164ccc-fbef-5f50-9109-15199a48cb11` | `L04`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d12:seat:L05` | `57cb3d75-aa51-5b01-b8e7-814712b5d4e7` | `L05`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d12:seat:L06` | `e73879a3-dd90-57c3-b08d-52de8f62123c` | `L06`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d12:seat:L07` | `339fbe9f-b9c2-5eaa-b735-279e00203768` | `L07`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d12:seat:L08` | `863c60d8-6fa9-5c10-9508-a59d6275c470` | `L08`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d12:seat:L09` | `f74dc727-5be5-5041-b77a-c4f9700208c8` | `L09`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d12:seat:L10` | `6ca34ab0-b009-5765-9868-cf19e2a0b663` | `L10`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d12:seat:L11` | `0dc9faa3-b1ae-5989-9ff0-0306596010d3` | `L11`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d12:seat:L12` | `0da76ad9-228e-5548-9950-f3aa8c30aeab` | `L12`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d12:seat:L13` | `4a00af0e-42e4-54e2-ad1c-6ebc3bea54cd` | `L13`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d12:seat:L14` | `af8d9460-4bc0-589a-8414-6cbc98f78cfe` | `L14`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d12:seat:L15` | `60c4b976-bb5e-5ef7-ae75-1af760acfbf7` | `L15`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d12:seat:L16` | `2eea0feb-969d-57ea-afe8-41b981074b45` | `L16`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d12:seat:L17` | `cdeeef6e-a323-531d-80e2-adc561c93f2c` | `L17`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d12:seat:L18` | `70a44a6a-2c6c-5e19-930b-5df9a7f521ac` | `L18`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d12:seat:L19` | `17e3acb9-3406-593e-9e7b-d231084a0d99` | `L19`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d12:seat:L20` | `3ebc2ae9-be02-50a0-8406-bdbac3a244dd` | `L20`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d12:seat:U01` | `55e5034e-8937-54ae-b63d-6e236ee4a564` | `U01`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d12:seat:U02` | `a0ed2ddf-1bf2-5b3e-82d2-073e72c7e15f` | `U02`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d12:seat:U03` | `8f10247f-1738-53f5-bfd0-034b992280c7` | `U03`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d12:seat:U04` | `d16b18a8-853b-5b07-a175-79b14fa51667` | `U04`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d12:seat:U05` | `88bbc068-57b1-5720-a85b-9e7ab78e0533` | `U05`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d12:seat:U06` | `9b8c416b-bc08-5c66-a531-070b57d7d8e6` | `U06`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d12:seat:U07` | `dc7c0e66-301a-5222-84cc-31bab02d8062` | `U07`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d12:seat:U08` | `b9b377e5-d996-5096-8e29-8cc4ac9439ef` | `U08`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d12:seat:U09` | `20ab96d2-304e-597b-9aaa-59da1f80dfb0` | `U09`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d12:seat:U10` | `66999389-e11a-5a1c-912f-7f626be3d9eb` | `U10`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d12:seat:U11` | `9e00c699-7b5c-5341-b19c-ef54f2626660` | `U11`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d12:seat:U12` | `b5058df6-6d87-5690-a071-40f1b4529cc8` | `U12`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d12:seat:U13` | `0dc85edd-fb72-5774-b26c-e43dbf77a51f` | `U13`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d12:seat:U14` | `f979e7f2-7237-5319-a11f-56ba883dde1f` | `U14`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d12:seat:U15` | `46785062-46eb-5e4c-a08d-37f5c00b754b` | `U15`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d12:seat:U16` | `ff2d58b3-2098-5f67-bc5a-44fa4a99aa62` | `U16`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d12:seat:U17` | `744085a5-b11b-53b0-89bf-a92ac304a658` | `U17`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d12:seat:U18` | `59f5ccb4-fe02-552a-a437-918d42db47d5` | `U18`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d12:seat:U19` | `1c8c11ac-2558-5f9e-8c9f-37c0e9f73419` | `U19`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d12:seat:U20` | `e9863216-e720-51be-84bd-b86d6c7696e6` | `U20`; SLEEPER_UPPER/AVAILABLE |
| Trip | `trip:trip:b:r3:d13` | `560b7338-8284-5d3d-a36f-02de9c52af15` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 13 |
| TripSeat | `trip:trip:b:r3:d13:seat:L01` | `9c22f5e3-be3d-5009-8ad3-5faa10f0c1b5` | `L01`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d13:seat:L02` | `7d70bd8b-978c-54e2-933a-511bd7881f1b` | `L02`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d13:seat:L03` | `53ce9747-b62e-592b-b738-032c92fd87aa` | `L03`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d13:seat:L04` | `8c43311e-8954-5fa1-8d92-bca8c089ff90` | `L04`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d13:seat:L05` | `09a7ea79-8dc9-5907-ba69-8f813c942a89` | `L05`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d13:seat:L06` | `156d70fc-2f46-5ebc-8402-446e585f428c` | `L06`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d13:seat:L07` | `e03ba306-a20d-5756-8b12-b47353908de1` | `L07`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d13:seat:L08` | `745ddf16-2ee9-5276-98fc-75b9b4feac2a` | `L08`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d13:seat:L09` | `9d2de027-64e4-5bd6-8f59-b011a77b3f8c` | `L09`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d13:seat:L10` | `4d311dba-85ff-5df2-8fad-af644d2adf10` | `L10`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d13:seat:L11` | `ece60805-f5ab-5a36-a91a-bad33132db1a` | `L11`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d13:seat:L12` | `d2a15d50-11f9-5c9c-b91a-7a990f3fa5c8` | `L12`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d13:seat:L13` | `8d3e6d17-8907-5077-aae0-402e8f1313ad` | `L13`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d13:seat:L14` | `4535c8dd-3a23-5509-ac3d-e7ca7ba5865b` | `L14`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d13:seat:L15` | `9209d228-c213-54fe-850d-554449bed28b` | `L15`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d13:seat:L16` | `882b1f36-71d1-5c4e-9656-0915283edf61` | `L16`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d13:seat:L17` | `0bc0bbf0-e575-5d03-8f62-452c88e10ab6` | `L17`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d13:seat:L18` | `ad456c82-3ed4-5b13-95f5-ede5c55a2841` | `L18`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d13:seat:L19` | `fd67d833-dcd3-56b1-a711-8b6657de4cee` | `L19`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d13:seat:L20` | `5c861bb3-0a01-5a17-8c76-49e2c82db3d5` | `L20`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d13:seat:U01` | `e405da60-454a-5edc-bc98-a712cce216dd` | `U01`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d13:seat:U02` | `36df1211-49bb-51cb-a64b-8322bacb5c0c` | `U02`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d13:seat:U03` | `eb5ade2d-1ea2-5bc5-bc08-11f5e3cd5c3d` | `U03`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d13:seat:U04` | `eb573117-7694-52fc-9408-c505434c0196` | `U04`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d13:seat:U05` | `56ece09f-f064-5c0d-9fd8-fda79dc12fed` | `U05`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d13:seat:U06` | `e8bfeb7b-296b-56a0-b912-71974356fdd4` | `U06`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d13:seat:U07` | `b6cc6be4-fd58-524f-9971-72ea646d9e3f` | `U07`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d13:seat:U08` | `cb066c19-6836-54d4-addf-ea4d3c8617ca` | `U08`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d13:seat:U09` | `d331259d-25dc-53d7-8026-414fbb3b3b4c` | `U09`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d13:seat:U10` | `7219bf26-a87a-5b4f-ae01-8d57646e420e` | `U10`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d13:seat:U11` | `42624656-e824-5983-b059-8dc53f0845cb` | `U11`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d13:seat:U12` | `29360879-3852-5bc3-a2c8-75e94f3cf036` | `U12`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d13:seat:U13` | `48eccb56-303b-501e-a22d-63ab2470fefd` | `U13`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d13:seat:U14` | `e6ed58e5-63a2-54e2-868d-d682551194de` | `U14`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d13:seat:U15` | `bed8d5f6-a59c-5be9-9a70-cd8f86851459` | `U15`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d13:seat:U16` | `086466a7-e8b9-5638-8d31-9e196406e6aa` | `U16`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d13:seat:U17` | `c5ebfbee-741b-5403-b588-6480657f3d14` | `U17`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d13:seat:U18` | `26d6be9d-a713-50da-8709-75cd26861a9e` | `U18`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d13:seat:U19` | `6ad0d46c-3059-52ac-baca-89727092652e` | `U19`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:b:r3:d13:seat:U20` | `95ad908d-521e-52a1-9908-327f16fbdcd0` | `U20`; SLEEPER_UPPER/AVAILABLE |
| Trip | `trip:trip:c:r1:d00` | `aad14adf-9ad3-598b-91dd-7483fe8dd589` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 00 |
| TripSeat | `trip:trip:c:r1:d00:seat:S01` | `f6153887-966c-57d8-93ca-09bffbc32e66` | `S01`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d00:seat:S02` | `c306cbf9-e619-5648-8670-b6a8b23927be` | `S02`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d00:seat:S03` | `707d22a2-ffce-5c34-aa55-0a06eb14e710` | `S03`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d00:seat:S04` | `9bf8425f-a263-54a7-93b4-a1f61b09aa3a` | `S04`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d00:seat:S05` | `a4e4806a-dd31-5dc1-b5c2-fb701597984d` | `S05`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d00:seat:S06` | `0169103c-07b5-5318-a262-7ed1dc0cf3ff` | `S06`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d00:seat:S07` | `ecfb0a78-3fac-5b91-aa85-283d22fecb73` | `S07`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d00:seat:S08` | `662c299d-3053-547d-b5c1-2286cb8ae1ed` | `S08`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d00:seat:S09` | `aa35cdc9-33e0-5022-afcb-10ac9d0c496c` | `S09`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d00:seat:S10` | `7634188f-c07f-5c85-859a-23f6c95ace95` | `S10`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d00:seat:S11` | `f8b435c0-0e6e-5382-b62a-311204f39cdc` | `S11`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d00:seat:S12` | `b66dec49-e6ae-51e1-92f5-cb1a785d25b7` | `S12`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d00:seat:S13` | `46f71a89-a0da-5866-883e-e894078d5825` | `S13`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d00:seat:S14` | `770a3a6e-b985-56e8-8d9b-8024bbe78cef` | `S14`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d00:seat:S15` | `5b63d4e7-9760-5677-9e95-09de8c7f564e` | `S15`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d00:seat:S16` | `459fb826-775a-5178-929e-c6067be73944` | `S16`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d00:seat:S17` | `5cb62ed9-1ae4-5e47-9240-47388abebfca` | `S17`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d00:seat:S18` | `9bbcd149-4809-5b93-8391-858575c7f323` | `S18`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d00:seat:S19` | `b8c9a2c2-d55d-5224-aaac-9135317cb9ff` | `S19`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d00:seat:S20` | `dded4559-ca13-5457-bd3f-be0dfab0bf13` | `S20`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d00:seat:S21` | `81e3884a-f119-5fa3-86e3-11af97feb764` | `S21`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d00:seat:S22` | `5071579f-dd73-522d-8bcb-674c4c93cd09` | `S22`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d00:seat:S23` | `a4d54d31-a00d-5a25-b702-65133a863dc2` | `S23`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d00:seat:S24` | `7d5bc035-cf34-5894-bd92-9d7d97c5a25c` | `S24`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d00:seat:S25` | `27426941-be23-5c19-b7c8-bc60edd6adfd` | `S25`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d00:seat:S26` | `cbdf2067-c40a-54f0-aa81-c92db69696fb` | `S26`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d00:seat:S27` | `8e3f18ed-7320-5642-8132-d4611a34bc40` | `S27`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d00:seat:S28` | `c220d568-e3fb-5408-8ff2-6cf66385d9df` | `S28`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d00:seat:S29` | `c5a29931-a2a5-566f-a450-4c222ac97464` | `S29`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d00:seat:S30` | `0adb33fc-5862-5c5a-a4e5-ef211c3317a0` | `S30`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d00:seat:S31` | `2ac6b068-b696-549a-b38e-6f2a2270889e` | `S31`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d00:seat:S32` | `b319422a-8003-523e-999b-90f87d9b1767` | `S32`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d00:seat:S33` | `75311c61-5d9e-571f-99d1-800bd9ee7b05` | `S33`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d00:seat:S34` | `82ea39db-b1dc-5c94-8fa1-20d7e39a1101` | `S34`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d00:seat:S35` | `1364f921-ada0-513a-b3d8-3a824f66e9b1` | `S35`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d00:seat:S36` | `487a3bd6-f1b1-55e8-8abd-fb1cad818713` | `S36`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d00:seat:S37` | `f21d7b68-ade1-5736-a8b9-083630156089` | `S37`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d00:seat:S38` | `ed8ce9dd-1dc5-566b-ad89-9d4d1d59fc6e` | `S38`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d00:seat:S39` | `1166e00e-26d4-5b24-9320-a03a443a6513` | `S39`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d00:seat:S40` | `acba5cb8-17da-5495-9311-ec734c9c2473` | `S40`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d00:seat:S41` | `a90371b7-e87e-5acb-909e-32fba4b634ec` | `S41`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d00:seat:S42` | `0f921b3e-1f86-5468-8a4a-aae62aed4075` | `S42`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d00:seat:S43` | `4906ec43-e551-5d63-aafd-ab3ebb1317d4` | `S43`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d00:seat:S44` | `b9f38e73-bf03-53fb-9615-2d1c5c54e7bf` | `S44`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d00:seat:S45` | `44404617-c39c-5ff7-80fb-3f5ce2c8fd41` | `S45`; STANDARD/AVAILABLE |
| Trip | `trip:trip:c:r1:d01` | `a943f25f-10a3-5afd-a322-c00b98e11991` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 01 |
| TripSeat | `trip:trip:c:r1:d01:seat:S01` | `b20cd696-d622-58bc-9a64-50b117e9d50a` | `S01`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d01:seat:S02` | `39f0e8e8-1fd2-53b6-b54d-606a00ec4a35` | `S02`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d01:seat:S03` | `cc18ba11-ed74-5d97-884c-957bc4c97b69` | `S03`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d01:seat:S04` | `00a114c9-0a48-507f-b6fb-646b0bd316de` | `S04`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d01:seat:S05` | `f173b035-1b61-5bf3-b958-ff75043127ad` | `S05`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d01:seat:S06` | `56cebb49-95e7-563d-b581-bfc22d3ed597` | `S06`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d01:seat:S07` | `de17d9c9-4be9-5cfa-aeac-2320425770c9` | `S07`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d01:seat:S08` | `9f795d4a-6812-52b5-80d8-985f0b72915a` | `S08`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d01:seat:S09` | `e4894899-09aa-5868-b14e-574eef6d58fb` | `S09`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d01:seat:S10` | `389dfab0-d916-5781-bacd-065666b7f16f` | `S10`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d01:seat:S11` | `73367a01-d4b5-53c7-8e8f-9bd72782a676` | `S11`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d01:seat:S12` | `3d5516b3-dfd8-5b98-b851-5cb47d65598e` | `S12`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d01:seat:S13` | `28c77fe9-7b85-59b1-82be-0ab65432f896` | `S13`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d01:seat:S14` | `c97566a9-3956-54bc-b50b-bc4ca65e9a3f` | `S14`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d01:seat:S15` | `82d7bb9b-9e0a-5c4d-afa5-6e4064d7b69b` | `S15`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d01:seat:S16` | `2223d6b4-2902-5df2-aca3-cdd420b6c16e` | `S16`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d01:seat:S17` | `07a0c64a-ad8d-5841-8eab-13c11025c308` | `S17`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d01:seat:S18` | `6ca4e35b-4509-5823-ab9e-3e6f5564ee5e` | `S18`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d01:seat:S19` | `9c998e47-0295-5b66-92b1-962c823bd6e2` | `S19`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d01:seat:S20` | `c2e66232-4539-5916-a5a6-81ad7c01da9b` | `S20`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d01:seat:S21` | `f642e822-f8e6-59cc-b250-4caea883fd84` | `S21`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d01:seat:S22` | `86dd25b3-6456-5054-9613-f8c58d7d6804` | `S22`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d01:seat:S23` | `c682a5d7-a82b-5ded-bb3f-2f65d2a397e5` | `S23`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d01:seat:S24` | `340a301a-1127-5c0b-b1d6-36696cefa08e` | `S24`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d01:seat:S25` | `f1960b82-defe-5fe3-aec4-f143659a2459` | `S25`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d01:seat:S26` | `e667d886-a73f-5424-b2b9-a0cd7c8186b8` | `S26`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d01:seat:S27` | `f8b8627d-e8d8-59c0-8856-4cf8a72304b3` | `S27`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d01:seat:S28` | `27a1ff87-c9c5-5b25-8204-05725de6a791` | `S28`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d01:seat:S29` | `b41f37a9-45b3-58e8-bbd4-6aaa033a690d` | `S29`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d01:seat:S30` | `f6fa4c46-bd71-5d89-8c6a-232ed43524ca` | `S30`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d01:seat:S31` | `9ce8385c-11d0-5e0a-9e50-0ce9e6821565` | `S31`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d01:seat:S32` | `e1a6dd96-8fc3-55fd-9250-3589aebb341a` | `S32`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d01:seat:S33` | `df483a65-a8e7-5cb9-ab08-3f17e9cdafab` | `S33`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d01:seat:S34` | `7b24cdbf-3981-5e97-97c4-5fe97838c45c` | `S34`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d01:seat:S35` | `c8527d81-bab4-558d-ba91-166c2cfa5161` | `S35`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d01:seat:S36` | `4a931ded-238f-5e6a-9bbe-b98e20725115` | `S36`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d01:seat:S37` | `cd433dd5-ddf5-5b00-ba72-68c6b5451888` | `S37`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d01:seat:S38` | `8b16a96c-7835-5f58-a66b-2eaee4af29bc` | `S38`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d01:seat:S39` | `34024f9d-be85-539e-b597-39c7c5c136d9` | `S39`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d01:seat:S40` | `6119f821-7fff-5c68-b877-e7d4f1b3f954` | `S40`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d01:seat:S41` | `a2878904-232a-55ee-adb3-2fcaa7df6f96` | `S41`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d01:seat:S42` | `21463fd5-c421-5b23-aa92-e31c3be73aa9` | `S42`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d01:seat:S43` | `2ee13d2c-d3fc-548e-b5f9-4f9483698016` | `S43`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d01:seat:S44` | `da898671-5cf6-54bd-846b-63bea4be3116` | `S44`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d01:seat:S45` | `f602fbde-5323-5953-a5c7-858ab62c0e00` | `S45`; STANDARD/AVAILABLE |
| Trip | `trip:trip:c:r1:d02` | `c7b28fbd-91a5-5878-9326-40bc3b81bd47` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 02 |
| TripSeat | `trip:trip:c:r1:d02:seat:S01` | `3a891766-056e-5e8a-bc64-f4f79106336b` | `S01`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d02:seat:S02` | `c49d14ae-897d-58c0-a934-b2f039cf1ded` | `S02`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d02:seat:S03` | `bd6d437f-5777-5aa1-ab49-7a980fb4d5f7` | `S03`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d02:seat:S04` | `2dc4f3ef-db71-5b0c-9c5c-6c1537c0cccd` | `S04`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d02:seat:S05` | `d1c9c2be-1b2d-5114-a4df-426798216e85` | `S05`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d02:seat:S06` | `8b14fd7d-399d-5bb3-be32-862dc3500612` | `S06`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d02:seat:S07` | `e8e73a6c-4ab1-5255-87b7-ccb52a5629a6` | `S07`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d02:seat:S08` | `7a1a131e-04be-563d-b6a1-112ed1c90069` | `S08`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d02:seat:S09` | `0114ef45-97cc-5da7-88e9-4d1979020479` | `S09`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d02:seat:S10` | `5678976d-c6c2-5e9e-8c3c-5dcbefacd4f0` | `S10`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d02:seat:S11` | `6eb6b41a-375d-5f6c-9bb1-1a38a0529a4c` | `S11`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d02:seat:S12` | `02714399-9720-5466-bf12-2a84e9bbf025` | `S12`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d02:seat:S13` | `6f4412f2-ea31-5dcf-bc06-839700416423` | `S13`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d02:seat:S14` | `20afb09f-435c-506b-af95-53909a78f7fb` | `S14`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d02:seat:S15` | `c214003a-e010-54fb-8171-eb594c743bb6` | `S15`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d02:seat:S16` | `6a11646d-7f89-57d8-80c6-54e2648c25ab` | `S16`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d02:seat:S17` | `6425a431-8ed0-5954-a42d-2b634130f01f` | `S17`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d02:seat:S18` | `f9d4f8f9-f308-599b-9d8b-cef2e53168af` | `S18`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d02:seat:S19` | `b542152b-32f1-5be4-8aec-54db04e3ccdb` | `S19`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d02:seat:S20` | `a860c72d-f701-520c-bb00-64c315b38279` | `S20`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d02:seat:S21` | `e3456af2-6dad-5ed7-afb4-ad545944f022` | `S21`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d02:seat:S22` | `aa5d92e3-22d8-562d-ae88-6bdf4b8e3e04` | `S22`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d02:seat:S23` | `8cbd8585-869f-52e3-88b8-21a0ee63d93b` | `S23`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d02:seat:S24` | `2b297424-fdc9-50f5-b249-ee5190624ce1` | `S24`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d02:seat:S25` | `b8dbc6de-9db1-5509-9102-037f9089c313` | `S25`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d02:seat:S26` | `6a7b6a32-2e96-5f8b-8bf5-edfe5aa5bab2` | `S26`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d02:seat:S27` | `bb3d112b-aca5-5dd6-9f0b-6aef27179ca1` | `S27`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d02:seat:S28` | `b283af6a-a1f6-5032-b597-26af2bf3c4f4` | `S28`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d02:seat:S29` | `b315cc9d-7713-5b46-ab8a-0cb661faf343` | `S29`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d02:seat:S30` | `d44af688-e606-58aa-a9f0-d7ceebc0a8ae` | `S30`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d02:seat:S31` | `39169326-77f8-556a-861f-5045b1f4e725` | `S31`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d02:seat:S32` | `fe7459e9-f1da-565f-b66b-d174c4d5d4ba` | `S32`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d02:seat:S33` | `207da508-9a77-5095-8995-8ac618033b3f` | `S33`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d02:seat:S34` | `ec0a5bfc-92bd-581f-a965-229f80b543d1` | `S34`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d02:seat:S35` | `1e93e246-94a0-514b-b38a-279f657f5cab` | `S35`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d02:seat:S36` | `9e4a0cd7-2a3a-529d-a4bf-be5218c2193e` | `S36`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d02:seat:S37` | `9905efdd-e414-578d-a85f-96857a249918` | `S37`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d02:seat:S38` | `ee369cfb-681d-50c2-b7cb-fb7f56962785` | `S38`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d02:seat:S39` | `78568f0c-5a99-5d64-ac02-1d5885e13b7a` | `S39`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d02:seat:S40` | `a1753dee-3762-59ea-bb43-14e3f527a883` | `S40`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d02:seat:S41` | `7ae3b6a2-a196-519c-a307-73c0a7d65cc3` | `S41`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d02:seat:S42` | `fb7003c8-cd46-576a-8e8f-089ede3c4624` | `S42`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d02:seat:S43` | `fa6a25a6-0bcf-52ea-b71e-5b512b99a8aa` | `S43`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d02:seat:S44` | `d385ee70-a670-5dc9-a42a-693903276ae1` | `S44`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d02:seat:S45` | `ad23f8ea-f919-553e-9eb1-f5627417a579` | `S45`; STANDARD/AVAILABLE |
| Trip | `trip:trip:c:r1:d03` | `80729fc6-19f4-5ea6-9134-ff8b9f15f128` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 03 |
| TripSeat | `trip:trip:c:r1:d03:seat:S01` | `48944900-ab7a-5673-bafa-c36232d67efa` | `S01`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d03:seat:S02` | `aa56bfc3-4915-521c-a2c8-5c84b7f2ac81` | `S02`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d03:seat:S03` | `e532bc83-734e-598d-a39d-2a97ba53b236` | `S03`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d03:seat:S04` | `6bb66a1f-a818-58ac-a591-755f72727e03` | `S04`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d03:seat:S05` | `1421ee72-9342-5ac4-be6e-c30b85151318` | `S05`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d03:seat:S06` | `591818b9-5d4e-5772-a46d-de6e405e1f82` | `S06`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d03:seat:S07` | `cd32a961-572b-5afe-b09a-5da8ec1ac628` | `S07`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d03:seat:S08` | `9c55364c-22d5-5376-99e3-5333b644cd9f` | `S08`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d03:seat:S09` | `f8796bfa-21c9-53ce-9d40-0758a9e22cf7` | `S09`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d03:seat:S10` | `e5c757ab-7537-5f1d-b9c6-ddba057c11b1` | `S10`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d03:seat:S11` | `3b5f8809-8dc3-564f-97ab-0c331f45090c` | `S11`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d03:seat:S12` | `dcdcd810-2fa3-5c16-a084-470a08e992cc` | `S12`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d03:seat:S13` | `f6e9dc08-9d85-5592-90fb-04e7265d197e` | `S13`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d03:seat:S14` | `e46a23a6-4d0e-5380-add4-b4afd281850e` | `S14`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d03:seat:S15` | `50e02fda-9f82-5281-a3f2-ea5bf72e657c` | `S15`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d03:seat:S16` | `252e51f4-7905-56f1-a988-6deeb2fdfc9b` | `S16`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d03:seat:S17` | `e53623fe-ddc0-5a0b-8dff-ff9f5ea32fe9` | `S17`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d03:seat:S18` | `ebea1707-ee9c-5528-b7f0-8d07f501de60` | `S18`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d03:seat:S19` | `3112c2d7-dab6-57eb-8a1c-46b6dbc964cf` | `S19`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d03:seat:S20` | `a5874284-038d-531a-8f72-3afc9242c768` | `S20`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d03:seat:S21` | `7cdac144-55cf-5173-b42d-bc581fd7361f` | `S21`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d03:seat:S22` | `8ee58779-1232-52d6-a613-fba2d49e0b7c` | `S22`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d03:seat:S23` | `317a3687-3e0a-51d6-b8db-0bd6ce50e100` | `S23`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d03:seat:S24` | `5abe8e84-1f5d-51a8-b513-48c894899ae7` | `S24`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d03:seat:S25` | `76c0b1f8-6890-51d0-a53e-b3aa4f2d563c` | `S25`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d03:seat:S26` | `b998bbbe-eec1-5e26-a801-b475055f3f12` | `S26`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d03:seat:S27` | `197fe7b4-4c70-5b6d-be79-e488fe261631` | `S27`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d03:seat:S28` | `d470ab98-248a-56eb-a1c0-412f5eaedcdb` | `S28`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d03:seat:S29` | `bd5e00c5-f373-5519-add3-5866fdf17add` | `S29`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d03:seat:S30` | `3c937fb5-5746-5fba-a8cd-d145b9885bf5` | `S30`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d03:seat:S31` | `2868622b-74a9-573a-ba1d-a9cdf4546676` | `S31`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d03:seat:S32` | `8d5d25bf-edd3-5e0d-b3e7-93843b556e82` | `S32`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d03:seat:S33` | `a7561f57-0cda-5454-9a9b-36b1f26b2e15` | `S33`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d03:seat:S34` | `013242e7-6831-50ae-a80a-ed13a7d477de` | `S34`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d03:seat:S35` | `ec74c315-18ac-5011-b6f0-ab97693f61a1` | `S35`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d03:seat:S36` | `953dc375-1c0a-5404-900f-6aa48973cf5d` | `S36`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d03:seat:S37` | `494b9e61-4986-5703-9296-f945d9a96c51` | `S37`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d03:seat:S38` | `eb628f00-4c46-5d92-8cc7-13b38dc4d9f3` | `S38`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d03:seat:S39` | `e4e66c0f-3fb8-5505-a6ef-04e68ee9318b` | `S39`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d03:seat:S40` | `70fe37d0-e4b7-5924-bf62-0d86421ac108` | `S40`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d03:seat:S41` | `ba7cc361-ac63-53a7-b603-21c663dcd2b0` | `S41`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d03:seat:S42` | `5b22ab8d-7158-5b6a-8763-baf9b724055e` | `S42`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d03:seat:S43` | `c2a28d5e-e70b-5d1d-ba03-7cf8a9e43e9c` | `S43`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d03:seat:S44` | `5b19fccf-eb3f-55db-9f56-af2b2d910e01` | `S44`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d03:seat:S45` | `a9486e05-b011-55f5-b978-918fa5dce68e` | `S45`; STANDARD/AVAILABLE |
| Trip | `trip:trip:c:r1:d04` | `489623d4-8c9f-5e32-9381-8585b6838c0b` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 04 |
| TripSeat | `trip:trip:c:r1:d04:seat:S01` | `ae66a6f6-225c-5b91-97ac-6f0539476c18` | `S01`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d04:seat:S02` | `473c4b14-b334-57ed-8db9-a0ea3b56c488` | `S02`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d04:seat:S03` | `615bbd79-bc91-5e23-989f-822bed5f532d` | `S03`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d04:seat:S04` | `6f69c54f-8eff-5966-954a-f967e8725d1d` | `S04`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d04:seat:S05` | `11505ad6-b29e-5f90-b961-0c3c27c606d2` | `S05`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d04:seat:S06` | `e5f34944-75b9-5ee0-815e-ac1d3ffc05c3` | `S06`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d04:seat:S07` | `bf853903-9de9-53b4-8c62-43e76049c1fb` | `S07`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d04:seat:S08` | `83f36b45-9672-568b-a9f8-62f2e81ccd02` | `S08`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d04:seat:S09` | `cf1344ca-f0e9-5d5a-bf92-e7f32641241f` | `S09`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d04:seat:S10` | `621ad607-6367-5f0d-a766-33bca7211b2f` | `S10`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d04:seat:S11` | `6a9ed622-b734-5cf3-ae5f-36f0bb9a3ee7` | `S11`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d04:seat:S12` | `24d0350b-ba91-53cc-a305-902cc5730c58` | `S12`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d04:seat:S13` | `515afe43-5d8e-54c3-92e2-5d9c393ce908` | `S13`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d04:seat:S14` | `6077deff-5e81-57f6-942c-e6d5083f9ec8` | `S14`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d04:seat:S15` | `a3eec488-474d-5f77-a108-1e2fd9e26542` | `S15`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d04:seat:S16` | `bf4c13bb-82a6-5270-bfac-ebca6262b130` | `S16`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d04:seat:S17` | `5ebb16df-07ff-52a0-879f-1d3830919d5c` | `S17`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d04:seat:S18` | `8cea1471-7e51-5141-bb2d-e59e75e66f57` | `S18`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d04:seat:S19` | `6f7164a6-3113-520d-bb0a-bd58ce462b2f` | `S19`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d04:seat:S20` | `dd144b66-e817-52e2-959e-0477adeb5392` | `S20`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d04:seat:S21` | `f09e5a4f-3b51-5a4c-9050-9a5aaf6ac16a` | `S21`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d04:seat:S22` | `c2108b7e-ba53-5885-a347-45132477c140` | `S22`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d04:seat:S23` | `3f41a62b-8725-52a6-9957-512310e1903a` | `S23`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d04:seat:S24` | `9fa30b20-e488-52ce-8cb5-f64aeba2b462` | `S24`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d04:seat:S25` | `74540983-0424-5c98-a095-92c6006be497` | `S25`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d04:seat:S26` | `9ff94dc4-77b9-5189-a1fe-1cbcc4a9524c` | `S26`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d04:seat:S27` | `8c9ab791-3b6d-5c6b-8e02-c703367f4ab6` | `S27`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d04:seat:S28` | `5e51cd18-8569-5b74-ad0b-eae7a25527f2` | `S28`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d04:seat:S29` | `0b30b7e0-c670-586c-ad8c-e9f57af2fb21` | `S29`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d04:seat:S30` | `9de86d90-bfa4-556a-9320-1c892e4b10bc` | `S30`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d04:seat:S31` | `3eb4580e-79e8-540b-a047-b0d82be1189d` | `S31`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d04:seat:S32` | `4784b65c-f4da-5809-8175-dc2b15e740fc` | `S32`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d04:seat:S33` | `e64fa1e9-a4a2-5310-99dd-cdafed2f8d83` | `S33`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d04:seat:S34` | `874e2f2e-0af4-599c-9425-c385e08a7d96` | `S34`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d04:seat:S35` | `cd3d4cfc-1047-5671-b5d3-ae4fa002f886` | `S35`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d04:seat:S36` | `6ee6d3dc-5f1e-52d2-bf68-7517a143b9e7` | `S36`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d04:seat:S37` | `1029baa7-1da3-547e-8c52-d300fa2f7aa1` | `S37`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d04:seat:S38` | `77a0b0a2-b522-5c6c-a566-ab5fc88928a9` | `S38`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d04:seat:S39` | `2ed80e13-aa3b-5486-ad52-ee7f12459e27` | `S39`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d04:seat:S40` | `f26b0dc4-db8d-5210-8c04-2242a8a7fc63` | `S40`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d04:seat:S41` | `0ede724d-9ade-5239-a14d-e19dfbfc1af4` | `S41`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d04:seat:S42` | `93256b9b-7258-5709-96d6-957e5ce111e0` | `S42`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d04:seat:S43` | `7b5291bf-5943-5d24-a733-757521f26af2` | `S43`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d04:seat:S44` | `afb18069-4656-59a2-8b9a-c54509a39b4a` | `S44`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d04:seat:S45` | `8752ac63-029b-56ae-b381-aafa0aaad236` | `S45`; STANDARD/AVAILABLE |
| Trip | `trip:trip:c:r1:d05` | `242a6089-3571-5d8c-8544-f293a4fc85f2` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 05 |
| TripSeat | `trip:trip:c:r1:d05:seat:S01` | `280284d5-7d85-561d-afa6-f3dac00ee45f` | `S01`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d05:seat:S02` | `8476fc38-e208-54b2-a3a5-492005c0be14` | `S02`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d05:seat:S03` | `1cd206af-75c9-52dc-a9ad-e34212d33df9` | `S03`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d05:seat:S04` | `f859f1d2-ea41-5702-882b-3385edfecc17` | `S04`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d05:seat:S05` | `f74b3d06-bf96-5fd9-8d32-d5fa7eeac878` | `S05`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d05:seat:S06` | `9623a019-d0b0-5b52-9963-ca0d20ef68eb` | `S06`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d05:seat:S07` | `4b88f3ee-f5ce-5557-a7b5-3ddf7e1217bf` | `S07`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d05:seat:S08` | `fe5875ed-8621-511d-a5fd-c3fb75bd5bd8` | `S08`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d05:seat:S09` | `82ae65db-5250-5e22-a1a2-d4ac13b6d343` | `S09`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d05:seat:S10` | `0d9231fb-9027-5d19-b2bf-538a2a2b93db` | `S10`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d05:seat:S11` | `6e6aa13e-7156-5c3c-9984-6e535fa625bc` | `S11`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d05:seat:S12` | `23d8ea5a-807b-5221-81c6-cf4db0cb9395` | `S12`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d05:seat:S13` | `d3097bdb-d1a2-5ec3-8b1f-de5ef2d0ac05` | `S13`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d05:seat:S14` | `d182a808-729d-5982-9d7d-9056d0c07261` | `S14`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d05:seat:S15` | `8919fa7c-a779-54bc-8aff-b954fa4b9ea1` | `S15`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d05:seat:S16` | `68b4e0a6-71b5-5a2a-a6e3-8e53d0b359aa` | `S16`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d05:seat:S17` | `03ce364c-a775-54d4-b860-089045bf06a6` | `S17`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d05:seat:S18` | `d0c279f7-139f-507a-86b3-2946505c5367` | `S18`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d05:seat:S19` | `13307af4-60e7-5031-a883-a1fa43cadfbe` | `S19`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d05:seat:S20` | `03057443-c712-5ca6-b5cc-741c7fe7daee` | `S20`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d05:seat:S21` | `2d6cc645-fb43-50cb-a1b8-a7495915e9d5` | `S21`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d05:seat:S22` | `751de8aa-1797-5fa0-b4da-0c20435a7475` | `S22`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d05:seat:S23` | `2ed85e67-79dc-5abf-acee-cde5ef1d6908` | `S23`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d05:seat:S24` | `884a4a7c-c9db-52a5-b43e-6a4bb01390f6` | `S24`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d05:seat:S25` | `b72c949f-9b5a-5933-b4ee-19eaee297c60` | `S25`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d05:seat:S26` | `101d1943-fe2d-5b91-8753-5c60b8c25511` | `S26`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d05:seat:S27` | `a93b8b55-5acf-5f91-8be3-2eb3b1786857` | `S27`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d05:seat:S28` | `49fc5fad-4579-55bc-a2de-98cb43488add` | `S28`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d05:seat:S29` | `2312660e-b92f-50b2-a8bf-dc88c68299ec` | `S29`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d05:seat:S30` | `c0458a0d-d4b3-5a59-b212-1ed42d8a243d` | `S30`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d05:seat:S31` | `d9f3fc3c-91b3-5db4-8608-d4a7e4c0cd7a` | `S31`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d05:seat:S32` | `19027bca-fd41-5adf-8fec-aca78e5983b1` | `S32`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d05:seat:S33` | `646a3383-d397-5b00-bf5b-02a30c99ff56` | `S33`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d05:seat:S34` | `631e1538-d13e-51ab-90e5-d6a901ed5751` | `S34`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d05:seat:S35` | `1a656769-81fa-58f4-972e-06c4b00d9459` | `S35`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d05:seat:S36` | `7d144fea-a204-5d8c-a6ab-f0ac544af1df` | `S36`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d05:seat:S37` | `5694b3a5-35e7-54b9-902f-2ce704563899` | `S37`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d05:seat:S38` | `406c270a-7917-5564-9027-9a7cff7abd16` | `S38`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d05:seat:S39` | `63da5374-89a0-5435-b774-97d8b4a54f88` | `S39`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d05:seat:S40` | `cbffa146-de37-5fdb-beb1-c9955b151e73` | `S40`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d05:seat:S41` | `4b23a6ef-7d02-56a4-8534-260a8690ae39` | `S41`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d05:seat:S42` | `f18dab80-0136-59cc-9635-83d92f1aaccc` | `S42`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d05:seat:S43` | `e6f1b41e-372e-5aa3-bb63-6676e37ee684` | `S43`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d05:seat:S44` | `0dc28c2f-6e61-5805-9029-289406e0ace1` | `S44`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d05:seat:S45` | `9bbcce60-ccac-5ba2-9f6e-6aabec1c9698` | `S45`; STANDARD/AVAILABLE |
| Trip | `trip:trip:c:r1:d06` | `017714bd-9ba6-5be9-9492-4f6caf10c343` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 06 |
| TripSeat | `trip:trip:c:r1:d06:seat:S01` | `9e2f3c8a-d8ad-5a86-bfc3-d03343b000f9` | `S01`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d06:seat:S02` | `9b80ee81-4d12-5e09-9924-b29a6a34b689` | `S02`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d06:seat:S03` | `d3ed36a8-114d-5e76-9a6a-f8a2e652db7f` | `S03`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d06:seat:S04` | `0413f8ea-3405-5489-a0b1-77b9624434b4` | `S04`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d06:seat:S05` | `b6eff8f2-698b-5344-a199-a2bd4133dcd9` | `S05`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d06:seat:S06` | `cd6af465-1be2-55df-9d94-4ee8b8eb8a97` | `S06`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d06:seat:S07` | `60426266-01c7-55ea-852e-9ba1ea4b8924` | `S07`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d06:seat:S08` | `36506792-5042-5113-b97d-6cb930ea4980` | `S08`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d06:seat:S09` | `0471db3e-e4e9-53e7-b4b9-d7f2994bfa57` | `S09`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d06:seat:S10` | `5d0461ff-e6af-5244-bad2-5585d2028ad5` | `S10`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d06:seat:S11` | `e6cae9f9-7226-51c5-9ca0-a6248f7bb29e` | `S11`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d06:seat:S12` | `8b253e0b-8529-5b6e-a2fb-ef93e598d161` | `S12`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d06:seat:S13` | `d1b37728-4543-574a-a7ec-3a69e100dbbf` | `S13`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d06:seat:S14` | `f7f9b3b3-5095-5997-90db-190654b9ad99` | `S14`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d06:seat:S15` | `e0eba2ad-0c76-5de2-9d1e-c572fc857864` | `S15`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d06:seat:S16` | `8b1ee701-5cfa-5ad4-9fa2-7811c152621b` | `S16`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d06:seat:S17` | `82140bbf-f5ae-5982-aaf3-a19f3ce31853` | `S17`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d06:seat:S18` | `0fa40cd6-e57a-5faf-91ea-954066777357` | `S18`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d06:seat:S19` | `1f7e9da3-3eef-5466-a4e0-3ed75c6164be` | `S19`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d06:seat:S20` | `6bc15bad-08b9-5ed2-97ef-61daa5d95168` | `S20`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d06:seat:S21` | `b5cfff6c-413a-5efe-afe5-90a67f16e8d9` | `S21`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d06:seat:S22` | `4e9571bb-63e8-5292-9eb3-d48054bdf402` | `S22`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d06:seat:S23` | `ecd9fac8-2410-5cf9-83ce-b22d1753497e` | `S23`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d06:seat:S24` | `817b0f44-8bec-5982-a920-9f50d4cb233a` | `S24`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d06:seat:S25` | `700b794e-82fd-5939-850d-bcac5b33d967` | `S25`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d06:seat:S26` | `f7efdfda-9011-5984-ad8d-96b6fc0ee48a` | `S26`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d06:seat:S27` | `9c849749-d62b-5c74-a0da-84a0d8da7567` | `S27`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d06:seat:S28` | `b94853c0-a29c-5c73-b2d1-24b47bdf93bf` | `S28`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d06:seat:S29` | `702f58a0-1cc7-5430-b751-095929d6ac32` | `S29`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d06:seat:S30` | `acae3590-808d-5565-8301-64e4a69fc5de` | `S30`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d06:seat:S31` | `e072d3db-450e-557b-9645-bc004cafcbed` | `S31`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d06:seat:S32` | `bc609ed1-3459-5d73-aff0-f0421ff5d1ab` | `S32`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d06:seat:S33` | `9dbc006f-1a25-5ce2-9571-35731a528ab9` | `S33`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d06:seat:S34` | `7934012b-523e-5afb-b281-f398fb42928b` | `S34`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d06:seat:S35` | `4674ca04-a16e-5250-a6a2-68d15336c759` | `S35`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d06:seat:S36` | `3c6fed26-8de2-59b3-b621-8478d243c7b1` | `S36`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d06:seat:S37` | `72b8bacb-fcca-5a55-815c-7f28c623c07a` | `S37`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d06:seat:S38` | `dc635c12-19d7-5f17-ae6d-b06807e165bd` | `S38`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d06:seat:S39` | `c7e41c01-f702-52b8-bc00-dd7f0a5240f7` | `S39`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d06:seat:S40` | `163d0d9a-51e0-5045-bfec-f4bdd0122a32` | `S40`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d06:seat:S41` | `a4a194fe-1143-5220-a1ab-ee81b19dda10` | `S41`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d06:seat:S42` | `87a32abe-452f-5d58-90e6-4fbfb039fa9b` | `S42`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d06:seat:S43` | `25c30cf9-3eaa-578b-84b0-e07b7bdad289` | `S43`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d06:seat:S44` | `864da599-5f26-5e62-890a-81228e132b33` | `S44`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d06:seat:S45` | `00928a18-85c9-5fab-8dbd-decdecb828b3` | `S45`; STANDARD/AVAILABLE |
| Trip | `trip:trip:c:r1:d07` | `e7cd8c49-047d-5743-8007-adc509df686c` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 07 |
| TripSeat | `trip:trip:c:r1:d07:seat:S01` | `489c3031-f6f1-5bc7-b4ac-48cbd7966a21` | `S01`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d07:seat:S02` | `7984c88a-5b39-5396-adce-99df801fda14` | `S02`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d07:seat:S03` | `a9b0a642-17e4-5afa-81d0-b7993894eeac` | `S03`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d07:seat:S04` | `06387e52-9a60-5fb9-ab0b-f2423bb2a1d4` | `S04`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d07:seat:S05` | `edf9d2dd-228e-5fe9-98f6-87c3fc2ffa0e` | `S05`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d07:seat:S06` | `38c5326e-3442-5b2d-be1e-f2c23a97258f` | `S06`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d07:seat:S07` | `c72bb92b-bd7f-5a55-ac0c-2c54386f695e` | `S07`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d07:seat:S08` | `d07cdea6-003f-57f3-91a9-e45df6c525c2` | `S08`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d07:seat:S09` | `ce4a2b00-cd1e-5225-beb4-b5594deb51d8` | `S09`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d07:seat:S10` | `6ac82f27-c282-5940-9eaf-01f96200cb31` | `S10`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d07:seat:S11` | `aa97f04a-c29e-5751-841e-ab5c294f9760` | `S11`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d07:seat:S12` | `aff5a751-0c0c-5d78-ad45-5bf370180e6e` | `S12`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d07:seat:S13` | `338a5ea5-074b-564c-ae38-5b5a94ef6f1f` | `S13`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d07:seat:S14` | `b2540cc6-ad47-5088-85bf-327c30ae95c0` | `S14`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d07:seat:S15` | `da10ba43-65a3-520d-8541-8c5cf87c843e` | `S15`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d07:seat:S16` | `4d8a9310-e378-5a38-892b-9cd74ff50242` | `S16`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d07:seat:S17` | `909b497f-0925-53f1-85aa-44a3852ea906` | `S17`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d07:seat:S18` | `a44433d4-4589-5e83-bd7c-cb7f1538e946` | `S18`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d07:seat:S19` | `903b52fc-9ba7-5748-b7df-e2ba1d89696c` | `S19`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d07:seat:S20` | `a377d889-5b9a-5d8e-927a-6b6682594ad0` | `S20`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d07:seat:S21` | `a726fbb9-9645-5fd3-9729-a7d90c24c51c` | `S21`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d07:seat:S22` | `74285082-e2b6-5244-bbcc-373a3a72ac14` | `S22`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d07:seat:S23` | `af64b284-62ac-5f8d-a12e-2b56556f01ad` | `S23`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d07:seat:S24` | `c4f63c46-2e3e-53ca-871f-f451bfe7785c` | `S24`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d07:seat:S25` | `a428ff46-cd9c-5a48-a165-e290608cf535` | `S25`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d07:seat:S26` | `b0ca838a-0635-5b4a-a774-420ba0ef2fc3` | `S26`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d07:seat:S27` | `55c44a5d-6163-5d97-8d9a-8ed893381dc5` | `S27`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d07:seat:S28` | `c0c3f6f4-0b49-5c19-b662-ad443a77bac7` | `S28`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d07:seat:S29` | `20819584-3800-5c15-b3e5-e67bed400050` | `S29`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d07:seat:S30` | `44d5bc61-df32-5939-b686-6698bfb582ca` | `S30`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d07:seat:S31` | `b2b0ef8c-2446-53d6-b54d-bbece86e5409` | `S31`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d07:seat:S32` | `2dda3955-66f6-5d87-8da3-4e3835499a65` | `S32`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d07:seat:S33` | `05b6425a-2c1f-574e-a4aa-e5a84ffa6453` | `S33`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d07:seat:S34` | `dcb35f0a-1d26-5b92-9e55-6d96633834df` | `S34`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d07:seat:S35` | `5a8e9b1f-3447-5b67-bed7-2bbeeaaf5138` | `S35`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d07:seat:S36` | `fff3c7d8-df49-570b-b716-0e09409588e4` | `S36`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d07:seat:S37` | `d00e662a-0160-5493-af33-4774c768c979` | `S37`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d07:seat:S38` | `c1e9882f-b127-5930-a945-aa64f29b90be` | `S38`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d07:seat:S39` | `10e9863e-4401-5607-aa45-3d4c543839e6` | `S39`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d07:seat:S40` | `bfa2e4fc-54f8-5fe5-b2ac-e2703c76de6b` | `S40`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d07:seat:S41` | `276afc20-90e3-53ba-a4dc-d0f1e6e83922` | `S41`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d07:seat:S42` | `fa2562a1-acdb-5eeb-989d-5056f5e9a57c` | `S42`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d07:seat:S43` | `f96c74ac-bd93-597e-8f8d-7cdd7519dc5b` | `S43`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d07:seat:S44` | `da3cb242-e033-5f4c-92b5-766cd634c5f0` | `S44`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d07:seat:S45` | `7d26003f-1213-5039-91cb-805e5aef3db7` | `S45`; STANDARD/AVAILABLE |
| Trip | `trip:trip:c:r1:d08` | `ef6e3b45-090f-50a7-8041-e1c74cf5e44a` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 08 |
| TripSeat | `trip:trip:c:r1:d08:seat:S01` | `eee20b7f-b1bf-5cb2-97dc-a1dbfbcfc8b2` | `S01`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d08:seat:S02` | `125a89ce-fd31-5766-ad27-671379bcfad9` | `S02`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d08:seat:S03` | `75eff496-41c8-50d5-8529-949156cdcaee` | `S03`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d08:seat:S04` | `7c515c38-9aa4-5232-a622-7f07be3d09af` | `S04`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d08:seat:S05` | `a2515d26-8ae5-5c1b-8026-ae061ce7d128` | `S05`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d08:seat:S06` | `72c489ed-58e2-5bfd-9fb4-32a988f2053f` | `S06`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d08:seat:S07` | `97172878-363b-5986-9523-daeb4be01dc9` | `S07`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d08:seat:S08` | `86ced103-a500-5c9e-b874-7b3daa4ec730` | `S08`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d08:seat:S09` | `8c0cb3bf-1285-51c4-a397-dce306f835d3` | `S09`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d08:seat:S10` | `31b44bb4-af14-5a3c-9898-1e2234284dd8` | `S10`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d08:seat:S11` | `f7dfca06-662c-5f3c-a7bf-75429b56f606` | `S11`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d08:seat:S12` | `03b8f788-a68c-52a2-a514-d4d4cb3290f5` | `S12`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d08:seat:S13` | `8bc02ce8-696c-5ee1-8757-1396bde5c831` | `S13`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d08:seat:S14` | `27951af7-bac9-5986-b770-e52e22b4d830` | `S14`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d08:seat:S15` | `8bd96d03-25ab-56f4-b70b-343070434627` | `S15`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d08:seat:S16` | `d77f7e21-219f-5e41-ac99-4a74986fd8d9` | `S16`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d08:seat:S17` | `254dafe3-c052-52ce-8dcf-bbbe16babe61` | `S17`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d08:seat:S18` | `3d9da03b-1b41-5027-ad26-525236f0edcb` | `S18`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d08:seat:S19` | `e0d41653-bc7d-5a83-b961-f05628008522` | `S19`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d08:seat:S20` | `f3a8b1ca-dfc9-523b-84bc-a28133e3d799` | `S20`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d08:seat:S21` | `76c2e6e9-6c97-55c1-ac16-430d6ecfd9cf` | `S21`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d08:seat:S22` | `e78687a3-21a9-5705-9ac7-db3b5997783e` | `S22`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d08:seat:S23` | `41374686-1b94-5fc4-bf5b-80310872c7a5` | `S23`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d08:seat:S24` | `7a046881-8adc-5c97-8798-2ea9efca5b93` | `S24`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d08:seat:S25` | `ecdaf8c4-c2b7-574a-a4f6-860723ff5162` | `S25`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d08:seat:S26` | `1f0eb015-83f4-5f36-869c-b7744ae20990` | `S26`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d08:seat:S27` | `ec1492a8-d860-5ed6-ad33-27acf128b859` | `S27`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d08:seat:S28` | `56b18e3e-c4f2-5b83-9bb6-3fe79b1d93b8` | `S28`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d08:seat:S29` | `ddc8d2ba-78da-5218-9586-49633193719a` | `S29`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d08:seat:S30` | `a131c7ea-8e99-54be-ba65-bb209eb00a9b` | `S30`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d08:seat:S31` | `1bbf9e20-8f0c-5f08-abe3-e377569103a5` | `S31`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d08:seat:S32` | `ba66da9c-8e5b-5060-9a96-09324c20246e` | `S32`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d08:seat:S33` | `474283ed-82a6-56fb-8b57-823b4159769c` | `S33`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d08:seat:S34` | `a5785e44-7390-5ad2-9d79-2c422e8e35c6` | `S34`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d08:seat:S35` | `d6a07c5a-3898-5842-b4af-bb17610bf162` | `S35`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d08:seat:S36` | `e435fb03-bf37-523b-8537-7e31aa5b2023` | `S36`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d08:seat:S37` | `074668c4-7f38-50ad-9b07-f918057d79c1` | `S37`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d08:seat:S38` | `925320ce-739a-5431-974b-a1b7ed401c2c` | `S38`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d08:seat:S39` | `f5f9044d-6111-5967-9af0-948b8953a311` | `S39`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d08:seat:S40` | `0223ec76-59b5-512b-84b7-7f319617583f` | `S40`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d08:seat:S41` | `77f42f99-86e6-527c-9103-e4e3addcf0e3` | `S41`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d08:seat:S42` | `38fd5870-9089-5ac6-a02c-d317c5f70eb9` | `S42`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d08:seat:S43` | `1a16117c-11b1-53d3-98ac-38dc5afe6207` | `S43`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d08:seat:S44` | `f4e6f6ca-3ecb-56ca-93e0-f5bb541a0757` | `S44`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d08:seat:S45` | `1bf4cebe-1f6f-5e03-8a01-f78ff0e0c375` | `S45`; STANDARD/AVAILABLE |
| Trip | `trip:trip:c:r1:d09` | `715b8ebb-ac82-5563-9e50-3c0a75550bda` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 09 |
| TripSeat | `trip:trip:c:r1:d09:seat:S01` | `a9c35d01-6fa8-5d3d-b896-b8bc0dd5bf1f` | `S01`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d09:seat:S02` | `ce619294-1250-5a69-bd08-d31fa6990d85` | `S02`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d09:seat:S03` | `ffb66974-6b8b-507d-b73b-5e33afa88e1c` | `S03`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d09:seat:S04` | `024d7520-bec7-5ce0-8f4c-b95d50b61e27` | `S04`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d09:seat:S05` | `2e217fc6-e3cd-5beb-b9e9-6e9716c19521` | `S05`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d09:seat:S06` | `5f6b1ced-091d-5fc8-94a1-23e54f3c6ae1` | `S06`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d09:seat:S07` | `3e8d24df-64ae-5a9e-a34d-1ea6e2d724d5` | `S07`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d09:seat:S08` | `ae5fee63-7e0a-564a-8b32-287454e825f2` | `S08`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d09:seat:S09` | `5548aa37-0092-5f70-b800-7a778e239da7` | `S09`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d09:seat:S10` | `866b5ca3-5bd1-5afb-acc0-e385878c3230` | `S10`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d09:seat:S11` | `64218d83-24a2-5baf-be0d-9250548928b9` | `S11`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d09:seat:S12` | `a9d25f30-25a6-576b-95c6-ae944efbf286` | `S12`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d09:seat:S13` | `e786a39b-0879-5417-8e0b-96ebd1c60c2f` | `S13`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d09:seat:S14` | `a4866bb2-f7ae-5fa8-9fb6-4913e4c8f1c3` | `S14`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d09:seat:S15` | `1ebef678-c342-541d-887b-8a185f7573cc` | `S15`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d09:seat:S16` | `546e22e6-2c33-557d-8064-abc0ed948de6` | `S16`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d09:seat:S17` | `1e8c8989-e45a-5abb-b8d3-5df71e0964a3` | `S17`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d09:seat:S18` | `ade24769-1594-58fb-a519-05746f58137a` | `S18`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d09:seat:S19` | `0ad6be27-2c85-503b-9714-56225873090d` | `S19`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d09:seat:S20` | `4e500580-d937-510d-a8f5-29537f7fa604` | `S20`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d09:seat:S21` | `021ce29f-8a71-5ad3-bb8f-bb904b417d9f` | `S21`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d09:seat:S22` | `a634c776-b9a2-574a-b7f5-29865b203d71` | `S22`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d09:seat:S23` | `85d0b5b8-ef0f-5f5b-beb3-6aec3a9cfa34` | `S23`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d09:seat:S24` | `c0e726fb-9982-5222-87f9-88a9d5a18c3d` | `S24`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d09:seat:S25` | `918a3862-93c8-5ede-b46a-af22ba6577bf` | `S25`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d09:seat:S26` | `c9a15a59-94e4-524e-bc48-682a3ae20c3f` | `S26`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d09:seat:S27` | `b065391d-6ed9-5906-b70a-36f4793d2849` | `S27`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d09:seat:S28` | `63276c2c-0e7d-57c4-b9a1-0da9c3fce769` | `S28`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d09:seat:S29` | `b7fe5061-988d-57ac-b93f-a697c50e9a70` | `S29`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d09:seat:S30` | `ba0f62e6-c8a6-5585-bfc9-0f1e8d7b66b4` | `S30`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d09:seat:S31` | `120f0544-54e5-5a83-b0e7-0fc1650e04ae` | `S31`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d09:seat:S32` | `37dc75cd-01a6-5d1d-9647-b448a8a7df1c` | `S32`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d09:seat:S33` | `87e739dc-a730-5535-b7cc-0be384422025` | `S33`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d09:seat:S34` | `bc4fc70f-11f7-512b-bd6f-2429b152afa5` | `S34`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d09:seat:S35` | `103a45aa-8a95-5d9d-839e-5e8147e026ac` | `S35`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d09:seat:S36` | `dc0b315f-ed81-55e6-bf69-29a430cfb7f6` | `S36`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d09:seat:S37` | `9a16bf78-8a09-50c3-a5df-f68094a15893` | `S37`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d09:seat:S38` | `6a796eaa-a64c-5be2-93e1-90582969bb9d` | `S38`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d09:seat:S39` | `02ba175b-3f8d-55d3-a068-c2b6ab65b325` | `S39`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d09:seat:S40` | `9765c93d-21b5-55e9-a9a5-11658e087646` | `S40`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d09:seat:S41` | `67b8e8e7-2f43-54a1-bb33-77123407613f` | `S41`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d09:seat:S42` | `eb27fb35-a803-53e5-a646-7d04a9eacc8b` | `S42`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d09:seat:S43` | `acbdef76-2e7b-5a48-b832-2b98dac185e4` | `S43`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d09:seat:S44` | `aa22b250-f913-57e1-9ec3-604ec7e65425` | `S44`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d09:seat:S45` | `3bfb47ba-4407-544d-b9ea-447d69250d6b` | `S45`; STANDARD/AVAILABLE |
| Trip | `trip:trip:c:r1:d10` | `d9727b90-add1-5de7-9512-7d787acf841a` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 10 |
| TripSeat | `trip:trip:c:r1:d10:seat:S01` | `b28d15fa-117f-54f7-be8d-a3148870f892` | `S01`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d10:seat:S02` | `9ba29a3c-df31-54af-a5b3-65b8e0558022` | `S02`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d10:seat:S03` | `96c691af-f3aa-5800-86a4-36731efcf122` | `S03`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d10:seat:S04` | `041a2d65-2087-52c5-a5d7-87304d695c63` | `S04`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d10:seat:S05` | `053d53d8-152f-55e8-b7a9-d1e63288fbf4` | `S05`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d10:seat:S06` | `22220a7e-1577-5fce-ac8e-de5ae9cadcfa` | `S06`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d10:seat:S07` | `16d880c7-f22b-59de-a31a-8b39127e3ec7` | `S07`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d10:seat:S08` | `9188e843-bef9-503d-bb7f-1a14a15f6e13` | `S08`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d10:seat:S09` | `74df990a-e849-5814-b202-6ff21b195c1b` | `S09`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d10:seat:S10` | `cc3b13ac-8a01-55ae-87cf-9c000913cbc4` | `S10`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d10:seat:S11` | `254f7ea1-4bc4-5185-86c2-e229ee4556be` | `S11`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d10:seat:S12` | `b9868739-fd88-5a4a-a1e2-1c079be3cf57` | `S12`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d10:seat:S13` | `b76e05f4-93f0-5d87-9d72-e2dcb3535164` | `S13`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d10:seat:S14` | `9afc11ed-2d4d-51d5-afdf-987e22515a15` | `S14`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d10:seat:S15` | `a61566e6-215f-5473-a6e8-df25faf71bfe` | `S15`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d10:seat:S16` | `6470d42d-e500-5b65-877e-27dd3201bfc3` | `S16`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d10:seat:S17` | `bc8e0a9a-d496-5e03-97ce-637121d7d15d` | `S17`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d10:seat:S18` | `12f14f62-d7de-5b06-9edb-b11be315e275` | `S18`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d10:seat:S19` | `ab773aaf-df5a-5437-a89d-eb58cc07a46b` | `S19`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d10:seat:S20` | `3c04c70c-9588-5ee2-b3c7-629afa4c3c06` | `S20`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d10:seat:S21` | `a4be739f-a807-5522-ba51-64f842fd886c` | `S21`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d10:seat:S22` | `73fa7823-eb5e-5e90-ad5a-c567bdfb4a3c` | `S22`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d10:seat:S23` | `3fb5bc4e-389b-5d33-a408-77d35fca29df` | `S23`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d10:seat:S24` | `2515cb43-d42e-55d2-b02e-76eef28c1f30` | `S24`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d10:seat:S25` | `b610b3cb-374c-5042-89d7-9946a8be10fe` | `S25`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d10:seat:S26` | `98e5be23-5e02-546c-a20c-82beb68cb22f` | `S26`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d10:seat:S27` | `1b8eb1cf-14bf-5350-97c7-7eafb74d78ef` | `S27`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d10:seat:S28` | `dd523c20-800b-50e1-a0d3-4df26f7519f5` | `S28`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d10:seat:S29` | `49b46699-a523-539a-9f55-fbe4f6110044` | `S29`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d10:seat:S30` | `a7848832-097e-57e1-be5f-24b45d9a8108` | `S30`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d10:seat:S31` | `481aa594-a343-5211-a4cd-2bd18aea511c` | `S31`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d10:seat:S32` | `a1aee533-acad-5638-862c-330e48f88423` | `S32`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d10:seat:S33` | `bb604b4e-f3ea-53e6-a04b-a6cb39e9308e` | `S33`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d10:seat:S34` | `6a92460c-9f0d-5674-903e-b76c54594c70` | `S34`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d10:seat:S35` | `cc6ff2d5-dbb3-51c6-8f8e-38b4beddba88` | `S35`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d10:seat:S36` | `c34058a5-d3a9-57f6-9ebe-7f151415e90a` | `S36`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d10:seat:S37` | `15cfa9f3-2566-5204-beba-37054bc5318a` | `S37`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d10:seat:S38` | `13ea09f7-5474-5fd6-9202-32944dec1685` | `S38`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d10:seat:S39` | `80744a6f-da09-53ee-a12c-84384e338e56` | `S39`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d10:seat:S40` | `35ad5452-e486-5f90-adec-5ea7c81f50ca` | `S40`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d10:seat:S41` | `2c2d0b68-6160-54b5-9c96-83fcf1727c44` | `S41`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d10:seat:S42` | `355a00ca-61b3-5b8d-af49-797aae048039` | `S42`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d10:seat:S43` | `51ff74c1-8fd7-5417-a02e-e51b30523151` | `S43`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d10:seat:S44` | `f24815a4-186b-5939-8216-35fe43d9d50c` | `S44`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d10:seat:S45` | `3b2e0543-352f-5d42-be64-78c81196ed40` | `S45`; STANDARD/AVAILABLE |
| Trip | `trip:trip:c:r1:d11` | `531c12f6-cb12-563a-8118-4392250a7f5c` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 11 |
| TripSeat | `trip:trip:c:r1:d11:seat:S01` | `9b8c51fa-a5b8-5e9f-b51a-742506e54180` | `S01`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d11:seat:S02` | `290d6e46-4332-50e1-9ffb-79f6b49686a8` | `S02`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d11:seat:S03` | `a5f4a537-fa71-5a51-b9a1-4431ef719b75` | `S03`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d11:seat:S04` | `141563fb-7924-5316-9cd5-28bc9cf0478b` | `S04`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d11:seat:S05` | `a81b7e83-aabe-5469-b897-861a2336cb17` | `S05`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d11:seat:S06` | `92400797-374b-5b57-9374-d24334955be0` | `S06`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d11:seat:S07` | `67b6bbcc-f6d1-5100-953a-9651204aa997` | `S07`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d11:seat:S08` | `90d0c190-b80f-5ebf-93e5-eedf3db6aa22` | `S08`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d11:seat:S09` | `deefc552-5876-5157-97cf-d1fee4d35c40` | `S09`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d11:seat:S10` | `795a59f7-84dc-507c-b960-59bda52242cf` | `S10`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d11:seat:S11` | `3ae13152-77ca-5447-a5c6-b644b2df8aa7` | `S11`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d11:seat:S12` | `fd08a94e-6adc-59d1-a0e5-c04b61bcbdae` | `S12`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d11:seat:S13` | `255c3819-e1cc-51dd-9da9-2b2a010a3301` | `S13`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d11:seat:S14` | `67d8f226-7fde-5cf0-bf81-4ec062fab693` | `S14`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d11:seat:S15` | `a0ea051d-4ccd-5abf-9b36-bb17609bf26f` | `S15`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d11:seat:S16` | `931763b3-2d7c-5fb5-8b4d-e0f2fa374e09` | `S16`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d11:seat:S17` | `be39d564-d23d-57a2-9f05-b99cbf44a4a2` | `S17`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d11:seat:S18` | `aaaf648c-24e1-59c4-9af6-98631ca1df22` | `S18`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d11:seat:S19` | `bfc86cb4-d43b-5d46-b779-9a022ce746ad` | `S19`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d11:seat:S20` | `397bf0e9-8a6a-5a51-ae96-d8b2b781ca19` | `S20`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d11:seat:S21` | `66215e49-682f-5471-b84e-b7ca6f634577` | `S21`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d11:seat:S22` | `d628b76d-1653-5932-b79b-d9201a89c36b` | `S22`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d11:seat:S23` | `567b2c85-c254-5ab1-8219-4e491703a015` | `S23`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d11:seat:S24` | `5b3e7b43-5d30-5d11-a649-7ec27d0ed17e` | `S24`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d11:seat:S25` | `34f52d2a-e349-5038-93de-67ae45b97f4e` | `S25`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d11:seat:S26` | `d8edc69c-c4f5-53d6-aaf6-5b0f941daacd` | `S26`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d11:seat:S27` | `3f963ff3-05cd-51ed-be8b-66dbe7d7d343` | `S27`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d11:seat:S28` | `3b0cdff7-8cba-521d-8678-9ea2c3527770` | `S28`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d11:seat:S29` | `1ec552e7-94a9-5d7a-8091-2075ee34b3ef` | `S29`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d11:seat:S30` | `8e947d6d-4824-5c9b-b06d-28e927bce0e8` | `S30`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d11:seat:S31` | `f1e410cb-3af0-5ced-b687-7ecdab30be78` | `S31`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d11:seat:S32` | `283375a7-09c9-5511-b314-f8981ef72535` | `S32`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d11:seat:S33` | `dd9d638b-e1f2-5cc8-8d2c-e03d23daf6f3` | `S33`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d11:seat:S34` | `2b03f605-1cb7-5307-8366-a33b9884cc4c` | `S34`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d11:seat:S35` | `8f4cc115-c785-5fbf-a529-0ed3d9b8f319` | `S35`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d11:seat:S36` | `97778808-c18b-5951-8137-1cf5940fb081` | `S36`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d11:seat:S37` | `914aa305-f556-5a08-ab56-14aa24b76b29` | `S37`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d11:seat:S38` | `52d46133-706b-58c8-9d22-a79810a8aa38` | `S38`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d11:seat:S39` | `dbfca695-3273-55e5-a121-423d43355318` | `S39`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d11:seat:S40` | `9df47a6b-c9c1-5c0d-884b-16534d61e601` | `S40`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d11:seat:S41` | `0fb95275-862a-5f33-983a-7652e900908a` | `S41`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d11:seat:S42` | `05215a95-4fc6-5d34-b1e2-75b978b4075f` | `S42`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d11:seat:S43` | `81fb9ac1-dda7-50e6-a824-74bf998bf415` | `S43`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d11:seat:S44` | `cc738ff2-30bb-59c5-8829-0b1a18c4783b` | `S44`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d11:seat:S45` | `8fa73969-fece-50a5-af10-e597bc896bcd` | `S45`; STANDARD/AVAILABLE |
| Trip | `trip:trip:c:r1:d12` | `b6321e6b-854b-530e-9f0f-69689db496e1` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 12 |
| TripSeat | `trip:trip:c:r1:d12:seat:S01` | `a5a04405-fe0c-58fe-b875-bd3349c0534c` | `S01`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d12:seat:S02` | `e6af4861-2fb1-545b-82d6-e017d2a9185f` | `S02`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d12:seat:S03` | `b2f7d845-5ab2-57d0-bb89-e424f4d254b8` | `S03`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d12:seat:S04` | `97dd17c6-dcbd-53af-b199-ef2840624f03` | `S04`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d12:seat:S05` | `101c3f93-5759-57b7-9b9d-e748cee596c0` | `S05`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d12:seat:S06` | `072b1436-9863-55dc-bd93-ab5a4e7c6be8` | `S06`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d12:seat:S07` | `5cb50f79-c464-5198-8dd0-18635a209932` | `S07`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d12:seat:S08` | `abd91aa8-0364-5bab-a1b6-12c5a03b6a63` | `S08`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d12:seat:S09` | `aa609afd-911c-57e5-ba4e-ac3439cfbaf8` | `S09`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d12:seat:S10` | `76b59881-ab16-51f0-8ed4-1617cd2d795d` | `S10`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d12:seat:S11` | `3d0aebda-5680-5493-8b1d-426ad351720f` | `S11`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d12:seat:S12` | `b90927b3-b4fb-5575-813a-f9f79f755f62` | `S12`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d12:seat:S13` | `33ced44c-2939-5bc5-8296-a305fe13372b` | `S13`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d12:seat:S14` | `e7a348d8-e28a-54ae-8148-12fa976ff3ab` | `S14`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d12:seat:S15` | `a92b4188-9c4c-51f4-873a-c11f44c46288` | `S15`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d12:seat:S16` | `4c2e5a6b-9865-582b-b21a-37514b103fda` | `S16`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d12:seat:S17` | `6eb638a6-5767-5f0c-aa06-7090754fef92` | `S17`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d12:seat:S18` | `f3622e77-9078-5941-b947-cfdd235a2669` | `S18`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d12:seat:S19` | `fa1a4dba-fb1f-5a4c-92ea-5f06c4e2c73f` | `S19`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d12:seat:S20` | `18419ca8-cb74-5199-a77f-056873a4dcf0` | `S20`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d12:seat:S21` | `b408bc1f-7459-5612-ab79-db3b67f0b4be` | `S21`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d12:seat:S22` | `c48f547b-5650-55cd-8c43-8e55cb9d530c` | `S22`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d12:seat:S23` | `c0b760e1-8442-54e2-87e2-246b83633fb0` | `S23`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d12:seat:S24` | `5850a3b2-0e7c-5c2c-a4e8-793c0c2551fe` | `S24`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d12:seat:S25` | `01bf3129-7aa8-5e49-8b0c-d302323b22e0` | `S25`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d12:seat:S26` | `2efc8fcc-b1ac-58a7-80a9-f11fcfa86187` | `S26`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d12:seat:S27` | `cc90b081-5692-5224-9c1c-081ee7509cbd` | `S27`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d12:seat:S28` | `e60aadaa-37c5-5b87-896c-5fd13992d1b3` | `S28`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d12:seat:S29` | `7a2db5d5-c352-5f3e-9035-8ae291b2eaf9` | `S29`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d12:seat:S30` | `ceb300a2-5e15-5166-b6fe-bb92233db1f0` | `S30`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d12:seat:S31` | `78bf07f0-218e-5e39-bed6-048ab384b195` | `S31`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d12:seat:S32` | `4b204008-16ee-510d-9645-6aa0734bbe8a` | `S32`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d12:seat:S33` | `02afe53f-56e8-5944-9d7b-6ee002100640` | `S33`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d12:seat:S34` | `2cb0e000-dbd4-5568-bda9-36723570b319` | `S34`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d12:seat:S35` | `aa66394b-b103-51c9-b082-b459c3eeeb60` | `S35`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d12:seat:S36` | `8099382b-3a26-546b-b6b4-fa0a2780af9e` | `S36`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d12:seat:S37` | `5328d9a2-f4cb-514b-acdd-13f3f2b6b7f8` | `S37`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d12:seat:S38` | `4474f3c8-4b5c-576b-b28a-e327cc79152f` | `S38`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d12:seat:S39` | `58aa72a8-a300-5960-9751-45f44b219ab7` | `S39`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d12:seat:S40` | `a813265e-8080-5d65-a4b9-6dec6c4a6ef1` | `S40`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d12:seat:S41` | `87b6c63d-f74b-5c86-9938-2f9a3e1dd513` | `S41`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d12:seat:S42` | `6c5df031-de6d-56bb-89ac-3741736a48e7` | `S42`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d12:seat:S43` | `a903e078-5079-5c19-90f1-47b0ae3759ee` | `S43`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d12:seat:S44` | `e371d01b-c3e8-5fe5-8557-6529faaf6be0` | `S44`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d12:seat:S45` | `f7652dff-bf8a-539a-9d75-88a1aef2782b` | `S45`; STANDARD/AVAILABLE |
| Trip | `trip:trip:c:r1:d13` | `7fd78aac-0cb3-5b5f-8eaf-626a45d847e1` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 13 |
| TripSeat | `trip:trip:c:r1:d13:seat:S01` | `15f6eb7a-15ef-509b-93f7-573ec03a7913` | `S01`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d13:seat:S02` | `509356fd-4ce1-5238-9d02-7733ad426c10` | `S02`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d13:seat:S03` | `c8d02e49-561a-571c-bfe4-3fb86cd45620` | `S03`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d13:seat:S04` | `b5d0ede0-20d2-5eac-9423-0bc0034ce262` | `S04`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d13:seat:S05` | `0f594ca2-7e37-5848-ba0e-67a3bf9ddbf3` | `S05`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d13:seat:S06` | `d916a311-fc57-500b-baa9-5ebaf245832e` | `S06`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d13:seat:S07` | `b57fb3da-716f-542f-ad02-51de0319274d` | `S07`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d13:seat:S08` | `b82570fc-77f1-5e52-8c10-6ce77b8faf43` | `S08`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d13:seat:S09` | `cc3aade3-6929-5c02-bcb0-a29e3e9888ba` | `S09`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d13:seat:S10` | `05a5209f-350e-5c1a-8a0c-4c19bf9e9c31` | `S10`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d13:seat:S11` | `a9113b9d-076e-5b0c-8d19-6ea104ceb887` | `S11`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d13:seat:S12` | `a76d30b2-7c25-5402-8475-95458c461bcd` | `S12`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d13:seat:S13` | `550ac412-c072-53b5-84da-f931a4db7430` | `S13`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d13:seat:S14` | `fc68640c-5097-57a1-aa67-71c8b4eb0b4a` | `S14`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d13:seat:S15` | `16caf631-73c7-5d13-bf63-91ed6883f28b` | `S15`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d13:seat:S16` | `102520a1-79b5-5bcc-9f4e-9913ef961104` | `S16`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d13:seat:S17` | `41eadb3f-d013-532f-8c64-9d45d6c13a38` | `S17`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d13:seat:S18` | `7beb660f-d158-5dca-a0ff-d432bba096bd` | `S18`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d13:seat:S19` | `96791d24-099f-546b-b35d-562212c0def6` | `S19`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d13:seat:S20` | `bb84c572-532a-59d2-87bc-6c6b6542befe` | `S20`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d13:seat:S21` | `fabf1fba-50bb-559f-8154-1496a540ff23` | `S21`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d13:seat:S22` | `b5ffc509-247b-50d9-99b7-00ecea7cf112` | `S22`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d13:seat:S23` | `230bbaf2-7bd6-59e2-9456-f5f3dfc632c3` | `S23`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d13:seat:S24` | `dca14dd7-0153-5aea-b95a-fed0e7b217e4` | `S24`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d13:seat:S25` | `c780c32b-b19b-596b-9cdd-ea97dbd48f34` | `S25`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d13:seat:S26` | `ca9a0c65-98ad-580a-8915-a999bd318ed6` | `S26`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d13:seat:S27` | `a314fd4f-3e4b-54c9-b943-7a3ceeaa6979` | `S27`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d13:seat:S28` | `c69b86ea-2f37-5d40-879d-d98851d4e3fd` | `S28`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d13:seat:S29` | `f98b1c38-5a52-5efd-b38f-6346949e203b` | `S29`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d13:seat:S30` | `70b2c7fb-babb-5848-8910-0fdcf0c33b0a` | `S30`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d13:seat:S31` | `7b779d81-f083-51e5-8cae-a9e7020c2d45` | `S31`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d13:seat:S32` | `447d43bc-8828-5340-8697-e34974684464` | `S32`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d13:seat:S33` | `af7a71b7-7ec4-59e4-a5ff-a696c41372ee` | `S33`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d13:seat:S34` | `53baee1c-0e1a-5d9a-b43d-3f5389df6f50` | `S34`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d13:seat:S35` | `d409d296-698e-5475-8e98-655e40b4123b` | `S35`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d13:seat:S36` | `68319e52-98e4-5b06-b047-d3c18e1b1d50` | `S36`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d13:seat:S37` | `2dd8ffe9-9de6-5699-9704-82b3432946ac` | `S37`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d13:seat:S38` | `8c580cd7-fd0a-5faa-bb09-a34de959342d` | `S38`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d13:seat:S39` | `f620fac6-f8dc-510b-a175-df03fe73730d` | `S39`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d13:seat:S40` | `eb557066-d914-54b3-af00-4b497c0476aa` | `S40`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d13:seat:S41` | `8cb23387-6e47-50b9-816f-28e99d00a4d1` | `S41`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d13:seat:S42` | `e6e7f06e-f21b-5a17-a7e7-0222efd293ca` | `S42`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d13:seat:S43` | `e6eedeec-2d52-575b-93d1-a27f48018e1d` | `S43`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d13:seat:S44` | `27077535-8997-5a09-b063-0c439b703b54` | `S44`; STANDARD/AVAILABLE |
| TripSeat | `trip:trip:c:r1:d13:seat:S45` | `83a099a3-25c7-5ec5-9979-80def13b51e7` | `S45`; STANDARD/AVAILABLE |
| Trip | `trip:trip:c:r2:d00` | `e8600e6e-bb72-5096-819a-4964c49854bf` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 00 |
| TripSeat | `trip:trip:c:r2:d00:seat:V01` | `1c263b29-e48a-5d09-a355-fb5703af5853` | `V01`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d00:seat:V02` | `8091113b-9b2d-50bb-960c-6822b40e82fd` | `V02`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d00:seat:V03` | `05963780-779b-5104-ae81-d9829432464b` | `V03`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d00:seat:V04` | `e8dc1047-ca2d-5923-8007-56ac4fbc6c34` | `V04`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d00:seat:V05` | `57738a33-ceb2-56cc-8638-2f7c26457e9d` | `V05`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d00:seat:V06` | `d0d09fc8-ece1-5397-87ac-5b95de236bae` | `V06`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d00:seat:V07` | `202ba056-5d7f-5565-9aaa-5b5e37ba1c93` | `V07`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d00:seat:V08` | `0609f604-d9ad-588b-b1b8-d7ebb59e9f5e` | `V08`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d00:seat:V09` | `e10f3434-2b2e-5c39-9ec9-1ff3fc442d1b` | `V09`; VIP/AVAILABLE |
| Trip | `trip:trip:c:r2:d01` | `942bc2dc-d2f2-5b9a-ba47-67272b095b8a` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 01 |
| TripSeat | `trip:trip:c:r2:d01:seat:V01` | `f94b4503-4663-5b32-bf98-4732bc3ae18e` | `V01`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d01:seat:V02` | `bf1f5226-599e-53f1-a4ae-563028183381` | `V02`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d01:seat:V03` | `e5696794-88f8-5123-a4af-ab199e752ee9` | `V03`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d01:seat:V04` | `f6df7c9c-a7d4-577e-be96-dff587291046` | `V04`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d01:seat:V05` | `b3c9448f-ca5f-570e-a124-93fde68a4333` | `V05`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d01:seat:V06` | `8e0f6234-60b9-54e7-baba-f4059b89b84a` | `V06`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d01:seat:V07` | `43a47633-1c4a-5ca2-aae7-072e56897250` | `V07`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d01:seat:V08` | `3f435248-c6a4-5de4-a39a-7546c8a7be82` | `V08`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d01:seat:V09` | `7f3f6c21-c97c-541a-940f-45b2447ec73a` | `V09`; VIP/AVAILABLE |
| Trip | `trip:trip:c:r2:d02` | `89633149-c673-508b-9b8c-954f7b2964f6` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 02 |
| TripSeat | `trip:trip:c:r2:d02:seat:V01` | `e4161daa-3812-5fe5-b2fd-c53fd3b5ba73` | `V01`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d02:seat:V02` | `c22274f9-7265-58b5-934f-11d47017f611` | `V02`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d02:seat:V03` | `2e407b94-4ac0-52cd-85b5-86694eb1efc6` | `V03`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d02:seat:V04` | `8a9d2baf-f852-5e21-874e-c602f9af86e8` | `V04`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d02:seat:V05` | `4ae28bb0-a98c-54de-8f47-d2a71ef58c1e` | `V05`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d02:seat:V06` | `fa81726e-2da6-5d61-b9c4-51fa2a18a3f9` | `V06`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d02:seat:V07` | `ce2a9f0f-9d1f-5d13-8465-e7a5fb0b7a0d` | `V07`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d02:seat:V08` | `23c54503-9ced-51eb-a4ca-abb3d3ac223d` | `V08`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d02:seat:V09` | `a4c11921-2367-567b-9da3-ccd72cb176ee` | `V09`; VIP/AVAILABLE |
| Trip | `trip:trip:c:r2:d03` | `48ac842e-8267-59d3-9b07-6ac6f34ba31e` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 03 |
| TripSeat | `trip:trip:c:r2:d03:seat:V01` | `f3d30f82-e1c2-5591-a043-cffb89e44bd0` | `V01`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d03:seat:V02` | `cb2c58a7-6ed2-534e-814c-adfa23e26fe4` | `V02`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d03:seat:V03` | `2033d7e6-2456-52f6-bf6e-16de6840ae79` | `V03`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d03:seat:V04` | `5f39af69-938f-5f45-8a28-18bc8a5e8194` | `V04`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d03:seat:V05` | `b4514eb8-d346-5522-91dc-c39a4d54f13a` | `V05`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d03:seat:V06` | `5531c8af-521b-5d6f-9a93-2a334961a601` | `V06`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d03:seat:V07` | `cae1c96b-7a72-55b6-a429-3245e85d6e93` | `V07`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d03:seat:V08` | `29a8da25-f8d9-53e6-abd2-e92613d6159f` | `V08`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d03:seat:V09` | `8278f16a-100e-5212-a313-cdef05b1d282` | `V09`; VIP/AVAILABLE |
| Trip | `trip:trip:c:r2:d04` | `0e3c3409-9fca-5893-97ef-82a92b5d4341` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 04 |
| TripSeat | `trip:trip:c:r2:d04:seat:V01` | `fbdfbc2d-57d8-59d2-922a-611fdd570892` | `V01`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d04:seat:V02` | `2654faf4-826f-5ce4-8c8b-615c15cba776` | `V02`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d04:seat:V03` | `d1647459-7d69-59e4-8207-65ccb28b62b9` | `V03`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d04:seat:V04` | `d488e47d-e063-5f7d-bf66-4468b839eb8c` | `V04`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d04:seat:V05` | `1ee106a7-a2a9-5973-91d4-add12a597753` | `V05`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d04:seat:V06` | `067aaf2c-bf3f-54d3-a2e3-1794911ec9a0` | `V06`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d04:seat:V07` | `44a06098-f5f3-5557-9790-475148dfa8c2` | `V07`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d04:seat:V08` | `596fd8fb-318d-5a98-ac41-0f7a57f3e120` | `V08`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d04:seat:V09` | `1d89642b-cd63-5406-9b88-c2fa9b78fc26` | `V09`; VIP/AVAILABLE |
| Trip | `trip:trip:c:r2:d05` | `307aaace-59d7-5b51-a5b2-1f0ba193aab1` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 05 |
| TripSeat | `trip:trip:c:r2:d05:seat:V01` | `394b75ed-01b8-56b3-bab0-10a179dd131d` | `V01`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d05:seat:V02` | `c7380740-e19a-5193-b17e-9cfaf782420a` | `V02`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d05:seat:V03` | `10b3aa61-96d2-598d-817e-9b32042fcb4a` | `V03`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d05:seat:V04` | `8519bdde-5a35-5a41-81ca-50c59cf3dfac` | `V04`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d05:seat:V05` | `64c66abd-23df-5e4a-af2d-efa8fb2c4e36` | `V05`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d05:seat:V06` | `4876fa3d-152c-5b7f-9456-0bca3fad563a` | `V06`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d05:seat:V07` | `93c055e8-4709-5448-a9af-14e78b6709f8` | `V07`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d05:seat:V08` | `b7780eb1-a7ea-541e-914f-9b2be6b7d673` | `V08`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d05:seat:V09` | `0cf8a18c-1453-5dee-9dc1-c2508c33e8f7` | `V09`; VIP/AVAILABLE |
| Trip | `trip:trip:c:r2:d06` | `2b563eda-4a25-59fe-afc7-b8bdf55a91ca` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 06 |
| TripSeat | `trip:trip:c:r2:d06:seat:V01` | `8d9c64c1-d2fb-5ba8-9243-9a30533cc61a` | `V01`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d06:seat:V02` | `db9bd3fe-9cbf-54ff-8b00-a4e468b54528` | `V02`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d06:seat:V03` | `aa61746c-9df4-524a-860a-7d2fca86f634` | `V03`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d06:seat:V04` | `778e1312-36dc-58fd-9839-4fe0964ab663` | `V04`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d06:seat:V05` | `fe80f1ff-40f6-5dbf-a58d-127ad21c14c9` | `V05`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d06:seat:V06` | `11f11b00-382b-566c-b980-607d6809b9b7` | `V06`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d06:seat:V07` | `9dc2bfd3-ac0d-5d72-b999-ab83699fe87a` | `V07`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d06:seat:V08` | `295f2848-a6c8-5207-93d3-0d701c51df3a` | `V08`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d06:seat:V09` | `e9da8642-879a-56e7-8018-57ee9b6342cb` | `V09`; VIP/AVAILABLE |
| Trip | `trip:trip:c:r2:d07` | `bca45097-0475-5ac0-b082-4a0f29264c39` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 07 |
| TripSeat | `trip:trip:c:r2:d07:seat:V01` | `4970bb8d-62bd-59d0-9814-5af9a3ad191a` | `V01`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d07:seat:V02` | `71d06527-3c69-5f85-8e22-6c17907af9ad` | `V02`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d07:seat:V03` | `c6a0507b-2758-5586-bb95-301e7010d921` | `V03`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d07:seat:V04` | `3225b533-1eaa-53a6-bc56-1ba2ddb532f9` | `V04`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d07:seat:V05` | `29461af1-67a6-5bfc-9d78-380a7a046882` | `V05`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d07:seat:V06` | `bf268f14-31d3-5238-ac94-fc658e6bf877` | `V06`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d07:seat:V07` | `cbfd7d7e-8cff-56dc-ba82-862cc6e192db` | `V07`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d07:seat:V08` | `1712cd36-22c2-5d94-82c1-b24c84671695` | `V08`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d07:seat:V09` | `a5908ebd-0c15-5d47-8039-156844e27f93` | `V09`; VIP/AVAILABLE |
| Trip | `trip:trip:c:r2:d08` | `035273a8-cf24-5d2e-b684-5fe500087ef2` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 08 |
| TripSeat | `trip:trip:c:r2:d08:seat:V01` | `2febe3ad-aa8a-5143-b9d0-834574edb0f9` | `V01`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d08:seat:V02` | `8a6c578c-99ad-5bae-902c-8598b68fd95b` | `V02`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d08:seat:V03` | `8fd5d9de-fc0d-5896-822a-667742041ae9` | `V03`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d08:seat:V04` | `b311ab01-4283-5922-95fa-0d97d86626e7` | `V04`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d08:seat:V05` | `52476d8b-72df-51e6-b9ce-17d6e54010e1` | `V05`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d08:seat:V06` | `294d06b3-ec4e-52b7-8e7a-d73d2facfa5d` | `V06`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d08:seat:V07` | `dc2e9f95-6541-5323-82a8-8c66f4755196` | `V07`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d08:seat:V08` | `8f091834-d747-569b-b653-e4e1086006e6` | `V08`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d08:seat:V09` | `bbf5ce48-061d-5e44-b820-45bc8152b4f5` | `V09`; VIP/AVAILABLE |
| Trip | `trip:trip:c:r2:d09` | `880c4797-3991-5fde-9001-31ea56abe7fd` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 09 |
| TripSeat | `trip:trip:c:r2:d09:seat:V01` | `0d8c1d9d-f136-5377-99d7-0b073ef6d0ed` | `V01`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d09:seat:V02` | `399552e8-96a5-58b1-9f05-7067c6f73117` | `V02`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d09:seat:V03` | `e6a2a3fa-b8e1-56a7-b62e-ba30c6e80362` | `V03`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d09:seat:V04` | `72533f83-1444-5d8c-b419-4f58314d59b4` | `V04`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d09:seat:V05` | `5bcd7299-7dd0-57a1-9f8e-c75a2aa823b5` | `V05`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d09:seat:V06` | `58729b76-cb1a-5567-ad0a-b349721a81ab` | `V06`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d09:seat:V07` | `57ec9115-bbdf-5391-a8bd-27fdb4502cad` | `V07`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d09:seat:V08` | `ccb92c2c-5546-5f77-acf8-74491d368454` | `V08`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d09:seat:V09` | `995079d8-c566-5176-9c9d-843ae3415bcf` | `V09`; VIP/AVAILABLE |
| Trip | `trip:trip:c:r2:d10` | `6410d74d-b78c-5ffd-a63b-b3ec2884c528` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 10 |
| TripSeat | `trip:trip:c:r2:d10:seat:V01` | `0a710333-a106-59a2-a20f-d83c9fb671f8` | `V01`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d10:seat:V02` | `e0787004-2c75-5370-9756-852bf13ef950` | `V02`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d10:seat:V03` | `c996f1d0-fb9e-5f3f-ae81-cd2b2d8ae390` | `V03`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d10:seat:V04` | `957769f6-897e-502a-a640-8c9b101bba15` | `V04`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d10:seat:V05` | `83ef6abd-b2c7-5385-925e-b1b52bea6307` | `V05`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d10:seat:V06` | `8e9ab35f-784a-5d38-aa3d-24fe12796be0` | `V06`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d10:seat:V07` | `d511b688-5bbb-520e-9e71-fa42730106a7` | `V07`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d10:seat:V08` | `33186fe2-e742-58ea-9518-5b6c0f67782e` | `V08`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d10:seat:V09` | `f0b1a923-17f8-5ac1-9302-66b940d69064` | `V09`; VIP/AVAILABLE |
| Trip | `trip:trip:c:r2:d11` | `a2811cda-f7e3-5097-8fa3-1694e2ac3c28` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 11 |
| TripSeat | `trip:trip:c:r2:d11:seat:V01` | `889383c5-b1b3-50d7-bd7d-b3875313f127` | `V01`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d11:seat:V02` | `6b0ce784-64d7-5039-9a02-cda3588a15dd` | `V02`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d11:seat:V03` | `7fce0083-c87f-50ce-bf1a-49ef49dc56ab` | `V03`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d11:seat:V04` | `af69ff43-2c87-5a74-b6dd-318530be19d6` | `V04`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d11:seat:V05` | `ccea2d2d-bda4-5c6e-83a8-fef8b5af6f3a` | `V05`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d11:seat:V06` | `f4e526c8-aed9-51a2-b825-2d79237dba1b` | `V06`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d11:seat:V07` | `75a5b48e-42ba-570a-aa51-23b3dbfde7b8` | `V07`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d11:seat:V08` | `2afddc34-124f-59c3-8e93-b25005cd3760` | `V08`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d11:seat:V09` | `6f4c80dd-d911-58c7-a03e-327088f1e84b` | `V09`; VIP/AVAILABLE |
| Trip | `trip:trip:c:r2:d12` | `2ed142de-38e7-55d8-8799-ff36029d3812` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 12 |
| TripSeat | `trip:trip:c:r2:d12:seat:V01` | `4cdc417c-46a5-5e62-be4d-e04044b2e20b` | `V01`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d12:seat:V02` | `0c595e30-7375-5ff5-9981-b8326c4e014f` | `V02`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d12:seat:V03` | `79dbd800-0d8f-59a2-b70d-2e0e2d22ab0c` | `V03`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d12:seat:V04` | `6fe38b9f-6631-558f-b60b-15c23edcf36a` | `V04`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d12:seat:V05` | `d89af49e-43f1-5158-b236-705182b512c0` | `V05`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d12:seat:V06` | `153223d5-4647-5fab-95bc-f0ea20197bed` | `V06`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d12:seat:V07` | `3bcbe963-b72e-5104-a4f4-76615b273fa0` | `V07`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d12:seat:V08` | `edf83ef9-0cb8-594c-aea0-94ff87e3943c` | `V08`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d12:seat:V09` | `23dae8a0-4fb2-5147-be32-62e55b6f4d07` | `V09`; VIP/AVAILABLE |
| Trip | `trip:trip:c:r2:d13` | `24a73543-2d1d-5bd4-a806-e6c80a76b8b6` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 13 |
| TripSeat | `trip:trip:c:r2:d13:seat:V01` | `e0ea9699-7beb-53aa-8125-2f606a69220f` | `V01`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d13:seat:V02` | `65ed2940-00bf-57be-8c09-8c11c3586d1c` | `V02`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d13:seat:V03` | `04544219-7200-5346-936d-d5dc9af6aa57` | `V03`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d13:seat:V04` | `4f0a95ec-fa81-52b0-acd4-b80549776dd8` | `V04`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d13:seat:V05` | `b939858d-2633-5fa2-bfda-bb9221aac593` | `V05`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d13:seat:V06` | `8a7fc39a-08c5-5526-8a74-d1715c4c3410` | `V06`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d13:seat:V07` | `6bce83c1-270a-5b22-a121-191289dfebd4` | `V07`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d13:seat:V08` | `ab141f29-ce9e-5ed8-a709-f2e2e39b2466` | `V08`; VIP/AVAILABLE |
| TripSeat | `trip:trip:c:r2:d13:seat:V09` | `fb2d2fd1-4dd6-5c9f-b976-86592595e3c6` | `V09`; VIP/AVAILABLE |
| Trip | `trip:trip:c:r3:d00` | `36f7a0fb-87e5-5657-bc02-5505e34503b3` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 00 |
| TripSeat | `trip:trip:c:r3:d00:seat:L01` | `a02dd23d-e9be-583f-bb6e-663e6e5bdf90` | `L01`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d00:seat:L02` | `8279ca85-25ba-50b4-9a27-ce272a7ca1c4` | `L02`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d00:seat:L03` | `842987c3-ce98-5fc7-a270-575b4a1b3862` | `L03`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d00:seat:L04` | `cd156424-ea38-5363-bb8a-849b8326e492` | `L04`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d00:seat:L05` | `9e3ce507-8979-5723-a0f7-b8df38314c75` | `L05`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d00:seat:L06` | `66eb0742-85dc-5b0e-bdef-cdaaf03f23e7` | `L06`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d00:seat:L07` | `033964a3-cd1b-57e2-9ba7-d19451ad240d` | `L07`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d00:seat:L08` | `cb085cd9-fab5-55bc-bc5a-dd3e0cacd1da` | `L08`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d00:seat:L09` | `16eb3b14-1ae8-543a-8bd2-170a8197cc9f` | `L09`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d00:seat:L10` | `d3372eb6-097c-58ba-9f66-c68a91a89c39` | `L10`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d00:seat:L11` | `4261a3d4-14af-57c2-808f-c9d4fa47b718` | `L11`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d00:seat:L12` | `9c1aae09-e96d-598e-9920-cb2291a03054` | `L12`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d00:seat:L13` | `6252777b-e9fc-5154-8945-43e630a6b20b` | `L13`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d00:seat:L14` | `377d0197-6b28-5b08-9b26-fc914f0f4c95` | `L14`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d00:seat:L15` | `10dec096-924c-5068-aa39-166485f10b5c` | `L15`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d00:seat:L16` | `8dfb5970-9e06-53e7-9ce6-c27ebda1dd9c` | `L16`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d00:seat:L17` | `494c64d4-ab6c-506a-8c27-85b52925486a` | `L17`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d00:seat:L18` | `0fb1991f-dc95-5eb0-b7f5-e31e4f826317` | `L18`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d00:seat:L19` | `f62ea78e-fccd-5856-8cb0-281438648a63` | `L19`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d00:seat:L20` | `cc0af6ff-1876-5d8d-94c3-39e7282e7e0e` | `L20`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d00:seat:U01` | `3e1e0e63-7ee0-5677-94f6-590ef4bbe41d` | `U01`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d00:seat:U02` | `85b8ed3c-337c-573d-8e60-c16e61b181ed` | `U02`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d00:seat:U03` | `e8da6d03-101a-5b7f-b57b-1a0ea51ea83a` | `U03`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d00:seat:U04` | `584e7088-1d24-5211-8b38-68e15e7bc31b` | `U04`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d00:seat:U05` | `424122fc-3dd8-5b69-9e2a-58cd7db6a707` | `U05`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d00:seat:U06` | `0cf5669b-67b2-5e1b-aae5-07637a3c4c31` | `U06`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d00:seat:U07` | `3e2db6a8-56f3-5a11-9a82-b96f28c1439c` | `U07`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d00:seat:U08` | `f635c572-18d2-5569-a23e-1e3524dcfede` | `U08`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d00:seat:U09` | `aa706a48-362c-5475-a86d-807a48337505` | `U09`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d00:seat:U10` | `7fec60e8-b1e4-5e57-a157-ce54df273afb` | `U10`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d00:seat:U11` | `b41fd461-7db1-5ba7-adab-a39575f2bb9f` | `U11`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d00:seat:U12` | `f55f36fe-70d5-5fa9-a851-8e3cbcea1c52` | `U12`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d00:seat:U13` | `e9d480c0-8eef-5d2b-b154-0c5893e7bb98` | `U13`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d00:seat:U14` | `ae8d7504-5a3e-5733-a28a-abfbe9ed1ac6` | `U14`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d00:seat:U15` | `5de7fb3e-69f2-584a-8e2e-2bb35c56cbe0` | `U15`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d00:seat:U16` | `cf0581ef-372d-5815-bc26-1f0354249af0` | `U16`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d00:seat:U17` | `cbcbbc5c-1619-5749-bd85-0d68163fddd5` | `U17`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d00:seat:U18` | `582f25bb-c23d-52e5-aa05-3952d4206a78` | `U18`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d00:seat:U19` | `a4d756cb-da59-5591-88fc-d36a8ff99655` | `U19`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d00:seat:U20` | `a6e9d1a1-aa0b-50c3-bc85-218f4b11da5c` | `U20`; SLEEPER_UPPER/AVAILABLE |
| Trip | `trip:trip:c:r3:d01` | `7296939f-bc99-5421-9945-6cfa3265fe8c` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 01 |
| TripSeat | `trip:trip:c:r3:d01:seat:L01` | `a4e5647b-4b49-527f-b849-c70f1e55c60d` | `L01`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d01:seat:L02` | `e468c740-266e-5eda-9ec6-132630016647` | `L02`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d01:seat:L03` | `2ffa8f15-70e7-54e3-8ed9-9cd3f974aeff` | `L03`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d01:seat:L04` | `b3366a83-20d4-511c-9eef-439b74bf67e8` | `L04`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d01:seat:L05` | `fd65b519-7f2a-5ba5-b09a-58de70b32f0c` | `L05`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d01:seat:L06` | `59af72d5-ece4-58ba-abd5-762627482677` | `L06`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d01:seat:L07` | `6842f3a7-ef06-51ec-8d4b-f77d6ceeefab` | `L07`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d01:seat:L08` | `3492532e-1630-5376-a2fb-cb82230d3685` | `L08`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d01:seat:L09` | `d246a354-318f-5c9f-8fdb-65bd78b788e4` | `L09`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d01:seat:L10` | `f94719fd-6baa-557a-9d9f-946edf3ad576` | `L10`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d01:seat:L11` | `edc635f0-064d-546c-9bf0-35204e32b901` | `L11`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d01:seat:L12` | `171b58bb-67d2-51d4-94d3-d4a785bd75e8` | `L12`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d01:seat:L13` | `cbf3d2eb-5bc1-53c5-92b6-4fe7c9b5d4c2` | `L13`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d01:seat:L14` | `372cf158-25fb-5efd-ad3c-e58d0feb403e` | `L14`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d01:seat:L15` | `079d7804-b874-5200-b65f-f55808f6d813` | `L15`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d01:seat:L16` | `dbe8f85d-9824-5b38-ab93-73557b17988e` | `L16`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d01:seat:L17` | `8556b094-67a5-56a0-b526-66a161b297c0` | `L17`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d01:seat:L18` | `9f5b2f06-4ca0-5f70-9895-c3f32c66ffa5` | `L18`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d01:seat:L19` | `a245ec58-517f-5e3f-83bd-951680008131` | `L19`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d01:seat:L20` | `147517eb-177e-5850-befb-20a9cb6cfc3c` | `L20`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d01:seat:U01` | `92df4050-e358-5751-a8d1-df9aa3fba878` | `U01`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d01:seat:U02` | `08733139-4a4d-5dfa-874c-770b84be331d` | `U02`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d01:seat:U03` | `8c9631a0-483b-5719-84c7-b5b53f71bec7` | `U03`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d01:seat:U04` | `f75abdc5-9d00-5b77-9ed5-a591ff0d3163` | `U04`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d01:seat:U05` | `c9fbe7b4-fb91-50df-beb2-ddb48f1e5f21` | `U05`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d01:seat:U06` | `ff804546-1a6a-521a-8bca-c006728921c9` | `U06`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d01:seat:U07` | `fab58c2f-48e8-55ec-a1d1-f6126bc0aff6` | `U07`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d01:seat:U08` | `bc950880-3612-5e7a-b0e6-1c9774859cc0` | `U08`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d01:seat:U09` | `f729b1f0-c49e-52df-92f5-e9c4e4a3bbf9` | `U09`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d01:seat:U10` | `3253913d-f751-5a1f-90e3-d34af6bbaa08` | `U10`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d01:seat:U11` | `5fadbcb4-b28c-5a2c-9aa2-653dc5157bd4` | `U11`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d01:seat:U12` | `76c5621a-639c-57e1-a20a-48251ec03433` | `U12`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d01:seat:U13` | `c5eab56d-c0ec-526f-9d8d-f8ddcaa90b0f` | `U13`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d01:seat:U14` | `0796b442-e049-5490-b337-b29d8ea60255` | `U14`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d01:seat:U15` | `2bcae4c0-a6c0-5618-8d2a-0fc8ed15ab7e` | `U15`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d01:seat:U16` | `13ad932d-132c-591d-a756-c081169e9f6c` | `U16`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d01:seat:U17` | `ef2f1b15-7681-510a-b1aa-05c2572b4806` | `U17`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d01:seat:U18` | `a33afb3b-5bb6-5806-b95e-a9e5fb2a4d01` | `U18`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d01:seat:U19` | `e96a6f48-82b7-5c9c-89c3-82d12ef4f157` | `U19`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d01:seat:U20` | `f056dc95-92da-5ecd-88ce-b6504e713fe4` | `U20`; SLEEPER_UPPER/AVAILABLE |
| Trip | `trip:trip:c:r3:d02` | `5674c91d-936e-5249-9d87-4858a65a67e3` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 02 |
| TripSeat | `trip:trip:c:r3:d02:seat:L01` | `05a634cd-5a90-555e-ae58-f8bb012934d7` | `L01`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d02:seat:L02` | `3ca4e0d0-74a8-500f-aba2-38e8d9caaea4` | `L02`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d02:seat:L03` | `c28ede48-5390-5172-bacd-cc389f8c05b9` | `L03`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d02:seat:L04` | `0cb6b414-c676-5fa4-ae2e-8a330c1dafe4` | `L04`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d02:seat:L05` | `0d09abf4-9fb0-5cc7-9a3b-cdaba33b2166` | `L05`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d02:seat:L06` | `ef616e8e-d62e-5399-8dd1-d9b083caa124` | `L06`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d02:seat:L07` | `62d32688-1270-5bb0-b335-b427eb4f243e` | `L07`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d02:seat:L08` | `55d60d41-2af0-52a5-b7c1-6901f51bc50f` | `L08`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d02:seat:L09` | `70ecf4ad-50b3-596c-be3f-e1e031750202` | `L09`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d02:seat:L10` | `2cf44938-dad6-5247-ba6a-50c2aac8af4c` | `L10`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d02:seat:L11` | `63ab541e-37ae-5af3-96b6-58b395a3b205` | `L11`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d02:seat:L12` | `6a950520-daa9-5980-b0ce-9cd319bc4bdb` | `L12`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d02:seat:L13` | `4ffcf6bd-2e37-54fe-b7e9-2235bf44536f` | `L13`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d02:seat:L14` | `35105ce2-e83e-5d99-b7ca-3328dcbb293b` | `L14`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d02:seat:L15` | `46ff4b5b-75bd-501c-86a4-99907b0a68a7` | `L15`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d02:seat:L16` | `b8758592-ac90-5672-9c5f-8474d8a30190` | `L16`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d02:seat:L17` | `ed372ea5-da4b-52b2-b0cc-a5760c1d0cee` | `L17`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d02:seat:L18` | `1006466f-a375-5c0a-a9ba-72aae2a1f554` | `L18`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d02:seat:L19` | `4bbcd7bd-6b94-55b1-8ef3-45f712c20fae` | `L19`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d02:seat:L20` | `07e41228-f772-58e9-99fd-4aa81177ad25` | `L20`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d02:seat:U01` | `0b3ba03c-66fc-566e-841d-0263b4967d49` | `U01`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d02:seat:U02` | `79cf8c3b-8372-52bc-b1c1-27faf8f96f74` | `U02`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d02:seat:U03` | `547fc7e5-d8d1-5ad8-add4-d8718df9e705` | `U03`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d02:seat:U04` | `56ac37ec-21d9-5a2e-aa94-de4b24a719a4` | `U04`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d02:seat:U05` | `32e40396-24cf-5f5f-b3ae-bb392a41a249` | `U05`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d02:seat:U06` | `70dc54d8-bc6c-5af5-9117-e0bd7c0dbcc2` | `U06`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d02:seat:U07` | `51e3259f-0f51-51b1-9c05-14c64944365b` | `U07`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d02:seat:U08` | `4b118475-a4a6-519c-ab0e-c0b5f4f01dda` | `U08`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d02:seat:U09` | `792cff41-42c5-59f5-8962-634ad6ffff9f` | `U09`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d02:seat:U10` | `d773df3c-c98e-5793-89d1-58312260617c` | `U10`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d02:seat:U11` | `1ca1631d-f469-5f74-8a5c-2d67efb18c07` | `U11`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d02:seat:U12` | `b5513fb4-da87-5f62-b2e5-85d731e2bfb7` | `U12`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d02:seat:U13` | `be1583d9-ee00-5977-924c-1506b929602e` | `U13`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d02:seat:U14` | `b1afce53-5758-56d2-9f93-daf5a1509ea6` | `U14`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d02:seat:U15` | `3970fcda-b6fd-5ba2-aacc-efd04628cedd` | `U15`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d02:seat:U16` | `8e3625b8-5c3d-5c7e-811c-b8b832952f0a` | `U16`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d02:seat:U17` | `c76ccfb9-da8d-5a3d-9f9d-45a88a7bc531` | `U17`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d02:seat:U18` | `8dc3a54b-fc76-55f5-95e5-d46a1e347582` | `U18`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d02:seat:U19` | `cb09954e-f5b7-57e7-a303-39a393b4f095` | `U19`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d02:seat:U20` | `cd2a3e76-7e0a-5ea0-98a6-3ecd94a72ca4` | `U20`; SLEEPER_UPPER/AVAILABLE |
| Trip | `trip:trip:c:r3:d03` | `7ecb6c7f-afca-5a9b-aa78-09f1717df5f6` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 03 |
| TripSeat | `trip:trip:c:r3:d03:seat:L01` | `8d97d558-11d9-58ef-891a-bd63e6990f65` | `L01`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d03:seat:L02` | `a10b5d6d-903d-505d-83b4-57d96495a695` | `L02`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d03:seat:L03` | `4db10fad-a7c6-5d3a-82a5-e15c3c9fc4f8` | `L03`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d03:seat:L04` | `f61939ca-85bb-5fce-a174-3d722a272a25` | `L04`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d03:seat:L05` | `faac6250-a50c-5f03-b240-0a139ad08e46` | `L05`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d03:seat:L06` | `2ca0cc52-dd53-5ab3-a2c8-7e090f870369` | `L06`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d03:seat:L07` | `5d6c7245-84fb-5311-b8cf-215c639b0551` | `L07`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d03:seat:L08` | `b08937c3-ee4c-53e5-9310-f161b93dca0f` | `L08`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d03:seat:L09` | `5c948dbf-9675-57d1-910c-d74fd1d0e717` | `L09`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d03:seat:L10` | `bb23f012-b0d4-54aa-aecb-1f43bb6d1069` | `L10`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d03:seat:L11` | `672d18f7-02f8-59b1-abfc-f0b4c22cd9f4` | `L11`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d03:seat:L12` | `3a5563be-118b-5742-a807-dddac1f6e16a` | `L12`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d03:seat:L13` | `6435d633-632e-52e7-ae22-41b67868e2eb` | `L13`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d03:seat:L14` | `c163d2a3-5179-53b3-a1b6-9e63377f0159` | `L14`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d03:seat:L15` | `b283da45-05cb-51ef-ad04-7b7398c776ca` | `L15`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d03:seat:L16` | `d3da02d9-d773-5b62-bda4-e02bb0db3c1e` | `L16`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d03:seat:L17` | `af352325-67ba-5e35-af99-4ab48650cdd0` | `L17`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d03:seat:L18` | `c5c60fce-6da5-5911-ab7e-b4653f9df799` | `L18`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d03:seat:L19` | `3a1b09c7-acf3-5fe3-9f94-99a90474385d` | `L19`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d03:seat:L20` | `59cd766b-94b6-5824-9ed3-d15e1dd25fe8` | `L20`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d03:seat:U01` | `cdf8e233-621e-5089-8ce0-c07bafb2eebb` | `U01`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d03:seat:U02` | `dc7b7d29-f3df-59d2-9c45-98f77c17fd8c` | `U02`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d03:seat:U03` | `7e956821-8873-53d5-825b-afee995ff2f5` | `U03`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d03:seat:U04` | `54ef844a-26a9-5ef9-aac9-88e7abed703a` | `U04`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d03:seat:U05` | `22dd5c34-d000-5834-aa0c-a11dfb8f1037` | `U05`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d03:seat:U06` | `7916d4d0-67a1-5041-8784-d0421f332c4e` | `U06`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d03:seat:U07` | `0cae3cec-c8d2-5ae1-97e5-d04209d29b6a` | `U07`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d03:seat:U08` | `3fb597c6-b693-5659-8805-f39fd2484ff7` | `U08`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d03:seat:U09` | `f4e38a5b-29f3-549c-96f9-db4c02dc409f` | `U09`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d03:seat:U10` | `d1cd5e74-4689-59c9-84a6-3cd47abbbec4` | `U10`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d03:seat:U11` | `212f818f-3a44-57ce-b802-84a73221b1aa` | `U11`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d03:seat:U12` | `5e659887-e68f-50d7-85f9-e3d33a4013eb` | `U12`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d03:seat:U13` | `b16984f6-5f92-5215-8bae-6b69a0fafc96` | `U13`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d03:seat:U14` | `9a5cda0a-5860-5b60-a067-e71dd11c8256` | `U14`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d03:seat:U15` | `63786af1-8c85-572d-a5c6-d41be097afc0` | `U15`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d03:seat:U16` | `fcf02c5b-da85-5c6b-9aa5-56df5858b637` | `U16`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d03:seat:U17` | `cdb20dcd-5e9f-5328-9964-1d71d9022355` | `U17`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d03:seat:U18` | `e4548cb1-fde5-5411-a815-ebf8f63fec64` | `U18`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d03:seat:U19` | `3f29ce0d-cf8d-51d2-b376-d7831a85ef21` | `U19`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d03:seat:U20` | `e3390b8b-979d-5708-be43-1dd11ef29434` | `U20`; SLEEPER_UPPER/AVAILABLE |
| Trip | `trip:trip:c:r3:d04` | `5781fd5f-8cb0-5ded-926a-4d7bcaf59b5f` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 04 |
| TripSeat | `trip:trip:c:r3:d04:seat:L01` | `138781fa-1322-5947-8053-f6bb1c2244ce` | `L01`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d04:seat:L02` | `f02272b9-3cb7-5330-a728-f069175451fd` | `L02`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d04:seat:L03` | `95e3905a-42ec-59ee-9d3b-9da8149f3c1a` | `L03`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d04:seat:L04` | `442b81fd-8866-5ad4-a463-553a25dbac48` | `L04`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d04:seat:L05` | `343cba0a-3eac-50d7-978e-79e487e99e54` | `L05`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d04:seat:L06` | `23e35be5-75f1-52a0-b086-01efbac400a1` | `L06`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d04:seat:L07` | `cddaec3f-cf2c-55f1-a345-42fc3b6037ea` | `L07`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d04:seat:L08` | `84bc609a-a23a-5592-8a8d-0391f368e2d2` | `L08`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d04:seat:L09` | `6de02609-5abb-5177-a14f-466bc69236cc` | `L09`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d04:seat:L10` | `86a273d3-9800-56e3-b023-bee8feaee21c` | `L10`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d04:seat:L11` | `d014eb9b-b726-52e4-be72-91614987494c` | `L11`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d04:seat:L12` | `58c356e0-9b86-5220-b8de-bd5f4e654676` | `L12`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d04:seat:L13` | `986e1495-d6b3-5951-81cd-24ed4d8226e9` | `L13`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d04:seat:L14` | `2be8893a-cb5f-51b5-94ae-fc6fe9fd9e39` | `L14`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d04:seat:L15` | `8661b784-678a-56eb-b08d-2a25ce27c6fb` | `L15`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d04:seat:L16` | `27bf217c-4232-53a9-b9d0-409eba5836e6` | `L16`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d04:seat:L17` | `e2d790d4-ca9e-5ced-9fd7-9e52cab4f868` | `L17`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d04:seat:L18` | `45a8c9c2-defc-5c81-8485-a710096553b7` | `L18`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d04:seat:L19` | `76d3d964-6d98-5aa1-917d-74da497a07dc` | `L19`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d04:seat:L20` | `f39995ae-6349-5c84-8b6c-3c03192b917c` | `L20`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d04:seat:U01` | `d4c7776f-5f07-573f-bc83-23b9b658a4bf` | `U01`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d04:seat:U02` | `6d18faee-8a54-5857-85c7-0094fa059ac0` | `U02`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d04:seat:U03` | `8fdc7e61-a94d-50aa-97ad-bfef05ab0c56` | `U03`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d04:seat:U04` | `d5803cc2-1cf8-594a-9d8d-a9e63f3dca73` | `U04`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d04:seat:U05` | `8ab0c722-971e-55db-a56b-a2b49cc449f4` | `U05`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d04:seat:U06` | `e3e5a7f0-ea8b-502a-8bdf-9492f9fe413e` | `U06`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d04:seat:U07` | `f69476d1-ff7c-5cd7-a379-6f64b78b8bfa` | `U07`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d04:seat:U08` | `31fb1bbc-ff45-50de-80d1-412775949dc0` | `U08`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d04:seat:U09` | `8355dc8a-abcd-500d-8662-ef15a2909423` | `U09`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d04:seat:U10` | `4cf5fe54-b256-5e74-9b06-dff5b50a3595` | `U10`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d04:seat:U11` | `62c604a8-aa78-5b0a-8618-d1c9c8aeb53d` | `U11`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d04:seat:U12` | `68c8ac95-d649-5cd3-8447-6370592431a0` | `U12`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d04:seat:U13` | `7f26791e-d136-569a-94cd-6ce2d452bb6e` | `U13`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d04:seat:U14` | `5822fa40-8ec2-5f51-8566-871d25f46bb7` | `U14`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d04:seat:U15` | `6b92e7b1-f4c3-566b-9077-9c0465285a37` | `U15`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d04:seat:U16` | `da470280-ddea-5752-960f-7f29c7566a33` | `U16`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d04:seat:U17` | `a372b475-c54f-59b2-b855-f7f34471ec07` | `U17`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d04:seat:U18` | `ba784e25-0060-5b7c-b47c-4ada5b5f6227` | `U18`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d04:seat:U19` | `43d4aba5-0c44-579c-b4a5-178247db5578` | `U19`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d04:seat:U20` | `d79061b1-9fb3-5e43-822e-404c992dc368` | `U20`; SLEEPER_UPPER/AVAILABLE |
| Trip | `trip:trip:c:r3:d05` | `ba1ee855-c42a-5787-a2ba-53b523f82ad5` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 05 |
| TripSeat | `trip:trip:c:r3:d05:seat:L01` | `9e1734e0-a1c4-522d-87e8-b7ad268fba31` | `L01`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d05:seat:L02` | `b0ddd916-27b2-53f5-b525-3e34c023b18e` | `L02`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d05:seat:L03` | `722f73aa-baae-539e-9e2c-7182c403a063` | `L03`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d05:seat:L04` | `3ae3b3bf-71cd-55a1-b28b-ded4cf57d1b8` | `L04`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d05:seat:L05` | `6c8f7e23-6735-5710-8128-a226ff008918` | `L05`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d05:seat:L06` | `423cdf9e-a138-543e-996c-7678ad826e03` | `L06`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d05:seat:L07` | `b49584a9-648d-57e2-8917-b2414b51f7e1` | `L07`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d05:seat:L08` | `84bda987-39b3-56c3-8af3-cadae67ff50a` | `L08`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d05:seat:L09` | `6041a9c4-0b77-5a23-9e8e-2339b1e8835f` | `L09`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d05:seat:L10` | `58e620eb-47ee-5123-89ae-1141e408b28e` | `L10`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d05:seat:L11` | `417ec41c-a038-5de5-823f-24963da3fcf1` | `L11`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d05:seat:L12` | `383dfd4a-2d42-5b02-a9b0-84210546baa2` | `L12`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d05:seat:L13` | `a0eb7293-e974-501e-8857-e0356ba1868b` | `L13`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d05:seat:L14` | `182cfd06-a2f4-5c93-99c2-5c8b467d6376` | `L14`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d05:seat:L15` | `e17e88b1-725b-5102-bc89-de9f1e5bb3bb` | `L15`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d05:seat:L16` | `a8c09f23-1a45-5843-b4d1-1cfd7499df2c` | `L16`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d05:seat:L17` | `38ebae6a-5c5d-511c-b8c7-9f3ae3f8fc06` | `L17`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d05:seat:L18` | `9ed3eb55-ee51-598a-85ce-e122111f172b` | `L18`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d05:seat:L19` | `c6a0c37c-fef0-555e-8020-1e7d335ac7eb` | `L19`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d05:seat:L20` | `de2e8e48-b499-5555-af47-7397d2ab4444` | `L20`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d05:seat:U01` | `6265f15c-3caf-5ecc-a413-0783cc41d4fe` | `U01`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d05:seat:U02` | `0d322bc4-120e-5fe8-9d64-83c5d02041c5` | `U02`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d05:seat:U03` | `294c45d1-c070-54ec-8d16-655fe16f7e81` | `U03`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d05:seat:U04` | `1a3dc8e4-49c0-5418-b426-02ac890245e2` | `U04`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d05:seat:U05` | `a5f554ff-45d7-59dc-9665-6df37a9b9d14` | `U05`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d05:seat:U06` | `e0bc45c4-223f-5088-a0a9-24f6bc429770` | `U06`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d05:seat:U07` | `3980b88f-7f70-51a2-aab2-8ccb6fb33a52` | `U07`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d05:seat:U08` | `3711131d-d55d-5dbf-93bc-244bfd20f516` | `U08`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d05:seat:U09` | `fda4dda8-a60e-5850-8178-bc4e5d7a9441` | `U09`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d05:seat:U10` | `d3f48a31-d020-5fe1-aefc-32b238fda2ff` | `U10`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d05:seat:U11` | `010c92b8-8733-5e8b-b287-ac35ca7468ad` | `U11`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d05:seat:U12` | `74777980-9a46-5ff2-b649-3d92eb2d017e` | `U12`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d05:seat:U13` | `acf87577-839e-572c-b642-59cd1df9360b` | `U13`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d05:seat:U14` | `44607060-6302-5b0c-ae8b-f77929f8ed11` | `U14`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d05:seat:U15` | `ea58ea18-423f-5080-b2b8-609624fac279` | `U15`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d05:seat:U16` | `83265831-e343-5a79-a973-d71c4fe0e472` | `U16`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d05:seat:U17` | `2dcbb394-73f8-5f2e-ac76-7f345291bfed` | `U17`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d05:seat:U18` | `e5363348-d4ec-5ef4-aaf1-86fa6658b545` | `U18`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d05:seat:U19` | `b0e38c55-2ac7-5ae4-966c-bcd15ba0c40c` | `U19`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d05:seat:U20` | `a89877d4-4f74-5b8d-ad2d-d230708e6758` | `U20`; SLEEPER_UPPER/AVAILABLE |
| Trip | `trip:trip:c:r3:d06` | `0207573b-05a6-5557-9ebe-f252e615fbee` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 06 |
| TripSeat | `trip:trip:c:r3:d06:seat:L01` | `5eb20f1f-428f-5315-b49c-11c8d2f1bd25` | `L01`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d06:seat:L02` | `a79e767b-d80a-50dd-8102-20b759d9ab80` | `L02`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d06:seat:L03` | `3e44f3ab-2a1e-5f42-b845-c5349e1d5e7e` | `L03`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d06:seat:L04` | `50267bd7-b3f6-54e5-8449-7b6f3919e490` | `L04`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d06:seat:L05` | `53e74d28-8c82-57ed-852b-26e9a7af51a1` | `L05`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d06:seat:L06` | `da3e8953-b4e9-51f8-ac90-53dcff3e02c7` | `L06`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d06:seat:L07` | `d0dbd9d4-0de5-5e33-84e4-1f7ef08aae54` | `L07`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d06:seat:L08` | `9f0951f1-4e57-59c4-a958-6a13956a2006` | `L08`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d06:seat:L09` | `9326e853-4d9a-50ac-b19d-866c3f13998d` | `L09`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d06:seat:L10` | `8ee07812-7b15-52e4-9068-e051622c1816` | `L10`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d06:seat:L11` | `bd5dbd3f-15da-5ab4-8fad-650949a12f01` | `L11`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d06:seat:L12` | `691a8324-f32b-5693-9ee0-1eba432dbb62` | `L12`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d06:seat:L13` | `2572e98c-5b02-56b8-9eff-1f54698cf340` | `L13`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d06:seat:L14` | `c7b1beac-6f75-5d24-ade2-11eb515c0b1b` | `L14`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d06:seat:L15` | `3f0f7dfb-d48c-55e0-9580-b1b37cae08de` | `L15`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d06:seat:L16` | `8996a2db-729a-5240-8a6e-85bfda867998` | `L16`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d06:seat:L17` | `2355a2eb-cc86-58c7-a8fa-a701e0c30255` | `L17`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d06:seat:L18` | `a192ba08-8873-5fc8-8d17-3624a5103462` | `L18`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d06:seat:L19` | `6f9c8cc3-8a5b-510b-8dad-e90cd21b4794` | `L19`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d06:seat:L20` | `6b29db19-6c26-5e32-ba2c-bc50bc4dd87b` | `L20`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d06:seat:U01` | `918d48c9-428d-5e2d-b324-aecb4e8e44fc` | `U01`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d06:seat:U02` | `65e286f1-45c3-584a-abc0-f20cadb10fc3` | `U02`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d06:seat:U03` | `71f83d61-9db3-501a-a537-4b74d0658b4d` | `U03`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d06:seat:U04` | `2dcdefac-db7a-5976-9a1e-769fb0cc3f9d` | `U04`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d06:seat:U05` | `2a8d6c02-034e-5780-a002-9f8c0c33aceb` | `U05`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d06:seat:U06` | `9f62b15f-b652-5111-9ca5-a881fad9fe2b` | `U06`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d06:seat:U07` | `1b66863b-e48a-5944-b257-9e6d873b36ae` | `U07`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d06:seat:U08` | `2b035c11-f671-5e15-b12a-2d3afad59ca7` | `U08`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d06:seat:U09` | `0458b15e-7130-5b7f-8c21-15d94abd87d9` | `U09`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d06:seat:U10` | `b48e4ab1-f210-55ea-9b63-136ccdf128a5` | `U10`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d06:seat:U11` | `addac2a7-70a3-519d-bc6f-15db4cee1622` | `U11`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d06:seat:U12` | `08ca2927-4928-58e1-82f4-70d6d1eb2dc0` | `U12`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d06:seat:U13` | `7b208aa3-2b6c-5f52-bcbe-d70f7adae369` | `U13`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d06:seat:U14` | `d69d95a4-83b1-51e9-b99a-2672bdbfbaed` | `U14`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d06:seat:U15` | `81ff9500-e214-53ea-8210-f6e25b3bdbbf` | `U15`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d06:seat:U16` | `a7837308-792d-5335-b52e-dd4833a50b8e` | `U16`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d06:seat:U17` | `ff2a5395-be33-580b-a6f2-b1e154771ae2` | `U17`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d06:seat:U18` | `224bd06d-7223-5d80-9130-2a0a484e6dc3` | `U18`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d06:seat:U19` | `8bee4644-ac48-5130-9a1e-30c4f07296fe` | `U19`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d06:seat:U20` | `f7dce3cc-d171-5fa0-b0a6-e74556ed7940` | `U20`; SLEEPER_UPPER/AVAILABLE |
| Trip | `trip:trip:c:r3:d07` | `f77e5a64-116f-592f-8d10-9642fcd579f2` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 07 |
| TripSeat | `trip:trip:c:r3:d07:seat:L01` | `54108e8f-c033-5a8d-9665-dca722b3868c` | `L01`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d07:seat:L02` | `f322f099-9bce-5250-a986-a6f39467fafb` | `L02`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d07:seat:L03` | `f211050b-be01-5900-a980-136b363d842e` | `L03`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d07:seat:L04` | `82a9c621-a61d-5d86-a9c1-17bf0d50087d` | `L04`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d07:seat:L05` | `b1cca5a5-4094-54c7-b910-91e192a57547` | `L05`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d07:seat:L06` | `d8de1827-535a-5457-985e-529ee7177d57` | `L06`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d07:seat:L07` | `1c3f2788-ad35-518b-a839-fb591699f54a` | `L07`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d07:seat:L08` | `fb55513f-c150-5dc3-845d-60b2a66fcd49` | `L08`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d07:seat:L09` | `ac0bc4e4-e3db-554a-ac94-917c36767df4` | `L09`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d07:seat:L10` | `5789f113-7d9f-558d-af74-7659c46aaf23` | `L10`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d07:seat:L11` | `32897314-9c53-5dce-b5c5-772ef2d12d03` | `L11`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d07:seat:L12` | `960c2a0d-5315-54b2-a618-01ecd323d97a` | `L12`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d07:seat:L13` | `8f38a8d3-bc81-567a-ae7e-f8c390d52c0d` | `L13`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d07:seat:L14` | `718f2581-0a2f-5f8f-bca3-af6b9d9e2067` | `L14`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d07:seat:L15` | `a867fa72-8c20-5526-bbe7-88bb30719b81` | `L15`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d07:seat:L16` | `febddaef-0362-5109-a35d-cead6cd84635` | `L16`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d07:seat:L17` | `7bc0d9f1-e325-5015-8bc7-01d4c1533cdd` | `L17`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d07:seat:L18` | `58d1d28d-e5e9-5fa6-8d99-66dbfeffa563` | `L18`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d07:seat:L19` | `d3b50acb-c381-51d1-958e-835b92a67c0a` | `L19`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d07:seat:L20` | `af61dc4f-bb77-5bd5-8ba2-98b96d4f9c1b` | `L20`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d07:seat:U01` | `f80b3337-fa89-5150-846b-5c34d597dcd2` | `U01`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d07:seat:U02` | `856f8845-cf43-5ed9-816d-795de9dea88c` | `U02`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d07:seat:U03` | `df65bef9-2fc5-502d-a0af-1ba140c1722b` | `U03`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d07:seat:U04` | `1ce14b9b-2418-585e-8334-ed435b97d36a` | `U04`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d07:seat:U05` | `a35dca2a-4a62-5797-88a7-768fd40baab8` | `U05`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d07:seat:U06` | `a3ade1b1-53c7-595f-a561-c43170b5efbc` | `U06`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d07:seat:U07` | `68364848-9d0e-585e-bc6a-30ac043fd9bf` | `U07`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d07:seat:U08` | `81e30fdc-de81-54f0-b91d-3ce1e7c41684` | `U08`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d07:seat:U09` | `2d7a0f3c-c7f4-567c-8f48-8b33d4351d57` | `U09`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d07:seat:U10` | `001567af-4ed7-5dc7-93e4-39f2670391f9` | `U10`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d07:seat:U11` | `28da22b3-7d65-5b8c-a7d9-2efa4bc64162` | `U11`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d07:seat:U12` | `bace38a1-acdf-51d5-a153-788448f990fc` | `U12`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d07:seat:U13` | `4f3b93df-8787-5d01-8ab9-1f6a882eb043` | `U13`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d07:seat:U14` | `cab5e056-2ed5-5bca-a3e6-c6edd081a112` | `U14`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d07:seat:U15` | `d2c7996d-1a38-5fca-81a1-722a9ce8c716` | `U15`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d07:seat:U16` | `4122e17a-e899-5dbf-a324-2c235148d1e3` | `U16`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d07:seat:U17` | `c155d74d-6793-58a5-b091-9224b36d6973` | `U17`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d07:seat:U18` | `eab2f91b-24d7-5a54-a6ee-e095478be1d8` | `U18`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d07:seat:U19` | `00ed11d5-38e8-5bae-bca7-e6adfc0057f7` | `U19`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d07:seat:U20` | `843f2e8d-14ad-5948-abfc-d20ac6f55607` | `U20`; SLEEPER_UPPER/AVAILABLE |
| Trip | `trip:trip:c:r3:d08` | `07da5520-13d2-5df8-9983-3a1cd927eaed` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 08 |
| TripSeat | `trip:trip:c:r3:d08:seat:L01` | `e84a93c4-29e0-5493-a32d-7b08b0a7e5cf` | `L01`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d08:seat:L02` | `f36ea5da-5884-5645-9d2a-9692a293b152` | `L02`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d08:seat:L03` | `4d18c2d5-71f0-5c0a-82e9-67d5b1d19d92` | `L03`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d08:seat:L04` | `13df84ba-fb6a-5ea0-8f80-4701c1f28ee5` | `L04`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d08:seat:L05` | `54d045a6-8601-5782-b43c-1972f554f10b` | `L05`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d08:seat:L06` | `07ca88e0-5b94-59a0-9238-f82f5d4c3840` | `L06`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d08:seat:L07` | `51269862-7012-5037-a203-9f9cecaba807` | `L07`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d08:seat:L08` | `6bb14927-59b8-5a6d-a98e-2e811ae2fb36` | `L08`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d08:seat:L09` | `565b0bdc-e055-5de4-a00c-bf7370f15381` | `L09`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d08:seat:L10` | `fb238341-bcf4-550e-a3e1-4d32df2b4054` | `L10`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d08:seat:L11` | `0880eb34-d57e-5a31-a93f-5c44721f8bf4` | `L11`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d08:seat:L12` | `6a302c63-6fe6-5da7-a8e4-6ed9cd603ab6` | `L12`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d08:seat:L13` | `04838092-f7ec-505c-ba55-c280940090de` | `L13`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d08:seat:L14` | `c1ba3f75-3abf-595a-aff2-5ba4a42b0bff` | `L14`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d08:seat:L15` | `5a26e481-44d9-54e4-bf96-8ec2776e5062` | `L15`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d08:seat:L16` | `f67602df-6ee0-550e-9d89-a5b1fd43b91a` | `L16`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d08:seat:L17` | `563471ff-aaf1-5815-8556-4b5e5a015450` | `L17`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d08:seat:L18` | `2b3bcd3d-1975-556e-9136-f81b3617ab7e` | `L18`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d08:seat:L19` | `3b0a8569-4b30-5b9e-a2f1-b330f45ede2f` | `L19`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d08:seat:L20` | `847eab67-006f-5434-84da-4a45062552c2` | `L20`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d08:seat:U01` | `5dfd8254-fb33-56d4-863f-90a9562264a1` | `U01`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d08:seat:U02` | `8334ddaf-de0e-5260-aefd-57d5355b7f50` | `U02`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d08:seat:U03` | `d1638af0-f5c9-5695-8ec6-83a67490ce76` | `U03`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d08:seat:U04` | `1effbc75-fcf8-55a0-bdbd-0e9a681529b1` | `U04`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d08:seat:U05` | `94e832b3-7eeb-5809-aa49-1244ede39ba7` | `U05`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d08:seat:U06` | `204a2914-a56f-5c74-893c-f7a280d2d08e` | `U06`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d08:seat:U07` | `05c9f8f1-0e29-5d22-ae98-bb56e1da2e7b` | `U07`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d08:seat:U08` | `b0fdab7a-b8ec-5762-ab2d-bf97bb202f46` | `U08`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d08:seat:U09` | `a650a207-d8ae-5229-8dc3-254af2501a27` | `U09`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d08:seat:U10` | `a1364d8d-933a-5c38-9754-cadc6f1c24ca` | `U10`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d08:seat:U11` | `e1e5cc9e-c7da-53e8-8b7a-109f4ea03e3f` | `U11`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d08:seat:U12` | `f5758325-9e85-5f54-9c8a-c1636983d811` | `U12`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d08:seat:U13` | `e83d40f4-06ca-5a74-af0c-713133b8a301` | `U13`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d08:seat:U14` | `6efcc377-ed70-5149-8bf8-794eb7b0226d` | `U14`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d08:seat:U15` | `87d6fbeb-97e1-5364-9ada-462ee2815614` | `U15`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d08:seat:U16` | `0ffff005-c3d3-55b3-b162-1b47c55dab3d` | `U16`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d08:seat:U17` | `fc7d69c5-3a2c-5c6b-a97e-c4b324918a22` | `U17`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d08:seat:U18` | `a0eb63ee-2c0c-5acd-880d-8051abf957a1` | `U18`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d08:seat:U19` | `0e58db15-25a8-5d9e-9a25-d4530424b981` | `U19`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d08:seat:U20` | `75f515b7-e1a3-50e4-abed-921c1b2a937c` | `U20`; SLEEPER_UPPER/AVAILABLE |
| Trip | `trip:trip:c:r3:d09` | `bfc420aa-99b6-58a7-a9e8-3d32debce516` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 09 |
| TripSeat | `trip:trip:c:r3:d09:seat:L01` | `96c4211e-5864-5751-93f3-4072dd34aa5d` | `L01`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d09:seat:L02` | `f186a034-bd92-5b41-8f03-eb853254ca0b` | `L02`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d09:seat:L03` | `a37f478c-33d2-59cc-9d55-791f2fc93773` | `L03`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d09:seat:L04` | `475c24fe-2952-50d9-9293-2a5ed1f6cbb3` | `L04`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d09:seat:L05` | `e5798778-4b59-52de-bd7c-ca2046ca95ed` | `L05`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d09:seat:L06` | `6d38641f-43b7-513f-964e-21359afb5dde` | `L06`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d09:seat:L07` | `2512fb92-10c8-5e16-859f-f283cc11ffb4` | `L07`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d09:seat:L08` | `eabed80e-88e1-5c79-b9b1-a02f419f2a54` | `L08`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d09:seat:L09` | `74a5e821-f122-517b-a0cc-be0714abe7b7` | `L09`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d09:seat:L10` | `9f294ebf-d784-5368-bcb5-44fc628b2252` | `L10`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d09:seat:L11` | `d4b9be66-add8-5dda-9841-8b4745cd00cf` | `L11`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d09:seat:L12` | `a6afdbb7-dc71-5cac-a4ae-57c25530d0cf` | `L12`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d09:seat:L13` | `48db39b0-68e8-59c7-a4a0-1048d93f39dc` | `L13`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d09:seat:L14` | `8cbd9b99-c316-59cf-a9e0-41196e54077b` | `L14`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d09:seat:L15` | `133892a9-0208-56fb-8783-d006e504c11a` | `L15`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d09:seat:L16` | `dcef53d6-c8ba-5e2d-a339-17697da86222` | `L16`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d09:seat:L17` | `e10950c6-e4de-5bb8-92cf-8cfa3a05bd2c` | `L17`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d09:seat:L18` | `e74304c7-8c0f-5262-9fcd-6e4c9eb7ae50` | `L18`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d09:seat:L19` | `79b06b6d-0593-5650-8f37-db556a198fce` | `L19`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d09:seat:L20` | `d425a1f2-a3a1-5e55-9318-44bd03e25a11` | `L20`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d09:seat:U01` | `af3241b1-40af-5b68-99b5-b234a5c39780` | `U01`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d09:seat:U02` | `ed5e7b1a-e2ff-5dc2-a28d-6035f773db87` | `U02`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d09:seat:U03` | `695740e8-a652-595f-9e4c-1ed1bb64c5af` | `U03`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d09:seat:U04` | `b84e9816-a9b6-57fb-b2af-bdd6ae039456` | `U04`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d09:seat:U05` | `382ff82c-b5ef-5e67-8710-3b5ef9f6f182` | `U05`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d09:seat:U06` | `00a557e7-6966-55d1-8fac-f23aab4bb34e` | `U06`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d09:seat:U07` | `f849a41c-f8e1-5784-ac05-1a486a4e7c06` | `U07`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d09:seat:U08` | `45600550-3ff2-55ee-b033-0ebc1d7436f8` | `U08`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d09:seat:U09` | `58d34e4a-fdb6-5b35-b40a-2d3fb429156b` | `U09`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d09:seat:U10` | `d6d7b778-f758-5993-b60c-73bf75389d92` | `U10`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d09:seat:U11` | `95c90ce6-c382-547f-9479-e2c3fc6bf1b4` | `U11`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d09:seat:U12` | `572315ac-1f84-5f23-b8c1-da3b8bbc9ce1` | `U12`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d09:seat:U13` | `84ad2c80-5cd5-533a-98f3-fceec8c71824` | `U13`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d09:seat:U14` | `749959d0-5bb2-5787-a5d4-666f90ae652e` | `U14`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d09:seat:U15` | `4b6ebae0-2010-5176-8a3b-447f68ed47e2` | `U15`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d09:seat:U16` | `b3b7ef95-87d8-5dfd-8cdc-4f050213a26f` | `U16`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d09:seat:U17` | `b9fda5e1-ec06-5c1f-a9c0-55cb6ece84c8` | `U17`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d09:seat:U18` | `992e9861-8fef-5626-87c4-208f8a0e3b76` | `U18`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d09:seat:U19` | `d205b9ed-8396-57de-9f03-59281506776b` | `U19`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d09:seat:U20` | `a333801a-381d-59b4-9904-102497cc5159` | `U20`; SLEEPER_UPPER/AVAILABLE |
| Trip | `trip:trip:c:r3:d10` | `44b41afc-652a-5dce-ab17-3d05f5c55cb7` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 10 |
| TripSeat | `trip:trip:c:r3:d10:seat:L01` | `223cacf1-1b4e-5f57-888b-f88a60fdef18` | `L01`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d10:seat:L02` | `a7389f8f-8d32-5bd3-8835-d8881ef9ee20` | `L02`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d10:seat:L03` | `6e05e21d-d989-5d75-8248-c5017ba26605` | `L03`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d10:seat:L04` | `bb82ce29-275f-58e2-b075-5ab91978744e` | `L04`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d10:seat:L05` | `afd07b3e-62da-5d0c-b8fb-54321915c662` | `L05`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d10:seat:L06` | `796507c9-4246-507f-afda-717de7246b8d` | `L06`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d10:seat:L07` | `0ebd195a-a35c-5bf9-b448-702b6c239ee0` | `L07`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d10:seat:L08` | `0f90c91a-5555-5580-bc2e-41f290b56815` | `L08`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d10:seat:L09` | `0fe14f15-bb45-52a0-8a85-6738b606fc28` | `L09`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d10:seat:L10` | `881eda39-2d17-5b31-a9c9-3619fc610fa6` | `L10`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d10:seat:L11` | `705862ea-ff93-56a9-9aad-96569cea6601` | `L11`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d10:seat:L12` | `e8c40ce2-8439-5eba-a596-ae49b6b8069c` | `L12`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d10:seat:L13` | `18688b6e-e6f1-5652-b4b1-7f09f7c59070` | `L13`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d10:seat:L14` | `6e03fdfd-ea0f-5ea0-baab-83a29607b993` | `L14`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d10:seat:L15` | `81f6d9f6-139c-5851-86ac-187249816eec` | `L15`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d10:seat:L16` | `5aa9c72d-c54c-5edf-9125-b588e077639c` | `L16`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d10:seat:L17` | `92cb6534-a57e-517b-8183-bdd276814dd7` | `L17`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d10:seat:L18` | `a5b8660b-2766-5849-9a59-1bccf285b08b` | `L18`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d10:seat:L19` | `6ad76b1a-ccb8-529f-841f-bf0884e0dc8d` | `L19`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d10:seat:L20` | `f0db9e8b-3786-5d51-95cf-60854156b3a9` | `L20`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d10:seat:U01` | `30cdeeaf-06e8-5ae8-b4f4-0573ce881067` | `U01`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d10:seat:U02` | `a266ed80-8f1a-515d-bdc9-166d2ba2b158` | `U02`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d10:seat:U03` | `224aaca3-bb81-5464-8468-8792275d1d73` | `U03`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d10:seat:U04` | `1dabd126-f722-584c-b823-71c66a8b9af2` | `U04`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d10:seat:U05` | `0ceaaefc-2d6d-5478-9823-5bb9261fbe40` | `U05`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d10:seat:U06` | `6b996128-a95d-5df4-86ae-6bcb9cc0fc8b` | `U06`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d10:seat:U07` | `ad95171b-447c-5c6a-8cef-8e1868933c23` | `U07`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d10:seat:U08` | `d1f96dac-42a1-54a5-aa0b-88be76724a82` | `U08`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d10:seat:U09` | `d62cc92b-1f2e-565f-b7fc-837c21594768` | `U09`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d10:seat:U10` | `539eb3ef-6c3d-554b-88f4-2219562a44d0` | `U10`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d10:seat:U11` | `8304a3ed-0d03-5e2e-95f2-6bb79fdd3a89` | `U11`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d10:seat:U12` | `de5a7790-fb76-5bfa-a1b8-e36ddaf8f7bc` | `U12`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d10:seat:U13` | `44a31bad-b633-5114-903f-e7e54ee30036` | `U13`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d10:seat:U14` | `68a2d6a8-67d4-5e98-8694-107d53b689ee` | `U14`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d10:seat:U15` | `d18d3c8b-7d84-556b-938a-5563c50c0956` | `U15`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d10:seat:U16` | `e6307045-b3f9-5c1f-a088-fe0863c0d8ed` | `U16`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d10:seat:U17` | `e71e7fb5-e54d-587a-be38-ef16ca8515b7` | `U17`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d10:seat:U18` | `9c85dd76-ab20-53ae-94d5-3affc7d682db` | `U18`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d10:seat:U19` | `d9e2f9a9-6ae1-57b5-a0ae-c101645a99d7` | `U19`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d10:seat:U20` | `79576ecd-9b64-5105-ae6c-d4373fdae187` | `U20`; SLEEPER_UPPER/AVAILABLE |
| Trip | `trip:trip:c:r3:d11` | `9c8d306c-9b9e-5bbb-b9d6-a85722d9f0f1` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 11 |
| TripSeat | `trip:trip:c:r3:d11:seat:L01` | `e83b810a-b649-50b6-8d62-103b1a06dbb7` | `L01`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d11:seat:L02` | `1332ecf6-c856-515e-a167-49c0ccc98c4c` | `L02`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d11:seat:L03` | `6d4810b1-7d36-55e0-a464-00a03e9c395b` | `L03`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d11:seat:L04` | `1c88158f-18f2-5f57-b8da-6edec645aea2` | `L04`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d11:seat:L05` | `4ba025ad-cc6a-558f-9d14-b4f388fbb0e1` | `L05`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d11:seat:L06` | `1ae876b8-2484-5056-a76f-ded269e6e35b` | `L06`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d11:seat:L07` | `05d5043b-9236-5670-adca-0ca110397d24` | `L07`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d11:seat:L08` | `0fb3d98c-4e74-5fde-966e-db0c4493fbd2` | `L08`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d11:seat:L09` | `457f6f43-badc-5b7b-9f76-0cbac0d2d2de` | `L09`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d11:seat:L10` | `1483c6b1-ab31-577d-b060-41f57eaafb2a` | `L10`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d11:seat:L11` | `af7babe5-5999-50f7-998a-55fe7184041c` | `L11`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d11:seat:L12` | `aadac2e0-a081-5a06-8d78-4c155fb67525` | `L12`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d11:seat:L13` | `54534a7f-4d72-553a-a718-d1c433c0f3de` | `L13`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d11:seat:L14` | `9fbc52b5-df48-5d98-86b3-7ac9b21d81ac` | `L14`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d11:seat:L15` | `2f4eaab8-0cca-59b4-bf46-ad4ff107ce58` | `L15`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d11:seat:L16` | `0561d451-439c-5d6a-aa13-04998317eeed` | `L16`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d11:seat:L17` | `cb62d254-52d7-53d1-8867-3e3f70b8cbc5` | `L17`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d11:seat:L18` | `44209e63-e5f1-5879-8293-3e2b6a07d517` | `L18`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d11:seat:L19` | `c46366f9-d077-52ee-80d3-96a1fa938791` | `L19`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d11:seat:L20` | `b4902b37-4c2f-56dd-9f6d-da165851b1a4` | `L20`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d11:seat:U01` | `00516f81-0480-5fc7-b80c-911bb6e0b3c6` | `U01`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d11:seat:U02` | `73c69874-903e-5f6a-92cf-f2b720c29e83` | `U02`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d11:seat:U03` | `64a79b14-a3f3-5f5c-8ed4-31da9e94a218` | `U03`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d11:seat:U04` | `b720282f-2cdc-5c0e-b6df-bc8bc3739fbb` | `U04`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d11:seat:U05` | `24da9791-262b-55ba-8195-caf73c451437` | `U05`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d11:seat:U06` | `bd925ea4-accb-5d88-b314-9a48f670a81c` | `U06`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d11:seat:U07` | `8aac20f4-a969-5d9d-b601-7cbcf7c7d684` | `U07`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d11:seat:U08` | `195be8e0-53f9-5100-9351-b2ae7b31053f` | `U08`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d11:seat:U09` | `7b1418d5-5d3b-539a-88e8-68ac38a4014d` | `U09`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d11:seat:U10` | `cb529076-031a-5d4b-a6e5-80dc7380f095` | `U10`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d11:seat:U11` | `e8fcd484-a0c9-5e6a-a3eb-b515bccd2ff1` | `U11`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d11:seat:U12` | `6582b4db-fe1b-5b72-bd98-a284040b45f4` | `U12`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d11:seat:U13` | `c8cedd36-7ba4-5b4d-be26-ae8b0fd3aaf3` | `U13`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d11:seat:U14` | `757c08fa-7a42-5e9c-9865-f7935b4dd470` | `U14`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d11:seat:U15` | `ef43671c-80fb-5fd6-8da8-7f0c1628a11e` | `U15`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d11:seat:U16` | `1e54e24c-a21c-571c-bb29-c62c0283d213` | `U16`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d11:seat:U17` | `b0c2eade-ab3f-589e-9dc5-cc80107a9ff8` | `U17`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d11:seat:U18` | `81471751-7f74-56a7-9c48-e8e8a9b8f820` | `U18`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d11:seat:U19` | `05c77156-3414-5008-b950-eff63a35f6e6` | `U19`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d11:seat:U20` | `1f17dd38-d30e-56db-a56d-5f3a554566eb` | `U20`; SLEEPER_UPPER/AVAILABLE |
| Trip | `trip:trip:c:r3:d12` | `d22fe089-77ec-59f7-b70b-88aafb79cca6` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 12 |
| TripSeat | `trip:trip:c:r3:d12:seat:L01` | `96de22cf-4096-5998-851d-47ce23e47ae4` | `L01`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d12:seat:L02` | `d3dd9617-b939-594c-888e-2862f84075e1` | `L02`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d12:seat:L03` | `f1807926-2fa4-5406-9b19-fdc5c74725f6` | `L03`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d12:seat:L04` | `afd17192-0f14-5af8-9d16-8c160898c72a` | `L04`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d12:seat:L05` | `37d32265-cf64-54cd-880f-e539e5bcf6ca` | `L05`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d12:seat:L06` | `d882e1d5-39a2-5b60-a5ef-265a46d02e0f` | `L06`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d12:seat:L07` | `5b4afe37-4d71-56e3-8c68-d7decbf5515e` | `L07`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d12:seat:L08` | `3ce40dbe-50e8-5ad0-ad6c-5d7b6a8cca82` | `L08`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d12:seat:L09` | `2935d5d0-cca3-5a10-af80-bcf0279e7512` | `L09`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d12:seat:L10` | `a9614bd5-2df5-5697-8a47-1faebf91dee8` | `L10`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d12:seat:L11` | `4fc0a8c7-3db8-506e-8cc3-55163f5c3b1c` | `L11`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d12:seat:L12` | `1c6515c2-523e-5034-bf76-744a1dcf2d2b` | `L12`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d12:seat:L13` | `695d0565-e3b5-563d-ab7a-5ac721d2b824` | `L13`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d12:seat:L14` | `ad0d6b74-b39b-5060-9aff-99115caf3a2b` | `L14`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d12:seat:L15` | `0c99ebc7-700e-5884-bc5c-0600875af0e1` | `L15`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d12:seat:L16` | `4e66b7a0-1945-5a2c-8b3e-70798224efb6` | `L16`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d12:seat:L17` | `8ef161b3-6e6e-5076-97bc-1f8d0fbcd482` | `L17`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d12:seat:L18` | `93591e1d-f2d4-5ee8-b49f-9250184c520a` | `L18`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d12:seat:L19` | `1ffbc069-5790-53bd-9a0e-9c382da1ebb2` | `L19`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d12:seat:L20` | `45740ccb-88b2-55ae-ac0f-6fa321c16941` | `L20`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d12:seat:U01` | `64c042c9-adee-5047-81a0-e5fa9de41504` | `U01`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d12:seat:U02` | `d85c0534-bc41-56ef-80e3-d56c323115a7` | `U02`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d12:seat:U03` | `7172a89f-17a0-5b6e-9b46-df20b0a5a206` | `U03`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d12:seat:U04` | `ed77a36c-4560-5912-a59b-d112f6554aa5` | `U04`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d12:seat:U05` | `b97bcd25-7698-5a08-8a5a-59e8fcbf8e88` | `U05`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d12:seat:U06` | `b702bdb1-897a-5e4a-b3f5-f97fbe66efa2` | `U06`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d12:seat:U07` | `c5eccc01-6660-5006-ae1e-3c2a0bc4a83c` | `U07`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d12:seat:U08` | `dddb898d-446f-5ed2-b5c6-20061820a8c1` | `U08`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d12:seat:U09` | `71c51209-17d9-52c4-a55a-bb82ee34f223` | `U09`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d12:seat:U10` | `d6edf8b4-0276-5798-aa77-e1bf36dbd7a4` | `U10`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d12:seat:U11` | `d39f2349-06f7-55a3-bbb1-4cec5b453952` | `U11`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d12:seat:U12` | `16cffab5-9f1d-51d1-aef2-6ee506dd1836` | `U12`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d12:seat:U13` | `21eacd1d-75b2-5192-b381-53a9a107d83f` | `U13`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d12:seat:U14` | `66ff51ff-9ba1-55b3-b5e0-5245d437b5db` | `U14`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d12:seat:U15` | `f9c3c0b4-d662-57cd-a9ab-31a4feb85bd1` | `U15`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d12:seat:U16` | `88795d12-93e2-5ad5-b81c-a55f82f068df` | `U16`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d12:seat:U17` | `e193ca8d-c6d3-5c76-af3e-deb36560c989` | `U17`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d12:seat:U18` | `5d80dc2a-0acc-55a8-a167-fd56193bc824` | `U18`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d12:seat:U19` | `0d84e34b-8517-5c92-b09a-e63cc3d38091` | `U19`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d12:seat:U20` | `16696120-aa1c-5e28-9e56-53e87214c9a2` | `U20`; SLEEPER_UPPER/AVAILABLE |
| Trip | `trip:trip:c:r3:d13` | `714b2053-dc72-566c-ad1d-629d7c86f8ec` | SCHEDULED; AUTO_FROM_SCHEDULE; offset 13 |
| TripSeat | `trip:trip:c:r3:d13:seat:L01` | `dfe1ef1c-4131-5c10-ae5c-a429fe7094d5` | `L01`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d13:seat:L02` | `8c39d130-a4d6-5740-b762-a85f3957c346` | `L02`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d13:seat:L03` | `32ed68fa-4e17-5904-b486-ac40f5f31dbc` | `L03`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d13:seat:L04` | `75a6d831-febd-506f-a877-626ef98e363d` | `L04`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d13:seat:L05` | `a3ab411f-d149-5c42-b188-8fbf54bc3d51` | `L05`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d13:seat:L06` | `7c1a62c2-41b4-51c4-916f-665ad6803ec4` | `L06`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d13:seat:L07` | `31f8389b-bbfe-547a-ada3-039ba24b4e7e` | `L07`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d13:seat:L08` | `45ac6d90-3559-5e03-9190-4a5c04567a6f` | `L08`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d13:seat:L09` | `d6ba2c69-ac60-5375-94b5-84519ebf4481` | `L09`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d13:seat:L10` | `79803827-43f1-5be1-a888-df380135b6b1` | `L10`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d13:seat:L11` | `347c4b87-cd7e-58f7-8203-9ce8adc5fdf3` | `L11`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d13:seat:L12` | `cffbab0e-6cfc-5694-a36e-fe65c915d5c2` | `L12`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d13:seat:L13` | `5113101c-01cf-557e-a52a-3d026f785c16` | `L13`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d13:seat:L14` | `c59931b3-73d1-5a4d-ba39-edb0c6599db7` | `L14`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d13:seat:L15` | `541604d3-63e3-51e5-b668-7f242bdcca32` | `L15`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d13:seat:L16` | `e925f8cd-c082-59e3-97a2-15121c5f7936` | `L16`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d13:seat:L17` | `a0b9f0de-f4aa-5e7e-93d7-e3669ea270c2` | `L17`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d13:seat:L18` | `dd8b4573-e244-5567-84e4-6afa08cdb27a` | `L18`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d13:seat:L19` | `233e0edb-06b9-5def-b8af-281e511651c2` | `L19`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d13:seat:L20` | `35b859ba-11eb-5dd3-91cf-691cfb2c9699` | `L20`; SLEEPER_LOWER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d13:seat:U01` | `d4e620fb-42d6-5897-8634-62f4e028cde5` | `U01`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d13:seat:U02` | `0787d485-a72b-504b-8d8f-23bbcb9329d9` | `U02`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d13:seat:U03` | `ccf64a5d-4fdd-59ca-b6a6-60fbb317f5dd` | `U03`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d13:seat:U04` | `a881f546-a89a-5189-a83a-3a63eb101d00` | `U04`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d13:seat:U05` | `f7d96046-dab1-5f27-bdc7-6a28094bb088` | `U05`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d13:seat:U06` | `3bb71a78-b30c-522f-b053-fc32bdb11c94` | `U06`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d13:seat:U07` | `d5a954e3-af5e-5113-a514-36763b3d4ea5` | `U07`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d13:seat:U08` | `7894b575-614d-5e12-9daa-4d5c2f5ee181` | `U08`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d13:seat:U09` | `66ad6552-db82-5f78-a112-c30d0ade68aa` | `U09`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d13:seat:U10` | `dbfd84ce-44b4-5f1a-a9ff-53af7a3de12d` | `U10`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d13:seat:U11` | `89ad17fd-6376-548f-9889-63173641c77a` | `U11`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d13:seat:U12` | `bf6a3d8f-52ae-5df0-9e70-2336a7c25dcc` | `U12`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d13:seat:U13` | `c335ca1c-51dc-5a76-8edb-b161017eb2d5` | `U13`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d13:seat:U14` | `6e11cfe3-57c5-5747-bf02-b29966a9b8b9` | `U14`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d13:seat:U15` | `847c12fb-9000-525b-94a9-e77653ea9da0` | `U15`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d13:seat:U16` | `98eae5b9-64eb-5867-adce-d4439c60ea6c` | `U16`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d13:seat:U17` | `84d5d56e-bd1c-50d4-841b-bcf43bbb0c4e` | `U17`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d13:seat:U18` | `15e9b7ad-2124-53ca-bd76-eeb7386a7011` | `U18`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d13:seat:U19` | `ba0e8a88-acb5-5841-99b1-89a52fc6be26` | `U19`; SLEEPER_UPPER/AVAILABLE |
| TripSeat | `trip:trip:c:r3:d13:seat:U20` | `f51fb46c-c305-5d60-b04f-8491701ef6a2` | `U20`; SLEEPER_UPPER/AVAILABLE |
| KnowledgeDocument | `rag:document:public-passenger-guide` | `53e1c002-7790-5b42-b460-18abed3e06a7` | APPROVED/COMPLETED |
| KnowledgeChunk | `rag:chunk:public-passenger-guide:0` | `6622f148-dd44-5d52-8e07-9ccceb2d1aa4` | index 0; searchable; halfvec(2048) |
| KnowledgeDocument | `rag:document:operator-a-policy` | `d71264b6-12ed-5dfe-ada4-bfc36d2d5ff2` | APPROVED/COMPLETED |
| KnowledgeChunk | `rag:chunk:operator-a-policy:0` | `f3eba0a6-e2fe-5d57-a57b-780b93d4003d` | index 0; searchable; halfvec(2048) |
| KnowledgeDocument | `rag:document:system-admin-runbook` | `d1fc37a4-8c62-5950-9866-acfffb7a8fbd` | APPROVED/COMPLETED |
| KnowledgeChunk | `rag:chunk:system-admin-runbook:0` | `998c813b-aafc-5d33-887f-d2c04364660a` | index 0; searchable; halfvec(2048) |

## Composite child registry

Composite children have no UUID column; their exact fixture key and database key are listed here.

| Row | Canonical fixture key | Exact composite key | Exact state |
|---|---|---|---|
| RouteStop | `trip:route-stop:a:r3:2` | `(059ccdba-c397-5213-81d7-8baaaf1fef9d,1ace61d6-f914-5d11-a242-d69bbb4c13c4)` | order 1; 35m/30.00km; pickup/dropoff true |
| RouteStop | `trip:route-stop:a:r3:3` | `(059ccdba-c397-5213-81d7-8baaaf1fef9d,07182f5b-714b-504a-9a60-94d2b165fd79)` | order 2; 75m/65.00km; pickup/dropoff true |
| RouteStop | `trip:route-stop:a:r3:4` | `(059ccdba-c397-5213-81d7-8baaaf1fef9d,0231e70c-dcfe-5951-aa8d-60ad8900b313)` | order 3; 115m/80.00km; pickup/dropoff true |
| AlternativeRouteStop | `trip:alternative-route-stop:a:r3:1:4` | `(9d72b698-30be-5a14-bd5f-fcfc2b21b36f,0231e70c-dcfe-5951-aa8d-60ad8900b313)` | order 1; 35m/30.00km |
| AlternativeRouteStop | `trip:alternative-route-stop:a:r3:1:2` | `(9d72b698-30be-5a14-bd5f-fcfc2b21b36f,1ace61d6-f914-5d11-a242-d69bbb4c13c4)` | order 2; 75m/65.00km |
| AlternativeRouteStop | `trip:alternative-route-stop:a:r3:1:3` | `(9d72b698-30be-5a14-bd5f-fcfc2b21b36f,07182f5b-714b-504a-9a60-94d2b165fd79)` | order 3; 115m/80.00km |
| RouteStop | `trip:route-stop:b:r3:2` | `(b99d9a47-0cdf-5c2c-a9a0-89933a22c623,45bac395-9783-5e50-a278-3912535daded)` | order 1; 35m/30.00km; pickup/dropoff true |
| RouteStop | `trip:route-stop:b:r3:3` | `(b99d9a47-0cdf-5c2c-a9a0-89933a22c623,f1fc929c-1989-5553-8d55-a01f59f98933)` | order 2; 75m/65.00km; pickup/dropoff true |
| RouteStop | `trip:route-stop:b:r3:4` | `(b99d9a47-0cdf-5c2c-a9a0-89933a22c623,cb6f1e02-2a87-5618-ad75-a60363885984)` | order 3; 115m/80.00km; pickup/dropoff true |
| AlternativeRouteStop | `trip:alternative-route-stop:b:r3:1:4` | `(031f1a57-67f0-5b3a-b9c6-294b207b9555,cb6f1e02-2a87-5618-ad75-a60363885984)` | order 1; 35m/30.00km |
| AlternativeRouteStop | `trip:alternative-route-stop:b:r3:1:2` | `(031f1a57-67f0-5b3a-b9c6-294b207b9555,45bac395-9783-5e50-a278-3912535daded)` | order 2; 75m/65.00km |
| AlternativeRouteStop | `trip:alternative-route-stop:b:r3:1:3` | `(031f1a57-67f0-5b3a-b9c6-294b207b9555,f1fc929c-1989-5553-8d55-a01f59f98933)` | order 3; 115m/80.00km |
| RouteStop | `trip:route-stop:c:r3:2` | `(08a8f325-cce9-5f73-ae64-84329e84526d,2ffffab1-9398-5d75-a957-0c328668e6f3)` | order 1; 35m/30.00km; pickup/dropoff true |
| RouteStop | `trip:route-stop:c:r3:3` | `(08a8f325-cce9-5f73-ae64-84329e84526d,8ca82c0e-c89d-5f55-9ec3-d4fc90a3d8a3)` | order 2; 75m/65.00km; pickup/dropoff true |
| RouteStop | `trip:route-stop:c:r3:4` | `(08a8f325-cce9-5f73-ae64-84329e84526d,8b5cfaf2-ef55-5af5-834f-274c9595f2ca)` | order 3; 115m/80.00km; pickup/dropoff true |
| AlternativeRouteStop | `trip:alternative-route-stop:c:r3:1:4` | `(eccde21c-b120-51e3-9a1c-bc66be9952dd,8b5cfaf2-ef55-5af5-834f-274c9595f2ca)` | order 1; 35m/30.00km |
| AlternativeRouteStop | `trip:alternative-route-stop:c:r3:1:2` | `(eccde21c-b120-51e3-9a1c-bc66be9952dd,2ffffab1-9398-5d75-a957-0c328668e6f3)` | order 2; 75m/65.00km |
| AlternativeRouteStop | `trip:alternative-route-stop:c:r3:1:3` | `(eccde21c-b120-51e3-9a1c-bc66be9952dd,8ca82c0e-c89d-5f55-9ec3-d4fc90a3d8a3)` | order 3; 115m/80.00km |
| TripStop | `trip:trip-stop:a:r3:d00:2` | `(edfa1ba9-d88f-5ea8-ae89-ac350508f866,1ace61d6-f914-5d11-a242-d69bbb4c13c4)` | order 1; departure+35m; 30.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:a:r3:d00:3` | `(edfa1ba9-d88f-5ea8-ae89-ac350508f866,07182f5b-714b-504a-9a60-94d2b165fd79)` | order 2; departure+75m; 65.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:a:r3:d00:4` | `(edfa1ba9-d88f-5ea8-ae89-ac350508f866,0231e70c-dcfe-5951-aa8d-60ad8900b313)` | order 3; departure+115m; 80.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:a:r3:d01:2` | `(0326c73f-3744-535d-b948-dff792950900,1ace61d6-f914-5d11-a242-d69bbb4c13c4)` | order 1; departure+35m; 30.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:a:r3:d01:3` | `(0326c73f-3744-535d-b948-dff792950900,07182f5b-714b-504a-9a60-94d2b165fd79)` | order 2; departure+75m; 65.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:a:r3:d01:4` | `(0326c73f-3744-535d-b948-dff792950900,0231e70c-dcfe-5951-aa8d-60ad8900b313)` | order 3; departure+115m; 80.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:a:r3:d02:2` | `(ced69318-4769-5f09-a14e-a94b1f276108,1ace61d6-f914-5d11-a242-d69bbb4c13c4)` | order 1; departure+35m; 30.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:a:r3:d02:3` | `(ced69318-4769-5f09-a14e-a94b1f276108,07182f5b-714b-504a-9a60-94d2b165fd79)` | order 2; departure+75m; 65.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:a:r3:d02:4` | `(ced69318-4769-5f09-a14e-a94b1f276108,0231e70c-dcfe-5951-aa8d-60ad8900b313)` | order 3; departure+115m; 80.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:a:r3:d03:2` | `(a1621824-13b0-5068-8b78-3e670691f2bb,1ace61d6-f914-5d11-a242-d69bbb4c13c4)` | order 1; departure+35m; 30.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:a:r3:d03:3` | `(a1621824-13b0-5068-8b78-3e670691f2bb,07182f5b-714b-504a-9a60-94d2b165fd79)` | order 2; departure+75m; 65.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:a:r3:d03:4` | `(a1621824-13b0-5068-8b78-3e670691f2bb,0231e70c-dcfe-5951-aa8d-60ad8900b313)` | order 3; departure+115m; 80.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:a:r3:d04:2` | `(65dba6b7-09bc-5cc0-a081-c395696ee4cc,1ace61d6-f914-5d11-a242-d69bbb4c13c4)` | order 1; departure+35m; 30.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:a:r3:d04:3` | `(65dba6b7-09bc-5cc0-a081-c395696ee4cc,07182f5b-714b-504a-9a60-94d2b165fd79)` | order 2; departure+75m; 65.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:a:r3:d04:4` | `(65dba6b7-09bc-5cc0-a081-c395696ee4cc,0231e70c-dcfe-5951-aa8d-60ad8900b313)` | order 3; departure+115m; 80.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:a:r3:d05:2` | `(390bf8f3-2060-5a7a-9711-a680eaf642f3,1ace61d6-f914-5d11-a242-d69bbb4c13c4)` | order 1; departure+35m; 30.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:a:r3:d05:3` | `(390bf8f3-2060-5a7a-9711-a680eaf642f3,07182f5b-714b-504a-9a60-94d2b165fd79)` | order 2; departure+75m; 65.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:a:r3:d05:4` | `(390bf8f3-2060-5a7a-9711-a680eaf642f3,0231e70c-dcfe-5951-aa8d-60ad8900b313)` | order 3; departure+115m; 80.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:a:r3:d06:2` | `(efadb172-0ce7-576e-ba0f-01380faa12c7,1ace61d6-f914-5d11-a242-d69bbb4c13c4)` | order 1; departure+35m; 30.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:a:r3:d06:3` | `(efadb172-0ce7-576e-ba0f-01380faa12c7,07182f5b-714b-504a-9a60-94d2b165fd79)` | order 2; departure+75m; 65.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:a:r3:d06:4` | `(efadb172-0ce7-576e-ba0f-01380faa12c7,0231e70c-dcfe-5951-aa8d-60ad8900b313)` | order 3; departure+115m; 80.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:a:r3:d07:2` | `(5cc30e8f-0f19-5ed8-aab7-7e876620cd3f,1ace61d6-f914-5d11-a242-d69bbb4c13c4)` | order 1; departure+35m; 30.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:a:r3:d07:3` | `(5cc30e8f-0f19-5ed8-aab7-7e876620cd3f,07182f5b-714b-504a-9a60-94d2b165fd79)` | order 2; departure+75m; 65.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:a:r3:d07:4` | `(5cc30e8f-0f19-5ed8-aab7-7e876620cd3f,0231e70c-dcfe-5951-aa8d-60ad8900b313)` | order 3; departure+115m; 80.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:a:r3:d08:2` | `(b3ce59ec-e680-5be2-b02d-80900f8e6133,1ace61d6-f914-5d11-a242-d69bbb4c13c4)` | order 1; departure+35m; 30.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:a:r3:d08:3` | `(b3ce59ec-e680-5be2-b02d-80900f8e6133,07182f5b-714b-504a-9a60-94d2b165fd79)` | order 2; departure+75m; 65.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:a:r3:d08:4` | `(b3ce59ec-e680-5be2-b02d-80900f8e6133,0231e70c-dcfe-5951-aa8d-60ad8900b313)` | order 3; departure+115m; 80.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:a:r3:d09:2` | `(a3fb999b-d938-5f53-a3a6-8811d3c21aba,1ace61d6-f914-5d11-a242-d69bbb4c13c4)` | order 1; departure+35m; 30.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:a:r3:d09:3` | `(a3fb999b-d938-5f53-a3a6-8811d3c21aba,07182f5b-714b-504a-9a60-94d2b165fd79)` | order 2; departure+75m; 65.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:a:r3:d09:4` | `(a3fb999b-d938-5f53-a3a6-8811d3c21aba,0231e70c-dcfe-5951-aa8d-60ad8900b313)` | order 3; departure+115m; 80.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:a:r3:d10:2` | `(bac62ea4-8671-5f19-9ac2-0d844d24f939,1ace61d6-f914-5d11-a242-d69bbb4c13c4)` | order 1; departure+35m; 30.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:a:r3:d10:3` | `(bac62ea4-8671-5f19-9ac2-0d844d24f939,07182f5b-714b-504a-9a60-94d2b165fd79)` | order 2; departure+75m; 65.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:a:r3:d10:4` | `(bac62ea4-8671-5f19-9ac2-0d844d24f939,0231e70c-dcfe-5951-aa8d-60ad8900b313)` | order 3; departure+115m; 80.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:a:r3:d11:2` | `(9cac0fde-c243-5850-ae65-a7a946f7076b,1ace61d6-f914-5d11-a242-d69bbb4c13c4)` | order 1; departure+35m; 30.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:a:r3:d11:3` | `(9cac0fde-c243-5850-ae65-a7a946f7076b,07182f5b-714b-504a-9a60-94d2b165fd79)` | order 2; departure+75m; 65.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:a:r3:d11:4` | `(9cac0fde-c243-5850-ae65-a7a946f7076b,0231e70c-dcfe-5951-aa8d-60ad8900b313)` | order 3; departure+115m; 80.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:a:r3:d12:2` | `(1cd9eb58-5d28-5e87-baed-54cf9c5d5c25,1ace61d6-f914-5d11-a242-d69bbb4c13c4)` | order 1; departure+35m; 30.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:a:r3:d12:3` | `(1cd9eb58-5d28-5e87-baed-54cf9c5d5c25,07182f5b-714b-504a-9a60-94d2b165fd79)` | order 2; departure+75m; 65.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:a:r3:d12:4` | `(1cd9eb58-5d28-5e87-baed-54cf9c5d5c25,0231e70c-dcfe-5951-aa8d-60ad8900b313)` | order 3; departure+115m; 80.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:a:r3:d13:2` | `(5903c28e-8e34-5854-a2ee-5cae5766f964,1ace61d6-f914-5d11-a242-d69bbb4c13c4)` | order 1; departure+35m; 30.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:a:r3:d13:3` | `(5903c28e-8e34-5854-a2ee-5cae5766f964,07182f5b-714b-504a-9a60-94d2b165fd79)` | order 2; departure+75m; 65.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:a:r3:d13:4` | `(5903c28e-8e34-5854-a2ee-5cae5766f964,0231e70c-dcfe-5951-aa8d-60ad8900b313)` | order 3; departure+115m; 80.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:b:r3:d00:2` | `(ef93427d-f9a3-5d99-be36-26e27c0bfd33,45bac395-9783-5e50-a278-3912535daded)` | order 1; departure+35m; 30.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:b:r3:d00:3` | `(ef93427d-f9a3-5d99-be36-26e27c0bfd33,f1fc929c-1989-5553-8d55-a01f59f98933)` | order 2; departure+75m; 65.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:b:r3:d00:4` | `(ef93427d-f9a3-5d99-be36-26e27c0bfd33,cb6f1e02-2a87-5618-ad75-a60363885984)` | order 3; departure+115m; 80.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:b:r3:d01:2` | `(a2424e44-8da2-575e-a944-6e31244a749b,45bac395-9783-5e50-a278-3912535daded)` | order 1; departure+35m; 30.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:b:r3:d01:3` | `(a2424e44-8da2-575e-a944-6e31244a749b,f1fc929c-1989-5553-8d55-a01f59f98933)` | order 2; departure+75m; 65.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:b:r3:d01:4` | `(a2424e44-8da2-575e-a944-6e31244a749b,cb6f1e02-2a87-5618-ad75-a60363885984)` | order 3; departure+115m; 80.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:b:r3:d02:2` | `(491b2a6d-8b68-5450-aeaa-f7aed08e33d3,45bac395-9783-5e50-a278-3912535daded)` | order 1; departure+35m; 30.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:b:r3:d02:3` | `(491b2a6d-8b68-5450-aeaa-f7aed08e33d3,f1fc929c-1989-5553-8d55-a01f59f98933)` | order 2; departure+75m; 65.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:b:r3:d02:4` | `(491b2a6d-8b68-5450-aeaa-f7aed08e33d3,cb6f1e02-2a87-5618-ad75-a60363885984)` | order 3; departure+115m; 80.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:b:r3:d03:2` | `(cd4f3530-c8fc-5bf5-a9c1-f526c0c66b3f,45bac395-9783-5e50-a278-3912535daded)` | order 1; departure+35m; 30.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:b:r3:d03:3` | `(cd4f3530-c8fc-5bf5-a9c1-f526c0c66b3f,f1fc929c-1989-5553-8d55-a01f59f98933)` | order 2; departure+75m; 65.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:b:r3:d03:4` | `(cd4f3530-c8fc-5bf5-a9c1-f526c0c66b3f,cb6f1e02-2a87-5618-ad75-a60363885984)` | order 3; departure+115m; 80.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:b:r3:d04:2` | `(642366b1-de24-550c-beb5-5fa43a8d5154,45bac395-9783-5e50-a278-3912535daded)` | order 1; departure+35m; 30.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:b:r3:d04:3` | `(642366b1-de24-550c-beb5-5fa43a8d5154,f1fc929c-1989-5553-8d55-a01f59f98933)` | order 2; departure+75m; 65.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:b:r3:d04:4` | `(642366b1-de24-550c-beb5-5fa43a8d5154,cb6f1e02-2a87-5618-ad75-a60363885984)` | order 3; departure+115m; 80.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:b:r3:d05:2` | `(ece7583c-cfc4-5669-a19f-7e18310d951d,45bac395-9783-5e50-a278-3912535daded)` | order 1; departure+35m; 30.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:b:r3:d05:3` | `(ece7583c-cfc4-5669-a19f-7e18310d951d,f1fc929c-1989-5553-8d55-a01f59f98933)` | order 2; departure+75m; 65.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:b:r3:d05:4` | `(ece7583c-cfc4-5669-a19f-7e18310d951d,cb6f1e02-2a87-5618-ad75-a60363885984)` | order 3; departure+115m; 80.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:b:r3:d06:2` | `(a28b2fb8-b364-5eb7-b979-948b8a9fa00c,45bac395-9783-5e50-a278-3912535daded)` | order 1; departure+35m; 30.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:b:r3:d06:3` | `(a28b2fb8-b364-5eb7-b979-948b8a9fa00c,f1fc929c-1989-5553-8d55-a01f59f98933)` | order 2; departure+75m; 65.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:b:r3:d06:4` | `(a28b2fb8-b364-5eb7-b979-948b8a9fa00c,cb6f1e02-2a87-5618-ad75-a60363885984)` | order 3; departure+115m; 80.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:b:r3:d07:2` | `(b606947d-0a25-5cca-a859-77ce305ffadc,45bac395-9783-5e50-a278-3912535daded)` | order 1; departure+35m; 30.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:b:r3:d07:3` | `(b606947d-0a25-5cca-a859-77ce305ffadc,f1fc929c-1989-5553-8d55-a01f59f98933)` | order 2; departure+75m; 65.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:b:r3:d07:4` | `(b606947d-0a25-5cca-a859-77ce305ffadc,cb6f1e02-2a87-5618-ad75-a60363885984)` | order 3; departure+115m; 80.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:b:r3:d08:2` | `(636e1e11-a5f8-58ff-8af1-b5f800a16d72,45bac395-9783-5e50-a278-3912535daded)` | order 1; departure+35m; 30.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:b:r3:d08:3` | `(636e1e11-a5f8-58ff-8af1-b5f800a16d72,f1fc929c-1989-5553-8d55-a01f59f98933)` | order 2; departure+75m; 65.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:b:r3:d08:4` | `(636e1e11-a5f8-58ff-8af1-b5f800a16d72,cb6f1e02-2a87-5618-ad75-a60363885984)` | order 3; departure+115m; 80.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:b:r3:d09:2` | `(23c2cc02-dd09-54b1-ae1a-f201c542937a,45bac395-9783-5e50-a278-3912535daded)` | order 1; departure+35m; 30.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:b:r3:d09:3` | `(23c2cc02-dd09-54b1-ae1a-f201c542937a,f1fc929c-1989-5553-8d55-a01f59f98933)` | order 2; departure+75m; 65.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:b:r3:d09:4` | `(23c2cc02-dd09-54b1-ae1a-f201c542937a,cb6f1e02-2a87-5618-ad75-a60363885984)` | order 3; departure+115m; 80.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:b:r3:d10:2` | `(8af525d7-1e3e-5eae-aa09-87174406e6a1,45bac395-9783-5e50-a278-3912535daded)` | order 1; departure+35m; 30.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:b:r3:d10:3` | `(8af525d7-1e3e-5eae-aa09-87174406e6a1,f1fc929c-1989-5553-8d55-a01f59f98933)` | order 2; departure+75m; 65.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:b:r3:d10:4` | `(8af525d7-1e3e-5eae-aa09-87174406e6a1,cb6f1e02-2a87-5618-ad75-a60363885984)` | order 3; departure+115m; 80.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:b:r3:d11:2` | `(cf2a2f2c-9889-594f-bf71-c6d183dd4a97,45bac395-9783-5e50-a278-3912535daded)` | order 1; departure+35m; 30.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:b:r3:d11:3` | `(cf2a2f2c-9889-594f-bf71-c6d183dd4a97,f1fc929c-1989-5553-8d55-a01f59f98933)` | order 2; departure+75m; 65.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:b:r3:d11:4` | `(cf2a2f2c-9889-594f-bf71-c6d183dd4a97,cb6f1e02-2a87-5618-ad75-a60363885984)` | order 3; departure+115m; 80.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:b:r3:d12:2` | `(a74b1d11-3790-5f9d-8719-36d570462dd0,45bac395-9783-5e50-a278-3912535daded)` | order 1; departure+35m; 30.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:b:r3:d12:3` | `(a74b1d11-3790-5f9d-8719-36d570462dd0,f1fc929c-1989-5553-8d55-a01f59f98933)` | order 2; departure+75m; 65.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:b:r3:d12:4` | `(a74b1d11-3790-5f9d-8719-36d570462dd0,cb6f1e02-2a87-5618-ad75-a60363885984)` | order 3; departure+115m; 80.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:b:r3:d13:2` | `(560b7338-8284-5d3d-a36f-02de9c52af15,45bac395-9783-5e50-a278-3912535daded)` | order 1; departure+35m; 30.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:b:r3:d13:3` | `(560b7338-8284-5d3d-a36f-02de9c52af15,f1fc929c-1989-5553-8d55-a01f59f98933)` | order 2; departure+75m; 65.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:b:r3:d13:4` | `(560b7338-8284-5d3d-a36f-02de9c52af15,cb6f1e02-2a87-5618-ad75-a60363885984)` | order 3; departure+115m; 80.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:c:r3:d00:2` | `(36f7a0fb-87e5-5657-bc02-5505e34503b3,2ffffab1-9398-5d75-a957-0c328668e6f3)` | order 1; departure+35m; 30.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:c:r3:d00:3` | `(36f7a0fb-87e5-5657-bc02-5505e34503b3,8ca82c0e-c89d-5f55-9ec3-d4fc90a3d8a3)` | order 2; departure+75m; 65.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:c:r3:d00:4` | `(36f7a0fb-87e5-5657-bc02-5505e34503b3,8b5cfaf2-ef55-5af5-834f-274c9595f2ca)` | order 3; departure+115m; 80.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:c:r3:d01:2` | `(7296939f-bc99-5421-9945-6cfa3265fe8c,2ffffab1-9398-5d75-a957-0c328668e6f3)` | order 1; departure+35m; 30.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:c:r3:d01:3` | `(7296939f-bc99-5421-9945-6cfa3265fe8c,8ca82c0e-c89d-5f55-9ec3-d4fc90a3d8a3)` | order 2; departure+75m; 65.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:c:r3:d01:4` | `(7296939f-bc99-5421-9945-6cfa3265fe8c,8b5cfaf2-ef55-5af5-834f-274c9595f2ca)` | order 3; departure+115m; 80.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:c:r3:d02:2` | `(5674c91d-936e-5249-9d87-4858a65a67e3,2ffffab1-9398-5d75-a957-0c328668e6f3)` | order 1; departure+35m; 30.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:c:r3:d02:3` | `(5674c91d-936e-5249-9d87-4858a65a67e3,8ca82c0e-c89d-5f55-9ec3-d4fc90a3d8a3)` | order 2; departure+75m; 65.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:c:r3:d02:4` | `(5674c91d-936e-5249-9d87-4858a65a67e3,8b5cfaf2-ef55-5af5-834f-274c9595f2ca)` | order 3; departure+115m; 80.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:c:r3:d03:2` | `(7ecb6c7f-afca-5a9b-aa78-09f1717df5f6,2ffffab1-9398-5d75-a957-0c328668e6f3)` | order 1; departure+35m; 30.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:c:r3:d03:3` | `(7ecb6c7f-afca-5a9b-aa78-09f1717df5f6,8ca82c0e-c89d-5f55-9ec3-d4fc90a3d8a3)` | order 2; departure+75m; 65.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:c:r3:d03:4` | `(7ecb6c7f-afca-5a9b-aa78-09f1717df5f6,8b5cfaf2-ef55-5af5-834f-274c9595f2ca)` | order 3; departure+115m; 80.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:c:r3:d04:2` | `(5781fd5f-8cb0-5ded-926a-4d7bcaf59b5f,2ffffab1-9398-5d75-a957-0c328668e6f3)` | order 1; departure+35m; 30.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:c:r3:d04:3` | `(5781fd5f-8cb0-5ded-926a-4d7bcaf59b5f,8ca82c0e-c89d-5f55-9ec3-d4fc90a3d8a3)` | order 2; departure+75m; 65.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:c:r3:d04:4` | `(5781fd5f-8cb0-5ded-926a-4d7bcaf59b5f,8b5cfaf2-ef55-5af5-834f-274c9595f2ca)` | order 3; departure+115m; 80.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:c:r3:d05:2` | `(ba1ee855-c42a-5787-a2ba-53b523f82ad5,2ffffab1-9398-5d75-a957-0c328668e6f3)` | order 1; departure+35m; 30.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:c:r3:d05:3` | `(ba1ee855-c42a-5787-a2ba-53b523f82ad5,8ca82c0e-c89d-5f55-9ec3-d4fc90a3d8a3)` | order 2; departure+75m; 65.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:c:r3:d05:4` | `(ba1ee855-c42a-5787-a2ba-53b523f82ad5,8b5cfaf2-ef55-5af5-834f-274c9595f2ca)` | order 3; departure+115m; 80.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:c:r3:d06:2` | `(0207573b-05a6-5557-9ebe-f252e615fbee,2ffffab1-9398-5d75-a957-0c328668e6f3)` | order 1; departure+35m; 30.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:c:r3:d06:3` | `(0207573b-05a6-5557-9ebe-f252e615fbee,8ca82c0e-c89d-5f55-9ec3-d4fc90a3d8a3)` | order 2; departure+75m; 65.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:c:r3:d06:4` | `(0207573b-05a6-5557-9ebe-f252e615fbee,8b5cfaf2-ef55-5af5-834f-274c9595f2ca)` | order 3; departure+115m; 80.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:c:r3:d07:2` | `(f77e5a64-116f-592f-8d10-9642fcd579f2,2ffffab1-9398-5d75-a957-0c328668e6f3)` | order 1; departure+35m; 30.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:c:r3:d07:3` | `(f77e5a64-116f-592f-8d10-9642fcd579f2,8ca82c0e-c89d-5f55-9ec3-d4fc90a3d8a3)` | order 2; departure+75m; 65.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:c:r3:d07:4` | `(f77e5a64-116f-592f-8d10-9642fcd579f2,8b5cfaf2-ef55-5af5-834f-274c9595f2ca)` | order 3; departure+115m; 80.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:c:r3:d08:2` | `(07da5520-13d2-5df8-9983-3a1cd927eaed,2ffffab1-9398-5d75-a957-0c328668e6f3)` | order 1; departure+35m; 30.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:c:r3:d08:3` | `(07da5520-13d2-5df8-9983-3a1cd927eaed,8ca82c0e-c89d-5f55-9ec3-d4fc90a3d8a3)` | order 2; departure+75m; 65.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:c:r3:d08:4` | `(07da5520-13d2-5df8-9983-3a1cd927eaed,8b5cfaf2-ef55-5af5-834f-274c9595f2ca)` | order 3; departure+115m; 80.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:c:r3:d09:2` | `(bfc420aa-99b6-58a7-a9e8-3d32debce516,2ffffab1-9398-5d75-a957-0c328668e6f3)` | order 1; departure+35m; 30.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:c:r3:d09:3` | `(bfc420aa-99b6-58a7-a9e8-3d32debce516,8ca82c0e-c89d-5f55-9ec3-d4fc90a3d8a3)` | order 2; departure+75m; 65.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:c:r3:d09:4` | `(bfc420aa-99b6-58a7-a9e8-3d32debce516,8b5cfaf2-ef55-5af5-834f-274c9595f2ca)` | order 3; departure+115m; 80.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:c:r3:d10:2` | `(44b41afc-652a-5dce-ab17-3d05f5c55cb7,2ffffab1-9398-5d75-a957-0c328668e6f3)` | order 1; departure+35m; 30.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:c:r3:d10:3` | `(44b41afc-652a-5dce-ab17-3d05f5c55cb7,8ca82c0e-c89d-5f55-9ec3-d4fc90a3d8a3)` | order 2; departure+75m; 65.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:c:r3:d10:4` | `(44b41afc-652a-5dce-ab17-3d05f5c55cb7,8b5cfaf2-ef55-5af5-834f-274c9595f2ca)` | order 3; departure+115m; 80.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:c:r3:d11:2` | `(9c8d306c-9b9e-5bbb-b9d6-a85722d9f0f1,2ffffab1-9398-5d75-a957-0c328668e6f3)` | order 1; departure+35m; 30.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:c:r3:d11:3` | `(9c8d306c-9b9e-5bbb-b9d6-a85722d9f0f1,8ca82c0e-c89d-5f55-9ec3-d4fc90a3d8a3)` | order 2; departure+75m; 65.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:c:r3:d11:4` | `(9c8d306c-9b9e-5bbb-b9d6-a85722d9f0f1,8b5cfaf2-ef55-5af5-834f-274c9595f2ca)` | order 3; departure+115m; 80.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:c:r3:d12:2` | `(d22fe089-77ec-59f7-b70b-88aafb79cca6,2ffffab1-9398-5d75-a957-0c328668e6f3)` | order 1; departure+35m; 30.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:c:r3:d12:3` | `(d22fe089-77ec-59f7-b70b-88aafb79cca6,8ca82c0e-c89d-5f55-9ec3-d4fc90a3d8a3)` | order 2; departure+75m; 65.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:c:r3:d12:4` | `(d22fe089-77ec-59f7-b70b-88aafb79cca6,8b5cfaf2-ef55-5af5-834f-274c9595f2ca)` | order 3; departure+115m; 80.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:c:r3:d13:2` | `(714b2053-dc72-566c-ad1d-629d7c86f8ec,2ffffab1-9398-5d75-a957-0c328668e6f3)` | order 1; departure+35m; 30.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:c:r3:d13:3` | `(714b2053-dc72-566c-ad1d-629d7c86f8ec,8ca82c0e-c89d-5f55-9ec3-d4fc90a3d8a3)` | order 2; departure+75m; 65.00km; PENDING; pickup/dropoff true |
| TripStop | `trip:trip-stop:c:r3:d13:4` | `(714b2053-dc72-566c-ad1d-629d7c86f8ec,8b5cfaf2-ef55-5af5-834f-274c9595f2ca)` | order 3; departure+115m; 80.00km; PENDING; pickup/dropoff true |
| ParcelRouteFare | `parcel:route-fare:a:r1:small` | `(c908c072-337a-526e-bf89-27254cae8e8f,SMALL)` | operator 6276b48c-3984-582b-9c35-0c2fbe20baa7; price 50000; per-kg/minimum 0/0; effective T0..null |
| ParcelRouteFare | `parcel:route-fare:b:r1:small` | `(67db3832-0894-5afc-94ab-ea73b3dd8671,SMALL)` | operator d63b3c32-8c12-5130-a347-0ef8df286605; price 50000; per-kg/minimum 0/0; effective T0..null |
| Wallet | `payment:wallet:01` | `user_id=167b6f1c-e47d-56cd-9715-1d9b75637cd3` | balance 2000000; rowVersion 0 |
| Wallet | `payment:wallet:02` | `user_id=c251549f-b0d5-5d73-9e36-50ff74bf69f2` | balance 2000000; rowVersion 0 |
| Wallet | `payment:wallet:03` | `user_id=6288dc1d-ac87-50b6-8b85-f45e7852ea50` | balance 2000000; rowVersion 0 |
| Wallet | `payment:wallet:04` | `user_id=b5ec73ed-ae93-5fb7-b0fe-c61ada94d4ba` | balance 2000000; rowVersion 0 |
| Wallet | `payment:wallet:05` | `user_id=fc58a993-6184-5cf1-971d-c38118fbbee7` | balance 2000000; rowVersion 0 |
| Wallet | `payment:wallet:06` | `user_id=b41d9085-e396-5014-ab7a-67e6b2d6fd88` | balance 2000000; rowVersion 0 |
| Wallet | `payment:wallet:07` | `user_id=4ca78bdc-23ba-5a01-b40a-49e2d84f69c5` | balance 2000000; rowVersion 0 |
| Wallet | `payment:wallet:08` | `user_id=1fcc1bb2-20fb-5c8f-bea4-41f319ed885f` | balance 2000000; rowVersion 0 |
| Wallet | `payment:wallet:09` | `user_id=99aa3004-333a-5105-8fd4-09d8f366de92` | balance 2000000; rowVersion 0 |
| Wallet | `payment:wallet:10` | `user_id=820ece02-0f0c-5bb4-90d4-0d5bbf0962ec` | balance 2000000; rowVersion 0 |

## SOT references

- `BE_TIMELINE_VU.md` Day 44.
- `SU26SE101_VIETRIDE_technical_context_v7.md` sections 4.4, 4.5, 6.5, 6.6, 6.8,
  6.10, and 6.11.
- `BACKEND_SOURCE_OF_TRUTH.md` sections 3.1 and 12.4.
- `db-schema/identity-user/README.md`, `db-schema/trip-route-vehicle/README.md`,
  `db-schema/booking/README.md`, `db-schema/payment-wallet/README.md`,
  `db-schema/parcel/README.md`, and `db-schema/rag-ai/README.md`.
- Human-approved Day 44 Q1-Q7 decisions dated 2026-08-08, frozen in
  `docs/handoff/day-44-plan.md`.

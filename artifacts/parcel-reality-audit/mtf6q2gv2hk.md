# Parcel Full Reality Audit — PCL-E2E-mtf6q2gv2hk

- Generated at: 2026-08-30T02:25:52.419Z
- Gateway: http://localhost:3000
- Result: PASS

## Retained data

| Resource | ID/value |
|---|---|
| users.systemAdminUserId | 67b971da-26d4-4906-a40c-bf4a4044331a |
| users.senderUserId | c1c99889-9e61-4ab6-97cb-640209584b83 |
| users.recipientUserId | 2150b4fd-0ab5-4541-80ad-aaab112aac45 |
| users.operatorAdminUserId | 9c3f16a6-006d-4849-b034-8d17be4130c2 |
| users.driverUserId | 8df0063e-facc-44a6-a566-ec289a35be4b |
| users.assistantUserId | 61b75ad6-3942-4a2f-8aa9-70d5be719fe7 |
| users.foreignOperatorAdminUserId | eb5284b4-4e62-4ef7-abd5-6b02c62e0c13 |
| operators.primaryOperatorId | dd72c2a3-ff0e-4e89-8421-4219cd5f0f21 |
| operators.foreignOperatorId | 4791f710-70bb-4abb-9897-d6dc85d481aa |
| trips.sourceTripId | 1f57c66a-730f-4b3a-ae4a-6c9027f6d97e |
| trips.forwardingTripId | 2c7e87b0-6418-4fcf-a067-3931158ad032 |
| trips.routeId | 3a196b3a-8e2c-4b56-adcb-99998539481c |
| trips.vehicleId | d5f9d13b-1909-4e9a-8a51-6cb8a89344c2 |
| trips.wrongStopId | b7e79f1c-0123-4d02-bc9c-a4c9dab74ece |
| trips.targetStopId | 073f1554-47d6-4e8d-8c96-9d5d3887eaeb |
| trips.destinationStationId | 5703c683-6d23-4006-adeb-a49c061e4632 |
| bookings.linkedBookingId | 62de9f30-2858-4da5-a711-53dc944b1e17 |
| parcels.happy | 0d5ee6d0-3828-480e-88b4-8f5a38de06b4 |
| parcels.bookingLinked | 309aa4fc-333a-4b7a-90af-23f9b95cb065 |
| parcels.wrongStop | fc4e4489-444c-4d3d-a586-7138c650e807 |
| parcels.missing | 1e94b393-fd68-42e8-a88a-86e37a510ffd |
| parcels.recovered | 7da3bf87-2de6-432e-8900-485c10ed25be |
| parcels.destinationUnresolved | 2c0215ff-e0e9-4f92-b930-59efa4246279 |
| parcels.claim12m | f9dd8d3a-6695-49fc-9d26-1efe1f4db956 |
| parcels.claimNoProof | fecf7dac-35e5-42b9-b150-c5b8465ad17f |
| parcels.claimExpired | c75559cb-df9c-4391-b0d1-5802ed6eb9d8 |
| parcels.claim80m | 8bc09863-d494-48ee-9726-99230c2f5e61 |
| parcels.identityMismatch | 008f0b8f-73a1-4832-93c2-0b564d77cbf5 |
| incidents.packageIdentityMismatchIncidentId | 30af2463-d048-4608-b1e9-e80dcb3f27fd |
| incidents.wrongStopIncidentId | 4781c0af-0027-4c3d-845d-a47200dc589a |
| incidents.missingAfterStopReconciliationIncidentId | f43248c2-1343-4889-ac1b-e5033e942783 |
| incidents.recoveredOnVehicleIncidentId | cbc2fdcc-e7d9-483b-9fbd-183df2993b06 |
| incidents.destinationUnscannedHandoffIncidentId | 630dbed1-2c62-46e3-877e-bd11f13e4a5f |
| incidents.claim12mIncidentId | 804a2e01-e87b-422f-9d12-195f87d396ba |
| incidents.claimNoProofIncidentId | 11c5b95c-79c1-45d3-9801-3cdc84bb65c7 |
| incidents.claimExpiredIncidentId | 5cf00f64-7d4b-44db-b816-9cbf557498d3 |
| incidents.claim80mIncidentId | ddab5d45-dc91-47c5-a445-43f03188bc63 |
| claims.claim12mId | ace50d1f-ae89-47fe-b7ab-948e36348c8c |
| claims.claimNoProofId | b98283b6-b35e-449b-87f4-f49b8d713c92 |
| claims.claim80mId | 72a09532-ae6a-42e8-bf92-5ed9494ac6c6 |
| appeals.claim12mAppealId | 4729fef7-37de-44a7-8ccc-e07f544f5532 |
| payoutReferences.claim12m | 9bbe4bd4-cc6d-4f3d-93ae-722dd7c7d1c8 |
| payoutReferences.claimNoProof | 0c28d7d6-009b-42da-939d-cbd35677c0d7 |
| payoutReferences.claim80m | a907baf3-e549-4727-a9c0-74a62e76e41e |
| databaseEvidence.custodyEventCount | 35 |
| databaseEvidence.incidentStatusSummary | PACKAGE_IDENTITY_MISMATCH:SEARCHING,WRONG_STOP:RESOLVED,UNSCANNED_HANDOFF:SEARCHING,UNSCANNED_HANDOFF:SEARCHING,UNSCANNED_HANDOFF:RESOLVED,UNSCANNED_HANDOFF:SEARCHING,DELIVERY_NOT_RECEIVED:LOST_CONFIRMED,DELIVERY_NOT_RECEIVED:LOST_CONFIRMED,DELIVERY_NOT_RECEIVED:LOST_CONFIRMED,DELIVERY_NOT_RECEIVED:LOST_CONFIRMED |
| databaseEvidence.transitLegStatusSummary | COMPLETED,ACTIVE,FORWARDED,ACTIVE,COMPLETED,ACTIVE,LOST,LOST,LOST,LOST,PLANNED,COMPLETED |
| databaseEvidence.searchTaskStatusSummary | OPEN,OPEN,CANCELLED,CANCELLED,OPEN,CANCELLED,OPEN,CANCELLED,OPEN,OPEN,OPEN,OPEN,FAILED,FAILED,FAILED,FAILED,FAILED,FAILED,FAILED,FAILED,FAILED,FAILED,FAILED,FAILED |
| databaseEvidence.claimStatusSummary | PAID,PAID,FUNDING_PENDING |
| databaseEvidence.negativeWalletCount | 0 |

## Business checks

| Result | Check | Detail |
|---|---|---|
| PASS | system admin authenticated through Gateway |  |
| PASS | real users/operator created and password login verified | operator=dd72c2a3-ff0e-4e89-8421-4219cd5f0f21 |
| PASS | operator subscription upgrade + VNPay IPN + replay | 2192a2c0-d325-49af-833e-bcd2782296cb |
| PASS | passenger wallet top-up + VNPay IPN + replay | 100000000 VND |
| PASS | Hangfire generated real trips from schedule | 7 trips |
| PASS | operator compensation policy read model | v1 |
| PASS | real Booking confirmed and ready for Parcel attachment | 62de9f30-2858-4da5-a711-53dc944b1e17 |
| PASS | eleven real Parcels created, including Booking-linked and parcel-only flows |  |
| PASS | Passenger sent/received/detail require one request per screen |  |
| PASS | Driver manifest screen-ready in one request | 11 items |
| PASS | custody scan records physical trace without changing business status |  |
| PASS | custody scan rejects an arbitrary physical location | 409 PARCEL_CUSTODY_LOCATION_MISMATCH |
| PASS | unload without QR rejected | 422 VALIDATION_ERROR |
| PASS | Trip started event moved loaded Parcels to IN_TRANSIT |  |
| PASS | QR of another Parcel rejected | 409 SCAN_IDENTITY_MISMATCH |
| PASS | correct QR at wrong stop rejected | 409 PARCEL_CUSTODY_LOCATION_MISMATCH |
| PASS | cross-tenant custody approval hidden | 404 PARCEL_CUSTODY_EXCEPTION_REQUEST_NOT_FOUND |
| PASS | unidentified package queue/candidates/manual match |  |
| PASS | wrong-stop found → forwarding option → new leg → crew handoff |  |
| PASS | unload at already-departed stop rejected | 409 PARCEL_CUSTODY_LOCATION_MISMATCH |
| PASS | stop reconciliation returns actionable unresolved Parcel rows |  |
| PASS | missing → searching → found on same vehicle → normal delivery without forwarding |  |
| PASS | complete blocked before destination reconciliation | 409 PARCEL_DESTINATION_RECONCILIATION_REQUIRED |
| PASS | destination arrive does not create MISSING; reconciliation gates completion and prevents duplicate incident |  |
| PASS | cross-tenant incident read blocked | 403 FORBIDDEN |
| PASS | Passenger tracking is complete in one request and claim privacy is enforced |  |
| PASS | Passenger cannot self-report MISSING for claim12m | 422 PARCEL_INCIDENT_TYPE_NOT_REPORTABLE |
| PASS | Passenger cannot self-report MISSING for claimNoProof | 422 PARCEL_INCIDENT_TYPE_NOT_REPORTABLE |
| PASS | Passenger cannot self-report MISSING for claimExpired | 422 PARCEL_INCIDENT_TYPE_NOT_REPORTABLE |
| PASS | claim window expiry enforced for claimExpired | 409 PARCEL_INCIDENT_CLAIM_WINDOW_EXPIRED |
| PASS | Passenger cannot self-report MISSING for claim80m | 422 PARCEL_INCIDENT_TYPE_NOT_REPORTABLE |
| PASS | 12m × 50% cargo compensation paid | cargo=6000000 VND, freightRefund=6000000 VND, total=12000000 VND |
| PASS | concurrent claim decision loser rejected | 409 PARCEL_CLAIM_ALREADY_DECIDED |
| PASS | no-proof fallback compensation paid | cargo=4 × 6000000 VND = 24000000 VND, freightRefund=6000000 VND, total=30000000 VND |
| PASS | paid claim appeal submit/replay/queue/operator decision |  |
| PASS | source trip settled and OperatorWallet prepared for insufficient-funding branch |  |
| PASS | duplicate claim decision rejected | 409 PARCEL_CLAIM_ALREADY_DECIDED |
| PASS | 80m compensation capped at 30m and enters FUNDING_PENDING |  |
| PASS | cross-tenant claim read blocked | 403 FORBIDDEN |
| PASS | forwarding target trip clock moved into the real boarding window |  |
| PASS | forwarded Parcel reached correct stop and recipient confirmed delivery |  |
| PASS | transit-leg lifecycle and terminal search-task invariants are consistent |  |
| PASS | DB invariants: append-only custody, unique event keys, non-negative wallets |  |

## HTTP evidence

| Method | Path | HTTP | Error code | traceId |
|---|---|---:|---|---|
| POST | /v1/auth/login | 401 | AUTH_INVALID_CREDENTIALS | 1becbcea-bcef-4b76-8ab3-a43874a17cf1 |
| POST | /v1/auth/login | 200 |  | 0b488476-f6cf-4b40-b704-c64d7731543a |
| POST | /v1/auth/register | 201 |  | 1810519b-803f-48b8-bef6-5185ac4482f4 |
| POST | /v1/auth/verify-email | 200 |  | 7e9a4462-d81e-4718-9f74-89a690da7449 |
| POST | /v1/auth/login | 200 |  | b22fe93c-0f9b-4bbc-8853-b515a10da24b |
| POST | /v1/auth/register | 201 |  | dc2b238c-323b-43a3-b99f-9959676b821d |
| POST | /v1/auth/verify-email | 200 |  | 99cce283-1e54-479c-beb2-a29bbfd8c0d7 |
| POST | /v1/auth/login | 200 |  | 338deb09-3333-434a-ba09-0269c68a4f44 |
| POST | /v1/admin/operators | 201 |  | c7488277-7595-4129-a658-9d7400e361d9 |
| POST | /v1/auth/set-initial-password | 200 |  | bb19901c-9f9f-40f6-b4af-f9ce4c6b498e |
| POST | /v1/auth/login | 200 |  | 2feb552e-29a6-473e-9287-88274737b944 |
| POST | /v1/admin/operators | 201 |  | e0d55bb8-b1c7-4915-a487-04c1d44d93d0 |
| POST | /v1/auth/set-initial-password | 200 |  | a4fbcefd-67b2-4e65-88e3-90785770f335 |
| POST | /v1/auth/login | 200 |  | a3071dbd-72b8-4232-9eaf-4eb6814d5a9a |
| POST | /v1/operator/users | 201 |  | 11d852c5-e13e-4585-8386-7c65f5a28e3e |
| POST | /v1/auth/set-initial-password | 200 |  | c9ff498a-8d0c-401f-98bb-7be9c489e7bb |
| POST | /v1/auth/login | 200 |  | 24e9fac0-19e4-4257-baf3-4b81e8a96af1 |
| POST | /v1/operator/users | 201 |  | d3d6e79d-874e-4afa-938c-9da917f2b029 |
| POST | /v1/auth/set-initial-password | 200 |  | f4931dce-e342-40f6-a1cc-7bed853b9758 |
| POST | /v1/auth/login | 200 |  | 65311004-440e-4423-9b1d-0b8f7993c14e |
| POST | /v1/admin/subscription-plans | 201 |  | e0b1fbb1-04d0-4712-98f4-bebc1b5406d7 |
| POST | /v1/operator/subscription/upgrade | 202 |  | fc309aef-e9b0-4eff-99b2-dad92408313a |
| POST | /v1/operator/subscription/upgrade | 202 |  | fc309aef-e9b0-4eff-99b2-dad92408313a |
| POST | /v1/payments/subscription-vnpay-ipn?vnp_Amount=100000&vnp_BankCode=NCB&vnp_PayDate=20260830022311&vnp_ResponseCode=00&vnp_TransactionNo=911788056591821&vnp_TransactionStatus=00&vnp_TmnCode=5J37I6CM&vnp_TxnRef=fec071cc-ebfb-4025-bf31-bd237b408f55&vnp_SecureHash=8cf0fd31306684821c55e032fa6778095af20018ecc3f7e40eb9703ad72235ec8e0ee276d32a1f12f444449dbfa177287e9323c15ebb0e9f3716c3082cb2f467 | 200 |  |  |
| GET | /v1/operator/subscription | 200 |  | e88e8048-3269-4106-9e36-53a1ed90909e |
| GET | /v1/operator/subscription | 200 |  | 86092c1a-9c4e-45a5-a2b8-12e5920a7d6a |
| GET | /v1/operator/subscription | 200 |  | 9af209cc-a1f2-42a6-85a8-b5a047858020 |
| GET | /v1/operator/subscription | 200 |  | 0a2e923e-baf8-46e5-ae03-89bc51dce44b |
| POST | /v1/wallet/top-up | 201 |  | 78b1dc5d-cdad-4c7d-a55b-774e812517ea |
| POST | /v1/wallet/top-up | 201 |  | 78b1dc5d-cdad-4c7d-a55b-774e812517ea |
| POST | /v1/payments/vnpay-topup-ipn?vnp_Amount=10000000000&vnp_BankCode=NCB&vnp_PayDate=20260830022313&vnp_ResponseCode=00&vnp_TransactionNo=921788056593595&vnp_TransactionStatus=00&vnp_TmnCode=5J37I6CM&vnp_TxnRef=ed00ecdf-b181-4aa9-9695-2048e56e9e6f&vnp_SecureHash=bc7d40dddc3973a8bf121d1f008f7eea42cdc9938175c2f6f8f02197a899f4b358c518e4922d65a491b641b6d2d4e643680e9bde4557891dc0ebb7cd1c3d4c62 | 200 |  |  |
| GET | /v1/wallet | 200 |  | dbd435ca-2964-4c69-bc3f-fbd5d2deea40 |
| POST | /v1/operator/stations | 200 |  | 70a99cbb-73a1-4490-a298-229117dc51f4 |
| POST | /v1/operator/stations | 200 |  | f903fc18-8c8d-4a4e-a189-9c5014443cc9 |
| POST | /v1/operator/stations | 200 |  | 50600252-94cc-4cbf-b843-846e722307a2 |
| POST | /v1/operator/stations | 200 |  | 1a699c33-ed47-401e-81ae-cc63ba377b90 |
| POST | /v1/operator/stops | 201 |  | 475750d6-3629-4d92-aba8-1b54a2e89407 |
| POST | /v1/operator/stops | 201 |  | 8a74f463-ad9c-49a8-98c4-1e6450831ec1 |
| POST | /v1/operator/routes/full | 201 |  | 23f031d0-d1f4-40ed-aa62-6c53b3e16fe0 |
| GET | /v1/vehicle-types | 200 |  | 4a777897-be05-483e-b572-d1f00e15e36f |
| POST | /v1/operator/vehicles | 201 |  | 1dc56b8c-6937-408b-aefb-c118565859e4 |
| POST | /v1/operator/driver-schedules | 201 |  | 2c1a13da-33b5-40a5-a091-11381e8dab99 |
| GET | /v1/operator/trips?from=2026-08-30&to=2026-09-05&page=1&pageSize=20 | 200 |  | 42ea98ef-9e64-4242-be64-6fd0d682b290 |
| GET | /v1/operator/trips?from=2026-08-30&to=2026-09-05&page=1&pageSize=20 | 200 |  | 253998aa-34c5-4bd6-b2f5-32f4151ce565 |
| GET | /v1/operator/trips?from=2026-08-30&to=2026-09-05&page=1&pageSize=20 | 200 |  | 27fab2b4-27c6-482d-821a-8599f5ba6928 |
| GET | /v1/operator/trips?from=2026-08-30&to=2026-09-05&page=1&pageSize=20 | 200 |  | e2a2cacc-81ad-4f1a-b694-a3a03f6c8d1c |
| PUT | /v1/operator/policies/parcel-compensation | 200 |  | f8a447b5-245f-420c-a7d9-3ba5f21cb09c |
| POST | /v1/operator/parcel-route-fares | 201 |  | bc99165a-1307-4193-8ad8-bd5d830c491d |
| GET | /v1/parcels/available-trips?originStationId=276655b4-b208-4c9e-9a6a-5ff3c9c4276b&destinationStationId=5703c683-6d23-4006-adeb-a49c061e4632&departureDate=2026-08-30&lengthCm=20&widthCm=20&heightCm=20&estimatedWeightKg=2&page=1&pageSize=20 | 200 |  | e59a84e1-733a-4fd6-b02f-1347098295f9 |
| POST | /v1/bookings | 201 |  | 3f568190-e913-485a-be43-02e9021f1df4 |
| GET | /v1/bookings/62de9f30-2858-4da5-a711-53dc944b1e17 | 200 |  | e3c419a2-f956-40d7-9d2c-4515f4bba78c |
| POST | /v1/parcels | 201 |  | 75f6131e-598c-4119-bbb7-d2a3d087e4dc |
| POST | /v1/parcels | 201 |  | 75f6131e-598c-4119-bbb7-d2a3d087e4dc |
| POST | /v1/parcels/0d5ee6d0-3828-480e-88b4-8f5a38de06b4/deposit-payment | 200 |  | d770c26e-ae5b-46d9-8bc2-0e64b8e47dc6 |
| GET | /v1/parcels/0d5ee6d0-3828-480e-88b4-8f5a38de06b4 | 200 |  | d5e1e693-2990-42d1-bea9-6176073a0b8e |
| GET | /v1/parcels/0d5ee6d0-3828-480e-88b4-8f5a38de06b4 | 200 |  | 7eacfaba-c89a-42c7-84b8-0dc799d6e027 |
| GET | /v1/parcels/0d5ee6d0-3828-480e-88b4-8f5a38de06b4 | 200 |  | fd10efd5-0a37-4a94-9cc3-ce4728b421f3 |
| GET | /v1/parcels/0d5ee6d0-3828-480e-88b4-8f5a38de06b4 | 200 |  | 6d91bbea-2180-4415-b5a9-80a3a96ea101 |
| GET | /v1/parcels/0d5ee6d0-3828-480e-88b4-8f5a38de06b4 | 200 |  | 2d7c7307-719c-400d-93e6-308a3c6836b1 |
| GET | /v1/parcels/0d5ee6d0-3828-480e-88b4-8f5a38de06b4 | 200 |  | 94e4abc4-8dc5-4858-a7ac-207625d4a315 |
| GET | /v1/parcels/0d5ee6d0-3828-480e-88b4-8f5a38de06b4 | 200 |  | 42fbe0dc-5530-4230-ad6b-c6026ad0e09a |
| POST | /v1/assistant/parcels/0d5ee6d0-3828-480e-88b4-8f5a38de06b4/check-in | 200 |  | f8cc89af-42a2-482d-8d08-b68dfb472f2d |
| POST | /v1/assistant/parcels/0d5ee6d0-3828-480e-88b4-8f5a38de06b4/reweigh | 200 |  | fd496345-736b-454f-aedd-e2a05b5a9e0f |
| POST | /v1/parcels/0d5ee6d0-3828-480e-88b4-8f5a38de06b4/final-payment | 200 |  | 6c41a4cc-7c7a-4bbf-b215-a151cb8530ca |
| GET | /v1/parcels/0d5ee6d0-3828-480e-88b4-8f5a38de06b4 | 200 |  | 827922bf-c552-4f32-a39b-dd7bb577f07b |
| GET | /v1/parcels/0d5ee6d0-3828-480e-88b4-8f5a38de06b4 | 200 |  | 759673e3-18c4-4299-b849-13012018a494 |
| GET | /v1/parcels/0d5ee6d0-3828-480e-88b4-8f5a38de06b4 | 200 |  | e1ceb38a-f9d4-46bb-a28e-9d7f2627796a |
| GET | /v1/parcels/0d5ee6d0-3828-480e-88b4-8f5a38de06b4 | 200 |  | d0142ef7-0e06-4101-93b2-971200215051 |
| GET | /v1/parcels/0d5ee6d0-3828-480e-88b4-8f5a38de06b4 | 200 |  | e9ae4234-9b30-47d1-9408-8f01e0f43cab |
| GET | /v1/parcels/0d5ee6d0-3828-480e-88b4-8f5a38de06b4 | 200 |  | bfef5351-a466-45d8-8b2e-c526a572c945 |
| GET | /v1/parcels/0d5ee6d0-3828-480e-88b4-8f5a38de06b4 | 200 |  | f366e412-f8bb-4335-bbbc-4b36dab63865 |
| GET | /v1/parcels/0d5ee6d0-3828-480e-88b4-8f5a38de06b4 | 200 |  | 2d580db0-2363-4d2f-8013-0fee88566377 |
| GET | /v1/parcels/0d5ee6d0-3828-480e-88b4-8f5a38de06b4 | 200 |  | 5fcda160-96f6-4d17-ac09-ad68a7744428 |
| GET | /v1/parcels/0d5ee6d0-3828-480e-88b4-8f5a38de06b4 | 200 |  | f6e9c5eb-abee-4756-852c-8774e49060de |
| POST | /v1/assistant/parcels/0d5ee6d0-3828-480e-88b4-8f5a38de06b4/load | 200 |  | 75bc7e89-f104-4a57-9e61-502b982fedd2 |
| POST | /v1/parcels | 201 |  | 20b35769-0460-487b-b7f4-6dc1434aa287 |
| POST | /v1/parcels | 201 |  | 20b35769-0460-487b-b7f4-6dc1434aa287 |
| POST | /v1/parcels/309aa4fc-333a-4b7a-90af-23f9b95cb065/deposit-payment | 200 |  | 94fa2afc-0056-4031-b3cf-8c57902e65ed |
| GET | /v1/parcels/309aa4fc-333a-4b7a-90af-23f9b95cb065 | 200 |  | 51ef3e73-a7e6-4f1f-8cd2-ea488d4f33a3 |
| GET | /v1/parcels/309aa4fc-333a-4b7a-90af-23f9b95cb065 | 200 |  | 122de22e-2b78-4038-807d-f6b31b0650cf |
| GET | /v1/parcels/309aa4fc-333a-4b7a-90af-23f9b95cb065 | 200 |  | d85b8966-9b75-4c9c-973d-5ac0ef89f8cd |
| GET | /v1/parcels/309aa4fc-333a-4b7a-90af-23f9b95cb065 | 200 |  | 3e22450f-ee2c-4394-a9b2-b29c2004cf1d |
| GET | /v1/parcels/309aa4fc-333a-4b7a-90af-23f9b95cb065 | 200 |  | b70faccf-416e-45f8-91b5-2a689692c3a5 |
| GET | /v1/parcels/309aa4fc-333a-4b7a-90af-23f9b95cb065 | 200 |  | e3740b17-91da-4b7b-bb84-14529dcf65b7 |
| GET | /v1/parcels/309aa4fc-333a-4b7a-90af-23f9b95cb065 | 200 |  | a0362a4b-0470-4ebf-b967-a4a9fb2266a5 |
| GET | /v1/parcels/309aa4fc-333a-4b7a-90af-23f9b95cb065 | 200 |  | ba09ce5f-ebe6-4b23-8c94-16b6ec53fca8 |
| GET | /v1/parcels/309aa4fc-333a-4b7a-90af-23f9b95cb065 | 200 |  | fb32a710-6449-4a48-94ad-5d081e5facd8 |
| POST | /v1/assistant/parcels/309aa4fc-333a-4b7a-90af-23f9b95cb065/check-in | 200 |  | f438fa3f-c186-4d05-8aeb-2e4746513af4 |
| POST | /v1/assistant/parcels/309aa4fc-333a-4b7a-90af-23f9b95cb065/reweigh | 200 |  | 582b44d8-62c3-45ec-95d6-803fd9b4564f |
| POST | /v1/parcels/309aa4fc-333a-4b7a-90af-23f9b95cb065/final-payment | 200 |  | 798cdf7d-b314-4e6d-b32a-d2e11da88923 |
| GET | /v1/parcels/309aa4fc-333a-4b7a-90af-23f9b95cb065 | 200 |  | dee42f03-0662-498c-a59c-116ad6ce7eec |
| GET | /v1/parcels/309aa4fc-333a-4b7a-90af-23f9b95cb065 | 200 |  | aa2c82b7-c85f-4316-b637-ad56f8fb3b76 |
| GET | /v1/parcels/309aa4fc-333a-4b7a-90af-23f9b95cb065 | 200 |  | c2a81a7b-6b7a-4bda-b362-bb55f15df628 |
| GET | /v1/parcels/309aa4fc-333a-4b7a-90af-23f9b95cb065 | 200 |  | 3f0295f0-9945-4604-99fe-127d62bc5f17 |
| GET | /v1/parcels/309aa4fc-333a-4b7a-90af-23f9b95cb065 | 200 |  | 93ba0e8e-38ed-4ab7-8e33-66da16efce5b |
| GET | /v1/parcels/309aa4fc-333a-4b7a-90af-23f9b95cb065 | 200 |  | 993a8988-e18d-42ad-a464-18aa118b69be |
| GET | /v1/parcels/309aa4fc-333a-4b7a-90af-23f9b95cb065 | 200 |  | f781ecd3-bd5d-48d4-8ec0-cc50d0152a36 |
| GET | /v1/parcels/309aa4fc-333a-4b7a-90af-23f9b95cb065 | 200 |  | f9d83585-7e87-4964-9c77-04b559217e8c |
| GET | /v1/parcels/309aa4fc-333a-4b7a-90af-23f9b95cb065 | 200 |  | e105237e-b4fb-415b-b811-872ae7190c6b |
| GET | /v1/parcels/309aa4fc-333a-4b7a-90af-23f9b95cb065 | 200 |  | ddf1e948-ef97-441e-a21d-368918503a6d |
| POST | /v1/assistant/parcels/309aa4fc-333a-4b7a-90af-23f9b95cb065/load | 200 |  | 9d488c57-d493-4062-b41c-0d3af41e655b |
| POST | /v1/parcels | 201 |  | e3fd3100-ccb1-41eb-a82b-d754b83a2018 |
| POST | /v1/parcels | 201 |  | e3fd3100-ccb1-41eb-a82b-d754b83a2018 |
| POST | /v1/parcels/fc4e4489-444c-4d3d-a586-7138c650e807/deposit-payment | 200 |  | c55144be-aa8c-4fc9-a9ec-befdef6ce3d5 |
| GET | /v1/parcels/fc4e4489-444c-4d3d-a586-7138c650e807 | 200 |  | 3b9c9580-4f7d-4aee-a19c-8b7923ed54c0 |
| GET | /v1/parcels/fc4e4489-444c-4d3d-a586-7138c650e807 | 200 |  | 6066fd5e-5ba1-4918-8bce-a964d8c41f77 |
| GET | /v1/parcels/fc4e4489-444c-4d3d-a586-7138c650e807 | 200 |  | d1548c55-081f-419a-ac12-b395c5e19d09 |
| GET | /v1/parcels/fc4e4489-444c-4d3d-a586-7138c650e807 | 200 |  | 94eef8e1-4874-4fe1-827e-ae627a1ecab5 |
| GET | /v1/parcels/fc4e4489-444c-4d3d-a586-7138c650e807 | 200 |  | 65049f9a-72c9-4eba-a17a-2c39616b5e86 |
| GET | /v1/parcels/fc4e4489-444c-4d3d-a586-7138c650e807 | 200 |  | 0545a868-12a9-45d1-b1c7-d63fa8fb6033 |
| GET | /v1/parcels/fc4e4489-444c-4d3d-a586-7138c650e807 | 200 |  | 21596ba3-9507-4db4-bbea-f99ae6c9125f |
| GET | /v1/parcels/fc4e4489-444c-4d3d-a586-7138c650e807 | 200 |  | 57f357d2-25ad-47b9-b9d7-d23e96738113 |
| GET | /v1/parcels/fc4e4489-444c-4d3d-a586-7138c650e807 | 200 |  | be01728f-4b0b-4080-a49e-167f49ee11bd |
| GET | /v1/parcels/fc4e4489-444c-4d3d-a586-7138c650e807 | 200 |  | 636576a2-e759-4de3-8d7d-e24ac631e185 |
| POST | /v1/assistant/parcels/fc4e4489-444c-4d3d-a586-7138c650e807/check-in | 200 |  | b7963fac-867e-436c-91b9-6c607a45a374 |
| POST | /v1/assistant/parcels/fc4e4489-444c-4d3d-a586-7138c650e807/reweigh | 200 |  | 93120565-2b6c-453f-b1a6-64f80db21741 |
| POST | /v1/parcels/fc4e4489-444c-4d3d-a586-7138c650e807/final-payment | 200 |  | b5862cad-cc71-4120-9cde-556a1750f954 |
| GET | /v1/parcels/fc4e4489-444c-4d3d-a586-7138c650e807 | 200 |  | 4fc37351-fb58-41a5-b09e-8b2458b9fcf7 |
| GET | /v1/parcels/fc4e4489-444c-4d3d-a586-7138c650e807 | 200 |  | 0e5cb35b-88d2-4aeb-9779-4cad520f1fed |
| GET | /v1/parcels/fc4e4489-444c-4d3d-a586-7138c650e807 | 200 |  | 0566f65f-4b01-4085-bcaa-6599f0015a1c |
| GET | /v1/parcels/fc4e4489-444c-4d3d-a586-7138c650e807 | 200 |  | 78cf2bed-5d18-492a-a96a-6cd9911dc163 |
| GET | /v1/parcels/fc4e4489-444c-4d3d-a586-7138c650e807 | 200 |  | a6abfc8b-ab39-4f6d-9570-b487d6c15f6f |
| GET | /v1/parcels/fc4e4489-444c-4d3d-a586-7138c650e807 | 200 |  | c7398bd4-2ac6-450d-b644-f731f3b0d9e0 |
| GET | /v1/parcels/fc4e4489-444c-4d3d-a586-7138c650e807 | 200 |  | 155e7e57-fc6b-41eb-9a5a-6a44a9e43114 |
| GET | /v1/parcels/fc4e4489-444c-4d3d-a586-7138c650e807 | 200 |  | 689df9e0-4de6-4db0-a8f7-0b49d9e269d3 |
| GET | /v1/parcels/fc4e4489-444c-4d3d-a586-7138c650e807 | 200 |  | b8487c61-a370-4160-a4b2-350e713cbb62 |
| POST | /v1/assistant/parcels/fc4e4489-444c-4d3d-a586-7138c650e807/load | 200 |  | 5dbb071f-ce9d-44d8-aadc-8d395da63b97 |
| POST | /v1/parcels | 201 |  | 9f89652b-87d8-449e-a021-e5b185c1647f |
| POST | /v1/parcels | 201 |  | 9f89652b-87d8-449e-a021-e5b185c1647f |
| POST | /v1/parcels/1e94b393-fd68-42e8-a88a-86e37a510ffd/deposit-payment | 200 |  | 7b18ffd6-64d1-421c-8c18-6331f8329b70 |
| GET | /v1/parcels/1e94b393-fd68-42e8-a88a-86e37a510ffd | 200 |  | fc016df4-b6dd-4763-a652-c040844deefb |
| GET | /v1/parcels/1e94b393-fd68-42e8-a88a-86e37a510ffd | 200 |  | 0e99ed6c-f7b6-428a-9589-9e94189a3be6 |
| GET | /v1/parcels/1e94b393-fd68-42e8-a88a-86e37a510ffd | 200 |  | d566dcef-d94d-436e-aab7-fdd5140ab6f6 |
| GET | /v1/parcels/1e94b393-fd68-42e8-a88a-86e37a510ffd | 200 |  | 89515135-9d85-4f7e-a8a4-171a9df3100f |
| GET | /v1/parcels/1e94b393-fd68-42e8-a88a-86e37a510ffd | 200 |  | 3f55596c-106c-4f38-9235-8e27f2a658c8 |
| GET | /v1/parcels/1e94b393-fd68-42e8-a88a-86e37a510ffd | 200 |  | b5621ecc-8d52-4d54-b456-b111ffe6eebc |
| GET | /v1/parcels/1e94b393-fd68-42e8-a88a-86e37a510ffd | 200 |  | c2756676-46dc-470f-a254-6b6490303738 |
| GET | /v1/parcels/1e94b393-fd68-42e8-a88a-86e37a510ffd | 200 |  | 3780a3ae-930c-4e6d-a09d-f1b5db2d2a5b |
| GET | /v1/parcels/1e94b393-fd68-42e8-a88a-86e37a510ffd | 200 |  | 83aabed1-f30c-4577-a0ba-60f9e4ab9638 |
| GET | /v1/parcels/1e94b393-fd68-42e8-a88a-86e37a510ffd | 200 |  | 1b7c52a6-2764-4c45-ad97-db18e5e614b6 |
| POST | /v1/assistant/parcels/1e94b393-fd68-42e8-a88a-86e37a510ffd/check-in | 200 |  | 58e39a01-81b6-4340-a8c3-8cd82cf19d7f |
| POST | /v1/assistant/parcels/1e94b393-fd68-42e8-a88a-86e37a510ffd/reweigh | 200 |  | b98679c0-741b-4fda-adf5-9a40dcadfd44 |
| POST | /v1/parcels/1e94b393-fd68-42e8-a88a-86e37a510ffd/final-payment | 200 |  | 45863f20-c97c-4ae0-9928-3128311a2579 |
| GET | /v1/parcels/1e94b393-fd68-42e8-a88a-86e37a510ffd | 200 |  | f443c136-98e9-4e38-8e5b-092bce8f7b42 |
| GET | /v1/parcels/1e94b393-fd68-42e8-a88a-86e37a510ffd | 200 |  | 28f2be1d-fd5d-44e7-b5f8-6c5cab5ea537 |
| GET | /v1/parcels/1e94b393-fd68-42e8-a88a-86e37a510ffd | 200 |  | 2d4056c8-b1a9-4a02-a8c1-8cc70bb84d85 |
| GET | /v1/parcels/1e94b393-fd68-42e8-a88a-86e37a510ffd | 200 |  | f5ba1539-95b2-4c07-93cc-7a949717081c |
| GET | /v1/parcels/1e94b393-fd68-42e8-a88a-86e37a510ffd | 200 |  | 6873c4d7-3be6-4b2e-8ae4-9938ba4dd4ea |
| GET | /v1/parcels/1e94b393-fd68-42e8-a88a-86e37a510ffd | 200 |  | 212014b4-944a-49bd-a4c2-5293ed72f3ae |
| GET | /v1/parcels/1e94b393-fd68-42e8-a88a-86e37a510ffd | 200 |  | da7c08be-8dcf-4134-a1e6-59b14e62e160 |
| GET | /v1/parcels/1e94b393-fd68-42e8-a88a-86e37a510ffd | 200 |  | 17d7a6d1-5484-4b65-8fd1-dd631d2e1055 |
| GET | /v1/parcels/1e94b393-fd68-42e8-a88a-86e37a510ffd | 200 |  | e0bd33d8-9356-4651-8ac8-2cfe04fa2437 |
| GET | /v1/parcels/1e94b393-fd68-42e8-a88a-86e37a510ffd | 200 |  | 05b34100-8ac9-40ef-a316-c20405332474 |
| POST | /v1/assistant/parcels/1e94b393-fd68-42e8-a88a-86e37a510ffd/load | 200 |  | 4ccaaa8d-b7d3-4d36-98ef-dd83ce56cd1d |
| POST | /v1/parcels | 201 |  | ac23f8ec-bf4c-48ad-8f84-e85501e5997d |
| POST | /v1/parcels | 201 |  | ac23f8ec-bf4c-48ad-8f84-e85501e5997d |
| POST | /v1/parcels/7da3bf87-2de6-432e-8900-485c10ed25be/deposit-payment | 200 |  | 5df7ba50-799e-4ca4-ba0a-5c9a6113cc1f |
| GET | /v1/parcels/7da3bf87-2de6-432e-8900-485c10ed25be | 200 |  | 070d5533-97b1-4fec-960d-66406aa994f6 |
| GET | /v1/parcels/7da3bf87-2de6-432e-8900-485c10ed25be | 200 |  | 559523d5-5bb8-4a17-b65d-e0fde184b567 |
| GET | /v1/parcels/7da3bf87-2de6-432e-8900-485c10ed25be | 200 |  | 0e727858-525d-454b-94c5-10c6d93cc21b |
| GET | /v1/parcels/7da3bf87-2de6-432e-8900-485c10ed25be | 200 |  | 9e6036e2-7bec-4ecc-a45a-a3fc67b42da1 |
| GET | /v1/parcels/7da3bf87-2de6-432e-8900-485c10ed25be | 200 |  | 5a9fa6af-f94d-4da5-9d3e-4b410b0f7702 |
| GET | /v1/parcels/7da3bf87-2de6-432e-8900-485c10ed25be | 200 |  | ececb0f9-9e06-4ae0-aabc-139b3bccbe7f |
| GET | /v1/parcels/7da3bf87-2de6-432e-8900-485c10ed25be | 200 |  | 023561be-3267-4765-8c27-021f2b37966e |
| GET | /v1/parcels/7da3bf87-2de6-432e-8900-485c10ed25be | 200 |  | 07a9472d-9f71-4bce-8056-140974b2f4c8 |
| GET | /v1/parcels/7da3bf87-2de6-432e-8900-485c10ed25be | 200 |  | cf38c47a-e474-445f-b3a7-c0a8647dded8 |
| GET | /v1/parcels/7da3bf87-2de6-432e-8900-485c10ed25be | 200 |  | 8a5d58b3-f872-416b-8cf4-a0a732e919e9 |
| POST | /v1/assistant/parcels/7da3bf87-2de6-432e-8900-485c10ed25be/check-in | 200 |  | 96a0c6e8-af46-4403-9529-fe4b8dc105b7 |
| POST | /v1/assistant/parcels/7da3bf87-2de6-432e-8900-485c10ed25be/reweigh | 200 |  | 0f4f167c-b186-43e8-9e40-415fa82e5349 |
| POST | /v1/parcels/7da3bf87-2de6-432e-8900-485c10ed25be/final-payment | 200 |  | 526d3872-5b0a-46ee-8b0a-4343aa4d65c0 |
| GET | /v1/parcels/7da3bf87-2de6-432e-8900-485c10ed25be | 200 |  | 0c316a0d-b0c7-4df1-a8d3-fffa834249a1 |
| GET | /v1/parcels/7da3bf87-2de6-432e-8900-485c10ed25be | 200 |  | 79e87596-41bd-4c81-a676-cbf45f0a13e9 |
| GET | /v1/parcels/7da3bf87-2de6-432e-8900-485c10ed25be | 200 |  | 5301490f-971f-479d-ac02-c91dc0c5b4c3 |
| GET | /v1/parcels/7da3bf87-2de6-432e-8900-485c10ed25be | 200 |  | f35a302a-a1ab-4021-adf6-074f28428c00 |
| GET | /v1/parcels/7da3bf87-2de6-432e-8900-485c10ed25be | 200 |  | c9778ee2-a8d1-4228-9f86-5bef47f4c50c |
| GET | /v1/parcels/7da3bf87-2de6-432e-8900-485c10ed25be | 200 |  | 7a2321e7-bd41-4f21-b2bd-87fa803653ee |
| GET | /v1/parcels/7da3bf87-2de6-432e-8900-485c10ed25be | 200 |  | 80900914-9d9a-4b31-a474-e4bfb658970e |
| GET | /v1/parcels/7da3bf87-2de6-432e-8900-485c10ed25be | 200 |  | 5feb37af-7f3f-464e-a215-10a32cbc60c2 |
| GET | /v1/parcels/7da3bf87-2de6-432e-8900-485c10ed25be | 200 |  | 69e7250f-cc04-4941-98ea-f4ce6d4da238 |
| GET | /v1/parcels/7da3bf87-2de6-432e-8900-485c10ed25be | 200 |  | eb4b34db-a1ae-4cbb-872f-67d3a0e714c1 |
| POST | /v1/assistant/parcels/7da3bf87-2de6-432e-8900-485c10ed25be/load | 200 |  | 4a33178b-7c71-4347-9542-c2121a5b2456 |
| POST | /v1/parcels | 201 |  | 865ec5d2-d256-4294-a8f9-8a189bf34a83 |
| POST | /v1/parcels | 201 |  | 865ec5d2-d256-4294-a8f9-8a189bf34a83 |
| POST | /v1/parcels/2c0215ff-e0e9-4f92-b930-59efa4246279/deposit-payment | 200 |  | 28be771a-ed8c-4abd-aaac-40a49c5e0aa2 |
| GET | /v1/parcels/2c0215ff-e0e9-4f92-b930-59efa4246279 | 200 |  | 2d2e4ebe-87cd-4ec1-8199-eadcfa18248f |
| GET | /v1/parcels/2c0215ff-e0e9-4f92-b930-59efa4246279 | 200 |  | a4d4853a-5b5f-4472-9e59-866b86a631be |
| GET | /v1/parcels/2c0215ff-e0e9-4f92-b930-59efa4246279 | 200 |  | 55896db4-6b93-4e38-8b18-1ee6c0349671 |
| GET | /v1/parcels/2c0215ff-e0e9-4f92-b930-59efa4246279 | 200 |  | 78c8b469-8d0e-4a6e-8ad9-6fbcedfba093 |
| GET | /v1/parcels/2c0215ff-e0e9-4f92-b930-59efa4246279 | 200 |  | 1e292743-6b25-42e1-afb6-b2f2f0b44297 |
| GET | /v1/parcels/2c0215ff-e0e9-4f92-b930-59efa4246279 | 200 |  | 6d694692-dc29-460d-b900-ce8e36a18026 |
| GET | /v1/parcels/2c0215ff-e0e9-4f92-b930-59efa4246279 | 200 |  | 8f653237-7195-48a1-9972-6c965ce54998 |
| GET | /v1/parcels/2c0215ff-e0e9-4f92-b930-59efa4246279 | 200 |  | 35dc9be4-8945-447b-9179-e3f9532954fa |
| GET | /v1/parcels/2c0215ff-e0e9-4f92-b930-59efa4246279 | 200 |  | f679919b-c771-42d2-b4e2-b1b88a5b2662 |
| GET | /v1/parcels/2c0215ff-e0e9-4f92-b930-59efa4246279 | 200 |  | 22c075d1-42de-45bc-afd3-dab0a5d2de1d |
| POST | /v1/assistant/parcels/2c0215ff-e0e9-4f92-b930-59efa4246279/check-in | 200 |  | 2b290f0a-2f8f-48be-8a70-38470d7743cd |
| POST | /v1/assistant/parcels/2c0215ff-e0e9-4f92-b930-59efa4246279/reweigh | 200 |  | d1d619e7-2f54-4fb7-90b2-e9ebb0b4fe7b |
| POST | /v1/parcels/2c0215ff-e0e9-4f92-b930-59efa4246279/final-payment | 200 |  | 3ac61d20-f9eb-495a-82bd-c7b51932e044 |
| GET | /v1/parcels/2c0215ff-e0e9-4f92-b930-59efa4246279 | 200 |  | 49077edb-ceeb-4e76-9968-1f69c1dc527f |
| GET | /v1/parcels/2c0215ff-e0e9-4f92-b930-59efa4246279 | 200 |  | 92a130d6-9039-4efb-b2ed-9472dd3d3982 |
| GET | /v1/parcels/2c0215ff-e0e9-4f92-b930-59efa4246279 | 200 |  | 0c2cee0f-cf65-4d94-be14-3ae6cc3d7b9a |
| GET | /v1/parcels/2c0215ff-e0e9-4f92-b930-59efa4246279 | 200 |  | 34cc43cf-629f-413f-a8ba-18ed037ec47d |
| GET | /v1/parcels/2c0215ff-e0e9-4f92-b930-59efa4246279 | 200 |  | 24ef8674-40a3-48ee-bd26-88747538199c |
| GET | /v1/parcels/2c0215ff-e0e9-4f92-b930-59efa4246279 | 200 |  | fd5cb345-218b-4d12-933f-acf238779e90 |
| GET | /v1/parcels/2c0215ff-e0e9-4f92-b930-59efa4246279 | 200 |  | 0f1c0d84-1736-47a9-8641-a03e61cef19a |
| GET | /v1/parcels/2c0215ff-e0e9-4f92-b930-59efa4246279 | 200 |  | 5912d601-ca10-4e49-a94d-1325c4dfcbdf |
| GET | /v1/parcels/2c0215ff-e0e9-4f92-b930-59efa4246279 | 200 |  | 9e5e1327-3138-4e55-a6dd-66518a4e870f |
| GET | /v1/parcels/2c0215ff-e0e9-4f92-b930-59efa4246279 | 200 |  | e77bf3ef-73e9-4336-839e-726e042a9c88 |
| POST | /v1/assistant/parcels/2c0215ff-e0e9-4f92-b930-59efa4246279/load | 200 |  | e80d42ff-d45f-420b-9a90-e622a75b213e |
| POST | /v1/parcels | 201 |  | 2a2340b3-eb33-4341-9d32-9ff724aef065 |
| POST | /v1/parcels | 201 |  | 2a2340b3-eb33-4341-9d32-9ff724aef065 |
| POST | /v1/parcels/f9dd8d3a-6695-49fc-9d26-1efe1f4db956/deposit-payment | 200 |  | 7eec79de-2734-437a-af0b-c665b20586bb |
| GET | /v1/parcels/f9dd8d3a-6695-49fc-9d26-1efe1f4db956 | 200 |  | e8b7e2b4-41ae-4bf7-9be5-55781b0b0023 |
| GET | /v1/parcels/f9dd8d3a-6695-49fc-9d26-1efe1f4db956 | 200 |  | 53e697cc-05cd-4319-b238-22b51ce41841 |
| GET | /v1/parcels/f9dd8d3a-6695-49fc-9d26-1efe1f4db956 | 200 |  | 057bb34b-b450-4ee5-930f-1c66e9afb7b6 |
| GET | /v1/parcels/f9dd8d3a-6695-49fc-9d26-1efe1f4db956 | 200 |  | b80bf6ea-9bac-499c-abfc-5a8a62305044 |
| GET | /v1/parcels/f9dd8d3a-6695-49fc-9d26-1efe1f4db956 | 200 |  | 8ca22b2b-8eb7-4334-a311-75dd4411d8ea |
| GET | /v1/parcels/f9dd8d3a-6695-49fc-9d26-1efe1f4db956 | 200 |  | 6cc9b5d6-7dee-4867-b9cc-7d6383e99a62 |
| GET | /v1/parcels/f9dd8d3a-6695-49fc-9d26-1efe1f4db956 | 200 |  | d8da4aeb-79bc-4986-a0c7-b36c5ff5c9fb |
| GET | /v1/parcels/f9dd8d3a-6695-49fc-9d26-1efe1f4db956 | 200 |  | d644ffc4-85bf-4d24-b53e-e0510d642945 |
| GET | /v1/parcels/f9dd8d3a-6695-49fc-9d26-1efe1f4db956 | 200 |  | a714ab5a-8429-4907-ae6c-58b8c7c09ffa |
| GET | /v1/parcels/f9dd8d3a-6695-49fc-9d26-1efe1f4db956 | 200 |  | 9b1ad4e5-c308-4a80-95c4-f04b8243f0b9 |
| POST | /v1/assistant/parcels/f9dd8d3a-6695-49fc-9d26-1efe1f4db956/check-in | 200 |  | 185b6166-3a3a-416b-84ae-1ca7cd4d8069 |
| POST | /v1/assistant/parcels/f9dd8d3a-6695-49fc-9d26-1efe1f4db956/reweigh | 200 |  | cb378b31-f479-438e-98a5-cc3b4e41315b |
| POST | /v1/parcels/f9dd8d3a-6695-49fc-9d26-1efe1f4db956/final-payment | 200 |  | 880ced76-6b35-4e9f-8851-2b1f8294c62b |
| GET | /v1/parcels/f9dd8d3a-6695-49fc-9d26-1efe1f4db956 | 200 |  | b7e353e9-8b8c-410f-be28-75b42574ee6b |
| GET | /v1/parcels/f9dd8d3a-6695-49fc-9d26-1efe1f4db956 | 200 |  | e330567a-fba0-4dfa-9b88-ef3fc3f50686 |
| GET | /v1/parcels/f9dd8d3a-6695-49fc-9d26-1efe1f4db956 | 200 |  | 8c0c9503-a5ac-4754-9b75-075d5d97d949 |
| GET | /v1/parcels/f9dd8d3a-6695-49fc-9d26-1efe1f4db956 | 200 |  | 0f9be28b-df4e-47f0-b6d1-facd6d3e197c |
| GET | /v1/parcels/f9dd8d3a-6695-49fc-9d26-1efe1f4db956 | 200 |  | 24cc388f-6f7d-4639-8538-e5845bedf65b |
| GET | /v1/parcels/f9dd8d3a-6695-49fc-9d26-1efe1f4db956 | 200 |  | ea5d1b08-b1e5-4b69-abe2-4dcdd18f8cbe |
| GET | /v1/parcels/f9dd8d3a-6695-49fc-9d26-1efe1f4db956 | 200 |  | a6785a61-3491-4797-bc24-39c0c6107226 |
| GET | /v1/parcels/f9dd8d3a-6695-49fc-9d26-1efe1f4db956 | 200 |  | 006e01a2-5ad8-4f7b-a0a1-149b3f85f8b3 |
| GET | /v1/parcels/f9dd8d3a-6695-49fc-9d26-1efe1f4db956 | 200 |  | 5a532af4-570c-4883-ba96-d690d1e26125 |
| GET | /v1/parcels/f9dd8d3a-6695-49fc-9d26-1efe1f4db956 | 200 |  | 487faa6d-5d90-4df6-8078-7bc0c7204c25 |
| POST | /v1/parcels | 201 |  | 52c62f4c-dda0-48e5-8b39-b61713e7e7cc |
| POST | /v1/parcels | 201 |  | 52c62f4c-dda0-48e5-8b39-b61713e7e7cc |
| POST | /v1/parcels/fecf7dac-35e5-42b9-b150-c5b8465ad17f/deposit-payment | 200 |  | 44de18bd-faa5-4a2a-abcd-d61f078aceae |
| GET | /v1/parcels/fecf7dac-35e5-42b9-b150-c5b8465ad17f | 200 |  | d428f7da-bb00-451a-9746-ee6de0201144 |
| GET | /v1/parcels/fecf7dac-35e5-42b9-b150-c5b8465ad17f | 200 |  | a985fdb0-5177-4218-9711-bdbe0a8dd1c7 |
| GET | /v1/parcels/fecf7dac-35e5-42b9-b150-c5b8465ad17f | 200 |  | 8065aeb4-7e1c-4b3c-a473-432998e2ceb2 |
| GET | /v1/parcels/fecf7dac-35e5-42b9-b150-c5b8465ad17f | 200 |  | 5a6c61b4-b2aa-4ef4-b2b3-47f50f3d6e8d |
| GET | /v1/parcels/fecf7dac-35e5-42b9-b150-c5b8465ad17f | 200 |  | 21b81c81-07eb-4101-b550-aa7c1fb7a7e5 |
| GET | /v1/parcels/fecf7dac-35e5-42b9-b150-c5b8465ad17f | 200 |  | 94ba0f17-b466-43d9-a877-7a100aaeb767 |
| GET | /v1/parcels/fecf7dac-35e5-42b9-b150-c5b8465ad17f | 200 |  | 41566ccc-c326-4089-81b2-03e36ee73ce7 |
| GET | /v1/parcels/fecf7dac-35e5-42b9-b150-c5b8465ad17f | 200 |  | 48717449-6784-4f99-87d3-f71ce1e22fd4 |
| GET | /v1/parcels/fecf7dac-35e5-42b9-b150-c5b8465ad17f | 200 |  | 9a7c488b-373f-44e2-804c-864055a65c93 |
| GET | /v1/parcels/fecf7dac-35e5-42b9-b150-c5b8465ad17f | 200 |  | 2fce6ab2-3ebb-435c-892b-baf3df0c1420 |
| POST | /v1/assistant/parcels/fecf7dac-35e5-42b9-b150-c5b8465ad17f/check-in | 200 |  | 1fd98469-7c7a-44e0-992e-77e91c56736b |
| POST | /v1/assistant/parcels/fecf7dac-35e5-42b9-b150-c5b8465ad17f/reweigh | 200 |  | af727847-0505-40af-873a-4a023a6c268d |
| POST | /v1/parcels/fecf7dac-35e5-42b9-b150-c5b8465ad17f/final-payment | 200 |  | 7d2a33aa-95b2-462f-ade7-b68cfcf3d847 |
| GET | /v1/parcels/fecf7dac-35e5-42b9-b150-c5b8465ad17f | 200 |  | 7f33eecc-c4a5-463b-b0b8-0adf5df34eff |
| GET | /v1/parcels/fecf7dac-35e5-42b9-b150-c5b8465ad17f | 200 |  | 61894545-886b-42e2-8a20-82e8086a6117 |
| GET | /v1/parcels/fecf7dac-35e5-42b9-b150-c5b8465ad17f | 200 |  | 3c760712-691b-4137-b4e7-ba221099b4cf |
| GET | /v1/parcels/fecf7dac-35e5-42b9-b150-c5b8465ad17f | 200 |  | 8f5fd40f-60e7-412f-b2fe-de0bb624a1de |
| GET | /v1/parcels/fecf7dac-35e5-42b9-b150-c5b8465ad17f | 200 |  | 5102d8eb-43a7-4079-b994-ed60af12d690 |
| GET | /v1/parcels/fecf7dac-35e5-42b9-b150-c5b8465ad17f | 200 |  | 18e397e7-9cea-4131-b3fe-b5052c2bdb6b |
| GET | /v1/parcels/fecf7dac-35e5-42b9-b150-c5b8465ad17f | 200 |  | 51684bb9-3e13-49ba-b365-7343889efe79 |
| GET | /v1/parcels/fecf7dac-35e5-42b9-b150-c5b8465ad17f | 200 |  | d3e4ca36-f64a-4911-bebd-da2ee048d3d6 |
| GET | /v1/parcels/fecf7dac-35e5-42b9-b150-c5b8465ad17f | 200 |  | 7141173e-58fd-4ea6-ba89-02d757f8b74d |
| GET | /v1/parcels/fecf7dac-35e5-42b9-b150-c5b8465ad17f | 200 |  | 2ccba403-dc05-4b44-a55c-35002ab5980d |
| GET | /v1/parcels/fecf7dac-35e5-42b9-b150-c5b8465ad17f | 200 |  | ca2d6475-a333-4d88-b9b5-7372f1c77625 |
| POST | /v1/parcels | 201 |  | 10e4e1cc-012e-4982-8e2d-e8dd9712c36b |
| POST | /v1/parcels | 201 |  | 10e4e1cc-012e-4982-8e2d-e8dd9712c36b |
| POST | /v1/parcels/c75559cb-df9c-4391-b0d1-5802ed6eb9d8/deposit-payment | 200 |  | cef931cd-9d05-4ce5-9577-724696d30d3c |
| GET | /v1/parcels/c75559cb-df9c-4391-b0d1-5802ed6eb9d8 | 200 |  | 88697e71-2df4-42c2-83ba-48cbd7a71e6e |
| GET | /v1/parcels/c75559cb-df9c-4391-b0d1-5802ed6eb9d8 | 200 |  | bcdb73b8-c759-4d7e-991b-0fe25c32d069 |
| GET | /v1/parcels/c75559cb-df9c-4391-b0d1-5802ed6eb9d8 | 200 |  | 680d7710-ba16-4b1b-8bd4-64000e508dd2 |
| GET | /v1/parcels/c75559cb-df9c-4391-b0d1-5802ed6eb9d8 | 200 |  | 11f82035-0bfc-427f-8345-baff82d0f373 |
| GET | /v1/parcels/c75559cb-df9c-4391-b0d1-5802ed6eb9d8 | 200 |  | a459ea72-268b-4e60-89e7-8462fa6d1852 |
| GET | /v1/parcels/c75559cb-df9c-4391-b0d1-5802ed6eb9d8 | 200 |  | f021589c-f362-477d-aa98-a1f154da7128 |
| GET | /v1/parcels/c75559cb-df9c-4391-b0d1-5802ed6eb9d8 | 200 |  | 8691d961-6c5f-4e0b-a092-473a4f982353 |
| GET | /v1/parcels/c75559cb-df9c-4391-b0d1-5802ed6eb9d8 | 200 |  | 19949427-b94c-4442-b15d-aa5a8e48d16e |
| GET | /v1/parcels/c75559cb-df9c-4391-b0d1-5802ed6eb9d8 | 200 |  | c90b9b09-02bb-4830-b80b-81247d2460e7 |
| GET | /v1/parcels/c75559cb-df9c-4391-b0d1-5802ed6eb9d8 | 200 |  | e2b345ca-3194-421c-a2a7-dcc9e41d622a |
| POST | /v1/assistant/parcels/c75559cb-df9c-4391-b0d1-5802ed6eb9d8/check-in | 200 |  | c21c15f1-10d1-4952-9cc5-c01c9a14186d |
| POST | /v1/assistant/parcels/c75559cb-df9c-4391-b0d1-5802ed6eb9d8/reweigh | 200 |  | a3e8c245-0e1d-4b6a-9980-eca9a036253b |
| POST | /v1/parcels/c75559cb-df9c-4391-b0d1-5802ed6eb9d8/final-payment | 200 |  | c3277e50-19b9-4c99-a91a-35c5ebb04ce5 |
| GET | /v1/parcels/c75559cb-df9c-4391-b0d1-5802ed6eb9d8 | 200 |  | 54249770-e492-4aea-a5c0-a93a0ce14e11 |
| GET | /v1/parcels/c75559cb-df9c-4391-b0d1-5802ed6eb9d8 | 200 |  | 011bd41d-78d8-49df-aa03-e5ff7af55235 |
| GET | /v1/parcels/c75559cb-df9c-4391-b0d1-5802ed6eb9d8 | 200 |  | 4fd23ea8-d6a2-4f9f-a6db-b8d2549bb76a |
| GET | /v1/parcels/c75559cb-df9c-4391-b0d1-5802ed6eb9d8 | 200 |  | 7a4d546b-bf47-413e-84e5-fd1bde7a6ff7 |
| GET | /v1/parcels/c75559cb-df9c-4391-b0d1-5802ed6eb9d8 | 200 |  | 13698a7a-df2f-4247-9914-37aed96f29ea |
| GET | /v1/parcels/c75559cb-df9c-4391-b0d1-5802ed6eb9d8 | 200 |  | 06e7d8d0-1e26-47cb-9bf3-d40c8f6fe9cd |
| GET | /v1/parcels/c75559cb-df9c-4391-b0d1-5802ed6eb9d8 | 200 |  | 369316c7-7b9f-4c05-86eb-f81a7cd64d44 |
| GET | /v1/parcels/c75559cb-df9c-4391-b0d1-5802ed6eb9d8 | 200 |  | aa6f89da-f4bb-4e63-b904-48e54b67da35 |
| GET | /v1/parcels/c75559cb-df9c-4391-b0d1-5802ed6eb9d8 | 200 |  | 3d68000e-f3c0-41e9-8e65-2eda807d13e8 |
| GET | /v1/parcels/c75559cb-df9c-4391-b0d1-5802ed6eb9d8 | 200 |  | c061eec5-c0fd-4608-9c11-8858efc88fab |
| POST | /v1/parcels | 201 |  | 2cc9f418-47bf-4a17-81fe-41b7cffc12ca |
| POST | /v1/parcels | 201 |  | 2cc9f418-47bf-4a17-81fe-41b7cffc12ca |
| POST | /v1/parcels/8bc09863-d494-48ee-9726-99230c2f5e61/deposit-payment | 200 |  | 1d3d7cf1-d55c-4921-8e4e-f7c4a7ea7310 |
| GET | /v1/parcels/8bc09863-d494-48ee-9726-99230c2f5e61 | 200 |  | 10010512-b39d-4d87-97fc-bd68b13b93c9 |
| GET | /v1/parcels/8bc09863-d494-48ee-9726-99230c2f5e61 | 200 |  | 436fb612-b2ac-47d4-893f-c10a1629645f |
| GET | /v1/parcels/8bc09863-d494-48ee-9726-99230c2f5e61 | 200 |  | b76e52e8-acf1-4d65-a74c-3e122774bb61 |
| GET | /v1/parcels/8bc09863-d494-48ee-9726-99230c2f5e61 | 200 |  | ef21aac9-c15b-4e0f-ba98-aae6f32334bf |
| GET | /v1/parcels/8bc09863-d494-48ee-9726-99230c2f5e61 | 200 |  | 8019d230-6091-4eab-b3cb-bbfa7fb03fe7 |
| GET | /v1/parcels/8bc09863-d494-48ee-9726-99230c2f5e61 | 200 |  | ae3fc189-51ba-497b-8381-c1713ebd152b |
| GET | /v1/parcels/8bc09863-d494-48ee-9726-99230c2f5e61 | 200 |  | 151cc25d-8028-437b-a7cb-ec2bf6571a38 |
| GET | /v1/parcels/8bc09863-d494-48ee-9726-99230c2f5e61 | 200 |  | eef27bb8-245b-4b03-8945-282a16d6b22b |
| GET | /v1/parcels/8bc09863-d494-48ee-9726-99230c2f5e61 | 200 |  | 69251824-e2b0-4f47-a89b-b3bddc78b796 |
| GET | /v1/parcels/8bc09863-d494-48ee-9726-99230c2f5e61 | 200 |  | 7be4a8c2-2136-4766-9e91-7aba71d5fefe |
| POST | /v1/assistant/parcels/8bc09863-d494-48ee-9726-99230c2f5e61/check-in | 200 |  | 9b5ff25c-2f60-4af8-949d-814106e6a687 |
| POST | /v1/assistant/parcels/8bc09863-d494-48ee-9726-99230c2f5e61/reweigh | 200 |  | 3661e2f9-6b01-4544-b418-eb97abf3189c |
| POST | /v1/parcels/8bc09863-d494-48ee-9726-99230c2f5e61/final-payment | 200 |  | 959cd9ad-d8f4-4f6b-bacd-afa0eecf7d72 |
| GET | /v1/parcels/8bc09863-d494-48ee-9726-99230c2f5e61 | 200 |  | 64d03d29-f163-4bbb-8486-bce63e332f08 |
| GET | /v1/parcels/8bc09863-d494-48ee-9726-99230c2f5e61 | 200 |  | 356c108c-6101-491d-baf0-2074208ccd42 |
| GET | /v1/parcels/8bc09863-d494-48ee-9726-99230c2f5e61 | 200 |  | 213e8644-b54d-4511-97a1-fd908cc3cd5c |
| GET | /v1/parcels/8bc09863-d494-48ee-9726-99230c2f5e61 | 200 |  | a69ef8d2-5d02-486d-a464-d33c1d19fbcc |
| GET | /v1/parcels/8bc09863-d494-48ee-9726-99230c2f5e61 | 200 |  | 97329d8e-05f1-4692-bb6e-8f98b85b01b8 |
| GET | /v1/parcels/8bc09863-d494-48ee-9726-99230c2f5e61 | 200 |  | 659801cc-257f-4430-b904-4925ddd407fc |
| GET | /v1/parcels/8bc09863-d494-48ee-9726-99230c2f5e61 | 200 |  | cca8d61b-ce56-474c-bea6-058ec4d5b9a3 |
| GET | /v1/parcels/8bc09863-d494-48ee-9726-99230c2f5e61 | 200 |  | 6237197e-e1e5-45ca-949d-af5a55848a0c |
| GET | /v1/parcels/8bc09863-d494-48ee-9726-99230c2f5e61 | 200 |  | 130f6849-ffa6-4803-8e6d-eb0ccc9c26ba |
| GET | /v1/parcels/8bc09863-d494-48ee-9726-99230c2f5e61 | 200 |  | 5dfd2cfd-82b2-45ee-bc93-01e0f36ff670 |
| POST | /v1/parcels | 201 |  | 36ea1b6d-6a61-4635-9de2-5a168ab27ab6 |
| POST | /v1/parcels | 201 |  | 36ea1b6d-6a61-4635-9de2-5a168ab27ab6 |
| POST | /v1/parcels/008f0b8f-73a1-4832-93c2-0b564d77cbf5/deposit-payment | 200 |  | d6c8138e-655d-47a0-a464-f6862addd109 |
| GET | /v1/parcels/008f0b8f-73a1-4832-93c2-0b564d77cbf5 | 200 |  | 8d9859fe-b1c0-4dd5-b901-8def2a9b9560 |
| GET | /v1/parcels/008f0b8f-73a1-4832-93c2-0b564d77cbf5 | 200 |  | 50ba8208-4f7b-400b-963d-88ffe8e98972 |
| GET | /v1/parcels/008f0b8f-73a1-4832-93c2-0b564d77cbf5 | 200 |  | fb47c575-68fd-4aa8-a4d6-0a165d54e213 |
| GET | /v1/parcels/008f0b8f-73a1-4832-93c2-0b564d77cbf5 | 200 |  | 17719c66-2549-4d37-81eb-4d0158e884e5 |
| GET | /v1/parcels/008f0b8f-73a1-4832-93c2-0b564d77cbf5 | 200 |  | 1de3e31a-a5e2-414d-9182-5b32191a1b9c |
| GET | /v1/parcels/008f0b8f-73a1-4832-93c2-0b564d77cbf5 | 200 |  | 0aaede87-c67a-45e4-bc45-7f122486c72b |
| GET | /v1/parcels/008f0b8f-73a1-4832-93c2-0b564d77cbf5 | 200 |  | a51249c1-2a30-43d6-bfad-9bd3e97d0ffb |
| GET | /v1/parcels/008f0b8f-73a1-4832-93c2-0b564d77cbf5 | 200 |  | 1489bd26-2efc-4ef8-811c-8cd29b4db21c |
| GET | /v1/parcels/008f0b8f-73a1-4832-93c2-0b564d77cbf5 | 200 |  | d12c202f-880f-459f-bd80-7bcf9e846784 |
| GET | /v1/parcels/008f0b8f-73a1-4832-93c2-0b564d77cbf5 | 200 |  | 9a2adc91-cba6-4ba6-b2ff-bcc3c78bbefc |
| POST | /v1/assistant/parcels/008f0b8f-73a1-4832-93c2-0b564d77cbf5/check-in | 200 |  | 9594dae0-4724-4e8b-9c63-53471beba8c1 |
| POST | /v1/assistant/parcels/008f0b8f-73a1-4832-93c2-0b564d77cbf5/reweigh | 200 |  | d991dd5b-db10-4f30-b136-f99ac6bd9a29 |
| POST | /v1/parcels/008f0b8f-73a1-4832-93c2-0b564d77cbf5/final-payment | 200 |  | cb254609-c8b9-483e-97a5-bf5f5df37dd3 |
| GET | /v1/parcels/008f0b8f-73a1-4832-93c2-0b564d77cbf5 | 200 |  | 82ca32de-fb29-44ba-8030-7ad108c6321b |
| GET | /v1/parcels/008f0b8f-73a1-4832-93c2-0b564d77cbf5 | 200 |  | a80d476f-d7f0-4c4a-baf9-2301252c2051 |
| GET | /v1/parcels/008f0b8f-73a1-4832-93c2-0b564d77cbf5 | 200 |  | 0a3c406b-f15e-4c39-b856-e0a223173c60 |
| GET | /v1/parcels/008f0b8f-73a1-4832-93c2-0b564d77cbf5 | 200 |  | f3abc2ea-7078-4e82-9f9b-7874a791dc9e |
| GET | /v1/parcels/008f0b8f-73a1-4832-93c2-0b564d77cbf5 | 200 |  | d97e93e7-0615-4520-ab75-13731d7be67c |
| GET | /v1/parcels/008f0b8f-73a1-4832-93c2-0b564d77cbf5 | 200 |  | e5fa3508-646a-47b4-b1e3-9a0425bfb22d |
| GET | /v1/parcels/008f0b8f-73a1-4832-93c2-0b564d77cbf5 | 200 |  | edb06877-52fe-42ed-89e4-2f5537d58c43 |
| GET | /v1/parcels/008f0b8f-73a1-4832-93c2-0b564d77cbf5 | 200 |  | 6a963f4e-bac8-45cf-8388-8be3bfbd3246 |
| GET | /v1/parcels/008f0b8f-73a1-4832-93c2-0b564d77cbf5 | 200 |  | 0518f081-7417-4796-9f1a-a8f9cff24e5a |
| GET | /v1/parcels/008f0b8f-73a1-4832-93c2-0b564d77cbf5 | 200 |  | 86a9795e-405a-42c5-a148-6925e5bbb32a |
| GET | /v1/parcels/sent?page=1&pageSize=20 | 200 |  | 4364a6c1-23c7-4c92-b686-6b8fa006d3af |
| GET | /v1/parcels/received?page=1&pageSize=20 | 200 |  | cf15e8c5-1a00-48f6-bc96-58d24ec5d2bb |
| GET | /v1/parcels/0d5ee6d0-3828-480e-88b4-8f5a38de06b4 | 200 |  | 0727de58-32a5-4465-b72c-9e2a81a87bbb |
| GET | /v1/assistant/trips/1f57c66a-730f-4b3a-ae4a-6c9027f6d97e/parcels?page=1&pageSize=50 | 200 |  | b2d3cc69-707b-4a61-9dc3-9ecb474eb0b9 |
| GET | /v1/parcels/f9dd8d3a-6695-49fc-9d26-1efe1f4db956 | 200 |  | 6a47af6a-2813-4e2d-b2dd-004e40b5bc74 |
| POST | /v1/assistant/parcels/f9dd8d3a-6695-49fc-9d26-1efe1f4db956/custody-scan | 200 |  | 611f54e7-895b-45fc-bff3-628786632201 |
| GET | /v1/parcels/f9dd8d3a-6695-49fc-9d26-1efe1f4db956 | 200 |  | e3951a8b-fd2b-418e-9e55-90f09327ebd2 |
| POST | /v1/assistant/parcels/f9dd8d3a-6695-49fc-9d26-1efe1f4db956/custody-scan | 409 | PARCEL_CUSTODY_LOCATION_MISMATCH | 4c633721-3aa8-42bb-8ae5-81420e177ee4 |
| POST | /v1/assistant/parcels/0d5ee6d0-3828-480e-88b4-8f5a38de06b4/unload | 422 | VALIDATION_ERROR | 6072fa73-2a49-427c-a766-39a5b6556568 |
| POST | /v1/operator/trips/1f57c66a-730f-4b3a-ae4a-6c9027f6d97e/boarding | 200 |  | 2b346c3f-1bf0-4c60-98ec-dfb51af1705e |
| POST | /v1/driver/trips/1f57c66a-730f-4b3a-ae4a-6c9027f6d97e/start | 200 |  | 2eb3e740-ed49-4535-9ddb-dc00bc11fe6e |
| GET | /v1/parcels/0d5ee6d0-3828-480e-88b4-8f5a38de06b4 | 200 |  | 8153c40a-7ef9-47dd-a497-dc27367f08c0 |
| GET | /v1/parcels/0d5ee6d0-3828-480e-88b4-8f5a38de06b4 | 200 |  | d4e7d719-569d-4bb7-ba32-671a02d6859e |
| GET | /v1/parcels/0d5ee6d0-3828-480e-88b4-8f5a38de06b4 | 200 |  | ab7245ee-78e3-4b8d-a3de-5940c6e69899 |
| GET | /v1/parcels/0d5ee6d0-3828-480e-88b4-8f5a38de06b4 | 200 |  | 9b43fc1a-fbdf-40e5-adb0-f0af4567daa0 |
| GET | /v1/parcels/0d5ee6d0-3828-480e-88b4-8f5a38de06b4 | 200 |  | c32ed2f4-b16d-4b8a-939b-8bebaf3315f5 |
| GET | /v1/parcels/0d5ee6d0-3828-480e-88b4-8f5a38de06b4 | 200 |  | b1980595-8309-46a7-a46a-b209314f5ce6 |
| GET | /v1/parcels/0d5ee6d0-3828-480e-88b4-8f5a38de06b4 | 200 |  | e2d5c7c5-a5d7-41ae-aaa8-7d1f03bad803 |
| POST | /v1/driver/trips/1f57c66a-730f-4b3a-ae4a-6c9027f6d97e/stops/b7e79f1c-0123-4d02-bc9c-a4c9dab74ece/arrive | 200 |  | 1c153b32-611e-438f-82b5-8d6ebaa7fe02 |
| POST | /v1/assistant/parcels/0d5ee6d0-3828-480e-88b4-8f5a38de06b4/unload | 409 | SCAN_IDENTITY_MISMATCH | 186ab55e-9bed-4dc4-8586-49817366fea8 |
| POST | /v1/assistant/parcels/0d5ee6d0-3828-480e-88b4-8f5a38de06b4/unload | 409 | PARCEL_CUSTODY_LOCATION_MISMATCH | af995c5d-f855-44dd-9cb8-f18c66b59137 |
| POST | /v1/assistant/parcels/008f0b8f-73a1-4832-93c2-0b564d77cbf5/custody-exception | 202 |  | 59a106b2-ea6e-43d1-b933-1e23367ad446 |
| POST | /v1/operator/parcel-incidents/30af2463-d048-4608-b1e9-e80dcb3f27fd/custody-exception-decision | 404 | PARCEL_CUSTODY_EXCEPTION_REQUEST_NOT_FOUND | 6f89fe28-245e-48bf-92a2-3f0ff611fb47 |
| POST | /v1/operator/parcel-incidents/30af2463-d048-4608-b1e9-e80dcb3f27fd/custody-exception-decision | 200 |  | bb5bdee8-fbb9-44a9-a97b-c0f7572aa1e4 |
| POST | /v1/assistant/parcels/fc4e4489-444c-4d3d-a586-7138c650e807/custody-exception | 202 |  | 0e232db7-d767-4b3c-a365-6036b85afa78 |
| POST | /v1/crew/parcels/fc4e4489-444c-4d3d-a586-7138c650e807/custody-exception-decision | 200 |  | f1654c15-4550-438e-bd96-706542398564 |
| POST | /v1/stations/parcels/unidentified | 201 |  | a866b79c-e715-4042-ba2c-7421ac58b18a |
| GET | /v1/operator/unidentified-packages?page=1&pageSize=20 | 200 |  | 4d5fd881-61f5-47a3-99c7-e62518628b09 |
| GET | /v1/operator/unidentified-packages/9fa54b47-f160-4175-9a2e-72940e51cc2c/match-candidates | 200 |  | 56738dc3-4fb5-476e-b516-dcf4f6c7bfde |
| POST | /v1/stations/parcels/unidentified/9fa54b47-f160-4175-9a2e-72940e51cc2c/match | 200 |  | 179be61c-434e-4215-b038-02c56316e5b8 |
| POST | /v1/operator/parcel-incidents/4781c0af-0027-4c3d-845d-a47200dc589a/mark-found | 200 |  | c9a6f91c-2b0d-44aa-b444-0023fb70fad8 |
| GET | /v1/operator/parcel-incidents/4781c0af-0027-4c3d-845d-a47200dc589a/forwarding-options | 200 |  | faf4901b-10f9-4fc8-8cef-f28b3e645311 |
| POST | /v1/operator/parcel-incidents/4781c0af-0027-4c3d-845d-a47200dc589a/forward | 200 |  | 8a44b883-7485-4137-a69b-82a785afb364 |
| POST | /v1/crew/parcels/fc4e4489-444c-4d3d-a586-7138c650e807/confirm-transfer | 200 |  | 09ed8050-1a90-45ba-883f-bf742f7e962c |
| POST | /v1/driver/trips/1f57c66a-730f-4b3a-ae4a-6c9027f6d97e/stops/b7e79f1c-0123-4d02-bc9c-a4c9dab74ece/depart | 200 |  | dbed0606-bc15-470d-9c93-da7acd448bba |
| POST | /v1/assistant/parcels/0d5ee6d0-3828-480e-88b4-8f5a38de06b4/unload | 409 | PARCEL_CUSTODY_LOCATION_MISMATCH | 94e587b0-4572-414b-aac0-45f41f4240d6 |
| POST | /v1/driver/trips/1f57c66a-730f-4b3a-ae4a-6c9027f6d97e/stops/073f1554-47d6-4e8d-8c96-9d5d3887eaeb/arrive | 200 |  | 984d82da-8bce-40e5-90b8-49be0208b8bd |
| POST | /v1/assistant/parcels/0d5ee6d0-3828-480e-88b4-8f5a38de06b4/unload | 200 |  | f00a39e9-dee3-47e5-b692-068240a4c39e |
| POST | /v1/assistant/parcels/0d5ee6d0-3828-480e-88b4-8f5a38de06b4/deliver | 200 |  | e53ec929-3dd4-4db8-b0bb-33c64922c5c5 |
| POST | /v1/assistant/parcels/0d5ee6d0-3828-480e-88b4-8f5a38de06b4/confirm-delivery | 200 |  | e01f0565-f9bc-458b-9c42-1526a16f7a18 |
| GET | /v1/parcels/0d5ee6d0-3828-480e-88b4-8f5a38de06b4 | 200 |  | c6cdc946-0070-4cb2-8c20-4e7920994589 |
| POST | /v1/assistant/trips/1f57c66a-730f-4b3a-ae4a-6c9027f6d97e/stops/073f1554-47d6-4e8d-8c96-9d5d3887eaeb/reconcile | 200 |  | b7a177b4-c0a8-491c-b934-0828cc168f77 |
| POST | /v1/crew/parcel-stop-departure-approvals/56eb61b9-f6b5-472b-8698-72c0bce091ea/decision | 200 |  | e6c751c9-1905-44b8-a8f6-861576f07938 |
| POST | /v1/assistant/trips/1f57c66a-730f-4b3a-ae4a-6c9027f6d97e/stops/073f1554-47d6-4e8d-8c96-9d5d3887eaeb/reconcile | 200 |  | d2e9fadc-8b81-4f68-a405-66b837e4113e |
| GET | /v1/operator/parcel-incidents/cbc2fdcc-e7d9-483b-9fbd-183df2993b06 | 200 |  | 7e47d7a6-eb2b-47df-ad6f-07172787bcc6 |
| POST | /v1/assistant/parcels/7da3bf87-2de6-432e-8900-485c10ed25be/confirm-found-on-vehicle | 200 |  | 10e3ca2e-3582-4ffb-b513-9ab4e097ab1b |
| GET | /v1/operator/parcel-incidents/cbc2fdcc-e7d9-483b-9fbd-183df2993b06 | 200 |  | 27f237ee-2638-44ac-86a8-95db70819df6 |
| POST | /v1/assistant/parcels/7da3bf87-2de6-432e-8900-485c10ed25be/unload | 200 |  | 514ac7d6-6c7b-47ad-ae29-0a09f3797e33 |
| POST | /v1/assistant/parcels/7da3bf87-2de6-432e-8900-485c10ed25be/deliver | 200 |  | 79eb32ab-74be-47cb-8438-0aa33c290cfe |
| POST | /v1/assistant/parcels/7da3bf87-2de6-432e-8900-485c10ed25be/confirm-delivery | 200 |  | ad557caf-d5b4-4709-a038-6073a34d7548 |
| GET | /v1/parcels/7da3bf87-2de6-432e-8900-485c10ed25be | 200 |  | 4787b68a-67d3-42cd-859d-c512692d4b97 |
| POST | /v1/assistant/trips/1f57c66a-730f-4b3a-ae4a-6c9027f6d97e/stops/073f1554-47d6-4e8d-8c96-9d5d3887eaeb/reconcile | 200 |  | 798beffa-cb00-4707-82a3-cb1182bac92c |
| POST | /v1/crew/parcel-stop-departure-approvals/2f6be59c-60ff-46c7-ace8-69f87f975289/decision | 200 |  | d8a8c624-24d2-4a31-aad5-a1e7ac612577 |
| POST | /v1/assistant/trips/1f57c66a-730f-4b3a-ae4a-6c9027f6d97e/stops/073f1554-47d6-4e8d-8c96-9d5d3887eaeb/reconcile | 200 |  | bd3c4970-e091-42ba-941a-9443d858e9ef |
| POST | /v1/driver/trips/1f57c66a-730f-4b3a-ae4a-6c9027f6d97e/stops/073f1554-47d6-4e8d-8c96-9d5d3887eaeb/depart | 200 |  | 5c55ac5c-00dc-489a-9154-fa35f8828fbd |
| POST | /v1/driver/trips/1f57c66a-730f-4b3a-ae4a-6c9027f6d97e/destination/arrive | 200 |  | b22f4fb1-b215-4e31-960c-d2c7da18aeee |
| POST | /v1/driver/trips/1f57c66a-730f-4b3a-ae4a-6c9027f6d97e/complete | 409 | PARCEL_DESTINATION_RECONCILIATION_REQUIRED | 8fd651b5-bc39-4815-bdfe-868e62c342a9 |
| POST | /v1/assistant/trips/1f57c66a-730f-4b3a-ae4a-6c9027f6d97e/destination/reconcile | 200 |  | 798efc17-685c-4f59-afc3-8d697ffdad53 |
| POST | /v1/driver/trips/1f57c66a-730f-4b3a-ae4a-6c9027f6d97e/complete | 200 |  | e6339b02-b9ed-4df2-b1a0-edfa64b57454 |
| GET | /v1/operator/parcel-incidents/f43248c2-1343-4889-ac1b-e5033e942783 | 200 |  | 9acaf444-f3cc-4090-8813-f592735e410d |
| GET | /v1/operator/parcel-incidents?search=VR-PCL-20260830-943V6KHY&page=1&pageSize=20 | 200 |  | e7ac0328-7951-4b29-8460-379ede4cc9c9 |
| GET | /v1/operator/parcel-incidents/f43248c2-1343-4889-ac1b-e5033e942783 | 200 |  | 7d13dd17-c7d6-4a59-984d-74978827b893 |
| GET | /v1/operator/parcel-incidents/f43248c2-1343-4889-ac1b-e5033e942783 | 403 | FORBIDDEN | 965dda77-9858-4bf2-a059-79ba461d9f33 |
| GET | /v1/parcels/1e94b393-fd68-42e8-a88a-86e37a510ffd/trace | 200 |  | a8570dfe-9e23-406d-87b2-071b30148853 |
| GET | /v1/parcels/1e94b393-fd68-42e8-a88a-86e37a510ffd/trace | 200 |  | e3c4f282-39c9-43bb-8866-b9b6db76a069 |
| POST | /v1/parcels/f9dd8d3a-6695-49fc-9d26-1efe1f4db956/incidents | 422 | PARCEL_INCIDENT_TYPE_NOT_REPORTABLE | 41ad5cdf-f41f-421f-895f-7b2b53bc058a |
| POST | /v1/parcels/f9dd8d3a-6695-49fc-9d26-1efe1f4db956/incidents | 201 |  | 338298b8-9552-47bf-916a-9fb16d5c1968 |
| POST | /v1/operator/parcel-incidents/804a2e01-e87b-422f-9d12-195f87d396ba/declare-lost | 200 |  | a7d07d3b-9ce0-40de-ae4e-58beffb63dde |
| POST | /v1/parcels/f9dd8d3a-6695-49fc-9d26-1efe1f4db956/claims | 201 |  | 73b309b5-5b31-479c-a5c0-202fc14d498f |
| POST | /v1/parcels/f9dd8d3a-6695-49fc-9d26-1efe1f4db956/claims/ace50d1f-ae89-47fe-b7ab-948e36348c8c/evidence | 201 |  | 71ecf334-b8a3-4a6e-8c54-416d5becb2d5 |
| POST | /v1/parcels/fecf7dac-35e5-42b9-b150-c5b8465ad17f/incidents | 422 | PARCEL_INCIDENT_TYPE_NOT_REPORTABLE | 913d9bd6-21a8-4946-a266-2818f42901b6 |
| POST | /v1/parcels/fecf7dac-35e5-42b9-b150-c5b8465ad17f/incidents | 201 |  | c1d7cc17-0bf4-4326-87bb-57e6f87e2a81 |
| POST | /v1/operator/parcel-incidents/11c5b95c-79c1-45d3-9801-3cdc84bb65c7/declare-lost | 200 |  | 2191e276-e606-4465-a211-359fc9ba4655 |
| POST | /v1/parcels/fecf7dac-35e5-42b9-b150-c5b8465ad17f/claims | 201 |  | 76fe033c-3196-4429-b6df-6e11296b61f8 |
| POST | /v1/parcels/c75559cb-df9c-4391-b0d1-5802ed6eb9d8/incidents | 422 | PARCEL_INCIDENT_TYPE_NOT_REPORTABLE | 57d1e36d-12e3-487c-9977-2d725f195351 |
| POST | /v1/parcels/c75559cb-df9c-4391-b0d1-5802ed6eb9d8/incidents | 201 |  | 5e778da8-1397-4760-a2d0-ebee09ddaedc |
| POST | /v1/operator/parcel-incidents/5cf00f64-7d4b-44db-b816-9cbf557498d3/declare-lost | 200 |  | e5c6da28-15c4-4473-849f-cc54f073e082 |
| POST | /v1/parcels/c75559cb-df9c-4391-b0d1-5802ed6eb9d8/claims | 409 | PARCEL_INCIDENT_CLAIM_WINDOW_EXPIRED | 110593d1-de6c-4b73-999a-0df6eed16d74 |
| POST | /v1/parcels/8bc09863-d494-48ee-9726-99230c2f5e61/incidents | 422 | PARCEL_INCIDENT_TYPE_NOT_REPORTABLE | 54f8008e-50c2-49f5-b064-151d3d46ec86 |
| POST | /v1/parcels/8bc09863-d494-48ee-9726-99230c2f5e61/incidents | 201 |  | 9f07722b-0970-4a9e-b021-84d648e514e3 |
| POST | /v1/operator/parcel-incidents/ddab5d45-dc91-47c5-a445-43f03188bc63/declare-lost | 200 |  | 45ba5425-5b34-4a19-8d4d-2a18a8a889a2 |
| POST | /v1/parcels/8bc09863-d494-48ee-9726-99230c2f5e61/claims | 201 |  | 32f2660a-1954-40b9-adeb-d61176f9906e |
| POST | /v1/parcels/8bc09863-d494-48ee-9726-99230c2f5e61/claims/72a09532-ae6a-42e8-bf92-5ed9494ac6c6/evidence | 201 |  | d394b984-ad2f-42c3-825b-8bc7de8a1ad4 |
| POST | /v1/admin/operators/dd72c2a3-ff0e-4e89-8421-4219cd5f0f21/wallet/adjust | 200 |  | c15c7b94-e993-4392-a7c3-daa3cb73cc2a |
| POST | /v1/operator/claims/ace50d1f-ae89-47fe-b7ab-948e36348c8c/decision | 200 |  | d9207506-6724-4c33-970d-dfdc09646216 |
| GET | /v1/operator/claims/ace50d1f-ae89-47fe-b7ab-948e36348c8c | 200 |  | eee047a0-fbc7-482b-9fed-d85325b12ccc |
| GET | /v1/operator/claims/ace50d1f-ae89-47fe-b7ab-948e36348c8c | 200 |  | af041453-e8f3-44de-b791-31a01e74fb97 |
| GET | /v1/operator/claims/ace50d1f-ae89-47fe-b7ab-948e36348c8c | 200 |  | f58b5bd7-bf1c-4e57-8d79-75246d4d6dba |
| GET | /v1/operator/claims/ace50d1f-ae89-47fe-b7ab-948e36348c8c | 200 |  | 7d129702-ac42-4e23-9048-44422f0fb859 |
| GET | /v1/operator/claims/ace50d1f-ae89-47fe-b7ab-948e36348c8c | 200 |  | 498cf24c-eeaa-4956-bc43-696e5ff6718d |
| GET | /v1/operator/claims/ace50d1f-ae89-47fe-b7ab-948e36348c8c | 200 |  | d4bc72c8-6208-402a-ae57-e6224d11045b |
| GET | /v1/operator/claims/ace50d1f-ae89-47fe-b7ab-948e36348c8c | 200 |  | 705f7828-1972-40fa-99ba-9a8056e93e8a |
| GET | /v1/operator/claims/ace50d1f-ae89-47fe-b7ab-948e36348c8c | 200 |  | 3cd42854-f9b1-4d0d-8aa2-631070c15481 |
| GET | /v1/operator/claims/ace50d1f-ae89-47fe-b7ab-948e36348c8c | 200 |  | 8112e62b-0a4c-438b-8828-8f9e53c0149e |
| GET | /v1/operator/claims/ace50d1f-ae89-47fe-b7ab-948e36348c8c | 200 |  | 7a54e8bb-583e-4fb3-872c-25b2c6b9066f |
| GET | /v1/operator/claims/ace50d1f-ae89-47fe-b7ab-948e36348c8c | 200 |  | 0780eaeb-384b-4db4-9103-3e9974d018b4 |
| GET | /v1/operator/claims/ace50d1f-ae89-47fe-b7ab-948e36348c8c | 200 |  | d10dcf44-1eb4-4f7b-b63c-19d87d2f11df |
| GET | /v1/operator/claims/ace50d1f-ae89-47fe-b7ab-948e36348c8c | 200 |  | 0ba7a863-a035-4d95-8829-068c0dfd1885 |
| GET | /v1/operator/claims/ace50d1f-ae89-47fe-b7ab-948e36348c8c | 200 |  | 3c9bc128-0ed4-4ff5-af5d-8ae9e24c1d7e |
| GET | /v1/operator/claims/ace50d1f-ae89-47fe-b7ab-948e36348c8c | 200 |  | 94389646-c4f5-4d41-8c4f-66882685869b |
| GET | /v1/operator/claims/ace50d1f-ae89-47fe-b7ab-948e36348c8c | 200 |  | e0a1f5ad-77ed-4538-abf4-3bf4bf98cf66 |
| GET | /v1/operator/claims/ace50d1f-ae89-47fe-b7ab-948e36348c8c | 200 |  | c75ff665-59fa-4a63-b061-0d573ae104d4 |
| GET | /v1/operator/claims/ace50d1f-ae89-47fe-b7ab-948e36348c8c | 200 |  | 7e006e8f-500b-4e67-a06d-08a821114020 |
| POST | /v1/operator/claims/b98283b6-b35e-449b-87f4-f49b8d713c92/decision | 409 | PARCEL_CLAIM_ALREADY_DECIDED | 9f6b523d-d95c-4801-8478-4e51ed3a447e |
| POST | /v1/operator/claims/b98283b6-b35e-449b-87f4-f49b8d713c92/decision | 200 |  | 318547e0-8adb-4dfc-ac4c-590955611b75 |
| GET | /v1/operator/claims/b98283b6-b35e-449b-87f4-f49b8d713c92 | 200 |  | f0b75714-2b45-46b0-a18c-467c67292c74 |
| GET | /v1/operator/claims/b98283b6-b35e-449b-87f4-f49b8d713c92 | 200 |  | 3e45cecb-d681-4562-8fdc-9f51856eb981 |
| GET | /v1/operator/claims/b98283b6-b35e-449b-87f4-f49b8d713c92 | 200 |  | 56e1e66c-77ad-4324-8971-4fa337e4efb7 |
| GET | /v1/operator/claims/b98283b6-b35e-449b-87f4-f49b8d713c92 | 200 |  | aaf36fd8-fc69-4f99-b4db-1277fba95661 |
| GET | /v1/operator/claims/b98283b6-b35e-449b-87f4-f49b8d713c92 | 200 |  | 01c54a00-82a7-42a2-852f-2e38fcbea431 |
| GET | /v1/operator/claims/b98283b6-b35e-449b-87f4-f49b8d713c92 | 200 |  | 275dfe17-7f81-41f2-a574-6d51543ea4f3 |
| GET | /v1/operator/claims/b98283b6-b35e-449b-87f4-f49b8d713c92 | 200 |  | 79a04296-387a-4f53-92e4-deda15955dbc |
| GET | /v1/operator/claims/b98283b6-b35e-449b-87f4-f49b8d713c92 | 200 |  | 7ed303b3-c59d-4e6f-ba29-9a5bfc2df5e9 |
| GET | /v1/operator/claims/b98283b6-b35e-449b-87f4-f49b8d713c92 | 200 |  | 0760ccfc-ff78-4015-835b-49b54d787494 |
| GET | /v1/operator/claims/b98283b6-b35e-449b-87f4-f49b8d713c92 | 200 |  | 9f761278-d314-4b4c-8ff2-fe03fecb955e |
| GET | /v1/operator/claims/b98283b6-b35e-449b-87f4-f49b8d713c92 | 200 |  | 12bb252b-c979-45cc-8206-d76cc8b99e38 |
| GET | /v1/operator/claims/b98283b6-b35e-449b-87f4-f49b8d713c92 | 200 |  | ae54a1d1-2ffe-49fd-984b-f64f5e627299 |
| GET | /v1/operator/claims/b98283b6-b35e-449b-87f4-f49b8d713c92 | 200 |  | c3a2e99e-120a-442a-9e63-f27c6e7d216c |
| GET | /v1/operator/claims/b98283b6-b35e-449b-87f4-f49b8d713c92 | 200 |  | eeae90b6-9495-48d4-b264-9f59cbc00dba |
| GET | /v1/operator/claims/b98283b6-b35e-449b-87f4-f49b8d713c92 | 200 |  | d4ba1e6d-a7f4-4e4b-8071-7331e1838e19 |
| GET | /v1/operator/claims/b98283b6-b35e-449b-87f4-f49b8d713c92 | 200 |  | 922ca4b2-3f2b-4aca-926f-dbd5f1043b82 |
| GET | /v1/operator/claims/b98283b6-b35e-449b-87f4-f49b8d713c92 | 200 |  | 9917c014-979e-4f7f-b742-c0963318c11b |
| GET | /v1/operator/claims/b98283b6-b35e-449b-87f4-f49b8d713c92 | 200 |  | dff18a3d-b755-4aa1-b2a6-1ce93cd433c4 |
| GET | /v1/operator/claims/b98283b6-b35e-449b-87f4-f49b8d713c92 | 200 |  | 9dc78484-14d5-4b81-9fcb-6eff2b23d9b3 |
| POST | /v1/parcels/f9dd8d3a-6695-49fc-9d26-1efe1f4db956/claims/ace50d1f-ae89-47fe-b7ab-948e36348c8c/appeal | 200 |  | 1262596b-7abc-49b6-84a5-37c5432a6563 |
| POST | /v1/parcels/f9dd8d3a-6695-49fc-9d26-1efe1f4db956/claims/ace50d1f-ae89-47fe-b7ab-948e36348c8c/appeal | 200 |  | 1262596b-7abc-49b6-84a5-37c5432a6563 |
| GET | /v1/operator/claim-appeals?status=SUBMITTED&page=1&pageSize=20 | 200 |  | 73fd1f64-dc4a-4a28-a764-7bfe011e8038 |
| POST | /v1/operator/claim-appeals/4729fef7-37de-44a7-8ccc-e07f544f5532/decision | 200 |  | b6c241d7-480b-4da1-bcad-e9e0a2718c6f |
| GET | /v1/admin/trip-settlements?tripId=1f57c66a-730f-4b3a-ae4a-6c9027f6d97e&page=1&pageSize=20 | 200 |  | 94be7009-5973-4ee4-ad16-e4b6e405d148 |
| POST | /v1/admin/trip-settlements/f683a76e-8379-4222-99bd-e3a1beb39da6/settle | 200 |  | f1447b3c-25ba-4790-be34-e4fc89f11258 |
| GET | /v1/operator/wallet | 200 |  | 529b59a3-9e09-47aa-8820-38006d9b7600 |
| POST | /v1/admin/operators/dd72c2a3-ff0e-4e89-8421-4219cd5f0f21/wallet/adjust | 200 |  | e4fe8633-8d3c-42c5-98de-d0192fb93dcd |
| POST | /v1/operator/claims/72a09532-ae6a-42e8-bf92-5ed9494ac6c6/decision | 200 |  | e2809f4a-9961-42eb-9993-934b34491d4c |
| GET | /v1/operator/claims/72a09532-ae6a-42e8-bf92-5ed9494ac6c6 | 200 |  | df9a6dd0-9e7c-4538-861f-3dc55ab003c0 |
| GET | /v1/operator/claims/72a09532-ae6a-42e8-bf92-5ed9494ac6c6 | 200 |  | da69093e-069b-4e87-894c-be6d4194dc10 |
| GET | /v1/operator/claims/72a09532-ae6a-42e8-bf92-5ed9494ac6c6 | 200 |  | 6a1d5bf3-1b54-44a3-a952-39a9ab08ca12 |
| GET | /v1/operator/claims/72a09532-ae6a-42e8-bf92-5ed9494ac6c6 | 200 |  | 6479f63e-5625-4009-ab9c-c8f49116fc51 |
| GET | /v1/operator/claims/72a09532-ae6a-42e8-bf92-5ed9494ac6c6 | 200 |  | 7507c90f-2a4a-465d-b82f-0c0d7424325b |
| GET | /v1/operator/claims/72a09532-ae6a-42e8-bf92-5ed9494ac6c6 | 200 |  | dabb257e-24c8-46e6-ba2b-c31908646d6c |
| GET | /v1/operator/claims/72a09532-ae6a-42e8-bf92-5ed9494ac6c6 | 200 |  | 7e1046d1-220d-47cd-a9ad-e45d0606ad88 |
| GET | /v1/operator/claims/72a09532-ae6a-42e8-bf92-5ed9494ac6c6 | 200 |  | b61b68c5-471e-4cbf-94e7-fd9116116213 |
| GET | /v1/operator/claims/72a09532-ae6a-42e8-bf92-5ed9494ac6c6 | 200 |  | 1d18d4cf-87de-417f-a972-cadc90e40b36 |
| GET | /v1/operator/claims/72a09532-ae6a-42e8-bf92-5ed9494ac6c6 | 200 |  | 98819454-d72f-4e9f-9b47-7dd6a8ca86cd |
| GET | /v1/operator/claims/72a09532-ae6a-42e8-bf92-5ed9494ac6c6 | 200 |  | 10cd2a81-2ae8-489a-bb0b-5da2ac5fb4ea |
| GET | /v1/operator/claims/72a09532-ae6a-42e8-bf92-5ed9494ac6c6 | 200 |  | 23bd56cf-1832-4a48-acef-6fadda3b3956 |
| GET | /v1/operator/claims/72a09532-ae6a-42e8-bf92-5ed9494ac6c6 | 200 |  | 36e09f89-3c41-4e20-9181-0d5c665256f6 |
| GET | /v1/operator/claims/72a09532-ae6a-42e8-bf92-5ed9494ac6c6 | 200 |  | a15b2fba-9d7d-4abb-9ff6-e7bd67c27178 |
| GET | /v1/operator/claims/72a09532-ae6a-42e8-bf92-5ed9494ac6c6 | 200 |  | d87dd258-2ba1-42e4-831a-20c2fcd6ea04 |
| GET | /v1/operator/claims/72a09532-ae6a-42e8-bf92-5ed9494ac6c6 | 200 |  | 748aa552-41cb-4973-b38d-7e15f16fef2e |
| GET | /v1/operator/claims/72a09532-ae6a-42e8-bf92-5ed9494ac6c6 | 200 |  | 307f02fb-8114-400e-bf11-b7eb6db1ab5d |
| GET | /v1/operator/claims/72a09532-ae6a-42e8-bf92-5ed9494ac6c6 | 200 |  | 5817935c-5007-4cdc-a37c-cd514a1df483 |
| POST | /v1/operator/claims/ace50d1f-ae89-47fe-b7ab-948e36348c8c/decision | 409 | PARCEL_CLAIM_ALREADY_DECIDED | ef70bb3a-b027-4866-8743-e36d5f29e898 |
| GET | /v1/operator/claims?page=1&pageSize=20 | 200 |  | b9965da5-5370-4425-845b-a123f144fe68 |
| GET | /v1/operator/claims/72a09532-ae6a-42e8-bf92-5ed9494ac6c6 | 403 | FORBIDDEN | 3114a477-9066-41a7-96e7-318d9bb65201 |
| POST | /v1/operator/trips/2c7e87b0-6418-4fcf-a067-3931158ad032/boarding | 200 |  | d255792a-b363-48fc-8422-f60d4ac60fa3 |
| POST | /v1/driver/trips/2c7e87b0-6418-4fcf-a067-3931158ad032/start | 200 |  | ede09d62-cbea-41e9-99dc-e52166d611b4 |
| GET | /v1/parcels/fc4e4489-444c-4d3d-a586-7138c650e807 | 200 |  | 82f51ffa-45a1-459e-9172-d5d2eb72b749 |
| GET | /v1/parcels/fc4e4489-444c-4d3d-a586-7138c650e807 | 200 |  | a4dc1fe5-9bc5-42aa-889a-91edeb3208f8 |
| GET | /v1/parcels/fc4e4489-444c-4d3d-a586-7138c650e807 | 200 |  | ece11c0a-0a76-413e-8765-e930e07e5646 |
| GET | /v1/parcels/fc4e4489-444c-4d3d-a586-7138c650e807 | 200 |  | 8f8505be-591b-476f-9349-8d5c1408c706 |
| GET | /v1/parcels/fc4e4489-444c-4d3d-a586-7138c650e807 | 200 |  | 394151ff-e757-40cb-81e7-06c4d570fecf |
| GET | /v1/parcels/fc4e4489-444c-4d3d-a586-7138c650e807 | 200 |  | 4c44021c-f4ac-4018-a5dc-95273e0bf017 |
| GET | /v1/parcels/fc4e4489-444c-4d3d-a586-7138c650e807 | 200 |  | 30f409a3-5039-4953-9e97-4a1ad32b08ab |
| POST | /v1/driver/trips/2c7e87b0-6418-4fcf-a067-3931158ad032/stops/b7e79f1c-0123-4d02-bc9c-a4c9dab74ece/arrive | 200 |  | 2a832e0c-e9f1-45ea-96e5-222ac7b7f958 |
| POST | /v1/driver/trips/2c7e87b0-6418-4fcf-a067-3931158ad032/stops/b7e79f1c-0123-4d02-bc9c-a4c9dab74ece/depart | 200 |  | 777830e3-0724-4a73-b8e5-2a27cc91cc6f |
| POST | /v1/driver/trips/2c7e87b0-6418-4fcf-a067-3931158ad032/stops/073f1554-47d6-4e8d-8c96-9d5d3887eaeb/arrive | 200 |  | 4511c649-caea-4448-a069-47f1b3225626 |
| POST | /v1/assistant/parcels/fc4e4489-444c-4d3d-a586-7138c650e807/unload | 200 |  | b36e9aa4-b4c7-4ffb-b355-fb3e2557a1a1 |
| POST | /v1/assistant/parcels/fc4e4489-444c-4d3d-a586-7138c650e807/deliver | 200 |  | 91e6df91-a25e-4610-940c-5d260d10b129 |
| POST | /v1/assistant/parcels/fc4e4489-444c-4d3d-a586-7138c650e807/confirm-delivery | 200 |  | 8cb67fb4-0f59-4931-9b32-8f8243940164 |
| GET | /v1/parcels/fc4e4489-444c-4d3d-a586-7138c650e807 | 200 |  | c37d10c8-f2b5-4c00-ae70-f50d9aedfaad |
| POST | /v1/operator/parcel-incidents/4781c0af-0027-4c3d-845d-a47200dc589a/resolve | 200 |  | 00e10ebf-74d9-4d5c-87ea-7ab17c8dd235 |

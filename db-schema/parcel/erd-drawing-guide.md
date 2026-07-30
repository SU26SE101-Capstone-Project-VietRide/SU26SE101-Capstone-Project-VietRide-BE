# Parcel — ERD Drawing Guide

## Statistics
- **Total tables:** 11
- **Total intra-service FK:** 3 (`ParcelDeliveryToken.parcelId` → `Parcel.id`, `ParcelCargoRecoveryOperation.parcelId` → `Parcel.id`, `PlatformParcelStats.parcelId` → `Parcel.id`)
- **Hub tables:** `Parcel`
- **Leaf tables:** `ParcelDeliveryToken`, `ParcelCargoRecoveryOperation`, `PlatformParcelStats`, `ParcelRouteFare`, `SystemConfig`, `OperatorDepositPolicy`, `ParcelStats`, `IntegrationInbox`, `OutboxEvent`, `OutboxDlq`

## Recommended Layout Zones

| Zone | Tables | Vị trí gợi ý |
|---|---|---|
| Core | `Parcel`, `ParcelDeliveryToken`, `ParcelCargoRecoveryOperation` | trung tâm |
| Pricing | `ParcelRouteFare` | trái |
| Policy | `SystemConfig`, `OperatorDepositPolicy` | trái dưới |
| Reporting | `ParcelStats`, `PlatformParcelStats` | phải |
| Reliability | `IntegrationInbox`, `OutboxEvent`, `OutboxDlq` | dưới |

## Drawing Order

### Phase 1 — Intra-service

- `ParcelDeliveryToken.parcelId` → `Parcel.id` (`ON DELETE CASCADE`)
- `ParcelCargoRecoveryOperation.parcelId` → `Parcel.id` (`ON DELETE CASCADE`)
- `PlatformParcelStats.parcelId` → `Parcel.id` (`ON DELETE CASCADE`)

### Phase 2 — Cross-Service Logical FK (KHÔNG vẽ trong file này)

- `Parcel.senderUserId/recipientUserId/reviewedByUserId/confirmedByUserId/transferConfirmedByUserId/transferConfirmationClaimedByUserId/returnedByUserId`, `ParcelCargoRecoveryOperation.actorUserId` → `identity.User.id`
- `Parcel.operatorId`, `ParcelRouteFare.operatorId`, `ParcelStats.operatorId`, `ParcelCargoRecoveryOperation.operatorId` → `identity.Operator.id`
- `Parcel.tripId`, `Parcel.transferTargetTripId`, `ParcelCargoRecoveryOperation.sourceTripId/targetTripId` → `trip.Trip.id`
- `Parcel.dropoffStopId` → `trip.Stop.id`
- `ParcelRouteFare.routeId` → `trip.Route.id`
- `Parcel.additionalPaymentId` → `payment.Payment.id`

Xem `_global/cross-service-references.md`.

## Drawing Tips

1. **Parcel ở giữa**, các table khác (Stats, Fares, policy, reliability) đặt ngoài rìa.
2. Vẽ connection `ParcelDeliveryToken.parcelId` → `Parcel.id`; annotation `token_hash` UNIQUE và `parcel_id WHERE revoked_at IS NULL` UNIQUE partial.
3. Vẽ connection `ParcelCargoRecoveryOperation.parcelId` → `Parcel.id`; annotation `parcel_id WHERE status='PENDING'` UNIQUE partial.
4. Vẽ connection `PlatformParcelStats.parcelId` → `Parcel.id`; đây là projection one-to-one.
5. Note rõ `ParcelRouteFare` có composite PK `(route_id, size_category)` trên drawio.

## Validation Checklist

- [ ] 11 table box hiển thị đúng tên + columns
- [ ] Có đúng 3 logical connection tới Parcel
- [ ] Annotation rõ Parcel có 40+ field — đảm bảo column list đầy đủ
- [ ] Composite PK (ParcelRouteFare) hiển thị 2 column với PK marker

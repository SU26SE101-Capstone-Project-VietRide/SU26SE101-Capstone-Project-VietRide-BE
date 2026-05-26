# Identity & User — ERD Drawing Guide

> Hướng dẫn vẽ relation lines manually trong draw.io sau khi mở `schema.drawio`.
> File `schema.drawio` đã có sẵn mọi table box, chỉ thiếu connection lines.

## Statistics
- **Total tables:** 9
- **Total intra-service FK:** 9
- **Hub tables (≥3 inbound FK):** `User` (5 inbound), `Operator` (2 inbound), `SubscriptionPlan` (2 inbound)
- **Leaf tables (no inbound FK):** `ActivityLog`, `OperatorSubscription`, `OAuthIdentity`, `EmailVerificationToken`, `UserDevice`

## Recommended Layout Zones

| Zone | Tables | Vị trí gợi ý |
|---|---|---|
| Operator hub (left) | `Operator`, `OperatorSubscription`, `SubscriptionPlan` | cột trái |
| User hub (center) | `User` | trung tâm |
| User satellites (right) | `OAuthIdentity`, `RefreshToken`, `EmailVerificationToken`, `UserDevice`, `ActivityLog` | bên phải User |

## Drawing Order

### Phase 1 — User hub relations (5 inbound FK to User)

Vẽ trước vì User là hub lớn nhất; nếu xử lý sau sẽ phải bend nhiều line.

| # | From (FK column) | To (PK) | Cardinality | Note |
|---|---|---|---|---|
| 1 | `OAuthIdentity.userId` | `User.id` | N:1 | ON DELETE CASCADE |
| 2 | `RefreshToken.userId` | `User.id` | N:1 | ON DELETE CASCADE |
| 3 | `EmailVerificationToken.userId` | `User.id` | N:1 | ON DELETE CASCADE |
| 4 | `UserDevice.userId` | `User.id` | N:1 | ON DELETE CASCADE |
| 5 | `ActivityLog.userId` | `User.id` | N:1 | ON DELETE RESTRICT |

### Phase 2 — Operator hub relations (2 inbound FK to Operator)

| # | From (FK column) | To (PK) | Cardinality | Note |
|---|---|---|---|---|
| 6 | `User.operatorId` | `Operator.id` | N:1 | ON DELETE RESTRICT. Nullable cho PASSENGER/SYSTEM_ADMIN. |
| 7 | `OperatorSubscription.operatorId` | `Operator.id` | 1:1 | UNIQUE — 1 operator có đúng 1 subscription. |

### Phase 3 — SubscriptionPlan + RefreshToken self-FK

| # | From (FK column) | To (PK) | Cardinality | Note |
|---|---|---|---|---|
| 8 | `OperatorSubscription.planId` | `SubscriptionPlan.id` | N:1 | ON DELETE RESTRICT |
| 9 | `OperatorSubscription.previousActivePlanId` | `SubscriptionPlan.id` | N:1 | Nullable, dùng cho revert flow PENDING_PAYMENT |
| 10 | `RefreshToken.parentTokenId` | `RefreshToken.id` | N:1 | **Self-reference** — chain rotation history. Vẽ loop nhỏ ở góc table. |

### Phase 4 — Cross-Service Logical FK (KHÔNG vẽ trong file này)

Identity & User service được tham chiếu LOGICAL từ nhiều service khác (Booking.passengerUserId, Trip.driverUserId, v.v.). Xem `_global/cross-service-references.md`.

## Drawing Tips

1. **Connector style:** Right-click line → Edit Style → set `endArrow=ERmany;startArrow=ERone` cho N:1 relations; `endArrow=ERone;startArrow=ERone` cho 1:1.
2. **Avoid crossings:** Vẽ Phase 1 (User hub) trước, sắp xếp 5 satellite quanh User để các line tỏa ra không cross. Đặt OAuthIdentity, RefreshToken, EmailVerificationToken theo cột phải; UserDevice + ActivityLog phía dưới-phải.
3. **Bend points:** Click giữa line để add bend point, route line qua "lane" giữa các table — đặc biệt khi line từ `User.operatorId` phải đi qua chiều dọc đến cột trái.
4. **Cardinality label:** Add text label `1..*`, `1..1`, `0..*` ở đầu line.
5. **Color code:** Dùng cùng màu cho tất cả line đến `User` (vd xanh dương) để dễ phân biệt với line đến `Operator` (vd xanh lá).
6. **Self-FK loop:** RefreshToken.parentTokenId → vẽ vòng cung ngắn từ phải qua trên về trái của chính nó.

## Validation Checklist

- [ ] Mọi FK column trong `schema.sql` có line tương ứng trong drawio
- [ ] 5 line tỏa ra từ User không cross 4+ table
- [ ] Cardinality marker đầu/cuối line đúng (1, N, hoặc 0..1)
- [ ] `OperatorSubscription.operatorId` hiển thị marker 1:1 (không phải N:1)
- [ ] Self-FK loop `RefreshToken.parentTokenId` hiển thị rõ
- [ ] Hub `User` không bị che bởi line đè

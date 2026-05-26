# VietRide — Cross-Service ERD Drawing Master Guide

## Approach

- **KHÔNG vẽ toàn bộ 8 service trong 1 diagram** — sẽ rối khó đọc.
- Mỗi service đã có `schema.drawio` riêng với entity boxes (no edges). User tự vẽ intra-service relation theo `<service>/erd-drawing-guide.md`.
- **Optional v1:** tạo `_global/cross-service-overview.drawio` chứa 8 service box lớn + logical FK lines giữa các service (high-level architecture view). KHÔNG generate file này tự động — tạo manually nếu cần present cho stakeholder.

## Hub Service Identification

Sắp xếp service theo độ "central" — service nào được nhiều service khác tham chiếu nhất ở đáy.

| Service | Inbound logical FK count (approx) | Role |
|---|---|---|
| **Identity & User** | ~30+ (every service refs User/Operator) | Platform hub — bootstrap first |
| **Trip-Route-Vehicle** | ~12 (Booking/Parcel/Tracking ref Trip/Route/Stop/Station) | Operational hub |
| **Booking** | ~4 (Payment.referenceId polymorphic, ShuttlePassenger.bookingId) | Mid-tier |
| **Payment & Wallet** | ~2 (Parcel.additionalPaymentId; intra-service Invoice→Payment) | Settlement |
| **Parcel** | ~2 (Payment refs Parcel.id polymorphic) | Leaf |
| **Tracking** | 0 (chỉ refs Trip outbound) | Leaf |
| **Notification** | 0 (chỉ consume events) | Leaf |
| **RAG AI** | 0 (chỉ refs User outbound) | Isolated |

## Drawing Order for cross-service overview (nếu vẽ)

Nếu user muốn tạo 1 cross-service overview diagram, theo thứ tự sau:

1. **Identity & User** — đặt ở **trung tâm** canvas. Vẽ 1 box lớn label "Identity & User Service" với 2 sub-box bên trong: "User" + "Operator".
2. **Trip-Route-Vehicle** — đặt **bên phải** Identity. Sub-box: "Trip", "Route+Stop", "Vehicle", "DriverSchedule".
3. **Booking** — đặt **giữa** Identity và Trip (trên). Sub-box: "Booking", "Passenger", "Voucher".
4. **Payment & Wallet** — đặt **phía dưới** Identity. Sub-box: "Payment", "PassengerWallet", "PlatformWallet", "OperatorWallet", "OperatorTripSettlement", "Invoice".
5. **Parcel** — đặt **bên phải** Trip. Sub-box: "Parcel", "ParcelRouteFare".
6. **Tracking** — đặt **dưới** Trip. Sub-box: "GpsTrail".
7. **Notification** — đặt **dưới** Identity (trái dưới). Sub-box: "Notification".
8. **RAG AI** — đặt **góc dưới phải** (isolated). Sub-box: "KnowledgeDocument", "RagConversation".

## Logical FK lines (high-level)

Vẽ với màu khác nhau theo direction:

| From service | To service | Lines (approximate) | Color suggestion |
|---|---|---|---|
| Trip-Route-Vehicle | Identity (User/Operator) | 8+ logical FK | blue |
| Booking | Identity (User/Operator) | 4+ logical FK | blue |
| Booking | Trip-Route-Vehicle | 4+ logical FK (tripId, stationIds, stopIds) | red |
| Payment & Wallet | Identity (User/Operator) | 6+ logical FK | blue |
| Payment & Wallet | Booking, Parcel (polymorphic) | dotted line (polymorphic) | purple |
| Parcel | Identity | 6+ logical FK | blue |
| Parcel | Trip-Route-Vehicle | 3 logical FK | red |
| Parcel | Payment & Wallet | 1 logical FK (additionalPaymentId) | purple |
| Tracking | Trip-Route-Vehicle | 1 logical FK (tripId) | red |
| Notification | Identity | 1 logical FK (userId) | blue |
| RAG AI | Identity | 3 logical FK (uploaded/approved/userId) | blue |

## Drawing Tips for cross-service overview

1. **Use thick borders** cho service box (3-4px) để dễ phân biệt với entity-level diagram.
2. **Use service color cheat sheet** từ task spec — vd Identity blue, Trip red, Booking yellow, Payment purple.
3. **Polymorphic line dotted** — `payment.Payment.reference_id` đến Booking/Parcel/etc. vẽ dotted line vì target dynamic.
4. **Group "Sync HTTP" vs "Async event" lines**:
   - Sync HTTP (validate at write): solid line với arrow.
   - Async event (consume): dashed line với arrow.
5. **Label key flows** trên line: vd "validate tripId @ POST /v1/bookings".

## Logical FK list (full reference)

Xem `cross-service-references.md` cho danh sách đầy đủ + enforcement note per FK.

## When to update this guide

- Khi thêm service mới → cập nhật hub identification + drawing order.
- Khi thêm logical FK cross-service mới → cập nhật `cross-service-references.md` trước; high-level lines trong file này chỉ update khi thay đổi đáng kể.
- KHÔNG tạo `cross-service-overview.drawio` tự động — yêu cầu manual creation khi cần.

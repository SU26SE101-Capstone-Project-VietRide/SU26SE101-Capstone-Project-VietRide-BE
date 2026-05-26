# Notification — ERD Drawing Guide

## Statistics
- **Total tables:** 2
- **Total intra-service FK:** 1
- **Hub tables:** `Notification` (1 inbound)
- **Leaf tables:** `NotificationDelivery`

## Recommended Layout Zones

| Zone | Tables | Vị trí gợi ý |
|---|---|---|
| In-app history | `Notification` | trái |
| Delivery audit | `NotificationDelivery` | phải |

## Drawing Order

### Phase 1 — Intra-service
| # | From (FK) | To (PK) | Cardinality | Note |
|---|---|---|---|---|
| 1 | `NotificationDelivery.notificationId` | `Notification.id` | N:1 | CASCADE |

### Phase 2 — Cross-Service Logical FK (KHÔNG vẽ)
- `Notification.userId` → `identity.User.id`

Xem `_global/cross-service-references.md`.

## Drawing Tips

1. 1 line ngang giữa 2 box — đơn giản.
2. Note rằng Notification.type là enum lớn (34 value).

## Validation Checklist

- [ ] 1 line từ NotificationDelivery → Notification (cardinality N:1)
- [ ] Note "NO OutboxEvent — Notification chỉ consume" trong drawing
- [ ] Enum notification_type column hiển thị "enum (34 values)" — không expand

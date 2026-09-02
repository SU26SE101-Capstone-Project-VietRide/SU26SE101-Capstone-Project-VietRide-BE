# BE phản hồi FE — Thông báo yêu cầu gói tùy chỉnh trên Web

**Ngày phản hồi:** 2026-09-03
**Phạm vi:** System Admin Web và Operator Admin Web
**Trạng thái BE:** Đã triển khai

## Kết luận

BE đã bổ sung thông báo chuông Web cho ba mốc của vòng đời yêu cầu gói tùy chỉnh:

1. Nhà xe gửi yêu cầu mới: thông báo tới toàn bộ `SYSTEM_ADMIN` đang hoạt động.
2. System Admin duyệt và tạo gói: thông báo tới các `OPERATOR_ADMIN` đang hoạt động của đúng nhà xe.
3. System Admin từ chối: thông báo tới các `OPERATOR_ADMIN` đang hoạt động của đúng nhà xe.

FE không cần gọi endpoint mới và không có Gateway route mới. Tiếp tục dùng:

- `GET /v1/notifications` để tải chuông thông báo bền vững.
- Socket.IO event `notification:created` để nhận thông báo realtime.

Ba loại thông báo này chỉ dành cho Web. BE không tạo FCM job và không gửi email, vì vậy Mobile không
nên chờ push notification cho luồng này.

## Ba Notification type mới

| `type` | Người nhận | Tiêu đề | Nội dung |
|---|---|---|---|
| `SUBSCRIPTION_CUSTOM_REQUEST_SUBMITTED` | Toàn bộ `SYSTEM_ADMIN` đang hoạt động | `Yêu cầu gói tùy chỉnh mới` | `Nhà xe {operatorName} vừa gửi yêu cầu gói tùy chỉnh và đang chờ xét duyệt.` |
| `SUBSCRIPTION_CUSTOM_REQUEST_APPROVED` | `OPERATOR_ADMIN` đang hoạt động của đúng nhà xe | `Gói tùy chỉnh đã được tạo` | `Gói {planName} đã được tạo. Vui lòng xem chi tiết và thực hiện nâng cấp để kích hoạt.` |
| `SUBSCRIPTION_CUSTOM_REQUEST_REJECTED` | `OPERATOR_ADMIN` đang hoạt động của đúng nhà xe | `Yêu cầu gói tùy chỉnh bị từ chối` | `Yêu cầu gói tùy chỉnh đã bị từ chối. Lý do: {rejectionReason}.` |

Không có thông báo cho `OPERATOR_STAFF`. Không có backfill cho yêu cầu đã phát sinh trước khi BE mới
được triển khai.

## Điều hướng khi bấm thông báo

FE điều hướng bằng trường `action`, không tự suy luận từ `type` hoặc chuỗi `body`.

| Notification type | `action.type` | `action.params` | Màn hình FE cần mở |
|---|---|---|---|
| `SUBSCRIPTION_CUSTOM_REQUEST_SUBMITTED` | `OPEN_ADMIN_SUBSCRIPTION_CUSTOM_REQUEST` | `{ requestId }` | Chi tiết yêu cầu gói tùy chỉnh dành cho System Admin |
| `SUBSCRIPTION_CUSTOM_REQUEST_APPROVED` | `OPEN_SUBSCRIPTION` | `{}` | Màn hình gói đăng ký/nâng cấp của nhà xe |
| `SUBSCRIPTION_CUSTOM_REQUEST_REJECTED` | `OPEN_SUBSCRIPTION` | `{}` | Màn hình gói đăng ký của nhà xe |

Ví dụ xử lý:

```ts
function openNotification(notification: NotificationItem) {
  switch (notification.action.type) {
    case "OPEN_ADMIN_SUBSCRIPTION_CUSTOM_REQUEST":
      return router.push(
        `/admin/subscription-custom-requests/${notification.action.params.requestId}`,
      );

    case "OPEN_SUBSCRIPTION":
      return router.push("/subscriptions");

    default:
      return;
  }
}
```

Các path trong ví dụ chỉ minh họa. FE ánh xạ sang route thực tế của ứng dụng, nhưng phải giữ nguyên
ý nghĩa của `action.type` và `action.params`.

## REST inbox

FE tiếp tục gọi endpoint hiện có qua Gateway:

```http
GET /v1/notifications?unreadOnly=false&page=1&pageSize=20&sortBy=createdAt&sortDir=desc
Authorization: Bearer <access-token>
```

Ví dụ một item System Admin nhận khi nhà xe gửi yêu cầu:

```json
{
  "id": "c23c6b76-09cc-43e3-a209-e4d33e20684d",
  "userId": "bb1e8098-0284-48e4-9ec6-e0e2bbac8907",
  "type": "SUBSCRIPTION_CUSTOM_REQUEST_SUBMITTED",
  "title": "Yêu cầu gói tùy chỉnh mới",
  "body": "Nhà xe VietRide Express vừa gửi yêu cầu gói tùy chỉnh và đang chờ xét duyệt.",
  "data": {
    "eventId": "8f988303-b94a-4ec1-92fc-73692a91b0fb",
    "occurredAt": "2026-09-03T08:15:00.000Z",
    "requestId": "cfca3891-4338-4071-8356-351c7f314b43",
    "operatorId": "76583752-2694-4508-84b6-b69497ff75d3",
    "operatorName": "VietRide Express"
  },
  "action": {
    "type": "OPEN_ADMIN_SUBSCRIPTION_CUSTOM_REQUEST",
    "params": {
      "requestId": "cfca3891-4338-4071-8356-351c7f314b43"
    }
  },
  "readAt": null,
  "createdAt": "2026-09-03T15:15:01+07:00"
}
```

Item nằm trong `data.items` của `ApiResponse` phân trang hiện có. Cấu trúc envelope, cursor và API
đánh dấu đã đọc không thay đổi.

## Realtime Socket.IO

FE tiếp tục kết nối public backend origin bằng namespace mặc định `/`:

```ts
import { io } from "socket.io-client";

const notificationSocket = io(API_ORIGIN, {
  path: "/notification/socket.io",
  auth: { token: accessToken },
});

notificationSocket.on("notification:created", (notification) => {
  // Deduplicate theo notification.id rồi cập nhật inbox và unread count.
});
```

Ví dụ Operator Admin nhận khi yêu cầu được duyệt:

```json
{
  "id": "2897a630-4da5-41d5-9c9e-528779091d6c",
  "type": "SUBSCRIPTION_CUSTOM_REQUEST_APPROVED",
  "title": "Gói tùy chỉnh đã được tạo",
  "body": "Gói Enterprise 2026 đã được tạo. Vui lòng xem chi tiết và thực hiện nâng cấp để kích hoạt.",
  "data": {
    "eventId": "004a17f0-33ae-4744-aab3-874a190a413c",
    "occurredAt": "2026-09-03T08:30:00.000Z",
    "requestId": "cfca3891-4338-4071-8356-351c7f314b43",
    "operatorId": "76583752-2694-4508-84b6-b69497ff75d3",
    "approvedPlanId": "20a6490e-de40-4caf-92d5-609281ff1006",
    "planName": "Enterprise 2026"
  },
  "action": {
    "type": "OPEN_SUBSCRIPTION",
    "params": {}
  },
  "readAt": null,
  "createdAt": "2026-09-03T15:30:01+07:00"
}
```

Ví dụ Operator Admin nhận khi yêu cầu bị từ chối:

```json
{
  "id": "32053428-a748-4710-aadf-8b06395b24fd",
  "type": "SUBSCRIPTION_CUSTOM_REQUEST_REJECTED",
  "title": "Yêu cầu gói tùy chỉnh bị từ chối",
  "body": "Yêu cầu gói tùy chỉnh đã bị từ chối. Lý do: Số lượng xe yêu cầu chưa phù hợp.",
  "data": {
    "eventId": "7c36d39c-8acd-4782-9f08-116289810aa3",
    "occurredAt": "2026-09-03T08:45:00.000Z",
    "requestId": "cfca3891-4338-4071-8356-351c7f314b43",
    "operatorId": "76583752-2694-4508-84b6-b69497ff75d3",
    "rejectionReason": "Số lượng xe yêu cầu chưa phù hợp"
  },
  "action": {
    "type": "OPEN_SUBSCRIPTION",
    "params": {}
  },
  "readAt": null,
  "createdAt": "2026-09-03T15:45:01+07:00"
}
```

Payload realtime là DTO thô, không bọc `ApiResponse` và không có `userId`. Đây là khác biệt đã có
sẵn giữa Socket.IO và REST inbox, không phải thay đổi riêng của Custom Request.

## Đồng bộ và chống trùng

- Khi mở màn hình thông báo hoặc sau mỗi lần Socket.IO reconnect, gọi lại `GET /v1/notifications`.
- Xem REST inbox là nguồn dữ liệu bền vững; Socket.IO chỉ giúp cập nhật ngay lập tức.
- Deduplicate item REST và realtime bằng `notification.id`.
- Không deduplicate bằng `requestId`, vì cùng một yêu cầu có thể có thông báo submitted và một
  thông báo approved hoặc rejected.
- Nếu FE chưa hỗ trợ một `action.type`, vẫn hiển thị notification và bỏ qua điều hướng an toàn.

## Việc FE cần làm

1. Bổ sung ba giá trị `SUBSCRIPTION_CUSTOM_REQUEST_*` vào union/enum Notification type phía Web.
2. Bổ sung action `OPEN_ADMIN_SUBSCRIPTION_CUSTOM_REQUEST` với `params.requestId`.
3. Tái sử dụng action `OPEN_SUBSCRIPTION` hiện có cho approved/rejected.
4. Hiển thị item mới trong chuông Web và cập nhật unread count khi nhận realtime.
5. Không triển khai kỳ vọng FCM/Mobile push cho ba type này.

## Checklist nghiệm thu FE

- Nhà xe gửi yêu cầu: mọi System Admin đang hoạt động thấy notification; Operator Admin không nhận
  notification submitted.
- Bấm notification submitted mở đúng chi tiết theo `requestId`.
- Admin duyệt: chỉ Operator Admin của đúng nhà xe nhận notification và bấm vào mở màn Subscription.
- Admin từ chối: chỉ Operator Admin của đúng nhà xe nhận notification, thấy đúng lý do và bấm vào
  mở màn Subscription.
- Operator Admin của nhà xe khác và `OPERATOR_STAFF` không nhận ba notification này.
- Reconnect không tạo item trùng; event bị lỡ được bù từ REST inbox.
- Không có FCM push hoặc email cho cả ba trường hợp.
- Yêu cầu cũ tạo trước rollout không tự xuất hiện notification.

## Điều kiện rollout

Thứ tự triển khai BE là: chạy migration Notification, deploy Notification consumer/queue, sau đó
deploy Identity producer. FE chỉ nên nghiệm thu ba type mới sau khi các thành phần này đã được deploy
đủ trên cùng môi trường.

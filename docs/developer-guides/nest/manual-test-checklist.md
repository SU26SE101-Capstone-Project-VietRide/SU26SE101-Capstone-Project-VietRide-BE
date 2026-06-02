# Manual Test Checklist — VietRide NestJS

> Checklist dành cho Human (User) để verify API bằng Postman/Thunder Client sau khi AI đã hoàn thành code và E2E test.

Khi AI yêu cầu bạn nghiệm thu (Manual Verify), hãy bật server (`npm run dev:<app>`) và kiểm tra các tiêu chí sau:

## 1. Happy Path
- [ ] **Status Code**: Đúng chuẩn HTTP (200 OK, 201 Created).
- [ ] **Response Body**: Data trả về có đúng shape theo yêu cầu (BSOT / DTO Contract) không? Không được chứa dữ liệu rác hoặc sensitive data (như password hash).

## 2. Authentication & Authorization (Auth)
- [ ] **Missing Token**: Không truyền JWT -> phải trả về `401 Unauthorized`.
- [ ] **Invalid Token**: Truyền JWT sai/hết hạn -> phải trả về `401 Unauthorized`.
- [ ] **Forbidden (nếu có)**: User không đủ role -> phải trả về `403 Forbidden`.

## 3. Validation & Error Handling (RFC 7807 ProblemDetails)
- [ ] **Validation Failed**: Truyền payload sai type/thiếu field -> phải trả về `400 Bad Request`.
- [ ] **Response Shape**: Format lỗi bắt buộc phải tuân theo chuẩn RFC 7807:
  ```json
  {
    "type": "https://httpstatuses.com/400",
    "title": "Bad Request",
    "status": 400,
    "detail": "...",
    "errorCode": "VALIDATION_FAILED"
  }
  ```
- [ ] **ErrorCode**: Thuộc tính `errorCode` luôn phải là UPPER_SNAKE_CASE (VD: `TRIP_NOT_FOUND`, `DUPLICATE_BOOKING`).

## 4. Observability
- [ ] **Headers**: Response Headers bắt buộc phải có `X-Request-Id` (được middleware sinh ra tự động để phục vụ trace log).

---
**Nghiệm thu**: Nếu API pass tất cả các mục trên, bạn có quyền gõ **"đã test ok"** để AI tiến hành đóng Task và commit!

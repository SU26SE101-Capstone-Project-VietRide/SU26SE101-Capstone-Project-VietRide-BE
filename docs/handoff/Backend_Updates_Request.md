# Yêu cầu cập nhật Backend API - Frontend Team (VietRide Mobile Passenger)

Gửi BE Team, dựa vào thiết kế UI và luồng (flow) hiện tại của Mobile App, Frontend team có một số yêu cầu cần điều chỉnh/bổ sung từ phía Backend như sau:

## 1. Cho phép đăng nhập đối với tài khoản chưa xác thực (Unverified Account)

- **Tình trạng hiện tại:** API `POST /v1/auth/login` đang trả về lỗi `403 Forbidden` (`AUTH_EMAIL_NOT_VERIFIED`) và chặn hoàn toàn không cho login nếu `Status` của account là `PENDING_EMAIL_VERIFICATION` (kiểm tra trong `LoginCommandHandler`). FE do đó sẽ không nhận được `accessToken`.
- **Luồng Frontend mong đợi:** Trong thiết kế ứng dụng, nếu account chưa xác thực email, _người dùng vẫn phải login thành công được vào app_. Tuy nhiên, tài khoản sẽ ở trạng thái bị giới hạn (restricted), và ở trang Profile sẽ có chức năng để yêu cầu gửi lại OTP và xác thực email (Verify).
- **Yêu cầu BE:** Không throws exception ở bước này nữa. Hãy vẫn cấp `TokenBundleDto` như bình thường. Frontend sẽ sử dụng field `status` (`PENDING_EMAIL_VERIFICATION`) trong response được trả về từ login (hoặc từ payload của access token) để tự chặn các chức năng liên quan.

## 2. Kiểm tra/Mở quyền gọi API `resend-verification-email` (Không bắt buộc Auth)

- **Tình trạng hiện tại:** Đang có sự cố khi FE gọi API `Resend Verification Email`. Ví dụ khi ở màn hình Đăng ký, user chưa có mật khẩu/token, nếu hệ thống bị lỗi hoặc trễ không gửi OTP tự động, tiến trình resend bị kẹt vì báo chưa login/chưa authorization.
- **Yêu cầu BE:** Kiểm tra lại để chắc chắn API `POST /v1/auth/resend-verification-email` không bị vướng JWT Token Guard/Policy. Mục đích là cho phép dùng tự do mà không cần (và có) `accessToken`. (Lưu ý: FE thấy trong code là đang gắn `[AllowAnonymous]` nhưng quá trình test/call thực tế vẫn gặp vấn đề, BE review kĩ lại Routing/Middleware dùm).

## 3. Cung cấp bộ API cho luồng Quên mật khẩu (Forgot Password)

- **Tình trạng hiện tại:** Bộ API này hoàn toàn chưa có ở `AuthController` hay bất kỳ đâu trong Identity Service. Việc làm Forgot Password của FE hiện đang phải cắm mock data.
- **Yêu cầu BE bổ sung 2 API mới:**
  1. `POST /v1/auth/forgot-password`: Endpoint nhận `email`. Kiểm tra nếu user có tồn tại, hệ thống sẽ trigger hàm tạo và gửi OTP mã 6 số qua email.
  2. `POST /v1/auth/reset-password`: Endpoint hoàn tất, nhận input gồm `email`, `code` (mã OTP user nhập), và `newPassword`. Kiểm tra OTP hợp lệ và update mật khẩu vào Database.

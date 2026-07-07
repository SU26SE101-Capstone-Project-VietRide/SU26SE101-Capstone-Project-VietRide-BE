export interface SetPasswordFallbackOptions {
  /** Custom URL scheme of the mobile app (e.g. "vietride"). Trusted env value. */
  scheme: string;
  /** Google Play URL; section hidden while the app is not on the store. */
  androidStoreUrl?: string | undefined;
}

/**
 * Minimal fallback page for users opening the set-password email link on
 * desktop or without the app installed. The token is NEVER rendered
 * server-side — inline JS reads it from location.search and builds the
 * custom-scheme link on the client.
 */
export function renderSetPasswordFallbackPage(opts: SetPasswordFallbackOptions): string {
  const storeSection = opts.androidStoreUrl
    ? `<a class="btn btn-secondary" href="${opts.androidStoreUrl}">Tải app trên Google Play</a>`
    : `<p class="muted">App hiện chưa có trên cửa hàng ứng dụng — vui lòng liên hệ nhà xe / quản trị viên để nhận bản cài đặt.</p>`;

  return `<!doctype html>
<html lang="vi">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="robots" content="noindex">
<title>Thiết lập mật khẩu — VietRide</title>
<style>
  body { margin: 0; font-family: system-ui, -apple-system, "Segoe UI", Roboto, sans-serif;
         background: #f4f7fb; color: #1a2b3c; display: flex; min-height: 100vh;
         align-items: center; justify-content: center; }
  .card { background: #fff; border-radius: 12px; padding: 32px 28px; max-width: 420px;
          margin: 16px; box-shadow: 0 2px 12px rgba(16, 42, 67, .08); text-align: center; }
  h1 { font-size: 20px; margin: 0 0 8px; }
  p { line-height: 1.5; }
  .btn { display: inline-block; margin: 12px 0; padding: 12px 24px; border-radius: 8px;
         text-decoration: none; font-weight: 600; }
  .btn-primary { background: #208aef; color: #fff; }
  .btn-secondary { background: #e8f1fb; color: #1667b8; }
  .muted { color: #62748a; font-size: 14px; }
  #no-token { display: none; color: #b3261e; font-size: 14px; }
</style>
</head>
<body>
<div class="card">
  <h1>Thiết lập mật khẩu VietRide</h1>
  <p>Mở liên kết này trên điện thoại đã cài app <strong>VietRide Driver</strong> để đặt mật khẩu cho tài khoản của bạn.</p>
  <a id="open-app" class="btn btn-primary" href="#">Mở trong app</a>
  <p id="no-token">Liên kết thiếu mã xác nhận — vui lòng mở đúng liên kết trong email.</p>
  ${storeSection}
  <p class="muted">Liên kết đặt mật khẩu có hiệu lực trong 48 giờ kể từ khi email được gửi.</p>
</div>
<script>
  (function () {
    var token = new URLSearchParams(window.location.search).get('token');
    var btn = document.getElementById('open-app');
    if (token) {
      btn.setAttribute('href', '${opts.scheme}://auth/set-password?token=' + encodeURIComponent(token));
    } else {
      btn.style.display = 'none';
      document.getElementById('no-token').style.display = 'block';
    }
  })();
</script>
</body>
</html>
`;
}

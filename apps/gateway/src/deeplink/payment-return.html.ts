export interface PaymentReturnOptions {
  /** Full Passenger custom deep link, for example vietride://payments/return. */
  appDeepLink: string;
  /** Google Play URL; hidden while the app is not published. */
  androidStoreUrl?: string | undefined;
}

/**
 * Public HTTPS bridge used as VNPay's browser Return URL. VNPay query parameters
 * are never rendered server-side. The browser forwards the signed query string
 * to the read-only Payment status endpoint and to the Passenger custom deep link.
 * Only the VNPay IPN is allowed to mutate Payment state.
 */
export function renderPaymentReturnPage(opts: PaymentReturnOptions): string {
  const deepLinkJson = JSON.stringify(opts.appDeepLink);
  const storeSection = opts.androidStoreUrl
    ? `<a class="btn btn-secondary" href="${opts.androidStoreUrl}">Tải app trên Google Play</a>`
    : '<p class="muted">Nếu ứng dụng chưa được cài đặt, bạn có thể đóng trang này và kiểm tra lại giao dịch sau.</p>';

  return `<!doctype html>
<html lang="vi">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="robots" content="noindex">
<title>Kết quả thanh toán — VietRide</title>
<style>
  body { margin: 0; font-family: system-ui, -apple-system, "Segoe UI", Roboto, sans-serif;
         background: #f4f7fb; color: #1a2b3c; display: flex; min-height: 100vh;
         align-items: center; justify-content: center; }
  .card { background: #fff; border-radius: 12px; padding: 32px 28px; max-width: 440px;
          margin: 16px; box-shadow: 0 2px 12px rgba(16, 42, 67, .08); text-align: center; }
  h1 { font-size: 22px; margin: 0 0 8px; }
  p { line-height: 1.5; }
  .btn { display: inline-block; margin: 12px 4px; padding: 12px 24px; border-radius: 8px;
         text-decoration: none; font-weight: 600; }
  .btn-primary { background: #208aef; color: #fff; }
  .btn-secondary { background: #e8f1fb; color: #1667b8; }
  .muted { color: #62748a; font-size: 14px; }
</style>
</head>
<body>
<div class="card">
  <h1 id="status-title">Đang xác nhận thanh toán</h1>
  <p id="status-message">VietRide đang chờ trạng thái được xác nhận từ IPN của VNPay.</p>
  <a id="open-app" class="btn btn-primary" href="#">Mở VietRide Passenger</a>
  ${storeSection}
  <p class="muted">Bạn không cần thanh toán lại nếu trạng thái đang được xử lý.</p>
</div>
<script>
  (function () {
    var target = ${deepLinkJson} + window.location.search;
    var button = document.getElementById('open-app');
    var title = document.getElementById('status-title');
    var message = document.getElementById('status-message');
    var attempts = 0;
    button.setAttribute('href', target);

    function showStatus(status) {
      if (status === 'SUCCEEDED') {
        title.textContent = 'Thanh toán thành công';
        message.textContent = 'Giao dịch đã được VNPay xác nhận. Bạn có thể mở ứng dụng để xem booking.';
      } else if (status === 'FAILED' || status === 'EXPIRED') {
        title.textContent = 'Thanh toán chưa thành công';
        message.textContent = 'Giao dịch không thành công hoặc đã hết hạn. Vui lòng quay lại ứng dụng để thử lại.';
      } else if (status === 'REFUNDED') {
        title.textContent = 'Giao dịch đã hoàn tiền';
        message.textContent = 'Khoản thanh toán này đã được hoàn tiền.';
      } else {
        title.textContent = 'Đang xác nhận thanh toán';
        message.textContent = 'VNPay đã chuyển bạn về VietRide; hệ thống vẫn đang chờ IPN xác nhận.';
      }
    }

    async function pollStatus() {
      attempts += 1;
      try {
        var response = await fetch('/v1/payments/vnpay-return-status' + window.location.search, {
          cache: 'no-store',
          credentials: 'omit'
        });
        if (response.ok) {
          var body = await response.json();
          var status = body && body.data && body.data.status;
          showStatus(status);
          if (status && status !== 'PENDING_REDIRECT') return;
        }
      } catch (_) {
        // Keep the web fallback usable when polling is temporarily unavailable.
      }
      if (attempts < 20) window.setTimeout(pollStatus, 1500);
    }

    pollStatus();
    if (/Android|iPhone|iPad|iPod/i.test(navigator.userAgent)) {
      window.setTimeout(function () { window.location.href = target; }, 150);
    }
  })();
</script>
</body>
</html>
`;
}

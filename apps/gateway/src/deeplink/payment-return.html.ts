export interface PaymentReturnOptions {
  /** Full Passenger custom deep link, for example vietride://payments/return. */
  appDeepLink: string;
  /** Google Play URL; hidden while the app is not published. */
  androidStoreUrl?: string | undefined;
}

/**
 * Public HTTPS bridge used as VNPay's browser Return URL. VNPay query parameters
 * are never rendered server-side; the browser forwards the current query string
 * to the Passenger app, which then polls the authenticated booking/payment view.
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
  <h1>Đang xác nhận thanh toán</h1>
  <p>VietRide sẽ mở ứng dụng Passenger để kiểm tra trạng thái giao dịch. Kết quả trong ứng dụng được xác nhận từ IPN của VNPay.</p>
  <a id="open-app" class="btn btn-primary" href="#">Mở VietRide Passenger</a>
  ${storeSection}
  <p class="muted">Bạn không cần thanh toán lại nếu trạng thái đang được xử lý.</p>
</div>
<script>
  (function () {
    var target = ${deepLinkJson} + window.location.search;
    var button = document.getElementById('open-app');
    button.setAttribute('href', target);
    if (/Android|iPhone|iPad|iPod/i.test(navigator.userAgent)) {
      window.setTimeout(function () { window.location.href = target; }, 150);
    }
  })();
</script>
</body>
</html>
`;
}

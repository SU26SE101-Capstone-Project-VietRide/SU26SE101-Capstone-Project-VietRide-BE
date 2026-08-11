const UUID_PATTERN =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const YEAR_MONTH_PATTERN = /^(\d{4})-(0[1-9]|1[0-2])$/;

export function formatBookingReference(bookingCode?: string): string {
  const displayCode = displaySnapshot(bookingCode);
  return displayCode ? `#${displayCode}` : 'của bạn';
}

export function formatTripLabel(routeName?: string): string {
  const displayName = displaySnapshot(routeName);
  return displayName ? `Chuyến ${displayName}` : 'Chuyến xe';
}

export function formatParcelLabel(parcelCode?: string): string {
  const displayCode = displaySnapshot(parcelCode);
  return displayCode ? `Đơn ${displayCode}` : 'Đơn gửi hàng';
}

export function formatOperatorLabel(operatorName?: string): string {
  const displayName = displaySnapshot(operatorName);
  return displayName ? `Nhà xe ${displayName}` : 'Nhà xe';
}

export function formatSubscriptionPeriod(periodKey?: string): string {
  const match = periodKey?.trim().match(YEAR_MONTH_PATTERN);
  if (!match) return '';

  const [, year, month] = match;
  return ` trong tháng ${month}/${year}`;
}

function displaySnapshot(value?: string): string | undefined {
  const normalized = value?.trim();
  return normalized && !UUID_PATTERN.test(normalized) ? normalized : undefined;
}

const UUID_PATTERN =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const YEAR_MONTH_PATTERN = /^(\d{4})-(0[1-9]|1[0-2])$/;
const TECHNICAL_CODE_PATTERN = /^[A-Z][A-Z0-9]*(?:_[A-Z0-9]+)*$/;

const RESOURCE_ROLE_LABELS: Readonly<Record<string, string>> = {
  DRIVER: 'Tài xế',
  ASSISTANT: 'Phụ xe',
  VEHICLE: 'Phương tiện',
};

const INCIDENT_CATEGORY_LABELS: Readonly<Record<string, string>> = {
  TRAFFIC_JAM: 'ùn tắc giao thông',
  VEHICLE_BREAKDOWN: 'phương tiện gặp sự cố',
  ACCIDENT: 'tai nạn',
  WEATHER: 'thời tiết xấu',
  OTHER: 'sự cố khác',
};

const SHUTTLE_STATUS_TITLES: Readonly<Record<string, string>> = {
  PICKED_UP: 'Đã đón hành khách',
  DELIVERED: 'Đã trả hành khách',
  NO_SHOW: 'Hành khách không có mặt',
  COMPLETED: 'Chuyến trung chuyển đã hoàn tất',
  CANCELLED: 'Chuyến trung chuyển đã bị hủy',
};

const CANCELLATION_REASON_LABELS: Readonly<Record<string, string>> = {
  USER_INITIATED: 'người dùng chủ động hủy',
  OPERATOR_CANCELLED_TRIP: 'nhà xe đã hủy chuyến',
  OPERATOR_DISRUPTED_IN_PROGRESS: 'chuyến xe bị gián đoạn khi đang vận hành',
  SCHEDULE_CHANGED: 'lịch trình đã thay đổi',
  ROUTE_CHANGED_REFUSED: 'không chấp nhận lộ trình mới',
  VEHICLE_SUBSTITUTION_DOWNGRADE: 'phương tiện thay thế không đáp ứng hạng dịch vụ',
  VEHICLE_SUBSTITUTION_NO_SEAT: 'phương tiện thay thế không còn ghế phù hợp',
  STOP_DISABLED_REFUSED: 'không chấp nhận điểm đón hoặc trả thay thế',
  DRIVER_SCHEDULE_DAY_REMOVED: 'lịch làm việc của tài xế đã bị hủy',
};

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

export function formatResourceRole(role?: string): string {
  const normalized = role?.trim().toUpperCase();
  return (normalized && RESOURCE_ROLE_LABELS[normalized]) || 'Tài nguyên';
}

export function formatIncidentCategory(category?: string): string {
  const normalized = category?.trim().toUpperCase();
  return (normalized && INCIDENT_CATEGORY_LABELS[normalized]) || 'sự cố khác';
}

export function formatShuttleStatusTitle(status?: string): string {
  const normalized = status?.trim().toUpperCase();
  return (
    (normalized && SHUTTLE_STATUS_TITLES[normalized]) ||
    'Trạng thái trung chuyển đã được cập nhật'
  );
}

export function formatCancellationReason(reason?: string): string {
  const normalized = reason?.trim();
  return (normalized && CANCELLATION_REASON_LABELS[normalized.toUpperCase()]) ||
    formatDisplayReason(normalized);
}

export function formatDisplayReason(reason?: string): string {
  const normalized = reason?.trim();
  return normalized && !TECHNICAL_CODE_PATTERN.test(normalized) ? normalized : 'lý do khác';
}

function displaySnapshot(value?: string): string | undefined {
  const normalized = value?.trim();
  return normalized && !UUID_PATTERN.test(normalized) ? normalized : undefined;
}

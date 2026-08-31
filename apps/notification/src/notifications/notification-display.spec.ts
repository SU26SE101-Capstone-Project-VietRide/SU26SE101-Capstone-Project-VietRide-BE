import {
  formatBookingReference,
  formatCancellationReason,
  formatDisplayReason,
  formatIncidentCategory,
  formatOperatorLabel,
  formatParcelLabel,
  formatResourceRole,
  formatShuttleStatusTitle,
  formatSubscriptionPeriod,
  formatTripLabel,
} from './notification-display';

const RAW_UUID = 'a373f602-6529-4eb8-a852-36d1f46ae1af';

describe('notification display fallbacks', () => {
  it('uses human-readable snapshots when available', () => {
    expect(formatBookingReference('VR-20260809-ABCD')).toBe('#VR-20260809-ABCD');
    expect(formatTripLabel('Sài Gòn – Đà Lạt')).toBe('Chuyến Sài Gòn – Đà Lạt');
    expect(formatParcelLabel('VR-PCL-20260809-ABCD')).toBe('Đơn VR-PCL-20260809-ABCD');
    expect(formatOperatorLabel('Việt')).toBe('Nhà xe Việt');
  });

  it('uses natural language instead of identifier fallbacks', () => {
    expect(formatBookingReference()).toBe('của bạn');
    expect(formatTripLabel()).toBe('Chuyến xe');
    expect(formatParcelLabel()).toBe('Đơn gửi hàng');
    expect(formatOperatorLabel()).toBe('Nhà xe');
  });

  it('rejects UUIDs accidentally supplied as display snapshots', () => {
    expect(formatBookingReference(RAW_UUID)).toBe('của bạn');
    expect(formatTripLabel(RAW_UUID)).toBe('Chuyến xe');
    expect(formatParcelLabel(RAW_UUID)).toBe('Đơn gửi hàng');
    expect(formatOperatorLabel(RAW_UUID)).toBe('Nhà xe');
  });

  it('only renders month-shaped subscription period keys', () => {
    expect(formatSubscriptionPeriod('2026-07')).toBe(' trong tháng 07/2026');
    expect(formatSubscriptionPeriod(RAW_UUID)).toBe('');
    expect(formatSubscriptionPeriod('not-a-period')).toBe('');
    expect(formatSubscriptionPeriod()).toBe('');
  });

  it.each([
    ['DRIVER', 'Tài xế'],
    ['ASSISTANT', 'Phụ xe'],
    ['VEHICLE', 'Phương tiện'],
    ['UNKNOWN_ROLE', 'Tài nguyên'],
  ])('formats resource role %s without leaking a technical code', (role, expected) => {
    expect(formatResourceRole(role)).toBe(expected);
  });

  it.each([
    ['TRAFFIC_JAM', 'ùn tắc giao thông'],
    ['VEHICLE_BREAKDOWN', 'phương tiện gặp sự cố'],
    ['ACCIDENT', 'tai nạn'],
    ['WEATHER', 'thời tiết xấu'],
    ['OTHER', 'sự cố khác'],
    ['UNKNOWN_CATEGORY', 'sự cố khác'],
  ])('formats incident category %s in Vietnamese', (category, expected) => {
    expect(formatIncidentCategory(category)).toBe(expected);
  });

  it.each([
    ['PICKED_UP', 'Đã đón hành khách'],
    ['DELIVERED', 'Đã trả hành khách'],
    ['NO_SHOW', 'Hành khách không có mặt'],
    ['COMPLETED', 'Chuyến trung chuyển đã hoàn tất'],
    ['CANCELLED', 'Chuyến trung chuyển đã bị hủy'],
    ['UNKNOWN_STATUS', 'Trạng thái trung chuyển đã được cập nhật'],
  ])('formats shuttle status %s as a user-facing title', (status, expected) => {
    expect(formatShuttleStatusTitle(status)).toBe(expected);
  });

  it.each([
    ['USER_INITIATED', 'người dùng chủ động hủy'],
    ['OPERATOR_CANCELLED_TRIP', 'nhà xe đã hủy chuyến'],
    ['OPERATOR_DISRUPTED_IN_PROGRESS', 'chuyến xe bị gián đoạn khi đang vận hành'],
    ['SCHEDULE_CHANGED', 'lịch trình đã thay đổi'],
    ['ROUTE_CHANGED_REFUSED', 'không chấp nhận lộ trình mới'],
    ['VEHICLE_SUBSTITUTION_DOWNGRADE', 'phương tiện thay thế không đáp ứng hạng dịch vụ'],
    ['VEHICLE_SUBSTITUTION_NO_SEAT', 'phương tiện thay thế không còn ghế phù hợp'],
    ['STOP_DISABLED_REFUSED', 'không chấp nhận điểm đón hoặc trả thay thế'],
    ['DRIVER_SCHEDULE_DAY_REMOVED', 'lịch làm việc của tài xế đã bị hủy'],
    ['UNKNOWN_REASON', 'lý do khác'],
  ])('formats cancellation reason %s without leaking a technical code', (reason, expected) => {
    expect(formatCancellationReason(reason)).toBe(expected);
  });

  it('keeps free-form reasons but hides unknown technical codes', () => {
    expect(formatDisplayReason('Xe cần bảo trì')).toBe('Xe cần bảo trì');
    expect(formatDisplayReason('RESOURCE_ACTIVE')).toBe('lý do khác');
    expect(formatDisplayReason()).toBe('lý do khác');
  });
});

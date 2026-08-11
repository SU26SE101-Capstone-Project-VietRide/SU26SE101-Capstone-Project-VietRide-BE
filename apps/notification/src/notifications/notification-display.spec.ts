import {
  formatBookingReference,
  formatOperatorLabel,
  formatParcelLabel,
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
});

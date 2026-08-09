import {
  formatBookingReference,
  formatOperatorLabel,
  formatParcelLabel,
  formatTripLabel,
} from './notification-display';

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
});

export function formatBookingReference(bookingCode?: string): string {
  return bookingCode ? `#${bookingCode}` : 'của bạn';
}

export function formatTripLabel(routeName?: string): string {
  return routeName ? `Chuyến ${routeName}` : 'Chuyến xe';
}

export function formatParcelLabel(parcelCode?: string): string {
  return parcelCode ? `Đơn ${parcelCode}` : 'Đơn gửi hàng';
}

export function formatOperatorLabel(operatorName?: string): string {
  return operatorName ? `Nhà xe ${operatorName}` : 'Nhà xe';
}

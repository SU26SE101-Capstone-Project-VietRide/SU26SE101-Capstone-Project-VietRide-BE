export type BookingPickupStatus = 'PENDING' | 'PICKED_UP' | 'MISSED';
export type BookingStatus = 'CONFIRMED' | 'CHECKED_IN' | 'CANCELLED' | 'NO_SHOW';

export interface PickupBookingSnapshot {
  bookingId: string;
  stopId: string;
  status: BookingStatus;
  pickupStatus?: BookingPickupStatus;
}

export interface BookingDataProvider {
  getPickupBookings(tripId: string, stopId: string): Promise<PickupBookingSnapshot[]>;
}

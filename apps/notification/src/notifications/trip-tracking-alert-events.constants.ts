export const TRIP_ASSIGNED_ROUTING_KEY = 'trip.trip.assigned';
export const TRIP_CREW_CHANGED_ROUTING_KEY = 'trip.trip.crew_changed';
export const TRIP_BOARDING_STARTED_ROUTING_KEY = 'trip.trip.boarding_started';
export const TRIP_ROUTE_CHANGED_ROUTING_KEY = 'trip.trip.route_changed';
export const TRIP_SCHEDULE_CHANGED_ROUTING_KEY = 'trip.trip.schedule_changed';
export const TRIP_CANCELLED_ROUTING_KEY = 'trip.trip.cancelled';
export const TRIP_DELAYED_ROUTING_KEY = 'trip.trip.delayed';
export const TRIP_INCIDENT_REPORTED_ROUTING_KEY = 'trip.incident.reported';
export const TRIP_STOP_DISABLED_ROUTING_KEY = 'trip.stop.disabled';
export const TRACKING_GPS_OFF_ROUTE_ROUTING_KEY = 'tracking.gps.off_route';
export const TRACKING_GPS_APPROACHING_STOP_ROUTING_KEY = 'tracking.gps.approaching_stop';

export const TRIP_TRACKING_ALERT_QUEUE_BINDINGS = [
  { queue: 'notification:trip-assigned', routingKey: TRIP_ASSIGNED_ROUTING_KEY },
  { queue: 'notification:trip-crew-changed', routingKey: TRIP_CREW_CHANGED_ROUTING_KEY },
  {
    queue: 'notification:trip-boarding-started',
    routingKey: TRIP_BOARDING_STARTED_ROUTING_KEY,
  },
  {
    queue: 'notification:trip-route-changed',
    routingKey: TRIP_ROUTE_CHANGED_ROUTING_KEY,
  },
  {
    queue: 'notification:trip-schedule-changed',
    routingKey: TRIP_SCHEDULE_CHANGED_ROUTING_KEY,
  },
  {
    queue: 'notification:trip-cancelled',
    routingKey: TRIP_CANCELLED_ROUTING_KEY,
  },
  {
    queue: 'notification:trip-delayed',
    routingKey: TRIP_DELAYED_ROUTING_KEY,
  },
  {
    queue: 'notification:trip-incident-reported',
    routingKey: TRIP_INCIDENT_REPORTED_ROUTING_KEY,
  },
  {
    queue: 'notification:trip-stop-disabled',
    routingKey: TRIP_STOP_DISABLED_ROUTING_KEY,
  },
  {
    queue: 'notification:tracking-gps-off-route',
    routingKey: TRACKING_GPS_OFF_ROUTE_ROUTING_KEY,
  },
  {
    queue: 'notification:tracking-gps-approaching-stop',
    routingKey: TRACKING_GPS_APPROACHING_STOP_ROUTING_KEY,
  },
] as const;

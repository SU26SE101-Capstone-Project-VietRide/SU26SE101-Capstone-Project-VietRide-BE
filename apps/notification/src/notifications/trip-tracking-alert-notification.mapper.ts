import { z } from 'zod';
import {
  BookingStopDisabledAffectedEventSchema,
  TRIP_STOP_DEPARTED_WITH_PENDING_ROUTING_KEY,
  TripCargoThresholdCrossedEventSchema,
  TripRouteChangedEventSchema,
  TripScheduleChangedEventSchema,
  TripStopDepartedWithPendingEventSchema,
  TripVehicleSwappedEventSchema,
  type BookingStopDisabledAffectedEvent,
  type TripStopDepartedWithPendingEvent,
} from '@vietride/contracts';
import { NotificationType } from '../generated/notification-prisma-client';
import type { CreateNotificationDto } from './dto/create-notification.dto';
import {
  TRACKING_GPS_APPROACHING_STOP_ROUTING_KEY,
  TRACKING_GPS_OFF_ROUTE_ROUTING_KEY,
  TRIP_ASSIGNED_ROUTING_KEY,
  TRIP_CARGO_THRESHOLD_CROSSED_ROUTING_KEY,
  TRIP_CREW_CHANGED_ROUTING_KEY,
  TRIP_BOARDING_STARTED_ROUTING_KEY,
  TRIP_DELAYED_ROUTING_KEY,
  TRIP_INCIDENT_REPORTED_ROUTING_KEY,
  TRIP_ROUTE_CHANGED_ROUTING_KEY,
  TRIP_SCHEDULE_CHANGED_ROUTING_KEY,
  TRIP_STOP_DISABLED_ROUTING_KEY,
  TRIP_VEHICLE_SWAPPED_ROUTING_KEY,
} from './trip-tracking-alert-events.constants';

const APPROACHING_WAVE_ONE = 1;
const APPROACHING_WAVE_TWO = 2;
const APPROACHING_WAVE_ONE_DEFAULT_ETA_MINUTES = 30;
const APPROACHING_WAVE_TWO_DEFAULT_ETA_MINUTES = 10;

const recipientPayloadSchema = z.object({
  userId: z.string().uuid().optional(),
  userIds: z.array(z.string().uuid()).optional(),
  recipientUserIds: z.array(z.string().uuid()).optional(),
});

const baseTripFieldsSchema = z
  .object({
    tripId: z.string().uuid(),
    routeName: z.string().trim().min(1).optional(),
    reason: z.string().trim().min(1).optional(),
  })
  .passthrough();

const baseTripAlertPayloadSchema = baseTripFieldsSchema
  .merge(recipientPayloadSchema)
  .superRefine((payload, ctx) => {
    if (!payload.userId && !payload.userIds?.length && !payload.recipientUserIds?.length) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'At least one recipient user id is required',
        path: ['userId'],
      });
    }
  });

const tripAssignedPayloadSchema = z.object({
  tripId: z.string().uuid(),
  operatorId: z.string().uuid(),
  driverUserId: z.string().uuid(),
  assistantUserId: z.string().uuid().nullable().optional(),
  routeName: z.string().trim().min(1),
  vehiclePlateNumber: z.string().trim().min(1),
  departureDateTime: z.string().datetime({ offset: true }),
});
const tripCrewChangedPayloadSchema = z.object({
  tripId: z.string().uuid(),
  operatorId: z.string().uuid(),
  oldDriverUserId: z.string().uuid(),
  oldAssistantUserId: z.string().uuid().nullable().optional(),
  driverUserId: z.string().uuid(),
  assistantUserId: z.string().uuid().nullable().optional(),
  routeName: z.string().trim().min(1),
  vehiclePlateNumber: z.string().trim().min(1).nullable().optional(),
  departureDateTime: z.string().datetime({ offset: true }),
});
const boardingStartedPayloadSchema = baseTripAlertPayloadSchema.and(
  z.object({ boardingStartedAt: z.string().datetime().optional() }),
);

const canonicalTripDelayedPayloadSchema = z
  .object({
    eventId: z.string().uuid(),
    occurredAt: z.string().datetime({ offset: true }),
    tripId: z.string().uuid(),
    stopId: z.string().uuid(),
    stopName: z.string().trim().min(1),
    delayMinutes: z.number().int().positive(),
    etaNew: z.string().datetime({ offset: true }),
  })
  .strict();

const legacyTripDelayedPayloadSchema = z
  .object({
    tripId: z.string().uuid(),
    stopId: z.string().uuid(),
    userIds: z.array(z.string().uuid()).optional(),
    staticEstimatedArrivalTime: z.string().datetime({ offset: true }),
    dynamicEstimatedArrivalTime: z.string().datetime({ offset: true }),
    etaNew: z.string().datetime({ offset: true }),
    delayMinutes: z.number().int().positive(),
    detectedAt: z.string().datetime({ offset: true }),
  })
  .strict();

const tripDelayedPayloadSchema = z.union([
  canonicalTripDelayedPayloadSchema,
  legacyTripDelayedPayloadSchema,
]);

const incidentReportedPayloadSchema = z
  .object({
    eventId: z.string().uuid().optional(),
    occurredAt: z.string().datetime({ offset: true }),
    eventType: z.literal(TRIP_INCIDENT_REPORTED_ROUTING_KEY).optional(),
    incidentId: z.string().uuid(),
    tripId: z.string().uuid(),
    operatorId: z.string().uuid(),
    reporterUserId: z.string().uuid(),
    category: z.enum(['TRAFFIC_JAM', 'VEHICLE_BREAKDOWN', 'ACCIDENT', 'WEATHER', 'OTHER']),
    description: z.string().trim().min(1).nullable().optional(),
    photoUrls: z.array(z.string().url()).max(3).nullable().optional(),
    latitude: z.number().min(-90).max(90).nullable().optional(),
    longitude: z.number().min(-180).max(180).nullable().optional(),
    reportedAt: z.string().datetime({ offset: true }),
  })
  .passthrough()
  .superRefine((payload, ctx) => {
    if (
      (payload.latitude !== null && payload.latitude !== undefined) !==
      (payload.longitude !== null && payload.longitude !== undefined)
    ) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'Latitude and longitude must be supplied together',
        path: ['latitude'],
      });
    }
  });

const canonicalOffRoutePayloadSchema = z
  .object({
    eventId: z.string().uuid(),
    occurredAt: z.string().datetime({ offset: true }),
    tripId: z.string().uuid(),
    durationSeconds: z.number().int().positive(),
  })
  .strict();

const legacyOffRoutePayloadSchema = z
  .object({
    tripId: z.string().uuid(),
    userIds: z.array(z.string().uuid()).optional(),
    latitude: z.number().min(-90).max(90),
    longitude: z.number().min(-180).max(180),
    distanceMeters: z.number().nonnegative(),
    durationSeconds: z.number().int().positive(),
    detectedAt: z.string().datetime({ offset: true }),
  })
  .strict();

const offRoutePayloadSchema = z.union([
  canonicalOffRoutePayloadSchema,
  legacyOffRoutePayloadSchema,
]);

const approachingStopPayloadSchema = baseTripAlertPayloadSchema.and(
  z.object({
    stopId: z.string().uuid(),
    stopName: z.string().trim().min(1).optional(),
    bookingIds: z.array(z.string().uuid()).optional(),
    wave: z.union([z.literal(APPROACHING_WAVE_ONE), z.literal(APPROACHING_WAVE_TWO)]),
    etaMinutes: z.number().int().nonnegative().optional(),
    terminal: z.boolean().optional(),
  }),
);

type TripAssignedPayload = z.infer<typeof tripAssignedPayloadSchema>;
type TripCrewChangedPayload = z.infer<typeof tripCrewChangedPayloadSchema>;
type RecipientPayload = z.infer<typeof recipientPayloadSchema>;
type BaseTripAlertPayload = z.infer<typeof baseTripAlertPayloadSchema>;
type ApproachingStopPayload = z.infer<typeof approachingStopPayloadSchema>;
export type IncidentReportedPayload = z.infer<typeof incidentReportedPayloadSchema>;

export { incidentReportedPayloadSchema as IncidentReportedPayloadSchema };

export type TripTrackingAlertRoutingKey =
  | typeof TRIP_ASSIGNED_ROUTING_KEY
  | typeof TRIP_CARGO_THRESHOLD_CROSSED_ROUTING_KEY
  | typeof TRIP_CREW_CHANGED_ROUTING_KEY
  | typeof TRIP_BOARDING_STARTED_ROUTING_KEY
  | typeof TRIP_ROUTE_CHANGED_ROUTING_KEY
  | typeof TRIP_SCHEDULE_CHANGED_ROUTING_KEY
  | typeof TRIP_VEHICLE_SWAPPED_ROUTING_KEY
  | typeof TRIP_DELAYED_ROUTING_KEY
  | typeof TRIP_INCIDENT_REPORTED_ROUTING_KEY
  | typeof TRIP_STOP_DISABLED_ROUTING_KEY
  | typeof TRIP_STOP_DEPARTED_WITH_PENDING_ROUTING_KEY
  | typeof TRACKING_GPS_OFF_ROUTE_ROUTING_KEY
  | typeof TRACKING_GPS_APPROACHING_STOP_ROUTING_KEY;

export function mapTripTrackingAlertToNotifications(
  routingKey: TripTrackingAlertRoutingKey,
  payload: unknown,
  resolvedRecipientUserIds?: string[],
): CreateNotificationDto[] {
  switch (routingKey) {
    case TRIP_ASSIGNED_ROUTING_KEY:
      return mapTripAssigned(tripAssignedPayloadSchema.parse(payload));
    case TRIP_CARGO_THRESHOLD_CROSSED_ROUTING_KEY:
      return mapTripCargoThresholdCrossed(
        TripCargoThresholdCrossedEventSchema.parse(payload),
        [],
      );
    case TRIP_CREW_CHANGED_ROUTING_KEY:
      return mapTripCrewChanged(tripCrewChangedPayloadSchema.parse(payload));
    case TRIP_BOARDING_STARTED_ROUTING_KEY:
      return fanOut(boardingStartedPayloadSchema.parse(payload), mapBoardingStarted);
    case TRIP_ROUTE_CHANGED_ROUTING_KEY:
      return fanOutResolved(
        TripRouteChangedEventSchema.parse(payload),
        resolvedRecipientUserIds,
        mapRouteChanged,
      );
    case TRIP_SCHEDULE_CHANGED_ROUTING_KEY:
      return fanOutResolved(
        TripScheduleChangedEventSchema.parse(payload),
        resolvedRecipientUserIds,
        mapScheduleChanged,
      );
    case TRIP_VEHICLE_SWAPPED_ROUTING_KEY:
      return mapTripVehicleSwapped(TripVehicleSwappedEventSchema.parse(payload));
    case TRIP_DELAYED_ROUTING_KEY:
      return fanOutResolved(
        tripDelayedPayloadSchema.parse(payload),
        resolvedRecipientUserIds,
        mapTripDelayed,
      );
    case TRIP_INCIDENT_REPORTED_ROUTING_KEY: {
      const incident = incidentReportedPayloadSchema.parse(payload);
      const recipients = recipientPayloadSchema.parse(payload);
      return mapIncidentReportedToNotifications(incident, collectRecipientUserIds(recipients));
    }
    case TRIP_STOP_DISABLED_ROUTING_KEY:
      return mapStopDisabled(BookingStopDisabledAffectedEventSchema.parse(payload));
    case TRIP_STOP_DEPARTED_WITH_PENDING_ROUTING_KEY:
      return mapDepartedWithPending(TripStopDepartedWithPendingEventSchema.parse(payload));
    case TRACKING_GPS_OFF_ROUTE_ROUTING_KEY:
      return fanOutResolved(
        offRoutePayloadSchema.parse(payload),
        resolvedRecipientUserIds,
        mapOffRoute,
      );
    case TRACKING_GPS_APPROACHING_STOP_ROUTING_KEY:
      return fanOut(approachingStopPayloadSchema.parse(payload), mapApproachingStop);
  }
}

export interface TripRecipientResolutionRequest {
  tripId: string;
  operatorId?: string;
  affectedBookingIds: string[];
}

export function parseTripRecipientResolutionRequest(
  routingKey: TripTrackingAlertRoutingKey,
  payload: unknown,
): TripRecipientResolutionRequest | null {
  switch (routingKey) {
    case TRIP_DELAYED_ROUTING_KEY:
      return { tripId: tripDelayedPayloadSchema.parse(payload).tripId, affectedBookingIds: [] };
    case TRACKING_GPS_OFF_ROUTE_ROUTING_KEY:
      return { tripId: offRoutePayloadSchema.parse(payload).tripId, affectedBookingIds: [] };
    case TRIP_SCHEDULE_CHANGED_ROUTING_KEY: {
      const event = TripScheduleChangedEventSchema.parse(payload);
      return { tripId: event.tripId, operatorId: event.operatorId, affectedBookingIds: [] };
    }
    case TRIP_ROUTE_CHANGED_ROUTING_KEY: {
      const event = TripRouteChangedEventSchema.parse(payload);
      return {
        tripId: event.tripId,
        operatorId: event.operatorId,
        affectedBookingIds: event.affectedBookings.map((booking) => booking.bookingId),
      };
    }
    default:
      return null;
  }
}

export function mapTripCargoThresholdCrossedToNotifications(
  payload: unknown,
  recipientUserIds: string[],
): CreateNotificationDto[] {
  const parsed = TripCargoThresholdCrossedEventSchema.parse(payload);
  return mapTripCargoThresholdCrossed(parsed, recipientUserIds);
}

function mapTripCargoThresholdCrossed(
  payload: z.infer<typeof TripCargoThresholdCrossedEventSchema>,
  recipientUserIds: string[],
): CreateNotificationDto[] {
  return [...new Set(recipientUserIds)].map((userId) => ({
    userId,
    type: NotificationType.CARGO_NEAR_FULL_ALERT,
    title: 'Khoang hàng gần đầy',
    body: `Chuyến ${payload.tripId} đã sử dụng ${payload.percentFull}% sức chứa khoang hàng (${payload.loadedWeightKg}/${payload.maxCargoWeightKg} kg).`,
    data: {
      eventId: payload.eventId,
      occurredAt: payload.occurredAt,
      tripId: payload.tripId,
      operatorId: payload.operatorId,
      loadedWeightKg: payload.loadedWeightKg,
      maxCargoWeightKg: payload.maxCargoWeightKg,
      percentFull: payload.percentFull,
    },
  }));
}

function mapTripAssigned(payload: TripAssignedPayload): CreateNotificationDto[] {
  return [payload.driverUserId, payload.assistantUserId]
    .filter((userId): userId is string => Boolean(userId))
    .map((userId) => ({
      userId,
      type: NotificationType.TRIP_ASSIGNED,
      title: 'Phân công chuyến mới',
      body: `Bạn được phân công chuyến ${payload.routeName} (${payload.vehiclePlateNumber}).`,
      data: {
        tripId: payload.tripId,
        operatorId: payload.operatorId,
        routeName: payload.routeName,
        vehiclePlateNumber: payload.vehiclePlateNumber,
        departureDateTime: payload.departureDateTime,
      },
    }));
}

function mapTripCrewChanged(payload: TripCrewChangedPayload): CreateNotificationDto[] {
  const previousCrew = new Set(
    [payload.oldDriverUserId, payload.oldAssistantUserId].filter((userId): userId is string =>
      Boolean(userId),
    ),
  );
  const newCrew = new Set(
    [payload.driverUserId, payload.assistantUserId].filter((userId): userId is string =>
      Boolean(userId),
    ),
  );
  const commonData = {
    tripId: payload.tripId,
    operatorId: payload.operatorId,
    routeName: payload.routeName,
    vehiclePlateNumber: payload.vehiclePlateNumber ?? null,
    departureDateTime: payload.departureDateTime,
  };

  const assigned = [...newCrew]
    .filter((userId) => !previousCrew.has(userId))
    .map((userId) => ({
      userId,
      type: NotificationType.TRIP_ASSIGNED,
      title: 'Phân công chuyến mới',
      body: `Bạn được phân công chuyến ${payload.routeName}${payload.vehiclePlateNumber ? ` (${payload.vehiclePlateNumber})` : ''}.`,
      data: commonData,
    }));
  const removed = [...previousCrew]
    .filter((userId) => !newCrew.has(userId))
    .map((userId) => ({
      userId,
      type: NotificationType.TRIP_ASSIGNMENT_REMOVED,
      title: 'Điều chỉnh phân công chuyến',
      body: `Bạn không còn được phân công chuyến ${payload.routeName}.`,
      data: commonData,
    }));

  return [...assigned, ...removed];
}
function mapBoardingStarted(
  userId: string,
  payload: z.infer<typeof boardingStartedPayloadSchema>,
): CreateNotificationDto {
  return {
    userId,
    type: NotificationType.TRIP_BOARDING_REMINDER,
    title: 'Chuyến xe bắt đầu đón khách',
    body: `${formatTripLabel(payload)} đã bắt đầu lên xe. Vui lòng sẵn sàng tại điểm đón.`,
    data: buildTripData(payload),
  };
}

function mapRouteChanged(
  userId: string,
  payload: z.infer<typeof TripRouteChangedEventSchema>,
): CreateNotificationDto {
  return {
    userId,
    type: NotificationType.TRIP_ROUTE_CHANGED,
    title: 'Lộ trình chuyến xe đã thay đổi',
    body: `${formatTripLabel(payload)} có điều chỉnh lộ trình. Vui lòng kiểm tra thông tin mới.`,
    data: buildTripData(payload),
  };
}

function mapScheduleChanged(
  userId: string,
  payload: z.infer<typeof TripScheduleChangedEventSchema>,
): CreateNotificationDto {
  return {
    userId,
    type: NotificationType.TRIP_SCHEDULE_CHANGED,
    title: 'Lịch làm việc của chuyến xe đã thay đổi',
    body: `Giờ khởi hành chuyến ${payload.tripId} đã đổi sang ${payload.newDeparture}.`,
    data: {
      eventId: payload.eventId,
      occurredAt: payload.occurredAt,
      tripId: payload.tripId,
      operatorId: payload.operatorId,
      oldDeparture: payload.oldDeparture,
      newDeparture: payload.newDeparture,
      severity: payload.severity,
    },
  };
}

function mapTripVehicleSwapped(
  payload: z.infer<typeof TripVehicleSwappedEventSchema>,
): CreateNotificationDto[] {
  const crewUserIds = [
    ...new Set([payload.driverUserId, payload.assistantUserId].filter(isString)),
  ];

  return crewUserIds.map((userId) => ({
    userId,
    type: NotificationType.VEHICLE_SWAPPED,
    title: 'Phương tiện chuyến xe đã thay đổi',
    body: `Phương tiện chuyến ${payload.tripId} đã đổi từ ${payload.oldVehiclePlateNumber} sang ${payload.newVehiclePlateNumber}.`,
    data: {
      eventId: payload.eventId,
      occurredAt: payload.occurredAt,
      tripId: payload.tripId,
      operatorId: payload.operatorId,
      oldVehicleId: payload.oldVehicleId,
      newVehicleId: payload.newVehicleId,
      oldVehiclePlateNumber: payload.oldVehiclePlateNumber,
      newVehiclePlateNumber: payload.newVehiclePlateNumber,
      departureDateTime: payload.departureDateTime,
      driverUserId: payload.driverUserId,
      assistantUserId: payload.assistantUserId,
      seatImpacts: payload.seatImpacts,
    },
  }));
}

function mapTripDelayed(
  userId: string,
  payload: z.infer<typeof tripDelayedPayloadSchema>,
): CreateNotificationDto {
  const delayText = payload.delayMinutes ? ` Dự kiến trễ ${payload.delayMinutes} phút.` : '';

  return {
    userId,
    type: NotificationType.TRIP_DELAYED,
    title: 'Chuyến xe bị trễ',
    body: `${formatTripLabel(payload)} đang bị trễ.${delayText}`,
    data: buildTripData(payload),
  };
}

export function mapIncidentReportedToNotifications(
  payload: IncidentReportedPayload,
  recipientUserIds: string[],
): CreateNotificationDto[] {
  return [...new Set(recipientUserIds)].map((userId) => ({
    userId,
    type: NotificationType.INCIDENT_REPORTED,
    title: 'Có sự cố trên chuyến xe',
    body: `Chuyến ${payload.tripId} vừa ghi nhận sự cố: ${payload.category}.`,
    data: {
      incidentId: payload.incidentId,
      tripId: payload.tripId,
      operatorId: payload.operatorId,
      reporterUserId: payload.reporterUserId,
      category: payload.category,
      reportedAt: payload.reportedAt,
    },
  }));
}

function mapOffRoute(
  userId: string,
  payload: z.infer<typeof offRoutePayloadSchema>,
): CreateNotificationDto {
  return {
    userId,
    type: NotificationType.OFF_ROUTE_ALERT,
    title: 'Cảnh báo xe lệch lộ trình',
    body: `${formatTripLabel(payload)} đang có dấu hiệu lệch lộ trình.`,
    data: buildTripData(payload),
  };
}

function mapApproachingStop(
  userId: string,
  payload: ApproachingStopPayload,
): CreateNotificationDto {
  const stopLabel = payload.stopName ?? 'điểm đón';
  const etaMinutes =
    payload.etaMinutes ??
    (payload.wave === APPROACHING_WAVE_ONE
      ? APPROACHING_WAVE_ONE_DEFAULT_ETA_MINUTES
      : APPROACHING_WAVE_TWO_DEFAULT_ETA_MINUTES);

  return {
    userId,
    type: NotificationType.TRIP_VEHICLE_APPROACHING,
    title: payload.wave === APPROACHING_WAVE_ONE ? 'Xe sắp đến điểm đón' : 'Xe đang đến rất gần',
    body:
      payload.wave === APPROACHING_WAVE_ONE
        ? `Xe của bạn sẽ đến ${stopLabel} trong khoảng ${etaMinutes} phút.`
        : `Xe của bạn sắp đến ${stopLabel}! Vui lòng ra điểm đón.`,
    data: buildTripData(payload),
  };
}

function mapStopDisabled(payload: BookingStopDisabledAffectedEvent): CreateNotificationDto[] {
  const stopLabel = `điểm dừng ${payload.stopId}`;
  const replacementText = payload.replacedByStopId
    ? ` Điểm dừng thay thế: ${payload.replacedByStopId}.`
    : '';

  return payload.recipientUserIds.map((userId) => ({
    userId: userId,
    type: NotificationType.STOP_DISABLED,
    title: 'Điểm dừng tạm ngưng phục vụ',
    body: `${stopLabel} tạm ngưng phục vụ.${replacementText}`,
    data: {
      eventId: payload.eventId,
      occurredAt: payload.occurredAt,
      eventType: payload.eventType,
      stopId: payload.stopId,
      replacedByStopId: payload.replacedByStopId ?? null,
      affectedBookingCount: payload.affectedBookingCount,
    },
  }));
}

function mapDepartedWithPending(
  payload: TripStopDepartedWithPendingEvent,
): CreateNotificationDto[] {
  const crewUserIds = [
    ...new Set([payload.driverUserId, payload.assistantUserId].filter(isString)),
  ];

  return crewUserIds.map((userId) => ({
    userId,
    type: NotificationType.DRIVER_STOP_DEPARTED_WITH_PENDING,
    title: 'Cảnh báo hành khách chưa lên xe',
    body: `Xe đã rời ${payload.stopName} khi còn ${payload.pendingPassengerCount} hành khách chưa lên xe.`,
    data: {
      eventId: payload.eventId,
      occurredAt: payload.occurredAt,
      eventType: payload.eventType,
      tripId: payload.tripId,
      stopId: payload.stopId,
      stopName: payload.stopName,
      pendingPassengerCount: payload.pendingPassengerCount,
      driverUserId: payload.driverUserId,
      assistantUserId: payload.assistantUserId,
      departedAt: payload.departedAt,
    },
  }));
}

function fanOut<TPayload extends RecipientPayload>(
  payload: TPayload,
  mapper: (userId: string, payload: TPayload) => CreateNotificationDto,
): CreateNotificationDto[] {
  return collectRecipientUserIds(payload).map((userId) => mapper(userId, payload));
}

function fanOutResolved<TPayload extends object>(
  payload: TPayload,
  resolvedRecipientUserIds: string[] | undefined,
  mapper: (userId: string, payload: TPayload) => CreateNotificationDto,
): CreateNotificationDto[] {
  const recipients =
    resolvedRecipientUserIds ??
    collectRecipientUserIds(recipientPayloadSchema.parse(payload));
  return [...new Set(recipients)].map((userId) => mapper(userId, payload));
}

function collectRecipientUserIds(payload: RecipientPayload): string[] {
  return [
    ...new Set(
      [payload.userId, ...(payload.userIds ?? []), ...(payload.recipientUserIds ?? [])].filter(
        isString,
      ),
    ),
  ];
}

function isString(value: unknown): value is string {
  return typeof value === 'string';
}

function formatTripLabel(payload: BaseTripAlertPayload): string {
  return payload.routeName ? `Chuyến ${payload.routeName}` : `Chuyến ${payload.tripId}`;
}

function buildTripData(payload: BaseTripAlertPayload): Record<string, unknown> {
  const { userId, userIds, recipientUserIds, ...data } = payload;
  void userId;
  void userIds;
  void recipientUserIds;
  return data;
}

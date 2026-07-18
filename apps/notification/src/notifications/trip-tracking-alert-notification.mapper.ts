import { z } from 'zod';
import { TripVehicleSwappedEventSchema } from '@vietride/contracts';
import { NotificationType } from '../generated/notification-prisma-client';
import type { CreateNotificationDto } from './dto/create-notification.dto';
import {
  TRACKING_GPS_APPROACHING_STOP_ROUTING_KEY,
  TRACKING_GPS_OFF_ROUTE_ROUTING_KEY,
  TRIP_ASSIGNED_ROUTING_KEY,
  TRIP_CREW_CHANGED_ROUTING_KEY,
  TRIP_BOARDING_STARTED_ROUTING_KEY,
  TRIP_DELAYED_ROUTING_KEY,
  TRIP_INCIDENT_REPORTED_ROUTING_KEY,
  TRIP_ROUTE_CHANGED_ROUTING_KEY,
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

const baseTripAlertPayloadSchema = z
  .object({
    tripId: z.string().uuid(),
    routeName: z.string().trim().min(1).optional(),
    reason: z.string().trim().min(1).optional(),
  })
  .merge(recipientPayloadSchema)
  .passthrough()
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

const routeChangedPayloadSchema = baseTripAlertPayloadSchema.and(
  z.object({
    alternativeRouteId: z.string().uuid().optional(),
    affectedBookingIds: z.array(z.string().uuid()).optional(),
  }),
);

const tripDelayedPayloadSchema = baseTripAlertPayloadSchema.and(
  z.object({
    delayMinutes: z.number().int().nonnegative().optional(),
    etaNew: z.string().datetime().optional(),
  }),
);

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
    if ((payload.latitude !== null && payload.latitude !== undefined) !==
        (payload.longitude !== null && payload.longitude !== undefined)) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'Latitude and longitude must be supplied together',
        path: ['latitude'],
      });
    }
  });

const offRoutePayloadSchema = baseTripAlertPayloadSchema.and(
  z.object({ durationSeconds: z.number().int().nonnegative().optional() }),
);

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

const stopDisabledPayloadSchema = z
  .object({
    stopId: z.string().uuid(),
    replacedByStopId: z.string().uuid().optional(),
    stopName: z.string().trim().min(1).optional(),
    routeName: z.string().trim().min(1).optional(),
    reason: z.string().trim().min(1).optional(),
  })
  .merge(recipientPayloadSchema)
  .passthrough()
  .superRefine((payload, ctx) => {
    if (!payload.userId && !payload.userIds?.length && !payload.recipientUserIds?.length) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'At least one recipient user id is required',
        path: ['userId'],
      });
    }
  });

type TripAssignedPayload = z.infer<typeof tripAssignedPayloadSchema>;
type TripCrewChangedPayload = z.infer<typeof tripCrewChangedPayloadSchema>;
type RecipientPayload = z.infer<typeof recipientPayloadSchema>;
type BaseTripAlertPayload = z.infer<typeof baseTripAlertPayloadSchema>;
type ApproachingStopPayload = z.infer<typeof approachingStopPayloadSchema>;
type StopDisabledPayload = z.infer<typeof stopDisabledPayloadSchema>;
export type IncidentReportedPayload = z.infer<typeof incidentReportedPayloadSchema>;

export { incidentReportedPayloadSchema as IncidentReportedPayloadSchema };

export type TripTrackingAlertRoutingKey =
  | typeof TRIP_ASSIGNED_ROUTING_KEY
  | typeof TRIP_CREW_CHANGED_ROUTING_KEY
  | typeof TRIP_BOARDING_STARTED_ROUTING_KEY
  | typeof TRIP_ROUTE_CHANGED_ROUTING_KEY
  | typeof TRIP_VEHICLE_SWAPPED_ROUTING_KEY
  | typeof TRIP_DELAYED_ROUTING_KEY
  | typeof TRIP_INCIDENT_REPORTED_ROUTING_KEY
  | typeof TRIP_STOP_DISABLED_ROUTING_KEY
  | typeof TRACKING_GPS_OFF_ROUTE_ROUTING_KEY
  | typeof TRACKING_GPS_APPROACHING_STOP_ROUTING_KEY;

export function mapTripTrackingAlertToNotifications(
  routingKey: TripTrackingAlertRoutingKey,
  payload: unknown,
): CreateNotificationDto[] {
  switch (routingKey) {
    case TRIP_ASSIGNED_ROUTING_KEY:
      return mapTripAssigned(tripAssignedPayloadSchema.parse(payload));
    case TRIP_CREW_CHANGED_ROUTING_KEY:
      return mapTripCrewChanged(tripCrewChangedPayloadSchema.parse(payload));
    case TRIP_BOARDING_STARTED_ROUTING_KEY:
      return fanOut(boardingStartedPayloadSchema.parse(payload), mapBoardingStarted);
    case TRIP_ROUTE_CHANGED_ROUTING_KEY:
      return fanOut(routeChangedPayloadSchema.parse(payload), mapRouteChanged);
    case TRIP_VEHICLE_SWAPPED_ROUTING_KEY:
      return mapTripVehicleSwapped(TripVehicleSwappedEventSchema.parse(payload));
    case TRIP_DELAYED_ROUTING_KEY:
      return fanOut(tripDelayedPayloadSchema.parse(payload), mapTripDelayed);
    case TRIP_INCIDENT_REPORTED_ROUTING_KEY: {
      const incident = incidentReportedPayloadSchema.parse(payload);
      const recipients = recipientPayloadSchema.parse(payload);
      return mapIncidentReportedToNotifications(incident, collectRecipientUserIds(recipients));
    }
    case TRIP_STOP_DISABLED_ROUTING_KEY:
      return fanOut(stopDisabledPayloadSchema.parse(payload), mapStopDisabled);
    case TRACKING_GPS_OFF_ROUTE_ROUTING_KEY:
      return fanOut(offRoutePayloadSchema.parse(payload), mapOffRoute);
    case TRACKING_GPS_APPROACHING_STOP_ROUTING_KEY:
      return fanOut(approachingStopPayloadSchema.parse(payload), mapApproachingStop);
  }
}

function mapTripAssigned(payload: TripAssignedPayload): CreateNotificationDto[] {
  return [payload.driverUserId, payload.assistantUserId]
    .filter((userId): userId is string => Boolean(userId))
    .map((userId) => ({
      userId,
      type: NotificationType.TRIP_ASSIGNED,
      title: 'Ph?n c?ng chuy?n m?i',
      body: `B?n ???c ph?n c?ng chuy?n ${payload.routeName} (${payload.vehiclePlateNumber}).`,
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
    title: 'Chuyen xe bat dau don khach',
    body: `${formatTripLabel(payload)} da bat dau len xe. Vui long san sang tai diem don.`,
    data: buildTripData(payload),
  };
}

function mapRouteChanged(
  userId: string,
  payload: z.infer<typeof routeChangedPayloadSchema>,
): CreateNotificationDto {
  return {
    userId,
    type: NotificationType.TRIP_ROUTE_CHANGED,
    title: 'Lo trinh chuyen xe da thay doi',
    body: `${formatTripLabel(payload)} co dieu chinh lo trinh. Vui long kiem tra thong tin moi.`,
    data: buildTripData(payload),
  };
}

function mapTripVehicleSwapped(
  payload: z.infer<typeof TripVehicleSwappedEventSchema>,
): CreateNotificationDto[] {
  const crewUserIds = [...new Set([payload.driverUserId, payload.assistantUserId].filter(isString))];

  return crewUserIds.map((userId) => ({
    userId,
    type: NotificationType.VEHICLE_SWAPPED,
    title: 'Phuong tien chuyen xe da thay doi',
    body: `Phuong tien chuyen ${payload.tripId} da doi tu ${payload.oldVehiclePlateNumber} sang ${payload.newVehiclePlateNumber}.`,
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
  const delayText = payload.delayMinutes ? ` Du kien tre ${payload.delayMinutes} phut.` : '';

  return {
    userId,
    type: NotificationType.TRIP_DELAYED,
    title: 'Chuyen xe bi tre',
    body: `${formatTripLabel(payload)} dang bi tre.${delayText}`,
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
    title: 'Canh bao xe lech lo trinh',
    body: `${formatTripLabel(payload)} dang co dau hieu lech lo trinh.`,
    data: buildTripData(payload),
  };
}

function mapApproachingStop(
  userId: string,
  payload: ApproachingStopPayload,
): CreateNotificationDto {
  const stopLabel = payload.stopName ?? 'diem don';
  const etaMinutes =
    payload.etaMinutes ??
    (payload.wave === APPROACHING_WAVE_ONE
      ? APPROACHING_WAVE_ONE_DEFAULT_ETA_MINUTES
      : APPROACHING_WAVE_TWO_DEFAULT_ETA_MINUTES);

  return {
    userId,
    type: NotificationType.TRIP_VEHICLE_APPROACHING,
    title: payload.wave === APPROACHING_WAVE_ONE ? 'Xe sap den diem don' : 'Xe dang den rat gan',
    body:
      payload.wave === APPROACHING_WAVE_ONE
        ? `Xe cua ban se den ${stopLabel} trong khoang ${etaMinutes} phut.`
        : `Xe cua ban sap den ${stopLabel}! Vui long ra diem don.`,
    data: buildTripData(payload),
  };
}

function mapStopDisabled(userId: string, payload: StopDisabledPayload): CreateNotificationDto {
  const stopLabel = payload.stopName ?? `diem dung ${payload.stopId}`;
  const replacementText = payload.replacedByStopId
    ? ` Diem dung thay the: ${payload.replacedByStopId}.`
    : '';

  return {
    userId,
    type: NotificationType.STOP_DISABLED,
    title: 'Diem dung tam ngung phuc vu',
    body: `${stopLabel} tam ngung phuc vu.${replacementText}`,
    data: buildStopData(payload),
  };
}

function fanOut<TPayload extends RecipientPayload>(
  payload: TPayload,
  mapper: (userId: string, payload: TPayload) => CreateNotificationDto,
): CreateNotificationDto[] {
  return collectRecipientUserIds(payload).map((userId) => mapper(userId, payload));
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
  return payload.routeName ? `Chuyen ${payload.routeName}` : `Chuyen ${payload.tripId}`;
}

function buildTripData(payload: BaseTripAlertPayload): Record<string, unknown> {
  const { userId, userIds, recipientUserIds, ...data } = payload;

  return {
    ...data,
    userId: userId ?? null,
    userIds: userIds ?? null,
    recipientUserIds: recipientUserIds ?? null,
  };
}

function buildStopData(payload: StopDisabledPayload): Record<string, unknown> {
  const { userId, userIds, recipientUserIds, ...data } = payload;

  return {
    ...data,
    userId: userId ?? null,
    userIds: userIds ?? null,
    recipientUserIds: recipientUserIds ?? null,
  };
}

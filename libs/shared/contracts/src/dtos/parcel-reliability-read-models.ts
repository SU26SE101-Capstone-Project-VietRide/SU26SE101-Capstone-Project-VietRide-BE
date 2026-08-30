import { z } from 'zod';

const nullableUuid = z.string().uuid().nullable();
const nullableDateTime = z.string().datetime({ offset: true }).nullable();

export const ParcelReliabilityLocationSchema = z.object({
  type: z.string().nullable(),
  id: nullableUuid,
  name: z.string().nullable(),
  orderIndex: z.number().int().nullable(),
  eta: nullableDateTime,
});

export const ParcelReliabilityRouteSchema = z.object({
  routeId: z.string().uuid(),
  name: z.string(),
  origin: ParcelReliabilityLocationSchema,
  destination: ParcelReliabilityLocationSchema,
});

export const ParcelReliabilityVehicleSchema = z.object({
  vehicleId: z.string().uuid(),
  licensePlate: z.string(),
  status: z.string().nullable(),
});

export const ParcelReliabilityTripStopSchema = z.object({
  stopId: z.string().uuid(),
  name: z.string(),
  orderIndex: z.number().int(),
  estimatedArrivalAt: z.string().datetime({ offset: true }),
  status: z.string(),
  actualArrivalAt: nullableDateTime,
  actualDepartureAt: nullableDateTime,
});

export const ParcelReliabilityTripSchema = z.object({
  tripId: z.string().uuid(),
  status: z.string().nullable(),
  departureAt: nullableDateTime,
  eta: nullableDateTime,
  route: ParcelReliabilityRouteSchema.nullable(),
  vehicle: ParcelReliabilityVehicleSchema.nullable(),
  stops: z.array(ParcelReliabilityTripStopSchema),
});

export const ParcelReliabilityCustodySchema = z.object({
  lastEventType: z.string(),
  lastConfirmedLocation: ParcelReliabilityLocationSchema,
  lastConfirmedAt: z.string().datetime({ offset: true }),
  currentTripId: nullableUuid,
  currentVehicleId: nullableUuid,
  trackingConfidence: z.enum([
    'CONFIRMED_SCAN',
    'MANUAL_EXCEPTION',
    'INFERRED_FROM_MANIFEST',
    'UNKNOWN',
  ]),
  hasTrackingGap: z.boolean(),
});

export const ParcelReliabilityIncidentSummarySchema = z.object({
  incidentId: z.string().uuid(),
  type: z.string(),
  status: z.string(),
  searchDeadline: nullableDateTime,
  nextUpdateAt: nullableDateTime,
  slaState: z.string(),
  operatorProcessBreach: z.boolean(),
});

export const ParcelReliabilityClaimSummarySchema = z.object({
  claimId: z.string().uuid(),
  status: z.string(),
  totalAwardVnd: z.number().int(),
  decisionDeadline: nullableDateTime,
  payoutDeadline: nullableDateTime,
  slaState: z.string().nullable(),
});

export const ParcelReliabilityParcelSummarySchema = z.object({
  parcelId: z.string().uuid(),
  parcelCode: z.string(),
  status: z.string(),
  description: z.string().nullable(),
  photoUrl: z.string().url().nullable(),
  quantity: z.number().int().positive(),
  declaredValueVnd: z.number().int().nonnegative().nullable(),
});

export const AssistantParcelActionResponseSchema = z.object({
  parcelState: z.object({
    parcelId: z.string().uuid(),
    parcelCode: z.string(),
    status: z.string(),
    dropoffLocation: ParcelReliabilityLocationSchema,
    paymentState: z.object({
      depositRequiredVnd: z.number().int(),
      depositPaidVnd: z.number().int(),
      balanceRequiredVnd: z.number().int(),
      balancePaidVnd: z.number().int(),
      finalPaymentDeadline: nullableDateTime,
      isFullyPaid: z.boolean(),
    }),
    identityCheckHints: z.object({
      photoUrl: z.string().url().nullable(),
      description: z.string().nullable(),
      estimatedWeightKg: z.number(),
      actualWeightKg: z.number().nullable(),
      estimatedLengthCm: z.number(),
      estimatedWidthCm: z.number(),
      estimatedHeightCm: z.number(),
      actualLengthCm: z.number().nullable(),
      actualWidthCm: z.number().nullable(),
      actualHeightCm: z.number().nullable(),
    }),
  }),
  currentCustody: ParcelReliabilityCustodySchema.nullable(),
  activeIncident: ParcelReliabilityIncidentSummarySchema.nullable(),
  createdCustodyEvent: z
    .object({
      eventId: z.string().uuid(),
      eventType: z.string(),
      actualLocationType: z.string().nullable(),
      actualLocationId: nullableUuid,
      locationSnapshot: z.string().nullable(),
      occurredAt: z.string().datetime({ offset: true }),
      sequence: z.number().int().positive(),
    })
    .nullable(),
  availableActions: z.array(z.string()),
  warning: z.string().nullable(),
});

export const AssistantParcelManifestSchema = z.object({
  tripContext: z.object({
    trip: ParcelReliabilityTripSchema,
    currentOperationalLocation: z
      .object({
        location: ParcelReliabilityLocationSchema,
        status: z.string(),
        actualArrivalAt: nullableDateTime,
        actualDepartureAt: nullableDateTime,
      })
      .nullable(),
    orderedStops: z.array(ParcelReliabilityTripStopSchema),
  }),
  summary: z.object({
    total: z.number().int(),
    checkedIn: z.number().int(),
    loaded: z.number().int(),
    expectedAtCurrentStop: z.number().int(),
    unloaded: z.number().int(),
    exceptionCount: z.number().int(),
    unresolvedCount: z.number().int(),
  }),
  items: z.array(
    z
      .object({
        parcelId: z.string().uuid(),
        parcelCode: z.string(),
        status: z.string(),
        dropoffLocation: ParcelReliabilityLocationSchema.nullable(),
        currentCustody: ParcelReliabilityCustodySchema.nullable(),
        activeIncident: ParcelReliabilityIncidentSummarySchema.nullable(),
        custodyExceptionApproval: z
          .object({
            requestId: z.string().uuid(),
            incidentId: z.string().uuid(),
            incidentType: z.string(),
            status: z.string(),
            reason: z.string(),
            reportedAt: z.string().datetime({ offset: true }),
          })
          .nullable(),
        availableActions: z.array(z.string()).nullable(),
      })
      .passthrough(),
  ),
  pagination: z.object({
    page: z.number().int().positive(),
    pageSize: z.number().int().positive(),
    totalItems: z.number().int().nonnegative(),
    totalPages: z.number().int().nonnegative(),
    hasNextPage: z.boolean(),
    hasPreviousPage: z.boolean(),
  }),
});

export const OperatorParcelIncidentListItemSchema = z
  .object({
    incidentId: z.string().uuid(),
    parcelId: z.string().uuid(),
    operatorId: z.string().uuid(),
    type: z.string(),
    status: z.string(),
    parcel: ParcelReliabilityParcelSummarySchema.nullable(),
    trip: ParcelReliabilityTripSchema.nullable(),
    expectedDropoff: ParcelReliabilityLocationSchema.nullable(),
    lastCustody: ParcelReliabilityCustodySchema.nullable(),
    claimSummary: ParcelReliabilityClaimSummarySchema.nullable(),
    availableActions: z.array(z.string()).nullable(),
  })
  .passthrough();

export const ParcelTransitLegSchema = z.object({
  legId: z.string().uuid(),
  tripId: z.string().uuid(),
  sequence: z.number().int().positive(),
  status: z.string(),
  expectedOriginId: nullableUuid,
  expectedDestinationId: nullableUuid,
  expectedOriginName: z.string().nullable(),
  expectedDestinationName: z.string().nullable(),
  vehicleId: nullableUuid,
  startedAt: nullableDateTime,
  endedAt: nullableDateTime,
});

export const ParcelForwardingOperationSchema = z.object({
  targetTrip: ParcelReliabilityTripSchema,
  newLeg: ParcelTransitLegSchema.nullable(),
  cargoTransferStatus: z.enum([
    'AWAITING_CREW_CONFIRMATION',
    'TRANSFERRED',
  ]),
  nextHandoffAction: z.enum([
    'CREW_CONFIRM_TRANSFER',
    'DELIVER_AT_EXPECTED_DROPOFF',
  ]),
});

export const OperatorParcelIncidentDetailSchema = z
  .object({
    incident: OperatorParcelIncidentListItemSchema,
    forwardingSummary: ParcelReliabilityTripSchema.nullable(),
    forwardingOperation: ParcelForwardingOperationSchema.nullable(),
    availableActions: z.array(z.string()).nullable(),
  })
  .passthrough();

export type ParcelReliabilityLocation = z.infer<typeof ParcelReliabilityLocationSchema>;
export type ParcelReliabilityTrip = z.infer<typeof ParcelReliabilityTripSchema>;
export type ParcelReliabilityCustody = z.infer<typeof ParcelReliabilityCustodySchema>;
export type ParcelReliabilityIncidentSummary = z.infer<
  typeof ParcelReliabilityIncidentSummarySchema
>;
export type AssistantParcelActionResponse = z.infer<typeof AssistantParcelActionResponseSchema>;
export type AssistantParcelManifest = z.infer<typeof AssistantParcelManifestSchema>;
export type OperatorParcelIncidentListItem = z.infer<
  typeof OperatorParcelIncidentListItemSchema
>;
export type ParcelForwardingOperation = z.infer<typeof ParcelForwardingOperationSchema>;
export type OperatorParcelIncidentDetail = z.infer<
  typeof OperatorParcelIncidentDetailSchema
>;

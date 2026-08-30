import { AssistantParcelManifestSchema } from '../parcel-reliability-read-models';

const id = '11111111-1111-4111-8111-111111111111';
const timestamp = '2026-08-30T10:00:00+07:00';

describe('AssistantParcelManifestSchema', () => {
  it('accepts the nested pagination and driver custody approval returned by Parcel API', () => {
    const location = { type: 'ROUTE_STOP', id, name: 'Stop', orderIndex: 1, eta: timestamp };
    const stop = {
      stopId: id,
      name: 'Stop',
      orderIndex: 1,
      estimatedArrivalAt: timestamp,
      status: 'ARRIVED',
      actualArrivalAt: timestamp,
      actualDepartureAt: null,
    };
    const trip = {
      tripId: id,
      status: 'IN_PROGRESS',
      departureAt: timestamp,
      eta: timestamp,
      route: null,
      vehicle: null,
      stops: [stop],
    };

    const parsed = AssistantParcelManifestSchema.parse({
      tripContext: {
        trip,
        currentOperationalLocation: {
          location,
          status: 'ARRIVED',
          actualArrivalAt: timestamp,
          actualDepartureAt: null,
        },
        orderedStops: [stop],
      },
      summary: {
        total: 1,
        checkedIn: 0,
        loaded: 1,
        expectedAtCurrentStop: 1,
        unloaded: 0,
        exceptionCount: 1,
        unresolvedCount: 1,
      },
      items: [
        {
          parcelId: id,
          parcelCode: 'VR-PCL-TEST',
          status: 'PENDING_OPERATOR_ACTION',
          dropoffLocation: location,
          currentCustody: null,
          activeIncident: {
            incidentId: id,
            type: 'WRONG_STOP',
            status: 'OPEN',
            searchDeadline: null,
            nextUpdateAt: null,
            slaState: 'NOT_STARTED',
            operatorProcessBreach: false,
          },
          custodyExceptionApproval: {
            requestId: id,
            incidentId: id,
            incidentType: 'WRONG_STOP',
            status: 'PENDING_APPROVAL',
            reason: 'Wrong stop report',
            reportedAt: timestamp,
          },
          availableActions: ['APPROVE_CUSTODY_EXCEPTION', 'REJECT_CUSTODY_EXCEPTION'],
        },
      ],
      pagination: {
        page: 1,
        pageSize: 20,
        totalItems: 1,
        totalPages: 1,
        hasNextPage: false,
        hasPreviousPage: false,
      },
    });

    expect(parsed.pagination.totalItems).toBe(1);
    expect(parsed.items[0].custodyExceptionApproval?.status).toBe('PENDING_APPROVAL');
  });
});

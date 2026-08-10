declare const require: (moduleName: string) => unknown;

const assert = require('node:assert/strict') as {
  deepEqual(actual: unknown, expected: unknown): void;
  equal(actual: unknown, expected: unknown): void;
  notEqual(actual: unknown, expected: unknown): void;
  ok(value: unknown): void;
  throws(block: () => unknown, regexp: RegExp): void;
};
const { describe, test } = require('node:test') as {
  describe(name: string, block: () => void): void;
  test(name: string, block: () => void): void;
};

import { Day44TripFixturePlan, ExistingTripFixtureState, planDay44TripFixture } from './seed-trip';

const vehicleTypes = [
  {
    id: '00000000-0000-0000-0000-000000000101',
    code: 'STANDARD_BUS',
    displayName: 'Xe ghế ngồi tiêu chuẩn',
    estimatedPassengerLuggageKgPerSeat: 10,
    defaultSeatCount: 45,
    isSystemDefined: true,
    isActive: true,
  },
  {
    id: '00000000-0000-0000-0000-000000000102',
    code: 'LIMOUSINE',
    displayName: 'Limousine',
    estimatedPassengerLuggageKgPerSeat: 15,
    defaultSeatCount: 9,
    isSystemDefined: true,
    isActive: true,
  },
  {
    id: '00000000-0000-0000-0000-000000000103',
    code: 'SLEEPER_BUS',
    displayName: 'Xe giường nằm',
    estimatedPassengerLuggageKgPerSeat: 20,
    defaultSeatCount: 40,
    isSystemDefined: true,
    isActive: true,
  },
];

const crewByOperator = {
  '6276b48c-3984-582b-9c35-0c2fbe20baa7': {
    drivers: [
      '6a61b1d5-4c98-5f40-8e0f-494651deebfa',
      '1432b243-ab2b-5a33-8db5-5441efd4d489',
      '67086aa7-71f3-5f60-9d13-f7f30bb8c7c8',
    ],
    assistant: '316ba0dc-6bea-5173-858d-4c9c3cde50de',
  },
  'd63b3c32-8c12-5130-a347-0ef8df286605': {
    drivers: [
      'ea9c2b90-c811-5281-9793-4722253b5b17',
      'aeebce20-d2d9-525c-9394-8c43c6cf8800',
      'f55eadcb-f314-5e35-898a-6d5ddad291aa',
    ],
    assistant: '2b7ae533-41e1-5fb6-9875-76e8923c4916',
  },
  '8554beea-8b1b-57c5-bb87-8d1f136654a3': {
    drivers: [
      '6e236fff-7856-51c4-917c-89c6724b7d60',
      'a052ed42-ef29-5180-b92e-317b01b92b65',
      '04ebbfdc-c20c-5f1c-b145-030eb9e247d4',
    ],
    assistant: 'f0931d74-4698-59a6-8eb6-de775b44e6fe',
  },
} as const;

const standardSeatNumbers =
  'S01 S02 S03 S04 S05 S06 S07 S08 S09 S10 S11 S12 S13 S14 S15 S16 S17 S18 S19 S20 S21 S22 S23 S24 S25 S26 S27 S28 S29 S30 S31 S32 S33 S34 S35 S36 S37 S38 S39 S40 S41 S42 S43 S44 S45'.split(
    ' ',
  );
const limousineSeatNumbers = 'V01 V02 V03 V04 V05 V06 V07 V08 V09'.split(' ');
const sleeperSeatNumbers =
  'L01 L02 L03 L04 L05 L06 L07 L08 L09 L10 L11 L12 L13 L14 L15 L16 L17 L18 L19 L20 U01 U02 U03 U04 U05 U06 U07 U08 U09 U10 U11 U12 U13 U14 U15 U16 U17 U18 U19 U20'.split(
    ' ',
  );

const standardSeatGeometry = `
S01:1:1:1:true:false S02:1:2:1:false:false S03:1:3:1:false:true S04:1:4:1:false:true S05:1:5:1:true:false
S06:2:1:1:true:false S07:2:2:1:false:false S08:2:3:1:false:true S09:2:4:1:false:true S10:2:5:1:true:false
S11:3:1:1:true:false S12:3:2:1:false:false S13:3:3:1:false:true S14:3:4:1:false:true S15:3:5:1:true:false
S16:4:1:1:true:false S17:4:2:1:false:false S18:4:3:1:false:true S19:4:4:1:false:true S20:4:5:1:true:false
S21:5:1:1:true:false S22:5:2:1:false:false S23:5:3:1:false:true S24:5:4:1:false:true S25:5:5:1:true:false
S26:6:1:1:true:false S27:6:2:1:false:false S28:6:3:1:false:true S29:6:4:1:false:true S30:6:5:1:true:false
S31:7:1:1:true:false S32:7:2:1:false:false S33:7:3:1:false:true S34:7:4:1:false:true S35:7:5:1:true:false
S36:8:1:1:true:false S37:8:2:1:false:false S38:8:3:1:false:true S39:8:4:1:false:true S40:8:5:1:true:false
S41:9:1:1:true:false S42:9:2:1:false:false S43:9:3:1:false:true S44:9:4:1:false:true S45:9:5:1:true:false
`
  .trim()
  .split(/\s+/u);
const limousineSeatGeometry = `
V01:1:1:1:true:false V02:1:2:1:false:true V03:1:3:1:true:true
V04:2:1:1:true:false V05:2:2:1:false:true V06:2:3:1:true:true
V07:3:1:1:true:false V08:3:2:1:false:true V09:3:3:1:true:true
`
  .trim()
  .split(/\s+/u);
const sleeperSeatGeometry = `
L01:1:1:1:true:false L02:1:2:1:false:true L03:1:3:1:false:true L04:1:4:1:true:false
L05:2:1:1:true:false L06:2:2:1:false:true L07:2:3:1:false:true L08:2:4:1:true:false
L09:3:1:1:true:false L10:3:2:1:false:true L11:3:3:1:false:true L12:3:4:1:true:false
L13:4:1:1:true:false L14:4:2:1:false:true L15:4:3:1:false:true L16:4:4:1:true:false
L17:5:1:1:true:false L18:5:2:1:false:true L19:5:3:1:false:true L20:5:4:1:true:false
U01:1:1:2:true:false U02:1:2:2:false:true U03:1:3:2:false:true U04:1:4:2:true:false
U05:2:1:2:true:false U06:2:2:2:false:true U07:2:3:2:false:true U08:2:4:2:true:false
U09:3:1:2:true:false U10:3:2:2:false:true U11:3:3:2:false:true U12:3:4:2:true:false
U13:4:1:2:true:false U14:4:2:2:false:true U15:4:3:2:false:true U16:4:4:2:true:false
U17:5:1:2:true:false U18:5:2:2:false:true U19:5:3:2:false:true U20:5:4:2:true:false
`
  .trim()
  .split(/\s+/u);

const r3StopsByOperator = {
  '6276b48c-3984-582b-9c35-0c2fbe20baa7': [
    '1ace61d6-f914-5d11-a242-d69bbb4c13c4',
    '07182f5b-714b-504a-9a60-94d2b165fd79',
    '0231e70c-dcfe-5951-aa8d-60ad8900b313',
  ],
  'd63b3c32-8c12-5130-a347-0ef8df286605': [
    '45bac395-9783-5e50-a278-3912535daded',
    'f1fc929c-1989-5553-8d55-a01f59f98933',
    'cb6f1e02-2a87-5618-ad75-a60363885984',
  ],
  '8554beea-8b1b-57c5-bb87-8d1f136654a3': [
    '2ffffab1-9398-5d75-a957-0c328668e6f3',
    '8ca82c0e-c89d-5f55-9ec3-d4fc90a3d8a3',
    '8b5cfaf2-ef55-5af5-834f-274c9595f2ca',
  ],
} as const;
const r3RouteByOperator = {
  '6276b48c-3984-582b-9c35-0c2fbe20baa7': '059ccdba-c397-5213-81d7-8baaaf1fef9d',
  'd63b3c32-8c12-5130-a347-0ef8df286605': 'b99d9a47-0cdf-5c2c-a9a0-89933a22c623',
  '8554beea-8b1b-57c5-bb87-8d1f136654a3': '08a8f325-cce9-5f73-ae64-84329e84526d',
} as const;

function emptyState(): ExistingTripFixtureState {
  return {
    vehicleTypes,
    stations: [],
    operatorStations: [],
    stops: [],
    routes: [],
    routeStops: [],
    alternativeRoutes: [],
    alternativeRouteStops: [],
    vehicles: [],
    driverSchedules: [],
    trips: [],
    tripSeats: [],
    tripStops: [],
    tripStopFares: [],
  };
}

function plan(startDate = '2026-08-25'): Day44TripFixturePlan {
  return planDay44TripFixture({
    environment: 'Development',
    startDate,
    currentInstant: new Date('2026-08-24T02:00:00.000Z'),
    existingState: emptyState(),
  });
}

function fixtureState(value: Day44TripFixturePlan): ExistingTripFixtureState {
  return {
    vehicleTypes: value.vehicleTypes,
    stations: value.stations,
    operatorStations: value.operatorStations,
    stops: value.stops,
    routes: value.routes,
    routeStops: value.routeStops,
    alternativeRoutes: value.alternativeRoutes,
    alternativeRouteStops: value.alternativeRouteStops,
    vehicles: value.vehicles,
    driverSchedules: value.driverSchedules,
    trips: value.trips,
    tripSeats: value.tripSeats,
    tripStops: value.tripStops,
    tripStopFares: value.tripStopFares,
  };
}

describe('Day 44 trip fixture planner', () => {
  test('expands the frozen topology and exact fixed-ID registry', () => {
    const result = plan();

    assert.equal(result.schemaVersion, 1);
    assert.equal(result.namespace, 'day44-v1');
    assert.equal(result.timezone, 'Asia/Ho_Chi_Minh');
    assert.equal(result.stations.length, 5);
    assert.equal(result.operatorStations.length, 15);
    assert.equal(result.stops.length, 9);
    assert.equal(result.routes.length, 9);
    assert.equal(result.routeStops.length, 9);
    assert.equal(result.alternativeRoutes.length, 3);
    assert.equal(result.alternativeRouteStops.length, 9);
    assert.equal(result.vehicles.length, 9);
    assert.equal(result.driverSchedules.length, 9);
    assert.equal(result.trips.length, 126);
    assert.equal(result.tripSeats.length, 3_948);
    assert.equal(result.tripStops.length, 126);
    assert.equal(result.tripStopFares.length, 0);
    assert.equal(result.stations[0].id, 'a05da7cf-042d-5471-864b-b7eff4c25fe3');
    assert.equal(result.stations[4].id, '4b80a62b-752a-5518-afb5-e2807e47a011');
    assert.equal(result.trips[0].id, '41558278-9727-5e2d-86d9-4b0bc4c00fb2');
    assert.equal(result.tripSeats[0].id, 'c9956626-f693-567e-984e-132ceec97056');
    assert.equal(result.tripSeats.at(-1)?.id, 'f51fb46c-c305-5d60-b04f-8491701ef6a2');

    const ids = [
      ...result.stations,
      ...result.operatorStations,
      ...result.stops,
      ...result.routes,
      ...result.alternativeRoutes,
      ...result.vehicles,
      ...result.driverSchedules,
      ...result.trips,
      ...result.tripSeats,
    ].map((row) => row.id);
    assert.equal(new Set(ids).size, 4_133);
  });

  test('keeps six-decimal station data and same-tenant topology without geocoding', () => {
    const result = plan();
    assert.deepEqual(
      result.stations.map((station) => [station.name, station.latitude, station.longitude]),
      [
        ['Bến xe Miền Tây', 10.741037, 106.61898],
        ['Bến xe Miền Đông mới', 10.87955, 106.81619],
        ['Bến xe Trung tâm TP Cần Thơ', 10.0052, 105.77231],
        ['Bến xe khách Phường Long Châu', 10.23823, 105.95773],
        ['Bến xe Bến Tre', 10.267025, 106.359834],
      ],
    );
    assert.deepEqual(
      result.stations.map((station) => station.locationId),
      [
        'fc57f7d4-0a54-5a64-bc15-5fe733230187',
        '35d39c7c-d0df-544f-adb1-0a3afd83ebf0',
        '0f1e42d8-25dd-5ef2-8a55-7122622a7301',
        'c2bcf64d-af68-5fcb-a3b8-168e202b48b7',
        '69b7c548-70f9-5d30-8eaa-b831f441f243',
      ],
    );
    assert.ok(result.stops.every((stop) => typeof stop.locationId === 'string'));
    assert.ok(result.stops.every((stop) => stop.googlePlaceId === null));

    for (const operatorId of new Set(result.routes.map((route) => route.operatorId))) {
      const routes = result.routes.filter((route) => route.operatorId === operatorId);
      const vehicles = result.vehicles.filter((vehicle) => vehicle.operatorId === operatorId);
      const schedules = result.driverSchedules.filter(
        (schedule) => schedule.operatorId === operatorId,
      );
      assert.equal(routes.length, 3);
      assert.equal(vehicles.length, 3);
      assert.equal(schedules.length, 3);
      assert.equal(routes[0].returnRouteId, routes[1].id);
      assert.equal(routes[1].returnRouteId, routes[0].id);
      assert.equal(routes[2].returnRouteId, null);
      assert.ok(
        schedules.every((schedule) => routes.some((route) => route.id === schedule.routeId)),
      );
      assert.ok(
        schedules.every((schedule) =>
          vehicles.some((vehicle) => vehicle.id === schedule.vehicleId),
        ),
      );
    }
  });

  test('materializes only the canonical 14-day horizon from 30-day all-day schedules', () => {
    const result = plan();
    assert.ok(
      result.driverSchedules.every(
        (schedule) =>
          JSON.stringify(schedule.dayOfWeek) === '[1,2,3,4,5,6,7]' &&
          schedule.validFrom === '2026-08-25' &&
          schedule.validUntil === '2026-09-23',
      ),
    );
    assert.ok(
      result.trips.every(
        (trip) => trip.status === 'SCHEDULED' && trip.source === 'AUTO_FROM_SCHEDULE',
      ),
    );
    assert.deepEqual(result.currentTripsThisMonthByOperator, { A: 21, B: 21, C: 21 });
    assert.ok(
      Object.values(result.tripDepartureInstantsByOperator).every((values) => values.length === 42),
    );
    Object.entries(crewByOperator).forEach(([operatorId, expectedCrew]) => {
      const schedules = result.driverSchedules.filter(
        (schedule) => schedule.operatorId === operatorId,
      );
      assert.deepEqual(
        schedules.map((schedule) => schedule.driverUserId),
        expectedCrew.drivers,
      );
      assert.equal(new Set(schedules.map((schedule) => schedule.driverUserId)).size, 3);
      assert.deepEqual(
        schedules.map((schedule) => schedule.assistantUserId),
        [expectedCrew.assistant, null, null],
      );
    });
  });

  test('snapshots canonical seats, fares, cargo, crew, and R3 stops immutably', () => {
    const result = plan();
    const seatCounts = new Map<string, number>();
    result.tripSeats.forEach((seat) =>
      seatCounts.set(seat.tripId as string, (seatCounts.get(seat.tripId as string) ?? 0) + 1),
    );
    assert.deepEqual(
      [...new Set(seatCounts.values())].sort((left, right) => left - right),
      [9, 40, 45],
    );
    assert.ok(
      result.tripSeats.every((seat) => seat.status === 'AVAILABLE' && seat.disabledReason === null),
    );
    assert.deepEqual(
      result.routes.map((route) => [route.name, route.baseFare]),
      [
        ['D44 A R1 Miền Tây - Cần Thơ', 180_000],
        ['D44 A R2 Cần Thơ - Miền Tây', 180_000],
        ['D44 A R3 Miền Tây - Bến Tre', 120_000],
        ['D44 B R1 Miền Tây - Cần Thơ', 180_000],
        ['D44 B R2 Cần Thơ - Miền Tây', 180_000],
        ['D44 B R3 Miền Tây - Bến Tre', 120_000],
        ['D44 C R1 Miền Tây - Cần Thơ', 180_000],
        ['D44 C R2 Cần Thơ - Miền Tây', 180_000],
        ['D44 C R3 Miền Tây - Bến Tre', 120_000],
      ],
    );

    const layoutAssertions = [
      {
        vehicleTypeCode: 'STANDARD_BUS',
        totalSeats: 45,
        rows: 9,
        cols: 5,
        decks: 1,
        aisle: 3,
        seatNumbers: standardSeatNumbers,
        types: ['STANDARD'],
        deckCounts: [[1, 45]],
        geometry: standardSeatGeometry,
      },
      {
        vehicleTypeCode: 'LIMOUSINE',
        totalSeats: 9,
        rows: 3,
        cols: 3,
        decks: 1,
        aisle: 2,
        seatNumbers: limousineSeatNumbers,
        types: ['VIP'],
        deckCounts: [[1, 9]],
        geometry: limousineSeatGeometry,
      },
      {
        vehicleTypeCode: 'SLEEPER_BUS',
        totalSeats: 40,
        rows: 5,
        cols: 4,
        decks: 2,
        aisle: 2,
        seatNumbers: sleeperSeatNumbers,
        types: ['SLEEPER_LOWER', 'SLEEPER_UPPER'],
        deckCounts: [
          [1, 20],
          [2, 20],
        ],
        geometry: sleeperSeatGeometry,
      },
    ];
    result.vehicles.forEach((vehicle, index) => {
      const layout = vehicle.seatLayoutJson as {
        version: number;
        vehicleTypeCode: string;
        totalSeats: number;
        rows: number;
        cols: number;
        decks: number;
        aisles: Array<{ afterCol: number }>;
        seats: Array<{
          seatNumber: string;
          row: number;
          col: number;
          type: string;
          deck: number;
          isWindow: boolean;
          isAisle: boolean;
          disabled: boolean;
        }>;
      };
      const expected = layoutAssertions[index % 3];
      assert.deepEqual(
        [
          layout.version,
          layout.vehicleTypeCode,
          layout.totalSeats,
          layout.rows,
          layout.cols,
          layout.decks,
          layout.aisles,
        ],
        [
          1,
          expected.vehicleTypeCode,
          expected.totalSeats,
          expected.rows,
          expected.cols,
          expected.decks,
          [{ afterCol: expected.aisle }],
        ],
      );
      assert.deepEqual(
        layout.seats.map((seat) => seat.seatNumber),
        expected.seatNumbers,
      );
      assert.deepEqual([...new Set(layout.seats.map((seat) => seat.type))], expected.types);
      assert.deepEqual(
        [...new Set(layout.seats.map((seat) => seat.deck))].map((deck) => [
          deck,
          layout.seats.filter((seat) => seat.deck === deck).length,
        ]),
        expected.deckCounts,
      );
      assert.ok(layout.seats.every((seat) => seat.disabled === false));
      assert.deepEqual(
        layout.seats.map(
          (seat) =>
            `${seat.seatNumber}:${seat.row}:${seat.col}:${seat.deck}:${seat.isWindow}:${seat.isAisle}`,
        ),
        expected.geometry,
      );
    });

    assert.deepEqual(
      result.routeStops.map((stop) => [
        stop.routeId,
        stop.stopId,
        stop.orderIndex,
        stop.estimatedDurationFromOriginMinutes,
        stop.distanceFromOriginKm,
      ]),
      [
        ['059ccdba-c397-5213-81d7-8baaaf1fef9d', '1ace61d6-f914-5d11-a242-d69bbb4c13c4', 1, 35, 30],
        ['059ccdba-c397-5213-81d7-8baaaf1fef9d', '07182f5b-714b-504a-9a60-94d2b165fd79', 2, 75, 65],
        [
          '059ccdba-c397-5213-81d7-8baaaf1fef9d',
          '0231e70c-dcfe-5951-aa8d-60ad8900b313',
          3,
          115,
          80,
        ],
        ['b99d9a47-0cdf-5c2c-a9a0-89933a22c623', '45bac395-9783-5e50-a278-3912535daded', 1, 35, 30],
        ['b99d9a47-0cdf-5c2c-a9a0-89933a22c623', 'f1fc929c-1989-5553-8d55-a01f59f98933', 2, 75, 65],
        [
          'b99d9a47-0cdf-5c2c-a9a0-89933a22c623',
          'cb6f1e02-2a87-5618-ad75-a60363885984',
          3,
          115,
          80,
        ],
        ['08a8f325-cce9-5f73-ae64-84329e84526d', '2ffffab1-9398-5d75-a957-0c328668e6f3', 1, 35, 30],
        ['08a8f325-cce9-5f73-ae64-84329e84526d', '8ca82c0e-c89d-5f55-9ec3-d4fc90a3d8a3', 2, 75, 65],
        [
          '08a8f325-cce9-5f73-ae64-84329e84526d',
          '8b5cfaf2-ef55-5af5-834f-274c9595f2ca',
          3,
          115,
          80,
        ],
      ],
    );
    assert.deepEqual(
      result.alternativeRouteStops.map((stop) => [
        stop.alternativeRouteId,
        stop.stopId,
        stop.orderIndex,
        stop.estimatedDurationFromOriginMinutes,
        stop.distanceFromOriginKm,
      ]),
      [
        ['9d72b698-30be-5a14-bd5f-fcfc2b21b36f', '0231e70c-dcfe-5951-aa8d-60ad8900b313', 1, 35, 30],
        ['9d72b698-30be-5a14-bd5f-fcfc2b21b36f', '1ace61d6-f914-5d11-a242-d69bbb4c13c4', 2, 75, 65],
        [
          '9d72b698-30be-5a14-bd5f-fcfc2b21b36f',
          '07182f5b-714b-504a-9a60-94d2b165fd79',
          3,
          115,
          80,
        ],
        ['031f1a57-67f0-5b3a-b9c6-294b207b9555', 'cb6f1e02-2a87-5618-ad75-a60363885984', 1, 35, 30],
        ['031f1a57-67f0-5b3a-b9c6-294b207b9555', '45bac395-9783-5e50-a278-3912535daded', 2, 75, 65],
        [
          '031f1a57-67f0-5b3a-b9c6-294b207b9555',
          'f1fc929c-1989-5553-8d55-a01f59f98933',
          3,
          115,
          80,
        ],
        ['eccde21c-b120-51e3-9a1c-bc66be9952dd', '8b5cfaf2-ef55-5af5-834f-274c9595f2ca', 1, 35, 30],
        ['eccde21c-b120-51e3-9a1c-bc66be9952dd', '2ffffab1-9398-5d75-a957-0c328668e6f3', 2, 75, 65],
        [
          'eccde21c-b120-51e3-9a1c-bc66be9952dd',
          '8ca82c0e-c89d-5f55-9ec3-d4fc90a3d8a3',
          3,
          115,
          80,
        ],
      ],
    );
    assert.deepEqual(
      result.routeStops.map((stop) => [stop.allowPickup, stop.allowDropoff]),
      [
        [true, true],
        [true, true],
        [true, true],
        [true, true],
        [true, true],
        [true, true],
        [true, true],
        [true, true],
        [true, true],
      ],
    );

    assert.deepEqual(
      result.tripStops
        .slice(0, 3)
        .map((stop) => [
          stop.tripId,
          stop.stopId,
          stop.orderIndex,
          stop.estimatedArrivalTime,
          stop.distanceFromOriginKm,
        ]),
      [
        [
          'edfa1ba9-d88f-5ea8-ae89-ac350508f866',
          '1ace61d6-f914-5d11-a242-d69bbb4c13c4',
          1,
          '2026-08-25T03:35:00.000Z',
          30,
        ],
        [
          'edfa1ba9-d88f-5ea8-ae89-ac350508f866',
          '07182f5b-714b-504a-9a60-94d2b165fd79',
          2,
          '2026-08-25T04:15:00.000Z',
          65,
        ],
        [
          'edfa1ba9-d88f-5ea8-ae89-ac350508f866',
          '0231e70c-dcfe-5951-aa8d-60ad8900b313',
          3,
          '2026-08-25T04:55:00.000Z',
          80,
        ],
      ],
    );
    assert.deepEqual(
      [
        ...new Set(
          result.tripStops.map((stop) => `${stop.orderIndex}:${stop.distanceFromOriginKm}`),
        ),
      ],
      ['1:30', '2:65', '3:80'],
    );
    const etaOffsets = [35, 75, 115] as const;
    result.tripStops.forEach((stop) => {
      const trip = result.trips.find((candidate) => candidate.id === stop.tripId);
      if (!trip) throw new Error(`TripStop references an unplanned Trip ${String(stop.tripId)}`);
      const operatorStops = r3StopsByOperator[trip.operatorId as keyof typeof r3StopsByOperator];
      assert.ok(operatorStops !== undefined);
      assert.equal(
        trip.routeId,
        r3RouteByOperator[trip.operatorId as keyof typeof r3RouteByOperator],
      );
      const orderIndex = stop.orderIndex as number;
      assert.equal(stop.stopId, operatorStops[orderIndex - 1]);
      assert.equal(
        stop.estimatedArrivalTime,
        new Date(
          new Date(trip.departureDateTime as string).getTime() +
            etaOffsets[orderIndex - 1] * 60_000,
        ).toISOString(),
      );
    });
    assert.ok(
      result.trips.every((trip) => {
        const vehicle = result.vehicles.find((candidate) => candidate.id === trip.vehicleId);
        const schedule = result.driverSchedules.find(
          (candidate) => candidate.id === trip.driverScheduleId,
        );
        const route = result.routes.find((candidate) => candidate.id === trip.routeId);
        return (
          vehicle !== undefined &&
          schedule !== undefined &&
          route !== undefined &&
          JSON.stringify(trip.seatLayoutSnapshotJson) === JSON.stringify(vehicle.seatLayoutJson) &&
          trip.baseFare === route.baseFare &&
          trip.driverUserId === schedule.driverUserId &&
          trip.assistantUserId === schedule.assistantUserId &&
          trip.maxCargoWeightKg === vehicle.maxCargoWeightKg
        );
      }),
    );
    assert.ok(
      result.tripStops.every(
        (stop) =>
          stop.status === 'PENDING' &&
          stop.actualArrivalTime === null &&
          stop.actualDepartureTime === null &&
          stop.allowPickup === true &&
          stop.allowDropoff === true,
      ),
    );
  });

  test('adopts only exact full state and fails closed on any collision', () => {
    const first = plan();
    const rerun = planDay44TripFixture({
      environment: 'Development',
      startDate: first.startDate,
      currentInstant: new Date('2026-08-24T02:00:00.000Z'),
      existingState: fixtureState(first),
    });
    assert.deepEqual(rerun, first);

    const mismatched = fixtureState(first);
    mismatched.routes = [{ ...first.routes[0], baseFare: 1 }];
    assert.throws(
      () =>
        planDay44TripFixture({
          environment: 'Development',
          startDate: first.startDate,
          currentInstant: new Date('2026-08-24T02:00:00.000Z'),
          existingState: mismatched,
        }),
      /full-state mismatch/,
    );
    const foreignChild = fixtureState(first);
    foreignChild.tripStops = [
      { ...first.tripStops[0], stopId: '40000000-0000-4000-8000-000000000099' },
    ];
    assert.throws(
      () =>
        planDay44TripFixture({
          environment: 'Development',
          startDate: first.startDate,
          currentInstant: new Date('2026-08-24T02:00:00.000Z'),
          existingState: foreignChild,
        }),
      /full-state mismatch/,
    );
  });

  test('rejects unsafe runtime inputs before planning', () => {
    assert.throws(
      () =>
        planDay44TripFixture({
          environment: 'Production',
          startDate: '2026-08-25',
          currentInstant: new Date('2026-08-24T02:00:00.000Z'),
          existingState: emptyState(),
        }),
      /forbidden in Production/,
    );
    assert.throws(
      () =>
        planDay44TripFixture({
          environment: 'Development',
          startDate: '2026-08-24',
          currentInstant: new Date('2026-08-24T02:00:00.000Z'),
          existingState: emptyState(),
        }),
      /at least one day/,
    );
    const missingCatalog = emptyState();
    missingCatalog.vehicleTypes = [];
    assert.throws(
      () =>
        planDay44TripFixture({
          environment: 'Development',
          startDate: '2026-08-25',
          currentInstant: new Date('2026-08-24T02:00:00.000Z'),
          existingState: missingCatalog,
        }),
      /VehicleType catalog/,
    );
  });
});

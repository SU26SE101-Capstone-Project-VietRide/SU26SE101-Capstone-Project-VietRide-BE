WITH concrete_assignments AS (
    SELECT
        'TRIP'::text AS source_type,
        trip.id AS source_id,
        resource.resource_type,
        resource.resource_role,
        resource.resource_id,
        trip.departure_date_time AS start_at,
        trip.estimated_arrival_time AS end_at,
        route.origin_station_id AS start_station_id,
        COALESCE(alternative.destination_station_id, route.destination_station_id) AS end_station_id,
        origin.latitude AS start_latitude,
        origin.longitude AS start_longitude,
        destination.latitude AS end_latitude,
        destination.longitude AS end_longitude
    FROM vietride_trip.trips AS trip
    JOIN vietride_trip.routes AS route ON route.id = trip.route_id
    LEFT JOIN vietride_trip.alternative_routes AS alternative
        ON alternative.id = trip.alternative_route_id
    JOIN vietride_trip.stations AS origin ON origin.id = route.origin_station_id
    JOIN vietride_trip.stations AS destination
        ON destination.id = COALESCE(alternative.destination_station_id, route.destination_station_id)
    CROSS JOIN LATERAL (
        VALUES
            ('CREW', 'DRIVER', trip.driver_user_id),
            ('CREW', 'ASSISTANT', trip.assistant_user_id),
            ('VEHICLE', 'VEHICLE', trip.vehicle_id)
    ) AS resource(resource_type, resource_role, resource_id)
    WHERE trip.status::text IN ('SCHEDULED', 'BOARDING', 'IN_PROGRESS')
      AND resource.resource_id IS NOT NULL

    UNION ALL

    SELECT
        'SHUTTLE_TRIP'::text,
        shuttle.id,
        resource.resource_type,
        resource.resource_role,
        resource.resource_id,
        shuttle.scheduled_departure_time,
        shuttle.scheduled_end_time,
        CASE WHEN shuttle.direction = 'OUTBOUND_FROM_STATION' THEN shuttle.station_id END,
        CASE WHEN shuttle.direction = 'INBOUND_TO_STATION' THEN shuttle.station_id END,
        CASE WHEN shuttle.direction = 'INBOUND_TO_STATION' THEN first_stop.pickup_lat ELSE station.latitude END,
        CASE WHEN shuttle.direction = 'INBOUND_TO_STATION' THEN first_stop.pickup_lng ELSE station.longitude END,
        CASE WHEN shuttle.direction = 'INBOUND_TO_STATION' THEN station.latitude ELSE last_stop.pickup_lat END,
        CASE WHEN shuttle.direction = 'INBOUND_TO_STATION' THEN station.longitude ELSE last_stop.pickup_lng END
    FROM vietride_trip.shuttle_trips AS shuttle
    JOIN vietride_trip.stations AS station ON station.id = shuttle.station_id
    LEFT JOIN LATERAL (
        SELECT passenger.pickup_lat, passenger.pickup_lng
        FROM vietride_trip.shuttle_passengers AS passenger
        WHERE passenger.shuttle_trip_id = shuttle.id
        ORDER BY passenger.pickup_order, passenger.created_at, passenger.id
        LIMIT 1
    ) AS first_stop ON TRUE
    LEFT JOIN LATERAL (
        SELECT passenger.pickup_lat, passenger.pickup_lng
        FROM vietride_trip.shuttle_passengers AS passenger
        WHERE passenger.shuttle_trip_id = shuttle.id
        ORDER BY passenger.pickup_order DESC, passenger.created_at DESC, passenger.id DESC
        LIMIT 1
    ) AS last_stop ON TRUE
    CROSS JOIN LATERAL (
        VALUES
            ('CREW', 'DRIVER', shuttle.driver_user_id),
            ('VEHICLE', 'VEHICLE', shuttle.vehicle_id)
    ) AS resource(resource_type, resource_role, resource_id)
    WHERE shuttle.status IN ('SCHEDULED', 'IN_PROGRESS')
), ordered AS (
    SELECT
        assignment.*,
        lead(source_type) OVER resource_timeline AS next_source_type,
        lead(source_id) OVER resource_timeline AS next_source_id,
        lead(start_at) OVER resource_timeline AS next_start_at,
        lead(start_station_id) OVER resource_timeline AS next_start_station_id,
        lead(start_latitude) OVER resource_timeline AS next_start_latitude,
        lead(start_longitude) OVER resource_timeline AS next_start_longitude
    FROM concrete_assignments AS assignment
    WINDOW resource_timeline AS (
        PARTITION BY resource_type, resource_id
        ORDER BY start_at, end_at, source_type, source_id
    )
)
SELECT
    resource_type,
    resource_role,
    resource_id,
    source_type AS previous_source_type,
    source_id AS previous_source_id,
    end_at AS previous_end_at,
    next_source_type,
    next_source_id,
    next_start_at,
    extract(epoch FROM (next_start_at - end_at)) / 60 AS gap_minutes,
    CASE
        WHEN next_start_at < end_at THEN 'TIME_OVERLAP'
        WHEN end_latitude IS NULL OR end_longitude IS NULL
          OR next_start_latitude IS NULL OR next_start_longitude IS NULL
            THEN 'LOCATION_DATA_MISSING'
        WHEN end_station_id IS NOT NULL AND end_station_id = next_start_station_id
          AND next_start_at < end_at + interval '30 minutes'
            THEN 'TURNAROUND_REQUIRED'
        WHEN end_station_id IS NOT NULL AND end_station_id = next_start_station_id
            THEN NULL
        WHEN end_latitude = next_start_latitude AND end_longitude = next_start_longitude
          AND next_start_at < end_at + interval '30 minutes'
            THEN 'TURNAROUND_REQUIRED'
        WHEN end_latitude = next_start_latitude AND end_longitude = next_start_longitude
            THEN NULL
        ELSE 'REPOSITION_REVIEW'
    END AS audit_reason,
    end_latitude AS previous_end_latitude,
    end_longitude AS previous_end_longitude,
    next_start_latitude,
    next_start_longitude
FROM ordered
WHERE next_source_id IS NOT NULL
  AND (
      next_start_at < end_at + interval '30 minutes'
      OR end_latitude IS NULL OR end_longitude IS NULL
      OR next_start_latitude IS NULL OR next_start_longitude IS NULL
      OR (end_station_id IS DISTINCT FROM next_start_station_id
          AND (end_latitude, end_longitude) IS DISTINCT FROM
              (next_start_latitude, next_start_longitude))
  )
ORDER BY resource_type, resource_id, start_at, source_id;

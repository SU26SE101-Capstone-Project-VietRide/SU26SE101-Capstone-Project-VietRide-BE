DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM "vietride_tracking"."gps_trails"
        GROUP BY "trip_id", "recorded_at"
        HAVING COUNT(*) > 1
    ) THEN
        RAISE EXCEPTION 'Cannot add GPS natural identity: duplicate (trip_id, recorded_at) rows exist';
    END IF;
END $$;

DROP INDEX IF EXISTS "vietride_tracking"."idx_gps_trails_trip_id_recorded_at";
CREATE UNIQUE INDEX "uq_gps_trails_trip_recorded_at"
    ON "vietride_tracking"."gps_trails"("trip_id", "recorded_at");

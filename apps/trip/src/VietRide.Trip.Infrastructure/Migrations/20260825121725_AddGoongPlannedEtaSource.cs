using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Trip.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGoongPlannedEtaSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:trip_generation_skip_reason", "SUBSCRIPTION_LIMIT_EXCEEDED,VEHICLE_CONFLICT,DRIVER_CONFLICT,OTHER")
                .Annotation("Npgsql:Enum:trip_seat_status", "AVAILABLE,HELD,BOOKED,UNAVAILABLE")
                .Annotation("Npgsql:Enum:trip_seat_type", "STANDARD,SLEEPER_LOWER,SLEEPER_UPPER,VIP,DRIVER_AREA")
                .Annotation("Npgsql:Enum:trip_source", "MANUAL,AUTO_FROM_SCHEDULE,VEHICLE_SUBSTITUTION")
                .Annotation("Npgsql:Enum:trip_status", "SCHEDULED,BOARDING,IN_PROGRESS,COMPLETED,CANCELLED,DISRUPTED")
                .Annotation("Npgsql:Enum:trip_stop_fare_source", "TEMPLATE_SNAPSHOT,MANUAL_OVERRIDE")
                .Annotation("Npgsql:Enum:trip_stop_status", "PENDING,ARRIVED,SKIPPED")
                .Annotation("Npgsql:Enum:vehicle_status", "ACTIVE,MAINTENANCE,OFF_DUTY,RETIRED")
                .Annotation("Npgsql:Enum:vietride_trip.incident_category", "TRAFFIC_JAM,VEHICLE_BREAKDOWN,ACCIDENT,WEATHER,OTHER")
                .Annotation("Npgsql:Enum:vietride_trip.outbox_event_status", "PENDING,PUBLISHING,PUBLISHED,FAILED")
                .Annotation("Npgsql:Enum:vietride_trip.planned_eta_source", "GOOGLE_ROUTES,GOONG,ROUTE_BASELINE")
                .Annotation("Npgsql:Enum:vietride_trip.route_change_proposal_status", "PENDING,APPROVED,REJECTED,SUPERSEDED,EXPIRED")
                .Annotation("Npgsql:Enum:vietride_trip.route_change_proposal_type", "EXISTING,CUSTOM")
                .Annotation("Npgsql:PostgresExtension:btree_gist", ",,")
                .OldAnnotation("Npgsql:Enum:trip_generation_skip_reason", "SUBSCRIPTION_LIMIT_EXCEEDED,VEHICLE_CONFLICT,DRIVER_CONFLICT,OTHER")
                .OldAnnotation("Npgsql:Enum:trip_seat_status", "AVAILABLE,HELD,BOOKED,UNAVAILABLE")
                .OldAnnotation("Npgsql:Enum:trip_seat_type", "STANDARD,SLEEPER_LOWER,SLEEPER_UPPER,VIP,DRIVER_AREA")
                .OldAnnotation("Npgsql:Enum:trip_source", "MANUAL,AUTO_FROM_SCHEDULE,VEHICLE_SUBSTITUTION")
                .OldAnnotation("Npgsql:Enum:trip_status", "SCHEDULED,BOARDING,IN_PROGRESS,COMPLETED,CANCELLED,DISRUPTED")
                .OldAnnotation("Npgsql:Enum:trip_stop_fare_source", "TEMPLATE_SNAPSHOT,MANUAL_OVERRIDE")
                .OldAnnotation("Npgsql:Enum:trip_stop_status", "PENDING,ARRIVED,SKIPPED")
                .OldAnnotation("Npgsql:Enum:vehicle_status", "ACTIVE,MAINTENANCE,OFF_DUTY,RETIRED")
                .OldAnnotation("Npgsql:Enum:vietride_trip.incident_category", "TRAFFIC_JAM,VEHICLE_BREAKDOWN,ACCIDENT,WEATHER,OTHER")
                .OldAnnotation("Npgsql:Enum:vietride_trip.outbox_event_status", "PENDING,PUBLISHING,PUBLISHED,FAILED")
                .OldAnnotation("Npgsql:Enum:vietride_trip.planned_eta_source", "GOOGLE_ROUTES,ROUTE_BASELINE")
                .OldAnnotation("Npgsql:Enum:vietride_trip.route_change_proposal_status", "PENDING,APPROVED,REJECTED,SUPERSEDED,EXPIRED")
                .OldAnnotation("Npgsql:Enum:vietride_trip.route_change_proposal_type", "EXISTING,CUSTOM")
                .OldAnnotation("Npgsql:PostgresExtension:btree_gist", ",,");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE vietride_trip.trips
                SET planned_eta_source = CASE
                    WHEN planned_eta_source = 'GOONG'::vietride_trip.planned_eta_source
                        THEN 'ROUTE_BASELINE'::vietride_trip.planned_eta_source
                    ELSE planned_eta_source
                END
                WHERE planned_eta_source = 'GOONG'::vietride_trip.planned_eta_source;
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE vietride_trip.trips
                    ALTER COLUMN planned_eta_source DROP DEFAULT;

                ALTER TYPE vietride_trip.planned_eta_source
                    RENAME TO planned_eta_source_old;

                CREATE TYPE vietride_trip.planned_eta_source AS ENUM (
                    'GOOGLE_ROUTES',
                    'ROUTE_BASELINE'
                );

                ALTER TABLE vietride_trip.trips
                    ALTER COLUMN planned_eta_source TYPE vietride_trip.planned_eta_source
                    USING planned_eta_source::text::vietride_trip.planned_eta_source;

                ALTER TABLE vietride_trip.trips
                    ALTER COLUMN planned_eta_source
                    SET DEFAULT 'ROUTE_BASELINE'::vietride_trip.planned_eta_source;

                DROP TYPE vietride_trip.planned_eta_source_old;
                """);
        }
    }
}

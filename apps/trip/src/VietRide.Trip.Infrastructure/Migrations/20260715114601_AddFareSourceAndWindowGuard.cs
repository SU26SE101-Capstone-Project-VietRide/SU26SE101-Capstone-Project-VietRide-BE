using Microsoft.EntityFrameworkCore.Migrations;
using VietRide.Trip.Domain.Entities;

#nullable disable

namespace VietRide.Trip.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFareSourceAndWindowGuard : Migration
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
                .Annotation("Npgsql:Enum:vietride_trip.outbox_event_status", "PENDING,PUBLISHING,PUBLISHED,FAILED")
                .Annotation("Npgsql:PostgresExtension:btree_gist", ",,")
                .OldAnnotation("Npgsql:Enum:trip_generation_skip_reason", "SUBSCRIPTION_LIMIT_EXCEEDED,VEHICLE_CONFLICT,DRIVER_CONFLICT,OTHER")
                .OldAnnotation("Npgsql:Enum:trip_seat_status", "AVAILABLE,HELD,BOOKED,UNAVAILABLE")
                .OldAnnotation("Npgsql:Enum:trip_seat_type", "STANDARD,SLEEPER_LOWER,SLEEPER_UPPER,VIP,DRIVER_AREA")
                .OldAnnotation("Npgsql:Enum:trip_source", "MANUAL,AUTO_FROM_SCHEDULE,VEHICLE_SUBSTITUTION")
                .OldAnnotation("Npgsql:Enum:trip_status", "SCHEDULED,BOARDING,IN_PROGRESS,COMPLETED,CANCELLED,DISRUPTED")
                .OldAnnotation("Npgsql:Enum:trip_stop_status", "PENDING,ARRIVED,SKIPPED")
                .OldAnnotation("Npgsql:Enum:vehicle_status", "ACTIVE,MAINTENANCE,OFF_DUTY,RETIRED")
                .OldAnnotation("Npgsql:Enum:vietride_trip.outbox_event_status", "PENDING,PUBLISHING,PUBLISHED,FAILED");

            migrationBuilder.AddColumn<TripStopFareSource>(
                name: "source",
                schema: "vietride_trip",
                table: "trip_stop_fares",
                type: "vietride_trip.trip_stop_fare_source",
                nullable: false,
                defaultValue: TripStopFareSource.TEMPLATE_SNAPSHOT);

            migrationBuilder.Sql(
                "ALTER TABLE vietride_trip.trip_stop_fares ALTER COLUMN source DROP DEFAULT;");

            migrationBuilder.Sql(
                """
                ALTER TABLE vietride_trip.route_stop_fare_templates
                ADD CONSTRAINT ex_route_stop_fare_templates_no_overlap
                EXCLUDE USING gist (
                    route_id WITH =,
                    stop_id WITH =,
                    tstzrange(effective_from, COALESCE(effective_until, 'infinity'::timestamptz), '[)') WITH &&
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE vietride_trip.route_stop_fare_templates
                DROP CONSTRAINT IF EXISTS ex_route_stop_fare_templates_no_overlap;
                """);

            migrationBuilder.DropColumn(
                name: "source",
                schema: "vietride_trip",
                table: "trip_stop_fares");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:trip_generation_skip_reason", "SUBSCRIPTION_LIMIT_EXCEEDED,VEHICLE_CONFLICT,DRIVER_CONFLICT,OTHER")
                .Annotation("Npgsql:Enum:trip_seat_status", "AVAILABLE,HELD,BOOKED,UNAVAILABLE")
                .Annotation("Npgsql:Enum:trip_seat_type", "STANDARD,SLEEPER_LOWER,SLEEPER_UPPER,VIP,DRIVER_AREA")
                .Annotation("Npgsql:Enum:trip_source", "MANUAL,AUTO_FROM_SCHEDULE,VEHICLE_SUBSTITUTION")
                .Annotation("Npgsql:Enum:trip_status", "SCHEDULED,BOARDING,IN_PROGRESS,COMPLETED,CANCELLED,DISRUPTED")
                .Annotation("Npgsql:Enum:trip_stop_status", "PENDING,ARRIVED,SKIPPED")
                .Annotation("Npgsql:Enum:vehicle_status", "ACTIVE,MAINTENANCE,OFF_DUTY,RETIRED")
                .Annotation("Npgsql:Enum:vietride_trip.outbox_event_status", "PENDING,PUBLISHING,PUBLISHED,FAILED")
                .OldAnnotation("Npgsql:Enum:trip_generation_skip_reason", "SUBSCRIPTION_LIMIT_EXCEEDED,VEHICLE_CONFLICT,DRIVER_CONFLICT,OTHER")
                .OldAnnotation("Npgsql:Enum:trip_seat_status", "AVAILABLE,HELD,BOOKED,UNAVAILABLE")
                .OldAnnotation("Npgsql:Enum:trip_seat_type", "STANDARD,SLEEPER_LOWER,SLEEPER_UPPER,VIP,DRIVER_AREA")
                .OldAnnotation("Npgsql:Enum:trip_source", "MANUAL,AUTO_FROM_SCHEDULE,VEHICLE_SUBSTITUTION")
                .OldAnnotation("Npgsql:Enum:trip_status", "SCHEDULED,BOARDING,IN_PROGRESS,COMPLETED,CANCELLED,DISRUPTED")
                .OldAnnotation("Npgsql:Enum:trip_stop_fare_source", "TEMPLATE_SNAPSHOT,MANUAL_OVERRIDE")
                .OldAnnotation("Npgsql:Enum:trip_stop_status", "PENDING,ARRIVED,SKIPPED")
                .OldAnnotation("Npgsql:Enum:vehicle_status", "ACTIVE,MAINTENANCE,OFF_DUTY,RETIRED")
                .OldAnnotation("Npgsql:Enum:vietride_trip.outbox_event_status", "PENDING,PUBLISHING,PUBLISHED,FAILED")
                .OldAnnotation("Npgsql:PostgresExtension:btree_gist", ",,");

            migrationBuilder.Sql("DROP EXTENSION IF EXISTS btree_gist;");
        }
    }
}

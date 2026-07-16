using System;
using Microsoft.EntityFrameworkCore.Migrations;
using VietRide.Trip.Domain.Entities;

#nullable disable

namespace VietRide.Trip.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTripIncidents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:vietride_trip.incident_category", "TRAFFIC_JAM,VEHICLE_BREAKDOWN,ACCIDENT,WEATHER,OTHER")
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
                .OldAnnotation("Npgsql:Enum:trip_stop_status", "PENDING,ARRIVED,SKIPPED")
                .OldAnnotation("Npgsql:Enum:vehicle_status", "ACTIVE,MAINTENANCE,OFF_DUTY,RETIRED")
                .OldAnnotation("Npgsql:Enum:vietride_trip.outbox_event_status", "PENDING,PUBLISHING,PUBLISHED,FAILED");

            migrationBuilder.CreateTable(
                name: "incidents",
                schema: "vietride_trip",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reported_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<IncidentCategory>(type: "vietride_trip.incident_category", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    photo_urls = table.Column<string>(type: "jsonb", nullable: true),
                    latitude = table.Column<decimal>(type: "numeric(10,7)", nullable: true),
                    longitude = table.Column<decimal>(type: "numeric(10,7)", nullable: true),
                    reported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resolution_note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_incidents", x => x.id);
                    table.ForeignKey(
                        name: "fk_incidents_trips_trip_id",
                        column: x => x.trip_id,
                        principalSchema: "vietride_trip",
                        principalTable: "trips",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "idx_incidents_reported_at",
                schema: "vietride_trip",
                table: "incidents",
                column: "reported_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "idx_incidents_reported_by",
                schema: "vietride_trip",
                table: "incidents",
                column: "reported_by_user_id");

            migrationBuilder.CreateIndex(
                name: "idx_incidents_trip_id",
                schema: "vietride_trip",
                table: "incidents",
                column: "trip_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "incidents",
                schema: "vietride_trip");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:trip_generation_skip_reason", "SUBSCRIPTION_LIMIT_EXCEEDED,VEHICLE_CONFLICT,DRIVER_CONFLICT,OTHER")
                .Annotation("Npgsql:Enum:trip_seat_status", "AVAILABLE,HELD,BOOKED,UNAVAILABLE")
                .Annotation("Npgsql:Enum:trip_seat_type", "STANDARD,SLEEPER_LOWER,SLEEPER_UPPER,VIP,DRIVER_AREA")
                .Annotation("Npgsql:Enum:trip_source", "MANUAL,AUTO_FROM_SCHEDULE,VEHICLE_SUBSTITUTION")
                .Annotation("Npgsql:Enum:trip_status", "SCHEDULED,BOARDING,IN_PROGRESS,COMPLETED,CANCELLED,DISRUPTED")
                .Annotation("Npgsql:Enum:trip_stop_status", "PENDING,ARRIVED,SKIPPED")
                .Annotation("Npgsql:Enum:vehicle_status", "ACTIVE,MAINTENANCE,OFF_DUTY,RETIRED")
                .Annotation("Npgsql:Enum:vietride_trip.outbox_event_status", "PENDING,PUBLISHING,PUBLISHED,FAILED")
                .OldAnnotation("Npgsql:Enum:vietride_trip.incident_category", "TRAFFIC_JAM,VEHICLE_BREAKDOWN,ACCIDENT,WEATHER,OTHER")
                .OldAnnotation("Npgsql:Enum:trip_generation_skip_reason", "SUBSCRIPTION_LIMIT_EXCEEDED,VEHICLE_CONFLICT,DRIVER_CONFLICT,OTHER")
                .OldAnnotation("Npgsql:Enum:trip_seat_status", "AVAILABLE,HELD,BOOKED,UNAVAILABLE")
                .OldAnnotation("Npgsql:Enum:trip_seat_type", "STANDARD,SLEEPER_LOWER,SLEEPER_UPPER,VIP,DRIVER_AREA")
                .OldAnnotation("Npgsql:Enum:trip_source", "MANUAL,AUTO_FROM_SCHEDULE,VEHICLE_SUBSTITUTION")
                .OldAnnotation("Npgsql:Enum:trip_status", "SCHEDULED,BOARDING,IN_PROGRESS,COMPLETED,CANCELLED,DISRUPTED")
                .OldAnnotation("Npgsql:Enum:trip_stop_status", "PENDING,ARRIVED,SKIPPED")
                .OldAnnotation("Npgsql:Enum:vehicle_status", "ACTIVE,MAINTENANCE,OFF_DUTY,RETIRED")
                .OldAnnotation("Npgsql:Enum:vietride_trip.outbox_event_status", "PENDING,PUBLISHING,PUBLISHED,FAILED");
        }
    }
}

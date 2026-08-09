using System;
using Microsoft.EntityFrameworkCore.Migrations;
using VietRide.Trip.Domain.Entities;

#nullable disable

namespace VietRide.Trip.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlannedEtaSource : Migration
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
                .Annotation("Npgsql:Enum:vietride_trip.planned_eta_source", "GOOGLE_ROUTES,ROUTE_BASELINE")
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
                .OldAnnotation("Npgsql:Enum:vietride_trip.route_change_proposal_status", "PENDING,APPROVED,REJECTED,SUPERSEDED,EXPIRED")
                .OldAnnotation("Npgsql:Enum:vietride_trip.route_change_proposal_type", "EXISTING,CUSTOM")
                .OldAnnotation("Npgsql:PostgresExtension:btree_gist", ",,");

            migrationBuilder.AddColumn<PlannedEtaSource>(
                name: "planned_eta_source",
                schema: "vietride_trip",
                table: "trips",
                type: "vietride_trip.planned_eta_source",
                nullable: false,
                defaultValueSql: "'ROUTE_BASELINE'::vietride_trip.planned_eta_source");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "planned_eta_source",
                schema: "vietride_trip",
                table: "trips");

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
    }
}

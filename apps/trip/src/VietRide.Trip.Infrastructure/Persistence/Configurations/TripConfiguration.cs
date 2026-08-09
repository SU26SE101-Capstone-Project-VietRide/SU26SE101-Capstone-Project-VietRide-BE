using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Configurations;

internal sealed class TripConfiguration : IEntityTypeConfiguration<Domain.Entities.Trip>
{
    public void Configure(EntityTypeBuilder<Domain.Entities.Trip> builder)
    {
        builder.ToTable("trips", table =>
        {
            table.HasCheckConstraint("chk_trips_base_fare_non_negative", "base_fare >= 0");
            table.HasCheckConstraint(
                "chk_trips_cargo_counters_non_negative",
                "reserved_parcel_weight_kg >= 0 AND reserved_parcel_volume_m3 >= 0 AND total_loaded_weight_kg >= 0 AND total_loaded_volume_m3 >= 0");
        });

        builder.HasKey(trip => trip.Id).HasName("pk_trips");
        builder.Ignore(trip => trip.RowVersion);

        builder.Property(trip => trip.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(trip => trip.OperatorId).HasColumnName("operator_id");
        builder.Property(trip => trip.RouteId).HasColumnName("route_id");
        builder.Property(trip => trip.AlternativeRouteId)
            .HasColumnName("alternative_route_id")
            .HasColumnType("uuid")
            .IsRequired(false);
        builder.Property(trip => trip.VehicleId).HasColumnName("vehicle_id");
        builder.Property(trip => trip.SeatLayoutSnapshotJson)
            .HasColumnName("seat_layout_snapshot_json")
            .HasColumnType("jsonb")
            .IsRequired();
        builder.Property(trip => trip.DriverUserId).HasColumnName("driver_user_id");
        builder.Property(trip => trip.AssistantUserId).HasColumnName("assistant_user_id");
        builder.Property(trip => trip.DriverScheduleId).HasColumnName("driver_schedule_id");
        builder.Property(trip => trip.DepartureDateTime).HasColumnName("departure_date_time");
        builder.Property(trip => trip.EstimatedArrivalTime).HasColumnName("estimated_arrival_time");
        builder.Property(trip => trip.PlannedEtaSource)
            .HasColumnName("planned_eta_source")
            .HasColumnType("vietride_trip.planned_eta_source")
            .HasDefaultValue(PlannedEtaSource.ROUTE_BASELINE);
        builder.Property(trip => trip.ActualDepartureTime).HasColumnName("actual_departure_time");
        builder.Property(trip => trip.DestinationArrivedAt).HasColumnName("destination_arrived_at");
        builder.Property(trip => trip.DestinationArrivedByUserId).HasColumnName("destination_arrived_by_user_id");
        builder.Property(trip => trip.CompletedAt).HasColumnName("completed_at");
        builder.Property(trip => trip.DisruptedAt).HasColumnName("disrupted_at");
        builder.Property(trip => trip.DisruptionReason).HasColumnName("disruption_reason");
        builder.Property(trip => trip.CancelledAt).HasColumnName("cancelled_at");
        builder.Property(trip => trip.CancelledByUserId).HasColumnName("cancelled_by_user_id");
        builder.Property(trip => trip.CancelReason).HasColumnName("cancel_reason");
        builder.Property(trip => trip.CompletedByUserId).HasColumnName("completed_by_user_id");
        builder.Property(trip => trip.Notes)
            .HasColumnName("notes")
            .HasMaxLength(2000);
        builder.Property(trip => trip.Status)
            .HasColumnName("status")
            .HasColumnType("vietride_trip.trip_status")
            .HasDefaultValue(Domain.Entities.TripStatus.SCHEDULED);
        builder.Property(trip => trip.Source)
            .HasColumnName("source")
            .HasColumnType("vietride_trip.trip_source")
            .HasComment("VEHICLE_SUBSTITUTION: created by 6.12 flow, exempt from maxTripsPerMonth counter check.");
        builder.Property(trip => trip.HasSubstitution)
            .HasColumnName("has_substitution")
            .HasDefaultValue(false)
            .HasComment("Set true when Trip_old triggers Vehicle Substitution (6.12). Reporting field.");
        builder.Property(trip => trip.BaseFare)
            .HasColumnName("base_fare")
            .HasColumnType("bigint")
            .HasConversion(m => m.Amount, amount => Money.FromRaw(amount));
        builder.Property(trip => trip.MaxCargoWeightKg)
            .HasColumnName("max_cargo_weight_kg")
            .HasColumnType("decimal(8,2)");
        builder.Property(trip => trip.MaxCargoVolumeM3)
            .HasColumnName("max_cargo_volume_m3")
            .HasColumnType("decimal(10,4)");
        builder.Property(trip => trip.EstimatedPassengerLuggageKg)
            .HasColumnName("estimated_passenger_luggage_kg")
            .HasColumnType("decimal(8,2)")
            .HasDefaultValue(0m)
            .HasComment("Snapshot at Trip create from VehicleType.estimatedPassengerLuggageKgPerSeat ?? Operator.luggagePolicy ?? 10 kg/seat × totalSeats.");
        builder.Property(trip => trip.ReservedParcelWeightKg)
            .HasColumnName("reserved_parcel_weight_kg")
            .HasColumnType("decimal(8,2)")
            .HasDefaultValue(0m);
        builder.Property(trip => trip.ReservedParcelVolumeM3)
            .HasColumnName("reserved_parcel_volume_m3")
            .HasColumnType("decimal(10,4)")
            .HasDefaultValue(0m);
        builder.Property(trip => trip.TotalLoadedWeightKg)
            .HasColumnName("total_loaded_weight_kg")
            .HasColumnType("decimal(8,2)")
            .HasDefaultValue(0m);
        builder.Property(trip => trip.TotalLoadedVolumeM3)
            .HasColumnName("total_loaded_volume_m3")
            .HasColumnType("decimal(10,4)")
            .HasDefaultValue(0m);
        builder.Property(trip => trip.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()");
        builder.Property(trip => trip.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()");

        builder.HasIndex(trip => new { trip.DriverUserId, trip.DepartureDateTime })
            .IsUnique()
            .HasDatabaseName("uq_trips_driver_departure")
            .HasFilter("status NOT IN ('CANCELLED')");
        builder.HasIndex(trip => new { trip.VehicleId, trip.DepartureDateTime })
            .IsUnique()
            .HasDatabaseName("uq_trips_vehicle_departure")
            .HasFilter("status NOT IN ('CANCELLED')");
        builder.HasIndex(trip => new { trip.OperatorId, trip.Status }).HasDatabaseName("idx_trips_operator_status");
        builder.HasIndex(trip => new { trip.RouteId, trip.DepartureDateTime }).HasDatabaseName("idx_trips_route_departure");
        builder.HasIndex(trip => trip.AlternativeRouteId).HasDatabaseName("idx_trips_alternative_route_id");
        builder.HasIndex(trip => new { trip.Status, trip.DepartureDateTime }).HasDatabaseName("idx_trips_status_departure");
        builder.HasIndex(trip => trip.AssistantUserId)
            .HasDatabaseName("idx_trips_assistant_user_id")
            .HasFilter("assistant_user_id IS NOT NULL");
        builder.HasIndex(trip => trip.DriverScheduleId)
            .HasDatabaseName("idx_trips_driver_schedule_id")
            .HasFilter("driver_schedule_id IS NOT NULL");
        builder.HasIndex(trip => new { trip.CompletedAt, trip.OperatorId })
            .HasDatabaseName("idx_trips_completed_report")
            .HasFilter("status = 'COMPLETED' AND completed_at IS NOT NULL");

        builder.HasOne<Route>()
            .WithMany()
            .HasForeignKey(trip => trip.RouteId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AlternativeRoute>()
            .WithMany()
            .HasForeignKey(trip => trip.AlternativeRouteId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Vehicle>()
            .WithMany()
            .HasForeignKey(trip => trip.VehicleId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DriverSchedule>()
            .WithMany()
            .HasForeignKey(trip => trip.DriverScheduleId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(trip => trip.Seats)
            .WithOne()
            .HasForeignKey(seat => seat.TripId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(trip => trip.Seats).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

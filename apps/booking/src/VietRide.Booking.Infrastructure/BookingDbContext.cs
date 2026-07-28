using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.NameTranslation;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Persistence;
using VietRide.Shared.Persistence.Inbox;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.Infrastructure;

/// Booking service EF Core context — owns schema `vietride_booking`.
public sealed class BookingDbContext : VietRideDbContextBase
{
    public const string SchemaName = "vietride_booking";

    public DbSet<BookingEntity> Bookings => Set<BookingEntity>();
    public DbSet<Passenger> Passengers => Set<Passenger>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<BookingTransfer> BookingTransfers => Set<BookingTransfer>();
    public DbSet<BookingPendingAction> BookingPendingActions => Set<BookingPendingAction>();
    public DbSet<Voucher> Vouchers => Set<Voucher>();
    public DbSet<VoucherUsage> VoucherUsages => Set<VoucherUsage>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<CampaignVoucher> CampaignVouchers => Set<CampaignVoucher>();
    public DbSet<OperatorVoucherConsent> OperatorVoucherConsents => Set<OperatorVoucherConsent>();
    public DbSet<BookingStats> BookingStats => Set<BookingStats>();
    public DbSet<BookingStatsProcessedEvent> BookingStatsProcessedEvents => Set<BookingStatsProcessedEvent>();
    public DbSet<BookingShuttleIntent> BookingShuttleIntents => Set<BookingShuttleIntent>();
    public DbSet<BookingStatusHistory> BookingStatusHistories => Set<BookingStatusHistory>();
    public DbSet<BookingStationRedirect> BookingStationRedirects => Set<BookingStationRedirect>();

    public BookingDbContext(DbContextOptions<BookingDbContext> options, IClock clock)
        : base(options, clock)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.HasPostgresEnum("booking_status", Enum.GetNames<BookingStatus>());
        modelBuilder.HasPostgresEnum(
            "booking_cancellation_reason",
            Enum.GetNames<BookingCancellationReason>());
        modelBuilder.HasPostgresEnum(
            "passenger_boarding_status",
            Enum.GetNames<PassengerBoardingStatus>());
        modelBuilder.HasPostgresEnum(
            SchemaName,
            "booking_transfer_confirmation_status",
            Enum.GetNames<BookingTransferConfirmationStatus>());
        modelBuilder.HasPostgresEnum(
            "booking_pending_action_reason",
            Enum.GetNames<BookingPendingActionReason>());
        modelBuilder.HasPostgresEnum(
            "booking_pending_action_severity",
            Enum.GetNames<BookingPendingActionSeverity>());
        modelBuilder.HasPostgresEnum(
            "booking_pending_action_resolved",
            Enum.GetNames<BookingPendingActionResolved>());
        modelBuilder.HasPostgresEnum("public", "ticket_status", Enum.GetNames<TicketStatus>());
        modelBuilder.HasPostgresEnum("trip_direction", Enum.GetNames<TripDirection>());
        modelBuilder.HasPostgresEnum("voucher_type", Enum.GetNames<VoucherType>());
        modelBuilder.HasPostgresEnum("voucher_funding_type", Enum.GetNames<VoucherFundingType>());
        modelBuilder.HasPostgresEnum(
            "operator_voucher_consent_status",
            Enum.GetNames<OperatorVoucherConsentStatus>());

        // Apply all IEntityTypeConfiguration<T> defined in this assembly BEFORE base
        // (base applies snake_case naming + OutboxEvent mapping).
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BookingDbContext).Assembly);
        modelBuilder.AddVietRideIntegrationInbox();

        base.OnModelCreating(modelBuilder);
    }

    public static void ConfigurePostgresTypes(NpgsqlDataSourceBuilder dataSourceBuilder)
    {
        dataSourceBuilder.MapEnum<BookingStatus>("booking_status", new NpgsqlNullNameTranslator());
        dataSourceBuilder.MapEnum<BookingCancellationReason>(
            "booking_cancellation_reason",
            new NpgsqlNullNameTranslator());
        dataSourceBuilder.MapEnum<PassengerBoardingStatus>(
            "passenger_boarding_status",
            new NpgsqlNullNameTranslator());
        dataSourceBuilder.MapEnum<BookingTransferConfirmationStatus>(
            $"{SchemaName}.booking_transfer_confirmation_status",
            new NpgsqlNullNameTranslator());
        dataSourceBuilder.MapEnum<BookingPendingActionReason>(
            "booking_pending_action_reason",
            new NpgsqlNullNameTranslator());
        dataSourceBuilder.MapEnum<BookingPendingActionSeverity>(
            "booking_pending_action_severity",
            new NpgsqlNullNameTranslator());
        dataSourceBuilder.MapEnum<BookingPendingActionResolved>(
            "booking_pending_action_resolved",
            new NpgsqlNullNameTranslator());
        dataSourceBuilder.MapEnum<TicketStatus>("public.ticket_status", new NpgsqlNullNameTranslator());
        dataSourceBuilder.MapEnum<TripDirection>("trip_direction", new NpgsqlNullNameTranslator());
        dataSourceBuilder.MapEnum<VoucherType>("voucher_type", new NpgsqlNullNameTranslator());
        dataSourceBuilder.MapEnum<VoucherFundingType>(
            "voucher_funding_type",
            new NpgsqlNullNameTranslator());
        dataSourceBuilder.MapEnum<OperatorVoucherConsentStatus>(
            "operator_voucher_consent_status",
            new NpgsqlNullNameTranslator());
    }
}

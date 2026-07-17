namespace VietRide.Booking.UnitTests.Features.Bookings;

public sealed class Day23BookingCurrentDepartureInitializationTests
{
    [Fact]
    public Task CreateBooking_InitializesCurrentDepartureFromTheTripSnapshot()
        => new CreateBookingCommandHandlerTests()
            .Handle_WalletPayment_HappyPath_ReturnsConfirmedBooking();

    [Fact]
    public Task CreateRoundTripBooking_InitializesEachLegFromItsOwnTripSnapshot()
        => new CreateRoundTripBookingCommandHandlerTests()
            .Handle_WalletPayment_HappyPath_BatchesChargeOnce_AndConfirmsBothLegs();
}

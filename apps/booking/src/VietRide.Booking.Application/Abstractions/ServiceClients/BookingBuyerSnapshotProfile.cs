namespace VietRide.Booking.Application.Abstractions.ServiceClients;

public sealed record BookingBuyerSnapshotProfile(
    Guid UserId,
    string DisplayName,
    string? Phone,
    string? Email,
    string? AvatarUrl,
    bool Deleted)
{
    public const string DeletedDisplayName = "Người dùng đã xóa";
}

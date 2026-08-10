namespace VietRide.Identity.Application.Features.Operators;

public sealed record OperatorProfileAddressResponse(
    string? Street,
    string? Ward,
    string? Province);

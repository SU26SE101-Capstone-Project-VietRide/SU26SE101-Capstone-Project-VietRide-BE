using VietRide.Payment.Domain.Enums;

namespace VietRide.Payment.Application.Abstractions.Repositories;

public sealed record PaymentReference(PaymentReferenceType ReferenceType, Guid ReferenceId);

using MediatR;

namespace VietRide.Payment.Application.Features.Payments.GetVnPayMobileSdkReturn;

public sealed record GetVnPayMobileSdkReturnQuery(
    IReadOnlyDictionary<string, string> Parameters) : IRequest<VnPayMobileSdkReturnResult>;

using MediatR;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Parcel.Application.Features.Parcels.OperationalRecovery;

public sealed class UpdateParcelStatusCommandHandler
    : IRequestHandler<UpdateParcelStatusCommand, OperationalParcelResponse>
{
    private readonly IMediator _mediator;

    public UpdateParcelStatusCommandHandler(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task<OperationalParcelResponse> Handle(
        UpdateParcelStatusCommand command,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ParcelStatus>(command.Status, ignoreCase: true, out var targetStatus)
            || targetStatus != ParcelStatus.RETURNED)
        {
            throw new CodedConflictException(
                "INVALID_TRANSITION",
                "The status override endpoint only supports target status RETURNED.");
        }

        return await _mediator.Send(
            new ReturnParcelCommand(
                command.ParcelId,
                command.OperatorId,
                command.UserId,
                command.Reason!,
                IsStatusOverride: true),
            cancellationToken);
    }
}

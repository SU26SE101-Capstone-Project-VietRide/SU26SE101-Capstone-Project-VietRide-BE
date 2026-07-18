using FluentValidation;

namespace VietRide.Trip.Application.Features.Stations.MergeStations;

public sealed class MergeStationsCommandValidator : AbstractValidator<MergeStationsCommand>
{
    public MergeStationsCommandValidator()
    {
        RuleFor(command => command.PrimaryStationId).NotEmpty();
        RuleFor(command => command.DuplicateStationId).NotEmpty();
        RuleFor(command => command.ActorUserId).NotEmpty();
        RuleFor(command => command.DuplicateStationId)
            .NotEqual(command => command.PrimaryStationId)
            .WithMessage("Primary and duplicate Station IDs must differ.");
    }
}

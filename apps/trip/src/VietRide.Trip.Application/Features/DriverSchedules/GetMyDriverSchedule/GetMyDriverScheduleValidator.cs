using FluentValidation;

namespace VietRide.Trip.Application.Features.DriverSchedules.GetMyDriverSchedule;

public sealed class GetMyDriverScheduleValidator : AbstractValidator<GetMyDriverScheduleQuery>
{
    public GetMyDriverScheduleValidator()
    {
        RuleFor(query => query.UserId).NotEmpty();

        RuleFor(query => query)
            .Must(query => query.From.HasValue == query.To.HasValue)
            .WithName("from")
            .WithMessage("From and To must either both be provided or both be omitted.");

        RuleFor(query => query.To)
            .GreaterThanOrEqualTo(query => query.From)
            .When(query => query.From.HasValue && query.To.HasValue)
            .WithMessage("To must be on or after From.");
    }
}

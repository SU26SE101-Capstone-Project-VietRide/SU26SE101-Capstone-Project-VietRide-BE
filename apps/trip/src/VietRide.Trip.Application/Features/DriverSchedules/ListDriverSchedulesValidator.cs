using FluentValidation;

namespace VietRide.Trip.Application.Features.DriverSchedules;

public sealed class ListDriverSchedulesValidator : AbstractValidator<ListDriverSchedulesQuery>
{
    public ListDriverSchedulesValidator()
    {
        RuleFor(query => query.OperatorId).NotEmpty();
        RuleFor(query => query.Page).GreaterThan(0).When(query => query.Page.HasValue);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100).When(query => query.PageSize.HasValue);
        RuleFor(query => query.Search).MaximumLength(255);
        RuleFor(query => query.RouteId).NotEqual(Guid.Empty).When(query => query.RouteId.HasValue);
        RuleFor(query => query.DriverUserId).NotEqual(Guid.Empty).When(query => query.DriverUserId.HasValue);
        RuleFor(query => query.VehicleTypeId).NotEqual(Guid.Empty).When(query => query.VehicleTypeId.HasValue);
    }
}

using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Trips.ListOperatorTrips;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.UnitTests.Features.Vehicles;

namespace VietRide.Trip.UnitTests.Features.Trips;

public sealed class ListOperatorTripsQueryHandlerTests
{
    [Fact]
    public async Task Handle_NormalizesFiltersUsesInclusiveIctRangeAndEnrichesCrewInOneBatch()
    {
        var operatorId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var assistantId = Guid.NewGuid();
        var row = new OperatorTripListRow(
            Guid.NewGuid(),
            TripStatus.IN_PROGRESS,
            Guid.NewGuid(),
            "HCM - Đà Lạt",
            "Bến xe Miền Đông",
            "Bến xe Đà Lạt",
            Guid.NewGuid(),
            "51B-123.45",
            VehicleStatus.MAINTENANCE,
            driverId,
            assistantId,
            DateTimeOffset.Parse("2026-07-29T01:00:00Z"),
            DateTimeOffset.Parse("2026-07-29T08:00:00Z"));
        object?[]? repositoryArguments = null;
        var repository = TestProxy<ITripRepository>.Create((method, args) =>
        {
            if (method.Name != nameof(ITripRepository.ListOperatorTripsAsync))
            {
                return null;
            }

            repositoryArguments = args;
            return PagedResult<OperatorTripListRow>.Create([row], 2, 25, 26);
        });
        IReadOnlyCollection<Guid>? requestedCrewIds = null;
        var identity = TestProxy<IIdentityInternalClient>.Create((method, args) =>
        {
            if (method.Name != nameof(IIdentityInternalClient.GetUsersAsync))
            {
                return null;
            }

            requestedCrewIds = (IReadOnlyCollection<Guid>)args![0]!;
            return new Dictionary<Guid, IdentityUserProfile>
            {
                [driverId] = new(driverId, "Nguyễn Văn A", null, "DRIVER", operatorId, "ACTIVE", "0900000000"),
                [assistantId] = new(assistantId, "Trần Văn B", null, "ASSISTANT", operatorId, "ACTIVE", "0911111111"),
            };
        });
        var handler = new ListOperatorTripsQueryHandler(repository, identity);

        var result = await handler.Handle(
            new ListOperatorTripsQuery(
                operatorId,
                " 51B–123_45 ",
                TripStatus.IN_PROGRESS,
                new DateOnly(2026, 7, 29),
                new DateOnly(2026, 7, 30),
                2,
                25,
                "departureAt",
                "desc"),
            CancellationToken.None);

        Assert.NotNull(repositoryArguments);
        Assert.Equal(operatorId, repositoryArguments[0]);
        Assert.Equal(2, repositoryArguments[1]);
        Assert.Equal(25, repositoryArguments[2]);
        Assert.Equal("51B–123_45", repositoryArguments[3]);
        Assert.Equal("51B12345", repositoryArguments[4]);
        Assert.Equal(TripStatus.IN_PROGRESS, repositoryArguments[5]);
        Assert.Equal(DateTimeOffset.Parse("2026-07-28T17:00:00Z"), repositoryArguments[6]);
        Assert.Equal(DateTimeOffset.Parse("2026-07-30T17:00:00Z"), repositoryArguments[7]);
        Assert.Equal(true, repositoryArguments[8]);
        Assert.NotNull(requestedCrewIds);
        Assert.Equal([driverId, assistantId], requestedCrewIds);

        var item = Assert.Single(result.Items);
        Assert.True(item.CanSubstituteVehicle);
        Assert.Equal("IN_PROGRESS", item.Status);
        Assert.Equal("Nguyễn Văn A", item.Driver!.DisplayName);
        Assert.Equal("0900000000", item.Driver.Phone);
        Assert.Equal("Trần Văn B", item.Assistant!.DisplayName);
        Assert.Equal(26, result.TotalItems);
    }

    [Fact]
    public async Task Handle_MissingCrewProfileReturnsNullAndNonInProgressTripIsNotSubstitutable()
    {
        var row = new OperatorTripListRow(
            Guid.NewGuid(),
            TripStatus.SCHEDULED,
            Guid.NewGuid(),
            "Hà Nội - Hải Phòng",
            "Hà Nội",
            "Hải Phòng",
            Guid.NewGuid(),
            "30A-12345",
            VehicleStatus.ACTIVE,
            Guid.NewGuid(),
            null,
            DateTimeOffset.Parse("2026-08-01T01:00:00Z"),
            DateTimeOffset.Parse("2026-08-01T03:00:00Z"));
        var repository = TestProxy<ITripRepository>.Create((method, _) =>
            method.Name == nameof(ITripRepository.ListOperatorTripsAsync)
                ? PagedResult<OperatorTripListRow>.Create([row], 1, 20, 1)
                : null);
        var identity = TestProxy<IIdentityInternalClient>.Create((method, _) =>
            method.Name == nameof(IIdentityInternalClient.GetUsersAsync)
                ? new Dictionary<Guid, IdentityUserProfile>()
                : null);

        var result = await new ListOperatorTripsQueryHandler(repository, identity).Handle(
            new ListOperatorTripsQuery(Guid.NewGuid(), null, null, null, null, null, null, null, null),
            CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.False(item.CanSubstituteVehicle);
        Assert.Null(item.Driver);
        Assert.Null(item.Assistant);
    }

    [Fact]
    public async Task Validator_RejectsInvalidDateRangePagingAndSort()
    {
        var validator = new ListOperatorTripsQueryValidator();

        var result = await validator.ValidateAsync(new ListOperatorTripsQuery(
            Guid.NewGuid(),
            new string('x', 256),
            null,
            DateOnly.MaxValue,
            DateOnly.MaxValue,
            0,
            101,
            "createdAt",
            "sideways"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(ListOperatorTripsQuery.Search));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(ListOperatorTripsQuery.From));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(ListOperatorTripsQuery.Page));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(ListOperatorTripsQuery.PageSize));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(ListOperatorTripsQuery.SortBy));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(ListOperatorTripsQuery.SortDir));
    }
}

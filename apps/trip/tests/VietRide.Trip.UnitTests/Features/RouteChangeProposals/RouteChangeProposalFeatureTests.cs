using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using VietRide.Shared.Web.Idempotency;
using VietRide.Trip.Api.Controllers;
using VietRide.Trip.Api.Controllers.Requests;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Application.Events;
using VietRide.Trip.Application.Features.AlternativeRoutes;
using VietRide.Trip.Application.Features.RouteChangeProposals;
using VietRide.Trip.Domain.Constants;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.UnitTests.Features.RouteChangeProposals;

public sealed class RouteChangeProposalFeatureTests
{
    [Fact]
    public void Create_NormalizesReason_AndSupportsStateMachine()
    {
        var proposal = CreateProposal("  avoid flooding  ");

        proposal.Reason.Should().Be("avoid flooding");
        proposal.Status.Should().Be(RouteChangeProposalStatus.PENDING);

        var approvedRouteId = Guid.NewGuid();
        proposal.Approve(Guid.NewGuid(), approvedRouteId, DateTimeOffset.UtcNow);

        proposal.Status.Should().Be(RouteChangeProposalStatus.APPROVED);
        proposal.ApprovedAlternativeRouteId.Should().Be(approvedRouteId);
        var act = () => proposal.Reject(Guid.NewGuid(), DateTimeOffset.UtcNow, null);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void SnapshotStops_AreNormalizedOrderedAndUnique()
    {
        var proposal = CreateProposal("Traffic diversion");
        var firstStopId = Guid.NewGuid();
        proposal.AddStop(RouteChangeProposalStop.Create(proposal.Id, firstStopId, 2, 15, 4.5m));
        proposal.AddStop(RouteChangeProposalStop.Create(proposal.Id, Guid.NewGuid(), 1, 5, 1.5m));

        var dto = RouteChangeProposalMapper.ToDto(proposal);

        dto.Snapshot.Stops.Select(stop => stop.OrderIndex).Should().Equal(1, 2);
        var duplicateStop = () => proposal.AddStop(RouteChangeProposalStop.Create(proposal.Id, firstStopId, 3, 20, 6m));
        duplicateStop.Should().Throw<InvalidOperationException>();
        var duplicateOrder = () => proposal.AddStop(RouteChangeProposalStop.Create(proposal.Id, Guid.NewGuid(), 2, 20, 6m));
        duplicateOrder.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void SupersedeAndExpire_PersistCanonicalResolutionContext()
    {
        var actorId = Guid.NewGuid();
        var winnerId = Guid.NewGuid();
        var superseded = CreateProposal("Traffic diversion");
        var expired = CreateProposal("Traffic diversion");

        superseded.Supersede(actorId, winnerId, RouteChangeProposalResolutionCode.AnotherProposalApproved, DateTimeOffset.UtcNow);
        expired.Expire(RouteChangeProposalResolutionCode.SourceRouteChanged, DateTimeOffset.UtcNow);

        superseded.Status.Should().Be(RouteChangeProposalStatus.SUPERSEDED);
        superseded.ResolutionCode.Should().Be(RouteChangeProposalResolutionCode.AnotherProposalApproved);
        superseded.SupersededByProposalId.Should().Be(winnerId);
        expired.Status.Should().Be(RouteChangeProposalStatus.EXPIRED);
        expired.ResolutionCode.Should().Be(RouteChangeProposalResolutionCode.SourceRouteChanged);
    }

    [Fact]
    public void ListValidators_RejectPageValuesOutsideSupportedRange()
    {
        new ListDriverRouteChangeProposalsValidator()
            .Validate(new ListDriverRouteChangeProposalsQuery(Guid.NewGuid(), Guid.NewGuid(), null, 0, 20))
            .IsValid.Should().BeFalse();
        new ListDriverRouteChangeProposalsValidator()
            .Validate(new ListDriverRouteChangeProposalsQuery(Guid.NewGuid(), Guid.NewGuid(), "OTHER", 1, 20))
            .IsValid.Should().BeFalse();
        new ListAssignedTripAlternativeRoutesValidator()
            .Validate(new ListAssignedTripAlternativeRoutesQuery(Guid.NewGuid(), Guid.NewGuid(), 1, 101))
            .IsValid.Should().BeFalse();
        new ListOperatorRouteChangeProposalsValidator()
            .Validate(new ListOperatorRouteChangeProposalsQuery(Guid.NewGuid(), null, null, null, 0, 101))
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public void OperatorMetadataAndApproveSerialization_MatchExactContract()
    {
        var getMetadata = typeof(OperatorRouteChangeProposalsController).GetMethod(nameof(OperatorRouteChangeProposalsController.GetAsync))!
            .GetCustomAttributes<Microsoft.AspNetCore.Mvc.ProducesResponseTypeAttribute>()
            .Single(attribute => attribute.StatusCode == 200);
        var approveMetadata = typeof(OperatorRouteChangeProposalsController).GetMethod(nameof(OperatorRouteChangeProposalsController.ApproveAsync))!
            .GetCustomAttributes<Microsoft.AspNetCore.Mvc.ProducesResponseTypeAttribute>()
            .Single(attribute => attribute.StatusCode == 200);
        getMetadata.Type.Should().Be(typeof(VietRide.Shared.Kernel.Primitives.ApiResponse<RouteChangeProposalDto>));
        approveMetadata.Type.Should().Be(typeof(VietRide.Shared.Kernel.Primitives.ApiResponse<ApproveRouteChangeProposalResponse>));

        var proposal = RouteChangeProposalMapper.ToDto(CreateProposal("Traffic diversion"));
        var response = new ApproveRouteChangeProposalResponse(
            proposal,
            new VietRide.Trip.Application.Features.Trips.ChangeTripRouteResponse(proposal.TripId, "SCHEDULED", Guid.NewGuid(), []));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        json.RootElement.TryGetProperty("routeChange", out _).Should().BeTrue();
        json.RootElement.TryGetProperty("changeTripRoute", out _).Should().BeFalse();
    }

    [Fact]
    public async Task OperatorListHandler_ForwardsTenantFiltersAndPagination()
    {
        var expected = RouteChangeProposalMapper.ToDto(CreateProposal("Traffic diversion"));
        var service = new FakeRouteChangeProposalService(expected);
        var query = new ListOperatorRouteChangeProposalsQuery(
            expected.OperatorId,
            expected.TripId,
            "PENDING",
            "CUSTOM",
            3,
            25);

        var result = await new ListOperatorRouteChangeProposalsHandler(service).Handle(query, CancellationToken.None);

        result.Page.Should().Be(3);
        result.PageSize.Should().Be(25);
        result.Items.Should().ContainSingle().Which.Should().Be(expected);
        service.OperatorListRequest.Should().NotBeNull();
        service.OperatorListRequest!.Value.OperatorId.Should().Be(expected.OperatorId);
        service.OperatorListRequest.Value.TripId.Should().Be(expected.TripId);
        service.OperatorListRequest.Value.Status.Should().Be("PENDING");
        service.OperatorListRequest.Value.Type.Should().Be("CUSTOM");
        service.OperatorListRequest.Value.Page.Should().Be(3);
        service.OperatorListRequest.Value.PageSize.Should().Be(25);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_RejectsMissingReason(string reason)
    {
        var act = () => CreateProposal(reason);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task CreateHandler_DelegatesToRouteChangeProposalService()
    {
        var expected = RouteChangeProposalMapper.ToDto(CreateProposal("Traffic diversion"));
        var service = new FakeRouteChangeProposalService(expected);
        var command = new CreateRouteChangeProposalCommand(
            expected.TripId,
            expected.ProposedByUserId,
            "CUSTOM",
            null,
            expected.Snapshot,
            null,
            expected.Reason);

        var result = await new CreateRouteChangeProposalHandler(service).Handle(command, CancellationToken.None);

        result.Should().Be(expected);
        service.CreateCalls.Should().Be(1);
    }

    [Fact]
    public void CreateValidator_RejectsReasonLongerThanFiveHundredCharacters()
    {
        var command = new CreateRouteChangeProposalCommand(
            Guid.NewGuid(), Guid.NewGuid(), "CUSTOM", null,
            new RouteChangeProposalSnapshotInput("Bypass", null, Guid.NewGuid(), null, null, null, []),
            null,
            new string('x', 501));

        var result = new CreateRouteChangeProposalValidator().Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == nameof(command.Reason));
    }

    [Fact]
    public void PublicMutationBodies_UseRouteAndReasonAndRejectNestedUnknownFields()
    {
        var create = new CreateRouteChangeProposalRequest
        {
            Type = "CUSTOM",
            Reason = "Traffic diversion",
            Route = new RouteChangeProposalSnapshotRequest
            {
                Name = "Bypass",
                DestinationStationId = Guid.NewGuid(),
                PathPolyline = "encoded",
            },
        };
        using var createJson = JsonDocument.Parse(JsonSerializer.Serialize(create, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        createJson.RootElement.TryGetProperty("route", out _).Should().BeTrue();
        createJson.RootElement.TryGetProperty("customRoute", out _).Should().BeFalse();

        var reject = new RejectRouteChangeProposalRequest { Reason = "Unsafe" };
        using var rejectJson = JsonDocument.Parse(JsonSerializer.Serialize(reject, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        rejectJson.RootElement.TryGetProperty("reason", out _).Should().BeTrue();
        rejectJson.RootElement.TryGetProperty("rejectionReason", out _).Should().BeFalse();

        var deserialize = () => JsonSerializer.Deserialize<CreateRouteChangeProposalRequest>(
            """{"type":"CUSTOM","reason":"Traffic diversion","route":{"name":"Bypass","destinationStationId":"00000000-0000-4000-8000-000000000001","pathPolyline":"encoded","unknown":true}}""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        deserialize.Should().Throw<JsonException>();
    }

    [Fact]
    public void CreateValidator_RequiresCustomPolyline()
    {
        var command = new CreateRouteChangeProposalCommand(
            Guid.NewGuid(), Guid.NewGuid(), "CUSTOM", null,
            new RouteChangeProposalSnapshotInput("Bypass", null, Guid.NewGuid(), null, null, "", []),
            null,
            "Traffic diversion");

        var result = new CreateRouteChangeProposalValidator().Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName.EndsWith("PathPolyline", StringComparison.Ordinal));
    }

    [Fact]
    public void Controllers_RequireExpectedRolesAndIdempotency()
    {
        typeof(DriverController).GetMethod(nameof(DriverController.CreateRouteChangeProposalAsync))!
            .GetCustomAttribute<RequireIdempotencyAttribute>().Should().NotBeNull();
        typeof(OperatorRouteChangeProposalsController).GetCustomAttribute<AuthorizeAttribute>()!
            .Roles.Should().Be("OPERATOR_ADMIN");
        typeof(OperatorRouteChangeProposalsController).GetMethod(nameof(OperatorRouteChangeProposalsController.ApproveAsync))!
            .GetCustomAttribute<RequireIdempotencyAttribute>().Should().NotBeNull();
        typeof(OperatorRouteChangeProposalsController).GetMethod(nameof(OperatorRouteChangeProposalsController.RejectAsync))!
            .GetCustomAttribute<RequireIdempotencyAttribute>().Should().NotBeNull();
    }

    [Fact]
    public void Events_UseCanonicalRoutingKeys()
    {
        RouteChangeProposalIntegrationEvent.Created.Should().Be("trip.route_change_proposal.created");
        RouteChangeProposalIntegrationEvent.Approved.Should().Be("trip.route_change_proposal.approved");
        RouteChangeProposalIntegrationEvent.Rejected.Should().Be("trip.route_change_proposal.rejected");
        RouteChangeProposalIntegrationEvent.Superseded.Should().Be("trip.route_change_proposal.superseded");
        RouteChangeProposalIntegrationEvent.Expired.Should().Be("trip.route_change_proposal.expired");
    }

    [Fact]
    public void EventPayload_ExposesTerminalResolutionFields()
    {
        var sourceRouteId = Guid.NewGuid();
        var approvedRouteId = Guid.NewGuid();
        var winnerId = Guid.NewGuid();
        var evt = new RouteChangeProposalIntegrationEvent(
            RouteChangeProposalIntegrationEvent.Superseded,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "EXISTING",
            "SUPERSEDED",
            sourceRouteId,
            approvedRouteId,
            null,
            "Traffic diversion",
            null,
            RouteChangeProposalResolutionCode.AnotherProposalApproved,
            winnerId,
            DateTimeOffset.UtcNow);

        evt.SourceAlternativeRouteId.Should().Be(sourceRouteId);
        evt.ApprovedAlternativeRouteId.Should().Be(approvedRouteId);
        evt.ResolutionCode.Should().Be(RouteChangeProposalResolutionCode.AnotherProposalApproved);
        evt.SupersededByProposalId.Should().Be(winnerId);
    }

    private static RouteChangeProposal CreateProposal(string reason)
        => RouteChangeProposal.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), RouteChangeProposalType.CUSTOM,
            null, null, null, reason, "Bypass", null, Guid.NewGuid(), 12.5m, 30, "encoded");

}

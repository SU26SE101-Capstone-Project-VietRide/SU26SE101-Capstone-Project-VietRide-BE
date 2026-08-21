using FluentAssertions;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using ParcelAggregate = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.UnitTests.Domain;

public sealed class ParcelReliabilityStateTests
{
    [Fact]
    public void AcceptDeclaration_FreezesPositiveQuantity()
    {
        var parcel = (ParcelAggregate)Activator.CreateInstance(typeof(ParcelAggregate), nonPublic: true)!;

        parcel.AcceptDeclaration(12_000_000, 1, DateTimeOffset.UtcNow, quantity: 3);

        parcel.Quantity.Should().Be(3);
        parcel.DeclaredValueVnd.Should().Be(12_000_000);
    }

    [Fact]
    public void AcceptDeclaration_RejectsNonPositiveQuantity()
    {
        var parcel = (ParcelAggregate)Activator.CreateInstance(typeof(ParcelAggregate), nonPublic: true)!;

        var action = () => parcel.AcceptDeclaration(null, 1, DateTimeOffset.UtcNow, quantity: 0);

        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void CompensationPolicy_BelowDefault_RequiresExplicitAcknowledgement()
    {
        var action = () => ParcelCompensationPolicy.CreateDefault(Guid.NewGuid(), Guid.NewGuid())
            .Update(
                compensationRatePercent: 49,
                maxCompensationVnd: 29_000_000,
                noProofFallbackMultiplier: 4,
                claimWindowDays: 30,
                searchSlaHours: 72,
                decisionSlaBusinessDays: 7,
                payoutSlaBusinessDays: 3,
                belowDefaultAcknowledged: false,
                updatedByUserId: Guid.NewGuid());

        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Incident_SearchExpiry_TransitionsToLostConfirmedWithoutChangingParcelStatus()
    {
        var incident = ParcelIncident.Open(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ParcelIncidentType.MISSING_AFTER_DEPARTURE,
            DateTimeOffset.UtcNow.AddHours(72),
            Guid.NewGuid(),
            null,
            null,
            "SYSTEM",
            "STOP:EXPECTED",
            "VEHICLE:LAST",
            "Reconciliation gap",
            null,
            operatorProcessBreach: true);

        incident.StartSearch();
        incident.Escalate(DateTimeOffset.UtcNow);
        incident.ExpireSearch();
        incident.ConfirmLost("Search SLA expired.", DateTimeOffset.UtcNow);

        incident.Status.Should().Be(ParcelIncidentStatus.LOST_CONFIRMED);
        incident.OperatorProcessBreach.Should().BeTrue();
        incident.ResolutionCode.Should().Be("LOST_CONFIRMED");
    }

    [Fact]
    public void Incident_TerminalState_CannotBeMarkedFoundOrDeclaredLostAgain()
    {
        var incident = ParcelIncident.Open(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ParcelIncidentType.MISSING,
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            null,
            null,
            "SYSTEM",
            null,
            null,
            null,
            null,
            operatorProcessBreach: false);
        incident.StartSearch();
        incident.Escalate(DateTimeOffset.UtcNow);
        incident.ExpireSearch();
        incident.ConfirmLost(null, DateTimeOffset.UtcNow);

        var markFound = () => incident.MarkFound(null);
        var confirmLost = () => incident.ConfirmLost(null, DateTimeOffset.UtcNow);

        markFound.Should().Throw<InvalidOperationException>();
        confirmLost.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void SearchTask_FailedState_IsTerminal()
    {
        var task = ParcelSearchTask.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ParcelSearchTaskType.VEHICLE_SWEEP,
            null,
            null,
            DateTimeOffset.UtcNow.AddMinutes(30));
        task.Fail("Vehicle sweep did not find the parcel.", null, DateTimeOffset.UtcNow);

        var assign = () => task.Assign(Guid.NewGuid());
        var overwrite = () => task.Complete("Late result", null, DateTimeOffset.UtcNow);

        assign.Should().Throw<InvalidOperationException>();
        overwrite.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Claim_Appeal_PreservesOriginalDecisionAudit()
    {
        var decidedBy = Guid.NewGuid();
        var appealedBy = Guid.NewGuid();
        var appealedAt = DateTimeOffset.UtcNow;
        var claim = ParcelClaim.Submit(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            appealedBy,
            12_000_000,
            1,
            50,
            30_000_000,
            4);
        claim.BeginReview();
        claim.Reject("Invoice did not match the parcel.", decidedBy, appealedAt.AddHours(-1));

        claim.Appeal("Submitted corrected invoice.", appealedBy, appealedAt);

        claim.Status.Should().Be(ParcelClaimStatus.APPEALED);
        claim.DecisionReason.Should().Be("Invoice did not match the parcel.");
        claim.DecidedBy.Should().Be(decidedBy);
        claim.AppealReason.Should().Be("Submitted corrected invoice.");
        claim.AppealedByUserId.Should().Be(appealedBy);
        claim.AppealedAt.Should().Be(appealedAt);
    }
}

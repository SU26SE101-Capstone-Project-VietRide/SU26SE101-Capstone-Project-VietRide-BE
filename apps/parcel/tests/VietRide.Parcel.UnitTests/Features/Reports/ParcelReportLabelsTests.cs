using FluentAssertions;
using VietRide.Parcel.Application.Features.Parcels.Reports;
using VietRide.Parcel.Domain.Enums;

namespace VietRide.Parcel.UnitTests.Features.Reports;

public sealed class ParcelReportLabelsTests
{
    [Fact]
    public void EveryParcelStatus_HasVietnameseLabel()
        => Enum.GetValues<ParcelStatus>()
            .Select(ParcelReportLabels.Status)
            .Should().OnlyContain(label => label != ParcelReportLabels.Unknown);

    [Fact]
    public void EverySizeCategory_HasVietnameseLabel()
        => Enum.GetValues<ParcelSizeCategory>()
            .Select(ParcelReportLabels.Size)
            .Should().OnlyContain(label => label != ParcelReportLabels.Unknown);
}

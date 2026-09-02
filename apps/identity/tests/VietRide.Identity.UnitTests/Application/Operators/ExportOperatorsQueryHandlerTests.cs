using System.Text;
using FluentAssertions;
using NSubstitute;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.Admin.ExportOperators;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Identity.UnitTests.Application.Operators;

public sealed class ExportOperatorsQueryHandlerTests
{
    [Fact]
    public void EveryRegistrationStatus_HasVietnameseLabel()
        => Enum.GetValues<OperatorRegistrationStatus>()
            .Select(OperatorExportLabels.RegistrationStatus)
            .Should().OnlyContain(label => label != OperatorExportLabels.Unknown);

    [Fact]
    public async Task Handle_UsesVietnameseBomAndNeutralizesFormulaValues()
    {
        var repository = Substitute.For<IOperatorRepository>();
        var operatorTenant = Operator.CreatePending(
            "=NHÀ XE, THỬ NGHIỆM",
            "DKKD-001",
            "0312345678",
            "contact@example.test",
            "+84900000000");
        repository.ListForExportAsync(
                Arg.Any<QueryOptions>(),
                null,
                null,
                null,
                null,
                "createdAt",
                Arg.Any<CancellationToken>())
            .Returns([operatorTenant]);
        var handler = new ExportOperatorsQueryHandler(repository, new FixedClock());

        var result = await handler.Handle(
            new ExportOperatorsQuery(
                "SYSTEM_ADMIN", null, null, null, null, null, null, null, null),
            CancellationToken.None);

        result.FileName.Should().Be("danh-sach-nha-xe-20260718.csv");
        result.ContentType.Should().Be("text/csv; charset=utf-8");
        result.Content.Take(3).Should().Equal(Encoding.UTF8.GetPreamble());
        var csv = Encoding.UTF8.GetString(result.Content.AsSpan(3));
        csv.Should().StartWith("Tên nhà xe,Email liên hệ,Số điện thoại liên hệ");
        csv.Should().Contain("\"'=NHÀ XE, THỬ NGHIỆM\"");
        csv.Should().Contain("'+84900000000");
        csv.Should().Contain(",Chờ duyệt,Có,");
        csv.Should().EndWith($",{operatorTenant.Id:D}{Environment.NewLine}");
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);
    }
}

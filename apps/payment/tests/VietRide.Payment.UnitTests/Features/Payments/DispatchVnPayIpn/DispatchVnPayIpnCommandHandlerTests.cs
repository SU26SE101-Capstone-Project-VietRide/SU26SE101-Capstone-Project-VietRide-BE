using Microsoft.Extensions.Logging.Abstractions;
using VietRide.Payment.Application.Abstractions.ExternalClients;
using VietRide.Payment.Application.Features.Payments.DispatchVnPayIpn;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Payment.UnitTests.Features.Payments.DispatchVnPayIpn;

public sealed class DispatchVnPayIpnCommandHandlerTests
{
    [Fact]
    public async Task EmptyConnectivityProbe_ReturnsInputRequiredWithoutVerifyingSignature()
    {
        var vnPay = new SignatureMustNotBeVerifiedVnPayClient();
        var handler = new DispatchVnPayIpnCommandHandler(
            vnPay,
            null!,
            null!,
            NullLogger<DispatchVnPayIpnCommandHandler>.Instance);

        var result = await handler.Handle(
            new DispatchVnPayIpnCommand(new Dictionary<string, string>()),
            CancellationToken.None);

        Assert.Equal("99", result.RspCode);
        Assert.Equal("INPUT_DATA_REQUIRED", result.Message);
    }

    private sealed class SignatureMustNotBeVerifiedVnPayClient : IVnPayClient
    {
        public string CreateTopUpRedirectUrl(
            Guid userId,
            Money amount,
            string vnPayTxnRef,
            string clientIpAddress,
            DateTimeOffset createdAt) => throw new NotSupportedException();

        public bool VerifySignature(IReadOnlyDictionary<string, string> parameters) =>
            throw new InvalidOperationException("Empty probes must be rejected before signature verification.");
    }
}

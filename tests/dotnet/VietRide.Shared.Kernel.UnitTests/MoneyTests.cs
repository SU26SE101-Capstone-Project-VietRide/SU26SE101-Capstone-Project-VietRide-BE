using FluentAssertions;
using VietRide.Shared.Kernel.ValueObjects;
using Xunit;

namespace VietRide.Shared.Kernel.UnitTests;

public class MoneyTests
{
    [Theory]
    [InlineData(1234, 1000)]
    [InlineData(999, 0)]
    [InlineData(2999, 2000)]
    [InlineData(0, 0)]
    [InlineData(50_000, 50_000)]
    [InlineData(123_456_789, 123_456_000)]
    public void FromRaw_Floors_To_Nearest_1000_VND(long input, long expected)
    {
        Money.FromRaw(input).Amount.Should().Be(expected);
    }

    [Fact]
    public void FromRaw_Negative_Throws()
    {
        var act = () => Money.FromRaw(-1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Add_Operator_Sums_Amounts()
    {
        var a = Money.FromRaw(50_000);
        var b = Money.FromRaw(30_000);
        (a + b).Amount.Should().Be(80_000);
    }

    [Fact]
    public void Zero_Has_Amount_Zero()
    {
        Money.Zero.Amount.Should().Be(0);
    }

    [Fact]
    public void ToString_Uses_Thousands_Separator_And_Vnd_Suffix()
    {
        Money.FromRaw(1_234_000).ToString().Should().Contain("1,234,000").And.Contain("VND");
    }

    [Theory]
    [InlineData(100_000, 50_000, true)]   // 100k > 50k
    [InlineData(50_000, 100_000, false)]
    [InlineData(50_000, 50_000, false)]
    public void Comparison_Operators_Work(long a, long b, bool aGreater)
    {
        (Money.FromRaw(a) > Money.FromRaw(b)).Should().Be(aGreater);
    }
}

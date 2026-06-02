using FluentAssertions;
using VietRide.Shared.Kernel.Primitives;
using Xunit;

namespace VietRide.Shared.Web.UnitTests.Primitives;

public sealed class QueryOptionsTests
{
    [Fact]
    public void Defaults_Are_Page1_PageSize20_SortDirDesc_IncludeDeletedFalse()
    {
        var opts = new QueryOptions();

        opts.Page.Should().Be(1);
        opts.PageSize.Should().Be(20);
        opts.SortDir.Should().Be("desc");
        opts.IncludeDeleted.Should().BeFalse();
        opts.Search.Should().BeNull();
        opts.SortBy.Should().BeNull();
    }

    [Theory]
    [InlineData(0, 1)]      // below minimum → clamped to 1
    [InlineData(-5, 1)]
    [InlineData(1, 1)]      // at minimum → unchanged
    [InlineData(50, 50)]    // within range → unchanged
    [InlineData(100, 100)]  // at maximum → unchanged
    [InlineData(101, 100)]  // above maximum → clamped to 100
    [InlineData(999, 100)]
    public void PageSize_Is_Clamped_To_1_100(int input, int expected)
    {
        var opts = new QueryOptions { PageSize = input };
        opts.PageSize.Should().Be(expected);
    }

    [Theory]
    [InlineData(0, 1)]   // below minimum → clamped to 1
    [InlineData(-1, 1)]
    [InlineData(1, 1)]   // valid
    [InlineData(5, 5)]
    public void Page_Below_One_Is_Clamped_To_1(int input, int expected)
    {
        var opts = new QueryOptions { Page = input };
        opts.Page.Should().Be(expected);
    }

    [Fact]
    public void Search_And_SortBy_Are_Carried_Through()
    {
        var opts = new QueryOptions
        {
            Search = "john",
            SearchIn = "email,phone",
            SortBy = "createdAt",
            SortDir = "asc",
        };

        opts.Search.Should().Be("john");
        opts.SearchIn.Should().Be("email,phone");
        opts.SortBy.Should().Be("createdAt");
        opts.SortDir.Should().Be("asc");
    }

    [Theory]
    [InlineData("asc", "asc")]
    [InlineData("ASC", "asc")]
    [InlineData(" asc ", "asc")]
    [InlineData("desc", "desc")]
    [InlineData("DESC", "desc")]
    [InlineData(" desc ", "desc")]
    public void SortDir_Is_Normalized_To_Asc_Or_Desc(string input, string expected)
    {
        var opts = new QueryOptions { SortDir = input };

        opts.SortDir.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("random")]
    public void SortDir_Rejects_Values_Outside_Asc_Or_Desc(string input)
    {
        var act = () => new QueryOptions { SortDir = input };

        act.Should().Throw<ArgumentException>()
            .WithMessage("SortDir must be 'asc' or 'desc'.*");
    }
}

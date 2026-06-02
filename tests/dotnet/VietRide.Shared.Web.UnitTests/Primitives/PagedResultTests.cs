using FluentAssertions;
using VietRide.Shared.Kernel.Primitives;
using Xunit;

namespace VietRide.Shared.Web.UnitTests.Primitives;

public sealed class PagedResultTests
{
    [Fact]
    public void Create_Computes_TotalPages_HasNextPage_HasPreviousPage()
    {
        var items = Enumerable.Range(1, 10).ToList();
        var result = PagedResult<int>.Create(items, page: 2, pageSize: 10, totalItems: 57);

        result.Items.Should().HaveCount(10);
        result.Page.Should().Be(2);
        result.PageSize.Should().Be(10);
        result.TotalItems.Should().Be(57);
        result.TotalPages.Should().Be(6);       // ceil(57/10) = 6
        result.HasNextPage.Should().BeTrue();    // page 2 < 6
        result.HasPreviousPage.Should().BeTrue(); // page 2 > 1
    }

    [Fact]
    public void Create_FirstPage_HasNoPreviousPage()
    {
        var result = PagedResult<string>.Create(["a", "b"], page: 1, pageSize: 20, totalItems: 2);

        result.HasPreviousPage.Should().BeFalse();
        result.HasNextPage.Should().BeFalse();
        result.TotalPages.Should().Be(1);
    }

    [Fact]
    public void Create_LastPage_HasNoNextPage()
    {
        var result = PagedResult<int>.Create([1], page: 3, pageSize: 20, totalItems: 57);

        result.TotalPages.Should().Be(3); // ceil(57/20) = 3
        result.HasNextPage.Should().BeFalse();
        result.HasPreviousPage.Should().BeTrue();
    }

    [Fact]
    public void Create_EmptyItems_Returns_ZeroTotalPages()
    {
        var result = PagedResult<int>.Create([], page: 1, pageSize: 20, totalItems: 0);

        result.TotalPages.Should().Be(0);
        result.HasNextPage.Should().BeFalse();
        result.HasPreviousPage.Should().BeFalse();
    }
}

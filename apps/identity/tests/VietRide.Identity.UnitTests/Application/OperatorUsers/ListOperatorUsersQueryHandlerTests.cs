using FluentAssertions;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.OperatorUsers.ListOperatorUsers;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Identity.UnitTests.Application.OperatorUsers;

public sealed class ListOperatorUsersQueryHandlerTests
{
    private static readonly Guid OperatorId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid OtherOperatorId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public async Task Handle_OperatorAdmin_ReturnsOwnOperatorUsersAndPassesFilters()
    {
        var driver = CreateUser(
            UserRole.DRIVER,
            OperatorId,
            "driver@example.com",
            "+84901112222",
            "Driver One",
            new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero));
        var users = new FakeUserRepository(PagedResult<User>.Create([driver], 1, 20, 1));
        var handler = new ListOperatorUsersQueryHandler(users);

        var result = await handler.Handle(
            new ListOperatorUsersQuery(
                ListOperatorUsersScope.Operator,
                UserRole.OPERATOR_ADMIN.ToString(),
                OperatorId,
                OperatorId: null,
                Page: 1,
                PageSize: 20,
                Search: "driver",
                SortBy: "email",
                SortDir: "asc",
                Role: UserRole.DRIVER.ToString(),
                Status: UserStatus.PENDING_INITIAL_PASSWORD.ToString()),
            CancellationToken.None);

        users.Calls.Should().Be(1);
        users.CapturedOperatorId.Should().Be(OperatorId);
        users.CapturedRole.Should().Be(UserRole.DRIVER);
        users.CapturedStatus.Should().Be(UserStatus.PENDING_INITIAL_PASSWORD);
        users.CapturedOptions.Should().NotBeNull();
        users.CapturedOptions!.Search.Should().Be("driver");
        users.CapturedOptions.SortBy.Should().Be("email");
        users.CapturedOptions.SortDir.Should().Be("asc");

        var item = result.Items.Should().ContainSingle().Subject;
        item.UserId.Should().Be(driver.Id);
        item.Email.Should().Be("driver@example.com");
        item.Phone.Should().Be("+84901112222");
        item.DisplayName.Should().Be("Driver One");
        item.Role.Should().Be(UserRole.DRIVER.ToString());
        item.Status.Should().Be(UserStatus.PENDING_INITIAL_PASSWORD.ToString());
        item.OperatorId.Should().Be(OperatorId);
        item.CreatedAt.Should().Be(driver.CreatedAt);
        item.AvatarUrl.Should().BeNull();
    }

    [Fact]
    public async Task Handle_SystemAdmin_ReturnsAllOperatorUsersAndPassesRoleFilter()
    {
        var assistant = CreateUser(
            UserRole.ASSISTANT,
            OtherOperatorId,
            "assistant@example.com",
            "+84903334444",
            "Assistant One",
            new DateTimeOffset(2026, 6, 2, 10, 0, 0, TimeSpan.Zero));
        var users = new FakeUserRepository(PagedResult<User>.Create([assistant], 1, 20, 1));
        var handler = new ListOperatorUsersQueryHandler(users);

        var result = await handler.Handle(
            new ListOperatorUsersQuery(
                ListOperatorUsersScope.Admin,
                UserRole.SYSTEM_ADMIN.ToString(),
                CallerOperatorId: null,
                OperatorId: null,
                Page: null,
                PageSize: null,
                Search: null,
                SortBy: null,
                SortDir: null,
                Role: UserRole.ASSISTANT.ToString(),
                Status: null),
            CancellationToken.None);

        users.CapturedOperatorId.Should().BeNull();
        users.CapturedRole.Should().Be(UserRole.ASSISTANT);
        users.CapturedOptions!.Page.Should().Be(1);
        users.CapturedOptions.PageSize.Should().Be(20);
        users.CapturedOptions.SortBy.Should().Be("createdAt");
        users.CapturedOptions.SortDir.Should().Be("desc");
        result.Items.Should().ContainSingle().Which.Role.Should().Be(UserRole.ASSISTANT.ToString());
        result.Items[0].OperatorId.Should().Be(OtherOperatorId);
    }

    [Fact]
    public async Task Handle_OperatorAdminWithoutOperatorScope_ThrowsForbidden()
    {
        var users = new FakeUserRepository(PagedResult<User>.Create([], 1, 20, 0));
        var handler = new ListOperatorUsersQueryHandler(users);

        var act = () => handler.Handle(
            new ListOperatorUsersQuery(
                ListOperatorUsersScope.Operator,
                UserRole.OPERATOR_ADMIN.ToString(),
                CallerOperatorId: null,
                OperatorId: null,
                Page: null,
                PageSize: null,
                Search: null,
                SortBy: null,
                SortDir: null,
                Role: null,
                Status: null),
            CancellationToken.None);

        var assertion = await act.Should().ThrowAsync<ForbiddenException>();
        assertion.Which.ErrorCode.Should().Be("FORBIDDEN");
        users.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Handle_InvalidSortBy_ThrowsInvalidSortField()
    {
        var handler = new ListOperatorUsersQueryHandler(
            new FakeUserRepository(PagedResult<User>.Create([], 1, 20, 0)));

        var act = () => handler.Handle(
            new ListOperatorUsersQuery(
                ListOperatorUsersScope.Admin,
                UserRole.SYSTEM_ADMIN.ToString(),
                CallerOperatorId: null,
                OperatorId: null,
                Page: null,
                PageSize: null,
                Search: null,
                SortBy: "operatorId",
                SortDir: null,
                Role: null,
                Status: null),
            CancellationToken.None);

        var assertion = await act.Should().ThrowAsync<BadRequestException>();
        assertion.Which.ErrorCode.Should().Be("INVALID_SORT_FIELD");
    }

    private static User CreateUser(
        UserRole role,
        Guid operatorId,
        string email,
        string phone,
        string displayName,
        DateTimeOffset createdAt)
    {
        var user = User.CreateOperatorScopedPendingPassword(
            email,
            PhoneNumber.Parse(phone),
            displayName,
            role,
            operatorId);
        user.CreatedAt = createdAt;
        return user;
    }

    private sealed class FakeUserRepository : IUserRepository
    {
        private readonly PagedResult<User> _result;

        public FakeUserRepository(PagedResult<User> result)
        {
            _result = result;
        }

        public int Calls { get; private set; }
        public QueryOptions? CapturedOptions { get; private set; }
        public Guid? CapturedOperatorId { get; private set; }
        public UserRole? CapturedRole { get; private set; }
        public UserStatus? CapturedStatus { get; private set; }

        public Task<PagedResult<User>> ListOperatorUsersAsync(
            QueryOptions options,
            Guid? operatorId,
            UserRole? role,
            UserStatus? status,
            CancellationToken ct = default)
        {
            Calls++;
            CapturedOptions = options;
            CapturedOperatorId = operatorId;
            CapturedRole = role;
            CapturedStatus = status;
            return Task.FromResult(_result);
        }

        public Task<User?> GetByEmailAsync(string emailLower, CancellationToken ct = default) => Task.FromResult<User?>(null);
        public Task<User?> GetByPhoneAsync(string e164Phone, CancellationToken ct = default) => Task.FromResult<User?>(null);
        public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<User?>(null);
        public Task<User> AddAsync(User entity, CancellationToken cancellationToken = default) => Task.FromResult(entity);
        public void Update(User entity) { }
        public void Remove(User entity) { }
        public IQueryable<User> Query() => Array.Empty<User>().AsQueryable();
        public IQueryable<User> QueryNoTracking() => Array.Empty<User>().AsQueryable();

        public Task<IReadOnlyList<Guid>> ListActiveOperatorAdminIdsAsync(
            Guid operatorId,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Guid>>([]);
    }
}

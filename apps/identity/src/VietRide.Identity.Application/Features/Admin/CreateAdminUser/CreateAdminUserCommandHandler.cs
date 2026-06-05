using MediatR;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Identity.Application.Features.Admin.CreateAdminUser;

public sealed class CreateAdminUserCommandHandler : IRequestHandler<CreateAdminUserCommand, CreateAdminUserResponseDto>
{
    private readonly IUserRepository _users;

    public CreateAdminUserCommandHandler(IUserRepository users)
    {
        _users = users;
    }

    public async Task<CreateAdminUserResponseDto> Handle(
        CreateAdminUserCommand request,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(request.CallerRole, UserRole.SYSTEM_ADMIN.ToString(), StringComparison.Ordinal))
            throw new ForbiddenException("FORBIDDEN", "Only SYSTEM_ADMIN can create admin users.");

        if (!string.Equals(request.Role, UserRole.SYSTEM_ADMIN.ToString(), StringComparison.Ordinal))
        {
            throw new ValidationException(
                "Only SYSTEM_ADMIN can be created by this endpoint.",
                [new ValidationError("role", "Only SYSTEM_ADMIN can be created by this endpoint.")]);
        }

        var emailLower = request.Email.Trim().ToLowerInvariant();
        var existing = await _users.GetByEmailAsync(emailLower, cancellationToken);
        if (existing is not null)
            throw new ConflictException("AUTH_EMAIL_ALREADY_REGISTERED", "Email is already registered.");

        var user = User.CreateAdminPendingPassword(
            email: emailLower,
            displayName: request.DisplayName.Trim());

        await _users.AddAsync(user, cancellationToken);

        return new CreateAdminUserResponseDto(
            UserId: user.Id,
            Email: user.Email,
            DisplayName: user.DisplayName,
            Role: user.Role.ToString(),
            Status: user.Status.ToString());
    }
}

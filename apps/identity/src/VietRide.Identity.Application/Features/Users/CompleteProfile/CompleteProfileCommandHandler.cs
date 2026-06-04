using MediatR;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Identity.Application.Features.Users.CompleteProfile;

public sealed class CompleteProfileCommandHandler : IRequestHandler<CompleteProfileCommand, CompleteProfileResponseDto>
{
    private const string CompletedMessage = "Hồ sơ hoàn tất.";

    private readonly IUserRepository _users;
    private readonly IActivityLogRepository _activityLogs;

    public CompleteProfileCommandHandler(
        IUserRepository users,
        IActivityLogRepository activityLogs)
    {
        _users = users;
        _activityLogs = activityLogs;
    }

    public async Task<CompleteProfileResponseDto> Handle(
        CompleteProfileCommand request,
        CancellationToken cancellationToken)
    {
        var user = await _users.GetByIdAsync(request.UserId, cancellationToken)
            ?? throw new NotFoundException("User", request.UserId);

        if (user.Phone is not null)
        {
            throw new ValidationException(
                "Phone is already set and cannot be overwritten through complete profile.",
                [new ValidationError("phone", "Phone is already set.")]);
        }

        PhoneNumber phone;
        try
        {
            phone = PhoneNumber.Parse(request.Phone.Trim());
        }
        catch (ArgumentException)
        {
            throw new BadRequestException("AUTH_PHONE_INVALID_FORMAT", "Invalid phone number format.");
        }

        var existingByPhone = await _users.GetByPhoneAsync(phone.Value, cancellationToken);
        if (existingByPhone is not null)
            throw new ConflictException("AUTH_PHONE_ALREADY_REGISTERED", "Phone number is already registered.");

        user.CompleteProfile(phone);
        _users.Update(user);

        var activityLog = ActivityLog.Create(user.Id, ActivityLogAction.COMPLETE_PROFILE);
        await _activityLogs.AddAsync(activityLog, cancellationToken);

        return new CompleteProfileResponseDto(
            UserId: user.Id,
            Phone: phone.Value,
            Message: CompletedMessage);
    }
}

using MediatR;
using VietRide.Identity.Application.Abstractions;

namespace VietRide.Identity.Application.Features.Auth.GetJwks;

public sealed class GetJwksQueryHandler : IRequestHandler<GetJwksQuery, string>
{
    private readonly IJwksProvider _jwksProvider;

    public GetJwksQueryHandler(IJwksProvider jwksProvider)
    {
        _jwksProvider = jwksProvider;
    }

    public Task<string> Handle(GetJwksQuery request, CancellationToken cancellationToken)
        => Task.FromResult(_jwksProvider.GetJwks());
}

using System.Security.Cryptography;
using System.Text;

namespace VietRide.Parcel.Application.Features.Parcels;

public static class DeliveryTokenHasher
{
    public static string Hash(Guid token)
    {
        var normalizedToken = token.ToString("D").ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedToken));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

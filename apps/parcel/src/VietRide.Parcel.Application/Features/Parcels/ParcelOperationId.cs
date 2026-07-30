using System.Security.Cryptography;
using System.Text;

namespace VietRide.Parcel.Application.Features.Parcels;

public static class ParcelOperationId
{
    public static Guid Create(Guid sourceId, Guid parcelId, string phase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phase);

        var material = Encoding.UTF8.GetBytes(
            $"{sourceId:N}:{parcelId:N}:{phase.Trim().ToUpperInvariant()}");
        var hash = SHA256.HashData(material);

        hash[6] = (byte)((hash[6] & 0x0F) | 0x40);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash.AsSpan(0, 16), bigEndian: true);
    }
}

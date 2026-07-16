using System.Buffers.Binary;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace VietRide.Shared.Web.Idempotency;

internal static class IdempotencyFingerprint
{
    public static async Task<string> ComputeAsync(HttpContext context)
    {
        var request = context.Request;
        var body = await ReadRawBodyAsync(request, context.RequestAborted);
        var canonicalQuery = BuildCanonicalQuery(request.Query);

        using var frame = new MemoryStream();
        WriteFrame(frame, Encoding.UTF8.GetBytes(ResolveSubject(context.User)));
        WriteFrame(frame, Encoding.UTF8.GetBytes(request.Method.ToUpperInvariant()));
        WriteFrame(frame, Encoding.UTF8.GetBytes(string.Concat(request.PathBase.Value, request.Path.Value)));
        WriteFrame(frame, canonicalQuery);
        WriteFrame(frame, body);

        return Convert.ToHexString(SHA256.HashData(frame.ToArray()));
    }

    public static string ResolveSubject(ClaimsPrincipal user)
    {
        var subject = user.FindFirstValue("sub");
        if (!string.IsNullOrWhiteSpace(subject))
        {
            return subject;
        }

        subject = user.FindFirstValue(ClaimTypes.NameIdentifier);
        return string.IsNullOrWhiteSpace(subject) ? string.Empty : subject;
    }

    private static async Task<byte[]> ReadRawBodyAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        request.EnableBuffering();
        request.Body.Position = 0;

        using var memory = new MemoryStream();
        try
        {
            await request.Body.CopyToAsync(memory, cancellationToken);
            return memory.ToArray();
        }
        finally
        {
            request.Body.Position = 0;
        }
    }

    private static byte[] BuildCanonicalQuery(IQueryCollection query)
    {
        var pairs = query
            .SelectMany(pair => pair.Value.Select(value => new KeyValuePair<string, string>(pair.Key, value ?? string.Empty)))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ThenBy(pair => pair.Value, StringComparer.Ordinal)
            .ToArray();

        using var canonical = new MemoryStream();
        WriteInt32(canonical, pairs.Length);
        foreach (var pair in pairs)
        {
            WriteFrame(canonical, Encoding.UTF8.GetBytes(pair.Key));
            WriteFrame(canonical, Encoding.UTF8.GetBytes(pair.Value));
        }

        return canonical.ToArray();
    }

    private static void WriteFrame(Stream stream, ReadOnlySpan<byte> value)
    {
        WriteInt32(stream, value.Length);
        stream.Write(value);
    }

    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, value);
        stream.Write(length);
    }
}

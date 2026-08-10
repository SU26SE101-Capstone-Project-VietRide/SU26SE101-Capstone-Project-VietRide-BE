using System.Text.Json.Serialization;

namespace VietRide.Shared.Kernel.Primitives;

/// <summary>
/// Standard API response envelope for all FE-facing HTTP responses (ADR 0004).
/// Error variant — carries <see cref="ApiError"/> in place of data.
/// </summary>
public sealed record ApiResponse
{
    public bool Success { get; init; }
    public int StatusCode { get; init; }
    public ApiError Error { get; init; } = null!;
    public ApiMeta Meta { get; init; } = null!;

    public static ApiResponse Failure(int statusCode, ApiError error, ApiMeta meta)
        => new() { Success = false, StatusCode = statusCode, Error = error, Meta = meta };
}

/// <summary>
/// Standard API response envelope for all FE-facing HTTP responses (ADR 0004).
/// Success variant — wraps <typeparamref name="T"/> data.
/// </summary>
public sealed record ApiResponse<T>
{
    public bool Success { get; init; }
    public int StatusCode { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; init; }
    public T Data { get; init; } = default!;
    public ApiMeta Meta { get; init; } = null!;

    public static ApiResponse<T> SuccessResponse(int statusCode, T data, ApiMeta meta, string? message = null)
        => new() { Success = true, StatusCode = statusCode, Data = data, Meta = meta, Message = message };

    public static ApiResponse<T> Ok(T data, ApiMeta meta, string? message = null)
        => SuccessResponse(200, data, meta, message);

    public static ApiResponse<T> Created(T data, ApiMeta meta, string? message = null)
        => SuccessResponse(201, data, meta, message);
}

/// <summary>Error detail carried inside <see cref="ApiResponse"/> on failure.</summary>
public sealed record ApiError
{
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ApiFieldError>? Fields { get; init; }
}

/// <summary>Per-field validation error inside <see cref="ApiError.Fields"/>.</summary>
public sealed record ApiFieldError(string Field, string Message);

/// <summary>Cross-cutting metadata present in every envelope.</summary>
public sealed record ApiMeta
{
    public string TraceId { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; }

    public static ApiMeta Create(string traceId)
        => new() { TraceId = traceId, Timestamp = DateTimeOffset.UtcNow };
}

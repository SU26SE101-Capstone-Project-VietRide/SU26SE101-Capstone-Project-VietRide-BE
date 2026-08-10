using System.Text.Json;
using System.Text.Json.Serialization;

namespace VietRide.Shared.Web.Serialization;

/// <summary>
/// Applies the public timestamp presentation policy inside otherwise-untyped JSON payloads,
/// such as admin DLQ event bodies, without mutating their canonical stored representation.
/// </summary>
public sealed class FrontendJsonElementConverter : JsonConverter<JsonElement>
{
    private readonly Func<bool> useVietnamPresentation;

    public FrontendJsonElementConverter(Func<bool> useVietnamPresentation)
    {
        this.useVietnamPresentation = useVietnamPresentation;
    }

    public override JsonElement Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) => JsonDocument.ParseValue(ref reader).RootElement.Clone();

    public override void Write(
        Utf8JsonWriter writer,
        JsonElement value,
        JsonSerializerOptions options)
    {
        if (!useVietnamPresentation())
        {
            value.WriteTo(writer);
            return;
        }

        var transformed = ApiTimestampPresentation.TransformJsonForFrontend(
            JsonSerializer.SerializeToUtf8Bytes(value),
            "application/json");
        using var document = JsonDocument.Parse(transformed);
        document.RootElement.WriteTo(writer);
    }
}

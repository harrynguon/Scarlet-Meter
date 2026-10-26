using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScarletMeter.App.Storage;

/// <summary>
/// An enum in a saved file, written by its camel-case name and read leniently: a name this build does not know (a later
/// build's new value) or a number reads as -1, never undefined-but-plausible, so the file's normalizer resets it and
/// says so, rather than the whole file being taken as unreadable and set aside (deep audit on #48). Names are matched
/// ignoring case; a name that is only digits is not taken as a name.
/// </summary>
public sealed class LenientEnumConverter<T> : JsonConverter<T> where T : struct, Enum
{
    /// <summary>What an unknown name or a number reads as: not a defined value.</summary>
    public static T Unknown { get; } = (T)Enum.ToObject(typeof(T), -1);

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                var name = reader.GetString();
                return name is not null && !name.All(char.IsAsciiDigit) && !name.StartsWith('-')
                    && Enum.TryParse<T>(name, ignoreCase: true, out var value) && Enum.IsDefined(value) ? value : Unknown;
            case JsonTokenType.Number:
                return Unknown;
            default:
                throw new JsonException($"A {typeof(T).Name} must be a name.");
        }
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        writer.WriteStringValue(JsonNamingPolicy.CamelCase.ConvertName(value.ToString()));
}

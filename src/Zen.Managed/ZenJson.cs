using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Zen.Managed;

/// <summary>
/// Conversion between <see cref="ZenValue"/> and JSON, used to ingest context
/// objects (the "input parameters") and to materialise results. Mirrors the
/// serde_json path used by the native implementation so both sides pay an
/// equivalent serialisation cost.
/// </summary>
public static class ZenJson
{
    public static ZenValue Parse(string json)
    {
        using var doc = JsonDocument.Parse(json ?? "null", new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        });
        return ToZen(doc.RootElement);
    }

    private static ZenValue ToZen(JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Null: return ZenValue.Null;
            case JsonValueKind.True: return ZenValue.True;
            case JsonValueKind.False: return ZenValue.False;
            case JsonValueKind.Number: return ZenValue.FromNumber(e.GetDouble());
            case JsonValueKind.String: return ZenValue.FromString(e.GetString() ?? "");
            case JsonValueKind.Array:
            {
                var list = new List<ZenValue>();
                foreach (var item in e.EnumerateArray()) list.Add(ToZen(item));
                return ZenValue.FromArray(list.ToArray());
            }
            case JsonValueKind.Object:
            {
                var dict = new Dictionary<string, ZenValue>(StringComparer.Ordinal);
                foreach (var prop in e.EnumerateObject()) dict[prop.Name] = ToZen(prop.Value);
                return ZenValue.FromObject(dict);
            }
            default: return ZenValue.Null;
        }
    }

    public static string Serialize(ZenValue v)
    {
        using var ms = new MemoryStream(256);
        using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
        {
            WriteValue(writer, v);
            writer.Flush();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static void WriteValue(Utf8JsonWriter w, ZenValue v)
    {
        switch (v.Kind)
        {
            case ZenKind.Null: w.WriteNullValue(); break;
            case ZenKind.Boolean: w.WriteBooleanValue(v.Boolean); break;
            case ZenKind.Number: w.WriteNumberValue(v.Number); break;
            case ZenKind.String: w.WriteStringValue(v.String); break;
            case ZenKind.Array:
                w.WriteStartArray();
                if (v.Array != null) foreach (var e in v.Array) WriteValue(w, e);
                w.WriteEndArray();
                break;
            case ZenKind.Object:
                w.WriteStartObject();
                if (v.Object != null)
                    foreach (var kv in v.Object) { w.WritePropertyName(kv.Key); WriteValue(w, kv.Value); }
                w.WriteEndObject();
                break;
        }
    }
}

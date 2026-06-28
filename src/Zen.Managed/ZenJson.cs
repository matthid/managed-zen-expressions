using System.Buffers;
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
        if (string.IsNullOrEmpty(json)) return ZenValue.Null;
        // Single-pass Utf8JsonReader → ZenValue. Avoids JsonDocument's pooled buffer
        // and the double number-parse (JsonDocument tokenizes, then GetDouble re-parses);
        // each number/atom is read exactly once and placed directly into the value tree.
        var bytes = Encoding.UTF8.GetBytes(json);
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        });
        if (!reader.Read()) return ZenValue.Null;
        return Consume(ref reader);
    }

    private static ZenValue Consume(ref Utf8JsonReader r)
    {
        switch (r.TokenType)
        {
            case JsonTokenType.Null: return ZenValue.Null;
            case JsonTokenType.True: return ZenValue.True;
            case JsonTokenType.False: return ZenValue.False;
            case JsonTokenType.Number: return ZenValue.FromNumber(r.GetDouble());
            case JsonTokenType.String: return ZenValue.FromString(r.GetString() ?? "");
            case JsonTokenType.StartArray:
            {
                // ArrayPool-backed growth: avoids the ~log(n) resize allocations a
                // List<> would create for large arrays (the heavy-load bottleneck).
                var buf = ArrayPool<ZenValue>.Shared.Rent(16);
                int n = 0;
                try
                {
                    while (r.Read() && r.TokenType != JsonTokenType.EndArray)
                    {
                        if (n == buf.Length)
                        {
                            var bigger = ArrayPool<ZenValue>.Shared.Rent(buf.Length * 2);
                            Array.Copy(buf, bigger, n);
                            ArrayPool<ZenValue>.Shared.Return(buf);
                            buf = bigger;
                        }
                        buf[n++] = Consume(ref r);
                    }
                    var exact = new ZenValue[n];
                    Array.Copy(buf, exact, n);
                    return ZenValue.FromArray(exact);
                }
                finally
                {
                    ArrayPool<ZenValue>.Shared.Return(buf);
                }
            }
            case JsonTokenType.StartObject:
            {
                var dict = new Dictionary<string, ZenValue>(StringComparer.Ordinal);
                while (r.Read() && r.TokenType == JsonTokenType.PropertyName)
                {
                    string key = r.GetString() ?? "";
                    r.Read();
                    dict[key] = Consume(ref r);
                }
                return ZenValue.FromObject(dict);
            }
            default:
                throw new ZenException($"Unexpected JSON token {r.TokenType}");
        }
    }

    /// <summary>Convert a <see cref="JsonElement"/> (e.g. a result from a third-party
    /// engine) into a <see cref="ZenValue"/> for comparison.</summary>
    public static ZenValue FromElement(JsonElement e) => ToZen(e);

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

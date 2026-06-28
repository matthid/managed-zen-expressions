using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
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
        => string.IsNullOrEmpty(json) ? ZenValue.Null : Parse(json.AsSpan());

    /// <summary>Parse JSON directly from a UTF-16 span — no UTF-8 encode step
    /// (Utf8JsonReader is UTF-8-only, which would force one), and single-pass
    /// (no JsonDocument pooled buffer / double number-parse). Each atom is read
    /// once and placed straight into the value tree.</summary>
    public static ZenValue Parse(ReadOnlySpan<char> s)
    {
        int i = 0;
        SkipWs(s, ref i);
        return i < s.Length ? Consume(s, ref i) : ZenValue.Null;
    }

    private static void SkipWs(ReadOnlySpan<char> s, ref int i)
    {
        while (i < s.Length && s[i] <= ' ') i++;
    }

    private static ZenValue Consume(ReadOnlySpan<char> s, ref int i)
    {
        SkipWs(s, ref i);
        char c = s[i];
        switch (c)
        {
            case '{': return ReadObject(s, ref i);
            case '[': return ReadArray(s, ref i);
            case '"': return ZenValue.FromString(ReadString(s, ref i));
            case 't': i += 4; return ZenValue.True;   // true
            case 'f': i += 5; return ZenValue.False;  // false
            case 'n': i += 4; return ZenValue.Null;   // null
            default:   return ZenValue.FromNumber(ReadNumber(s, ref i));
        }
    }

    private static ZenValue ReadArray(ReadOnlySpan<char> s, ref int i)
    {
        i++; // consume '['
        var buf = ArrayPool<ZenValue>.Shared.Rent(16);
        int n = 0;
        try
        {
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return ZenValue.FromArray(System.Array.Empty<ZenValue>()); }
            while (true)
            {
                if (n == buf.Length)
                {
                    var bigger = ArrayPool<ZenValue>.Shared.Rent(buf.Length * 2);
                    System.Array.Copy(buf, bigger, n);
                    ArrayPool<ZenValue>.Shared.Return(buf);
                    buf = bigger;
                }
                buf[n++] = Consume(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length) break;
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; break; }
                break;
            }
            var exact = new ZenValue[n];
            System.Array.Copy(buf, exact, n);
            return ZenValue.FromArray(exact);
        }
        finally
        {
            ArrayPool<ZenValue>.Shared.Return(buf);
        }
    }

    private static ZenValue ReadObject(ReadOnlySpan<char> s, ref int i)
    {
        i++; // consume '{'
        var dict = new Dictionary<string, ZenValue>(StringComparer.Ordinal);
        SkipWs(s, ref i);
        if (i < s.Length && s[i] == '}') { i++; return ZenValue.FromObject(dict); }
        while (true)
        {
            SkipWs(s, ref i);
            string key = s[i] == '"' ? ReadString(s, ref i) : s[i..].ToString();
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ':') i++;
            dict[key] = Consume(s, ref i);
            SkipWs(s, ref i);
            if (i >= s.Length) break;
            if (s[i] == ',') { i++; continue; }
            if (s[i] == '}') { i++; break; }
            break;
        }
        return ZenValue.FromObject(dict);
    }

    private static string ReadString(ReadOnlySpan<char> s, ref int i)
    {
        i++; // opening quote
        int start = i;
        while (i < s.Length && s[i] != '"')
        {
            if (s[i] == '\\') return ReadStringEscaped(s, ref i, start);
            i++;
        }
        string result = new string(s.Slice(start, i - start));
        i++; // closing quote
        return result;
    }

    private static string ReadStringEscaped(ReadOnlySpan<char> s, ref int i, int start)
    {
        var sb = new StringBuilder();
        sb.Append(s.Slice(start, i - start));
        while (i < s.Length && s[i] != '"')
        {
            if (s[i] == '\\')
            {
                i++;
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'u':
                        sb.Append((char)Hex4(s, i)); i += 4; break;
                    default: sb.Append(e); break;
                }
            }
            else { sb.Append(s[i]); i++; }
        }
        i++; // closing quote
        return sb.ToString();
    }

    private static int Hex4(ReadOnlySpan<char> s, int i)
    {
        int v = 0;
        for (int k = 0; k < 4; k++) v = v * 16 + HexVal(s[i + k]);
        return v;
    }

    private static int HexVal(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => 0,
    };

    private static double ReadNumber(ReadOnlySpan<char> s, ref int i)
    {
        int start = i;
        if (s[i] == '-') i++;
        while (i < s.Length)
        {
            char c = s[i];
            if ((c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-') i++;
            else break;
        }
        return double.Parse(s.Slice(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
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

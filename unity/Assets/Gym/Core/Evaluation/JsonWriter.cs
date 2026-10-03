using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Gym.Core.Evaluation
{
    /// <summary>Minimal JSON writer for the evaluation detail files.</summary>
    public static class JsonWriter
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Serialize(object value)
        {
            var sb = new StringBuilder();
            WriteJson(sb, value, 0);
            return sb.ToString();
        }

        static void WriteJson(StringBuilder sb, object value, int indent)
        {
            switch (value)
            {
                case null: sb.Append("null"); return;
                case string s: WriteString(sb, s); return;
                case bool b: sb.Append(b ? "true" : "false"); return;
                case int i: sb.Append(i.ToString(Inv)); return;
                case long l: sb.Append(l.ToString(Inv)); return;
                case decimal m: sb.Append(m.ToString(Inv)); return;
                case float f: WriteDouble(sb, f); return;
                case double d: WriteDouble(sb, d); return;
                case DateTime t: WriteString(sb, t.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", Inv)); return;
                case JsonObject o:
                {
                    if (o.Count == 0) { sb.Append("{}"); return; }
                    sb.Append("{\n");
                    int k = 0;
                    foreach (KeyValuePair<string, object> pair in o)
                    {
                        sb.Append(' ', (indent + 1) * 2);
                        WriteString(sb, pair.Key);
                        sb.Append(": ");
                        WriteJson(sb, pair.Value, indent + 1);
                        sb.Append(++k < o.Count ? ",\n" : "\n");
                    }
                    sb.Append(' ', indent * 2).Append('}');
                    return;
                }
                case IEnumerable list:
                {
                    var values = new List<object>();
                    foreach (object item in list) values.Add(item);
                    if (values.Count == 0) { sb.Append("[]"); return; }
                    sb.Append("[\n");
                    for (int k = 0; k < values.Count; k++)
                    {
                        sb.Append(' ', (indent + 1) * 2);
                        WriteJson(sb, values[k], indent + 1);
                        sb.Append(k + 1 < values.Count ? ",\n" : "\n");
                    }
                    sb.Append(' ', indent * 2).Append(']');
                    return;
                }
                default: WriteString(sb, Convert.ToString(value, Inv)); return;
            }
        }

        static void WriteDouble(StringBuilder sb, double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) sb.Append("null");
            else sb.Append(d.ToString("R", Inv));
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", Inv));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}

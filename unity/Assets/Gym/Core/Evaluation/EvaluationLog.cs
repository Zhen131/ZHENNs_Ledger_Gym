using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Gym.Core.Evaluation
{
    /// <summary>
    /// The append-only evaluation log and the per-run detail files:
    /// <c>&lt;dir&gt;/log.csv</c> and <c>&lt;dir&gt;/runs/&lt;UTC time&gt;-&lt;policy&gt;-&lt;segment&gt;.json</c>.
    /// The log is only ever appended to; a file whose header differs is refused.
    /// </summary>
    public static class EvaluationLog
    {
        public const string FileName = "log.csv";
        public const string RunsFolder = "runs";

        public static readonly string[] Columns =
        {
            "timestamp_utc", "kind", "policy", "model_run_id", "model_sha256", "symbol", "segment", "segment_start",
            "segment_end", "fee_rate", "fixed_fee", "slippage", "seeds", "total_return", "max_drawdown", "sharpe",
            "trades", "rejected", "turnover", "fees_paid", "fees_pct", "exposure", "notes",
        };

        public static string Header => string.Join(",", Columns);

        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>Append one row; creates the file with its header if it does not exist. Returns the path.</summary>
        public static string Append(string directory, EvaluationRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, FileName);
            string line = FormatRow(record);

            if (!File.Exists(path))
            {
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                    Write(stream, Header + "\n" + line + "\n");
                return path;
            }

            string firstLine = ReadFirstLine(path);
            if (firstLine != Header)
                throw new InvalidDataException(
                    $"{path} starts with the header\n  '{firstLine}'\nbut this version writes\n  '{Header}'.\n" +
                    "Nothing was written. Move the old log aside (it is never rewritten) and run again.");

            bool needsNewline = !EndsWithNewline(path);
            using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
                Write(stream, (needsNewline ? "\n" : "") + line + "\n");
            return path;
        }

        /// <summary>Write the detail file for one evaluation. Never overwrites; returns the path.</summary>
        public static string WriteRunDetails(string directory, DateTime timestampUtc, string policy, string segment, JsonObject details)
        {
            string runs = Path.Combine(directory, RunsFolder);
            Directory.CreateDirectory(runs);
            string stem = $"{timestampUtc.ToUniversalTime().ToString("yyyyMMdd'T'HHmmssfff'Z'", Inv)}-{Safe(policy)}-{Safe(segment)}";
            string path = Path.Combine(runs, stem + ".json");
            for (int n = 2; File.Exists(path); n++) path = Path.Combine(runs, $"{stem}-{n}.json");
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                Write(stream, Json(details) + "\n");
            return path;
        }

        public static string FormatRow(EvaluationRecord r)
        {
            var cells = new[]
            {
                r.TimestampUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", Inv),
                r.Kind, r.Policy, r.ModelRunId, r.ModelSha256, r.Symbol, r.Segment,
                r.SegmentStart.ToString("yyyy-MM-dd", Inv), r.SegmentEnd.ToString("yyyy-MM-dd", Inv),
                Number(r.FeeRate), Number(r.FixedFee), Number(r.Slippage), r.Seeds.ToString(Inv),
                Number(r.TotalReturn), Number(r.MaxDrawdown), Number(r.Sharpe), Number(r.Trades), Number(r.Rejected),
                Number(r.Turnover), Number(r.FeesPaid), Number(r.FeesPct), Number(r.Exposure), r.Notes,
            };
            var sb = new StringBuilder();
            for (int i = 0; i < cells.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Csv(cells[i] ?? ""));
            }
            return sb.ToString();
        }

        /// <summary>The metrics of one episode as a JSON object.</summary>
        public static JsonObject MetricsJson(EpisodeMetrics m) => new JsonObject
        {
            { "total_return", m.TotalReturn },
            { "max_drawdown", m.MaxDrawdown },
            { "sharpe", m.SharpeAnnualized },
            { "trades", m.Trades },
            { "rejected", m.Rejected },
            { "turnover", m.Turnover },
            { "fees_paid", m.FeesPaid },
            { "fees_pct", m.FeesPct },
            { "exposure", m.Exposure },
            { "steps", m.Steps },
            { "initial_equity", m.InitialEquity },
            { "final_equity", m.FinalEquity },
        };

        public static string Number(double value) =>
            double.IsNaN(value) || double.IsInfinity(value) ? "" : value.ToString("R", Inv);

        static string Csv(string cell)
        {
            if (cell.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return cell;
            return "\"" + cell.Replace("\"", "\"\"") + "\"";
        }

        static string Safe(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in name ?? "") sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
            return sb.Length > 0 ? sb.ToString() : "unnamed";
        }

        static void Write(Stream stream, string text)
        {
            byte[] bytes = Utf8.GetBytes(text);
            stream.Write(bytes, 0, bytes.Length);
        }

        static string ReadFirstLine(string path)
        {
            using (var reader = new StreamReader(path, Utf8, true))
                return (reader.ReadLine() ?? "").TrimStart('﻿');
        }

        static bool EndsWithNewline(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (stream.Length == 0) return true;
                stream.Seek(-1, SeekOrigin.End);
                return stream.ReadByte() == '\n';
            }
        }

        // ---------------------------------------------------------------- minimal JSON writer

        public static string Json(object value)
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

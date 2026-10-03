using System;
using System.Collections.Generic;
using System.Globalization;

namespace Gym.Core.Market
{
    /// <summary>
    /// A contiguous run of hourly candles: every candle starts on the hour and
    /// the next one starts exactly one hour later. Built by <see cref="Parse"/>
    /// from the CSV written by scripts/data/fetch_binance_klines.py.
    /// </summary>
    public sealed class CandleSeries
    {
        public const long HourMs = 3_600_000;
        public const string Header = "open_time_ms,open,high,low,close,volume";

        readonly Candle[] candles;
        readonly double[] open;
        readonly double[] close;

        CandleSeries(Candle[] candles)
        {
            this.candles = candles;
            open = new double[candles.Length];
            close = new double[candles.Length];
            for (int i = 0; i < candles.Length; i++)
            {
                open[i] = candles[i].Open;
                close[i] = candles[i].Close;
            }
        }

        public int Count => candles.Length;
        public Candle this[int index] => candles[index];
        public long FirstOpenTimeMs => candles[0].OpenTimeMs;
        public long LastOpenTimeMs => candles[candles.Length - 1].OpenTimeMs;

        public double OpenAt(int index) => open[index];
        public double CloseAt(int index) => close[index];

        public static DateTime ToUtc(long openTimeMs) =>
            DateTimeOffset.FromUnixTimeMilliseconds(openTimeMs).UtcDateTime;

        public static long ToMs(DateTime utc) =>
            new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

        public DateTime OpenTimeUtc(int index) => ToUtc(candles[index].OpenTimeMs);

        /// <summary>Index of the candle that opens at <paramref name="openTimeMs"/>, or -1.</summary>
        public int IndexOfTime(long openTimeMs)
        {
            long offset = openTimeMs - FirstOpenTimeMs;
            if (offset < 0 || offset % HourMs != 0) return -1;
            long index = offset / HourMs;
            return index < Count ? (int)index : -1;
        }

        /// <summary>First candle at or after 00:00 UTC of <paramref name="utcDate"/>, or -1 if none.</summary>
        public int FirstIndexOnOrAfter(DateTime utcDate)
        {
            long target = ToMs(utcDate.Date);
            if (target <= FirstOpenTimeMs) return 0;
            long index = (target - FirstOpenTimeMs + HourMs - 1) / HourMs;
            return index < Count ? (int)index : -1;
        }

        /// <summary>Last candle at or before 23:00 UTC of <paramref name="utcDate"/>, or -1 if none.</summary>
        public int LastIndexOnOrBefore(DateTime utcDate)
        {
            long target = ToMs(utcDate.Date.AddHours(23));
            if (target < FirstOpenTimeMs) return -1;
            long index = (target - FirstOpenTimeMs) / HourMs;
            return index < Count ? (int)index : Count - 1;
        }

        public static CandleSeries FromCandles(IReadOnlyList<Candle> source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var copy = new Candle[source.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = source[i];
            Validate(copy);
            return new CandleSeries(copy);
        }

        public static CandleSeries Parse(string csvText)
        {
            if (csvText == null) throw new ArgumentNullException(nameof(csvText));
            string[] lines = csvText.Split('\n');
            string header = lines[0].TrimEnd('\r').TrimStart('﻿');
            if (header != Header)
                throw new FormatException($"Unexpected CSV header '{header}', expected '{Header}'.");

            var rows = new List<Candle>(lines.Length);
            for (int lineNo = 1; lineNo < lines.Length; lineNo++)
            {
                string line = lines[lineNo].TrimEnd('\r');
                if (line.Length == 0) continue;
                string[] cells = line.Split(',');
                if (cells.Length != 6)
                    throw new FormatException($"Line {lineNo + 1}: expected 6 columns, got {cells.Length}.");
                try
                {
                    rows.Add(new Candle(
                        long.Parse(cells[0], NumberStyles.Integer, CultureInfo.InvariantCulture),
                        ParseNumber(cells[1]),
                        ParseNumber(cells[2]),
                        ParseNumber(cells[3]),
                        ParseNumber(cells[4]),
                        ParseNumber(cells[5])));
                }
                catch (Exception e) when (e is FormatException || e is OverflowException)
                {
                    throw new FormatException($"Line {lineNo + 1}: {e.Message}", e);
                }
            }

            Candle[] candles = rows.ToArray();
            Validate(candles);
            return new CandleSeries(candles);
        }

        static double ParseNumber(string text) =>
            double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

        static void Validate(Candle[] candles)
        {
            if (candles.Length == 0) throw new FormatException("The series contains no candles.");
            for (int i = 0; i < candles.Length; i++)
            {
                Candle c = candles[i];
                if (c.OpenTimeMs % HourMs != 0)
                    throw new FormatException($"Candle {i} at {c.OpenTimeMs} does not start on the hour.");
                if (!(c.Open > 0 && c.High > 0 && c.Low > 0 && c.Close > 0) ||
                    double.IsInfinity(c.Open) || double.IsInfinity(c.High) ||
                    double.IsInfinity(c.Low) || double.IsInfinity(c.Close) ||
                    !(c.Volume >= 0) || double.IsInfinity(c.Volume))
                    throw new FormatException($"Candle {i} at {c.OpenTimeMs} has a non-positive or invalid price.");
                if (i == 0) continue;
                long step = c.OpenTimeMs - candles[i - 1].OpenTimeMs;
                if (step != HourMs)
                {
                    string what = step == 0 ? "duplicates" : step < 0 ? "goes back from" : "is not one hour after";
                    throw new FormatException(
                        $"Candle {i} at {c.OpenTimeMs} {what} the previous candle at {candles[i - 1].OpenTimeMs}.");
                }
            }
        }
    }
}

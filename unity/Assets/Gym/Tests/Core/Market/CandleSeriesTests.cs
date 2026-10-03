using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using Gym.Core.Market;
using NUnit.Framework;

namespace Gym.Tests.Core.Market
{
    public class CandleSeriesTests
    {
        const long H = CandleSeries.HourMs;
        const long T0 = 1502942400000; // 2017-08-17T04:00:00Z

        static string Row(long t, string close = "4308.83000000") =>
            $"{t},4261.48000000,4313.62000000,4261.32000000,{close},47.18100900";

        // ---- T-1 CSV parsing

        [Test]
        public void T01_ParsesHeaderAndRows()
        {
            CandleSeries s = CandleSeries.Parse(TestData.CsvOf(new[] { Row(T0), Row(T0 + H, "4315.32") }));
            Assert.AreEqual(2, s.Count);
            Assert.AreEqual(T0, s[0].OpenTimeMs);
            Assert.AreEqual(4261.48, s[0].Open);
            Assert.AreEqual(4313.62, s[0].High);
            Assert.AreEqual(4261.32, s[0].Low);
            Assert.AreEqual(4308.83, s[0].Close);
            Assert.AreEqual(47.181009, s[0].Volume);
            Assert.AreEqual(4315.32, s.CloseAt(1));
            Assert.AreEqual(new DateTime(2017, 8, 17, 4, 0, 0, DateTimeKind.Utc), s.OpenTimeUtc(0));
        }

        [Test]
        public void T01_ParsesTheSameUnderGermanCulture()
        {
            CultureInfo saved = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                Assert.AreEqual("1,5", 1.5.ToString(), "culture switch did not take effect");
                CandleSeries s = CandleSeries.Parse(TestData.CsvOf(new[] { Row(T0), Row(T0 + H, "4315.32") }));
                Assert.AreEqual(4261.48, s[0].Open);
                Assert.AreEqual(4315.32, s.CloseAt(1));
                Assert.AreEqual(47.181009, s[0].Volume);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = saved;
            }
        }

        [Test]
        public void T01_AcceptsCrlfAndBom()
        {
            string text = "﻿" + TestData.CsvOf(new[] { Row(T0), Row(T0 + H) }).Replace("\n", "\r\n");
            Assert.AreEqual(2, CandleSeries.Parse(text).Count);
        }

        [Test]
        public void T01_RejectsOutOfOrderRows() =>
            Assert.Throws<FormatException>(() => CandleSeries.Parse(TestData.CsvOf(new[] { Row(T0 + H), Row(T0) })));

        [Test]
        public void T01_RejectsDuplicateRows() =>
            Assert.Throws<FormatException>(() => CandleSeries.Parse(TestData.CsvOf(new[] { Row(T0), Row(T0) })));

        [Test]
        public void T01_RejectsAMissingHour() =>
            Assert.Throws<FormatException>(() => CandleSeries.Parse(TestData.CsvOf(new[] { Row(T0), Row(T0 + 2 * H) })));

        [Test]
        public void T01_RejectsABadHeader()
        {
            Assert.Throws<FormatException>(() => CandleSeries.Parse("time,open,high,low,close,volume\n" + Row(T0) + "\n"));
            Assert.Throws<FormatException>(() => CandleSeries.Parse(Row(T0) + "\n"));
        }

        [Test]
        public void T01_RejectsMalformedRows()
        {
            Assert.Throws<FormatException>(() => CandleSeries.Parse(TestData.CsvOf(new[] { $"{T0},1,1,1,1" })));
            Assert.Throws<FormatException>(() => CandleSeries.Parse(TestData.CsvOf(new[] { $"{T0},1,1,1,abc,1" })));
            Assert.Throws<FormatException>(() => CandleSeries.Parse(TestData.CsvOf(new[] { $"{T0 + 60000},1,1,1,1,1" })));
            Assert.Throws<FormatException>(() => CandleSeries.Parse(CandleSeries.Header + "\n"));
        }

        [Test]
        public void T01_FindsIndicesByUtcDate()
        {
            CandleSeries s = TestData.Synthetic(72, i => 100); // 2024-01-01 00:00 .. 2024-01-03 23:00
            Assert.AreEqual(24, s.FirstIndexOnOrAfter(new DateTime(2024, 1, 2)));
            Assert.AreEqual(47, s.LastIndexOnOrBefore(new DateTime(2024, 1, 2)));
            Assert.AreEqual(0, s.FirstIndexOnOrAfter(new DateTime(2023, 12, 1)));
            Assert.AreEqual(-1, s.FirstIndexOnOrAfter(new DateTime(2024, 1, 4)));
            Assert.AreEqual(71, s.LastIndexOnOrBefore(new DateTime(2024, 2, 1)));
            Assert.AreEqual(-1, s.LastIndexOnOrBefore(new DateTime(2023, 12, 31)));
            Assert.AreEqual(25, s.IndexOfTime(TestData.SyntheticStartMs + 25 * H));
            Assert.AreEqual(-1, s.IndexOfTime(TestData.SyntheticStartMs + 72 * H));
        }

        // ---- T-2 the committed BTCUSDT data

        [Test]
        public void T02_RealDataMatchesTheManifest()
        {
            CandleSeries s = TestData.Btc;
            Assert.AreEqual(TestData.ManifestLong("rows"), s.Count);
            Assert.AreEqual(TestData.ManifestLong("first_open_time_ms"), s.FirstOpenTimeMs);
            Assert.AreEqual(TestData.ManifestLong("last_open_time_ms"), s.LastOpenTimeMs);
            Assert.AreEqual(new DateTime(2017, 8, 17, 4, 0, 0, DateTimeKind.Utc), s.OpenTimeUtc(0));
            Assert.AreEqual(new DateTime(2026, 8, 31, 23, 0, 0, DateTimeKind.Utc), s.OpenTimeUtc(s.Count - 1));
        }

        [Test]
        public void T02_RealDataChecksumMatchesTheManifest()
        {
            byte[] bytes = File.ReadAllBytes(TestData.BtcCsvPath);
            string hex;
            using (SHA256 sha = SHA256.Create())
                hex = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
            Assert.AreEqual(TestData.ManifestString("csv_sha256"), hex);
        }

        [Test]
        public void T02_MillisecondAndMicrosecondYearsMeetWithoutAGap()
        {
            CandleSeries s = TestData.Btc;
            long lastOf2024 = CandleSeries.ToMs(new DateTime(2024, 12, 31, 23, 0, 0, DateTimeKind.Utc));
            int i = s.IndexOfTime(lastOf2024);
            Assert.GreaterOrEqual(i, 0);
            Assert.AreEqual(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), s.OpenTimeUtc(i + 1));
            Assert.AreEqual(CandleSeries.HourMs, s[i + 1].OpenTimeMs - s[i].OpenTimeMs);
        }
    }
}

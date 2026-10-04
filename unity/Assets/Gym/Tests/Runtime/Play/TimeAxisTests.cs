using System;
using System.Collections.Generic;
using System.Linq;
using Gym.Runtime.Play;
using NUnit.Framework;

namespace Gym.Tests.Runtime.Play
{
    public class TimeAxisTests
    {
        static List<DateTime> Hours(DateTime first, int count) =>
            Enumerable.Range(0, count).Select(i => first.AddHours(i)).ToList();

        [Test]
        public void EveryUtcMidnight_GetsAMonthDayLabelUnderItsCandle()
        {
            List<DateTime> times = Hours(new DateTime(2024, 1, 1, 13, 0, 0, DateTimeKind.Utc), 64);
            List<TimeLabel> labels = TimeAxis.Pick(times);
            CollectionAssert.AreEqual(new[] { 11, 35, 59 }, labels.Select(l => l.Offset));
            CollectionAssert.AreEqual(new[] { "01-02", "01-03", "01-04" }, labels.Select(l => l.Text));
        }

        [Test]
        public void NoMidnightInView_StillLabelsTheOldestCandleWithDateAndTime()
        {
            List<DateTime> times = Hours(new DateTime(2024, 3, 9, 1, 0, 0, DateTimeKind.Utc), 20);
            List<TimeLabel> labels = TimeAxis.Pick(times);
            Assert.AreEqual(1, labels.Count);
            Assert.AreEqual(0, labels[0].Offset);
            Assert.AreEqual("03-09 01:00", labels[0].Text);
        }

        [Test]
        public void NoCandles_NoLabels()
        {
            Assert.AreEqual(0, TimeAxis.Pick(new List<DateTime>()).Count);
        }
    }
}

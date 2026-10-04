using Gym.Runtime.Play;
using NUnit.Framework;
using UnityEngine;

namespace Gym.Tests.Runtime.Play
{
    public class PlayLayoutTests
    {
        static readonly (string name, Rect area)[] Panels =
        {
            ("language button", PlayLayout.LanguageButton),
            ("readout", PlayLayout.Readout),
            ("wallet", PlayLayout.Wallet),
            ("chart", PlayLayout.Chart),
            ("price labels", PlayLayout.PriceLabels),
            ("time labels", PlayLayout.TimeLabels),
        };

        [Test]
        public void OnASixteenByNineScreen_EveryAreaIsVisible()
        {
            Assert.AreEqual(16f / 9f, PlayLayout.Screen.width / PlayLayout.Screen.height, 0.01f);
            foreach ((string name, Rect area) in Panels)
            {
                Assert.GreaterOrEqual(area.xMin, PlayLayout.Screen.xMin, name);
                Assert.LessOrEqual(area.xMax, PlayLayout.Screen.xMax, name);
                Assert.GreaterOrEqual(area.yMin, PlayLayout.Screen.yMin, name);
                Assert.LessOrEqual(area.yMax, PlayLayout.Screen.yMax, name);
            }
        }

        [Test]
        public void NoTwoAreas_Overlap()
        {
            for (int i = 0; i < Panels.Length; i++)
            for (int j = i + 1; j < Panels.Length; j++)
                Assert.IsFalse(Panels[i].area.Overlaps(Panels[j].area), $"{Panels[i].name} overlaps {Panels[j].name}");
        }

        [Test]
        public void StripAboveTheChart_IsLeftFreeForTheAvatar()
        {
            foreach ((string name, Rect area) in Panels)
                Assert.IsFalse(PlayLayout.AboveChart.Overlaps(area), name);
            Assert.GreaterOrEqual(PlayLayout.AboveChart.yMin, PlayLayout.Chart.yMax);
            Assert.GreaterOrEqual(PlayLayout.AboveChart.xMax, PlayLayout.Chart.xMax, "it covers the newest candle on the right");
        }
    }
}

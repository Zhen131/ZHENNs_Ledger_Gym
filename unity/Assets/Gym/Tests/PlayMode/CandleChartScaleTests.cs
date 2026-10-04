using System;
using System.Collections;
using System.Linq;
using Gym.Runtime.Play;
using NUnit.Framework;
using Unity.MLAgents;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Gym.Tests.PlayMode
{
    public class CandleChartScaleTests
    {
        const string PlayScenePath = "Assets/Gym/Scenes/Play.unity";

        [TearDown]
        public void TearDown()
        {
            if (Academy.IsInitialized) Academy.Instance.AutomaticSteppingEnabled = true;
        }

        static string[] ActiveTexts(Component owner, string namePrefix) =>
            owner.GetComponentsInChildren<TextMesh>(false).Where(t => t.name.StartsWith(namePrefix)).Select(t => t.text).ToArray();

        [UnityTest]
        public IEnumerator AfterSomeSteps_TheChartDrawsThePriceLinesAndMidnightLabelsThePureFunctionsGive()
        {
            using var logs = new LogGuard();
            yield return SceneManager.LoadSceneAsync(PlayScenePath, LoadSceneMode.Single);
            yield return null;
            var controller = Object.FindFirstObjectByType<PlayController>();
            var chart = Object.FindFirstObjectByType<CandleChartView>();
            Assert.AreEqual(CandleChartView.DefaultPriceLines, chart.DesiredPriceLines);

            for (int round = 0; round < 3; round++)
            {
                for (int i = 0; i < 25; i++) controller.PressHold();
                if (round == 1) controller.PressBuy(0.25f);
                string when = $"after round {round}";

                PriceTicks expected = PriceScale.Compute(chart.VisibleLow, chart.VisibleHigh, chart.DesiredPriceLines);
                Assert.AreEqual(expected.Step, chart.DrawnPriceTicks.Step, when);
                CollectionAssert.AreEqual(expected.Values, chart.DrawnPriceTicks.Values, when);
                Assert.Greater(chart.DrawnPriceLabels.Count, 0, when);
                CollectionAssert.AreEqual(expected.Values.Select(v => PriceScale.Label(v, expected.Step)), chart.DrawnPriceLabels, when);
                foreach (string label in chart.DrawnPriceLabels) StringAssert.Contains(",", label, $"{when}: thousands separator");
                CollectionAssert.AreEqual(chart.DrawnPriceLabels, ActiveTexts(chart, "Price label"), $"{when}: what is on screen");

                var agent = controller.Agent;
                int midnights = 0;
                for (int i = chart.FirstVisibleIndex; i <= agent.Env.CurrentIndex; i++)
                {
                    DateTime t = agent.Env.Series.OpenTimeUtc(i);
                    if (t.TimeOfDay != TimeSpan.Zero) continue;
                    midnights++;
                    Assert.IsTrue(chart.DrawnTimeLabels.Any(l => l.Offset == i - chart.FirstVisibleIndex && l.Text == t.ToString("MM-dd")),
                        $"{when}: a label under {t:yyyy-MM-dd HH:mm}");
                }
                Assert.Greater(midnights, 0, $"{when}: premise: a midnight is in view");
                Assert.AreEqual(midnights, chart.DrawnTimeLabels.Count, when);
                CollectionAssert.AreEqual(chart.DrawnTimeLabels.Select(l => l.Text), ActiveTexts(chart, "Time label"), $"{when}: what is on screen");
                Assert.AreEqual(CandleChartView.VisibleCandles, chart.DrawnCandles, when);
            }
            logs.AssertNoErrors();
        }
    }
}

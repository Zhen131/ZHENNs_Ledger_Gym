using System;
using System.IO;
using System.Linq;
using Gym.Editor;
using Gym.Tests.Runtime.Evaluation;
using NUnit.Framework;

namespace Gym.Tests.Editor
{
    /// <summary>
    /// 两个对手进基准工具（24 批）：-gymPolicies constmix,dialmom 才跑，不给时仍是那六个（锁在 EvalToolsTests）；
    /// 流水的 notes 写 mode=dial; band=0.05; hours=24; changes=&lt;k&gt;，明细有和 agent 同名的动作键。
    /// </summary>
    public class EvalToolsOpponentTests
    {
        string tempDir;

        [SetUp]
        public void SetUp()
        {
            tempDir = Path.Combine(Path.GetTempPath(), "gym-eval-opponents-" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }

        [Test]
        public void ThePoliciesArgument_KnowsTheTwoOpponentsButDoesNotRunThemByDefault()
        {
            CollectionAssert.AreEquivalent(new[] { "constmix", "dialmom" }, EvalTools.ParsePolicies("constmix, DialMom"));
            CollectionAssert.AreEquivalent(new[] { "constmix", "buyhold" }, EvalTools.ParsePolicies("buyhold,constmix"));
            CollectionAssert.DoesNotContain(EvalTools.ParsePolicies(null), "constmix");
            CollectionAssert.DoesNotContain(EvalTools.ParsePolicies(null), "dialmom");
            var e = Assert.Throws<ArgumentException>(() => EvalTools.ParsePolicies("constmix,mix50"));
            StringAssert.Contains("constmix, dialmom", e.Message, "the error lists the opponents' names too");
        }

        /// <summary>
        /// BTC 2022（validation-bear）、费率 0.001：两行、两份明细；数和 23 批 OpponentsTests 的日志逐位相同
        /// （同一个段首、全现金开局；23C 第四节）。
        /// </summary>
        [Test]
        public void TheTwoOpponents_AreLoggedWithTheirModeBandHoursAndChanges()
        {
            EvalTools.Run(new[]
            {
                "x", "-gymSegment", "validation-bear", "-gymPolicies", "constmix,dialmom", "-gymFeeRates", "0.001", "-gymOut", tempDir,
            });
            string[] lines = File.ReadAllLines(Path.Combine(tempDir, "log.csv"));
            Assert.AreEqual(3, lines.Length, "header and two rows");
            string[] header = lines[0].Split(',');
            string Cell(int row, string column) => lines[row].Split(',')[Array.IndexOf(header, column)];

            Assert.AreEqual("constant_mix_50", Cell(1, "policy"));
            Assert.AreEqual("baseline", Cell(1, "kind"));
            Assert.AreEqual("BTCUSDT", Cell(1, "symbol"));
            Assert.AreEqual("validation-bear", Cell(1, "segment"));
            Assert.AreEqual("0.001", Cell(1, "fee_rate"));
            Assert.AreEqual("-0.38254941994604996", Cell(1, "total_return"));
            Assert.AreEqual("0.41320517404320689", Cell(1, "max_drawdown"));
            Assert.AreEqual("8", Cell(1, "trades"));
            Assert.AreEqual("mode=dial; band=0.05; hours=24; changes=0", Cell(1, "notes"));

            Assert.AreEqual("dial_momentum_90d", Cell(2, "policy"));
            Assert.AreEqual("-0.3652341752949102", Cell(2, "total_return"));
            Assert.AreEqual("0.38829848791970212", Cell(2, "max_drawdown"));
            Assert.AreEqual("19", Cell(2, "trades"));
            Assert.AreEqual("mode=dial; band=0.05; hours=24; changes=14", Cell(2, "notes"));

            string[] details = Directory.GetFiles(Path.Combine(tempDir, "runs"), "*.json");
            Assert.AreEqual(2, details.Length);
            string mix = File.ReadAllText(details.Single(f => f.Contains("-constant_mix_50-validation-bear")));
            string mom = File.ReadAllText(details.Single(f => f.Contains("-dial_momentum_90d-validation-bear")));
            foreach (string text in new[] { mix, mom })
            {
                StringAssert.Contains("\"action_mode\": \"dial\"", text);
                StringAssert.Contains("\"decision_hours\": 24", text);
                StringAssert.Contains("\"rebalance_band\": 0.05", text);
                StringAssert.Contains("\"decisions\": 365", text);
                StringAssert.Contains("\"last_decision_hours\": 23", text);
                StringAssert.Contains("\"data_file\": \"data/BTCUSDT-1h.csv\"", text);
                RecordFileAssert.NoMachinePath(text, "opponent details");
            }
            StringAssert.Contains("\"dial_changes\": 0", mix);
            StringAssert.DoesNotContain("\"lookback\"", mix);
            StringAssert.Contains("\"dial_changes\": 14", mom);
            StringAssert.Contains("\"lookback\": 2160", mom);
        }
    }
}

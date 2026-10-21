using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Gym.Core.Env;
using Gym.Runtime.Configuration;
using NUnit.Framework;
using UnityEngine;

namespace Gym.Tests.Runtime.Configuration
{
    /// <summary>旋钮模式的配置字段和命令行开关（23 批）。GymConfigLoaderTests 本批不改。</summary>
    public class GymConfigLoaderDialTests
    {
        string tempDir;

        [SetUp]
        public void SetUp()
        {
            tempDir = Path.Combine(Path.GetTempPath(), "gym-config-dial-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }

        static GymConfig DefaultConfig() =>
            JsonUtility.FromJson<GymConfig>(File.ReadAllText(GymConfigLoader.DefaultConfigPath));

        string WriteConfig(GymConfig config)
        {
            config.dataFile = Path.Combine(GymConfigLoader.DefaultDirectory, "data", "BTCUSDT-1h.csv");
            string path = Path.Combine(tempDir, "gym-config.json");
            File.WriteAllText(path, JsonUtility.ToJson(config, true));
            return path;
        }

        static GymSettings LoadDefault(params string[] args) =>
            GymConfigLoader.Load(GymConfigLoader.DefaultConfigPath, GymConfigLoader.DefaultSymbolsPath, args);

        GymConfigException Refused(GymConfig config, params string[] args) =>
            Assert.Throws<GymConfigException>(() => GymConfigLoader.Load(WriteConfig(config), GymConfigLoader.DefaultSymbolsPath, args));

        [Test]
        public void TheDefaultConfig_IsInOrderMode_WithTheRoundThreeDialValues()
        {
            GymSettings s = LoadDefault();
            Assert.AreEqual("orders", s.Config.actionMode, "gym-config.json says orders");
            Assert.AreEqual(ActionMode.Orders, s.ActionMode);
            Assert.AreEqual(24, s.DecisionHours);
            Assert.AreEqual(0.05, s.RebalanceBand);
            CollectionAssert.AreEqual(new[] { 0.3, 0.5, 0.7 }, s.DialLevels.ToArray());
            Assert.AreEqual(2160, s.DialEpisodeLength);
            Assert.AreEqual(720, s.EpisodeLengthFor(ActionMode.Orders));
            Assert.AreEqual(2160, s.EpisodeLengthFor(ActionMode.Dial));
            Assert.IsEmpty(s.Warnings);
        }

        [Test]
        public void AConfigWithoutTheNewFields_GetsTheDefaults()
        {
            // 第一、二轮的配置没有这五个字段：缺省是 orders、24、0.05、0.3/0.5/0.7、2160
            string text = File.ReadAllText(GymConfigLoader.DefaultConfigPath);
            string old = Regex.Replace(text, @",\s*""(actionMode|decisionHours|rebalanceBand|dialLevels|dialEpisodeLength)""\s*:\s*(""[^""]*""|\[[^\]]*\]|[0-9.]+)", "");
            Assert.IsFalse(old.Contains("actionMode") || old.Contains("dialLevels") || old.Contains("dialEpisodeLength"), "premise: the fields are gone");
            var config = JsonUtility.FromJson<GymConfig>(old);
            Assert.AreEqual("orders", config.actionMode);
            Assert.AreEqual(24, config.decisionHours);
            Assert.AreEqual(0.05, config.rebalanceBand);
            CollectionAssert.AreEqual(new[] { 0.3, 0.5, 0.7 }, config.dialLevels);
            Assert.AreEqual(2160, config.dialEpisodeLength);

            // 相对路径的数据文件找不到配置旁边的，就找 StreamingAssets/Gym
            string configPath = Path.Combine(tempDir, "gym-config.json");
            File.WriteAllText(configPath, old);
            GymSettings s = GymConfigLoader.Load(configPath, GymConfigLoader.DefaultSymbolsPath);
            Assert.AreEqual(ActionMode.Orders, s.ActionMode);
            Assert.AreEqual(24, s.DecisionHours);
            Assert.AreEqual(0.05, s.RebalanceBand);
            CollectionAssert.AreEqual(new[] { 0.3, 0.5, 0.7 }, s.DialLevels.ToArray());
            Assert.AreEqual(2160, s.DialEpisodeLength);
        }

        [Test]
        public void TheCommandLine_OverridesTheConfig()
        {
            GymSettings s = LoadDefault("x", "-gymActionMode", "dial", "-gymDecisionHours", "12", "-gymBand", "0.03");
            Assert.AreEqual(ActionMode.Dial, s.ActionMode);
            Assert.AreEqual(12, s.DecisionHours);
            Assert.AreEqual(0.03, s.RebalanceBand);
            Assert.AreEqual(ActionMode.Dial, LoadDefault("x", "-gymActionMode", "DIAL").ActionMode, "names are not case sensitive");
            Assert.AreEqual(ActionMode.Orders, LoadDefault("x", "-gymActionMode", "orders").ActionMode);

            GymConfig config = DefaultConfig();
            config.actionMode = "dial";
            config.decisionHours = 6;
            config.rebalanceBand = 0.08;
            GymSettings fromConfig = GymConfigLoader.Load(WriteConfig(config), GymConfigLoader.DefaultSymbolsPath);
            Assert.AreEqual(ActionMode.Dial, fromConfig.ActionMode);
            Assert.AreEqual(6, fromConfig.DecisionHours);
            Assert.AreEqual(0.08, fromConfig.RebalanceBand);
            GymSettings back = GymConfigLoader.Load(WriteConfig(config), GymConfigLoader.DefaultSymbolsPath,
                new[] { "x", "-gymActionMode", "orders", "-gymDecisionHours", "24", "-gymBand", "0.05" });
            Assert.AreEqual(ActionMode.Orders, back.ActionMode, "the command line beats the config");
            Assert.AreEqual(24, back.DecisionHours);
            Assert.AreEqual(0.05, back.RebalanceBand);
        }

        [Test]
        public void BadDialValuesInTheConfig_AreRefused()
        {
            GymConfig config = DefaultConfig();
            config.dialLevels = new[] { 0.5, 0.3, 0.7 };
            StringAssert.Contains("dialLevels: dial levels must be strictly increasing", Refused(config).Message);

            config = DefaultConfig();
            config.dialLevels = new[] { 0.5 };
            StringAssert.Contains("dialLevels: dial levels need at least two values", Refused(config).Message);

            config = DefaultConfig();
            config.dialLevels = new[] { 0.3, 1.2 };
            StringAssert.Contains("must be inside (0, 1)", Refused(config).Message);

            config = DefaultConfig();
            config.rebalanceBand = 0.6;
            StringAssert.Contains("rebalanceBand must be inside (0, 0.5) (got 0.6)", Refused(config).Message);

            config = DefaultConfig();
            config.decisionHours = 0;
            StringAssert.Contains("decisionHours must be a whole number >= 1 (got 0)", Refused(config).Message);

            config = DefaultConfig();
            config.dialEpisodeLength = 100;
            StringAssert.Contains("dialEpisodeLength must be in [720, 2160] (got 100)", Refused(config).Message);
            config.dialEpisodeLength = 2161;
            StringAssert.Contains("dialEpisodeLength must be in [720, 2160] (got 2161)", Refused(config).Message);

            config = DefaultConfig();
            config.actionMode = "dail";
            StringAssert.Contains("actionMode 'dail' is not an action mode; use orders or dial", Refused(config).Message);
            // 配置里的名字不管有没有被命令行盖过都要对
            StringAssert.Contains("actionMode 'dail'", Refused(config, "x", "-gymActionMode", "dial").Message);
        }

        [Test]
        public void BadDialSwitches_AreRefused()
        {
            GymConfig config = DefaultConfig();
            StringAssert.Contains("-gymActionMode dail is not an action mode; use orders or dial", Refused(config, "x", "-gymActionMode", "dail").Message);
            StringAssert.Contains("-gymDecisionHours 0 is out of range", Refused(config, "x", "-gymDecisionHours", "0").Message);
            StringAssert.Contains("-gymDecisionHours 2.5 is out of range", Refused(config, "x", "-gymDecisionHours", "2.5").Message);
            StringAssert.Contains("-gymBand 0.6 is out of range: must be inside (0, 0.5)", Refused(config, "x", "-gymBand", "0.6").Message);
            StringAssert.Contains("-gymBand 'abc' is not a number", Refused(config, "x", "-gymBand", "abc").Message);
        }

        [Test]
        public void InDialMode_TheSplitsAreCheckedWithTheDialEpisodeLength()
        {
            // 训练段 2017-08-17～2017-10-31：base 从 32 起够一个 720 根的局，不够一个 2160 根的局
            GymConfig config = DefaultConfig();
            config.symbols = new SymbolDataEntry[0]; // 只看主币；晚上市的币的数据里没有这里的训练段
            config.train = new DateRange { start = "2017-08-17", end = "2017-10-31" };
            config.playStart = "2017-10-01";
            string path = WriteConfig(config);
            GymSettings s = GymConfigLoader.Load(path, GymConfigLoader.DefaultSymbolsPath);
            Assert.AreEqual(ActionMode.Orders, s.ActionMode);
            CollectionAssert.IsEmpty(GymConfigLoader.ProblemsWith(s, ObservationSet.Base, ActionMode.Orders));

            // 训练器的 action_mode 换成旋钮时，Agent 拿到的就是这一条（不改已经载入的设置）
            var problems = GymConfigLoader.ProblemsWith(s, ObservationSet.Base, ActionMode.Dial);
            Assert.AreEqual(1, problems.Count, string.Join("\n", problems));
            StringAssert.StartsWith("too short: train 2017-08-17..2017-10-31 has", problems[0]);
            StringAssert.EndsWith("decision bars, an episode needs 2161", problems[0]);
            Assert.AreEqual(ActionMode.Orders, s.ActionMode, "the loaded settings are unchanged");
            CollectionAssert.IsEmpty(GymConfigLoader.ProblemsWithObservationSet(s, ObservationSet.Base), "the old entry point keeps the loaded mode");

            // 命令行或配置直接选旋钮时，载入就报同一条
            var e = Assert.Throws<GymConfigException>(() =>
                GymConfigLoader.Load(path, GymConfigLoader.DefaultSymbolsPath, new[] { "x", "-gymActionMode", "dial" }));
            CollectionAssert.AreEqual(problems, e.Errors);
            config.actionMode = "dial";
            CollectionAssert.AreEqual(problems, Refused(config).Errors);
        }
    }
}

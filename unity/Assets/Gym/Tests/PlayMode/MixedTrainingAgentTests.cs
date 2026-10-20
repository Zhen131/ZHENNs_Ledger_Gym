#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Gym.Core.Accounting;
using Gym.Core.Env;
using Gym.Runtime.Agents;
using Gym.Runtime.Configuration;
using NUnit.Framework;
using Unity.MLAgents;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Gym.Tests.PlayMode
{
    /// <summary>
    /// 混训：训练模式的 Agent 给清单里每只币建一个环境，每局按本局的 EpisodeSeed 用 <see cref="SymbolPicker"/> 选一个。
    /// PlayMode 测试改不了命令行，所以两只币的清单用 <see cref="TradingAgent.TrainSymbolsOverride"/> 给；没有训练器时
    /// 主种子来自时钟，「同一个主种子跑两遍」做不出来，所以逐局核对：每局的币等于选币函数作用在这一局 EpisodeSeed
    /// 上的结果，起点和开局持币等于一个单独建的同币环境用同一个 seed 重置的结果；EpisodeSeed 序列等于
    /// new System.Random(MasterSeed) 连续 Next() 的序列（选币不从 seedSource 抽数）。
    /// </summary>
    public class MixedTrainingAgentTests
    {
        const string PrefabPath = "Assets/Gym/Prefabs/TradingAgent.prefab";
        const int Episodes = 48;

        GameObject holder;

        [TearDown]
        public void TearDown()
        {
            if (holder != null) Object.Destroy(holder);
            if (Academy.IsInitialized) Academy.Instance.AutomaticSteppingEnabled = true;
        }

        /// <summary>一局开始时记下的东西。</summary>
        struct Episode
        {
            public int Seed;
            public TradingEnv Env;
            public string Symbol;
            public int Start;
            public long Units;
            public double Cash;
        }

        /// <summary>在没启用的物体下放一个训练模式的预制体实例，记下它每一局的开头。</summary>
        TradingAgent MakeTrainingAgent(string name, IReadOnlyList<string> trainSymbols, ObservationSet? set, List<Episode> episodes)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, PrefabPath);
            holder = new GameObject(name + " test");
            holder.SetActive(false); // Agent 在启用时初始化：先设好覆盖再启用
            GameObject agentObject = Object.Instantiate(prefab, holder.transform);
            agentObject.name = name;
            var agent = agentObject.GetComponent<TradingAgent>();
            agent.StartMode = AgentStartMode.Training;
            agent.TrainSymbolsOverride = trainSymbols;
            agent.ObservationSetOverride = set;
            agent.EpisodeStarted += a => episodes.Add(new Episode
            {
                Seed = a.EpisodeSeed, Env = a.Env, Symbol = a.Env.Rules.Symbol, Start = a.Env.StartIndex,
                Units = a.Env.Account.CoinUnits, Cash = a.Env.Account.Cash,
            });
            return agent;
        }

        /// <summary>每局的 seed 依次是 new System.Random(MasterSeed) 的 Next()：选币没有多抽 seedSource。</summary>
        static void AssertSeedsComeStraightFromTheMasterSeed(TradingAgent agent, List<Episode> episodes)
        {
            var reference = new System.Random(agent.MasterSeed);
            for (int i = 0; i < episodes.Count; i++)
                Assert.AreEqual(reference.Next(), episodes[i].Seed, $"episode {i}: the seed is the next number of new System.Random(MasterSeed)");
        }

        /// <summary>同一只币、同一个 seed，单独建的环境重置出来的起点和开局账户与 Agent 这一局的逐位相同。</summary>
        static void AssertStartMatchesAStandaloneEnv(SymbolSettings symbol, GymSettings s, ObservationSet set, Episode episode, int i)
        {
            var standalone = TradingEnv.ForSegment(symbol.Series, symbol.Rules, symbol.Train, s.Config.initialCash,
                s.Config.episodeLength, s.Config.randomInitialPositionShare, set);
            standalone.ResetForTraining(episode.Seed, new CostModel(CostModel.DefaultFeeRate, 0, 0));
            Assert.AreEqual(standalone.First, episode.Env.First, $"episode {i}: {symbol.Symbol}'s clipped training segment");
            Assert.AreEqual(standalone.Last, episode.Env.Last, $"episode {i}");
            Assert.AreEqual(standalone.StartIndex, episode.Start, $"episode {i}: start candle");
            Assert.AreEqual(standalone.Account.CoinUnits, episode.Units, $"episode {i}: initial coin");
            Assert.IsTrue(TestDataBits.Same(standalone.Account.Cash, episode.Cash), $"episode {i}: initial cash");
        }

        [UnityTest]
        public IEnumerator TwoCoins_EachEpisodeUsesTheCoinThePickerChoosesForItsSeed_AndALongEpisodeRunsToTheEnd()
        {
            using var logs = new LogGuard();
            var episodes = new List<Episode>();
            string[] names = { "BTCUSDT", "ADAUSDT" };
            TradingAgent agent = MakeTrainingAgent("TradingAgent (mixed, long)", names, ObservationSet.Long, episodes);
            Academy.Instance.AutomaticSteppingEnabled = false;
            holder.SetActive(true);
            yield return null;

            GymSettings s = agent.Settings;
            Assert.AreEqual(AgentStartMode.Training, agent.EffectiveMode);
            CollectionAssert.AreEqual(new[] { "BTCUSDT" }, s.TrainSymbolNames, "premise: the test process trains on the main symbol only");
            Assert.AreEqual(2, agent.Envs.Count, "one environment per coin of the list");
            CollectionAssert.AreEqual(names, agent.Envs.Select(e => e.Rules.Symbol).ToArray(), "in the list's order");
            Assert.That(agent.Envs.Select(e => e.ObservationSet), Has.All.EqualTo(ObservationSet.Long));
            SymbolSettings[] symbols = names.Select(n => { s.TryGetSymbol(n, out SymbolSettings x); return x; }).ToArray();
            Assert.AreNotEqual(symbols[0].Train.ToString(), symbols[1].Train.ToString(), "premise: ADA's training segment is clipped");

            // 若干局：EndEpisode 当场开下一局
            Academy.Instance.EnvironmentStep();
            while (episodes.Count < Episodes) agent.EndEpisode();

            // 再走完整的一局（long 档，720 步），结束时记 Trading/SymbolShare/<币名>，接着自己开下一局
            int finished = agent.FinishedEpisodes;
            int steps = 0;
            while (agent.FinishedEpisodes == finished && steps < 800)
            {
                Academy.Instance.EnvironmentStep();
                steps++;
            }
            Assert.AreEqual(finished + 1, agent.FinishedEpisodes, $"a long episode finished within {steps} steps");
            Assert.AreEqual(720, agent.LastEpisodeStats.Steps, "a whole 720-step episode");
            Academy.Instance.EnvironmentStep(); // 换局后观测清空了，走一步再看
            Assert.AreEqual(41, agent.GetObservations().Count, "long: 41 observations in the next episode too");

            AssertSeedsComeStraightFromTheMasterSeed(agent, episodes);
            var perCoin = new int[names.Length];
            for (int i = 0; i < episodes.Count; i++)
            {
                int pick = SymbolPicker.Pick(episodes[i].Seed, names.Length);
                Assert.AreEqual(names[pick], episodes[i].Symbol, $"episode {i}: the coin is the picker's for seed {episodes[i].Seed}");
                Assert.AreSame(agent.Envs[pick], episodes[i].Env, $"episode {i}");
                AssertStartMatchesAStandaloneEnv(symbols[pick], s, ObservationSet.Long, episodes[i], i);
                perCoin[pick]++;
            }
            Assert.That(perCoin, Has.All.GreaterThan(0), "both coins came up");
            Assert.GreaterOrEqual(episodes.Count, Episodes + 1, "the episode after the long one started too");

            Debug.Log($"[Gym] mixed training test: {agent.name}, training symbols {string.Join(", ", names)}, " +
                      $"master seed {agent.MasterSeed}, {episodes.Count} episodes: BTCUSDT {perCoin[0]}, ADAUSDT {perCoin[1]}; " +
                      $"first seeds and coins {string.Join(", ", episodes.Take(6).Select(e => $"{e.Seed}->{e.Symbol}@{e.Start}"))}; " +
                      $"a long episode finished after {steps} environment steps ({agent.LastEpisodeStats.Steps} steps)");
            logs.AssertNoErrors();
        }

        [UnityTest]
        public IEnumerator OneCoin_KeepsTheEpisodeSeedsAndStartsOfBefore()
        {
            using var logs = new LogGuard();
            var episodes = new List<Episode>();
            TradingAgent agent = MakeTrainingAgent("TradingAgent (one coin)", null, null, episodes);
            Academy.Instance.AutomaticSteppingEnabled = false;
            holder.SetActive(true);
            yield return null;

            GymSettings s = agent.Settings;
            CollectionAssert.AreEqual(new[] { "BTCUSDT" }, s.TrainSymbolNames, "premise: no list, so the main symbol only");
            Assert.AreEqual(1, agent.Envs.Count, "one coin: one environment");
            Assert.AreSame(agent.Envs[0], agent.Env);
            Assert.AreSame(s.Series, agent.Env.Series, "the current symbol's candles");

            Academy.Instance.EnvironmentStep();
            while (episodes.Count < Episodes) agent.EndEpisode();

            AssertSeedsComeStraightFromTheMasterSeed(agent, episodes);
            for (int i = 0; i < episodes.Count; i++)
            {
                Assert.AreSame(agent.Envs[0], episodes[i].Env, $"episode {i}: the environment never changes");
                Assert.AreEqual("BTCUSDT", episodes[i].Symbol);
                AssertStartMatchesAStandaloneEnv(s.Current, s, ObservationSet.Base, episodes[i], i);
            }
            // 改动前的做法：同一个环境、seed 依次取 Next()，起点由 ResetForTraining 定
            var before = TradingEnv.ForSegment(s.Series, s.Rules, s.Train, s.Config.initialCash, s.Config.episodeLength,
                s.Config.randomInitialPositionShare);
            var reference = new System.Random(agent.MasterSeed);
            var starts = new List<int>();
            foreach (Episode episode in episodes)
            {
                before.ResetForTraining(reference.Next(), new CostModel(CostModel.DefaultFeeRate, 0, 0));
                starts.Add(before.StartIndex);
            }
            CollectionAssert.AreEqual(starts, episodes.Select(e => e.Start).ToArray(), "start candles as before");

            Debug.Log($"[Gym] one-coin training test: {agent.name}, master seed {agent.MasterSeed}, {episodes.Count} episodes on BTCUSDT; " +
                      $"first seeds and starts {string.Join(", ", episodes.Take(6).Select(e => $"{e.Seed}@{e.Start}"))}");
            logs.AssertNoErrors();
        }
    }

    /// <summary>逐位比较两个 double。</summary>
    static class TestDataBits
    {
        public static bool Same(double a, double b) => System.BitConverter.DoubleToInt64Bits(a) == System.BitConverter.DoubleToInt64Bits(b);
    }
}
#endif

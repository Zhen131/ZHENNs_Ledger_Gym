using System;
using Gym.Core.Env;
using NUnit.Framework;

namespace Gym.Tests.Core.Env
{
    /// <summary>观测集的名字、环境参数的取值，和「模型的输入维数对不对得上」这个不碰模型的比较。</summary>
    public class ObservationSetTests
    {
        [Test]
        public void Names_AreBaseLongAndLongTa_AndParseWithoutCase()
        {
            Assert.AreEqual("base", ObservationSet.Base.Name());
            Assert.AreEqual("long", ObservationSet.Long.Name());
            Assert.AreEqual("long-ta", ObservationSet.LongTa.Name());
            Assert.IsTrue(ObservationSets.TryParse("long", out ObservationSet set));
            Assert.AreEqual(ObservationSet.Long, set);
            Assert.IsTrue(ObservationSets.TryParse("LONG-TA", out set));
            Assert.AreEqual(ObservationSet.LongTa, set);
            Assert.IsTrue(ObservationSets.TryParse("Base", out set));
            Assert.AreEqual(ObservationSet.Base, set);
            foreach (string bad in new[] { "", null, "longta", "long_ta", "ta", "1", "base " })
                Assert.IsFalse(ObservationSets.TryParse(bad, out _), $"'{bad}'");
            Assert.AreEqual("base, long or long-ta", ObservationSets.NamesText);
        }

        [Test]
        public void TheEnvironmentParameter_IsZeroOneOrTwo()
        {
            Assert.AreEqual(0, (int)ObservationSet.Base);
            Assert.AreEqual(1, (int)ObservationSet.Long);
            Assert.AreEqual(2, (int)ObservationSet.LongTa);
            Assert.IsTrue(ObservationSets.TryFromParameter(0, out ObservationSet set));
            Assert.AreEqual(ObservationSet.Base, set);
            Assert.IsTrue(ObservationSets.TryFromParameter(1.0, out set));
            Assert.AreEqual(ObservationSet.Long, set);
            Assert.IsTrue(ObservationSets.TryFromParameter(2, out set));
            Assert.AreEqual(ObservationSet.LongTa, set);
            foreach (double bad in new[] { -1, 0.5, 1.5, 3, 2.0000001, double.NaN, double.PositiveInfinity })
                Assert.IsFalse(ObservationSets.TryFromParameter(bad, out _), bad.ToString());
        }

        [Test]
        public void AnUnknownSetValue_Throws()
        {
            var bad = (ObservationSet)7;
            Assert.Throws<ArgumentOutOfRangeException>(() => bad.Size());
            Assert.Throws<ArgumentOutOfRangeException>(() => bad.HistoryLength());
            Assert.Throws<ArgumentOutOfRangeException>(() => bad.Name());
        }

        // ---- 维数核对：纯函数，不要模型

        [Test]
        public void ModelInputCheck_PassesWhenTheSizesAgree()
        {
            Assert.IsNull(ObservationSets.ModelInputProblem(35, ObservationSet.Base));
            Assert.IsNull(ObservationSets.ModelInputProblem(41, ObservationSet.Long));
            Assert.IsNull(ObservationSets.ModelInputProblem(43, ObservationSet.LongTa));
        }

        [TestCase(35, ObservationSet.Long, "the model takes 35 observations but the observation set long has 41")]
        [TestCase(35, ObservationSet.LongTa, "the model takes 35 observations but the observation set long-ta has 43")]
        [TestCase(41, ObservationSet.Base, "the model takes 41 observations but the observation set base has 35")]
        [TestCase(43, ObservationSet.Long, "the model takes 43 observations but the observation set long has 41")]
        public void ModelInputCheck_NamesBothNumbersWhenTheyDiffer(int modelSize, ObservationSet set, string expected)
        {
            string problem = ObservationSets.ModelInputProblem(modelSize, set);
            Assert.IsNotNull(problem);
            StringAssert.StartsWith(expected, problem);
        }
    }
}

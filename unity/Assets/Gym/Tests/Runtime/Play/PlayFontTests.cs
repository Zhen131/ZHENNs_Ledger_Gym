using Gym.Runtime.Play;
using NUnit.Framework;

namespace Gym.Tests.Runtime.Play
{
    public class PlayFontTests
    {
        static readonly string[] Preferred = { "PingFang SC", "Hiragino Sans GB", "STHeiti" };

        [Test]
        public void SeveralPreferredFontsInstalled_TakesTheFirstInTheList()
        {
            Assert.AreEqual("Hiragino Sans GB",
                PlayFont.FindInstalled(Preferred, new[] { "Arial", "STHeiti", "Hiragino Sans GB", "Hiragino Sans GB Bold" }));
        }

        [Test]
        public void InstalledNameHasAStyleSuffix_StillCounts()
        {
            Assert.AreEqual("STHeiti Medium", PlayFont.FindInstalled(Preferred, new[] { "Arial", "STHeiti Medium" }));
            Assert.AreEqual("pingfang sc", PlayFont.FindInstalled(Preferred, new[] { "pingfang sc" }));
        }

        [Test]
        public void OnlyAPrefixWithoutASpace_DoesNotCount()
        {
            Assert.IsNull(PlayFont.FindInstalled(Preferred, new[] { "STHeitiX", "PingFang SCX" }));
        }

        [Test]
        public void NoneInstalled_ReturnsNull()
        {
            Assert.IsNull(PlayFont.FindInstalled(Preferred, new[] { "Arial", "Helvetica" }));
            Assert.IsNull(PlayFont.FindInstalled(Preferred, new string[0]));
            Assert.IsNull(PlayFont.FindInstalled(Preferred, null));
        }

        [Test]
        public void PreferredList_CoversTheCommonChineseSystemFontsAndStartsWithPingFang()
        {
            CollectionAssert.IsSubsetOf(new[] { "PingFang SC", "Hiragino Sans GB", "STHeiti", "Songti SC", "Arial Unicode MS",
                "Microsoft YaHei", "SimHei", "Noto Sans CJK SC" }, PlayFont.PreferredNames);
            Assert.AreEqual("PingFang SC", PlayFont.PreferredNames[0]);
        }
    }
}

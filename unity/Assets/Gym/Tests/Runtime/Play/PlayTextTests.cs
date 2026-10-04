using System;
using System.Linq;
using System.Text.RegularExpressions;
using Gym.Runtime.Play;
using NUnit.Framework;

namespace Gym.Tests.Runtime.Play
{
    public class PlayTextTests
    {
        static readonly Regex Chinese = new Regex(@"[㐀-鿿]");

        static PlayTextKey[] AllKeys => (PlayTextKey[])Enum.GetValues(typeof(PlayTextKey));

        [Test]
        public void EveryKey_HasNonEmptyChineseAndEnglishText()
        {
            CollectionAssert.AreEquivalent(AllKeys, PlayText.Keys.ToArray(), "the table covers every key exactly once");
            foreach (PlayTextKey key in AllKeys)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(PlayText.Get(key, PlayLanguage.Chinese)), $"{key} Chinese");
                Assert.IsFalse(string.IsNullOrWhiteSpace(PlayText.Get(key, PlayLanguage.English)), $"{key} English");
            }
        }

        [Test]
        public void ChineseText_ContainsChineseCharactersForEveryKey()
        {
            // 表里没有纯数字或纯符号的键，所以没有例外。
            foreach (PlayTextKey key in AllKeys)
                StringAssert.IsMatch(Chinese.ToString(), PlayText.Get(key, PlayLanguage.Chinese), key.ToString());
        }

        [Test]
        public void EnglishText_ContainsNoChineseCharacters()
        {
            foreach (PlayTextKey key in AllKeys)
                Assert.IsFalse(Chinese.IsMatch(PlayText.Get(key, PlayLanguage.English)), key.ToString());
        }

        [Test]
        public void Format_FillsInNumbersThatAreAlreadyWrittenTheEnglishWay()
        {
            Assert.AreEqual("0.05879 BTC（5879 个单位）", PlayText.Format(PlayTextKey.CoinAmount, PlayLanguage.Chinese, "0.05879", 5879));
            Assert.AreEqual("0.05879 BTC (5879 units)", PlayText.Format(PlayTextKey.CoinAmount, PlayLanguage.English, "0.05879", 5879));
            Assert.AreEqual("买入 25% 成交", PlayText.Format(PlayTextKey.ActionBuyFilled, PlayLanguage.Chinese, "25%"));
            Assert.AreEqual("BUY 25%: filled", PlayText.Format(PlayTextKey.ActionBuyFilled, PlayLanguage.English, "25%"));
        }
    }
}

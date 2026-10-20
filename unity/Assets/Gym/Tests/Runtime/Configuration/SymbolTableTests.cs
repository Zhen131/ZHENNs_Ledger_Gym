using System.IO;
using Gym.Runtime.Configuration;
using NUnit.Framework;
using UnityEngine;

namespace Gym.Tests.Runtime.Configuration
{
    public class SymbolTableTests
    {
        [Test]
        public void DefaultSymbolsFile_ListsSixSymbols()
        {
            SymbolTable table = JsonUtility.FromJson<SymbolTable>(File.ReadAllText(GymConfigLoader.DefaultSymbolsPath));
            Assert.AreEqual(6, table.symbols.Length); // BTC、ETH、ADA，加上 12 批的 DOGE、BNB、XRP
            Assert.AreEqual(("BTCUSDT", 5.0, "0.00001"), (table.symbols[0].symbol, table.symbols[0].minNotional, table.symbols[0].stepSize));
            Assert.AreEqual(("ETHUSDT", 5.0, "0.0001"), (table.symbols[1].symbol, table.symbols[1].minNotional, table.symbols[1].stepSize));
            Assert.AreEqual(("ADAUSDT", 5.0, "0.1"), (table.symbols[2].symbol, table.symbols[2].minNotional, table.symbols[2].stepSize));
        }
    }
}

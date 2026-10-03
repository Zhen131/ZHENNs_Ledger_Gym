using System.IO;
using Gym.Core.Market;
using Gym.Runtime.Configuration;
using NUnit.Framework;

namespace Gym.Tests.Editor
{
    public class GymDataCacheTests
    {
        [Test]
        public void E01_DataIsParsedOncePerPath()
        {
            CandleSeries a = GymDataCache.Get(Path.Combine(GymConfigLoader.DefaultDirectory, "data", "BTCUSDT-1h.csv"));
            CandleSeries b = GymDataCache.Get(Path.Combine(GymConfigLoader.DefaultDirectory, "data", ".", "BTCUSDT-1h.csv"));
            Assert.AreSame(a, b);
        }
    }
}

using System.IO;
using Gym.Core.Market;
using Gym.Runtime.Configuration;
using NUnit.Framework;

namespace Gym.Tests.Runtime.Configuration
{
    public class GymDataCacheTests
    {
        [Test]
        public void SameFileByTwoPaths_IsParsedOnce()
        {
            CandleSeries a = GymDataCache.Get(Path.Combine(GymConfigLoader.DefaultDirectory, "data", "BTCUSDT-1h.csv"));
            CandleSeries b = GymDataCache.Get(Path.Combine(GymConfigLoader.DefaultDirectory, "data", ".", "BTCUSDT-1h.csv"));
            Assert.AreSame(a, b);
        }
    }
}

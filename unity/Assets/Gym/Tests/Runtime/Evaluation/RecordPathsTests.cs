using Gym.Runtime.Evaluation;
using NUnit.Framework;

namespace Gym.Tests.Editor
{
    public class RecordPathsTests
    {
        [Test]
        public void M2_PathsForRecordsDropMachineFolders()
        {
            Assert.AreEqual("data/BTCUSDT-1h.csv", RecordPaths.PathForRecords("data/BTCUSDT-1h.csv"));
            Assert.AreEqual("data/BTCUSDT-1h.csv", RecordPaths.PathForRecords(@"data\BTCUSDT-1h.csv"));
            Assert.AreEqual("BTCUSDT-1h.csv", RecordPaths.PathForRecords("/Users/someone/Gym/data/BTCUSDT-1h.csv"));
            Assert.AreEqual("BTCUSDT-1h.csv", RecordPaths.PathForRecords(@"C:\Users\someone\Gym\data\BTCUSDT-1h.csv"));
            Assert.AreEqual("BTCUSDT-1h.csv", RecordPaths.PathForRecords(@"\\server\share\BTCUSDT-1h.csv"));
            Assert.AreEqual("BTCUSDT-1h.csv", RecordPaths.PathForRecords("~/Gym/BTCUSDT-1h.csv"));
            Assert.AreEqual("", RecordPaths.PathForRecords(null));
            Assert.AreEqual("TradingAgent.onnx", RecordPaths.FileNameOnly(@"C:\Users\someone\results\run\TradingAgent.onnx"));
            Assert.AreEqual("TradingAgent.onnx", RecordPaths.FileNameOnly("/Users/someone/results/run/TradingAgent.onnx"));
            Assert.AreEqual("", RecordPaths.FileNameOnly(null));
        }
    }
}

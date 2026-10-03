using System.Collections;
using System.Collections.Generic;

namespace Gym.Core.Evaluation
{
    /// <summary>A JSON object that keeps its keys in insertion order.</summary>
    public sealed class JsonObject : IEnumerable<KeyValuePair<string, object>>
    {
        readonly List<KeyValuePair<string, object>> items = new List<KeyValuePair<string, object>>();

        public void Add(string key, object value) => items.Add(new KeyValuePair<string, object>(key, value));
        public int Count => items.Count;
        public IEnumerator<KeyValuePair<string, object>> GetEnumerator() => items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => items.GetEnumerator();
    }
}

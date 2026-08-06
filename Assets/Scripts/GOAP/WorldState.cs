using System.Collections.Generic;
using System.Text;

namespace GOAP
{
    
    public class WorldState
    {
        // A key that is absent counts as false.
        public readonly Dictionary<string, bool> Facts;

        public WorldState()
        {
            Facts = new Dictionary<string, bool>();
        }

        private WorldState(Dictionary<string, bool> facts)
        {
            Facts = new Dictionary<string, bool>(facts);
        }

        public bool Get(string key)
        {
            return Facts.TryGetValue(key, out bool v) && v;
        }

        public void Set(string key, bool value)
        {
            Facts[key] = value;
        }

        public bool Satisfies(Dictionary<string, bool> conditions)
        {
            foreach (KeyValuePair<string, bool> c in conditions)
            {
                if (Get(c.Key) != c.Value)
                    return false;
            }
            return true;
        }

        public WorldState ApplyEffects(Dictionary<string, bool> effects)
        {
            WorldState result = new WorldState(Facts);
            foreach (KeyValuePair<string, bool> e in effects)
                result.Facts[e.Key] = e.Value;
            return result;
        }

        public string Key()
        {
            List<string> keys = new List<string>(Facts.Keys);
            keys.Sort();
            StringBuilder sb = new StringBuilder();
            foreach (string k in keys)
                sb.Append(k).Append('=').Append(Facts[k] ? '1' : '0').Append(';');
            return sb.ToString();
        }
    }
}

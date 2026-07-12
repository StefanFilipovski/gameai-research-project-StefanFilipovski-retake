using System.Collections.Generic;
using System.Text;

namespace GOAP
{
    /// <summary>
    /// The symbolic world model used by the planner.
    ///
    /// GOAP does not reason about the game world directly. Instead it reasons about
    /// a small set of named boolean facts — e.g. "HasAxe", "HasWood", "AxeInShed".
    /// A <see cref="WorldState"/> is simply a snapshot of those facts.
    ///
    /// The planner treats each distinct WorldState as a NODE in a graph, and searches
    /// through that graph with A* (see <see cref="GoapPlanner"/>). Keeping the state
    /// small and symbolic is what makes that search cheap.
    /// </summary>
    public class WorldState
    {
        // key -> value. Missing key is treated as false.
        public readonly Dictionary<string, bool> Facts;

        public WorldState()
        {
            Facts = new Dictionary<string, bool>();
        }

        public WorldState(Dictionary<string, bool> facts)
        {
            // Copy so the caller's dictionary is never mutated behind their back.
            Facts = new Dictionary<string, bool>(facts);
        }

        /// <summary>A deep copy — planning explores many hypothetical states without touching the real one.</summary>
        public WorldState Clone()
        {
            return new WorldState(Facts);
        }

        public bool Get(string key)
        {
            return Facts.TryGetValue(key, out bool v) && v;
        }

        public void Set(string key, bool value)
        {
            Facts[key] = value;
        }

        /// <summary>
        /// True if EVERY fact in <paramref name="conditions"/> matches this state.
        /// Used both to test action preconditions and to test whether a goal is reached.
        /// </summary>
        public bool Satisfies(Dictionary<string, bool> conditions)
        {
            foreach (KeyValuePair<string, bool> c in conditions)
            {
                bool current = Facts.TryGetValue(c.Key, out bool v) && v;
                if (current != c.Value)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Returns a NEW state with the given effects applied. The original is untouched,
        /// which is exactly what A* needs when generating a neighbour node.
        /// </summary>
        public WorldState ApplyEffects(Dictionary<string, bool> effects)
        {
            WorldState result = Clone();
            foreach (KeyValuePair<string, bool> e in effects)
                result.Facts[e.Key] = e.Value;
            return result;
        }

        /// <summary>
        /// A stable string that uniquely identifies this state's facts, used as the key
        /// for A*'s closed set so we never expand the same world-state twice.
        /// </summary>
        public string Key()
        {
            List<string> keys = new List<string>(Facts.Keys);
            keys.Sort(); // sort so identical states always produce identical keys
            StringBuilder sb = new StringBuilder();
            foreach (string k in keys)
                sb.Append(k).Append('=').Append(Facts[k] ? '1' : '0').Append(';');
            return sb.ToString();
        }
    }
}

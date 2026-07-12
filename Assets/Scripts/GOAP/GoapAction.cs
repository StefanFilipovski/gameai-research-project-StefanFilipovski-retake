using System.Collections.Generic;
using UnityEngine;

namespace GOAP
{
    /// <summary>
    /// A single action the agent can plan with and perform.
    ///
    /// An action has two halves:
    ///   1. The SYMBOLIC half the planner cares about — <see cref="Preconditions"/> (what
    ///      must be true to start), <see cref="Effects"/> (what becomes true afterwards),
    ///      and a <see cref="Cost"/> (how "expensive" it is). This is all A* needs.
    ///   2. The RUNTIME half the agent cares about — a <see cref="Target"/> to walk to and a
    ///      <see cref="Duration"/> to spend performing it once there.
    ///
    /// The fluent helpers (Pre/Effect/At/TakesSeconds) let the demo declare actions readably:
    ///   new GoapAction("ChopTree", 3f).Pre("HasAxe", true).Effect("HasWood", true).At(tree);
    /// </summary>
    public class GoapAction
    {
        public readonly string Name;

        /// <summary>
        /// The cost A* accumulates when it includes this action in a plan. Cheaper actions
        /// are preferred, so cost is how you bias the agent toward one strategy over another
        /// (e.g. fetching a free axe from the shed should cost less than buying one).
        /// </summary>
        public float Cost;

        public readonly Dictionary<string, bool> Preconditions = new Dictionary<string, bool>();
        public readonly Dictionary<string, bool> Effects = new Dictionary<string, bool>();

        // ---- Runtime (world) data, ignored by the planner ----

        /// <summary>Where the agent must stand to perform this action. Null = perform in place.</summary>
        public Transform Target;

        /// <summary>Seconds the agent spends performing the action after arriving.</summary>
        public float Duration = 1f;

        public GoapAction(string name, float cost = 1f)
        {
            Name = name;
            Cost = cost;
        }

        // ---- Fluent builders ----

        public GoapAction Pre(string key, bool value)
        {
            Preconditions[key] = value;
            return this;
        }

        public GoapAction Effect(string key, bool value)
        {
            Effects[key] = value;
            return this;
        }

        public GoapAction At(Transform target)
        {
            Target = target;
            return this;
        }

        public GoapAction TakesSeconds(float seconds)
        {
            Duration = seconds;
            return this;
        }
    }
}

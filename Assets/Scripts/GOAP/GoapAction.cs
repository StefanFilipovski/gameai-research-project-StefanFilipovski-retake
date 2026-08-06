using System.Collections.Generic;
using UnityEngine;

namespace GOAP
{
    /// <summary>
    /// Something the agent can do. The planner only reads Preconditions, Effects and Cost;
    /// Target and Duration are used by the agent when actually performing it.
    /// </summary>
    public class GoapAction
    {
        public readonly string Name;

        /// <summary>What A* accumulates for including this action. Lower cost = preferred route.</summary>
        public readonly float Cost;

        public readonly Dictionary<string, bool> Preconditions = new Dictionary<string, bool>();
        public readonly Dictionary<string, bool> Effects = new Dictionary<string, bool>();

        /// <summary>Where the agent must stand to perform this action.</summary>
        public Transform Target;

        /// <summary>Seconds spent performing it after arriving.</summary>
        public float Duration = 1f;

        public GoapAction(string name, float cost)
        {
            Name = name;
            Cost = cost;
        }

        // Fluent builders, so the demo can declare actions in one readable line each.

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

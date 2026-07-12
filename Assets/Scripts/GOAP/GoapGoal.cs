using System.Collections.Generic;

namespace GOAP
{
    /// <summary>
    /// A goal is a desired <see cref="WorldState"/> plus a priority.
    ///
    /// The agent may hold several goals at once (e.g. "StayFed", "GatherWood"). Each frame it
    /// picks the highest-priority goal that is not already satisfied and asks the planner for a
    /// sequence of actions that reaches it. Priority is what makes the behaviour feel like it
    /// has changing intentions rather than a fixed script.
    /// </summary>
    public class GoapGoal
    {
        public readonly string Name;
        public float Priority;

        /// <summary>The facts that must all be true for this goal to count as achieved.</summary>
        public readonly Dictionary<string, bool> DesiredState = new Dictionary<string, bool>();

        public GoapGoal(string name, float priority)
        {
            Name = name;
            Priority = priority;
        }

        public GoapGoal Want(string key, bool value)
        {
            DesiredState[key] = value;
            return this;
        }
    }
}

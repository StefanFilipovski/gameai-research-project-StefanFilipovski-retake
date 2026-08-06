using System.Collections.Generic;

namespace GOAP
{
    /// <summary>
    /// A world-state the agent wants to bring about, plus a priority used to pick between
    /// several unsatisfied goals (see GoapAgent.ChooseGoal).
    /// </summary>
    public class GoapGoal
    {
        public readonly string Name;
        public readonly float Priority;

        /// <summary>All of these facts must hold for the goal to count as achieved.</summary>
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

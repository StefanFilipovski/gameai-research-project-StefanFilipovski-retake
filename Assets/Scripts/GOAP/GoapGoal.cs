using System.Collections.Generic;

namespace GOAP
{
   
    public class GoapGoal
    {
        public readonly string Name;
        public readonly float Priority;

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

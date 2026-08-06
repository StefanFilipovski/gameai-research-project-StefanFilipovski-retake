using System.Collections.Generic;
using UnityEngine;

namespace GOAP
{
    
    public class GoapAction
    {
        public readonly string Name;

        public readonly float Cost;

        public readonly Dictionary<string, bool> Preconditions = new Dictionary<string, bool>();
        public readonly Dictionary<string, bool> Effects = new Dictionary<string, bool>();

        public Transform Target;

        public float Duration = 1f;

        public GoapAction(string name, float cost)
        {
            Name = name;
            Cost = cost;
        }


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

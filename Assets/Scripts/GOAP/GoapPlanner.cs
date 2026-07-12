using System.Collections.Generic;

namespace GOAP
{
    /// <summary>
    /// The core of GOAP: an A* search through the space of WORLD-STATES.
    ///
    /// This is the single most important idea of the whole project, so here is the mapping
    /// between classic A* pathfinding and GOAP planning explicitly:
    ///
    ///     Grid / navmesh A*         GOAP planner
    ///     ------------------------  ------------------------------------------------
    ///     node   = a tile           node   = a whole WorldState (a set of facts)
    ///     edge   = step to neighbour edge  = applying an ACTION
    ///     edge cost = distance       edge cost = action.Cost
    ///     start  = start tile        start  = the agent's CURRENT world state
    ///     goal   = goal tile         goal   = ANY state that satisfies the goal's facts
    ///     heuristic = distance-to-goal  heuristic = number of goal facts not yet satisfied
    ///
    /// So "planning" is literally pathfinding — but through an abstract graph of possible
    /// world-states instead of through physical space. The output path is the ordered list
    /// of actions the agent should perform.
    /// </summary>
    public class GoapPlanner
    {
        /// <summary>One node in the search: a hypothetical world-state and how we reached it.</summary>
        private class Node
        {
            public WorldState State;
            public Node Parent;      // the node we came from
            public GoapAction Action; // the action applied to Parent to reach this State
            public float G;          // cost accumulated from the start to here
            public float F;          // G + heuristic  (A*'s priority value)
        }

        /// <summary>
        /// Returns the cheapest ordered list of actions that takes <paramref name="start"/> to a
        /// state satisfying <paramref name="goal"/>, or null if no such plan exists.
        /// </summary>
        public List<GoapAction> Plan(WorldState start, GoapGoal goal, List<GoapAction> actions)
        {
            List<Node> open = new List<Node>();          // frontier, still to explore
            HashSet<string> closed = new HashSet<string>(); // world-states already expanded

            Node startNode = new Node
            {
                State = start,
                Parent = null,
                Action = null,
                G = 0f,
                F = Heuristic(start, goal)
            };
            open.Add(startNode);

            while (open.Count > 0)
            {
                // --- Pop the open node with the lowest F (best estimated total cost) ---
                int bestIndex = 0;
                for (int i = 1; i < open.Count; i++)
                    if (open[i].F < open[bestIndex].F)
                        bestIndex = i;

                Node current = open[bestIndex];
                open.RemoveAt(bestIndex);

                // --- Goal test: are all the goal's facts satisfied in this state? ---
                if (current.State.Satisfies(goal.DesiredState))
                    return ReconstructPlan(current);

                closed.Add(current.State.Key());

                // --- Expand: every action whose preconditions hold is an outgoing edge ---
                foreach (GoapAction action in actions)
                {
                    if (!current.State.Satisfies(action.Preconditions))
                        continue; // can't perform this action from the current state

                    WorldState next = current.State.ApplyEffects(action.Effects);

                    if (closed.Contains(next.Key()))
                        continue; // already fully explored this resulting state

                    float g = current.G + action.Cost;

                    // If we already have a cheaper route to an identical state on the frontier,
                    // don't bother adding a worse duplicate.
                    if (HasCheaperOpenNode(open, next, g))
                        continue;

                    open.Add(new Node
                    {
                        State = next,
                        Parent = current,
                        Action = action,
                        G = g,
                        F = g + Heuristic(next, goal)
                    });
                }
            }

            return null; // frontier exhausted without reaching the goal -> no plan
        }

        /// <summary>
        /// Heuristic h(n): how many of the goal's facts are still wrong in this state.
        /// Because a well-formed action fixes at most one goal fact, this never overestimates
        /// the remaining cost, so it is admissible and A* stays optimal.
        /// </summary>
        private int Heuristic(WorldState state, GoapGoal goal)
        {
            int missing = 0;
            foreach (KeyValuePair<string, bool> g in goal.DesiredState)
            {
                bool current = state.Facts.TryGetValue(g.Key, out bool v) && v;
                if (current != g.Value)
                    missing++;
            }
            return missing;
        }

        private bool HasCheaperOpenNode(List<Node> open, WorldState state, float g)
        {
            string key = state.Key();
            foreach (Node n in open)
                if (n.State.Key() == key && n.G <= g)
                    return true;
            return false;
        }

        /// <summary>Walk parent pointers from the goal node back to the start, reversing into plan order.</summary>
        private List<GoapAction> ReconstructPlan(Node node)
        {
            List<GoapAction> plan = new List<GoapAction>();
            while (node != null && node.Action != null)
            {
                plan.Insert(0, node.Action);
                node = node.Parent;
            }
            return plan;
        }
    }
}

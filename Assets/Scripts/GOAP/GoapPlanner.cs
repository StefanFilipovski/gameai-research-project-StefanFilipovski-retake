using System.Collections.Generic;

namespace GOAP
{
    /// <summary>
    /// A* search over world-states. Same algorithm as grid pathfinding, different graph:
    ///
    ///     node      = a WorldState        (instead of a tile)
    ///     edge      = applying an action  (instead of stepping to a neighbour)
    ///     edge cost = action.Cost         (instead of distance)
    ///     goal test = state satisfies the goal's facts
    ///     heuristic = number of goal facts not yet satisfied
    ///
    /// The returned "path" is the ordered list of actions the agent should perform.
    /// </summary>
    public class GoapPlanner
    {
        private class Node
        {
            public WorldState State;
            public Node Parent;
            public GoapAction Action;   // the action applied to Parent to reach State
            public float G;             // cost from the start
            public float H;             // estimated cost remaining
            public float F;             // G + H

            // Only filled in when RecordSearch is on.
            public int Id = -1;
            public int ParentId = -1;
            public int Depth;
            public int ExpandedOrder = -1;
        }

        /// <summary>A read-only copy of one searched node, for the debug visualizer.</summary>
        public class SearchNode
        {
            public int Id;
            public int ParentId;        // -1 for the start node
            public string ActionName;   // null for the start node
            public float G;
            public float H;
            public float F;
            public int Depth;
            public int ExpandedOrder;   // -1 if generated but never expanded
            public bool SatisfiesGoal;
            public bool OnFinalPlan;
            public string TrueFacts;
        }

        /// <summary>When on, each Plan() call fills LastSearch so the demo can draw the search tree.</summary>
        public bool RecordSearch;
        public List<SearchNode> LastSearch;

        /// <summary>Cheapest action sequence from start to a state satisfying goal, or null if none exists.</summary>
        public List<GoapAction> Plan(WorldState start, GoapGoal goal, List<GoapAction> actions)
        {
            List<Node> open = new List<Node>();
            HashSet<string> closed = new HashSet<string>();
            List<Node> recorded = RecordSearch ? new List<Node>() : null;
            int expandCounter = 0;

            Node startNode = new Node
            {
                State = start,
                G = 0f,
                H = Heuristic(start, goal)
            };
            startNode.F = startNode.G + startNode.H;
            Register(recorded, startNode);
            open.Add(startNode);

            while (open.Count > 0)
            {
                // Pop the lowest F: the most promising state to expand next.
                int bestIndex = 0;
                for (int i = 1; i < open.Count; i++)
                    if (open[i].F < open[bestIndex].F)
                        bestIndex = i;

                Node current = open[bestIndex];
                open.RemoveAt(bestIndex);
                if (recorded != null)
                    current.ExpandedOrder = expandCounter++;

                if (current.State.Satisfies(goal.DesiredState))
                {
                    BuildTrace(recorded, current, goal);
                    return ReconstructPlan(current);
                }

                closed.Add(current.State.Key());

                // Each action whose preconditions hold is an edge out of this state.
                foreach (GoapAction action in actions)
                {
                    if (!current.State.Satisfies(action.Preconditions))
                        continue;

                    WorldState next = current.State.ApplyEffects(action.Effects);
                    if (closed.Contains(next.Key()))
                        continue;

                    float g = current.G + action.Cost;
                    if (HasCheaperOpenNode(open, next, g))
                        continue;

                    Node child = new Node
                    {
                        State = next,
                        Parent = current,
                        Action = action,
                        G = g,
                        H = Heuristic(next, goal),
                        Depth = current.Depth + 1,
                        ParentId = current.Id
                    };
                    child.F = child.G + child.H;
                    Register(recorded, child);
                    open.Add(child);
                }
            }

            BuildTrace(recorded, null, goal);
            return null;
        }

        /// <summary>
        /// Counts goal facts that are still wrong. An action fixes at most one of them, so this
        /// never overestimates the remaining cost — it is admissible, so A* returns the cheapest plan.
        /// </summary>
        private int Heuristic(WorldState state, GoapGoal goal)
        {
            int missing = 0;
            foreach (KeyValuePair<string, bool> g in goal.DesiredState)
                if (state.Get(g.Key) != g.Value)
                    missing++;
            return missing;
        }

        /// <summary>True if the frontier already holds this same state at no greater cost.</summary>
        private bool HasCheaperOpenNode(List<Node> open, WorldState state, float g)
        {
            string key = state.Key();
            foreach (Node n in open)
                if (n.G <= g && n.State.Key() == key)
                    return true;
            return false;
        }

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

        private static void Register(List<Node> recorded, Node node)
        {
            if (recorded == null)
                return;
            node.Id = recorded.Count;
            recorded.Add(node);
        }

        /// <summary>Copies the recorded search into SearchNodes, marking the winning path.</summary>
        private void BuildTrace(List<Node> recorded, Node goalNode, GoapGoal goal)
        {
            if (recorded == null)
                return;

            HashSet<int> finalIds = new HashSet<int>();
            for (Node n = goalNode; n != null; n = n.Parent)
                finalIds.Add(n.Id);

            LastSearch = new List<SearchNode>(recorded.Count);
            foreach (Node n in recorded)
            {
                LastSearch.Add(new SearchNode
                {
                    Id = n.Id,
                    ParentId = n.ParentId,
                    ActionName = n.Action != null ? n.Action.Name : null,
                    G = n.G,
                    H = n.H,
                    F = n.F,
                    Depth = n.Depth,
                    ExpandedOrder = n.ExpandedOrder,
                    SatisfiesGoal = n.State.Satisfies(goal.DesiredState),
                    OnFinalPlan = finalIds.Contains(n.Id),
                    TrueFacts = SummarizeTrueFacts(n.State)
                });
            }
        }

        private static string SummarizeTrueFacts(WorldState state)
        {
            List<string> trueFacts = new List<string>();
            foreach (KeyValuePair<string, bool> f in state.Facts)
                if (f.Value)
                    trueFacts.Add(f.Key);
            trueFacts.Sort();
            return trueFacts.Count == 0 ? "(nothing true)" : string.Join(", ", trueFacts);
        }
    }
}

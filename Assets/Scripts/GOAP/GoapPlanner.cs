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
            public Node Parent;       // the node we came from
            public GoapAction Action; // the action applied to Parent to reach this State
            public float G;           // cost accumulated from the start to here
            public float H;           // heuristic estimate of cost remaining
            public float F;           // G + H  (A*'s priority value)

            // Bookkeeping used only when recording the search for the visualizer.
            public int Id = -1;
            public int ParentId = -1;
            public int Depth;
            public int ExpandedOrder = -1; // the order in which A* popped/expanded this node
        }

        /// <summary>
        /// A read-only snapshot of one node in the last search, exposed for the debug visualizer.
        /// This is deliberately a plain data record so UI code never touches the live search nodes.
        /// </summary>
        public class SearchNode
        {
            public int Id;
            public int ParentId;        // -1 for the start node
            public string ActionName;   // null for the start node
            public float G;
            public float H;
            public float F;
            public int Depth;
            public int ExpandedOrder;   // -1 if it was generated but never expanded
            public bool SatisfiesGoal;
            public bool OnFinalPlan;    // true if this node lies on the returned plan's path
            public string TrueFacts;    // the facts that are true in this state (for the label)
        }

        // When true, Plan() records every frontier node into LastSearch so the demo can draw the tree.
        public bool RecordSearch;
        public List<SearchNode> LastSearch;

        /// <summary>
        /// Returns the cheapest ordered list of actions that takes <paramref name="start"/> to a
        /// state satisfying <paramref name="goal"/>, or null if no such plan exists.
        /// </summary>
        public List<GoapAction> Plan(WorldState start, GoapGoal goal, List<GoapAction> actions)
        {
            List<Node> open = new List<Node>();          // frontier, still to explore
            HashSet<string> closed = new HashSet<string>(); // world-states already expanded

            // Every node we put on the frontier, kept only so the visualizer can replay the search.
            List<Node> recorded = RecordSearch ? new List<Node>() : null;
            int expandCounter = 0;

            Node startNode = new Node
            {
                State = start,
                Parent = null,
                Action = null,
                G = 0f,
                H = Heuristic(start, goal),
                Depth = 0
            };
            startNode.F = startNode.G + startNode.H;
            Register(recorded, startNode);
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
                if (recorded != null)
                    current.ExpandedOrder = expandCounter++;

                // --- Goal test: are all the goal's facts satisfied in this state? ---
                if (current.State.Satisfies(goal.DesiredState))
                {
                    BuildTrace(recorded, current, goal);
                    return ReconstructPlan(current);
                }

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

                    Node child = new Node
                    {
                        State = next,
                        Parent = current,
                        Action = action,
                        G = g,
                        H = Heuristic(next, goal),
                        Depth = current.Depth + 1
                    };
                    child.F = child.G + child.H;
                    child.ParentId = current.Id;
                    Register(recorded, child);
                    open.Add(child);
                }
            }

            BuildTrace(recorded, null, goal); // no plan found — still record the explored tree
            return null;                       // frontier exhausted without reaching the goal
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

        // ---- Search recording (only active when RecordSearch is true) ----

        private static void Register(List<Node> recorded, Node node)
        {
            if (recorded == null)
                return;
            node.Id = recorded.Count;
            recorded.Add(node);
        }

        /// <summary>Turn the internal search nodes into the read-only SearchNode snapshots for the UI.</summary>
        private void BuildTrace(List<Node> recorded, Node goalNode, GoapGoal goal)
        {
            if (recorded == null)
                return;

            // Collect the ids that lie on the winning path (if we found one).
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
            return trueFacts.Count == 0 ? "(start / nothing true)" : string.Join(", ", trueFacts);
        }
    }
}

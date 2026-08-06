using System.Collections.Generic;

namespace GOAP
{
  
    public class GoapPlanner
    {
        private class Node
        {
            public WorldState State;
            public Node Parent;
            public GoapAction Action;   
            public float G;             
            public float H;             
            public float F;             

            // Only filled in when RecordSearch is on.
            public int Id = -1;
            public int ParentId = -1;
            public int Depth;
            public int ExpandedOrder = -1;
        }

        public class SearchNode
        {
            public int Id;
            public int ParentId;        
            public string ActionName;   
            public float G;
            public float H;
            public float F;
            public int Depth;
            public int ExpandedOrder;   
            public bool SatisfiesGoal;
            public bool OnFinalPlan;
            public string TrueFacts;
        }

        public struct PlanStats
        {
            public int NodesExpanded;   
            public int NodesGenerated;  
            public double Microseconds; 
            public int PlanLength;
            public float PlanCost;
            public bool Found;
        }

        public bool RecordSearch;
        public List<SearchNode> LastSearch;
        public PlanStats LastStats;

        private readonly System.Diagnostics.Stopwatch _timer = new System.Diagnostics.Stopwatch();

        public List<GoapAction> Plan(WorldState start, GoapGoal goal, List<GoapAction> actions)
        {
            _timer.Restart();
            LastStats = new PlanStats();

            List<Node> open = new List<Node>();
            HashSet<string> closed = new HashSet<string>();
            List<Node> recorded = RecordSearch ? new List<Node>() : null;

            Node startNode = new Node
            {
                State = start,
                G = 0f,
                H = Heuristic(start, goal)
            };
            startNode.F = startNode.G + startNode.H;
            Register(recorded, startNode);
            open.Add(startNode);
            LastStats.NodesGenerated = 1;

            while (open.Count > 0)
            {
                int bestIndex = 0;
                for (int i = 1; i < open.Count; i++)
                    if (open[i].F < open[bestIndex].F)
                        bestIndex = i;

                Node current = open[bestIndex];
                open.RemoveAt(bestIndex);
                current.ExpandedOrder = LastStats.NodesExpanded++;

                if (current.State.Satisfies(goal.DesiredState))
                {
                    BuildTrace(recorded, current, goal);
                    List<GoapAction> plan = ReconstructPlan(current);
                    FinishStats(plan);
                    return plan;
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
                    LastStats.NodesGenerated++;
                }
            }

            BuildTrace(recorded, null, goal);
            FinishStats(null);
            return null;
        }

        private void FinishStats(List<GoapAction> plan)
        {
            _timer.Stop();
            LastStats.Microseconds = _timer.Elapsed.TotalMilliseconds * 1000.0;
            LastStats.Found = plan != null;
            if (plan == null)
                return;

            LastStats.PlanLength = plan.Count;
            foreach (GoapAction a in plan)
                LastStats.PlanCost += a.Cost;
        }

       
        private int Heuristic(WorldState state, GoapGoal goal)
        {
            int missing = 0;
            foreach (KeyValuePair<string, bool> g in goal.DesiredState)
                if (state.Get(g.Key) != g.Value)
                    missing++;
            return missing;
        }

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

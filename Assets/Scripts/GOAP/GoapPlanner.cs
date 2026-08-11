using System.Collections.Generic;

namespace GOAP
{
    public class GoapPlanner
    {
        public enum HeuristicMode { GoalFactCount, RelaxedPlanGraph }

        private class Node
        {
            public WorldState State;
            public Node Parent;
            public GoapAction Action;
            public float G;
            public float H;
            public float F;
            public int Sequence;

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
            public int NodesPruned;
            public double Microseconds;
            public int PlanLength;
            public float PlanCost;
            public bool Found;
        }

        public HeuristicMode Heuristics = HeuristicMode.RelaxedPlanGraph;
        public bool RecordSearch;
        public List<SearchNode> LastSearch;
        public PlanStats LastStats;

        private readonly System.Diagnostics.Stopwatch _timer = new System.Diagnostics.Stopwatch();
        private readonly MinHeap _open = new MinHeap();

        public List<GoapAction> Plan(WorldState start, GoapGoal goal, List<GoapAction> actions)
        {
            _timer.Restart();
            LastStats = new PlanStats();

            _open.Clear();
            HashSet<string> closed = new HashSet<string>();

            // Cheapest cost at which each state currently sits on the frontier. This is what makes
            // a duplicate state replaceable instead of merely skippable.
            Dictionary<string, float> bestOnOpen = new Dictionary<string, float>();
            List<Node> recorded = RecordSearch ? new List<Node>() : null;
            int sequence = 0;

            float startH = Estimate(start, goal, actions);
            if (float.IsPositiveInfinity(startH))
            {
                // Even ignoring every negative effect the goal cannot be produced, so no plan exists.
                BuildTrace(recorded, null, goal);
                FinishStats(null);
                return null;
            }

            Node startNode = new Node { State = start, G = 0f, H = startH, F = startH, Sequence = sequence++ };
            Register(recorded, startNode);
            _open.Push(startNode);
            bestOnOpen[start.Key()] = 0f;
            LastStats.NodesGenerated = 1;

            while (_open.Count > 0)
            {
                Node current = _open.Pop();
                string currentKey = current.State.Key();

                // A cheaper route to this state was found after this copy was queued.
                if (bestOnOpen.TryGetValue(currentKey, out float best) && current.G > best)
                    continue;

                current.ExpandedOrder = LastStats.NodesExpanded++;

                if (current.State.Satisfies(goal.DesiredState))
                {
                    BuildTrace(recorded, current, goal);
                    List<GoapAction> plan = ReconstructPlan(current);
                    FinishStats(plan);
                    return plan;
                }

                closed.Add(currentKey);
                bestOnOpen.Remove(currentKey);

                foreach (GoapAction action in actions)
                {
                    if (!current.State.Satisfies(action.Preconditions))
                        continue;

                    WorldState next = current.State.ApplyEffects(action.Effects);
                    string nextKey = next.Key();
                    if (closed.Contains(nextKey))
                        continue;

                    float g = current.G + action.Cost;
                    if (bestOnOpen.TryGetValue(nextKey, out float queued) && queued <= g)
                        continue;

                    float h = Estimate(next, goal, actions);
                    if (float.IsPositiveInfinity(h))
                    {
                        // This branch can never reach the goal; drop it rather than queue it.
                        LastStats.NodesPruned++;
                        continue;
                    }

                    Node child = new Node
                    {
                        State = next,
                        Parent = current,
                        Action = action,
                        G = g,
                        H = h,
                        F = g + h,
                        Sequence = sequence++,
                        Depth = current.Depth + 1,
                        ParentId = current.Id
                    };
                    Register(recorded, child);
                    _open.Push(child);
                    bestOnOpen[nextKey] = g;
                    LastStats.NodesGenerated++;
                }
            }

            BuildTrace(recorded, null, goal);
            FinishStats(null);
            return null;
        }

        private float Estimate(WorldState state, GoapGoal goal, List<GoapAction> actions)
        {
            return Heuristics == HeuristicMode.GoalFactCount
                ? UnsatisfiedGoalFacts(state, goal)
                : RelaxedPlanCost(state, goal, actions);
        }

        private static float UnsatisfiedGoalFacts(WorldState state, GoapGoal goal)
        {
            int missing = 0;
            foreach (KeyValuePair<string, bool> g in goal.DesiredState)
                if (state.Get(g.Key) != g.Value)
                    missing++;
            return missing;
        }

        private static float RelaxedPlanCost(WorldState state, GoapGoal goal, List<GoapAction> actions)
        {
            Dictionary<string, float> reach = new Dictionary<string, float>();
            foreach (KeyValuePair<string, bool> f in state.Facts)
                if (f.Value)
                    reach[f.Key] = 0f;

            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (GoapAction action in actions)
                {
                    float readyAt = 0f;
                    bool applicable = true;

                    foreach (KeyValuePair<string, bool> pre in action.Preconditions)
                    {
                        if (!pre.Value)
                            continue; // the relaxation never deletes facts, so negative ones are free
                        if (!reach.TryGetValue(pre.Key, out float at))
                        {
                            applicable = false;
                            break;
                        }
                        if (at > readyAt)
                            readyAt = at;
                    }

                    if (!applicable)
                        continue;

                    float produced = readyAt + action.Cost;
                    foreach (KeyValuePair<string, bool> effect in action.Effects)
                    {
                        if (!effect.Value)
                            continue;
                        if (!reach.TryGetValue(effect.Key, out float existing) || produced < existing)
                        {
                            reach[effect.Key] = produced;
                            changed = true;
                        }
                    }
                }
            }

            float estimate = 0f;
            foreach (KeyValuePair<string, bool> g in goal.DesiredState)
            {
                if (!g.Value)
                    continue;
                if (!reach.TryGetValue(g.Key, out float at))
                    return float.PositiveInfinity;
                if (at > estimate)
                    estimate = at;
            }
            return estimate;
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

        private class MinHeap
        {
            private readonly List<Node> _items = new List<Node>();

            public int Count => _items.Count;

            public void Clear()
            {
                _items.Clear();
            }

            public void Push(Node node)
            {
                _items.Add(node);
                int child = _items.Count - 1;
                while (child > 0)
                {
                    int parent = (child - 1) / 2;
                    if (!IsBetter(_items[child], _items[parent]))
                        break;
                    Swap(child, parent);
                    child = parent;
                }
            }

            public Node Pop()
            {
                Node top = _items[0];
                int last = _items.Count - 1;
                _items[0] = _items[last];
                _items.RemoveAt(last);

                int parent = 0;
                while (true)
                {
                    int left = parent * 2 + 1;
                    if (left >= _items.Count)
                        break;

                    int best = left;
                    int right = left + 1;
                    if (right < _items.Count && IsBetter(_items[right], _items[left]))
                        best = right;

                    if (!IsBetter(_items[best], _items[parent]))
                        break;

                    Swap(best, parent);
                    parent = best;
                }
                return top;
            }

            private static bool IsBetter(Node a, Node b)
            {
                if (a.F != b.F)
                    return a.F < b.F;
                return a.Sequence < b.Sequence;
            }

            private void Swap(int a, int b)
            {
                Node temp = _items[a];
                _items[a] = _items[b];
                _items[b] = temp;
            }
        }
    }
}

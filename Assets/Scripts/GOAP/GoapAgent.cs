using System.Collections.Generic;
using UnityEngine;

namespace GOAP
{
    /// <summary>
    /// The runtime brain that sits on top of the planner.
    ///
    /// GOAP separates DECIDING (the planner works out *what* sequence of actions to do) from
    /// DOING (this component walks to each action's target and performs it). This agent is a
    /// tiny finite state machine:
    ///
    ///     Idle       -> ask the planner for a plan toward the best goal
    ///     Moving     -> walk to the current action's Target
    ///     Performing -> wait out the action's Duration, then apply its effects
    ///
    /// The important GOAP behaviour is REPLANNING: every frame we check that the current action
    /// is still valid against the (possibly changed) world. If someone empties the shed while we
    /// walk to it, the action's precondition breaks, we throw the plan away and plan again.
    /// </summary>
    public class GoapAgent : MonoBehaviour
    {
        public float MoveSpeed = 3.5f;

        // When on, the agent prints its planning/execution/replanning trace to the Console.
        public bool VerboseLogging = true;

        // The agent's live picture of the world. The planner reads this as its start state,
        // and completed actions write their effects back into it.
        public WorldState State = new WorldState();

        public readonly List<GoapGoal> Goals = new List<GoapGoal>();
        public readonly List<GoapAction> Actions = new List<GoapAction>();

        private readonly GoapPlanner _planner = new GoapPlanner();

        private List<GoapAction> _plan;
        private int _planIndex;
        private GoapAction _current;
        private GoapGoal _activeGoal;

        private enum Phase { Idle, Moving, Performing }
        private Phase _phase = Phase.Idle;
        private float _performTimer;
        private bool _loggedNoPlan; // avoids spamming the "no plan" warning every frame

        // ---- Exposed for the on-screen HUD ----
        public string StatusLine { get; private set; } = "Booting...";
        // True when the agent has an active goal but the planner could not find any plan for it.
        public bool PlanningFailed { get; private set; }
        public GoapGoal ActiveGoal => _activeGoal;
        public IReadOnlyList<GoapAction> CurrentPlan => _plan;
        public int PlanIndex => _planIndex;

        // ---- Exposed for the plan-search visualizer ----
        // When on, the planner records its A* search tree each time it plans.
        public bool RecordSearch
        {
            get => _planner.RecordSearch;
            set => _planner.RecordSearch = value;
        }
        public IReadOnlyList<GoapPlanner.SearchNode> LastSearch => _planner.LastSearch;

        private void Update()
        {
            if (_phase == Phase.Idle)
            {
                BuildPlan();
                return;
            }

            if (_current == null)
            {
                _phase = Phase.Idle;
                return;
            }

            // Replanning trigger: the world may have changed since we committed to this action.
            // If its preconditions no longer hold, abandon the plan and think again.
            if (!State.Satisfies(_current.Preconditions))
            {
                Replan("precondition of '" + _current.Name + "' broke");
                return;
            }

            if (_phase == Phase.Moving)
                TickMoving();
            else if (_phase == Phase.Performing)
                TickPerforming();
        }

        private void TickMoving()
        {
            if (_current.Target == null)
            {
                // No physical target — perform it where we stand.
                _phase = Phase.Performing;
                _performTimer = _current.Duration;
                return;
            }

            Vector3 destination = _current.Target.position;
            destination.y = transform.position.y; // stay on the ground plane
            transform.position = Vector3.MoveTowards(transform.position, destination, MoveSpeed * Time.deltaTime);

            StatusLine = "Moving to " + _current.Name;

            if (Vector3.Distance(transform.position, destination) < 0.05f)
            {
                _phase = Phase.Performing;
                _performTimer = _current.Duration;
            }
        }

        private void TickPerforming()
        {
            StatusLine = "Performing " + _current.Name;
            _performTimer -= Time.deltaTime;
            if (_performTimer > 0f)
                return;

            // Action finished: commit its effects to the real world state...
            foreach (KeyValuePair<string, bool> e in _current.Effects)
                State.Set(e.Key, e.Value);

            if (VerboseLogging)
                Debug.Log("[GOAP]   done '" + _current.Name + "'  -> " + DescribeEffects(_current.Effects));

            // ...and move on to the next action in the plan.
            AdvancePlan();
        }

        private void BuildPlan()
        {
            _activeGoal = ChooseGoal();
            if (_activeGoal == null)
            {
                StatusLine = "Idle — all goals satisfied";
                PlanningFailed = false;
                _plan = null;
                return;
            }

            List<GoapAction> plan = _planner.Plan(State, _activeGoal, Actions);
            if (plan == null || plan.Count == 0)
            {
                StatusLine = "No plan found for goal '" + _activeGoal.Name + "'";
                PlanningFailed = true;
                if (VerboseLogging && !_loggedNoPlan)
                {
                    Debug.LogWarning("[GOAP] No valid plan for goal '" + _activeGoal.Name +
                                     "'. World: " + DescribeState());
                    _loggedNoPlan = true;
                }
                _plan = null;
                return;
            }

            PlanningFailed = false;
            _loggedNoPlan = false;
            if (VerboseLogging)
                Debug.Log("[GOAP] Planned for goal '" + _activeGoal.Name + "': " +
                          DescribePlan(plan) + "  (total cost " + TotalCost(plan) + ")");

            _plan = plan;
            _planIndex = -1;
            AdvancePlan();
        }

        private void AdvancePlan()
        {
            _planIndex++;
            if (_plan == null || _planIndex >= _plan.Count)
            {
                // Plan complete — drop back to Idle so we re-evaluate goals next frame.
                _current = null;
                _phase = Phase.Idle;
                return;
            }

            _current = _plan[_planIndex];
            _phase = Phase.Moving;

            if (VerboseLogging)
                Debug.Log("[GOAP]   step " + (_planIndex + 1) + "/" + _plan.Count +
                          ": start '" + _current.Name + "' (cost " + _current.Cost + ")" +
                          (_current.Target != null ? "  -> moving to " + _current.Target.name : ""));
        }

        /// <summary>Discard the current plan and re-plan from scratch on the next frame.</summary>
        public void Replan(string reason)
        {
            StatusLine = "Replanning (" + reason + ")";
            if (VerboseLogging)
                Debug.Log("[GOAP] Replan requested: " + reason);
            _plan = null;
            _current = null;
            _phase = Phase.Idle;
        }

        /// <summary>Highest-priority goal that is not already satisfied.</summary>
        private GoapGoal ChooseGoal()
        {
            GoapGoal best = null;
            foreach (GoapGoal g in Goals)
            {
                if (State.Satisfies(g.DesiredState))
                    continue; // nothing to do for this one
                if (best == null || g.Priority > best.Priority)
                    best = g;
            }
            return best;
        }

        // ---- Logging helpers ----

        private static string DescribePlan(List<GoapAction> plan)
        {
            string[] names = new string[plan.Count];
            for (int i = 0; i < plan.Count; i++)
                names[i] = plan[i].Name;
            return string.Join(" -> ", names);
        }

        private static float TotalCost(List<GoapAction> plan)
        {
            float c = 0f;
            foreach (GoapAction a in plan)
                c += a.Cost;
            return c;
        }

        private static string DescribeEffects(Dictionary<string, bool> effects)
        {
            List<string> parts = new List<string>();
            foreach (KeyValuePair<string, bool> e in effects)
                parts.Add(e.Key + "=" + e.Value);
            return string.Join(", ", parts);
        }

        private string DescribeState()
        {
            List<string> parts = new List<string>();
            foreach (KeyValuePair<string, bool> f in State.Facts)
                parts.Add(f.Key + "=" + f.Value);
            return "{ " + string.Join(", ", parts) + " }";
        }
    }
}

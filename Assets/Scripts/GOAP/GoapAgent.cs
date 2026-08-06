using System.Collections.Generic;
using UnityEngine;

namespace GOAP
{
    /// <summary>
    /// Executes plans produced by GoapPlanner: Idle (ask for a plan) -> Moving -> Performing.
    /// Every frame it re-checks the running action's preconditions, so a world change mid-plan
    /// causes a replan rather than a broken sequence.
    /// </summary>
    public class GoapAgent : MonoBehaviour
    {
        public float MoveSpeed = 3.5f;
        public bool VerboseLogging = true;

        /// <summary>The agent's live world model: the planner's start state, written back by finished actions.</summary>
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
        private bool _loggedNoPlan; // so an unreachable goal warns once, not every frame

        // Read by the demo HUD.
        public string StatusLine { get; private set; } = "Booting...";
        public bool PlanningFailed { get; private set; }
        public GoapGoal ActiveGoal => _activeGoal;
        public IReadOnlyList<GoapAction> CurrentPlan => _plan;
        public int PlanIndex => _planIndex;

        // Read by the [V] plan-search visualizer.
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

            // The world may have changed since we committed to this action.
            if (!State.Satisfies(_current.Preconditions))
            {
                Replan("precondition of '" + _current.Name + "' broke");
                return;
            }

            if (_phase == Phase.Moving)
                TickMoving();
            else
                TickPerforming();
        }

        private void TickMoving()
        {
            StatusLine = "Moving to " + _current.Name;

            Vector3 destination = _current.Target.position;
            destination.y = transform.position.y;
            transform.position = Vector3.MoveTowards(transform.position, destination, MoveSpeed * Time.deltaTime);

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

            foreach (KeyValuePair<string, bool> e in _current.Effects)
                State.Set(e.Key, e.Value);

            if (VerboseLogging)
                Debug.Log("[GOAP]   done '" + _current.Name + "'  -> " + Describe(_current.Effects));

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
                                     "'. World: " + Describe(State.Facts));
                    _loggedNoPlan = true;
                }
                _plan = null;
                return;
            }

            PlanningFailed = false;
            _loggedNoPlan = false;
            if (VerboseLogging)
                Debug.Log("[GOAP] Planned for goal '" + _activeGoal.Name + "': " +
                          PlanNames(plan) + "  (total cost " + TotalCost(plan) + ")");

            _plan = plan;
            _planIndex = -1;
            AdvancePlan();
        }

        private void AdvancePlan()
        {
            _planIndex++;
            if (_planIndex >= _plan.Count)
            {
                // Plan finished; go Idle so goals are re-evaluated next frame.
                _current = null;
                _phase = Phase.Idle;
                return;
            }

            _current = _plan[_planIndex];
            _phase = Phase.Moving;

            if (VerboseLogging)
                Debug.Log("[GOAP]   step " + (_planIndex + 1) + "/" + _plan.Count +
                          ": start '" + _current.Name + "' (cost " + _current.Cost +
                          ")  -> moving to " + _current.Target.name);
        }

        /// <summary>Drop the current plan; a new one is built on the next frame.</summary>
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
                    continue;
                if (best == null || g.Priority > best.Priority)
                    best = g;
            }
            return best;
        }

        private static string PlanNames(List<GoapAction> plan)
        {
            List<string> names = new List<string>(plan.Count);
            foreach (GoapAction a in plan)
                names.Add(a.Name);
            return string.Join(" -> ", names);
        }

        private static float TotalCost(List<GoapAction> plan)
        {
            float total = 0f;
            foreach (GoapAction a in plan)
                total += a.Cost;
            return total;
        }

        private static string Describe(Dictionary<string, bool> facts)
        {
            List<string> parts = new List<string>(facts.Count);
            foreach (KeyValuePair<string, bool> f in facts)
                parts.Add(f.Key + "=" + f.Value);
            return string.Join(", ", parts);
        }
    }
}

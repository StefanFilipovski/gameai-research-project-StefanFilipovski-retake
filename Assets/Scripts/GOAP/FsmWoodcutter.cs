using UnityEngine;

namespace GOAP
{
   
    public class FsmWoodcutter : MonoBehaviour
    {
        public float MoveSpeed = 3.5f;
        public bool VerboseLogging = true;

        public WorldState State;

        public Transform Shed, Grindstone, Tree, Sawmill, Stockpile;

        private const string HasAxe = "HasAxe";
        private const string AxeSharp = "AxeSharp";
        private const string HasLogs = "HasLogs";
        private const string HasPlanks = "HasPlanks";
        private const string AxeInShed = "AxeInShed";
        private const string PlanksDelivered = "PlanksDelivered";

        private enum Step
        {
            ToShed, GetAxe, ToGrindstone, Sharpen, ToTree, Chop,
            ToSawmill, Saw, ToStockpile, Deliver, WaitForNextOrder, Stuck
        }

        private Step _step;
        private float _timer;

        public string StatusLine { get; private set; }
        public bool IsStuck => _step == Step.Stuck;

        // Restart the script each time this brain is switched on.
        private void OnEnable()
        {
            _step = Step.ToShed;
            StatusLine = "FSM: go to the shed";
        }

        private void Update()
        {
            switch (_step)
            {
                case Step.ToShed:
                    if (MoveTo(Shed)) { _step = Step.GetAxe; _timer = 1.0f; Say("get axe from shed"); }
                    else Say("walk to shed");
                    break;

                case Step.GetAxe:
                    if (!Wait()) break;
                    if (State.Get(AxeInShed))
                    {
                        State.Set(HasAxe, true);
                        State.Set(AxeInShed, false);
                        _step = Step.ToGrindstone;
                        Say("go to the grindstone");
                    }
                    else GetStuck("shed is empty — no authored 'buy axe' transition exists");
                    break;

                case Step.ToGrindstone:
                    if (MoveTo(Grindstone)) { _step = Step.Sharpen; _timer = 1.0f; Say("sharpen the axe"); }
                    else Say("walk to grindstone");
                    break;

                case Step.Sharpen:
                    if (!RequireAxe()) break;
                    if (!Wait()) break;
                    State.Set(AxeSharp, true);
                    _step = Step.ToTree;
                    Say("go to the tree");
                    break;

                case Step.ToTree:
                    if (MoveTo(Tree)) { _step = Step.Chop; _timer = 1.5f; Say("chop logs"); }
                    else Say("walk to tree");
                    break;

                case Step.Chop:
                    if (!RequireAxe()) break;
                    if (!Wait()) break;
                    State.Set(HasLogs, true);
                    _step = Step.ToSawmill;
                    Say("go to the sawmill");
                    break;

                case Step.ToSawmill:
                    if (MoveTo(Sawmill)) { _step = Step.Saw; _timer = 1.5f; Say("saw planks"); }
                    else Say("walk to sawmill");
                    break;

                case Step.Saw:
                    if (!Wait()) break;
                    State.Set(HasPlanks, true);
                    State.Set(HasLogs, false);
                    _step = Step.ToStockpile;
                    Say("go to the stockpile");
                    break;

                case Step.ToStockpile:
                    if (MoveTo(Stockpile)) { _step = Step.Deliver; _timer = 1.0f; Say("deliver planks"); }
                    else Say("walk to stockpile");
                    break;

                case Step.Deliver:
                    if (!Wait()) break;
                    State.Set(PlanksDelivered, true);
                    State.Set(HasPlanks, false);
                    _step = Step.WaitForNextOrder;
                    Say("delivered — waiting for next order");
                    break;

                case Step.WaitForNextOrder:
                    // The demo clears PlanksDelivered when it issues the next order.
                    if (!State.Get(PlanksDelivered)) { _step = Step.ToShed; Say("go to the shed"); }
                    break;

                case Step.Stuck:
                    // No authored recovery
                    break;
            }
        }

        private bool Wait()
        {
            _timer -= Time.deltaTime;
            return _timer <= 0f;
        }

        private bool RequireAxe()
        {
            if (State.Get(HasAxe))
                return true;
            GetStuck("the axe is gone — FSM cannot replan to get another");
            return false;
        }

        private bool MoveTo(Transform target)
        {
            Vector3 destination = target.position;
            destination.y = transform.position.y;
            transform.position = Vector3.MoveTowards(transform.position, destination, MoveSpeed * Time.deltaTime);
            return Vector3.Distance(transform.position, destination) < 0.05f;
        }

        private void Say(string s)
        {
            StatusLine = "FSM: " + s;
        }

        private void GetStuck(string why)
        {
            _step = Step.Stuck;
            StatusLine = "FSM STUCK: " + why;
            if (VerboseLogging)
                Debug.LogWarning("[FSM] STUCK: " + why + ". A GOAP agent would replan here. Press [M] to compare.");
        }
    }
}

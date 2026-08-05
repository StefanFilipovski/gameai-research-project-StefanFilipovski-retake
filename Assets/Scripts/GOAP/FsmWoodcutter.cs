using UnityEngine;

namespace GOAP
{
    /// <summary>
    /// A deliberately hand-authored Finite State Machine that does the SAME job as the GOAP agent,
    /// so the two can be compared directly (toggle with [M] in the demo).
    ///
    /// The whole point of this class is to show GOAP's advantage by contrast. This FSM is wired for
    /// exactly one route — go to the shed, get the axe, chop, deliver — because that is the "happy
    /// path" a designer would author. It works perfectly as long as the world matches that script.
    ///
    /// But it has NO transition for "the shed is empty" or "my axe is gone", because nobody wired
    /// one. So the moment you steal the axe, it reaches a state it was never told how to handle and
    /// gets STUCK. A GOAP agent in the same situation simply re-plans onto the buy-axe branch — that
    /// branch exists automatically because the BuyAxe action exists, with no transitions to author.
    ///
    /// This is the classic trade-off: FSMs are cheap and predictable but brittle to unforeseen
    /// situations; GOAP spends a little search to stay adaptive.
    /// </summary>
    public class FsmWoodcutter : MonoBehaviour
    {
        public float MoveSpeed = 3.5f;
        public bool VerboseLogging = true;

        // Shared blackboard (the same WorldState the GOAP agent and the player interactions use).
        public WorldState State;

        // Job sites, assigned by the demo bootstrap.
        public Transform Shed, Tree, Stockpile;

        private const string HasAxe = "HasAxe";
        private const string HasWood = "HasWood";
        private const string AxeInShed = "AxeInShed";
        private const string WoodDelivered = "WoodDelivered";

        // The hand-authored states. Note there is no "BuyAxe" state anywhere — that is the point.
        private enum Step { ToShed, GetAxe, ToTree, Chop, ToStockpile, Deliver, WaitForNextOrder, Stuck }
        private Step _step = Step.ToShed;
        private float _timer;

        public string StatusLine { get; private set; } = "FSM idle";
        public bool IsStuck => _step == Step.Stuck;

        private void OnEnable()
        {
            // Restart the script whenever this brain becomes active.
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
                    _timer -= Time.deltaTime;
                    if (_timer > 0f) break;
                    // The FSM was told "the axe is in the shed". It has no fallback if it isn't.
                    if (State.Get(AxeInShed))
                    {
                        State.Set(HasAxe, true);
                        State.Set(AxeInShed, false);
                        _step = Step.ToTree;
                        Say("go to the tree");
                    }
                    else
                    {
                        GetStuck("shed is empty — no authored 'buy axe' transition exists");
                    }
                    break;

                case Step.ToTree:
                    if (MoveTo(Tree)) { _step = Step.Chop; _timer = 1.5f; Say("chop wood"); }
                    else Say("walk to tree");
                    break;

                case Step.Chop:
                    // If the axe was taken after we "got" it, the FSM is stranded here.
                    if (!State.Get(HasAxe)) { GetStuck("no axe at the tree — FSM cannot replan to get one"); break; }
                    _timer -= Time.deltaTime;
                    if (_timer > 0f) break;
                    State.Set(HasWood, true);
                    _step = Step.ToStockpile;
                    Say("go to the stockpile");
                    break;

                case Step.ToStockpile:
                    if (MoveTo(Stockpile)) { _step = Step.Deliver; _timer = 1.0f; Say("deliver wood"); }
                    else Say("walk to stockpile");
                    break;

                case Step.Deliver:
                    _timer -= Time.deltaTime;
                    if (_timer > 0f) break;
                    State.Set(WoodDelivered, true);
                    State.Set(HasWood, false);
                    _step = Step.WaitForNextOrder;
                    Say("delivered — waiting for next order");
                    break;

                case Step.WaitForNextOrder:
                    // The demo clears WoodDelivered and restocks the shed for the next order.
                    if (!State.Get(WoodDelivered)) { _step = Step.ToShed; Say("go to the shed"); }
                    break;

                case Step.Stuck:
                    // No authored recovery. This is the whole demonstration: switch to GOAP ([M])
                    // and the same situation is solved by re-planning.
                    break;
            }
        }

        private bool MoveTo(Transform target)
        {
            if (target == null) return true;
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

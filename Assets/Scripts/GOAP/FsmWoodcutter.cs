using UnityEngine;

namespace GOAP
{
    /// <summary>
    /// A hand-authored finite state machine doing the same job as the GOAP agent, for the [M]
    /// comparison. It is wired for one route — shed, tree, stockpile — which is what a designer
    /// would author. There is deliberately no "buy an axe" state, so when the shed is emptied it
    /// reaches a situation nobody anticipated and gets stuck; GOAP replans out of the same state.
    /// </summary>
    public class FsmWoodcutter : MonoBehaviour
    {
        public float MoveSpeed = 3.5f;
        public bool VerboseLogging = true;

        /// <summary>Shared with the GOAP agent, so player interactions affect whichever brain is active.</summary>
        public WorldState State;

        public Transform Shed, Tree, Stockpile;

        private const string HasAxe = "HasAxe";
        private const string HasWood = "HasWood";
        private const string AxeInShed = "AxeInShed";
        private const string WoodDelivered = "WoodDelivered";

        private enum Step { ToShed, GetAxe, ToTree, Chop, ToStockpile, Deliver, WaitForNextOrder, Stuck }
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
                    _timer -= Time.deltaTime;
                    if (_timer > 0f) break;
                    if (State.Get(AxeInShed))
                    {
                        State.Set(HasAxe, true);
                        State.Set(AxeInShed, false);
                        _step = Step.ToTree;
                        Say("go to the tree");
                    }
                    else GetStuck("shed is empty — no authored 'buy axe' transition exists");
                    break;

                case Step.ToTree:
                    if (MoveTo(Tree)) { _step = Step.Chop; _timer = 1.5f; Say("chop wood"); }
                    else Say("walk to tree");
                    break;

                case Step.Chop:
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
                    // The demo clears WoodDelivered when it issues the next order.
                    if (!State.Get(WoodDelivered)) { _step = Step.ToShed; Say("go to the shed"); }
                    break;

                case Step.Stuck:
                    // No authored recovery — that is the point of the comparison.
                    break;
            }
        }

        /// <summary>Walks toward the target; returns true once it has arrived.</summary>
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

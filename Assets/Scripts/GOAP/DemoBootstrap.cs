using System.Collections.Generic;
using UnityEngine;

namespace GOAP
{
    /// <summary>
    /// Builds and drives the whole demo from a single component so the project runs out of the
    /// box: create an empty scene, add one empty GameObject, attach this script, press Play.
    ///
    /// It constructs the ground, the four job sites, the agent, a camera and a light entirely in
    /// code, wires up the woodcutter's actions/goal, feeds keyboard interactions into the world
    /// state so you can watch the agent REPLAN live, and draws a HUD showing the current plan.
    ///
    /// Scenario — a woodcutter whose goal is to deliver wood to the stockpile:
    ///   GetAxeFromShed : needs an axe in the shed   -> gains an axe        (cheap)
    ///   BuyAxe         : needs gold                 -> gains an axe        (expensive)
    ///   ChopWood       : needs an axe               -> gains wood
    ///   DeliverWood    : needs wood                 -> wood delivered
    /// The planner (A*) prefers the cheapest way to get an axe. Empty the shed mid-walk and it
    /// re-plans onto the buy-axe branch instead.
    /// </summary>
    public class DemoBootstrap : MonoBehaviour
    {
        // Fact names kept as constants to avoid typos between actions, goals and interactions.
        private const string HasAxe = "HasAxe";
        private const string HasWood = "HasWood";
        private const string AxeInShed = "AxeInShed";
        private const string HasGold = "HasGold";
        private const string WoodDelivered = "WoodDelivered";

        private GoapAgent _agent;
        private float _resetTimer;
        private string _lastEvent = "";

        private void Start()
        {
            BuildEnvironment();
            BuildAgent();
        }

        // ---------------------------------------------------------------- world construction

        private void BuildEnvironment()
        {
            EnsureCameraAndLight();

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(2.4f, 1f, 2.4f);
            Colorize(ground, new Color(0.20f, 0.22f, 0.26f));

            _tree = MakeSite("Tree (chop here)", new Vector3(-6, 0.5f, 5), new Color(0.20f, 0.65f, 0.25f));
            _shed = MakeSite("Shed (free axe)", new Vector3(6, 0.5f, 5), new Color(0.55f, 0.38f, 0.20f));
            _shop = MakeSite("Shop (buy axe)", new Vector3(6, 0.5f, -5), new Color(0.25f, 0.45f, 0.85f));
            _stockpile = MakeSite("Stockpile (deliver)", new Vector3(-6, 0.5f, -5), new Color(0.85f, 0.75f, 0.20f));
        }

        private Transform _tree, _shed, _shop, _stockpile;

        private void EnsureCameraAndLight()
        {
            if (Camera.main == null)
            {
                GameObject camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                camGo.AddComponent<Camera>();
            }
            Camera cam = Camera.main;
            cam.transform.position = new Vector3(0f, 16f, -11f);
            cam.transform.rotation = Quaternion.Euler(54f, 0f, 0f);
            cam.backgroundColor = new Color(0.10f, 0.11f, 0.13f);
            cam.clearFlags = CameraClearFlags.SolidColor;

            if (FindObjectOfType<Light>() == null)
            {
                GameObject lightGo = new GameObject("Directional Light");
                Light l = lightGo.AddComponent<Light>();
                l.type = LightType.Directional;
                lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }
        }

        private Transform MakeSite(string name, Vector3 pos, Color color)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = pos;
            go.transform.localScale = new Vector3(1.4f, 1.4f, 1.4f);
            Colorize(go, color);
            return go.transform;
        }

        private void BuildAgent()
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "Woodcutter";
            go.transform.position = new Vector3(0f, 1f, 0f);
            Colorize(go, new Color(0.90f, 0.30f, 0.30f));

            _agent = go.AddComponent<GoapAgent>();

            // Starting world state: an axe is available in the shed, agent has some gold, nothing else.
            _agent.State.Set(AxeInShed, true);
            _agent.State.Set(HasGold, true);
            _agent.State.Set(HasAxe, false);
            _agent.State.Set(HasWood, false);
            _agent.State.Set(WoodDelivered, false);

            // Two competing ways to obtain an axe — the planner picks the cheaper one that is valid.
            _agent.Actions.Add(new GoapAction("GetAxeFromShed", 2f)
                .Pre(AxeInShed, true).Effect(HasAxe, true).Effect(AxeInShed, false)
                .At(_shed).TakesSeconds(1.0f));

            _agent.Actions.Add(new GoapAction("BuyAxe", 4f)
                .Pre(HasGold, true).Effect(HasAxe, true).Effect(HasGold, false)
                .At(_shop).TakesSeconds(1.0f));

            _agent.Actions.Add(new GoapAction("ChopWood", 3f)
                .Pre(HasAxe, true).Effect(HasWood, true)
                .At(_tree).TakesSeconds(1.5f));

            _agent.Actions.Add(new GoapAction("DeliverWood", 1f)
                .Pre(HasWood, true).Effect(WoodDelivered, true).Effect(HasWood, false)
                .At(_stockpile).TakesSeconds(1.0f));

            _agent.Goals.Add(new GoapGoal("DeliverWood", 5f).Want(WoodDelivered, true));
        }

        // ---------------------------------------------------------------- interaction + loop

        private void Update()
        {
            HandleInput();

            // Keep the demo running: once wood is delivered, pause briefly then reset the job so
            // the agent has something to plan for again.
            if (_agent.State.Get(WoodDelivered))
            {
                _resetTimer += Time.deltaTime;
                if (_resetTimer > 1.5f)
                {
                    _resetTimer = 0f;
                    _agent.State.Set(WoodDelivered, false);
                    _agent.State.Set(HasWood, false);
                    _lastEvent = "New order: deliver more wood";
                }
            }
            else
            {
                _resetTimer = 0f;
            }
        }

        private void HandleInput()
        {
            if (Input.GetKeyDown(KeyCode.S))
            {
                // Steal the axe and empty the shed: forces the agent onto the buy-axe branch.
                _agent.State.Set(HasAxe, false);
                _agent.State.Set(AxeInShed, false);
                _lastEvent = "You stole the axe and emptied the shed!";
                _agent.Replan("world changed by player");
            }
            else if (Input.GetKeyDown(KeyCode.R))
            {
                _agent.State.Set(AxeInShed, true);
                _lastEvent = "Shed restocked with an axe";
                _agent.Replan("world changed by player");
            }
            else if (Input.GetKeyDown(KeyCode.G))
            {
                _agent.State.Set(HasGold, true);
                _lastEvent = "Gave the agent gold";
                _agent.Replan("world changed by player");
            }
            else if (Input.GetKeyDown(KeyCode.B))
            {
                _agent.State.Set(HasAxe, false);
                _lastEvent = "The axe broke!";
                _agent.Replan("world changed by player");
            }
        }

        // ---------------------------------------------------------------- helpers + HUD

        private static void Colorize(GameObject go, Color color)
        {
            // Works with the Built-in Render Pipeline's Standard shader.
            Renderer r = go.GetComponent<Renderer>();
            if (r != null)
                r.material.color = color;
        }

        private GUIStyle _h1, _body, _tag;

        private void OnGUI()
        {
            if (_h1 == null)
            {
                _h1 = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
                _body = new GUIStyle(GUI.skin.label) { fontSize = 15 };
                _tag = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter };
                _tag.normal.textColor = Color.white;
            }

            DrawWorldLabels();

            GUILayout.BeginArea(new Rect(12, 12, 360, Screen.height - 24));

            GUILayout.Label("GOAP — Woodcutter", _h1);
            GUILayout.Space(4);
            GUILayout.Label("Goal: " + (_agent.ActiveGoal != null ? _agent.ActiveGoal.Name : "-"), _body);
            GUILayout.Label("Status: " + _agent.StatusLine, _body);

            GUILayout.Space(8);
            GUILayout.Label("Plan (A* over world-states):", _body);
            IReadOnlyList<GoapAction> plan = _agent.CurrentPlan;
            if (plan == null || plan.Count == 0)
            {
                GUILayout.Label("   (no plan)", _body);
            }
            else
            {
                for (int i = 0; i < plan.Count; i++)
                {
                    string marker = i == _agent.PlanIndex ? " > " : "   ";
                    GUILayout.Label(marker + plan[i].Name + "  (cost " + plan[i].Cost + ")", _body);
                }
            }

            GUILayout.Space(8);
            GUILayout.Label("World state:", _body);
            GUILayout.Label("   HasAxe=" + _agent.State.Get(HasAxe)
                          + "   HasWood=" + _agent.State.Get(HasWood), _body);
            GUILayout.Label("   AxeInShed=" + _agent.State.Get(AxeInShed)
                          + "   HasGold=" + _agent.State.Get(HasGold), _body);

            GUILayout.Space(8);
            GUILayout.Label("Interact:", _body);
            GUILayout.Label("   [S] steal axe + empty shed", _body);
            GUILayout.Label("   [R] restock shed   [G] give gold", _body);
            GUILayout.Label("   [B] break the agent's axe", _body);

            if (!string.IsNullOrEmpty(_lastEvent))
            {
                GUILayout.Space(8);
                GUILayout.Label("Last event: " + _lastEvent, _body);
            }

            GUILayout.EndArea();
        }

        private void DrawWorldLabels()
        {
            LabelWorld(_tree, "TREE");
            LabelWorld(_shed, "SHED");
            LabelWorld(_shop, "SHOP");
            LabelWorld(_stockpile, "STOCKPILE");
            if (_agent != null)
                LabelWorld(_agent.transform, "AGENT");
        }

        private void LabelWorld(Transform t, string text)
        {
            if (t == null || Camera.main == null)
                return;
            Vector3 sp = Camera.main.WorldToScreenPoint(t.position + Vector3.up * 1.6f);
            if (sp.z < 0f)
                return; // behind the camera
            Rect r = new Rect(sp.x - 50f, Screen.height - sp.y - 12f, 100f, 24f);
            GUI.Label(r, text, _tag);
        }
    }
}

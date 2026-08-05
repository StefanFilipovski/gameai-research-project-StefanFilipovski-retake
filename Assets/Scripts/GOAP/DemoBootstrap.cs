using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

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

            if (FindFirstObjectByType<Light>() == null)
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

            // Keep the demo running: once wood is delivered, pause briefly then issue a new order.
            // We reset the axe too (the tool is "returned" to the shed) and replenish supplies, so
            // every cycle shows the full fetch-axe -> chop -> deliver route and the agent can always
            // form a plan (no accidental dead-ends from spent gold / empty shed).
            if (_agent.State.Get(WoodDelivered))
            {
                _resetTimer += Time.deltaTime;
                if (_resetTimer > 0.6f)
                {
                    _resetTimer = 0f;
                    _agent.State.Set(WoodDelivered, false);
                    _agent.State.Set(HasWood, false);
                    _agent.State.Set(HasAxe, false);
                    _agent.State.Set(AxeInShed, true);
                    _agent.State.Set(HasGold, true);
                    _lastEvent = "New order: fetch an axe, chop, and deliver";
                    Debug.Log("[GOAP][world] New order issued (axe reset, shed restocked, gold given)");
                }
            }
            else
            {
                _resetTimer = 0f;
            }
        }

        // Reads keys from whichever input backend the project uses. The new Input System is
        // preferred when present (Unity 6 default); it falls back to the legacy Input Manager
        // otherwise. This means the demo runs with no Player Settings changes required.
        private void HandleInput()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.sKey.wasPressedThisFrame) StealAxe();
                else if (kb.rKey.wasPressedThisFrame) RestockShed();
                else if (kb.gKey.wasPressedThisFrame) GiveGold();
                else if (kb.bKey.wasPressedThisFrame) BreakAxe();
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.S)) StealAxe();
            else if (Input.GetKeyDown(KeyCode.R)) RestockShed();
            else if (Input.GetKeyDown(KeyCode.G)) GiveGold();
            else if (Input.GetKeyDown(KeyCode.B)) BreakAxe();
#endif
        }

        // Steal the axe and empty the shed: forces the agent onto the buy-axe branch.
        private void StealAxe()
        {
            _agent.State.Set(HasAxe, false);
            _agent.State.Set(AxeInShed, false);
            _lastEvent = "You stole the axe and emptied the shed!";
            Debug.Log("[GOAP][player] S: stole axe + emptied shed (HasAxe=false, AxeInShed=false)");
            _agent.Replan("player stole axe / emptied shed");
        }

        private void RestockShed()
        {
            _agent.State.Set(AxeInShed, true);
            _lastEvent = "Shed restocked with an axe";
            Debug.Log("[GOAP][player] R: restocked shed (AxeInShed=true)");
            _agent.Replan("player restocked shed");
        }

        private void GiveGold()
        {
            _agent.State.Set(HasGold, true);
            _lastEvent = "Gave the agent gold";
            Debug.Log("[GOAP][player] G: gave gold (HasGold=true)");
            _agent.Replan("player gave gold");
        }

        private void BreakAxe()
        {
            _agent.State.Set(HasAxe, false);
            _lastEvent = "The axe broke!";
            Debug.Log("[GOAP][player] B: broke the axe (HasAxe=false)");
            _agent.Replan("player broke the axe");
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
        private Texture2D _panelTex;

        private void OnGUI()
        {
            // Scale the HUD up on high-resolution screens so the text stays readable.
            float hudScale = Mathf.Max(1f, Screen.height / 720f);

            if (_h1 == null)
            {
                _h1 = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(26 * hudScale), fontStyle = FontStyle.Bold };
                _h1.normal.textColor = Color.white;
                _body = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(18 * hudScale) };
                _body.normal.textColor = Color.white;
                _tag = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(15 * hudScale), alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                _tag.normal.textColor = Color.white;

                _panelTex = new Texture2D(1, 1);
                _panelTex.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.65f));
                _panelTex.Apply();
            }

            DrawWorldLabels();

            const int hudLines = 16;                 // roughly how many text rows the HUD draws
            float lineHeight = 26f * hudScale;       // ~ body font size * line spacing
            float panelW = Mathf.Max(380f, Screen.width * 0.30f);
            float panelH = Mathf.Min(Screen.height - 24f, 30f + hudLines * lineHeight);
            Rect panelRect = new Rect(12, 12, panelW, panelH);
            GUI.DrawTexture(panelRect, _panelTex);

            GUILayout.BeginArea(new Rect(panelRect.x + 14, panelRect.y + 12, panelRect.width - 28, panelRect.height - 24));

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
            Vector3 sp = Camera.main.WorldToScreenPoint(t.position + Vector3.up * 1.9f);
            if (sp.z < 0f)
                return; // behind the camera

            // Size the box to the actual text so nothing is clipped, and draw a dark chip
            // behind it so the white label is readable over any colour underneath.
            GUIContent gc = new GUIContent(text);
            Vector2 size = _tag.CalcSize(gc);
            float padX = 8f, padY = 4f;
            Rect box = new Rect(sp.x - size.x / 2f - padX,
                                Screen.height - sp.y - size.y / 2f - padY,
                                size.x + padX * 2f,
                                size.y + padY * 2f);
            GUI.DrawTexture(box, _panelTex);
            GUI.Label(box, gc, _tag);
        }
    }
}

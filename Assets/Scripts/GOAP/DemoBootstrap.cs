using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace GOAP
{
    /// <summary>
    /// Builds and drives the whole demo from one component: attach it to an empty GameObject and
    /// press Play. It creates the scene, defines the woodcutter's actions and goal, applies the
    /// player's interactions to the world state, and draws the HUD and the plan-search visualizer.
    ///
    /// Scenario — goal is WoodDelivered:
    ///   GetAxeFromShed (needs an axe in the shed)  -> has axe   cost 2
    ///   BuyAxe         (needs gold)                -> has axe   cost 4
    ///   ChopWood       (needs an axe)              -> has wood  cost 3
    ///   DeliverWood    (needs wood)                -> delivered cost 1
    /// The planner prefers the shed (total 6) over buying (total 8), and re-plans onto the buy
    /// branch if the shed is emptied mid-task.
    /// </summary>
    public class DemoBootstrap : MonoBehaviour
    {
        // Fact names as constants so actions, goals and interactions cannot disagree by typo.
        private const string HasAxe = "HasAxe";
        private const string HasWood = "HasWood";
        private const string AxeInShed = "AxeInShed";
        private const string HasGold = "HasGold";
        private const string WoodDelivered = "WoodDelivered";

        private GoapAgent _agent;
        private FsmWoodcutter _fsm;
        private bool _useGoap = true;   // which brain drives the woodcutter ([M])
        private bool _showSearch;       // plan-search visualizer ([V])
        private float _resetTimer;
        private string _lastEvent = "";

        private Transform _tree, _shed, _shop, _stockpile;

        private void Start()
        {
            BuildEnvironment();
            BuildAgent();
        }

        // ---------------------------------------------------------------- scene construction

        private void BuildEnvironment()
        {
            SetUpCameraAndLight();

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(2.4f, 1f, 2.4f);
            Colorize(ground, new Color(0.20f, 0.22f, 0.26f));

            _tree = MakeSite("Tree", new Vector3(-6, 0.5f, 5), new Color(0.20f, 0.65f, 0.25f));
            _shed = MakeSite("Shed", new Vector3(6, 0.5f, 5), new Color(0.55f, 0.38f, 0.20f));
            _shop = MakeSite("Shop", new Vector3(6, 0.5f, -5), new Color(0.25f, 0.45f, 0.85f));
            _stockpile = MakeSite("Stockpile", new Vector3(-6, 0.5f, -5), new Color(0.85f, 0.75f, 0.20f));
        }

        private void SetUpCameraAndLight()
        {
            if (Camera.main == null)
            {
                GameObject camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                camGo.AddComponent<Camera>();
            }

            // Fixed overhead view of the whole site; the visualizer assumes it does not move.
            Camera cam = Camera.main;
            cam.transform.position = new Vector3(0f, 16f, -11f);
            cam.transform.rotation = Quaternion.Euler(54f, 0f, 0f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.10f, 0.11f, 0.13f);

            if (FindFirstObjectByType<Light>() == null)
            {
                GameObject lightGo = new GameObject("Directional Light");
                lightGo.AddComponent<Light>().type = LightType.Directional;
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

            _agent.State.Set(AxeInShed, true);
            _agent.State.Set(HasGold, true);
            _agent.State.Set(HasAxe, false);
            _agent.State.Set(HasWood, false);
            _agent.State.Set(WoodDelivered, false);

            // Two ways to get an axe; the planner picks whichever valid route is cheapest.
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
            _agent.RecordSearch = true; // keep the A* search so [V] can draw it

            // The FSM brain shares the same world state and starts disabled; GOAP drives by default.
            _fsm = go.AddComponent<FsmWoodcutter>();
            _fsm.State = _agent.State;
            _fsm.Shed = _shed;
            _fsm.Tree = _tree;
            _fsm.Stockpile = _stockpile;
            _fsm.MoveSpeed = _agent.MoveSpeed;
            _fsm.enabled = false;
        }

        // ---------------------------------------------------------------- interaction + loop

        private void Update()
        {
            HandleInput();

            // Issue a fresh order shortly after each delivery, restocking supplies so every cycle
            // shows the full fetch-axe -> chop -> deliver route.
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

        // Reads the new Input System when present (Unity 6 default) and the legacy Input Manager
        // otherwise, so the demo runs without changing Player Settings.
        private void HandleInput()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            if (kb == null)
                return;
            if (kb.sKey.wasPressedThisFrame) StealAxe();
            else if (kb.rKey.wasPressedThisFrame) RestockShed();
            else if (kb.gKey.wasPressedThisFrame) GiveGold();
            else if (kb.bKey.wasPressedThisFrame) BreakAxe();
            else if (kb.mKey.wasPressedThisFrame) ToggleBrain();
            else if (kb.vKey.wasPressedThisFrame) _showSearch = !_showSearch;
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.S)) StealAxe();
            else if (Input.GetKeyDown(KeyCode.R)) RestockShed();
            else if (Input.GetKeyDown(KeyCode.G)) GiveGold();
            else if (Input.GetKeyDown(KeyCode.B)) BreakAxe();
            else if (Input.GetKeyDown(KeyCode.M)) ToggleBrain();
            else if (Input.GetKeyDown(KeyCode.V)) _showSearch = !_showSearch;
#endif
        }

        private void StealAxe()
        {
            _agent.State.Set(HasAxe, false);
            _agent.State.Set(AxeInShed, false);
            _lastEvent = "You stole the axe and emptied the shed!";
            Debug.Log("[GOAP][player] S: stole axe + emptied shed");
            ReplanIfGoap("player stole axe / emptied shed");
        }

        private void RestockShed()
        {
            _agent.State.Set(AxeInShed, true);
            _lastEvent = "Shed restocked with an axe";
            Debug.Log("[GOAP][player] R: restocked shed");
            ReplanIfGoap("player restocked shed");
        }

        private void GiveGold()
        {
            _agent.State.Set(HasGold, true);
            _lastEvent = "Gave the agent gold";
            Debug.Log("[GOAP][player] G: gave gold");
            ReplanIfGoap("player gave gold");
        }

        private void BreakAxe()
        {
            _agent.State.Set(HasAxe, false);
            _lastEvent = "The axe broke!";
            Debug.Log("[GOAP][player] B: broke the axe");
            ReplanIfGoap("player broke the axe");
        }

        // Only GOAP plans ahead; the FSM just reads the shared state on its next step.
        private void ReplanIfGoap(string reason)
        {
            if (_useGoap)
                _agent.Replan(reason);
        }

        // Swaps brains in place, leaving the world state and position alone, so the incoming brain
        // inherits the exact situation the other one was in.
        private void ToggleBrain()
        {
            _useGoap = !_useGoap;
            _agent.enabled = _useGoap;
            _fsm.enabled = !_useGoap;

            if (_useGoap)
                _agent.Replan("switched to GOAP brain — solving the current world state");

            _lastEvent = _useGoap
                ? "Brain: GOAP takes over the CURRENT situation and re-plans"
                : "Brain: hand-authored FSM (fixed route, no planning)";
            Debug.Log("[GOAP][brain] Switched to " + (_useGoap ? "GOAP" : "FSM"));
        }

        private static void Colorize(GameObject go, Color color)
        {
            // Primitives ship with a renderer using the Built-in pipeline's Standard shader.
            go.GetComponent<Renderer>().material.color = color;
        }

        // ---------------------------------------------------------------- HUD

        private GUIStyle _h1, _body, _tag, _nodeTitle, _small, _smallWrap, _warn;
        private Texture2D _panelTex, _whiteTex;

        private void OnGUI()
        {
            float hudScale = Mathf.Max(1f, Screen.height / 720f); // keep text readable on tall screens
            if (_h1 == null)
                BuildGuiStyles(hudScale);

            DrawWorldLabels();

            const int hudRows = 17; // text rows the panel must fit
            float panelW = Mathf.Max(380f, Screen.width * 0.30f);
            float panelH = Mathf.Min(Screen.height - 24f, 30f + hudRows * 26f * hudScale);
            Rect panelRect = new Rect(12, 12, panelW, panelH);
            GUI.DrawTexture(panelRect, _panelTex);

            GUILayout.BeginArea(new Rect(panelRect.x + 14, panelRect.y + 12,
                                         panelRect.width - 28, panelRect.height - 24));

            GUILayout.Label("GOAP vs FSM — Woodcutter", _h1);
            GUILayout.Space(4);
            GUILayout.Label("Brain: " + (_useGoap ? "GOAP (plans with A*)" : "Hand-authored FSM"), _body);

            if (_useGoap)
                DrawGoapStatus();
            else
                DrawFsmStatus();

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
            GUILayout.Label("   [M] brain: " + (_useGoap ? "GOAP" : "FSM") + "  (switch)", _body);
            GUILayout.Label("   [V] plan-search tree: " + (_showSearch ? "ON" : "OFF"), _body);

            if (_lastEvent.Length > 0)
            {
                GUILayout.Space(8);
                GUILayout.Label("Last event: " + _lastEvent, _body);
            }

            GUILayout.EndArea();

            if (_showSearch)
                DrawSearchPanel(panelRect.xMax, hudScale);
        }

        private void BuildGuiStyles(float scale)
        {
            _h1 = Label(26, scale, Color.white, FontStyle.Bold);
            _body = Label(18, scale, Color.white);
            _tag = Label(15, scale, Color.white, FontStyle.Bold);
            _tag.alignment = TextAnchor.MiddleCenter;
            _nodeTitle = Label(14, scale, Color.white, FontStyle.Bold);
            _small = Label(12, scale, new Color(0.88f, 0.88f, 0.88f));
            _smallWrap = new GUIStyle(_small) { wordWrap = true };
            _warn = Label(16, scale, new Color(1f, 0.82f, 0.25f));
            _warn.wordWrap = true;

            _panelTex = SolidTexture(new Color(0f, 0f, 0f, 0.65f));
            _whiteTex = SolidTexture(Color.white);
        }

        private static GUIStyle Label(int size, float scale, Color color, FontStyle style = FontStyle.Normal)
        {
            GUIStyle s = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(size * scale),
                fontStyle = style
            };
            s.normal.textColor = color;
            return s;
        }

        private static Texture2D SolidTexture(Color color)
        {
            Texture2D t = new Texture2D(1, 1);
            t.SetPixel(0, 0, color);
            t.Apply();
            return t;
        }

        private void DrawGoapStatus()
        {
            GUILayout.Label("Goal: " + (_agent.ActiveGoal != null ? _agent.ActiveGoal.Name : "-"), _body);
            GUILayout.Label("Status: " + _agent.StatusLine, _body);

            GUILayout.Space(8);
            GUILayout.Label("Plan (A* over world-states):", _body);

            IReadOnlyList<GoapAction> plan = _agent.CurrentPlan;
            if (plan == null)
            {
                // The only way this scenario has no plan: no wood, no axe, empty shed and no gold.
                if (_agent.PlanningFailed)
                    GUILayout.Label("No plan: can't get an axe — the shed is empty and there's no gold. " +
                                    "Press [R] to restock the shed or [G] to give gold.", _warn);
                else
                    GUILayout.Label("   (goal satisfied — awaiting the next order)", _body);
                return;
            }

            for (int i = 0; i < plan.Count; i++)
            {
                string marker = i == _agent.PlanIndex ? " > " : "   ";
                GUILayout.Label(marker + plan[i].Name + "  (cost " + plan[i].Cost + ")", _body);
            }
        }

        private void DrawFsmStatus()
        {
            GUILayout.Label("Status: " + _fsm.StatusLine, _fsm.IsStuck ? _warn : _body);
            GUILayout.Space(8);
            GUILayout.Label("Route: hard-coded  shed -> tree -> stockpile", _body);
            GUILayout.Label("No planning, no buy-axe fallback wired.", _body);
            if (_fsm.IsStuck)
                GUILayout.Label("Press [M] to hand the same situation to GOAP.", _warn);
        }

        private void DrawWorldLabels()
        {
            LabelWorld(_tree, "TREE");
            LabelWorld(_shed, "SHED");
            LabelWorld(_shop, "SHOP");
            LabelWorld(_stockpile, "STOCKPILE");
            LabelWorld(_agent.transform, "AGENT");
        }

        // Draws a name above a world object, sized to its text so nothing is clipped.
        private void LabelWorld(Transform t, string text)
        {
            Vector3 sp = Camera.main.WorldToScreenPoint(t.position + Vector3.up * 1.9f);
            GUIContent gc = new GUIContent(text);
            Vector2 size = _tag.CalcSize(gc);
            const float padX = 8f, padY = 4f;
            Rect box = new Rect(sp.x - size.x / 2f - padX,
                                Screen.height - sp.y - size.y / 2f - padY,
                                size.x + padX * 2f,
                                size.y + padY * 2f);
            GUI.DrawTexture(box, _panelTex);
            GUI.Label(box, gc, _tag);
        }

        // ---------------------------------------------------------------- plan-search visualizer

        private static readonly Color ColGoal = new Color(0.35f, 0.95f, 0.45f);
        private static readonly Color ColPlan = new Color(0.20f, 0.70f, 0.30f);
        private static readonly Color ColExpanded = new Color(0.30f, 0.55f, 0.95f);
        private static readonly Color ColFrontier = new Color(0.50f, 0.50f, 0.55f);

        /// <summary>
        /// Draws the planner's last A* search: one box per world-state it generated, columns by
        /// search depth, edges are actions, and the chosen plan highlighted. The state-space
        /// equivalent of a grid pathfinding debug view.
        /// </summary>
        private void DrawSearchPanel(float leftX, float hudScale)
        {
            Rect area = new Rect(leftX + 12f, 12f, Screen.width - leftX - 24f, Screen.height - 24f);
            if (area.width < 260f)
                return; // not enough room to be readable

            FillRect(area, new Color(0f, 0f, 0f, 0.55f));
            GUI.Label(new Rect(area.x + 12f, area.y + 8f, area.width - 24f, 30f * hudScale),
                      "Plan search — A* over world-states", _body);

            Rect textRect = new Rect(area.x + 12f, area.y + 42f * hudScale, area.width - 24f, 48f * hudScale);
            if (!_useGoap)
            {
                GUI.Label(textRect, "The FSM does not search — it follows a fixed route. " +
                                    "Press [M] to switch to the GOAP brain.", _smallWrap);
                return;
            }

            IReadOnlyList<GoapPlanner.SearchNode> trace = _agent.LastSearch;
            if (trace == null)
            {
                GUI.Label(textRect, "(no search recorded yet)", _smallWrap);
                return;
            }

            // Lay out: one column per depth, nodes stacked in rows within their column.
            int maxDepth = 0;
            foreach (GoapPlanner.SearchNode n in trace)
                if (n.Depth > maxDepth)
                    maxDepth = n.Depth;

            float top = area.y + 44f * hudScale;
            float colW = (area.width - 24f) / (maxDepth + 1);
            float nodeW = Mathf.Min(colW - 14f, 220f * hudScale);
            float nodeH = 58f * hudScale;
            float rowGap = 12f * hudScale;

            Dictionary<int, Rect> rectById = new Dictionary<int, Rect>();
            Dictionary<int, int> rowsUsed = new Dictionary<int, int>();
            foreach (GoapPlanner.SearchNode n in trace)
            {
                int row = rowsUsed.TryGetValue(n.Depth, out int used) ? used : 0;
                rowsUsed[n.Depth] = row + 1;
                rectById[n.Id] = new Rect(area.x + 12f + n.Depth * colW,
                                          top + row * (nodeH + rowGap),
                                          nodeW, nodeH);
            }

            // Edges first so the node boxes sit on top of them.
            foreach (GoapPlanner.SearchNode n in trace)
            {
                if (n.ParentId < 0)
                    continue;
                Rect parent = rectById[n.ParentId];
                Rect child = rectById[n.Id];
                GuiLine(new Vector2(parent.xMax, parent.center.y),
                        new Vector2(child.x, child.center.y),
                        n.OnFinalPlan ? 3f : 1.5f,
                        n.OnFinalPlan ? ColPlan : new Color(1f, 1f, 1f, 0.25f));
            }

            foreach (GoapPlanner.SearchNode n in trace)
                DrawSearchNode(rectById[n.Id], n, hudScale);

            float legendY = area.yMax - 26f * hudScale;
            DrawLegendChip(area.x + 12f, legendY, hudScale, ColPlan, "chosen plan");
            DrawLegendChip(area.x + 12f + 150f * hudScale, legendY, hudScale, ColExpanded, "expanded");
            DrawLegendChip(area.x + 12f + 290f * hudScale, legendY, hudScale, ColFrontier, "frontier");
            DrawLegendChip(area.x + 12f + 420f * hudScale, legendY, hudScale, ColGoal, "goal reached");
        }

        private void DrawSearchNode(Rect r, GoapPlanner.SearchNode n, float hudScale)
        {
            Color border = n.SatisfiesGoal ? ColGoal
                         : n.OnFinalPlan ? ColPlan
                         : n.ExpandedOrder >= 0 ? ColExpanded
                         : ColFrontier;

            FillRect(new Rect(r.x - 2f, r.y - 2f, r.width + 4f, r.height + 4f), border); // border
            FillRect(r, new Color(0.06f, 0.06f, 0.08f, 0.96f));                          // body

            const float pad = 8f;
            float lineH = 18f * hudScale;
            string title = n.ActionName ?? "START";
            if (n.SatisfiesGoal)
                title += "  (GOAL)";

            GUI.Label(new Rect(r.x + pad, r.y + 3f, r.width - pad * 2f, 20f * hudScale), title, _nodeTitle);
            GUI.Label(new Rect(r.x + pad, r.y + 3f + lineH, r.width - pad * 2f, lineH),
                      "f=" + n.F.ToString("0.#") + "  g=" + n.G.ToString("0.#") + "  h=" + n.H.ToString("0.#") +
                      "   " + (n.ExpandedOrder >= 0 ? "expanded #" + n.ExpandedOrder : "frontier"), _small);
            GUI.Label(new Rect(r.x + pad, r.y + 3f + lineH * 2f, r.width - pad * 2f, lineH), n.TrueFacts, _small);
        }

        private void DrawLegendChip(float x, float y, float scale, Color color, string label)
        {
            FillRect(new Rect(x, y, 14f * scale, 14f * scale), color);
            GUI.Label(new Rect(x + 20f * scale, y - 2f * scale, 140f * scale, 20f * scale), label, _small);
        }

        private void FillRect(Rect r, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(r, _whiteTex);
            GUI.color = previous;
        }

        // IMGUI has no line primitive, so stretch a 1x1 texture and rotate it into place.
        private void GuiLine(Vector2 a, Vector2 b, float width, Color color)
        {
            Matrix4x4 saved = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg, a);
            FillRect(new Rect(a.x, a.y - width / 2f, Vector2.Distance(a, b), width), color);
            GUI.matrix = saved;
        }
    }
}

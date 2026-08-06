using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace GOAP
{
   
    public class DemoBootstrap : MonoBehaviour
    {
        private const string HasAxe = "HasAxe";
        private const string AxeSharp = "AxeSharp";
        private const string HasLogs = "HasLogs";
        private const string HasPlanks = "HasPlanks";
        private const string AxeInShed = "AxeInShed";
        private const string HasGold = "HasGold";
        private const string PlanksDelivered = "PlanksDelivered";

        private GoapAgent _agent;
        private FsmWoodcutter _fsm;
        private bool _useGoap = true;   // which brain drives the woodcutter ([M])
        private bool _showSearch;       // plan-search visualizer ([V])
        private float _resetTimer;
        private string _lastEvent = "";

        private Transform _tree, _shed, _shop, _stockpile, _grindstone, _sawmill;

        private void Start()
        {
            BuildEnvironment();
            BuildAgent();
        }

        //scene construction

        private void BuildEnvironment()
        {
            SetUpCameraAndLight();

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(2.4f, 1f, 2.4f);
            Colorize(ground, new Color(0.20f, 0.22f, 0.26f));

            // Laid out roughly in the order the cheapest plan visits them.
            _shed = MakeSite("Shed", new Vector3(8, 0.5f, 6), new Color(0.55f, 0.38f, 0.20f));
            _grindstone = MakeSite("Grindstone", new Vector3(0, 0.5f, 8), new Color(0.60f, 0.60f, 0.66f));
            _tree = MakeSite("Tree", new Vector3(-8, 0.5f, 6), new Color(0.20f, 0.65f, 0.25f));
            _sawmill = MakeSite("Sawmill", new Vector3(-8, 0.5f, -2), new Color(0.75f, 0.45f, 0.75f));
            _stockpile = MakeSite("Stockpile", new Vector3(-4, 0.5f, -8), new Color(0.85f, 0.75f, 0.20f));
            _shop = MakeSite("Shop", new Vector3(8, 0.5f, -4), new Color(0.25f, 0.45f, 0.85f));
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

            ResetForNewOrder();

            // Two ways to get an axe; the planner picks whichever valid route is cheapest.
            _agent.Actions.Add(new GoapAction("GetAxeFromShed", 2f)
                .Pre(AxeInShed, true).Effect(HasAxe, true).Effect(AxeInShed, false)
                .At(_shed).TakesSeconds(1.0f));

            _agent.Actions.Add(new GoapAction("BuyAxe", 4f)
                .Pre(HasGold, true).Effect(HasAxe, true).Effect(HasGold, false)
                .At(_shop).TakesSeconds(1.0f));

            // The rest of the chain: each action's effect unlocks the next one's precondition.
            _agent.Actions.Add(new GoapAction("SharpenAxe", 1f)
                .Pre(HasAxe, true).Effect(AxeSharp, true)
                .At(_grindstone).TakesSeconds(1.0f));

            _agent.Actions.Add(new GoapAction("ChopLogs", 3f)
                .Pre(HasAxe, true).Pre(AxeSharp, true).Effect(HasLogs, true)
                .At(_tree).TakesSeconds(1.5f));

            _agent.Actions.Add(new GoapAction("SawPlanks", 2f)
                .Pre(HasLogs, true).Effect(HasPlanks, true).Effect(HasLogs, false)
                .At(_sawmill).TakesSeconds(1.5f));

            _agent.Actions.Add(new GoapAction("DeliverPlanks", 1f)
                .Pre(HasPlanks, true).Effect(PlanksDelivered, true).Effect(HasPlanks, false)
                .At(_stockpile).TakesSeconds(1.0f));

            _agent.Goals.Add(new GoapGoal("DeliverPlanks", 5f).Want(PlanksDelivered, true));
            _agent.RecordSearch = true; 

            // The FSM brain shares the same world state and starts disabled; GOAP drives by default.
            _fsm = go.AddComponent<FsmWoodcutter>();
            _fsm.State = _agent.State;
            _fsm.Shed = _shed;
            _fsm.Grindstone = _grindstone;
            _fsm.Tree = _tree;
            _fsm.Sawmill = _sawmill;
            _fsm.Stockpile = _stockpile;
            _fsm.MoveSpeed = _agent.MoveSpeed;
            _fsm.enabled = false;
        }

        // interaction + loop

        private void Update()
        {
            HandleInput();

            // Issue a fresh order shortly after each delivery, restocking supplies so every cycle
            // shows the full five-action route.
            if (_agent.State.Get(PlanksDelivered))
            {
                _resetTimer += Time.deltaTime;
                if (_resetTimer > 0.6f)
                {
                    _resetTimer = 0f;
                    ResetForNewOrder();
                    _lastEvent = "New order: fetch an axe, sharpen, chop, saw, and deliver";
                    Debug.Log("[GOAP][world] New order issued (tools returned, shed restocked, gold given)");
                }
            }
            else
            {
                _resetTimer = 0f;
            }
        }

        // The starting situation, also used to begin each new order.
        private void ResetForNewOrder()
        {
            _agent.State.Set(AxeInShed, true);
            _agent.State.Set(HasGold, true);
            _agent.State.Set(HasAxe, false);
            _agent.State.Set(AxeSharp, false);
            _agent.State.Set(HasLogs, false);
            _agent.State.Set(HasPlanks, false);
            _agent.State.Set(PlanksDelivered, false);
        }

       
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
            _agent.State.Set(AxeSharp, false); // a replacement axe will need sharpening again
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
            _agent.State.Set(AxeSharp, false);
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
          
            go.GetComponent<Renderer>().material.color = color;
        }

        // HUD

        private enum HudText { Title, Body, Small, Warn }

        private struct HudLine
        {
            public string Text;
            public HudText Kind;
            public float GapBefore;
        }

        private class HudStyles
        {
            public GUIStyle Title, Body, Small, Warn, Tag, NodeTitle;
        }

        private readonly List<HudLine> _lines = new List<HudLine>();
        private HudStyles _styles;     
        private HudStyles _reference;  
        private float _appliedScale = -1f;
        private Texture2D _panelTex, _whiteTex;

        private void OnGUI()
        {
            if (_reference == null)
            {
                _reference = MakeStyles(1f);
                _panelTex = SolidTexture(new Color(0f, 0f, 0f, 0.65f));
                _whiteTex = SolidTexture(Color.white);
            }

            BuildHudLines();

            float panelW = Mathf.Clamp(Screen.width * 0.30f, 400f, Screen.width * 0.5f);
            float innerW = panelW - 28f;
            float available = Screen.height - 48f; 

          
            float natural = MeasureHud(_reference, innerW);
            float scale = Mathf.Min(Screen.height / 720f, available / natural);
            EnsureStyles(Mathf.Max(scale, 0.7f));

            DrawWorldLabels();

            float contentH = MeasureHud(_styles, innerW);
            Rect panel = new Rect(12f, 12f, panelW, Mathf.Min(contentH + 24f, Screen.height - 24f));
            GUI.DrawTexture(panel, _panelTex);
            DrawHud(panel.x + 14f, panel.y + 12f, innerW);

            if (_showSearch)
                DrawSearchPanel(panel.xMax, _appliedScale);
        }

       
        private void BuildHudLines()
        {
            _lines.Clear();
            Add("GOAP vs FSM — Woodcutter", HudText.Title);
            Add("Brain: " + (_useGoap ? "GOAP (plans with A*)" : "Hand-authored FSM"), HudText.Body, 4f);

            if (_useGoap)
                AddGoapStatus();
            else
                AddFsmStatus();

            Add("World state:", HudText.Body, 8f);
            Add("   HasAxe=" + _agent.State.Get(HasAxe) + "   AxeSharp=" + _agent.State.Get(AxeSharp));
            Add("   HasLogs=" + _agent.State.Get(HasLogs) + "   HasPlanks=" + _agent.State.Get(HasPlanks));
            Add("   AxeInShed=" + _agent.State.Get(AxeInShed) + "   HasGold=" + _agent.State.Get(HasGold));

            Add("Interact:", HudText.Body, 8f);
            Add("   [S] steal axe + empty shed");
            Add("   [R] restock shed   [G] give gold");
            Add("   [B] break the agent's axe");
            Add("   [M] brain: " + (_useGoap ? "GOAP" : "FSM") + "  (switch)");
            Add("   [V] plan-search tree: " + (_showSearch ? "ON" : "OFF"));

            if (_lastEvent.Length > 0)
                Add("Last event: " + _lastEvent, HudText.Body, 8f);
        }

        private void AddGoapStatus()
        {
            Add("Goal: " + (_agent.ActiveGoal != null ? _agent.ActiveGoal.Name : "-"));
            Add("Status: " + _agent.StatusLine);
            Add("Plan (A* over world-states):", HudText.Body, 8f);

            IReadOnlyList<GoapAction> plan = _agent.CurrentPlan;
            if (plan == null)
            {
                // The only unreachable case here: no planks, no logs, no axe, empty shed, no gold.
                if (_agent.PlanningFailed)
                    Add("No plan: can't get an axe — the shed is empty and there's no gold. " +
                        "Press [R] to restock the shed or [G] to give gold.", HudText.Warn);
                else
                    Add("   (goal satisfied — awaiting the next order)");
                return;
            }

            for (int i = 0; i < plan.Count; i++)
                Add((i == _agent.PlanIndex ? " > " : "   ") + plan[i].Name + "  (cost " + plan[i].Cost + ")");

          
            GoapPlanner.PlanStats s = _agent.LastStats;
            Add("Last search:  " + s.NodesExpanded + " expanded / " + s.NodesGenerated +
                " generated,  " + s.Microseconds.ToString("0.#") + " us", HudText.Small, 8f);
            Add("Plan cost " + s.PlanCost + " over " + s.PlanLength +
                " actions   ·   replans so far: " + _agent.ReplanCount, HudText.Small);
        }

        private void AddFsmStatus()
        {
            Add("Status: " + _fsm.StatusLine, _fsm.IsStuck ? HudText.Warn : HudText.Body);
            Add("Route: hard-coded  shed -> grindstone -> tree -> sawmill -> stockpile", HudText.Body, 8f);
            Add("No planning, no buy-axe fallback wired.");
            if (_fsm.IsStuck)
                Add("Press [M] to hand the same situation to GOAP.", HudText.Warn);
        }

        private void Add(string text, HudText kind = HudText.Body, float gapBefore = 0f)
        {
            _lines.Add(new HudLine { Text = text, Kind = kind, GapBefore = gapBefore });
        }

        private float MeasureHud(HudStyles styles, float width)
        {
            float height = 0f;
            foreach (HudLine line in _lines)
                height += line.GapBefore + StyleOf(styles, line.Kind).CalcHeight(new GUIContent(line.Text), width);
            return height;
        }

        private void DrawHud(float x, float y, float width)
        {
            foreach (HudLine line in _lines)
            {
                GUIStyle style = StyleOf(_styles, line.Kind);
                GUIContent content = new GUIContent(line.Text);
                float height = style.CalcHeight(content, width);
                y += line.GapBefore;
                GUI.Label(new Rect(x, y, width, height), content, style);
                y += height;
            }
        }

        private static GUIStyle StyleOf(HudStyles styles, HudText kind)
        {
            switch (kind)
            {
                case HudText.Title: return styles.Title;
                case HudText.Small: return styles.Small;
                case HudText.Warn: return styles.Warn;
                default: return styles.Body;
            }
        }

        private void EnsureStyles(float scale)
        {
            if (_styles != null && Mathf.Abs(scale - _appliedScale) < 0.02f)
                return;
            _styles = MakeStyles(scale);
            _appliedScale = scale;
        }

        private static HudStyles MakeStyles(float scale)
        {
            HudStyles s = new HudStyles
            {
                Title = Label(24, scale, Color.white, FontStyle.Bold, true),
                Body = Label(18, scale, Color.white, FontStyle.Normal, true),
                Small = Label(13, scale, new Color(0.88f, 0.88f, 0.88f), FontStyle.Normal, true),
                Warn = Label(16, scale, new Color(1f, 0.82f, 0.25f), FontStyle.Normal, true),
                Tag = Label(15, scale, Color.white, FontStyle.Bold),
                NodeTitle = Label(14, scale, Color.white, FontStyle.Bold)
            };
            s.Tag.alignment = TextAnchor.MiddleCenter;
            return s;
        }

        private static GUIStyle Label(int size, float scale, Color color, FontStyle style, bool wrap = false)
        {
            GUIStyle s = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(size * scale),
                fontStyle = style,
                wordWrap = wrap
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

        private void DrawWorldLabels()
        {
            LabelWorld(_shed, "SHED");
            LabelWorld(_grindstone, "GRINDSTONE");
            LabelWorld(_tree, "TREE");
            LabelWorld(_sawmill, "SAWMILL");
            LabelWorld(_stockpile, "STOCKPILE");
            LabelWorld(_shop, "SHOP");
            LabelWorld(_agent.transform, "AGENT");
        }

        // Draws a name above a world object, sized to its text so nothing is clipped.
        private void LabelWorld(Transform t, string text)
        {
            Vector3 sp = Camera.main.WorldToScreenPoint(t.position + Vector3.up * 1.9f);
            GUIContent gc = new GUIContent(text);
            Vector2 size = _styles.Tag.CalcSize(gc);
            const float padX = 8f, padY = 4f;
            Rect box = new Rect(sp.x - size.x / 2f - padX,
                                Screen.height - sp.y - size.y / 2f - padY,
                                size.x + padX * 2f,
                                size.y + padY * 2f);
            GUI.DrawTexture(box, _panelTex);
            GUI.Label(box, gc, _styles.Tag);
        }

        // plan-search visualizer

        private static readonly Color ColGoal = new Color(0.35f, 0.95f, 0.45f);
        private static readonly Color ColPlan = new Color(0.20f, 0.70f, 0.30f);
        private static readonly Color ColExpanded = new Color(0.30f, 0.55f, 0.95f);
        private static readonly Color ColFrontier = new Color(0.50f, 0.50f, 0.55f);

       
        private void DrawSearchPanel(float leftX, float hudScale)
        {
            Rect area = new Rect(leftX + 12f, 12f, Screen.width - leftX - 24f, Screen.height - 24f);
            if (area.width < 260f)
                return; // not enough room to be readable

            FillRect(area, new Color(0f, 0f, 0f, 0.55f));
            GUI.Label(new Rect(area.x + 12f, area.y + 8f, area.width - 24f, 30f * hudScale),
                      "Plan search — A* over world-states", _styles.Body);

            Rect textRect = new Rect(area.x + 12f, area.y + 42f * hudScale, area.width - 24f, 48f * hudScale);
            if (!_useGoap)
            {
                GUI.Label(textRect, "The FSM does not search — it follows a fixed route. " +
                                    "Press [M] to switch to the GOAP brain.", _styles.Small);
                return;
            }

            IReadOnlyList<GoapPlanner.SearchNode> trace = _agent.LastSearch;
            if (trace == null)
            {
                GUI.Label(textRect, "(no search recorded yet)", _styles.Small);
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

            FillRect(new Rect(r.x - 2f, r.y - 2f, r.width + 4f, r.height + 4f), border); 
            FillRect(r, new Color(0.06f, 0.06f, 0.08f, 0.96f));                          

            const float pad = 8f;
            float lineH = 18f * hudScale;
            float textW = r.width - pad * 2f;

            string title = n.ActionName ?? "START";
            string cost = "f=" + n.F.ToString("0.#") + " g=" + n.G.ToString("0.#") + " h=" + n.H.ToString("0.#");
            string order = n.ExpandedOrder >= 0 ? "#" + n.ExpandedOrder : "frontier";

            GUI.Label(new Rect(r.x + pad, r.y + 3f, textW, 20f * hudScale),
                      Fit(title, _styles.NodeTitle, textW), _styles.NodeTitle);
            GUI.Label(new Rect(r.x + pad, r.y + 3f + lineH, textW, lineH),
                      Fit(cost + "  " + order, _styles.Small, textW), _styles.Small);
            GUI.Label(new Rect(r.x + pad, r.y + 3f + lineH * 2f, textW, lineH),
                      Fit(n.TrueFacts, _styles.Small, textW), _styles.Small);
        }

        private static string Fit(string text, GUIStyle style, float width)
        {
            if (style.CalcSize(new GUIContent(text)).x <= width)
                return text;

            for (int len = text.Length - 1; len > 1; len--)
            {
                string candidate = text.Substring(0, len).TrimEnd(' ', ',') + "..";
                if (style.CalcSize(new GUIContent(candidate)).x <= width)
                    return candidate;
            }
            return "..";
        }

        private void DrawLegendChip(float x, float y, float scale, Color color, string label)
        {
            FillRect(new Rect(x, y, 14f * scale, 14f * scale), color);
            GUI.Label(new Rect(x + 20f * scale, y - 2f * scale, 140f * scale, 20f * scale), label, _styles.Small);
        }

        private void FillRect(Rect r, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(r, _whiteTex);
            GUI.color = previous;
        }

        private void GuiLine(Vector2 a, Vector2 b, float width, Color color)
        {
            Matrix4x4 saved = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg, a);
            FillRect(new Rect(a.x, a.y - width / 2f, Vector2.Distance(a, b), width), color);
            GUI.matrix = saved;
        }
    }
}

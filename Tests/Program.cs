using System;
using System.Collections.Generic;
using GOAP;

// Headless test harness for the GOAP planner core (no Unity required).
// Run with:  dotnet run --project Tests
// It exercises GoapPlanner against the demo's woodcutter actions and asserts the resulting plans.
class Program
{
    static List<GoapAction> BuildActions()
    {
        return new List<GoapAction>
        {
            new GoapAction("GetAxeFromShed", 2f).Pre("AxeInShed", true).Effect("HasAxe", true).Effect("AxeInShed", false),
            new GoapAction("BuyAxe", 4f).Pre("HasGold", true).Effect("HasAxe", true).Effect("HasGold", false),
            new GoapAction("SharpenAxe", 1f).Pre("HasAxe", true).Effect("AxeSharp", true),
            new GoapAction("ChopLogs", 3f).Pre("HasAxe", true).Pre("AxeSharp", true).Effect("HasLogs", true),
            new GoapAction("SawPlanks", 2f).Pre("HasLogs", true).Effect("HasPlanks", true).Effect("HasLogs", false),
            new GoapAction("DeliverPlanks", 1f).Pre("HasPlanks", true).Effect("PlanksDelivered", true).Effect("HasPlanks", false),
        };
    }

    static string PlanStr(List<GoapAction> plan)
    {
        if (plan == null) return "NULL";
        return string.Join(" -> ", plan.ConvertAll(a => a.Name));
    }

    static int failures = 0;

    static void Check(string label, string got, string expected)
    {
        bool ok = got == expected;
        if (!ok) failures++;
        Console.WriteLine((ok ? "PASS " : "FAIL ") + label + "\n     got:      " + got + "\n     expected: " + expected);
    }

    static void Main()
    {
        var planner = new GoapPlanner();
        var goal = new GoapGoal("DeliverPlanks", 5f).Want("PlanksDelivered", true);

        // 1. Full chain from nothing: the planner must find all five steps, preferring the cheap shed.
        var s1 = new WorldState();
        s1.Set("AxeInShed", true); s1.Set("HasGold", true);
        Check("full chain via shed", PlanStr(planner.Plan(s1, goal, BuildActions())),
              "GetAxeFromShed -> SharpenAxe -> ChopLogs -> SawPlanks -> DeliverPlanks");

        // 2. Shed empty -> the only route to an axe is buying one.
        var s2 = new WorldState();
        s2.Set("AxeInShed", false); s2.Set("HasGold", true);
        Check("shed empty -> buy axe", PlanStr(planner.Plan(s2, goal, BuildActions())),
              "BuyAxe -> SharpenAxe -> ChopLogs -> SawPlanks -> DeliverPlanks");

        // 3. Already holding a sharp axe -> skip both acquisition and sharpening.
        var s3 = new WorldState();
        s3.Set("HasAxe", true); s3.Set("AxeSharp", true);
        Check("sharp axe in hand -> chop onward", PlanStr(planner.Plan(s3, goal, BuildActions())),
              "ChopLogs -> SawPlanks -> DeliverPlanks");

        // 4. Carrying planks already -> one action is enough, regardless of tools.
        var s4 = new WorldState();
        s4.Set("HasPlanks", true);
        Check("planks in hand -> just deliver", PlanStr(planner.Plan(s4, goal, BuildActions())),
              "DeliverPlanks");

        // 5. No axe, no gold, empty shed -> the goal is genuinely unreachable.
        var s5 = new WorldState();
        s5.Set("AxeInShed", false); s5.Set("HasGold", false);
        Check("no way to get an axe -> null plan", PlanStr(planner.Plan(s5, goal, BuildActions())), "NULL");

        // 6. Goal already satisfied -> nothing to do.
        var s6 = new WorldState();
        s6.Set("PlanksDelivered", true);
        var p6 = planner.Plan(s6, goal, BuildActions());
        Check("goal already met -> empty plan", p6 != null ? "EMPTY(" + p6.Count + ")" : "NULL", "EMPTY(0)");

        // 7. The chosen plan must be the cheapest one, not merely a valid one.
        var s7 = new WorldState();
        s7.Set("AxeInShed", true); s7.Set("HasGold", true);
        var p7 = planner.Plan(s7, goal, BuildActions());
        float cost = 0f;
        foreach (var a in p7) cost += a.Cost;
        Check("cheapest plan costs 9 (shed 2, not buy 4)", cost.ToString(), "9");

        // 8. Stats are reported for the search that just ran.
        Check("stats recorded for last search",
              planner.LastStats.Found && planner.LastStats.NodesExpanded > 0 &&
              planner.LastStats.PlanLength == 5 ? "ok" : "missing",
              "ok");

        // 9. Both heuristics must agree on the answer — an admissible heuristic changes how much
        //    of the space is searched, never which plan comes out.
        var naive = new GoapPlanner { Heuristics = GoapPlanner.HeuristicMode.GoalFactCount };
        var informed = new GoapPlanner { Heuristics = GoapPlanner.HeuristicMode.RelaxedPlanGraph };

        var startState = new WorldState();
        startState.Set("AxeInShed", true); startState.Set("HasGold", true);

        string naivePlan = PlanStr(naive.Plan(startState, goal, BuildActions()));
        string informedPlan = PlanStr(informed.Plan(startState, goal, BuildActions()));
        Check("both heuristics return the same optimal plan", informedPlan, naivePlan);

        // 10. The relaxed-plan-graph estimate must actually pay for itself in expansions.
        Check("informed heuristic expands fewer states",
              informed.LastStats.NodesExpanded < naive.LastStats.NodesExpanded ? "fewer" : "not fewer",
              "fewer");
        Console.WriteLine("     naive: " + naive.LastStats.NodesExpanded + " expanded / " +
                          naive.LastStats.NodesGenerated + " generated" +
                          "   informed: " + informed.LastStats.NodesExpanded + " expanded / " +
                          informed.LastStats.NodesGenerated + " generated");

        // 11. An unreachable goal is detected without exhausting the whole space.
        var deadEnd = new WorldState();
        deadEnd.Set("AxeInShed", false); deadEnd.Set("HasGold", false);
        Check("unreachable goal still returns null", PlanStr(informed.Plan(deadEnd, goal, BuildActions())), "NULL");
        Check("unreachable goal detected immediately",
              informed.LastStats.NodesExpanded == 0 ? "no expansion needed" : "searched anyway",
              "no expansion needed");

        Console.WriteLine(failures == 0 ? "\nALL TESTS PASSED" : "\n" + failures + " TEST(S) FAILED");
        Environment.Exit(failures == 0 ? 0 : 1);
    }
}

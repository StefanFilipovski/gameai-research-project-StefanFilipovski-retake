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
            new GoapAction("ChopWood", 3f).Pre("HasAxe", true).Effect("HasWood", true),
            new GoapAction("DeliverWood", 1f).Pre("HasWood", true).Effect("WoodDelivered", true).Effect("HasWood", false),
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
        var goal = new GoapGoal("DeliverWood", 5f).Want("WoodDelivered", true);

        // 1. Axe in shed + gold -> take the CHEAP shed branch (2+3+1=6) not buy (4+3+1=8).
        var s1 = new WorldState();
        s1.Set("AxeInShed", true); s1.Set("HasGold", true);
        Check("shed available -> use shed", PlanStr(planner.Plan(s1, goal, BuildActions())),
              "GetAxeFromShed -> ChopWood -> DeliverWood");

        // 2. Shed empty, has gold -> must buy the axe.
        var s2 = new WorldState();
        s2.Set("AxeInShed", false); s2.Set("HasGold", true);
        Check("shed empty -> buy axe", PlanStr(planner.Plan(s2, goal, BuildActions())),
              "BuyAxe -> ChopWood -> DeliverWood");

        // 3. Already has an axe -> skip acquiring one entirely.
        var s3 = new WorldState();
        s3.Set("HasAxe", true);
        Check("already has axe -> just chop+deliver", PlanStr(planner.Plan(s3, goal, BuildActions())),
              "ChopWood -> DeliverWood");

        // 4. No axe, no gold, empty shed -> IMPOSSIBLE, planner must return null.
        var s4 = new WorldState();
        s4.Set("AxeInShed", false); s4.Set("HasGold", false);
        Check("no way to get axe -> null plan", PlanStr(planner.Plan(s4, goal, BuildActions())), "NULL");

        // 5. Goal already satisfied -> empty plan.
        var s5 = new WorldState();
        s5.Set("WoodDelivered", true);
        var p5 = planner.Plan(s5, goal, BuildActions());
        Check("goal already met -> empty plan", p5 != null ? "EMPTY(" + p5.Count + ")" : "NULL", "EMPTY(0)");

        Console.WriteLine(failures == 0 ? "\nALL TESTS PASSED" : "\n" + failures + " TEST(S) FAILED");
        Environment.Exit(failures == 0 ? 0 : 1);
    }
}

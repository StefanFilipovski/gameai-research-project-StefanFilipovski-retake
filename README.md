# Goal-Oriented Action Planning (GOAP) — Game AI Research

**Author:** Stefan Filipovski
**Engine:** Unity 6 (6000.3, Built-in Render Pipeline), C#
**Repository:** https://github.com/StefanFilipovski/gameai-research-project-StefanFilipovski-retake

> A small, self-contained Unity demo of an agent that *plans* its own behaviour with A\*
> instead of following a hand-authored script. A woodcutter is given a goal — "deliver wood"
> — and works out the cheapest sequence of actions to achieve it, re-planning on the fly when
> the world changes underneath it.
>
> The demo also ships **a live A\* search visualizer** (press **V**) and **a hand-authored FSM
> running the same job** (press **M**), so you can watch the planning happen and see exactly
> where the traditional approach breaks down.

---

## Why GOAP?

Most game agents make decisions with a **Finite State Machine (FSM)** or a **Behaviour Tree
(BT)**. Both work by *hand-authoring the transitions*: the designer explicitly wires "if in
state A and condition X, go to state B". This is fast and predictable, but it has a well-known
scaling problem — every time you add an action or a condition, the number of transitions you
have to author and maintain grows combinatorially. A FSM with 10 states can need up to 90
transitions. Add an eleventh state and you may touch dozens of existing ones.

**GOAP inverts the problem.** Instead of authoring *how* the agent reaches its goals, you only
describe:

- **the actions** the agent *can* do, each as a set of **preconditions** and **effects**, and
- **the goals** it *wants*, as a desired world state.

At runtime the agent **searches for its own plan** — a sequence of actions whose combined
effects satisfy the goal. Nobody wired "chop → deliver"; the planner discovered it. Add a new
action and it is automatically considered in every future plan, with **no transitions to
rewire**. This is exactly the property that made GOAP famous in *F.E.A.R.* (2005), whose
soldiers appeared strikingly tactical while each only carried a handful of generic actions
(Orkin 2006).

| | FSM / Behaviour Tree | GOAP |
|---|---|---|
| Designer authors | every transition explicitly | actions + goals only |
| Adding an action | edit many existing transitions | drop one action in, done |
| Runtime cost | ~free (just a lookup) | a small A\* search per (re)plan |
| Behaviour feels | scripted, fixed | emergent, adaptive |

GOAP trades a little runtime CPU (the search) for a large reduction in authoring complexity and
much more adaptive behaviour. That trade is the whole point of the technique.

### Proving it live — the [M] brain toggle

Rather than just asserting that advantage, the demo lets you **run the same woodcutter on two
different brains** and break them both. Press **M** to swap between:

- **GOAP** — [`GoapAgent.cs`](Assets/Scripts/GOAP/GoapAgent.cs) + the planner, and
- **a hand-authored FSM** — [`FsmWoodcutter.cs`](Assets/Scripts/GOAP/FsmWoodcutter.cs), wired for
  exactly the route a designer would author: `shed → grindstone → tree → sawmill → stockpile`.

![The hand-authored FSM stuck after the shed is emptied](Docs/fsm-vs-goap.gif)

*The FSM after the shed was emptied mid-route. It is **stuck**: it reached a situation no
transition was authored for, and it has no way to reason its way out. Pressing **M** hands the
identical world state to GOAP, which simply plans around the problem and buys an axe instead.*

**Left alone, both brains look identical** — they each fetch the axe, chop, and deliver. The
difference only appears when the world stops matching the script:

| | Hand-authored FSM | GOAP |
|---|---|---|
| Author writes | 12 states wired in a fixed order | 6 independent actions, no ordering |
| Happy path | ✅ works perfectly | ✅ works perfectly |
| Shed emptied mid-task (**S**) | ❌ **stuck** — no "shed is empty" transition was ever authored | ✅ re-plans onto `BuyAxe` |
| Axe taken at the tree (**B**) | ❌ **stuck** — cannot go back for another | ✅ re-plans from wherever it is |
| To fix the FSM | write a new state + wire every transition into it | *nothing* — the `BuyAxe` action already existed |

The key detail: **the [M] toggle swaps the brain in place** and leaves the world state untouched.
So you can get the FSM hopelessly stuck, hand that *exact* situation to GOAP, and watch it plan
its way out. The FSM did not fail because it was badly written — it failed because a designer has
to anticipate every situation in advance, and GOAP does not.

---

## The Core Idea: Planning *is* A\* Pathfinding

This is the single most important insight in the project, and the one worth internalising:

> **GOAP planning is A\* search — but through a graph of *world-states* instead of physical space.**

In grid or navmesh pathfinding, A\* walks from tile to tile. In GOAP, A\* walks from
world-state to world-state, and the "step" between them is *performing an action*:

| Grid / navmesh A\* | GOAP planner |
|---|---|
| node = a tile | node = a whole **world-state** (a set of true/false facts) |
| edge = move to a neighbouring tile | edge = **apply an action** (its effects) |
| edge cost = distance | edge cost = **action cost** |
| start = start tile | start = the agent's **current** world-state |
| goal = the goal tile | goal = **any** state satisfying the goal's facts |
| heuristic = distance left to goal | heuristic = **number of goal facts not yet satisfied** |

Everything else about A\* — the open set, the closed set, `f = g + h`, reconstructing the path
by walking parent pointers — is *identical*. If you understand A\* for movement, you already
understand the GOAP planner. The only thing that changed is what a "node" means.

> _This is also why it pairs naturally with my previous research project on **Flow Fields**:
> that was pathfinding through **space**; this is pathfinding through **action / state space**.
> Same algorithm, different graph._

---

## The Building Blocks

### 1. World State — [`WorldState.cs`](Assets/Scripts/GOAP/WorldState.cs)

The agent never reasons about the Unity scene directly. It reasons about a tiny set of named
boolean **facts**:

```
HasAxe = false
AxeSharp = false
HasLogs = false
HasPlanks = false
AxeInShed = true
HasGold = true
PlanksDelivered = false
```

A `WorldState` is just a snapshot of those facts (`Dictionary<string,bool>`). It offers three
operations the planner leans on:

- `Satisfies(conditions)` — are all these facts currently true? (used for both preconditions and the goal test)
- `ApplyEffects(effects)` — return a **new** state with an action's effects applied (a neighbour node)
- `Key()` — a stable string used to deduplicate states in A\*'s closed set

Keeping the state small and symbolic is what keeps the search cheap.

### 2. Actions — [`GoapAction.cs`](Assets/Scripts/GOAP/GoapAction.cs)

Each action carries a **symbolic** half (for the planner) and a **runtime** half (for the agent):

| Action | Precondition | Effect | Cost |
|---|---|---|---|
| `GetAxeFromShed` | `AxeInShed` | `HasAxe`, `¬AxeInShed` | 2 |
| `BuyAxe` | `HasGold` | `HasAxe`, `¬HasGold` | 4 |
| `SharpenAxe` | `HasAxe` | `AxeSharp` | 1 |
| `ChopLogs` | `HasAxe`, `AxeSharp` | `HasLogs` | 3 |
| `SawPlanks` | `HasLogs` | `HasPlanks`, `¬HasLogs` | 2 |
| `DeliverPlanks` | `HasPlanks` | `PlanksDelivered`, `¬HasPlanks` | 1 |

The runtime half is just a `Transform` to walk to and a `Duration` to spend performing it once
there. **Cost is how you bias the AI**: fetching a free axe from the shed (2) is cheaper than
buying one (4), so given the choice the planner always prefers the shed.

Note that **no action mentions any other action**. `SawPlanks` does not know that `ChopLogs`
exists — it only declares that it needs `HasLogs`. The five-step chain below is discovered by the
search purely from matching effects to preconditions, which is why adding a seventh action needs
no edits to the existing six.

### 3. Goals — [`GoapGoal.cs`](Assets/Scripts/GOAP/GoapGoal.cs)

A goal is a desired world-state plus a **priority**. An agent can hold several; each frame it
picks the highest-priority goal that is not already satisfied and plans toward it. Here there is
one goal — `PlanksDelivered = true` — but the machinery supports many.

---

## The Planner — [`GoapPlanner.cs`](Assets/Scripts/GOAP/GoapPlanner.cs)

`Plan(start, goal, actions)` runs textbook A\*:

1. Put the start state on the open set with `f = h(start)`.
2. Pop the lowest-`f` node. If it **satisfies the goal**, walk parent pointers back into an
   ordered action list and return it.
3. Otherwise, for every action whose **preconditions are satisfied** by this state, generate the
   successor state via `ApplyEffects`, give it `g = g_current + action.Cost` and
   `f = g + h`, and add it to the open set (skipping states already closed or reachable more
   cheaply).
4. If the open set empties without reaching the goal, there is **no plan** → return `null`.

**The heuristic** `h(state)` = *the number of goal facts still unsatisfied*. Because a
well-formed action fixes at most one goal fact, `h` never overestimates the true remaining cost,
so it is **admissible** and A\* returns an **optimal (cheapest) plan**.

Worked example — start with `AxeInShed = true`, `HasGold = true`, goal `PlanksDelivered`:

```
GetAxeFromShed (2) → SharpenAxe (1) → ChopLogs (3) → SawPlanks (2) → DeliverPlanks (1)   cost 9   ✅ chosen
BuyAxe         (4) → SharpenAxe (1) → ChopLogs (3) → SawPlanks (2) → DeliverPlanks (1)   cost 11  ❌ dearer
```

Empty the shed and the first branch's precondition disappears, so the planner is forced onto the
buy branch. This exact behaviour is covered by the automated planner tests (see
[Testing](#testing)).

### What the search costs

Because adaptivity is bought with runtime CPU, the demo **measures** it. The HUD reports the
last search live, and the same numbers go to the Console:

```
Last search:  6 expanded / 11 generated,  38.4 us
Plan cost 9 over 5 actions   ·   replans so far: 3
```

A full five-action plan is found in **tens of microseconds** from a handful of node expansions.
That is the honest answer to "isn't searching every time expensive?" — at this state-space size
it is cheap enough to re-run whenever the world changes, which is exactly what replanning does.
The cost grows with the number of *actions* and the *plan depth*, not with the size of the level,
so keeping the symbolic state small is what keeps GOAP affordable.

---

## Replanning — the part that makes it feel alive

A plan is only a *belief* about the future. The agent — [`GoapAgent.cs`](Assets/Scripts/GOAP/GoapAgent.cs)
— is a small finite state machine that executes the plan while continuously validating it:

```
Idle       → ask the planner for a plan toward the best goal
Moving     → walk to the current action's Target
Performing → wait out the action's Duration, then write its effects into the world state
```

**Every frame** it checks that the current action's preconditions still hold. If you steal the
axe out of the shed while the woodcutter is walking to it, `AxeInShed` becomes false, the
`GetAxeFromShed` precondition breaks, the agent throws the plan away and re-plans — now routing
to the shop to buy one instead. No transition for "axe was stolen" was ever authored; the
adaptive behaviour falls out of the search.

Replanning is visible in both recordings below: the [search visualizer](#seeing-the-search--the-plan-search-visualizer)
clip shows the tree being rebuilt after the world changes, and the HUD's `replans so far` counter
ticks up each time the agent is forced to think again.

---

## The Demo

![The woodcutter working through its five-action plan](Docs/overview.gif)

*The agent executing a plan it worked out itself. The HUD shows the goal, the ordered plan with
the running action marked `>`, what the last search cost, and the live world state.*

A woodcutter (red capsule) works six sites built at runtime:

- 🟫 **Shed** — pick up a free axe (cheap)
- 🟦 **Shop** — buy an axe (expensive)
- ⬜ **Grindstone** — sharpen the axe
- 🟩 **Tree** — chop logs (needs a sharp axe)
- 🟪 **Sawmill** — saw logs into planks
- 🟨 **Stockpile** — deliver the planks

The on-screen HUD shows, live: the **active goal**, the **current plan** with the running action
marked `>`, the **world state**, and the interaction keys. When a goal becomes unreachable the
plan area turns into an **amber hint** explaining *why* and which key recovers it. Everything —
ground, sites, agent, camera, light and HUD — is created in code by
[`DemoBootstrap.cs`](Assets/Scripts/GOAP/DemoBootstrap.cs), so the project runs from an empty
scene with a single component.

### Controls

| Key | Action | What it demonstrates |
|---|---|---|
| **S** | Steal the axe & empty the shed | Forces a re-plan onto the expensive buy-axe branch |
| **R** | Restock the shed | Next plan prefers the cheap shed branch again |
| **G** | Give the agent gold | Re-enables the buy-axe branch when the shed is empty |
| **B** | Break the agent's axe | Agent must acquire a new axe before it can chop |
| **M** | Switch brain: GOAP ↔ hand-authored FSM | The comparison above — swaps in place, world untouched |
| **V** | Toggle the plan-search visualizer | Shows/hides the live A\* search tree (see below) |

Try this: press **S** while the agent walks to the shed and watch it reroute to the shop. Then,
with no gold left, press **B** to see it report an **amber "no plan" hint** — the goal is
genuinely unreachable until you press **R** or **G**.

---

## Seeing the Search — the Plan-Search Visualizer

Press **V** to overlay the planner's **last A\* search** as a left-to-right tree. This is the
state-space analogue of the grid/flow-field debug views used for movement pathfinding — it makes
the otherwise-invisible planning search concrete.

![The A* search tree over world-states](Docs/plan-search-visualizer.gif)

*Pressing **V** reveals the planner's actual search. `START` is on the left; each column is one
action deeper; every box carries its own `f / g / h`. The green path is the plan that won.*

- **Each box is one world-state** the planner generated. **Columns = search depth** (column 0 is
  `START`, column 1 is after one action, and so on).
- **Edges are actions.** The **thick green path** is the chosen plan.
- Every box shows the action that produced it, its **`f / g / h`** values, whether it was
  **`expanded #N`** (popped by A\* in that order) or still **`frontier`** (generated but not yet
  expanded), and the facts true in that state.
- **Colours:** green = chosen plan · blue = expanded · gray = frontier · bright green = goal reached.

Because the tree is the *actual* recorded search (see `RecordSearch` / `SearchNode` in
[`GoapPlanner.cs`](Assets/Scripts/GOAP/GoapPlanner.cs)), pressing **S** mid-walk visibly redraws
it: the `GetAxeFromShed` branch disappears and the green path re-routes through `BuyAxe` at a
higher `g`. You are watching A\* re-solve in world-state space in real time.

## Debug Logging

The agent prints a full plan/execute/replan trace to the Console (toggle with `VerboseLogging`
on the agent). A typical order reads:

```
[GOAP] Planned for goal 'DeliverPlanks': GetAxeFromShed -> SharpenAxe -> ChopLogs -> SawPlanks
       -> DeliverPlanks  (cost 9, expanded 6/11 states in 38.4 us)
[GOAP]   step 1/5: start 'GetAxeFromShed' (cost 2)  -> moving to Shed
[GOAP]   done 'GetAxeFromShed'  -> HasAxe=True, AxeInShed=False
...
[GOAP] Replan requested: player stole axe / emptied shed
[GOAP] Planned for goal 'DeliverPlanks': BuyAxe -> SharpenAxe -> ChopLogs -> SawPlanks
       -> DeliverPlanks  (cost 11, expanded 5/9 states in 31.7 us)
```

The `cost 9 → cost 11` switch is the planner proving, in the log, that it re-evaluated and chose
the new cheapest route now that the cheap one is gone.

---

## Complexity & Comparison

| Approach | Authoring effort | Runtime cost | Adaptivity |
|---|---|---|---|
| FSM | O(states²) transitions | O(1) lookup | low — only wired transitions |
| Behaviour Tree | O(nodes), still hand-built | O(tree depth) | medium |
| **GOAP** | **O(actions) — no transitions** | **A\* per (re)plan, small state space** | **high — emergent, replans** |

GOAP's search is over a *symbolic* state space that is deliberately kept tiny (a handful of
boolean facts), so in practice each plan is a few dozen node expansions — cheap enough to run on
demand and to re-run whenever the world changes.

---

## Code Structure

```
Assets/Scripts/GOAP/
├── WorldState.cs     — symbolic facts; Satisfies / ApplyEffects / Key
├── GoapAction.cs     — preconditions, effects, cost + runtime target/duration
├── GoapGoal.cs       — desired world-state + priority
├── GoapPlanner.cs    — A* over world-states (+ optional search recording)  ← the core
├── GoapAgent.cs      — plan/execute FSM with live replanning + logging
├── FsmWoodcutter.cs  — hand-authored FSM brain, for the [M] comparison
└── DemoBootstrap.cs  — builds the scene, wires the woodcutter, draws the HUD + visualizer
```

**Key methods**
- `GoapPlanner.Plan()` — A\* search returning the cheapest action sequence (or `null`)
- `GoapPlanner.BuildTrace()` — snapshots the search into `SearchNode`s for the `[V]` visualizer
- `WorldState.ApplyEffects()` — generates a successor node during the search
- `GoapAgent.Update()` — the Idle→Moving→Performing FSM and the per-frame replan check
- `GoapAgent.ChooseGoal()` — highest-priority unsatisfied goal
- `DemoBootstrap.DrawSearchTree()` — renders the recorded A\* search as a node-link tree

---

## How to Run

This repository **is** a ready-to-open Unity project — clone it and open the folder in **Unity
6 (6000.3.9f1 or compatible)** via Unity Hub, then open `Assets/Scenes/SampleScene` and press
**Play**. It uses the **Built-in Render Pipeline** (not URP/HDRP, or the runtime-created
materials would be magenta) and works with either input backend — the demo reads the **new Input
System** when present (Unity 6 default) and falls back to the legacy Input Manager, so **no
Player Settings changes are needed**.

To rebuild from scratch instead: create a new **3D (Built-in Render Pipeline)** project, copy the
`Assets/Scripts` folder into its `Assets`, add the **DemoBootstrap** component to an empty
GameObject in a scene, and press Play. A ready-to-run **release build** is also included in the
hand-in.

## Testing

The pure-C# core (`WorldState`, `GoapAction`, `GoapGoal`, `GoapPlanner`) has no Unity
dependencies, so it is covered by a headless test harness in `Tests/` that compiles those four
files directly and runs without the editor:

```bash
dotnet run --project Tests
```

Its eight checks assert that the planner finds the full five-action chain, falls back to `BuyAxe`
when the shed is emptied, skips steps it no longer needs (a sharp axe in hand, or planks already
carried), returns `null` when the goal is genuinely unreachable, returns an empty plan when the
goal already holds, picks the **cheapest** plan rather than merely a valid one, and reports search
statistics.

---

## Sources

- **Orkin, Jeff.** *"Three States and a Plan: The A.I. of F.E.A.R."* — Game Developers
  Conference (GDC) 2006. The canonical GOAP paper; introduces planning with A\* over world
  states in a shipping game. https://alumni.media.mit.edu/~jorkin/gdc2006_orkin_jeff_fear.pdf
- **Orkin, Jeff.** *"Applying Goal-Oriented Action Planning to Games"* — in *AI Game Programming
  Wisdom 2*, Charles River Media, 2004.
- **Millington, Ian & Funge, John.** *Artificial Intelligence for Games*, 2nd ed., Morgan
  Kaufmann, 2009 — Chapter 5 (Decision Making) covers goal-oriented behaviour and planning.
- **Thompson, Tommy (AI and Games).** *"Building the AI of F.E.A.R. with Goal Oriented Action
  Planning."* Video breakdown of GOAP in practice. https://www.aiandgames.com
- **Hart, P., Nilsson, N., Raphael, B.** *"A Formal Basis for the Heuristic Determination of
  Minimum Cost Paths"* — IEEE, 1968. The original A\* paper the planner is built on.
- **Unity Technologies.** *Unity Scripting Reference.* https://docs.unity3d.com/ScriptReference/

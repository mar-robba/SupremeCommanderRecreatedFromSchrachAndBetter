# Research library — papers, talks and reference material

Documents found on the web (2026-11-14, session e4dd3c13) that can help the next waves: pathing and crowds, multiplayer
determinism, skirmish AI, terrain rendering. Programs, reference source code (BAR, FAF, OpenRA...) and Claude Code skills
are catalogued in [EXTERNAL.md](EXTERNAL.md).

**How to use it.** Find the area in the tables below, read the one or two documents it points to *before* designing a
change, and say in the brief which one you followed. A PDF is read with `pdftotext -layout FILE.pdf -` (poppler is
installed) or opened normally. Nothing here is binding: the project's decisions stay in ARCHITECTURE / WORKFLOW.

**Honesty note.** The "why it matters here" column comes from titles, abstracts and the architecture as written in
ARCHITECTURE.md; the documents were checked to open and to be the right paper, **not read in full**. Whoever uses one for a
design should read it first and correct the line here if it turns out different.

**Licences and size.** Everything in `papers/` is a copy of a document its authors or publisher distribute for free
(Game AI Pro states that all chapters are free to download; the others are author, university, AAAI or GDC pages). They are
kept as reference copies; if the repo is ever published, check each one's terms or drop the PDFs and keep this index
(each entry has its source URL). The folder is about 38 MB (35 PDFs), so it roughly doubles the size of `.git` once
committed: the owner decides whether the PDFs are committed or ignored.

## If you read only six

1. `game-ai-pro/GameAIPro3_Chapter19_RVO_and_ORCA_How_They_Really_Work.pdf` — what ORCA does and where it breaks; for the
   crowd behaviour of big armies (separation is today a priority push, ARCHITECTURE "Tick pipeline" step 9).
2. `pathfinding-dhpa-shpa-kring-2010.pdf` — dynamic hierarchical pathfinding when buildings appear and die; the project's
   `PathService` is HPA*-style with regions, so this is the direct next idea for dynamic obstacles.
3. `game-ai-pro/GameAIPro3_Chapter25_Combat_Outcome_Prediction_for_Real-Time_Strategy_Games.pdf` — a cheap way for the
   skirmish AI to decide "attack now or wait", instead of tuned thresholds (AI_TUNING "pressure" rule).
4. `game-ai-pro/GameAIPro2_Chapter30_Modular_Tactical_Influence_Maps.pdf` — the standard design for threat / value maps;
   compare with the AI's current threat maps (ARCHITECTURE "Skirmish AI").
5. `determinism-cross-platform-frontier-gdc2024.pdf` — an RTS studio's checklist for cross-platform determinism; check our
   wave 10a-1 work (software maths, `GoldenStateHash`) against it.
6. `pathfinding-flow-field-tiles-emerson-2013.pdf` — the Supreme Commander 2 / Planetary Annihilation flow-field design by
   its author; our region goal fields are a close cousin, so this is the reference for what a full version does.

## Pathfinding, movement and crowds

| File (in `papers/`) | What it is | Why it matters here |
|---|---|---|
| `pathfinding-flow-field-tiles-emerson-2013.pdf` | E. Emerson, "Crowd Pathfinding and Steering Using Flow Field Tiles", Game AI Pro 1 ch. 23. Source: gameaipro.com | The design behind Supreme Commander 2's movement: tiled flow fields, line-of-sight fields, group steering. Compare with `PathService` (cached region goal fields). |
| `pathfinding-dhpa-shpa-kring-2010.pdf` | Kring, Champandard, Samarin, "DHPA* and SHPA*: Efficient Hierarchical Pathfinding in Dynamic and Static Game Worlds", AIIDE 2010. Source: cdn.aaai.org | Hierarchical A* that updates when the world changes (walls, factories, wrecks). Ours re-paths on stuck; this is the cheaper alternative. |
| `pathfinding-hpa-enhancements-jansen-buro-2007.pdf` | Jansen, Buro, "HPA* Enhancements", AIIDE 2007. Source: cdn.aaai.org | Improvements to the HPA* that `PathService` is modelled on (smaller graphs, smoother paths). The original HPA* paper (Botea 2004) could not be downloaded: its university URL returns 404. |
| `crowds-continuum-crowds-treuille-2006.pdf` | Treuille, Cooper, Popović, "Continuum Crowds", SIGGRAPH 2006. Source: grail.cs.washington.edu | The paper flow-field RTS crowds descend from (a potential field shared by a group). Background for formations and big blobs. |
| `game-ai-pro/GameAIPro_Chapter17_Pathfinding_Architecture_Optimizations.pdf` | Game AI Pro 1 ch. 17 | How to organise path requests (budgets, caching, early outs): our per-tick solve cap is the same idea. |
| `game-ai-pro/GameAIPro_Chapter18_Choosing_a_Search_Space_Representation.pdf` | Game AI Pro 1 ch. 18 | Grid vs navmesh vs waypoint graph, with costs. Read before changing `NavGrid`. |
| `game-ai-pro/GameAIPro_Chapter21_Techniques_for_Formation_Movement_Using_Steering_Circles.pdf` | Game AI Pro 1 ch. 21 | Formation movement with turning-radius limits; fits ships and big experimentals. |
| `game-ai-pro/GameAIPro2_Chapter14_JPS_Plus_An_Extreme_A_Star_Speed_Optimization_for_Static_Uniform_Cost_Grids.pdf` | Game AI Pro 2 ch. 14 (JPS+) | A* 10-100x faster on static uniform grids; only for the part of the map that never changes. |
| `game-ai-pro/GameAIPro2_Chapter17_Advanced_Techniques_for_Robust_Efficient_Crowds.pdf` | Game AI Pro 2 ch. 17 | Practical crowd tricks (density, lanes, fallbacks) with robustness in mind. |
| `game-ai-pro/GameAIPro2_Chapter20_Hierarchical_Architecture_for_Group_Navigation_Behaviors.pdf` | Game AI Pro 2 ch. 20 | Group-level plus individual-level navigation layers: the shape of formation + leader path. |
| `game-ai-pro/GameAIPro3_Chapter22_Faster_A_Star_with_Goal_Bounding.pdf` | Game AI Pro 3 ch. 22 | Pre-computed bounding boxes that prune A*; a possible speed-up of corridor A* on static terrain. |
| `game-ai-pro/GameAIPro3_Chapter23_Faster_Dijkstra_Search_on_Uniform_Cost_Grids.pdf` | Game AI Pro 3 ch. 23 | Faster Dijkstra for goal fields (what the cached region goal fields compute). |
| `game-ai-pro/GameAIPro3_Chapter34_1000_NPCs_at_60_FPS.pdf` | Game AI Pro 3 ch. 34 | Engineering a thousand agents in a frame budget: LOD of updates, batching. Same problem as the bench (`2000 300`). |
| `game-ai-pro/GameAIPro_Chapter14_Phenomenal_AI_Level-of-Detail_Control_with_the_LOD_Trader.pdf` | Game AI Pro 1 ch. 14 | Spending AI time where the player looks / where it matters. |

## Collision avoidance and steering

| File (in `papers/`) | What it is | Why it matters here |
|---|---|---|
| `collision-avoidance-orca-vandenberg-2011.pdf` | van den Berg, Guy, Lin, Manocha, "Reciprocal n-body Collision Avoidance" (ORCA), Springer Tracts in Advanced Robotics, 2011. Source: gamma.cs.unc.edu | The original ORCA. Read together with the Game AI Pro chapter below. |
| `game-ai-pro/GameAIPro3_Chapter19_RVO_and_ORCA_How_They_Really_Work.pdf` | Game AI Pro 3 ch. 19 | Implementation-level explanation, pitfalls included. Note: ORCA is a floating-point linear-programming step, so a lockstep version needs the deterministic maths of wave 10a-1. |
| `game-ai-pro/GameAIPro2_Chapter18_Context_Steering_Behavior-Driven_Steering_at_the_Macro_Scale.pdf` | Game AI Pro 2 ch. 18 | Context steering: cheap and solver-free (so probably easier to keep deterministic than ORCA), aimed at vehicles with turn limits. |
| `game-ai-pro/GameAIPro2_Chapter19_Guide_to_Anticipatory_Collision_Avoidance.pdf` | Game AI Pro 2 ch. 19 | Time-to-collision based avoidance; handles the large-ship collisions the owner listed in `docs2RTS/appunti.org`. |

## Multiplayer, lockstep and determinism

| File (in `papers/`) | What it is | Why it matters here |
|---|---|---|
| `network-lockstep-aoe-1500-archers-2001.pdf` | Bettner, Terrano, "1500 Archers on a 28.8: Network Programming in Age of Empires", GDC 2001 (Gamasutra print). | The founding lockstep article: sync commands, not state; turn length vs latency; desync handling. Compare with `Lockstep/LockstepHooks.cs`. |
| `network-deterministic-lockstep-mieschke.pdf` | P. Mieschke, "Deterministic Lockstep in Networked Games", Stuttgart Media University. Source: hdms.bsz-bw.de | Background on lockstep variants; less authoritative than the previous one. |
| `determinism-cross-platform-frontier-gdc2024.pdf` | B. Pollard (Frontier), "Cross-Platform Determinism in Warhammer Age of Sigmar: Realms of Ruin", GDC 2024 slides. Source: media.gdcvault.com | A modern RTS postmortem: why rounding is not enough, tooling for desyncs, dedicated server. Use it as a checklist against ARCHITECTURE "Float determinism review". |

## Skirmish AI: combat, planning, maps

| File (in `papers/`) | What it is | Why it matters here |
|---|---|---|
| `rts-combat-model-based-heuristic-search-churchill-2017.pdf` | Churchill, "An Analysis of Model-Based Heuristic Search Techniques for StarCraft Combat Scenarios", AIIDE-17 workshop. | Compares search methods that pick targets and moves in a battle; the benchmark for any combat micro in the AI. |
| `rts-combat-fast-random-genetic-search-clark-2017.pdf` | Clark, Fleshner, "Fast Random Genetic Search for Large-Scale RTS Combat Scenarios", AIIDE-17. | A search that scales to large armies; read for the scale argument (our fights have hundreds of units). |
| `game-ai-pro/GameAIPro3_Chapter25_Combat_Outcome_Prediction_for_Real-Time_Strategy_Games.pdf` | Game AI Pro 3 ch. 25 | Predicting who wins a fight from unit stats (Lanchester-like and learned models). A direct input for "attack / retreat". |
| `game-ai-pro/GameAIPro3_Chapter30_Hierarchical_Portfolio_Search_in_Prismata.pdf` | Game AI Pro 3 ch. 30 | A strategy game AI that searches over *scripts* instead of raw actions: a way to add look-ahead on top of the existing build/attack rules. |
| `game-ai-pro/GameAIPro3_Chapter28_Pitfalls_and_Solutions_When_Using_Monte_Carlo_Tree_Search_for_Strategy_and_Tactical_Games.pdf` | Game AI Pro 3 ch. 28 | What goes wrong with MCTS in strategy games; read before anyone proposes MCTS for the skirmish AI. |
| `game-ai-pro/GameAIPro2_Chapter25_Monte_Carlo_Tree_Search_and_Related_Algorithms_for_Games.pdf` | Game AI Pro 2 ch. 25 | MCTS primer. |
| `game-ai-pro/GameAIPro2_Chapter29_Escaping_the_Grid_Infinite-Resolution_Influence_Mapping.pdf` | Game AI Pro 2 ch. 29 | Influence maps without a fixed grid. |
| `game-ai-pro/GameAIPro2_Chapter30_Modular_Tactical_Influence_Maps.pdf` | Game AI Pro 2 ch. 30 | Composable influence maps (threat, value, access) — the model for AI target selection and base-attack routing. |
| `game-ai-pro/GameAIPro2_Chapter31_Spatial_Reasoning_for_Strategic_Decision_Making.pdf` | Game AI Pro 2 ch. 31 | Strategic spatial reasoning (where to expand, where to defend). |
| `game-ai-pro/GameAIPro_Chapter09_An_Introduction_to_Utility_Theory.pdf` | Game AI Pro 1 ch. 9 | Utility scoring for choosing between actions; useful if the AI's rule lists keep growing. |
| `game-ai-pro/GameAIPro3_Chapter13_Choosing_Effective_Utility-Based_Considerations.pdf` | Game AI Pro 3 ch. 13 | How to pick and shape the inputs of utility scores. |
| `game-ai-pro/GameAIPro_Chapter12_Exploring_HTN_Planners_through_Example.pdf` | Game AI Pro 1 ch. 12 | Hierarchical task networks: build orders and expansion plans as tasks. |
| `game-ai-pro/GameAIPro_Chapter13_Hierarchical_Plan-Space_Planning_for_Multi-unit_Combat_Maneuvers.pdf` | Game AI Pro 1 ch. 13 | Planning multi-unit manoeuvres (flanking, pincer) — the "tactics" layer above attack waves. |

## Rendering

| File (in `papers/`) | What it is | Why it matters here |
|---|---|---|
| `rendering-cdlod-terrain-strugar-2010.pdf` | Strugar, "Continuous Distance-Dependent Level of Detail for Rendering Heightmaps (CDLOD)", 2010. Source: github.com/fstrugar/CDLOD | Terrain LOD with no popping, from the heightmap, GPU-side. The client already has terrain LOD levels (`--terrain-lod 0..3`); this is the reference for making the transitions continuous on the big 1024 maps. Needs vertex texture fetch (GL 3.3 has it). |

## Found, but not downloaded (link only)

Either the host refuses downloads from this machine, the page is a blog (copying a blog into the repo is not ours to do), or
the paper has no free copy.

| What | Link | Why it matters here |
|---|---|---|
| A. Ontañón et al., "A Survey of Real-Time Strategy Game AI Research and Competition in StarCraft" (IEEE TCIAIG 2013) | https://hal.science/hal-00871001 (the author copy at cs.mun.ca did not answer from here) | The overview paper of RTS AI: levels of decision (strategy / tactics / reactive control), what is solved. Read first if the AI is ever redesigned. |
| Churchill, Buro, "Portfolio Greedy Search and Simulation for Large-Scale Combat in StarCraft" (CIG 2013) | https://www.cs.mun.ca/~dchurchill/publications/pdf/combat13.pdf (host unreachable from here) | The method that the two AIIDE-17 papers above compare themselves with. |
| Botea, Müller, Schaeffer, "Near Optimal Hierarchical Path-Finding" (HPA*, 2004) | https://webdocs.cs.ualberta.ca/~mmueller/ps/hpastar.pdf (404 on 2026-11-14) | The original of our region pathing. |
| G. Fiedler, "Deterministic Lockstep" and "Floating Point Determinism" | https://gafferongames.com/post/deterministic_lockstep/ , https://gafferongames.com/post/floating_point_determinism/ | The clearest explanation of the rules our hard rule 1 follows. |
| F. Smith, "Synchronous RTS Engines 2: Sync Harder" | https://www.forrestthewoods.com/blog/synchronous_rts_engines_2_sync_harder/ | Practical desync-hunting stories from an RTS developer. |
| "How to RTS" blog: flow fields, continuum crowds | https://howtorts.github.io/2014/01/04/basic-flow-fields.html , https://howtorts.github.io/2014/01/09/continuum-crowds.html | A tutorial implementation of the SupCom 2 / Planetary Annihilation approach. |
| Spring / Recoil QTPFS (quad-tree pathfinder used by BAR) | https://springrts.com/wiki/QTPFS | How BAR's engine paths on a heightmap; compare with `NavGrid`. Code: see EXTERNAL.md. |
| GDC Vault, "Pathing in Age of Empires" | https://www.gdcvault.com/play/1027659/Pathing-in-Age-of-Empires | Talk page; availability of the video/slides unverified. |
| Gamasutra, "Cross-Platform RTS Synchronization and Floating-Point Indeterminism" | https://www.gamedeveloper.com/programming/cross-platform-rts-synchronization-and-floating-point-indeterminism | Article on floating-point indeterminism between platforms in an RTS; compare its conclusions with our choice (floats plus software maths, wave 10a-1). Not read in full. |
| FAF developer docs | https://faforever.github.io/fa/development | How the open-source Supreme Commander: FA community builds and tests the game. |

## Adding to the library

Put a free-to-distribute document in `papers/` named `<area>-<topic>-<author>-<year>.pdf` (Game AI Pro chapters keep their
original names in `papers/game-ai-pro/`), add one row to the right table here with the source and *why it matters here*, and
link it from the ARCHITECTURE section it informs. Check that the file opens (`pdfinfo FILE`): some hosts answer a download
with an HTML error page that has a `.pdf` name.

# External projects, tools and skills

Companion of [README.md](README.md) (papers). Checked 2026-11-14 (session e4dd3c13) through the GitHub API and by reading the
repos' skill lists; **nothing here is installed or copied into the project**: this is a catalogue with a verdict, so a later
session or the owner can decide.

## 1. Reference source code (read-only)

Open-source RTS games and engines whose code answers "how does a mature game do X". All are GPL or similar: **read, learn,
re-implement; never paste code into this repo without asking the owner** (this project's licence is the owner's choice and
GPL code would force it).

| Project | What it is | Size* | Licence | Use it for |
|---|---|---|---|---|
| **BAR — Beyond All Reason** (https://github.com/beyond-all-reason/Beyond-All-Reason) | The most active open-source Total-Annihilation-style RTS (Lua game on the Recoil engine). Units are Lua defs; huge unit/AI/UI content. | 4.3 GB with history; 5.2 GB shallow on disk | GitHub reports "other"; the code is GPL-2.0 per the project's own docs (not re-checked) | Unit stats and roles, balance, build trees, UI widgets, economy numbers; the closest game to ours in spirit. |
| **Recoil engine** (https://github.com/beyond-all-reason/RecoilEngine, ex-Spring) | The C++ engine under BAR: movement, QTPFS pathfinder, weapons, LOS, lockstep. | 208 MB | GitHub reports "other"; the Spring headers say GPL v2 or later | Pathing on heightmaps (`rts/Sim/Path/QTPFS`), ground units' "move types", sync-safe sim code. Wiki: https://springrts.com/wiki/QTPFS |
| **FAF — Forged Alliance Forever** (https://github.com/FAForever/fa) | The community-maintained Lua code of Supreme Commander: Forged Alliance: the game we copy. Blueprints and balance changes since 2007. | 1.27 GB | none stated on the repo | Real Supreme Commander numbers (health, speed, costs, weapon stats) for the "proportions to check" in the owner's notes; AI scripts. It does not hold every original file (base game archives load first). |
| **FAF unit database** (https://github.com/FAForever/etfreeman-db, successor of FAForever/spooky-db and FAForever/UnitDB) | Web app that parses the FAF blueprints into JSON (`index.json`, `index.fat.json`). | small | not checked | The easiest way to get all SupCom unit stats as data, to compare with `data/blueprints/units/*.json` and with `Rts.Bench -- duel`. |
| **OpenRA** (https://github.com/OpenRA/OpenRA) | An RTS engine in **C#** (.NET) for Command & Conquer-style games, with lockstep multiplayer, replays, modding by data. | 343 MB | GPL-3.0 | The one that is written in our language: sync/desync tooling, order/selection UI, replay and lobby code, trait-based unit definition. |
| 0 A.D. (Pyrogenesis) | Open-source historical RTS, C++ with a documented long-/short-range pathfinder and a JS AI ("Petra"). | not checked | GPL | Skirmish-AI structure (Petra); not checked further. |

\* GitHub API size with full history, 2026-11-14. Ask for a shallow clone instead:
`git clone --depth 1 URL` (BAR and FAF shrink a lot; units/ and AI/ are the interesting folders).

**BAR on the owner's disk.** The owner asked on 2026-11-14 for a local copy, and it is in `/run/media/marco/docs2RTS/BAR/`
(shallow clones, `--depth 1`, outside this repo, never to be committed here): `Beyond-All-Reason/` (the game, commit e6a51022 of
2026-11-14, 5.2 GB of which about 1 GB is sounds and music) and `RecoilEngine/` (the engine, commit 084d53c of 2026-11-13,
126 MB). Where to look: game `units/` (Lua unit defs, 5 MB), `luaui/` (UI widgets), `gamedata/`, `luarules/` (gadgets,
balance rules); engine `rts/Sim/Path/QTPFS` and `rts/Sim/Path/HAPFS` (the two pathfinders), `rts/Sim/MoveTypes`, `AI/`. Update
with `git -c safe.directory=<path> -C <repo> pull --depth 1` (the disk is a FUSE mount owned by root, so git refuses the repo
without `safe.directory`).

**Licences, exactly as the repo's `LICENSE.md` says.** Game *code*: GPL v2 (individual files may differ). Models, textures and
animations: mostly **CC-BY-NC-ND 4.0** (no commercial use, **no derivatives**), a few by one author CC-BY-SA 4.0; sounds, music,
icons and unit pictures have their own `license_*.txt`. So: reading and re-implementing ideas is fine; **do not copy models,
textures, icons, sounds or music into this project**, and do not paste GPL code without asking the owner. The same pointer is in
the agents' memory (`bar-source-location`). `docs2RTS/appunti.org` is the owner's list of things to fix (weapons' shots not
starting from the weapons, rendering of units, bumpy maps, proportions against the original game, collisions between big
ships).

## 2. Programs for development (candidates, none installed)

Rule 8 of CLAUDE.md: no system packages or toolchains; NuGet packages and .NET local tools are fine.

| Tool | What it does | Fit | Verdict |
|---|---|---|---|
| **Tracy** (https://github.com/wolfpld/tracy, frame profiler in C++; GitHub reports its licence as "other", I believe BSD-3, check before use) | Real-time frame profiler with zones, threads, allocations. | We have `--perf-log` and `BENCH_ALLOC`; Tracy would show per-system timings per frame across the sim thread and `Par.For` workers. It needs a C# client wrapper (third-party; not checked) and a viewer binary. | Worth a trial only if a perf wave starts; compare with the existing counters first (DEV_GUIDE "Profiling"). |
| **BenchmarkDotNet** (https://github.com/dotnet/BenchmarkDotNet, MIT, NuGet) | Micro-benchmarks with statistics and allocation counts. | Good for hot loops (path solve, separation, spatial hash) where `Rts.Bench` measures the whole tick. | Useful as a NuGet reference in a separate bench project; no change to the sim. |
| **CsCheck** (NuGet, property-based testing for C#; shown in the dotnet-skills list below) | Random-input tests with shrinking. | Fits invariants: "path never crosses blocked cells", "save → load → hash is equal", "the sim hash does not depend on thread count". | Candidate for a test module; verify the package before use. |
| **Slopwatch** (NuGet `Slopwatch.Cmd` as a .NET local tool, from the dotnet-skills repo) | Scans changes for "LLM reward hacking": skipped tests, `#pragma warning disable`, empty catch blocks, `Task.Delay` in tests. | Every change in this repo is written by AI agents, and the rule "tests must stay green" invites exactly these shortcuts. | **Best fit of the whole search.** Try it as a local tool (`.config/dotnet-tools.json`, version pinned) in a worktree and see how many false positives the 1,000+ existing tests give. |
| **Verify** (NuGet, snapshot testing) | Approves outputs against stored files. | Replays/saves/hashes are already golden-value tests (`GoldenStateHash`). | Low priority. |

## 3. Claude Code skills and plugins

A skill is a set of instructions the agent follows with the agent's own permissions, so it is code from a stranger:
**read it before installing, pin a version, and install only what a task needs.** The four repos below were cloned shallow into a
scratch folder and their `SKILL.md` files listed; none was installed. Counts are the number of `SKILL.md` files.

| Repo | Licence | Skills | What it is | Verdict |
|---|---|---|---|---|
| **Aaronontheweb/dotnet-skills** (https://github.com/Aaronontheweb/dotnet-skills) | MIT | 39 | .NET practice: coding standards, type design for performance, concurrency patterns, CRAP/coverage analysis, CsCheck, snapshot testing, Slopwatch, ILSpy decompile, project structure (`.slnx`, central package management). Many are server-side (Akka, Aspire, EF Core, Playwright-Blazor, email) and useless here. | **Take a few, not the plugin**: `slopwatch`, `csharp-type-design-performance`, `csharp-concurrency-patterns`, `crap-analysis`, `cscheck`, `project-structure`. Copy the text into `docs/` as lessons if they hold up, rather than installing 39 skills. Plugin command if wanted: `/plugin marketplace add https://github.com/Aaronontheweb/dotnet-skills`. |
| **anthropics/skills** (https://github.com/anthropics/skills) | per skill (some Apache-2.0, document skills proprietary) | 20 | Anthropic's example skills: document formats, `skill-creator`, `mcp-builder`, `webapp-testing` (Playwright), `frontend-design`, `claude-api`. | Nothing missing for this project: the document and skill-creator ones are already available in the session; `webapp-testing` is for browser apps, not a raylib client. |
| **Donchitos/Claude-Code-Game-Studios** (https://github.com/Donchitos/Claude-Code-Game-Studios) | MIT | 78 (+ about 49 agent definitions) | A whole studio simulation: design docs, sprints, QA, release checklists, for Unity/Unreal/Godot. | **Do not adopt.** It overlaps and competes with this project's own organisation (WORKFLOW.md, AGENT_RULES.md, the token ledger) and is engine-specific. Ideas worth a look: `balance-check`, `perf-profile`, `soak-test`, `test-flakiness`, `regression-suite`. |
| **Jeffallan/claude-skills** (https://github.com/Jeffallan/claude-skills) | MIT | 67 | Generic role skills (`game-developer`, `csharp-developer`, `dotnet-core-expert`, `debugging-wizard`, `test-master`, `code-reviewer`...). | Low marginal value over the repo's own docs (ARCHITECTURE, DEV_GUIDE). Skip unless a gap shows up. |

Other directories seen in the search results (claudepluginhub.com, claudemarketplaces.com, mdskills.ai) are aggregators of
the same kind of repos; the Unity plugin is the only game-engine one from an official marketplace, and we do not use Unity.

## 4. What to do next (proposal, nothing started)

1. Try Slopwatch in a worktree and report its false-positive rate (small, concrete, uses the main weakness of agent-written code).
2. Compare BAR's unit roles (`units/`) and Recoil's QTPFS/HAPFS pathfinders with ours, using the local copy.
3. Pull the FAF unit database to check the "proportions" item of the owner's notes with data instead of by eye.
4. Before the next pathing or AI wave, put the matching rows of [README.md](README.md) into the module briefs.

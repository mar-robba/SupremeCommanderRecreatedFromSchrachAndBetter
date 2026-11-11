# (8a) Missing units & walls — agent report (8a-mod-2, 2026-11-11)

**What works:** 9 new blueprints: UEF Mech Marine, UEF T2 stealth field generator, UEF/Cybran T3 sonars (upgrades of the T2 ones), UEF/Cybran T3 strategic missile subs (build and launch the nuke submerged and on the move, cruise missiles fire submerged), Cybran T3 carrier (Atlantis code on a ship: builds, docks, repairs and launches 16 aircraft), UEF/Cybran T1 walls (block paths, absorb enemy direct shots and beams, never auto-targeted). The Salem destroyer walks on land at 35 % speed (torpedoes held ashore), with a client hull lift and 4 legs. Models for every new shape. Faction roles are optional; the AI does not build these units.

**Try it:** `--scene units8a[:land|:walls|:naval|:salem]`; sandbox `spawn uef_t1_wall me at x,z`; Shift-drag line placement for walls (it already worked, nothing changed).

**Tests:** MissingUnitsRosterTests, WalkingShipTests (10), MissileSubTests (8), CarrierTests (6), WallTests (11), client WalkingShipRenderTests (6). Sim 569, Client 315, Net 27: all pass.

**Hotkeys:** none.

**Shared files:** one line each in GameApp.cs, GameSession.cs, UnitModels.cs (3 cases), UnitModels.Cybran.cs, UnitRenderer.cs, UnitRenderer.Cargo.cs. Small hooks in MovementSystem (land speed), Combat (Projectile/Targeting/Weapon systems, CombatCatalog), PropStore. Nothing in `Input/`.

**Numbers:** bench `2000 300` (runs alternated with main): main 2.05-2.10 ms/tick, branch 1.85-1.92. AI reference: Hard wins after 15:35, hash BBD1FF3C4E116A4A. The hash did not move after the fix or the merge; it differs from main only because the new files shift the blueprint indices (AI_TUNING note).

**Replay bug:** PropStore's alive list was a lazy cache that got saved. The slots past its count depended on who had read `AliveList` and when, not on the game itself. The Salem hooks changed the game's course, and that exposed it. Fix: the cache is now `[NoSave]` and rebuilt when read (DEV_GUIDE pitfall added).

**Known issues:** saves and replays from before this branch refuse to open after the new blueprints (expected). See the ROADMAP "later (8a)" items.

**Handoff review:** the note pointed straight at the bug and was accurate. Bisecting by file would not have found it, because the cause was in PropStore; comparing the two states field by field with reflection found it in one run. One client sub-agent stopped at its context limit; the next one found that the carrier deck spots were never used (`BaseShape` keeps "cy_carrier") and fixed it.

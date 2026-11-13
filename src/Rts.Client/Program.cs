using Rts.Client;

// Usage: Rts.Client                         -> main menu
//        Rts.Client --menu | --setup | --settings [--preset P --map N --players N]  -> start in that screen
//        Rts.Client --settings-tab audio|graphics|gameplay  -> settings screen on that tab (screenshots)
//                   [--autostart] (setup: start as soon as the preview is ready) [--flow-test] (scripted menu flow)
//        Rts.Client [--seed N] [--map 256|512|1024] [--players N] [--preset plains|lakes|highlands|canyon|archipelago]
//                   [--water 0..1] [--demo] [--size 1600x900] [--pause-menu] [--endscreen]   -> straight into a game
//                   [--biome temperate|desert|tundra|lava|tropical] (8b-3 visuals only; default = the map file's or temperate)
//                   [--faction ID] (local player's faction) [--factions a,b,...] (per slot in order; "random" allowed;
//                   ids = data/factions/*.json; also preset the --setup screen)
//                   [--options SPEC] (game options, e.g. victory=supremacy,cap=500,resources=high,fog=explored,speed=fast,
//                   cheats=off,prebuilt=on; default = cheats on; also presets the --setup screen)
//                   [--screenshot out.png --frames 120] [--camera x,z,distance]
//                   [--stress N] [--fxtest] [--gallery] [--novsync] [--debugwater H] [--nofog]
//                   [--nosound] [--sound] [--volume F] [--sfx-volume F] [--music-volume F] [--nomusic]
//                   [--perf-log] (prints fps, frame stages, allocations and sim tick once per second)
//                   [--terrain-lod N] (force terrain LOD level 0..3: 0 = full detail; default automatic)
//                   [--sandbox] (sandbox game: spawn panel F9, console F10/`) [--exec "spawn tank 100 enemy; wait 30; nuke 128,128"]
//                   (--exec runs sandbox console commands at the start and implies --sandbox; with --setup --sandbox: sandbox setup)
//        Rts.Client --saves | --replays                      -> the Load Game / Replays list (saves/, replays/ folders)
//                   --load FILE                            -> continue a saved game (.rtssave; sandbox saves keep F9/F10)
//                   --replay FILE [--seek TICK]            -> watch a replay (.rtsreplay), optionally from TICK
//                   [--save-at N] (quicksave at frame N) [--save-dialog] (pause menu + save dialog, screenshots)
//                   [--write-replay] (--screenshot runs don't write replays unless given)
//                   [--autosave-every TICKS] (autosave interval, default 3000 = 5 min of game time; also in --screenshot runs)
//                   [--help-overlay] (open the F1 help overlay at the start, screenshots)
// In game: F5 quicksave, F6 quickload, Esc → Save Game. Every game is recorded; the replay is written at the end/quit.
// Audio is off in --screenshot runs unless --sound is given. Audio and --nofog flags don't force a direct game.
//        Rts.Client --export-icon FILE.png                  -> writes the game icon (256x256 PNG) and exits, no window
//        Rts.Client --voice-test [--voice-only id,id] [--voice-lang it] [--volume F] [--nosound]
//                   -> (11b-1) plays every voice announcer line (id, text, length printed) and exits, no window
// Packaged builds (release.txt next to the exe) keep saves/replays in ~/.local/share/RtsGame; crashes are logged to
// ~/.local/share/RtsGame/crash.log in every mode (docs/DEV_GUIDE.md "Packaging").
Rts.Sim.Persistence.UserDirs.Apply();
if (AppIcon.TryExport(args)) return;
if (Rts.Client.Audio.VoiceTest.TryRun(args, out int voiceExit)) { Environment.ExitCode = voiceExit; return; }
CrashLog.Install(interactive: !args.Contains("--screenshot"));
try
{
GameApp.Run(ClientOptions.Parse(args));
}
catch (Exception e) { CrashLog.Record(e, "main thread"); throw; }

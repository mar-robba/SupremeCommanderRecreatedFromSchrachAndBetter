using Rts.Client;

// Usage: Rts.Client                         -> main menu
//        Rts.Client --menu | --setup | --settings [--preset P --map N --players N]  -> start in that screen
//                   [--autostart] (setup: start as soon as the preview is ready) [--flow-test] (scripted menu flow)
//        Rts.Client [--seed N] [--map 256|512|1024] [--players N] [--preset plains|lakes|highlands|canyon|archipelago]
//                   [--water 0..1] [--demo] [--size 1600x900] [--pause-menu] [--endscreen]   -> straight into a game
//                   [--screenshot out.png --frames 120] [--camera x,z,distance]
//                   [--stress N] [--fxtest] [--gallery] [--novsync] [--debugwater H] [--nofog]
//                   [--nosound] [--sound] [--volume F] [--sfx-volume F] [--music-volume F] [--nomusic]
//                   [--sandbox] (sandbox game: spawn panel F9, console F10/`) [--exec "spawn tank 100 enemy; wait 30; nuke 128,128"]
//                   (--exec runs sandbox console commands at the start and implies --sandbox; with --setup --sandbox: sandbox setup)
//        Rts.Client --saves | --replays                      -> the Load Game / Replays list (saves/, replays/ folders)
//                   --load FILE                            -> continue a saved game (.rtssave; sandbox saves keep F9/F10)
//                   --replay FILE [--seek TICK]            -> watch a replay (.rtsreplay), optionally from TICK
//                   [--save-at N] (quicksave at frame N) [--save-dialog] (pause menu + save dialog, screenshots)
//                   [--write-replay] (--screenshot runs don't write replays unless given)
// In game: F5 quicksave, F6 quickload, Esc → Save Game. Every game is recorded; the replay is written at the end/quit.
// Audio is off in --screenshot runs unless --sound is given. Audio and --nofog flags don't force a direct game.
GameApp.Run(ClientOptions.Parse(args));

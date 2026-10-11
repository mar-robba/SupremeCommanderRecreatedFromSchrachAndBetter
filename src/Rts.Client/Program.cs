using Rts.Client;

// Usage: Rts.Client                         -> main menu
//        Rts.Client --menu | --setup | --settings [--preset P --map N --players N]  -> start in that screen
//        Rts.Client [--seed N] [--map 256|512|1024] [--players N] [--preset plains|lakes|highlands|canyon|archipelago]
//                   [--water 0..1] [--demo] [--size 1600x900] [--pause-menu] [--endscreen]   -> straight into a game
//                   [--screenshot out.png --frames 120] [--camera x,z,distance]
//                   [--stress N] [--fxtest] [--gallery] [--novsync] [--debugwater H]
GameApp.Run(ClientOptions.Parse(args));

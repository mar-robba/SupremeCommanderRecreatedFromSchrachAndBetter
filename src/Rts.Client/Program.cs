using Rts.Client;

// Usage: Rts.Client [--seed N] [--map 256|512|1024] [--players N] [--demo] [--size 1600x900]
//                   [--screenshot out.png --frames 120] [--camera x,z,distance]
//                   [--stress N] [--fxtest] [--gallery] [--novsync] [--debugwater H]
GameApp.Run(ClientOptions.Parse(args));

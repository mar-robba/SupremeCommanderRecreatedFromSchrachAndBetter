# Audio and voice

Synthesized sound effects, ambient sound and music, alerts, and the spoken announcer.

**Contracts of this area**
- Everything is synthesized at startup; the only asset files are the voice lines.
- No audio device means silence, never a failure.
- The voice speaks one line at a time, by priority (`VoiceQueue`, pure). The wording lives in
  `data/audio/voice/lines.json`; the sound files are generated offline and committed.

Files and top-level types of this area: [index/audio.txt](index/audio.txt) (generated).

## Sound engine (from "Client")

- `Audio/`: everything synthesized at startup (no asset files, except the voice lines: "Voice announcer (11b-1: files)"). `Synth` (DSP layers) + `SoundBank` (recipes, variants,
  gains/voice limits) → raylib Sounds with alias voice pools (`AudioEngine`); `AmbientSynth` renders wind, generative
  music and reclaim/build hums into a real-time AudioStream; `GameAudio` maps snapshot `EffectEvent`s (incl.
  `WeaponIndex`) to positional one-shots (listener = camera focus, zoom ducking, pan, per-frame merging) and raises
  alerts (under attack, stall, unit ready). `UiSounds` = click/confirm/error requests. `--nosound`; no device = silent.
  `AudioSystem` (app-wide, created once by `GameApp`): device + sound bank (synthesized once) + ambient stream +
  `AudioSettings` volumes; each game's `GameAudio` drives it, menus call `MenuUpdate` (clicks + music). Volumes
  come from `AppSettings` (settings screen, saved) unless `--volume/--sfx-volume/--music-volume` are given.

## Voice announcer (11b-1: files)

Spoken lines for the events SupCom announces (gap G24). Owner decisions (2026-11-13): voice made with `espeak`, spoken
language Italian, every on-screen text stays English. Client only: no sim change, no new game events.
- Data: `data/audio/voice/lines.json` = the line table (id, `text.<lang>`, priority, cooldown s, maxWait s, default 4) and
  `data/audio/voice/<lang>/<id>.wav` (16-bit mono PCM, 22050 Hz = `VoiceLineTable.SampleRate`; raylib converts to the
  device rate). Generated offline by `tools/voice/generate.py` (DEV_GUIDE "Voice announcer"); the game needs no speech
  tool and `package.sh` ships `data/` whole. Only Italian ships; the layout is per language.
- `Audio/VoiceLines.cs` (table + `VoiceIds`, the ids the code raises; a test checks they equal the table),
  `VoiceWav.cs` (pure WAV reader: returns null, never throws), `VoiceQueue.cs` (pure, time passed in): one line at a time
  + `Gap` 0.3 s; the highest waiting priority goes next (ties: older), a line being spoken is never cut; waiting longer
  than its maxWait = dropped; per-line cooldown from the moment it is accepted; at most 4 waiting (a new line pushes out
  a less important one).
- `VoiceAnnouncer.cs` (owned by `AudioSystem.Voice`, updated in `AudioSystem.Update` with `Raylib.GetTime`): table read at
  once, files decoded on a worker thread, each uploaded (`LoadSoundFromWave`) the first time it is spoken. Missing /
  broken / not yet decoded file = duration 0 = silence (the beep stays). Volume = `AudioSettings.Voice` per sound under the
  engine master volume (Mute, `--nosound` also silence it); `VoiceOn` off clears the queue. Without a device it is inert
  (no table). `GameAudio.Dispose` calls `Voice.Reset()` (nothing from the old game is said later).
- Sources (`Audio/GameAudio.Voice.cs`): `Notify` (AlertMonitor alerts → `VoiceFor`: commander / units / base (toast text
  `AlertMonitor.StructuresUnderAttackText`) / ally under attack, structure lost, upgrade done, unit cap), the mass / energy
  stall warnings, `PlayExperimentalAlert` (enemy experimental), and `VoiceFrame` per new snapshot: new `Launches`
  (enemy = "nuclear launch detected", own / allied = "strategic missile launched"; the first snapshot only sets the
  baseline), visible effects (enemy `MissileLaunch` of Size < 2 = tactical; `UnitDeath` of a Commander-category unit =
  enemy / allied commander destroyed; own `BuildComplete`: same handle with another blueprint in the previous snapshot +
  Commander = enhancement complete, Experimental and not swapped = experimental complete) and `ArmyView.Defeated`
  turning true between consecutive snapshots (enemy / ally defeated, skipped for 15 s after that army's commander death
  was seen: enemy commander deaths under fog are not in the snapshot, so a defeat is the fallback).
- Settings: `AppSettings.VoiceOn` / `VoiceVolume` → `ApplyVoice` (also when `--volume` overrides the other volumes);
  Settings → Audio rows "Voice" + "Voice volume" (`Menus/SettingsScreen.Voice.cs`). `--voice-test` (`Audio/VoiceTest.cs`,
  no window): plays every line with id, text and length printed; `--voice-only id,id`, `--voice-lang`, `--nosound` = print.
- Tests: `tests/Rts.Client.Tests/VoiceTests.cs` (table = `VoiceIds`, every file valid: rate, mono, 0.6–4 s, RMS, no
  clipping, total size; queue rules; missing files / table silent; alert mapping).

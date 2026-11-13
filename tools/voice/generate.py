#!/usr/bin/env python3
"""(11b-1) Voice announcer generator: data/audio/voice/lines.json -> data/audio/voice/<lang>/<id>.wav

Run from the repo root (the volume has no exec bits, so always through python3):
    python3 -I tools/voice/generate.py                 # every line, every language in lines.json
    python3 -I tools/voice/generate.py --only mass_low  # one line (comma-separated list allowed)
    python3 -I tools/voice/generate.py --check          # no generation: print duration / RMS / peak of each file
Needs espeak and ffmpeg at generation time only (the game just loads the .wav files).
Then listen in game: dotnet run --project src/Rts.Client -- --voice-test

Every voice parameter is in the block below. Change it, regenerate, listen, commit the .wav files.
"""
import argparse
import array
import json
import math
import os
import subprocess
import sys
import tempfile
import wave

# ---------------------------------------------------------------- voice parameters (edit here)
# espeak voice per language: "<language>+<variant>". Variants (espeak-data/voices/!v): m1..m7 (male), f1..f5
# (female), klatt, klatt2..4, croak, whisper. "it+m3" = a calm, low male voice.
ESPEAK_VOICE = {"it": "it+m3"}
ESPEAK_SPEED = 150      # words per minute (espeak default 175); slower = clearer
ESPEAK_PITCH = 38       # 0..99 (default 50); lower = deeper
ESPEAK_AMPLITUDE = 120  # 0..200 (loudness is normalized afterwards anyway)
ESPEAK_WORD_GAP = 1     # extra pause between words, in units of 10 ms

# Output format the game expects (VoiceLines.SampleRate in src/Rts.Client/Audio/VoiceLines.cs; the tests check it).
# 22050 Hz mono 16-bit: the band-pass below keeps nothing above ~4.5 kHz, so a higher rate would only double the size.
SAMPLE_RATE = 22050

# ffmpeg clean-up, in order:
#  1. trim the silence espeak leaves at both ends (silenceremove, reversed for the tail);
#  2. "radio / computer" colour: band-pass 260 Hz - 4.2 kHz, gentle compression, a short metallic echo
#     (aecho in_gain:out_gain:delays_ms:decays) that stays well under the words;
#  3. normalize loudness (EBU R128, -16 LUFS, true peak -1.5 dB) so every line plays at the same level;
#  4. resample to SAMPLE_RATE and add 60 ms of tail silence so the echo is not cut.
TRIM = ("silenceremove=start_periods=1:start_threshold=-45dB:start_silence=0.02,"
        "areverse,silenceremove=start_periods=1:start_threshold=-45dB:start_silence=0.04,areverse")
COLOUR = ("highpass=f=260:poles=2,lowpass=f=4200:poles=2,"
          "acompressor=threshold=-20dB:ratio=3:attack=4:release=80:makeup=2,"
          "aecho=0.9:0.7:28|55:0.22|0.10")
LOUDNESS = "loudnorm=I=-16:TP=-1.5:LRA=11"
FILTER = f"aresample=44100,{TRIM},{COLOUR},{LOUDNESS},aresample={SAMPLE_RATE},apad=pad_dur=0.06"
# ---------------------------------------------------------------- end of parameters

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
VOICE_DIR = os.path.join(ROOT, "data", "audio", "voice")


def load_lines():
    with open(os.path.join(VOICE_DIR, "lines.json"), encoding="utf-8") as f:
        table = json.load(f)
    return table["languages"], table["lines"]


def run(cmd):
    r = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    if r.returncode != 0:
        sys.exit(f"failed ({r.returncode}): {' '.join(cmd)}\n{r.stderr[-2000:]}")


def generate(lang, line_id, text, tmp):
    raw = os.path.join(tmp, line_id + ".raw.wav")
    out_dir = os.path.join(VOICE_DIR, lang)
    os.makedirs(out_dir, exist_ok=True)
    out = os.path.join(out_dir, line_id + ".wav")
    run(["espeak", "-v", ESPEAK_VOICE[lang], "-s", str(ESPEAK_SPEED), "-p", str(ESPEAK_PITCH),
         "-a", str(ESPEAK_AMPLITUDE), "-g", str(ESPEAK_WORD_GAP), "-w", raw, text])
    run(["ffmpeg", "-y", "-hide_banner", "-loglevel", "error", "-i", raw, "-af", FILTER,
         "-ac", "1", "-ar", str(SAMPLE_RATE), "-c:a", "pcm_s16le", "-map_metadata", "-1", "-fflags", "+bitexact",
         "-flags:a", "+bitexact", out])
    return out


def stats(path):
    """(duration s, RMS dBFS, peak 0..1, sample rate, channels, bits)."""
    with wave.open(path, "rb") as w:
        rate, ch, width, n = w.getframerate(), w.getnchannels(), w.getsampwidth(), w.getnframes()
        data = array.array("h", w.readframes(n)) if width == 2 else array.array("h")
    if not data:
        return n / rate, -999.0, 0.0, rate, ch, width * 8
    peak = max(abs(s) for s in data) / 32768.0
    rms = math.sqrt(sum(s * s for s in data) / len(data)) / 32768.0
    return n / rate, 20 * math.log10(max(rms, 1e-9)), peak, rate, ch, width * 8


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--only", help="comma-separated line ids")
    ap.add_argument("--lang", help="one language (default: all in lines.json)")
    ap.add_argument("--check", action="store_true", help="only print the stats of the existing files")
    a = ap.parse_args()
    langs, lines = load_lines()
    if a.lang:
        langs = [a.lang]
    only = set(a.only.split(",")) if a.only else None
    total, bad = 0, 0
    with tempfile.TemporaryDirectory() as tmp:
        for lang in langs:
            for line in lines:
                if only and line["id"] not in only:
                    continue
                path = os.path.join(VOICE_DIR, lang, line["id"] + ".wav")
                if not a.check:
                    generate(lang, line["id"], line["text"][lang], tmp)
                if not os.path.exists(path):
                    print(f"MISSING {lang}/{line['id']}")
                    bad += 1
                    continue
                dur, rms, peak, rate, ch, bits = stats(path)
                size = os.path.getsize(path)
                total += size
                ok = 0.6 <= dur <= 4.0 and rms > -35 and peak < 0.999 and rate == SAMPLE_RATE and ch == 1 and bits == 16
                bad += 0 if ok else 1
                print(f"{'ok ' if ok else 'BAD'} {lang}/{line['id']:<28} {dur:5.2f} s  rms {rms:6.1f} dB  peak {peak:4.2f}"
                      f"  {rate} Hz {ch} ch {bits} bit  {size // 1024:4d} KB  \"{line['text'][lang]}\"")
    print(f"total {total / 1024:.0f} KB, {bad} problem(s)")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())

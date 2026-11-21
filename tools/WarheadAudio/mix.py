import argparse
import subprocess
from pathlib import Path

import numpy as np


RATE = 48000
MUSIC_TAIL = 4
SIREN_TRANSITION = {30: 3, 40: 15, 50: 26, 60: 37, 70: 49, 80: 49,
                    90: 60, 100: 70.3, 110: 81, 120: 91}
SECONDS_WORD = {30: .78, 40: .75, 50: .8, 60: .85, 70: .9, 80: .68,
                90: .78, 100: .94, 110: 1.38, 120: 1.44}


def read_mono(path):
    data = subprocess.check_output(["ffmpeg", "-v", "error", "-i", str(path),
                                    "-ar", str(RATE), "-ac", "1", "-f", "f32le", "-"])
    return np.frombuffer(data, dtype="<f4")


def write_ogg(path, samples):
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-f", "f32le", "-ar", str(RATE),
                    "-ac", "1", "-i", "-", "-c:a", "libvorbis", "-q:a", "5", str(path)],
                   input=samples.astype("<f4").tobytes(), check=True)


def add(layer, clip, at):
    offset = round(at * RATE)
    count = min(len(clip), len(layer) - offset)
    if count > 0:
        layer[offset:offset + count] += clip[:count]


def siren_curve(t, start, end, start_slope, end_slope):
    x = np.clip(t, 0, 1)
    return ((2*x**3 - 3*x**2 + 1)*start + (x**3 - 2*x**2 + x)*start_slope
            + (-2*x**3 + 3*x**2)*end + (x**3 - x**2)*end_slope)


def impact_stem(stems):
    impact = stems["warhead-finale"][66*RATE:70*RATE].copy()
    if len(impact) != MUSIC_TAIL*RATE or np.max(np.abs(impact)) < .1:
        raise ValueError("Missing native detonation at the 66-second marker")
    impact *= .91 / np.max(np.abs(impact))
    impact[:96] *= np.linspace(0, 1, 96)
    impact[-RATE:] *= np.linspace(1, 0, RATE)**2
    return impact


def limit(mixed):
    magnitude = np.abs(mixed)
    return np.sign(mixed) * (np.minimum(magnitude, .84)
                             + .12*np.tanh(np.maximum(magnitude-.84, 0)/.12))


def make_mix(stems, song, impact, mode, seconds):
    intro = {"start": 11.6, "resume": 7.2, "deadman": 14}[mode]
    blast = seconds + intro
    timeline = np.arange(round((blast + MUSIC_TAIL) * RATE)) / RATE
    voice = np.zeros(len(timeline), dtype=np.float32)
    siren = np.zeros_like(voice)
    add(voice, stems[mode], 0)
    if mode == "deadman":
        last_word = 14.8
    else:
        add(voice, stems[f"t-{seconds}"], intro)
        last_word = intro + SECONDS_WORD[seconds]
        change = intro + SIREN_TRANSITION[seconds]
        add(siren, stems["siren-start"], intro)
        siren *= siren_curve(timeline-change, 1, 0, -.06745, 0)
        final_siren = np.zeros_like(siren)
        add(final_siren, stems["siren-end"], change)
        siren += final_siren * siren_curve(timeline-change, 0, 1, 2.0508, .5151)
        siren *= np.clip((blast - .65 - timeline) / .12, 0, 1)

    music = np.zeros_like(voice)
    onset = max(round(last_word * RATE), len(music) - len(song))
    excerpt = song[-(len(music) - onset):]
    fade = np.clip((timeline[onset:] - onset/RATE) / 1.8, 0, 1)
    music[onset:] = excerpt * fade**2 * (3 - 2*fade)
    mixed = voice*.78 + siren*.50 + music*.77
    add(mixed, impact, blast)
    return limit(mixed), onset/RATE


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("stems", type=Path)
    parser.add_argument("song", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    names = ["start", "resume", "deadman", "siren-start", "siren-end", "warhead-finale"]
    names += [f"t-{n}" for n in SIREN_TRANSITION]
    stems = {name: read_mono(args.stems / (name + ".wav")) for name in names}
    song = read_mono(args.song)
    impact = impact_stem(stems)
    cues = []
    for mode, variants in [("start", range(70, 121, 10)), ("resume", range(30, 101, 10)),
                           ("deadman", [90])]:
        for n in variants:
            mixed, onset = make_mix(stems, song, impact, mode, n)
            path = args.output / f"{mode}-{n}.ogg"
            write_ogg(path, mixed)
            audio = read_mono(path)
            if len(audio) != len(mixed) or not np.isfinite(audio).all():
                raise ValueError(f"Invalid encoded timeline: {path.name}")
            cues.append(f"{path.stem}\t{onset:.6f}\n")
            print(f"{path.stem}: music {onset:.2f}, blast {len(audio)/RATE-MUSIC_TAIL:.2f}, end {len(audio)/RATE:.2f}")
    (args.output / "music-cues.tsv").write_text("".join(cues), encoding="ascii")


if __name__ == "__main__":
    main()

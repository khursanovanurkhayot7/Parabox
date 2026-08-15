#!/usr/bin/env python3
"""Generate the prebuilt Hayot's Parabox neon-arcade music and interaction WAVs."""

from array import array
import math
from pathlib import Path
import struct
import wave


ROOT = Path(__file__).resolve().parents[1]
MUSIC_RATE = 32000
SFX_RATE = 44100
BPM = 140.0


def midi(note):
    return 440.0 * (2.0 ** ((note - 69) / 12.0))


def noise(value):
    value &= 0xFFFFFFFF
    value ^= (value << 13) & 0xFFFFFFFF
    value ^= value >> 17
    value ^= (value << 5) & 0xFFFFFFFF
    return (value & 0xFFFFFFFF) / 0xFFFFFFFF * 2.0 - 1.0


def smooth(value):
    return value * value * (3.0 - 2.0 * value)


def mix(left, right, index, sample, pan=0.0):
    if not 0 <= index < len(left):
        return
    pan = max(-1.0, min(1.0, pan))
    left[index] += sample * math.sqrt((1.0 - pan) * 0.5)
    right[index] += sample * math.sqrt((1.0 + pan) * 0.5)


def add_tone(left, right, start, duration, frequency, gain, pan, timbre):
    first = max(0, round(start * MUSIC_RATE))
    count = round(duration * MUSIC_RATE)
    phase = 0.0
    for n in range(count):
        index = first + n
        if index >= len(left):
            break
        k = n / max(1, count - 1)
        phase += 2.0 * math.pi * frequency / MUSIC_RATE
        attack = min(1.0, k * (18.0 if timbre == 2 else 45.0))
        release = (1.0 - k) ** (2.4 if timbre == 1 else 1.25)
        if timbre == 1:
            sample = math.sin(phase) + 0.45 * math.sin(phase * 2.0) + 0.20 * math.sin(phase * 3.0)
        elif timbre == 2:
            sample = math.sin(phase) + 0.28 * math.sin(phase * 0.5) + 0.18 * math.sin(phase * 2.0)
        else:
            sample = math.sin(phase) + 0.25 * math.sin(phase * 2.0) + 0.10 * math.sin(phase * 4.0)
        mix(left, right, index, sample * attack * release * gain, pan)


def add_kick(left, right, start, gain):
    first = round(start * MUSIC_RATE)
    phase = 0.0
    for n in range(round(0.19 * MUSIC_RATE)):
        index = first + n
        if index >= len(left):
            break
        t = n / MUSIC_RATE
        frequency = 145.0 + (48.0 - 145.0) * min(1.0, t / 0.12)
        phase += 2.0 * math.pi * frequency / MUSIC_RATE
        mix(left, right, index, math.sin(phase) * math.exp(-t * 19.0) * gain)


def add_snare(left, right, start, gain, seed):
    first = round(start * MUSIC_RATE)
    for n in range(round(0.13 * MUSIC_RATE)):
        index = first + n
        if index >= len(left):
            break
        t = n / MUSIC_RATE
        body = math.sin(2.0 * math.pi * 185.0 * t) * 0.32
        sample = (noise(n + seed * 193) * 0.76 + body) * math.exp(-t * 27.0) * gain
        mix(left, right, index, sample, 0.08)


def add_hat(left, right, start, gain, seed):
    first = round(start * MUSIC_RATE)
    previous = 0.0
    for n in range(round(0.045 * MUSIC_RATE)):
        index = first + n
        if index >= len(left):
            break
        t = n / MUSIC_RATE
        value = noise(n + seed * 271)
        high = value - previous * 0.82
        previous = value
        mix(left, right, index, high * math.exp(-t * 82.0) * gain, -0.35 if seed % 2 == 0 else 0.35)


def make_music():
    beat = 60.0 / BPM
    samples = round(MUSIC_RATE * beat * 4.0 * 16.0)
    left = array("f", [0.0]) * samples
    right = array("f", [0.0]) * samples
    chords = ((62, 66, 69, 74), (59, 62, 66, 71), (55, 59, 62, 67), (57, 61, 64, 69))
    bass = (38, 35, 31, 33)
    melody = (
        (74, 78, 81, 78, 76, 74, 69, 71),
        (71, 74, 78, 74, 73, 71, 69, 66),
        (67, 71, 74, 79, 78, 74, 71, 69),
        (69, 73, 76, 81, 78, 76, 73, 71),
    )
    for bar in range(16):
        progression = bar % 4
        bar_start = bar * beat * 4.0
        for pulse in range(4):
            at = bar_start + pulse * beat
            add_kick(left, right, at, 0.66)
            if pulse in (1, 3):
                add_snare(left, right, at, 0.34, bar * 17 + pulse)
            add_tone(left, right, at, beat * 0.72, midi(bass[progression]), 0.19, 0.0, 2)
        for eighth in range(8):
            at = bar_start + eighth * beat * 0.5
            add_hat(left, right, at, 0.105 if eighth % 2 == 0 else 0.075, bar * 97 + eighth)
            chord_tone = chords[progression][(eighth + bar) % 4] + 12
            add_tone(left, right, at, beat * 0.34, midi(chord_tone), 0.075,
                     -0.48 if eighth % 2 == 0 else 0.48, 1)
            if bar >= 2 and not (bar >= 8 and eighth == 6):
                note = melody[(bar // 4) % len(melody)][eighth]
                if bar >= 12 and eighth in (2, 6):
                    note += 12
                add_tone(left, right, at, beat * 0.39, midi(note), 0.12, (eighth - 3.5) * 0.055, 0)
        if bar % 4 == 3:
            add_kick(left, right, bar_start + beat * 3.50, 0.34)
            add_kick(left, right, bar_start + beat * 3.75, 0.27)
    peak = max(max(abs(value) for value in left), max(abs(value) for value in right))
    scale = 0.84 / peak
    for index in range(samples):
        left[index] = math.tanh(left[index] * scale * 1.08) / 1.08
        right[index] = math.tanh(right[index] * scale * 1.08) / 1.08
    edge = round(MUSIC_RATE * 0.008)
    for index in range(edge):
        fade_in = index / edge
        fade_out = (edge - 1 - index) / edge
        left[index] *= fade_in
        right[index] *= fade_in
        end = samples - edge + index
        left[end] *= fade_out
        right[end] *= fade_out
    return left, right


def move_voice(variant):
    duration = 0.105 + variant * 0.006
    count = round(duration * SFX_RATE)
    data = array("f", [0.0]) * count
    phase = 0.0
    start = 350.0 + variant * 42.0
    end = 680.0 + variant * 55.0
    for index in range(count):
        k = index / (count - 1)
        frequency = start + (end - start) * smooth(k)
        phase += 2.0 * math.pi * frequency / SFX_RATE
        envelope = min(1.0, k * 30.0) * (1.0 - k) ** 2.1
        vowel = math.sin(phase) + 0.34 * math.sin(phase * 2.02) + 0.13 * math.sin(phase * 3.97)
        data[index] = vowel * envelope * 0.40
    return data


def push_voice():
    count = round(0.17 * SFX_RATE)
    data = array("f", [0.0]) * count
    phase = 0.0
    for index in range(count):
        k = index / (count - 1)
        phase += 2.0 * math.pi * (300.0 + (150.0 - 300.0) * smooth(k)) / SFX_RATE
        envelope = min(1.0, k * 24.0) * (1.0 - k) ** 1.7
        voice = math.sin(phase) + 0.38 * math.sin(phase * 0.5)
        thump = math.sin(2.0 * math.pi * 68.0 * index / SFX_RATE) * math.exp(-k * 8.0)
        data[index] = (voice * 0.34 + thump * 0.24) * envelope
    return data


def button_sound():
    count = round(0.105 * SFX_RATE)
    data = array("f", [0.0]) * count
    for index in range(count):
        t = index / SFX_RATE
        click = noise(index + 9001) * math.exp(-t * 110.0) * 0.24
        first = math.sin(2.0 * math.pi * 720.0 * t) * math.exp(-t * 35.0)
        local = max(0.0, t - 0.028)
        second = math.sin(2.0 * math.pi * 1080.0 * local) * math.exp(-local * 38.0) if t >= 0.028 else 0.0
        data[index] = click + first * 0.28 + second * 0.24
    return data


def hover_sound():
    count = round(0.034 * SFX_RATE)
    return array("f", (math.sin(2.0 * math.pi * 1180.0 * i / SFX_RATE)
                       * math.exp(-(i / SFX_RATE) * 95.0) * 0.22 for i in range(count)))


def pcm(value):
    return round(max(-1.0, min(1.0, value)) * 32767.0)


def write_wav(path, left, rate, right=None):
    path.parent.mkdir(parents=True, exist_ok=True)
    channels = 2 if right is not None else 1
    frames = bytearray()
    for index, value in enumerate(left):
        frames.extend(struct.pack("<h", pcm(value)))
        if right is not None:
            frames.extend(struct.pack("<h", pcm(right[index])))
    with wave.open(str(path), "wb") as output:
        output.setnchannels(channels)
        output.setsampwidth(2)
        output.setframerate(rate)
        output.writeframes(frames)


def main():
    music_dir = ROOT / "Assets/Parabox/Resources/Music"
    sfx_dir = ROOT / "Assets/Parabox/Resources/Sfx"
    left, right = make_music()
    write_wav(music_dir / "NeonPuzzleParty.wav", left, MUSIC_RATE, right)
    for variant in range(4):
        write_wav(sfx_dir / f"MoveVoice_0{variant + 1}.wav", move_voice(variant), SFX_RATE)
    write_wav(sfx_dir / "PushVoice.wav", push_voice(), SFX_RATE)
    write_wav(sfx_dir / "ButtonFun.wav", button_sound(), SFX_RATE)
    write_wav(sfx_dir / "HoverFun.wav", hover_sound(), SFX_RATE)
    print("Generated NeonPuzzleParty.wav and 7 interaction WAVs.")


if __name__ == "__main__":
    main()

"""Extract stereo F2004 engine loops from the supplied Jeddah lap.

Usage: python make_banks.py recording.mp3 output_directory
Dependencies: pygame, numpy, scipy. Each loop uses 384 samples per two crank
revolutions at 57,600 Hz, so all banks play at unity pitch at 18,000 RPM.
"""
import argparse
import os
from pathlib import Path

import numpy as np
from scipy import ndimage, signal
from scipy.io import wavfile

os.environ.setdefault("SDL_AUDIODRIVER", "dummy")
import pygame

RATE = 44100
CYCLE = 384
# name, start/end seconds, expected RPM at either end, number of whole cycles
PASSAGES = [
    ("Pickup", 11.48, 12.24, 9600, 12500, 42),
    ("Pull", 38.91, 40.24, 13600, 16300, 114),
    ("Straight", 4.82, 7.14, 18000, 18500, 270),
    ("Overrun", 9.12, 9.95, 12600, 11100, 54),
    ("Lift", 18.38, 19.04, 15900, 14650, 50),
]


def track_cycles(clip, first, last, cycles):
    mono = clip.mean(axis=1)
    window = np.hanning(2048)
    fft_hz = np.fft.rfftfreq(16384, 1 / RATE)
    times, rates = [], []
    for index in range(0, len(clip) - len(window), 128):
        center = index + len(window) / 2
        estimate = (first + (last - first) * center / len(clip)) / 120
        candidates = np.arange(estimate - 11, estimate + 11, 0.12)
        spectrum = abs(np.fft.rfft(mono[index:index + len(window)] * window, 16384))
        orders = np.array([3, 5, 7, 9, 10, 13, 15, 20])
        peaks = np.interp(candidates[:, None] * orders, fft_hz, spectrum)
        rates.append(candidates[np.log1p(peaks).mean(axis=1).argmax()])
        times.append(center)
    rate = np.interp(np.arange(len(clip)), times, ndimage.median_filter(rates, 5))
    phase = np.cumsum(rate) / RATE
    # The strong fifth order (one exhaust bank) resolves sub-cycle clock drift.
    baseband = mono * np.exp(-2j * np.pi * 5 * phase)
    carrier = signal.sosfiltfilt(signal.butter(2, 30, fs=RATE, output="sos"), baseband)
    phase += np.unwrap(np.angle(carrier)) / (2 * np.pi * 5)
    start = np.ceil(phase[int(.065 * RATE)])
    fade = 4 * CYCLE
    grid = start + np.arange(cycles * CYCLE + fade) / CYCLE
    if grid[-1] >= phase[-1] or np.any(np.diff(phase) <= 0):
        raise ValueError("Passage has insufficient clean crank cycles")
    take = np.column_stack([np.interp(grid, phase, channel) for channel in clip.T])
    fade_in = np.linspace(0, 1, fade, endpoint=False)[:, None]
    take[:fade] = take[:fade] * fade_in + take[-fade:] * (1 - fade_in)
    return take[:-fade]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("recording", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    pygame.mixer.init(frequency=RATE, size=-16, channels=2)
    audio = pygame.sndarray.array(pygame.mixer.Sound(str(args.recording))).astype(float) / 32768
    # Remove the microphone rumble before slowing any passage for low-speed driving.
    audio = signal.sosfiltfilt(signal.butter(3, 120, fs=RATE, btype="highpass", output="sos"), audio, axis=0)
    banks = {}
    for name, start, end, first, last, cycles in PASSAGES:
        banks[name] = (57600, track_cycles(audio[int(start*RATE):int(end*RATE)], first, last, cycles))
    for name, start, end in [("Upshift", 1.206, 1.426), ("Downshift", 8.099, 8.257)]:
        shot = audio[int(start*RATE):int(end*RATE)].copy()
        fade = np.linspace(0, 1, int(.014 * RATE))[:, None]
        shot[:len(fade)] *= fade
        shot[-len(fade):] *= fade[::-1]
        banks[name] = (RATE, shot)
    gain = .94 / max(np.max(np.abs(clip)) for _, clip in banks.values())
    for name, (rate, clip) in banks.items():
        wavfile.write(args.output / (name + ".wav"), rate, np.int16(32767 * gain * clip))


if __name__ == "__main__":
    main()

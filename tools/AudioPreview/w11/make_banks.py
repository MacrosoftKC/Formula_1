"""Extract the W11 engine banks from the supplied Assetto Corsa W11 hotlap of Albert Park (1:18.551) MP3.

python make_banks.py <hotlap.mp3> <wav-output-dir>
Requires pygame, numpy and scipy. Pack the resulting WAVs with pack_banks.gd.

Every loop is resampled onto one crank-angle clock (512 samples per four-stroke cycle,
51200 Hz = unity pitch at 12,000 RPM), so the game can crossfade them at a shared pitch.
Loops keep their recorded level relative to each other (one global gain for all banks),
so the in-game mix inherits the reference's loudness for each RPM, gear and throttle state.
"""
import argparse
import os
from pathlib import Path

import numpy as np
from scipy import ndimage, signal
from scipy.io import wavfile

SOURCE_RATE = 44100
CYCLE = 512                        # one four-stroke cycle, two crank revolutions
PLAYBACK_RATE = 51200              # unity pitch at 12,000 RPM
ORDERS = np.array([2, 3, 5, 6, 7, 9, 11, 12, 15])
PEAK = 0.94

# Clips are (start s, end s, RPM at start, RPM at end) in the supplied MP3. Each stays inside
# one gear, clear of gear changes and downshift blips. Short corner-exit sweeps are pooled and
# re-sequenced cycle by cycle into longer loops.
BANKS = {
    # full throttle out of the slow corners (T3, T11-12, T13 and T1): low gears, little wind
    "LowA": [(69.640, 70.100, 6290, 7520), (17.420, 17.950, 7010, 7560)],                       # T13, T3
    "LowB": [(70.050, 70.450, 7500, 8480), (17.950, 18.420, 7560, 8900), (60.000, 60.400, 8020, 8860)],
    "LowC": [(70.400, 70.720, 8420, 9580), (18.300, 18.600, 8700, 9450), (60.300, 60.600, 8670, 9250)],
    "LowD": [(70.700, 71.020, 9450, 10500), (60.720, 61.080, 9420, 10250), (18.600, 18.880, 9450, 10200)],
    "LowE": [(61.100, 61.600, 10370, 11630), (71.350, 72.000, 10720, 11560),
             (20.350, 20.750, 10700, 11460), (6.550, 7.200, 10480, 11420)],
    "LowF": [(61.600, 61.990, 11630, 12300), (72.000, 72.780, 11560, 12330),
             (7.250, 8.000, 11480, 12300), (73.200, 73.660, 11230, 12170)],
    "LowTop": [(20.950, 21.300, 12350, 13240), (21.750, 22.180, 12330, 13020)],              # T3 exit over-rev
    # full throttle in 8th gear on the straights: road and wind noise at 280-320 km/h
    "HighZ": [(48.300, 50.400, 10000, 10140)],
    "HighA": [(37.100, 38.600, 10760, 11090), (1.100, 1.700, 11070, 11300)],
    "HighB": [(39.300, 41.600, 11190, 11620), (1.700, 3.900, 11300, 11680), (13.000, 14.250, 11150, 11430)],
    "HighC": [(43.300, 47.120, 11790, 11995)],                                               # flat out
    # off throttle between the downshift blips: overrun and engine braking
    "CoastLow": [(68.200, 68.850, 8000, 7000), (58.850, 59.520, 8440, 7620),
                 (16.350, 16.850, 8200, 7570), (81.600, 82.350, 9500, 9140)],
    "CoastHigh": [(80.000, 80.380, 11150, 10780), (47.300, 47.980, 11350, 10450), (29.500, 29.970, 10550, 10080)],
}
# Formant-preserving extensions beyond the recorded range (the lap never idles).
EXTEND = [
    # name,     source,     target RPM
    ("LowZ",    "LowA",     5500),
    ("Idle",    "CoastLow", 4600),
    ("HighTop", "HighC",   13600),
]
# Shift transients at their recorded speed: a few ms before the gear change to the end of the
# torque interruption (upshift), or from just before the rev-matching blip to where it settles.
#                        shift time before  after
SHIFTS = [
    ("UpshiftLow",    62.041, 0.006, 0.120),   # T12 exit, 12,340 -> 11,000 RPM
    ("UpshiftHigh",   36.808, 0.006, 0.120),   # into 8th before T9, 12,240 -> 10,760 RPM
    ("DownshiftLow",  66.265, 0.012, 0.095),   # T13 braking, flares then settles near 10,800 RPM
    ("DownshiftHigh",  4.168, 0.012, 0.095),   # T1 braking from 300 km/h, settles near 10,900 RPM
]
ALIGN = [  # each bank is phase-aligned with the one it crossfades with
    ("LowA", None), ("LowB", "LowA"), ("LowC", "LowB"), ("LowD", "LowC"), ("LowE", "LowD"),
    ("LowF", "LowE"), ("LowTop", "LowF"), ("HighZ", "LowD"), ("HighA", "HighZ"), ("HighB", "HighA"),
    ("HighC", "HighB"), ("CoastLow", "LowB"), ("CoastHigh", "LowD"),
]


def read_mp3(path):
    os.environ.setdefault("SDL_AUDIODRIVER", "dummy")
    import pygame
    pygame.mixer.init(frequency=SOURCE_RATE, size=-16, channels=2, allowedchanges=0)
    try:
        return pygame.sndarray.array(pygame.mixer.Sound(str(path))).astype(float) / 32768
    finally:
        pygame.mixer.quit()


def crank_phase(clip, start_rpm, end_rpm):
    window = 2304
    centers = np.arange(window // 2, len(clip) - window // 2, 128)
    frequency = []
    taper = np.hanning(window)[:, None]
    for center in centers:
        block = clip[center - window // 2:center + window // 2] * taper
        spec = abs(np.fft.rfft(block, n=16384, axis=0)).mean(axis=1)
        expected = (start_rpm + (end_rpm - start_rpm) * center / len(clip)) / 120
        candidates = np.arange(expected - 5, expected + 5, 0.10)
        amplitudes = np.interp(candidates[:, None] * ORDERS * 16384 / SOURCE_RATE,
                               np.arange(len(spec)), spec)
        frequency.append(candidates[np.mean(np.log(0.35 + amplitudes), axis=1).argmax()])
    hz = np.interp(np.arange(len(clip)), centers, ndimage.median_filter(frequency, 7))
    phase = np.cumsum(hz) / SOURCE_RATE
    # Use the most coherent channel/order to refine fractional-cycle timing.
    demod = signal.butter(2, 18, fs=SOURCE_RATE, output="sos")
    best_strength, residual, best_order = 0, None, 0
    for order in (3, 6, 9, 15):
        carrier = np.exp(-2j * np.pi * phase * order)
        envelope = signal.sosfiltfilt(demod, clip * carrier[:, None], axis=0)
        strength = np.median(abs(envelope), axis=0)
        channel = strength.argmax()
        if strength[channel] > best_strength:
            best_strength = strength[channel]
            best_order = order
            residual = np.unwrap(np.angle(envelope[:, channel]))
    return np.maximum.accumulate(phase + residual / (2 * np.pi * best_order))


def crank_resample(clip, start_rpm, end_rpm):
    """Whole cycles of the clip on the crank clock, without the edges the tracker can't see."""
    phase = crank_phase(clip, start_rpm, end_rpm)
    pad = int(SOURCE_RATE * 0.028)
    origin = np.ceil(phase[pad])
    cycles = int(phase[-pad] - origin)
    grid = origin + np.arange(cycles * CYCLE) / CYCLE
    return np.column_stack([np.interp(grid, phase, channel) for channel in clip.T])


def close_loop(stereo):
    cycles = len(stereo) // CYCLE
    overlap = max(3, min(8, cycles // 5)) * CYCLE
    end = len(stereo) - overlap
    fade = np.sin(0.5 * np.pi * np.arange(overlap) / overlap)[:, None]**2
    stereo = stereo.copy()
    stereo[:overlap] = stereo[end:] * (1 - fade) + stereo[:overlap] * fade
    return stereo[:end]


def grain_loop(sources, cycles_out, grain, overlap, seed):
    """Re-sequence whole crank cycles from one or more sweeps into a seamless loop.
    Grains start on the same crank angle, so the firing pulses stay coherent across each
    crossfade, while the order of the grains (and the noise between pulses) never repeats
    inside the loop. The loop is far longer than any single short chicane-exit sweep."""
    rng = np.random.default_rng(seed)
    hop = grain - overlap
    assert cycles_out % hop == 0
    starts = [(s, c) for s, src in enumerate(sources) for c in range(len(src) // CYCLE - grain + 1)]
    out = np.zeros((cycles_out * CYCLE, 2))
    ramp = np.sin(0.5 * np.pi * np.arange(overlap * CYCLE) / (overlap * CYCLE))**2
    shape = np.concatenate([ramp, np.ones((grain - 2 * overlap) * CYCLE), ramp[::-1]])[:, None]
    recent = []
    for k in range(cycles_out // hop):
        choices = [i for i in range(len(starts)) if i not in recent] or list(range(len(starts)))
        pick = choices[rng.integers(len(choices))]
        recent = (recent + [pick])[-max(1, min(len(starts) // 2, 6)):]
        s, c = starts[pick]
        piece = sources[s][c * CYCLE:(c + grain) * CYCLE] * shape
        at = (k * hop * CYCLE + np.arange(grain * CYCLE)) % len(out)
        out[at] += piece
    return out


def template(loop):
    return loop.reshape(-1, CYCLE, 2).mean(axis=0)


def align(loop, reference):
    if reference is None:
        return loop
    score = np.fft.irfft((np.fft.rfft(reference, axis=0) *
                          np.fft.rfft(template(loop), axis=0).conj()).sum(axis=1), n=CYCLE)
    return np.roll(loop, int(score.argmax()), axis=0)


def formant_shift(loop, ratio):
    """Re-voice a crank-clock loop for `ratio` times its RPM, keeping its spectral envelope fixed
    in Hz: the airbox, exhaust and cockpit resonances stay put while the firing orders move."""
    n, hop = 2048, 256                        # 4 cycles per frame; loop lengths are multiples of hop
    window = np.hanning(n + 1)[:-1]
    padded = np.concatenate([loop[-n:], loop, loop[:n]])
    out = np.zeros_like(padded)
    norm = np.zeros(len(padded))
    bins = np.arange(n // 2 + 1)
    lifter = np.zeros(n)
    lifter[:96] = 1
    lifter[-95:] = 1                          # smoother than the 3rd-order harmonic spacing
    for start in range(0, len(padded) - n + 1, hop):
        frame = padded[start:start + n] * window[:, None]
        spectrum = np.fft.rfft(frame, axis=0)
        log_mag = np.log(np.abs(spectrum).mean(axis=1) + 1e-9)
        envelope = np.fft.rfft(np.fft.irfft(log_mag, n) * lifter).real
        warped = np.interp(bins * ratio, bins, envelope)
        spectrum *= np.exp(warped - envelope)[:, None]
        out[start:start + n] += np.fft.irfft(spectrum, n, axis=0) * window[:, None]
        norm[start:start + n] += window ** 2
    return (out / np.maximum(norm, 1e-9)[:, None])[n:n + len(loop)]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mp3", type=Path)
    parser.add_argument("out", type=Path)
    args = parser.parse_args()
    args.out.mkdir(exist_ok=True, parents=True)
    source = read_mp3(args.mp3)
    filt = signal.butter(3, [85, 13500], fs=SOURCE_RATE, btype="bandpass", output="sos")

    def section(t0, t1):
        segment = source[round(t0 * SOURCE_RATE):round(t1 * SOURCE_RATE)]
        return signal.sosfiltfilt(filt, segment, axis=0)

    loops, centre = {}, {}
    for seed, (name, partner) in enumerate(ALIGN):
        clips = BANKS[name]
        sweeps = [crank_resample(section(t0, t1), lo, hi) for t0, t1, lo, hi in clips]
        reference = template(loops[partner]) if partner else None
        # Align every sweep to the partner bank (or the first sweep) before mixing grains.
        anchor = reference if reference is not None else template(sweeps[0])
        sweeps = [align(s, anchor) for s in sweeps]
        if name == "HighC":
            loop = close_loop(sweeps[0])               # long and steady: keep it whole
        elif name.startswith("High"):
            loop = grain_loop(sweeps, 192, 16, 4, seed)
        else:
            loop = grain_loop(sweeps, 64, 6, 2, seed)
        loops[name] = align(loop - loop.mean(axis=0), anchor)
        centre[name] = float(np.exp(np.mean([np.log(lo * hi) / 2 for _, _, lo, hi in clips])))
    for name, src, rpm in EXTEND:
        shifted = formant_shift(loops[src], rpm / centre[src])
        # Moving the envelope down piles energy into the low orders; keep the source's loudness.
        loops[name] = shifted * np.sqrt(np.mean(loops[src] ** 2) / np.mean(shifted ** 2))
        centre[name] = float(rpm)

    # One gain for every bank, set by the loudest peak.
    gain = PEAK / max(np.max(abs(loop)) for loop in loops.values())
    for name, loop in loops.items():
        wavfile.write(args.out / (name + ".wav"), PLAYBACK_RATE, np.round(loop * gain * 32767).astype("<i2"))
        rms = 20 * np.log10(np.sqrt(np.mean((loop * gain) ** 2)))
        print(f"{name:10s} {len(loop) // CYCLE:4d} cycles  centre {centre[name]:6.0f} RPM  {rms:6.1f} dBFS")

    # The lap stays below the game's hard limiter. Apply cylinder cuts to the over-rev timbre
    # on the audio clock, with an integer number of cut periods so the pattern loops.
    body = loops["LowTop"] * gain
    phase = np.arange(len(body)) / len(body) * round(len(body) / (4 * CYCLE))
    cut = np.clip((phase % 1 - 0.46) * 12, 0, 1) * np.clip((0.92 - phase % 1) * 12, 0, 1)
    wavfile.write(args.out / "Limiter.wav", PLAYBACK_RATE,
                  np.round(body * (1 - 0.45 * cut[:, None]) * 32767).astype("<i2"))

    # Shift transients keep their recorded speed and their level relative to the loops.
    ramp = int(0.004 * SOURCE_RATE)
    for name, t, before, after in SHIFTS:
        clip = section(t - before, t + after) * gain
        clip[:ramp] *= np.linspace(0, 1, ramp)[:, None]
        clip[-ramp:] *= np.linspace(1, 0, ramp)[:, None]
        if np.max(abs(clip)) > 0.99:
            raise SystemExit(f"{name} clips")
        wavfile.write(args.out / (name + ".wav"), SOURCE_RATE, np.round(clip * 32767).astype("<i2"))
    print(f"global gain {gain:.3f}")


if __name__ == "__main__":
    main()

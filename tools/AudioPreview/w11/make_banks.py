"""Extract the W11 onboard engine banks from the supplied Monza pole-lap MP3.

python make_banks.py <onboard.mp3> <wav-output-dir>
Requires pygame, numpy and scipy. Pack the resulting WAVs with pack_banks.gd.

Every loop is resampled onto one crank-angle clock (512 samples per four-stroke cycle,
51200 Hz = unity pitch at 12,000 RPM), so the game can crossfade them at a shared pitch.
Loops keep their recorded level relative to each other (one global gain for all banks),
so the in-game mix inherits the real loudness of each RPM, gear and throttle state.
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
# one gear and avoids kerb strikes (a low thump would repeat every loop) and gear changes.
# Short chicane-exit sweeps are pooled and re-sequenced cycle by cycle into longer loops.
BANKS = {
    # full throttle, 2nd-5th gear: little wind, strong airbox and exhaust
    "LowA": [(20.420, 20.950, 6640, 7600)],                                  # Rettifilo exit, 2nd
    "LowB": [(20.850, 21.320, 7430, 8450)],                                  # Rettifilo exit, 2nd
    "LowC": [(37.500, 38.000, 8450, 9350)],                                  # Roggia exit
    "LowD": [(37.900, 38.220, 9300, 9700)],                                  # Roggia exit
    "LowE": [(22.270, 22.550, 10900, 11600), (22.800, 23.080, 10850, 11600),  # 3rd, 4th
             (23.400, 23.700, 11000, 11450)],                                 # 5th
    "LowF": [(21.980, 22.228, 11500, 12600), (22.550, 22.770, 11600, 12450),  # 2nd, 3rd
             (23.080, 23.300, 11600, 12080), (23.750, 24.120, 11650, 12120)], # 4th, 5th
    # full throttle, 6th-8th gear: road and wind noise at 280-340 km/h
    "HighA": [(4.700, 6.000, 10760, 11150), (25.750, 26.850, 10850, 11450),  # 8th, 7th
              (64.100, 64.700, 11060, 11500)],                                # 6th
    "HighB": [(8.000, 10.000, 11550, 11910), (27.000, 28.050, 11550, 12070), # 8th, 7th
              (64.800, 65.650, 11600, 12150)],                                # 6th
    "HighC": [(13.270, 15.510, 12190, 12110)],                                # 8th, flat out
    # off throttle: overrun, MGU-K harvest and downshift braking
    "CoastLow": [(17.330, 18.250, 9800, 7420), (36.300, 37.050, 9700, 8400)],  # Rettifilo, Roggia
    "CoastHigh": [(76.470, 76.900, 11300, 9900)],                              # Parabolica, 300 km/h
}
# Formant-preserving extensions beyond the recorded range (the lap never idles or over-revs).
EXTEND = [
    # name,     source,     target RPM
    ("LowZ",    "LowA",     5500),
    ("Idle",    "CoastLow", 4600),
    ("LowTop",  "LowF",    13600),
    ("HighTop", "HighC",   13600),
]
# Shift transients at their recorded speed: a few ms before the gear change to the end of
# the torque interruption (upshift) or the rev-matching blip (downshift).
#                        shift time before  after
SHIFTS = [
    ("UpshiftLow",    22.239, 0.006, 0.050),   # 2-3, Rettifilo exit, 12,600 -> 10,950 RPM
    ("UpshiftHigh",   63.980, 0.006, 0.050),   # 5-6, Ascari exit, 12,440 -> 11,080 RPM
    ("DownshiftLow",  36.238, 0.012, 0.095),   # Roggia braking, 8,930 -> 10,250 RPM
    ("DownshiftHigh", 62.030, 0.012, 0.095),   # Ascari braking, 10,330 -> 11,380 RPM
]
# The Roggia exit is taken over the kerbs on a part throttle, so its two banks sit ~5 dB under the
# Rettifilo and 3rd-gear pulls either side; lift them onto that curve so a full-throttle pull doesn't
# dip in the middle. The 6th/7th-gear grains make HighB ~1.5 dB louder than the 8th-gear banks.
# The Parabolica overrun starts a few tenths after the lift; in-game lifts at high RPM need it ~2 dB up.
TRIM_DB = {"LowC": 5.0, "LowD": 4.0, "HighB": -1.5, "CoastHigh": 2.0}
ALIGN = [  # each bank is phase-aligned with the one it crossfades with
    ("LowA", None), ("LowB", "LowA"), ("LowC", "LowB"), ("LowD", "LowC"), ("LowE", "LowD"),
    ("LowF", "LowE"), ("HighA", "LowE"), ("HighB", "HighA"), ("HighC", "HighB"),
    ("CoastLow", "LowB"), ("CoastHigh", "LowD"),
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
        loops[name] = align(loop - loop.mean(axis=0), anchor) * 10 ** (TRIM_DB.get(name, 0) / 20)
        centre[name] = float(np.exp(np.mean([np.log(lo * hi) / 2 for _, _, lo, hi in clips])))
    for name, src, rpm in EXTEND:
        loops[name] = formant_shift(loops[src], rpm / centre[src])
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

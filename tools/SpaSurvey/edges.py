"""Stage 2: digitise both painted track-limit lines from the 2023 orthophoto.

  python tools/SpaSurvey/edges.py      -> cache/edges.npz

The TUM centreline, registered into Lambert 2008, is only a search prior. Along each prior normal the 25 cm image is
unwrapped from -17 m to +17 m; a white-line response (local minimum-channel brightness minus its 5 px background) is
traced on each side by dynamic programming with a lateral-jump penalty, so a single continuous stripe is followed
around the lap. The stripe offsets are the measured road edges (outer edge of the white line = track limit).
"""
from scipy.ndimage import gaussian_filter1d

from spa_common import *


def tum_prior():
    raw = np.loadtxt(CACHE / "tum" / "Spa.csv", delimiter=",", comments="#")
    pts = np.roll(raw[:, :2], -TUM_ROLL, axis=0)
    return gaussian_filter1d(pts, 1.15, axis=0, mode="wrap") @ TUM_ROT + TUM_TRANS, raw


def trace(points, normal, side, dists, bright):
    ix = np.flatnonzero((side * dists >= 2) & (side * dists <= 13.5))
    n = len(ix)
    score = gaussian_filter1d(bright[:, ix], 1, axis=0, mode="wrap") - .60 * np.abs(dists[ix] - side * 5.7)
    shifts = np.arange(n)
    penalty = 2.7 * np.abs(shifts[:, None] - shifts)
    val = np.zeros(n)
    back = np.zeros_like(score, dtype=int)
    # two passes round the closed loop so the start is not a boundary
    for i, row in enumerate(np.tile(score, (2, 1))):
        cand = val[:, None] - penalty
        j = cand.argmax(0)
        val = cand[j, shifts] + row
        back[i % len(score)] = j
    k = val.argmax()
    path = []
    for i in reversed(range(len(score))):
        path.append(k)
        k = back[i, k]
    return gaussian_filter1d(dists[ix[path[::-1]]], 1.4, mode="wrap")


def main():
    points, _ = tum_prior()
    normal = normals(points)
    dists = np.arange(-17, 17.01, .125)
    rgb = ortho_rgb(points[:, None, :] + normal[:, None, :] * dists[None, :, None]).astype(float)
    lum = rgb.min(axis=2)
    bright = gaussian_filter1d(lum, .9, axis=1) - gaussian_filter1d(lum, 5, axis=1)
    left, right = trace(points, normal, 1, dists, bright), trace(points, normal, -1, dists, bright)
    np.savez(CACHE / "edges.npz", points=points, normal=normal, left=left, right=right)
    print("painted width percentiles 0/10/50/90/100:", np.round(np.percentile(left - right, [0, 10, 50, 90, 100]), 2))


if __name__ == "__main__":
    main()

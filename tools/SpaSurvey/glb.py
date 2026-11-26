"""Stage 4: engine-neutral export tracks/spa/spa_neutral.glb (glTF 2.0, metres, +Y up, right-handed).

  python tools/SpaSurvey/glb.py

Mapping (X, Y, Z) = (east, up, -north) from the local frame of track.json (origin at the measured control line,
heights relative to the control line; the Ostend height of the origin is stored in asset.extras). One mesh per
surface type: road, lines, kerbs, gravel, asphalt run-off, barriers, pit lane, terrain (flat-shaded).
"""
import struct

from spa_common import *

COLOURS = {"road": (0.15, 0.15, 0.17), "lines": (0.95, 0.95, 0.95), "kerbs": (0.86, 0.13, 0.13), "gravel": (0.85, 0.76, 0.55),
           "runoff_asphalt": (0.23, 0.23, 0.25), "barriers": (0.8, 0.8, 0.82), "pitlane": (0.19, 0.19, 0.21), "terrain": (0.38, 0.49, 0.29)}


def gl(e, n, u):
    return np.c_[e, u, -n].astype(np.float32)


class Builder:
    def __init__(self):
        self.parts = {k: ([], []) for k in COLOURS}

    def strip(self, name, a, b):
        """Quad strip between two polylines of 3D points (both ordered along the lap)."""
        verts, idx = self.parts[name]
        base = sum(len(v) for v in verts)
        m = len(a)
        verts.append(np.vstack([a, b]))
        i = np.arange(m - 1)
        idx.append(base + np.c_[i, i + m, i + 1, i + 1, i + m, i + m + 1].reshape(-1, 3))

    def tris(self, name, v, t):
        verts, idx = self.parts[name]
        base = sum(len(x) for x in verts)
        verts.append(v)
        idx.append(base + t)


def runs(mask):
    out, i, n = [], 0, len(mask)
    while i < n:
        if mask[i]:
            j = i
            while j < n and mask[j]:
                j += 1
            out.append((i, min(j + 1, n)))
            i = j
        else:
            i += 1
    return out


def build():
    t = read_json(GAME / "track.json")
    p = np.array(t["centerline"])
    n = len(p)
    z = np.array(t["elevation"])
    cf = np.array(t["crossfall"])
    nrm = normals(p)
    wl, wr = np.array(t["widthLeft"]), np.array(t["widthRight"])
    bl, br = np.array(t["barrierLeft"]), np.array(t["barrierRight"])
    idx = np.r_[np.arange(n), 0]

    def at(d, h=0.0, ix=idx):
        q = p[ix] + nrm[ix] * d[ix][:, None] if np.ndim(d) else p[ix] + nrm[ix] * d
        dd = d[ix] if np.ndim(d) else d
        return gl(q[:, 0], q[:, 1], z[ix] + dd * cf[ix] + h)

    b = Builder()
    b.strip("road", at(-wr + .25), at(wl - .25))
    b.strip("lines", at(wl - .25, .004), at(wl, .004))
    b.strip("lines", at(-wr, .004), at(-wr + .25, .004))
    for side, w, kerb, run, bar in ((1, wl, t["kerbLeft"], t["runoffLeft"], bl), (-1, wr, t["kerbRight"], t["runoffRight"], br)):
        for a, c in runs(np.array([ch == "K" for ch in kerb])):
            ix = np.arange(a, c) % n
            b.strip("kerbs", at(side * w, .03, ix), at(side * (w + 1.2), .01, ix))
        for code, name in (("R", "gravel"), ("A", "runoff_asphalt")):
            for a, c in runs(np.array([ch == code for ch in run])):
                ix = np.arange(a, c) % n
                b.strip(name, at(side * (w + 1.0), 0, ix), at(side * (bar - .3), 0, ix))
        keep = np.array([ch != "P" for ch in run])
        for a, c in runs(keep):
            ix = np.arange(a, c) % n
            b.strip("barriers", at(side * bar, 0, ix), at(side * bar, 1.05, ix))
    pit = t["pitLane"]
    pp, pz = np.array(pit["path"]), np.array(pit["elevation"])
    tg = np.gradient(pp, axis=0)
    tg /= np.linalg.norm(tg, axis=1)[:, None]
    pn = np.c_[-tg[:, 1], tg[:, 0]]
    half = pit["width"] / 2
    b.strip("pitlane", gl(*(pp - pn * half).T, pz), gl(*(pp + pn * half).T, pz))

    ter = read_json(GAME / "terrain.json")
    tv = np.array(ter["vertices"])
    tt = np.array(ter["triangles"]).reshape(-1, 3)
    flat = tv[tt.ravel()]  # one vertex per corner: flat-shaded facets
    b.tris("terrain", gl(flat[:, 0], flat[:, 1], flat[:, 2]), np.arange(len(flat)).reshape(-1, 3))
    return b, t


def write_glb(b, track, path):
    log = read_json(OUT / "build_log.json")
    blob = bytearray()
    views, accessors, meshes, nodes, materials = [], [], [], [], []
    totals = {}
    for name, (verts, idx) in b.parts.items():
        if not verts:
            continue
        v = np.vstack(verts).astype(np.float32)
        i = np.vstack(idx).astype(np.uint32)
        # face normals per vertex for lighting (flat look for terrain, averaged elsewhere is unnecessary for a reference file)
        nrm = np.zeros_like(v)
        a, c, d = v[i[:, 0]], v[i[:, 1]], v[i[:, 2]]
        fn = np.cross(c - a, d - a)
        fn[fn[:, 1] < 0] *= -1  # every surface here faces up or towards the track
        for k in range(3):
            np.add.at(nrm, i[:, k], fn)
        nrm /= np.maximum(np.linalg.norm(nrm, axis=1)[:, None], 1e-9)
        totals[name] = len(i)
        for arr, target, comp, typ in ((v, 34962, 5126, "VEC3"), (nrm.astype(np.float32), 34962, 5126, "VEC3"), (i.ravel(), 34963, 5125, "SCALAR")):
            while len(blob) % 4:
                blob.append(0)
            off = len(blob)
            blob += arr.tobytes()
            views.append(dict(buffer=0, byteOffset=off, byteLength=arr.nbytes, target=target))
            acc = dict(bufferView=len(views) - 1, componentType=comp, count=len(arr) if typ == "VEC3" else arr.size, type=typ)
            if typ == "VEC3" and target == 34962 and arr is v:
                acc.update(min=v.min(0).tolist(), max=v.max(0).tolist())
            accessors.append(acc)
        materials.append(dict(name=name, pbrMetallicRoughness=dict(baseColorFactor=[*COLOURS[name], 1], metallicFactor=0, roughnessFactor=1)))
        meshes.append(dict(name=name, primitives=[dict(attributes=dict(POSITION=len(accessors) - 3, NORMAL=len(accessors) - 2),
                                                        indices=len(accessors) - 1, material=len(materials) - 1)]))
        nodes.append(dict(name=name, mesh=len(meshes) - 1))
    gltf = dict(asset=dict(version="2.0", generator="tools/SpaSurvey/glb.py",
                           extras=dict(frame="(X,Y,Z) = (east, up, -north) metres from the measured control line",
                                       originLambert2008=log["origin_lambert"], originHeightDNG=log["datum_m"],
                                       credits="© SPW 2021-2023 (CC BY 4.0); see CREDITS.md")),
                scene=0, scenes=[dict(nodes=list(range(len(nodes))))], nodes=nodes, meshes=meshes, materials=materials,
                accessors=accessors, bufferViews=views, buffers=[dict(byteLength=len(blob))])
    js = json.dumps(gltf, separators=(",", ":")).encode()
    js += b" " * (-len(js) % 4)
    while len(blob) % 4:
        blob.append(0)
    with open(path, "wb") as f:
        f.write(struct.pack("<III", 0x46546C67, 2, 12 + 8 + len(js) + 8 + len(blob)))
        f.write(struct.pack("<II", len(js), 0x4E4F534A) + js)
        f.write(struct.pack("<II", len(blob), 0x004E4942) + bytes(blob))
    return totals


if __name__ == "__main__":
    builder, track = build()
    totals = write_glb(builder, track, OUT / "spa_neutral.glb")
    print("spa_neutral.glb triangles:", totals, "total", sum(totals.values()))

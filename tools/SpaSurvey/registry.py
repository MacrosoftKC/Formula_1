"""Check anchors from the replica brief (SPA_1TO1_REPLICA_PROMPT.md). These are NOT geometry: they are compared
against the measured build in checks.py, and corner names/numbering follow the FIA / formula1.com scheme.

Lap metres are from the research estimate of the control line (TUM s = 140 m); the build measures the real
painted line and reports the offset."""

# number, name, direction, heading change (deg), Rmin (m), entry, apex, exit (lap m), apex lat, lon
_TABLE = """
1|La Source|R|146|19|210|260|294|50.44631|5.96346
2|Eau Rouge|L|19|113|884|919|939|50.44257|5.97045
3|Raidillon|R|52|104|949|999|1099|50.44217|5.97138
4|Raidillon exit|L|24|117|1114|1144|1184|50.44095|5.97204
5|Les Combes|R|73|44|2239|2288|2323|50.43132|5.97752
6|Les Combes exit|L|72|53|2328|2378|2413|50.43073|5.97671
7|Malmedy|R|70|52|2468|2528|2568|50.42941|5.97696
8|Bruxelles|R|185|45|2833|2868|3028|50.42764|5.97307
9|Jacky Ickx|L|77|51|3108|3163|3218|50.42929|5.97431
10|Pouhon|L|76|92|3650|3697|3770|50.43394|5.97249
11|Pouhon 2|L|70|132|3810|3877|4005|50.43417|5.97008
12|Fagnes|R|98|66|4302|4402|4457|50.43036|5.96625
13|Fagnes 2|L|90|68|4467|4527|4607|50.43069|5.96461
14|Campus|R|93|45|4762|4812|4871|50.42874|5.96212
15|Courbe Paul Frere|R|117|90|4961|5071|5491|50.43016|5.95956
16|Blanchimont|L|37.5|231|5661|5816|5856|50.43473|5.96677
17|Blanchimont 2|L|44|118|5991|6071|6121|50.43686|5.96805
18|Bus Stop|R|102|21|6576|6611|6635|50.44157|5.96643
19|Bus Stop exit|L|127|20|6640|6665|6705|50.44163|5.96716
"""

CORNERS = []
for row in _TABLE.strip().splitlines():
    n, name, d, h, r, e, a, x, lat, lon = row.split("|")
    CORNERS.append(dict(number=int(n), name=name.replace("Frere", "Frère"), dir=d, heading=float(h), rmin=float(r),
                        entry=float(e), apex=float(a), exit=float(x), lat=float(lat), lon=float(lon)))

# Unnumbered, effectively flat-out bends: heading change (deg), radius (m), lap-metre span. Direction is measured.
BENDS = [dict(number=0, name=name, dir=None, heading=h, rmin=r, entry=a, apex=(a + b) / 2, exit=b)
         for name, h, r, a, b in [("Kink after La Source", 5.8, 622, 434, 499), ("Kink before Eau Rouge", 7.7, 305, 559, 609),
                                  ("Kemmel kink", 13.5, 287, 1424, 1509), ("Kink before the Bus Stop", 12.0, 373, 6316, 6411)]]

DIRECTIONS = "RLRLRLRRLLLRLRRLLRL"

# Operator altitude markers (D5, re-read from the PDF: identical to the brief): lap m -> altitude m
MARKERS = [(0, 418), (220, 424), (310, 419), (690, 403), (890, 391), (1130, 415), (1490, 446), (2200, 463), (2470, 470),
           (2870, 455), (3110, 438), (3600, 402), (4280, 377), (4740, 375), (5100, 373), (5700, 388), (6000, 398),
           (6540, 408), (7004, 418)]

# Stretch-average gradients from the brief (percent): (from, to, value, tolerance in percentage points)
STRETCHES = [("La Source exit to Eau Rouge bottom", 310, 890, -4.8, 1.5), ("Eau Rouge bottom to Raidillon crest", 890, 1130, 10.0, 2.0),
             ("Early Kemmel", 1130, 1490, 8.6, 1.5), ("Kemmel overall", 1130, 2200, 4.1, 1.5),
             ("T7 entry to T10 entry", 2470, 3600, -6.0, 1.5), ("Fagnes-Campus valley floor", 4280, 4740, -0.5, 1.5),
             ("Paul Frère to the line", 5100, 7004, 2.4, 1.5)]

LAP_REFERENCES = {"published": 7004.0, "race_distance_over_laps": 7001.2, "tum_closed_polygon": 7000.05}
AREA_REFERENCE = 923549.0

"""Read-only boundary feasibility audit; color is a region proxy, not kingdom ID."""
import collections
import csv
import json
import pathlib
import sys


def analyze(path):
    edges = collections.defaultdict(list)
    count = 0
    with open(path, newline="", encoding="utf-8-sig") as source:
        for row in csv.DictReader(source):
            points = [tuple(float(row[p + axis]) for axis in "xyz") for p in "abc"]
            color = int(row["color"])
            count += 1
            for a, b in zip(points, points[1:] + points[:1]):
                key = tuple(sorted((a[:2], b[:2])))
                edges[key].append((color, a, b))
    degrees = collections.defaultdict(collections.Counter)
    coast = internal = nonmanifold = mismatch = 0
    for key, faces in edges.items():
        if len(faces) > 2:
            nonmanifold += 1
            continue
        if len(faces) == 2:
            z1 = {a[:2]: a[2] for a in faces[0][1:]}
            z2 = {a[:2]: a[2] for a in faces[1][1:]}
            mismatch += any(abs(z1[p] - z2[p]) > 0.0001 for p in key)
            if faces[0][0] == faces[1][0]:
                continue
            internal += 1
        else:
            coast += 1
        for color, a, b in faces:
            degrees[color].update((a[:2], b[:2]))
    return dict(triangles=count, exact_xy_edges=len(edges), outer_boundary_edges=coast,
                different_color_edges=internal, nonmanifold_edges=nonmanifold,
                paired_edge_height_mismatches=mismatch,
                color_regions={str(color): dict(vertices=len(degree),
                    degree_histogram=dict(collections.Counter(degree.values())))
                    for color, degree in sorted(degrees.items())},
                limits="Exact XY endpoint matching only. Unsplit T junctions can look like outer edges. "
                "Color is not kingdom identity. Outer edges include excluded holes and cutouts, not only coast.")


if __name__ == "__main__":
    result = analyze(sys.argv[1])
    output = json.dumps(result, indent=2)
    pathlib.Path(sys.argv[2]).write_text(output, encoding="utf-8")
    print(output)

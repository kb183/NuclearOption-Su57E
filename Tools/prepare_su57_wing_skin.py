"""Restore rigid wing-surface selections and original pivots for all three LODs.

Asset preparation only; does not build, launch Unity/game or run validation.
LOD1/2 use the original BA dominant bone weights. LOD0 has no weight channel:
recover its separate, position-welded surface islands using the LOD1 envelopes.
The optional --extract-source step reads the installed BA bundle into a cache.
"""
import argparse
import json
import os
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "Assets/Blueprinter/Mods/SU57/Source"
REFERENCE = ROOT / "Reference/SU57/ControlSurfaces/v0.2.11"
CACHE = REFERENCE / "source-wing-weights.json"
IDS = ("-837618460868326853", "1284410701970027520", "7577132308821048523")
FILES = ("RU_SU_57_-837618460868326853.obj", "02_RU_SU_57_1284410701970027520.obj", "03_RU_SU_57_7577132308821048523.obj")
KINDS = {"frontwing1": "Levcon", "frontwing2": "LeadingEdge", "backwing1": "VerticalTail", "backwing2": "HorizontalTail", "backwing3": "Flaperon", "backwing4": "Aileron"}


def extract_source(bundle):
    import UnityPy
    from UnityPy.helpers.MeshHelper import MeshHandler

    existing = json.loads((ROOT / "Reference/SU57/VectoringVideoResearch/source-nozzle-weights.json").read_text())
    result = {}
    print("Reading original mesh weights...", flush=True)
    env = UnityPy.load(str(bundle))
    for obj in env.objects:
        if str(obj.path_id) not in IDS or obj.type.name != "Mesh":
            continue
        mesh = obj.read()
        handler = MeshHandler(mesh)
        handler.process()
        names = existing[str(obj.path_id)]["bones"]
        groups = {}
        for i, (indices, weights) in enumerate(zip(handler.m_BoneIndices or [], handler.m_BoneWeights or [])):
            slot = max(range(len(weights)), key=lambda j: weights[j])
            groups.setdefault(names[indices[slot]], []).append(i)
        result[str(obj.path_id)] = {"mesh": mesh.m_Name, "bones": names, "groups": groups}
    CACHE.write_text(json.dumps(result, indent=2), encoding="utf-8")


def read_mesh(path):
    vertices, faces = [], []
    for line in path.read_text().splitlines():
        fields = line.split()
        if not fields:
            continue
        if fields[0] == "v":
            x, y, z = map(float, fields[1:4])
            vertices.append((-x, y, z))  # Same X conversion as BuildSu57.LoadMesh.
        elif fields[0] == "f":
            faces.append([int(s.split("/")[0]) - 1 for s in fields[1:]])
    return vertices, faces


def components(vertices, faces):
    parent = list(range(len(vertices)))

    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    def join(i, j):
        parent[find(i)] = find(j)

    welded = {}
    for i, point in enumerate(vertices):
        key = tuple(round(x, 5) for x in point)
        if key in welded:
            join(i, welded[key])
        else:
            welded[key] = i
    for face in faces:
        for i in face[1:]:
            join(face[0], i)
    groups = {}
    for i in range(len(vertices)):
        groups.setdefault(find(i), []).append(i)
    return list(groups.values())


def bounds(vertices, indices):
    return [(min(vertices[i][k] for i in indices), max(vertices[i][k] for i in indices)) for k in range(3)]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--extract-source", action="store_true")
    parser.add_argument("--bundle", type=Path)
    args = parser.parse_args()
    REFERENCE.mkdir(parents=True, exist_ok=True)
    if args.extract_source:
        configured = os.environ.get("SU57_BROKEN_ARROW_BUNDLE")
        bundle = args.bundle or (Path(configured) if configured else None)
        if bundle is None:
            parser.error("--extract-source requires --bundle or SU57_BROKEN_ARROW_BUNDLE")
        extract_source(bundle)
    if not CACHE.exists():
        raise FileNotFoundError("Original weight cache missing; use --extract-source to read the installed source model")
    reference = json.loads(CACHE.read_text())
    hierarchy = json.loads((SOURCE / "source_hierarchy.json").read_text())
    body = next(n for n in hierarchy["roots"][0]["children"] if n["name"] == "root")["children"][0]
    nodes = {n["name"]: n for n in body["children"]}
    names = [n for n in reference[IDS[1]]["bones"] if n.split("_")[0] in KINDS]
    bones = []
    for name in names:
        node = nodes[name]
        position = {k: node["position"][k] + body["position"][k] for k in ("x", "y", "z")}
        bones.append({"name": name, "kind": KINDS[name.split("_")[0]], "position": position, "rotation": node["rotation"]})

    meshes = [read_mesh(SOURCE / f) for f in FILES]
    original = reference[IDS[1]]["groups"]
    envelopes = {name: bounds(meshes[1][0], original[name]) for name in names}
    lod0 = {name: [] for name in names}
    assignments = []
    for island in components(*meshes[0]):
        box = bounds(meshes[0][0], island)
        matches = [name for name, envelope in envelopes.items() if all(box[k][0] >= envelope[k][0] - .09 and box[k][1] <= envelope[k][1] + .09 for k in range(3))]
        if len(matches) > 1:
            raise ValueError("Ambiguous surface island: " + repr(matches))
        if matches:
            name = matches[0]
            lod0[name].extend(island)
            assignments.append({"name": name, "vertices": len(island), "bounds": box})

    lods = []
    for i, (mesh_id, filename, (vertices, _)) in enumerate(zip(IDS, FILES, meshes)):
        groups = lod0 if i == 0 else reference[mesh_id]["groups"]
        surfaces = [{"name": name, "vertices": sorted(groups[name])} for name in names]
        if any(not s["vertices"] for s in surfaces):
            raise ValueError("Source model has an empty movable surface")
        lods.append({"meshFile": filename, "vertexCount": len(vertices), "surfaces": surfaces})

    output = SOURCE / "SU57_WingSkin.json"
    output.write_text(json.dumps({"bones": bones, "lods": lods}, indent=2), encoding="utf-8")
    (REFERENCE / "lod0-islands.json").write_text(json.dumps(assignments, indent=2), encoding="utf-8")
    print("Prepared", output)
    for name in names:
        counts = [len(next(s for s in lod["surfaces"] if s["name"] == name)["vertices"]) for lod in lods]
        print(name, "LOD0/1/2 vertices:", *counts)


if __name__ == "__main__":
    main()

"""Prepare the three-bone nozzle skin selection without changing source geometry.

LOD1/2 selections come from the original BA engine/turbine bone weights.
LOD0 has no exported weight channel; use the same isolated nozzle envelope,
which selects exactly those source-weight vertices on both lower LODs.
This is asset preparation, not a flight or bundle validation script.
"""
import json
from pathlib import Path

project = Path(__file__).resolve().parents[1]
source = project / "Assets/Blueprinter/Mods/SU57/Source"
reference = json.loads((project / "Reference/SU57/VectoringVideoResearch/source-nozzle-weights.json").read_text())
mesh_ids = ["-837618460868326853", "1284410701970027520", "7577132308821048523"]
filenames = ["RU_SU_57_-837618460868326853.obj", "02_RU_SU_57_1284410701970027520.obj", "03_RU_SU_57_7577132308821048523.obj"]
lods = []
for mesh_id, filename in zip(mesh_ids, filenames):
    vertices = [tuple(map(float, line.split()[1:4])) for line in (source / filename).read_text().splitlines() if line.startswith("v ")]
    # OBJ exporter mirrors X; BuildSu57.LoadMesh mirrors it back.
    vertices = [(-x, y, z) for x, y, z in vertices]
    selected = reference[mesh_id]["nozzles"]
    if selected:
        left, right = selected["L"], selected["R"]
    else:
        candidates = [i for i, (x, y, z) in enumerate(vertices) if .75 < abs(x) < 2 and 1.55 < y < 2.8 and z < -7.9]
        left = [i for i in candidates if vertices[i][0] < 0]
        right = [i for i in candidates if vertices[i][0] > 0]
    lods.append(dict(meshFile=filename, vertexCount=len(vertices), left=left, right=right))

output = source / "SU57_NozzleSkin.json"
output.write_text(json.dumps(dict(lods=lods), indent=2), encoding="utf-8")
print(f"Prepared {output}")
for i, lod in enumerate(lods):
    print(f"LOD{i}: {len(lod['left'])} left / {len(lod['right'])} right nozzle vertices")

"""Author native primary-collider mass references from local mesh assets.

Offline asset production only. Does not start Unity, run game physics or tests.
Integrates signed solid tetrahedra instead of querying inactive editor bodies.
"""
from pathlib import Path
import hashlib
import json
import re
import struct
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "Tools/AuthoringDeps"))
import numpy as np

TEMPLATE = ROOT / "Assets/Blueprinter/_donotship/GameObject/Multirole1_PLACEHOLDER.prefab"
OUTPUT = ROOT / "Assets/Blueprinter/Mods/SU57/Source/SU57_NativeMassProperties.json"


def blocks(path):
    text = path.read_text(encoding="utf-8-sig")
    return {m[2]: (int(m[1]), m[3]) for m in re.finditer(
        r"^--- !u!(\d+) &(-?\d+)\n(.*?)(?=^--- !u!|\Z)", text, re.M | re.S)}


def field(text, key):
    match = re.search(r"^  " + key + r": (.*)$", text, re.M)
    if match is None:
        raise ValueError("Missing serialized field: " + key)
    return match[1]


def file_id(text):
    return re.search(r"fileID: (-?\d+)", text)[1]


def vector(text, axes="xyz"):
    return np.array([float(re.search(r"\b" + axis + r": ([^,}]+)", text)[1]) for axis in axes])


def rotation(q):
    x, y, z, w = q / np.linalg.norm(q)
    return np.array([[1-2*(y*y+z*z), 2*(x*y-z*w), 2*(x*z+y*w)],
                     [2*(x*y+z*w), 1-2*(x*x+z*z), 2*(y*z-x*w)],
                     [2*(x*z-y*w), 2*(y*z+x*w), 1-2*(x*x+y*y)]])


def quaternion(matrix):
    # Eigenvectors are columns of the principal-axis rotation.
    trace = np.trace(matrix)
    if trace > 0:
        s = np.sqrt(trace+1)*2
        q = [(matrix[2, 1]-matrix[1, 2])/s,
             (matrix[0, 2]-matrix[2, 0])/s,
             (matrix[1, 0]-matrix[0, 1])/s, s/4]
    else:
        i = int(np.argmax(np.diag(matrix)))
        j, k = (i+1) % 3, (i+2) % 3
        s = np.sqrt(1+matrix[i, i]-matrix[j, j]-matrix[k, k])*2
        q = [0., 0., 0., 0.]
        q[i] = s/4
        q[j] = (matrix[j, i]+matrix[i, j])/s
        q[k] = (matrix[k, i]+matrix[i, k])/s
        q[3] = (matrix[k, j]-matrix[j, k])/s
    q = np.array(q)
    return q / np.linalg.norm(q)


def mesh_geometry(path):
    text = path.read_text(encoding="utf-8-sig")
    channels = [tuple(map(int, item)) for item in re.findall(
        r"- stream: (\d+)\n\s+offset: (\d+)\n\s+format: (\d+)\n\s+dimension: (\d+)", text)]
    count = int(re.search(r"m_VertexCount: (\d+)", text)[1])
    raw = bytes.fromhex(re.search(r"_typelessdata: ([0-9a-fA-F]+)", text)[1])
    sizes = {0: 4, 1: 2, 2: 1, 3: 1, 4: 2, 5: 2, 6: 1, 7: 1, 8: 2, 9: 2, 10: 4, 11: 4}
    strides = {}
    for stream, offset, fmt, dimension in channels:
        if dimension:
            strides[stream] = max(strides.get(stream, 0), offset + sizes[fmt]*dimension)
    strides = {stream: (size+3)//4*4 for stream, size in strides.items()}
    offsets, cursor = {}, 0
    for stream, stride in sorted(strides.items()):
        cursor = (cursor+15)//16*16
        offsets[stream] = cursor
        cursor += count*stride
    stream, offset, fmt, dimension = channels[0]
    if fmt != 0 or dimension != 3:
        raise ValueError("Unsupported native position channel: " + str(path))
    vertices = np.array([struct.unpack_from("<3f", raw, offsets[stream]+offset+i*strides[stream])
                         for i in range(count)], dtype=float)
    index_bytes = bytes.fromhex(re.search(r"m_IndexBuffer: ([0-9a-fA-F]+)", text)[1])
    width = 2 if int(re.search(r"m_IndexFormat: (\d+)", text)[1]) == 0 else 4
    indices = np.frombuffer(index_bytes, dtype="<u2" if width == 2 else "<u4")
    triangles = []
    for first, amount, topology, base in re.findall(
            r"firstByte: (\d+)\n\s+indexCount: (\d+)\n\s+topology: (\d+)\n\s+baseVertex: (\d+)", text):
        if int(topology) != 0:
            raise ValueError("Non-triangle native collider: " + str(path))
        first, amount, base = int(first)//width, int(amount), int(base)
        triangles.extend((indices[first:first+amount]+base).reshape(-1, 3).tolist())
    if not triangles:
        raise ValueError("Empty native collider: " + str(path))
    return vertices, np.array(triangles)


def integrate(vertices, triangles):
    origin = vertices.mean(axis=0)
    volume, first, second = 0., np.zeros(3), np.zeros((3, 3))
    for ids in triangles:
        a, b, c = vertices[ids] - origin
        v = np.dot(a, np.cross(b, c))/6
        total = a+b+c
        volume += v
        first += v*total/4
        second += v*(np.outer(total, total)+np.outer(a, a)+np.outer(b, b)+np.outer(c, c))/20
    if abs(volume) < 1e-10:
        raise ValueError("Native collider does not enclose a solid volume")
    center = first/volume
    covariance = second/volume-np.outer(center, center)
    return abs(volume), center+origin, (covariance+covariance.T)/2


def entry(name, center, covariance):
    inertia = np.trace(covariance)*np.eye(3)-covariance
    values, axes = np.linalg.eigh(inertia)
    if not np.all(np.isfinite(values)) or np.any(values <= 0):
        raise ValueError("Invalid solid inertia: " + name)
    if np.linalg.det(axes) < 0:
        axes[:, 0] *= -1
    return {"name": name, "centerOfMass": dict(zip("xyz", map(float, center))),
            "inertiaPerMass": dict(zip("xyz", map(float, values))),
            "inertiaRotation": dict(zip("xyzw", map(float, quaternion(axes))))}


def main():
    objects = blocks(TEMPLATE)
    transforms = {file_id(field(text, "m_GameObject")): ident
                  for ident, (kind, text) in objects.items() if kind == 4}
    matrices = {}

    def matrix(ident):
        if ident == "0":
            return np.eye(4)
        if ident not in matrices:
            text = objects[ident][1]
            local = np.eye(4)
            local[:3, :3] = rotation(vector(field(text, "m_LocalRotation"), "xyzw")) @ np.diag(vector(field(text, "m_LocalScale")))
            local[:3, 3] = vector(field(text, "m_LocalPosition"))
            matrices[ident] = matrix(file_id(field(text, "m_Father"))) @ local
        return matrices[ident]

    meshes = {}
    for meta in (ROOT/"Assets/Blueprinter/_donotship/Mesh").glob("*.asset.meta"):
        guid = re.search(r"^guid: (\w+)", meta.read_text(), re.M)[1]
        meshes[guid] = Path(str(meta)[:-5])
    parts, used, combined = [], {TEMPLATE}, []
    for _, (kind, text) in objects.items():
        if kind != 114 or "\n  wingArea:" not in text or "\n  joints:" not in text:
            continue
        game_object = file_id(field(text, "m_GameObject"))
        name = field(objects[game_object][1], "m_Name")
        transform_id = transforms[game_object]
        mat = matrix(transform_id)
        if file_id(field(objects[transform_id][1], "m_Father")) == "0":
            name = "SU57"
        collider = next(value for cls, value in objects.values()
                        if cls == 64 and file_id(field(value, "m_GameObject")) == game_object)
        path = meshes[re.search(r"guid: (\w+)", field(collider, "m_Mesh"))[1]]
        used.add(path)
        vertices, triangles = mesh_geometry(path)
        # Native structural scales are unity. Refuse silent coordinate changes.
        if not np.allclose(mat[:3, :3].T @ mat[:3, :3], np.eye(3), atol=1e-5):
            raise ValueError("Unsupported scaled native structural part: " + name)
        volume, center, covariance = integrate(vertices, triangles)
        parts.append(entry(name, center, covariance))
        combined.append((volume, mat[:3, :3] @ center+mat[:3, 3],
                         mat[:3, :3] @ covariance @ mat[:3, :3].T))
    if len(parts) != 34 or len({p["name"] for p in parts}) != 34:
        raise ValueError("Expected 34 unique native structural parts")
    total = sum(v for v, _, _ in combined)
    center = sum(v*c for v, c, _ in combined)/total
    covariance = sum(v*(cov+np.outer(c-center, c-center)) for v, c, cov in combined)/total
    parts.append(entry("$merged", center, covariance))
    profile = {"schemaVersion": 2, "method": "signed solid tetrahedra over original KR-67 primary collider meshes; uniform volume density",
               "parts": parts, "sources": [{"path": str(path.relative_to(ROOT)).replace("\\", "/"),
                   "sha256": hashlib.sha256(path.read_bytes()).hexdigest()} for path in sorted(used)]}
    OUTPUT.write_text(json.dumps(profile, ensure_ascii=False, indent=2)+"\n", encoding="utf-8")
    print("Authored 34 solid mass references and merged primary-hull reference:", OUTPUT)


if __name__ == "__main__":
    main()

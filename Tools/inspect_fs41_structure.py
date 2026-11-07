"""Offline asset inventory for FS-41/Su-57; never loads Unity or runs game tests.

Writes research evidence only. References keep their file/path IDs so unresolved
external resources cannot accidentally be presented as local assets.
"""
import argparse
import collections
import hashlib
import json
from pathlib import Path

import UnityPy


def inventory(source, output):
    env = UnityPy.load(str(source))
    objects = {o.path_id: o for o in env.objects}
    trees = {}
    skip = {"Mesh", "Texture2D", "AudioClip", "Shader", "Sprite", "TextAsset"}
    for pid, obj in objects.items():
        if obj.type.name not in skip:
            trees[pid] = obj.read_typetree()

    def pid(ref):
        return ref.get("m_PathID", 0) if isinstance(ref, dict) else 0

    def local(ref):
        return trees.get(pid(ref), {}) if ref.get("m_FileID", 0) == 0 else {}

    def cls(data):
        return local(data.get("m_Script", {})).get("m_ClassName", "")

    go_to_transform = {
        pid(d.get("m_GameObject")): i for i, d in trees.items()
        if objects[i].type.name in {"Transform", "RectTransform"}
    }
    paths = {}

    def path_of_go(go):
        if not go:
            return ""
        if go in paths:
            return paths[go]
        tr = trees.get(go_to_transform.get(go), {})
        parent = local(tr.get("m_Father", {}))
        name = trees.get(go, {}).get("m_Name", "?")
        prefix = path_of_go(pid(parent.get("m_GameObject")))
        paths[go] = (prefix + "/" if prefix else "") + name
        return paths[go]

    def describe(ref):
        ident = pid(ref)
        if not ident:
            return None
        if ref.get("m_FileID", 0):
            return {"external_file": ref["m_FileID"], "path_id": ident}
        obj = objects.get(ident)
        if not obj:
            return {"unresolved_path_id": ident}
        d = trees.get(ident, {})
        if obj.type.name in {"GameObject", "Transform", "RectTransform"}:
            return path_of_go(ident if obj.type.name == "GameObject" else pid(d.get("m_GameObject")))
        if pid(d.get("m_GameObject")):
            return {"path": path_of_go(pid(d["m_GameObject"])), "type": cls(d) or obj.type.name, "path_id": ident}
        if obj.type.name == "Mesh":
            mesh = obj.read()
            return {"name": mesh.m_Name, "vertices": mesh.m_VertexData.m_VertexCount, "path_id": ident}
        return {"name": d.get("m_Name", obj.type.name), "type": cls(d) or obj.type.name, "path_id": ident}

    aircraft = [(i, d) for i, d in trees.items() if objects[i].type.name == "MonoBehaviour" and cls(d) == "Aircraft"]
    root = path_of_go(pid(aircraft[0][1]["m_GameObject"]))
    def in_aircraft(data):
        path = path_of_go(pid(data.get("m_GameObject")))
        return path == root or path.startswith(root + "/")
    classes = collections.Counter(cls(d) for i, d in trees.items() if objects[i].type.name == "MonoBehaviour" and in_aircraft(d))
    colliders, parts, renderers, components, transforms = [], [], [], [], []
    component_by_go = collections.defaultdict(list)
    for i, d in trees.items():
        if "m_GameObject" in d:
            component_by_go[pid(d["m_GameObject"])].append(i)

    for i, d in trees.items():
        if not in_aircraft(d):
            continue
        typ = objects[i].type.name
        go = pid(d.get("m_GameObject"))
        path = path_of_go(go)
        entry = {"path_id": i, "path": path, "type": cls(d) or typ, "data": d}
        if typ in {"Transform", "RectTransform"}:
            transforms.append({"path": path, "path_id": i, "position": d.get("m_LocalPosition"), "rotation": d.get("m_LocalRotation"), "scale": d.get("m_LocalScale")})
        if typ.endswith("Collider"):
            colliders.append({**entry, "mesh": describe(d.get("m_Mesh", {})), "same_object_parts": [cls(trees[j]) for j in component_by_go[go] if cls(trees[j]) in {"AeroPart", "UnitPart", "Damageable"}]})
        if typ in {"MeshRenderer", "SkinnedMeshRenderer"}:
            mesh_ref = d.get("m_Mesh", {})
            if typ == "MeshRenderer":
                filters = [trees[j] for j in component_by_go[go] if objects[j].type.name == "MeshFilter"]
                mesh_ref = filters[0].get("m_Mesh", {}) if filters else {}
            renderers.append({"path_id": i, "path": path, "type": typ, "mesh": describe(mesh_ref), "materials": d.get("m_Materials"), "bones": [describe(b) for b in d.get("m_Bones", [])]})
        if typ != "MonoBehaviour":
            continue
        components.append(entry)
        if cls(d) in {"AeroPart", "UnitPart", "Damageable"}:
            parts.append({**entry, "damage_renderers": [describe(v) for v in d.get("damageMaterial", {}).get("renderers", [])], "colliders": [j for j in component_by_go[go] if objects[j].type.name.endswith("Collider")]})

    weapon_managers = []
    for entry in components:
        if entry["type"] != "WeaponManager":
            continue
        sets = []
        for s in entry["data"].get("hardpointSets", []):
            sets.append({**s, "resolved_options": [describe(v) for v in s.get("weaponOptions", [])], "resolved_hardpoints": [{**h, "resolved": {k: [describe(v) for v in val] if isinstance(val, list) and k in {"bayDoors", "BuiltInWeapons", "BuiltInTurrets"} else describe(val) for k, val in h.items() if k in {"transform", "part", "bayDoors", "Pylon", "Plug", "BuiltInWeapons", "BuiltInTurrets"}}} for h in s.get("hardpoints", [])]})
        weapon_managers.append({"path": entry["path"], "sets": sets})

    materials = [{"path_id": i, "data": d} for i, d in trees.items() if objects[i].type.name == "Material"]
    lod_groups = [{"path_id": i, "path": path_of_go(pid(d.get("m_GameObject"))), "data": d} for i, d in trees.items() if objects[i].type.name == "LODGroup" and in_aircraft(d)]
    manifests = []
    for obj in env.objects:
        if obj.type.name == "TextAsset":
            data = obj.read()
            if "manifest" in data.m_Name:
                manifest = json.loads(data.m_Script.lstrip("\ufeff"))
                out = output.parent / (output.stem + "-" + data.m_Name + ".json")
                out.write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
                manifests.append(str(out))
    result = {"source": str(source), "sha256": hashlib.sha256(source.read_bytes()).hexdigest(), "root": root, "scope": "Aircraft subtree only; excludes bundled carrier/weapon prefabs; includes inactive children", "bundle_types": dict(collections.Counter(o.type.name for o in env.objects)), "aircraft_classes": dict(classes), "transforms": transforms, "colliders": colliders, "parts": parts, "renderers": renderers, "weapon_managers": weapon_managers, "components": components, "materials": materials, "lod_groups": lod_groups, "manifests": manifests}
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({"source": str(source), "root": root, "classes": dict(classes), "colliders": dict(collections.Counter(e["type"] for e in colliders)), "parts": len(parts), "renderers": len(renderers), "hardpoint_sets": [len(w["sets"]) for w in weapon_managers], "output": str(output)}, ensure_ascii=True))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    args.output.parent.mkdir(parents=True, exist_ok=True)
    inventory(args.source, args.output)

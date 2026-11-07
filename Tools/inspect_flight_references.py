"""Read aircraft flight metadata from the user's reference bundles without loading plugins."""
import json
from pathlib import Path

import UnityPy

root = Path(__file__).resolve().parents[1] / "Reference/SU57/FlightPhase2"
results = []
for filename in ("FS41_reference.nobp", "F16M_reference.nobp"):
    env = UnityPy.load(str(root / filename))
    definitions, parameters, scripts = [], [], set()
    for obj in env.objects:
        if obj.type.name == "MonoScript":
            script = obj.read()
            scripts.add((script.m_AssemblyName, script.m_ClassName))
        elif obj.type.name == "MonoBehaviour":
            try:
                data = obj.read_typetree()
            except Exception:
                continue
            if "aircraftParameters" in data and "unitPrefab" in data:
                definitions.append({key: data.get(key) for key in (
                    "m_Name", "jsonKey", "mass", "spawnOffset", "restRotation", "aircraftInfo"
                )})
            if "takeoffSpeed" in data:
                parameters.append({key: data.get(key) for key in (
                    "m_Name", "aircraftName", "takeoffSpeed", "takeoffDistance", "landingSpeed",
                    "approachSpeed", "cornerSpeed", "cruiseThrottle", "aircraftGLimit", "PIDReferenceAirspeed"
                )})
    results.append({
        "file": filename, "definitions": definitions, "parameters": parameters,
        "flight_related_scripts": [s for s in sorted(scripts) if any(
            word in s[1].lower() for word in ("flight", "control", "pilot", "engine", "turbo", "gear", "catapult")
        )]
    })
output = root / "reference-flight-summary.json"
output.write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding="utf-8")
for result in results:
    print(result["file"], json.dumps(result["parameters"], ensure_ascii=False))
print(output)

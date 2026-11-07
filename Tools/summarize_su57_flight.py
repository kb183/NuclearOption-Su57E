"""Summarize recorded in-game telemetry; a completion marker is required for PASS."""
import argparse
import csv
import json
from collections import Counter
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("run", type=Path)
args = parser.parse_args()
with (args.run / "telemetry.csv").open(encoding="utf-8-sig", newline="") as file:
    rows = list(csv.DictReader(file))
report_path = args.run / "flight-validation.txt"
report = report_path.read_text(encoding="utf-8-sig") if report_path.exists() else ""
result = {"run": args.run.name, "result": "passed" if "PASS: automated native-physics flight cycle" in report else "failed" if "FAIL:" in report else "running", "samples": len(rows)}
if rows:
    numbers = lambda key: [float(row[key]) for row in rows]
    health = [float(value) for row in rows for value in row["part_health"].split(";") if value]
    result.update({
        "duration_seconds": numbers("realtime_s")[-1],
        "phase_samples": dict(Counter(row["phase"] for row in rows)),
        "max_altitude_m": max(numbers("altitude_m")),
        "max_agl_m": max(numbers("agl_m")),
        "max_speed_mps": max(numbers("speed_mps")),
        "fuel_used_before_refuel_kg": numbers("fuel_kg")[0] - min(numbers("fuel_kg")),
        "minimum_recorded_part_health": min(health) if health else None,
        "registered_part_count": len(rows[-1]["part_health"].split(";")),
        "last_sample": rows[-1],
    })
(args.run / "summary.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps({k: v for k, v in result.items() if k != "last_sample"}, ensure_ascii=False, indent=2))

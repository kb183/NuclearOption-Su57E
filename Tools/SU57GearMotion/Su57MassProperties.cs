using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

// Data lives inside Su-57.dll alongside the aircraft. No custom component or
// assembly reference is serialized into the prefab.
static class Su57MassProperties
{
    sealed class Entry
    {
        public string name;
        public Vector3 centerOfMass, inertiaPerMass;
        public Quaternion inertiaRotation;
    }
    static readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>();
    internal static int Count => Entries.Count;
    internal static void Clear() { Entries.Clear(); }

    static float Number(JToken token)
    {
        if (token == null || (token.Type != JTokenType.Float && token.Type != JTokenType.Integer))
            throw new InvalidDataException("Missing numeric mass-reference field");
        float value = token.Value<float>();
        if (float.IsNaN(value) || float.IsInfinity(value))
            throw new InvalidDataException("Non-finite mass-reference field");
        return value;
    }

    static Vector3 Vector(JToken token)
    {
        if (token == null) throw new InvalidDataException("Missing mass-reference vector");
        return new Vector3(Number(token["x"]), Number(token["y"]), Number(token["z"]));
    }

    internal static void Load()
    {
        using (var stream = typeof(Su57GearMotion).Assembly.GetManifestResourceStream("SU57.NativeMassProperties.json"))
        {
            if (stream == null) throw new InvalidDataException("Su-57 native mass profile is missing; rebuild the complete single DLL");
            using (var reader = new StreamReader(stream))
            {
                // BepInEx invokes Awake inside Unity's Application class
                // initializer. Use managed JSON here, without JsonUtility's
                // native engine serializer during that early startup phase.
                var profile = JObject.Parse(reader.ReadToEnd());
                var parts = profile["parts"] as JArray;
                if ((int?)profile["schemaVersion"] != 2 || parts == null || parts.Count != 35)
                    throw new InvalidDataException("Su-57 mass profile: expected schema 2 / 35 entries; schema="+
                        profile["schemaVersion"]+", entries="+(parts == null ? "null" : parts.Count.ToString()));
                var loaded = new Dictionary<string, Entry>();
                foreach (var part in parts)
                {
                    string name = part.Value<string>("name");
                    var q = part["inertiaRotation"];
                    if (string.IsNullOrEmpty(name) || q == null)
                        throw new InvalidDataException("Unnamed or incomplete Su-57 mass reference");
                    var entry = new Entry { name = name, centerOfMass = Vector(part["centerOfMass"]),
                        inertiaPerMass = Vector(part["inertiaPerMass"]),
                        inertiaRotation = new Quaternion(Number(q["x"]), Number(q["y"]), Number(q["z"]), Number(q["w"])) };
                    var r = entry.inertiaRotation;
                    float norm = r.x*r.x+r.y*r.y+r.z*r.z+r.w*r.w;
                    if (!(entry.inertiaPerMass.x > 0 && entry.inertiaPerMass.y > 0 && entry.inertiaPerMass.z > 0) ||
                        Math.Abs(norm-1) > .001f)
                        throw new InvalidDataException("Invalid solid mass reference: " + name);
                    loaded.Add(name, entry);
                }
                if (!loaded.ContainsKey("SU57") || !loaded.ContainsKey("$merged"))
                    throw new InvalidDataException("Su-57 mass reference is missing root/merged body");
                Entries.Clear();
                foreach (var item in loaded) Entries.Add(item.Key, item.Value);
            }
        }
    }

    internal static bool IsSu57(Aircraft aircraft)
    {
        return aircraft != null && aircraft.definition != null && aircraft.definition.jsonKey == "SU57";
    }

    static void Set(Rigidbody body, Entry entry)
    {
        if (body == null || !(body.mass > 0)) return;
        body.centerOfMass = entry.centerOfMass;
        body.inertiaTensorRotation = entry.inertiaRotation;
        body.inertiaTensor = entry.inertiaPerMass * body.mass;
    }

    internal static void RestorePart(AeroPart part)
    {
        if (part == null || !IsSu57(part.GetUnit() as Aircraft) || part.IsDetached() ||
            part.rb == null || part.rb.gameObject != part.gameObject) return;
        Entry entry;
        if (Entries.TryGetValue(part.name, out entry))
        {
            Set(part.rb, entry);
            // Preserve the native explicit fuel/weapon center when provided.
            if (part.rb != null && part.CenterOfMass != null)
                part.rb.centerOfMass = part.CenterOfMass.localPosition;
        }
    }

    internal static void Restore(Aircraft aircraft, bool complex)
    {
        if (!IsSu57(aircraft)) return;
        if (complex)
        {
            foreach (var item in aircraft.partLookup) RestorePart(item as AeroPart);
        }
        else
        {
            Entry entry;
            if (Entries.TryGetValue("$merged", out entry)) Set(aircraft.rb, entry);
        }
    }

    internal static void MassChanged(UnitPart part)
    {
        var aircraft = part == null ? null : part.GetUnit() as Aircraft;
        if (!IsSu57(aircraft) || part.IsDetached()) return;
        // In simple mode child parts share the aircraft body. Applying a
        // child's reference there would replace the entire aircraft inertia.
        bool complex = false;
        foreach (var item in aircraft.partLookup)
            if (!item.IsDetached() && item.rb != null && item.rb != aircraft.rb) { complex = true; break; }
        if (complex) RestorePart(part as AeroPart);
        else Restore(aircraft, false);
    }
}

[HarmonyPatch(typeof(AeroPart), "CreateRB")]
static class Su57CreatedBodyMass
{
    [HarmonyPostfix] static void After(AeroPart __instance)
    {
        Su57MassProperties.RestorePart(__instance);
        Su57StructuralContacts.Apply(__instance.GetUnit() as Aircraft);
    }
}

[HarmonyPatch(typeof(UnitPart), "ModifyMass")]
static class Su57ChangedBodyMass
{
    [HarmonyPostfix] static void After(UnitPart __instance) { Su57MassProperties.MassChanged(__instance); }
}

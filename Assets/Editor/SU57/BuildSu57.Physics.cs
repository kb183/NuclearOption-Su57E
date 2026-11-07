using System;
using System.IO;
using System.Linq;
using UnityEngine;

public static partial class BuildSu57
{
    const string NativeMassProfilePath = "Builds/SU57/Internal/SU57.NativeMassProperties.json";

    [Serializable] class NativeMassEntry
    {
        public string name;
        public Vector3 centerOfMass, inertiaPerMass;
        public Quaternion inertiaRotation;
    }
    [Serializable] class NativeMassSource { public string path, sha256; }
    [Serializable] class NativeMassProfile
    {
        public int schemaVersion;
        public NativeMassEntry[] parts;
        public NativeMassSource[] sources;
    }

    static void WriteNativeMassProperties(Transform template)
    {
        const string input = Root+"/Source/SU57_NativeMassProperties.json";
        if (!File.Exists(input))
            throw new Exception("Missing solid mass data; run Tools/prepare_su57_mass_properties.py");
        string json = File.ReadAllText(input);
        var data = JsonUtility.FromJson<NativeMassProfile>(json);
        if (data == null || data.schemaVersion != 2 || data.parts == null || data.parts.Length != 35 ||
            data.sources == null || data.sources.Length == 0)
            throw new Exception("Expected schema 2 solid mass data; run Tools/prepare_su57_mass_properties.py");
        foreach (var source in data.sources)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                string actual = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(source.path))).Replace("-", "").ToLowerInvariant();
                if (actual != source.sha256)
                    throw new Exception("Native mass source changed: "+source.path+"; rerun Tools/prepare_su57_mass_properties.py");
            }
        }
        var expected = template.GetComponentsInChildren<AeroPart>(true)
            .Select(p => p.transform == template ? "SU57" : p.name).Concat(new[] { "$merged" }).OrderBy(n => n).ToArray();
        var authored = data.parts.Select(p => p.name).OrderBy(n => n).ToArray();
        if (!expected.SequenceEqual(authored)) throw new Exception("Solid mass references do not match native structural owners");
        foreach (var part in data.parts)
        {
            var inertia = part.inertiaPerMass;
            if (!(inertia.x > 0 && inertia.y > 0 && inertia.z > 0) ||
                float.IsInfinity(inertia.x) || float.IsInfinity(inertia.y) || float.IsInfinity(inertia.z))
                throw new Exception("Invalid solid inertia: "+part.name);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(NativeMassProfilePath));
        File.WriteAllText(NativeMassProfilePath, json);
        Notes.Add("Embedded 34 original primary-mesh solid mass references and merged primary-hull reference, calculated offline by signed tetrahedral volume integration. Inactive editor rigidbody defaults are not used. This is a uniform-density mesh reference, not a game stability result.");
    }
}

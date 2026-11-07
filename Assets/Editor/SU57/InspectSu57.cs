using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class InspectSu57
{
    public static void Lights()
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Blueprinter/Mods/SU57/SU57.prefab");
        var lines=new System.Collections.Generic.List<string>();
        foreach(var t in prefab.GetComponentsInChildren<Transform>(true))
            if(t.name.ToLowerInvariant().Contains("light") || t.GetComponent<Light>()!=null)
                lines.Add(AnimationUtility.CalculateTransformPath(t,prefab.transform)+" world="+t.position.ToString("F4"));
        var nav=prefab.GetComponent<NavLights>();
        lines.Add(EditorJsonUtility.ToJson(nav,true));
        File.WriteAllLines("Builds/SU57/lights-inspection.txt",lines);
        Debug.Log("SU57_LIGHT_INSPECT_COMPLETE");
    }
    public static void Run()
    {
        var path = "Assets/Blueprinter/_donotship/MonoBehaviour/Multirole1_PLACEHOLDER.asset";
        var def = AssetDatabase.LoadAssetAtPath<AircraftDefinition>(path);
        if (def == null || def.unitPrefab == null) throw new Exception("Missing Ifrit template");
        var go = UnityEngine.Object.Instantiate(def.unitPrefab);
        var lines = new System.Collections.Generic.List<string>();
        lines.Add("DEF " + EditorJsonUtility.ToJson(def));
        lines.Add("PARAMETERS " + EditorJsonUtility.ToJson(def.aircraftParameters));
        foreach (var c in go.GetComponentsInChildren<Component>(true))
        {
            if(c == null) { lines.Add("MISSING SCRIPT"); continue; }
            var p = AnimationUtility.CalculateTransformPath(c.transform, go.transform);
            if (c is Renderer r) {
                var mf=r.GetComponent<MeshFilter>();
                var mesh=mf != null ? mf.sharedMesh : (r is SkinnedMeshRenderer sk ? sk.sharedMesh : null);
                lines.Add("RENDER " + p + " active="+r.gameObject.activeInHierarchy+" pos="+r.transform.position.ToString("F3")+" bounds="+r.bounds+" mesh="+(mesh != null ? mesh.name : "none"));
            }
            if(c is Aircraft || c is IEngine || c is LandingGear || c is WeaponManager || c is Cockpit || c is JetNozzle || c.GetType().Name.Contains("Hardpoint"))
                lines.Add(c.GetType().Name + " " + p + " " + EditorJsonUtility.ToJson(c));
            if(c is Aircraft || c is LandingGear || c is WeaponManager || c is Cockpit || c is IEngine) {
                var so=new SerializedObject(c);var it=so.GetIterator();
                while(it.Next(true)) if(it.propertyType==SerializedPropertyType.ObjectReference)
                    lines.Add("REF " + c.GetType().Name + " " + it.propertyPath + " = " + (it.objectReferenceValue != null ? it.objectReferenceValue.name + "#" + it.objectReferenceValue.GetInstanceID() : "NULL"));
            }
        }
        File.WriteAllLines("Builds/SU57/template-inspection.txt",lines);
        UnityEngine.Object.DestroyImmediate(go);
        Debug.Log("SU57_INSPECT_COMPLETE");
    }
}

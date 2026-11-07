using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static partial class BuildSu57
{
    [Serializable] class WingSkinData { public WingBoneData[] bones; public WingSkinLod[] lods; }
    [Serializable] class WingBoneData
    {
        public string name;
        public string kind;
        public Vector3 position;
        public Quaternion rotation;
    }
    [Serializable] class WingSkinLod
    {
        public string meshFile;
        public int vertexCount;
        public WingSelection[] surfaces;
    }
    [Serializable] class WingSelection { public string name; public int[] vertices; }
    class WingRig { public string[] names; public Transform[] bones; }

    static WingRig ConfigureWingRig(Transform root,WingSkinData data)
    {
        var rig=new WingRig{names=data.bones.Select(b=>b.name).ToArray(),bones=new Transform[data.bones.Length]};
        for(int i=0;i<data.bones.Length;i++)
        {
            var definition=data.bones[i];
            bool left=definition.name.EndsWith("_L",StringComparison.Ordinal);
            string side=left?"L":"R";
            string wing="gearbay_"+side+"/wing1_"+side;
            string tail="fuselage_R/engine_"+side+"/tail_"+side;
            string path;
            switch(definition.kind)
            {
                case "Levcon":path="fuselage_F/wingRoot_"+side;break;
                case "LeadingEdge":path=wing+"/wing1_F"+side;break;
                case "VerticalTail":path=tail;break;
                case "HorizontalTail":path=tail+"/elevator_"+side;break;
                case "Flaperon":path=wing+"/flap_"+side;break;
                case "Aileron":path=wing+"/wing2_"+side+"/aileron_"+side;break;
                default:throw new Exception("Unknown Su-57 wing kind: "+definition.kind);
            }
            var parent=root.Find(path);
            var part=parent!=null?parent.GetComponent<AeroPart>():null;
            if(part==null)throw new Exception("Missing native part for "+definition.name+": "+path);

            // Only add visual hinge children. The native AeroPart transform,
            // joints and original aerodynamic drivers stay intact. The damage
            // geometry pass replaces collider meshes in their local space.
            var axis=Child(parent,"SU57_"+definition.name+"_Axis");
            axis.position=root.TransformPoint(definition.position+ModelOffset);
            axis.rotation=root.rotation*definition.rotation;
            Transform bone;
            switch(definition.kind)
            {
                case "VerticalTail":
                    // Native ControlSurface always rotates around local X.
                    // Point X along the original canted fin's local Y axis.
                    axis.localRotation*=Quaternion.Euler(0,0,90);
                    bone=WingHinge(axis,"Yaw",part,0,0,-25,false,60);
                    break;
                case "HorizontalTail":
                    bone=WingHinge(axis,"PitchRoll",part,-25,left?-10:10,0,false,60);
                    break;
                case "Aileron":
                    bone=WingHinge(axis,"Roll",part,0,left?-22:22,0,false,70);
                    break;
                case "Flaperon":
                    // Separate, coaxial hinges retain roll authority while the
                    // native flap mode supplies the landing deflection.
                    var flap=WingHinge(axis,"LandingFlap",part,20,0,0,true,25);
                    bone=WingHinge(flap,"Roll",part,0,left?-14:14,0,false,60);
                    break;
                case "LeadingEdge":
                    // Reverse X so the native negative flap angle lowers a
                    // leading edge rather than raising it.
                    axis.localRotation*=Quaternion.Euler(0,180,0);
                    bone=WingHinge(axis,"LandingSlat",part,15,0,0,true,25);
                    break;
                case "Levcon":
                    var landingAxis=Child(axis,"LandingAxis");
                    landingAxis.localRotation=Quaternion.Euler(0,180,0);
                    var landing=WingHinge(landingAxis,"LandingDroop",part,8,0,0,true,25);
                    var pitchAxis=Child(landing,"PitchAxis");
                    pitchAxis.localRotation=Quaternion.Euler(0,180,0);
                    bone=WingHinge(pitchAxis,"Pitch",part,12,0,0,false,45);
                    break;
                default:throw new Exception("Missing wing driver: "+definition.kind);
            }
            rig.bones[i]=bone;
            Notes.Add("Wing "+definition.name+": "+definition.kind+", source pivot="+definition.position.ToString("F4")+", native part="+path);
        }
        Notes.Add("Wing motion: twelve source panels, native ControlSurface jobs; tail pitch +/-25 plus roll +/-10, canted fins yaw +/-25, ailerons +/-22; flaperons 20 landing plus +/-14 roll, leading edges 15 landing, LEVCON 8 landing plus +/-12 pitch. Landing mode uses native gear Extending/LockedExtended state; angles are prototype settings, not measured aircraft data.");
        Notes.Add("Existing native AeroPart flight physics and original controllers retained; new wing drivers have no split drag or added aerodynamic force; no runtime DLL.");
        return rig;
    }

    static Transform WingHinge(Transform parent,string name,AeroPart part,float pitch,float roll,float yaw,bool flap,float speed)
    {
        var hinge=Child(parent,name);
        var drive=hinge.gameObject.AddComponent<ControlSurface>();
        var so=new SerializedObject(drive);
        so.FindProperty("attachedSurface").objectReferenceValue=part;
        so.FindProperty("visibleMesh").objectReferenceValue=hinge.gameObject;
        so.FindProperty("pitchRange").floatValue=pitch;
        so.FindProperty("rollRange").floatValue=roll;
        so.FindProperty("yawRange").floatValue=yaw;
        so.FindProperty("brakeRange").floatValue=0;
        so.FindProperty("flap").boolValue=flap;
        so.FindProperty("servoSpeed").floatValue=speed;
        so.FindProperty("maxSplit").floatValue=0;
        so.FindProperty("splitDrag").floatValue=0;
        so.ApplyModifiedPropertiesWithoutUndo();
        return hinge;
    }

}

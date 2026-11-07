using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class BuildSu57
{
    // Aircraft-local coordinates measured from the BA open-gear mesh, after
    // LoadMesh's X reflection and ModelOffset. These are model measurements,
    // not claimed real-aircraft dimensions. The nose has an additional 15 cm
    // extension to give a slight nose-up ground stance after suspension sag.
    const float NoseStrutExtension = .15f;
    const float NoseSpringRate = 650000f;
    const float NoseDampingRate = 100000f;
    // Align the nose SUPPORT root with the marked rear recess. Its tyre has
    // a longitudinal offset from the hinge, so tyre Z cannot locate the root.
    const float NoseSupportSlotZ = 2.60f;
    // Keep deployed height; Z is resolved from the actual support offset below.
    static readonly Vector3 NoseWheelCenter = new Vector3(0,-1.908f-NoseStrutExtension,3.0294f);
    // Align the SUPPORT with the marked slot, not the offset tyre centre.
    // At the deployed hinge's Z the main cover spans about X=1.75..2.24 m.
    const float MainSupportSlotX = 2.00f;
    // X is resolved from the actual support/tyre offset during placement.
    static readonly Vector3 MainWheelYZ = new Vector3(0,-1.676f,-2.9451f);
    const float MainBraceAnchorX = MainSupportSlotX-.22756f;
    const float NoseWheelRadius = .292f;
    const float MainWheelRadius = .524f;

    sealed class GearDoorRig
    {
        public Transform noseLeft, noseRight, mainLeft, mainRight;
        public readonly List<Renderer[]> lodRenderers=new List<Renderer[]>();
        public Renderer[] braceRenderers;
    }

    static GearDoorRig ConfigureLandingGear(Transform root)
    {
        var gears=root.GetComponentsInChildren<LandingGear>(true);
        if(gears.Length!=3)throw new Exception("Expected three native landing gear assemblies");
        var nose=gears.Single(g=>g.name=="gear_F_sprung");
        var left=gears.Single(g=>g.name=="gear_L_sprung");
        var right=gears.Single(g=>g.name=="gear_R_sprung");
        var noseSo=new SerializedObject(nose);
        // The shorter wheelbase places about 3x the old load on the nose gear.
        // At the inherited full-fuel mass/CG, 650 kN/m gives similar static sag
        // to each 500 kN/m main strut. This is an initial tune for user testing.
        noseSo.FindProperty("springRate").floatValue=NoseSpringRate;
        noseSo.FindProperty("dampingRate").floatValue=NoseDampingRate;
        noseSo.ApplyModifiedPropertiesWithoutUndo();
        var noseWheelTarget=PositionLandingGear(root,nose,"fuselage_F/cockpit/gearHinge_F",NoseWheelCenter,NoseWheelRadius,NoseStrutExtension,supportSlotZ:NoseSupportSlotZ);
        PositionLandingGear(root,left,"gearbay_L/gearMount_L",MainWheelYZ,MainWheelRadius,useTireMeshCenter:true,supportSlotX:-MainSupportSlotX);
        PositionLandingGear(root,right,"gearbay_R/gearMount_R",MainWheelYZ,MainWheelRadius,useTireMeshCenter:true,supportSlotX:MainSupportSlotX);

        // The brace targets are outside the movable gear subtrees. Place them
        // in the Su-57 bays as well, instead of leaving the KR-67's IK anchors.
        float noseLongitudinalShift=noseWheelTarget.z-NoseWheelCenter.z;
        SetGearPoint(root,"fuselage_F/gear_F_armTarget",new Vector3(0,-.65f,4.65f+noseLongitudinalShift));
        SetGearPoint(root,"gearbay_L/gear_L_armTarget",new Vector3(-MainBraceAnchorX,-.1542f,-1.3799f));
        SetGearPoint(root,"gearbay_R/gear_R_armTarget",new Vector3(MainBraceAnchorX,-.1542f,-1.3799f));
        // Keep the direct forward/up fold and zero lower-strut yaw. Match the
        // folded tyre mesh centre to the marked main bay. The companion stages
        // the lift and inward travel; retain the deployed wheel stance.
        ConfigureMainGearStow(root,left);
        ConfigureMainGearStow(root,right);
        ConfigureMainGearUpperHousing(left);
        ConfigureMainGearUpperHousing(right);
        // Fit only the diagonal brace. Keep the wheel endpoint and the native
        // main struts/suspension intact after the user's position acceptance.
        ConfigureNoseGearGeometry(nose);
        var braceRenderers=ConfigureMainGearBrace(left).Concat(ConfigureMainGearBrace(right)).Concat(ConfigureNoseGearBrace(nose)).ToArray();

        // The Su-57 nose bay is forward of its tire. Reverse the KR-67 nose
        // fold direction so the wheel stows towards that bay, not behind it.
        noseSo.Update();
        noseSo.FindProperty("foldDegrees").floatValue=-92f;
        // Any native gear can lock the aircraft-wide gear state. Match all
        // three stroke durations so the nose cannot end the new main stroke
        // early and snap the main wheels inward while closing their doors.
        noseSo.FindProperty("foldSpeed").floatValue=92f/MainGearTravelSeconds;
        // Compensate the extra oleo extension only at the folded endpoint, so
        // increasing ground clearance does not push the stowed tire forward.
        var closedExtension=Quaternion.Euler(-92f,0,0)*(Vector3.down*NoseStrutExtension);
        noseSo.FindProperty("hingeFoldMotion").vector3Value-=closedExtension;
        noseSo.ApplyModifiedPropertiesWithoutUndo();
        ConfigureLandingGearLighting(root,nose);
        ConfigureLandingGearMaterials(gears,braceRenderers);

        var rig=new GearDoorRig
        {
            braceRenderers=braceRenderers,
            noseLeft=GearDoorPivot(root,"fuselage_F/cockpit","SU57_NoseDoor_L",new Vector3(-.275f,-.70f,4.10f)),
            noseRight=GearDoorPivot(root,"fuselage_F/cockpit","SU57_NoseDoor_R",new Vector3(.275f,-.70f,4.10f)),
            mainLeft=GearDoorPivot(root,"gearbay_L","SU57_MainDoor_L",new Vector3(-2.55f,-.195f,-1.75f)),
            mainRight=GearDoorPivot(root,"gearbay_R","SU57_MainDoor_R",new Vector3(2.55f,-.195f,-1.75f))
        };
        SetGearDoors(nose,new[]{rig.noseLeft,rig.noseRight},new[]{-90f,90f});
        SetGearDoors(left,new[]{rig.mainLeft},new[]{-65f});
        SetGearDoors(right,new[]{rig.mainRight},new[]{65f});
        Notes.Add("Su-57 gear: deployed main SUPPORT hinges at aircraft X=+/-2.00 m in marked slots, instead of v0.2.22's +/-1.411 m; tyre/contact X resolved from the actual scaled tyre mesh offset, about +/-2.489 m; main height, wheelbase and radius retained; nose oleo extended .15 m; nose radius=.292, main radius=.524; native steering/brakes retained");
        Notes.Add("Nose suspension: spring=650000 N/m, damping=100000 N s/m; loaded-sag compensation is an initial tune, user acceptance pending");
        Notes.Add("Nose SUPPORT root aligned to marked recess at aircraft Z="+NoseSupportSlotZ.ToString("F2",CI)+" m; complete nose assembly and fixed brace target shifted longitudinally by "+noseLongitudinalShift.ToString("F4",CI)+" m; wheel height/radius, suspension and original forward fold retained; exact recess fit awaits user acceptance");
        Notes.Add("All three native gear strokes take "+MainGearTravelSeconds.ToString("F1",CI)+" s (main "+(Mathf.Abs(MainGearFoldDegrees)/MainGearTravelSeconds).ToString("F3",CI)+" deg/s, nose "+(92f/MainGearTravelSeconds).ToString("F3",CI)+" deg/s) before/after the original door phase, preventing early aircraft-wide locking by the nose gear");
        Notes.Add("Su-57 bay covers: BA nose/main panels use native LandingGear door animation in all LODs; main weapon covers now use their own BayDoors; nose folds forward -92 deg. User acceptance pending.");
        return rig;
    }

    static Vector3 PositionLandingGear(Transform root,LandingGear gear,string mountPath,Vector3 target,float radius,float strutExtension=0f,bool useTireMeshCenter=false,float? supportSlotX=null,float? supportSlotZ=null)
    {
        var so=new SerializedObject(gear);
        var bumpStop=so.FindProperty("bumpStop").objectReferenceValue as GameObject;
        var unsprung=so.FindProperty("unsprung").objectReferenceValue as GameObject;
        var castPoint=so.FindProperty("castPoint").objectReferenceValue as Transform;
        var mount=root.Find(mountPath);
        var wheelArray=so.FindProperty("wheels");
        if(bumpStop==null || unsprung==null || castPoint==null || mount==null || wheelArray.arraySize==0)
            throw new Exception("Incomplete landing gear references: "+gear.name);
        // Moving only a gear mount keeps the AeroPart rigidbody/joint anchors
        // in place. ValidatePhysicsPartTransforms continues to protect them.
        if(mount.GetComponentsInChildren<AeroPart>(true).Length!=0)
            throw new Exception("Refusing to reposition a physical AeroPart: "+mountPath);
        var wheels=new List<Transform>();
        for(int i=0;i<wheelArray.arraySize;i++)
        {
            var wheel=wheelArray.GetArrayElementAtIndex(i).objectReferenceValue as Transform;
            if(wheel==null || !wheel.IsChildOf(unsprung.transform))throw new Exception("Unresolved landing gear wheel: "+gear.name);
            wheels.Add(wheel);
        }
        var before=root.InverseTransformPoint(wheels.Aggregate(Vector3.zero,(v,t)=>v+t.position)/wheels.Count);
        float oldRadius=so.FindProperty("wheelRadius").floatValue;
        float travel=so.FindProperty("suspensionTravel").floatValue;
        so.FindProperty("wheelRadius").floatValue=radius;
        so.ApplyModifiedPropertiesWithoutUndo();
        // Extend the lower leg relative to its upper strut, not the tire or
        // the whole mounting hinge. Runtime suspension uses this bump stop.
        bumpStop.transform.position-=bumpStop.transform.up*strutExtension;
        // LandingGear.FixedUpdate overwrites unsprung.position every frame.
        // Use that exact fully-extended equation before measuring the delta,
        // otherwise the apparent editor alignment jumps on the first frame.
        unsprung.transform.position=bumpStop.transform.position-bumpStop.transform.up*(travel-radius);
        // Main tyres have a mesh offset along the axle. Scale before measuring
        // that offset; their visible centre must coincide with the contact ray.
        if(useTireMeshCenter)foreach(var wheel in wheels)wheel.localScale*=radius/oldRadius;
        var center=useTireMeshCenter?MainGearTireMeshCenter(so,unsprung.transform):wheels.Aggregate(Vector3.zero,(v,t)=>v+t.position)/wheels.Count;
        if(supportSlotX.HasValue || supportSlotZ.HasValue)
        {
            var support=so.FindProperty("gearHinge").objectReferenceValue as Transform;
            if(support==null)throw new Exception("Missing gear support for slot placement: "+gear.name);
            // Preserve the measured tyre-to-support offset; target the support
            // directly on the selected axis. Target Y retains ground height.
            var wheelPosition=root.InverseTransformPoint(center);
            var supportPosition=root.InverseTransformPoint(support.position);
            if(supportSlotX.HasValue)target.x=wheelPosition.x+supportSlotX.Value-supportPosition.x;
            if(supportSlotZ.HasValue)target.z=wheelPosition.z+supportSlotZ.Value-supportPosition.z;
        }
        var delta=target-root.InverseTransformPoint(center);
        mount.position+=root.TransformVector(delta);
        if(!useTireMeshCenter)foreach(var wheel in wheels)wheel.localScale*=radius/oldRadius;
        center=useTireMeshCenter?MainGearTireMeshCenter(so,unsprung.transform):wheels.Aggregate(Vector3.zero,(v,t)=>v+t.position)/wheels.Count;
        // Linecast travels from this point down by suspensionTravel. Its end
        // coincides with the tire bottom at zero suspension compression.
        castPoint.position=center+bumpStop.transform.up*(travel-radius);
        castPoint.rotation=bumpStop.transform.rotation;
        Notes.Add(gear.name+": serialized wheel="+before.ToString("F4")+"; zero-compression target="+target.ToString("F4")+"; mount delta="+delta.ToString("F4")+"; radius="+radius.ToString("F3",CI));
        return target;
    }

    static void SetGearPoint(Transform root,string path,Vector3 position)
    {
        var point=root.Find(path);
        if(point==null)throw new Exception("Missing landing gear brace target: "+path);
        point.position=root.TransformPoint(position);
    }

    static Transform GearDoorPivot(Transform root,string parentPath,string name,Vector3 position)
    {
        var parent=root.Find(parentPath);
        if(parent==null)throw new Exception("Missing gear door parent: "+parentPath);
        var pivot=Child(parent,name);
        pivot.position=root.TransformPoint(position);
        pivot.rotation=root.rotation;
        return pivot;
    }

    static void SetGearDoors(LandingGear gear,Transform[] pivots,float[] openDegrees)
    {
        var so=new SerializedObject(gear);
        var doors=so.FindProperty("gearDoors");
        doors.arraySize=pivots.Length;
        for(int i=0;i<pivots.Length;i++)
        {
            var door=doors.GetArrayElementAtIndex(i);
            door.FindPropertyRelative("transform").objectReferenceValue=pivots[i];
            door.FindPropertyRelative("closedAngle").vector3Value=Vector3.zero;
            door.FindPropertyRelative("openAngle").vector3Value=new Vector3(0,0,openDegrees[i]);
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // Closed source geometry includes weapon-bay covers too. Split complete
    // triangles into disjoint regions; never rotate the central weapons doors
    // when operating the landing gear. Section zero is consumed by the
    // independent native weapon-bay door builder, not emitted here.
    static int GearCoverSection(Vector3 sourcePoint)
    {
        if(sourcePoint.z>3f)return sourcePoint.x<0?1:2;
        if(Mathf.Abs(sourcePoint.x)>1.4f)return sourcePoint.x<0?3:4;
        return 0;
    }

    static Renderer[] CreateLandingGearCovers(Transform model,Mesh source,Material skin,GearDoorRig rig,int lod)
    {
        var parents=new[]{model,rig.noseLeft,rig.noseRight,rig.mainLeft,rig.mainRight};
        var names=new[]{"WeaponBayCovers","NoseDoor_L","NoseDoor_R","MainDoor_L","MainDoor_R"};
        var renderers=new List<Renderer>();
        var sourceVertices=source.vertices;
        var sourceNormals=source.normals;
        var sourceUv=source.uv;
        for(int section=0;section<parents.Length;section++)
        {
            if(section==0){renderers.Add(null);continue;} // Created by ConfigureSu57Weapons.
            var matrix=parents[section].worldToLocalMatrix*model.localToWorldMatrix;
            var normalMatrix=matrix.inverse.transpose;
            var map=new Dictionary<int,int>();
            var vertices=new List<Vector3>();
            var normals=new List<Vector3>();
            var uv=new List<Vector2>();
            var triangles=new List<int>();
            for(int submesh=0;submesh<source.subMeshCount;submesh++)
            {
                var indices=source.GetTriangles(submesh);
                for(int t=0;t<indices.Length;t+=3)
                {
                    var center=(sourceVertices[indices[t]]+sourceVertices[indices[t+1]]+sourceVertices[indices[t+2]])/3f;
                    if(GearCoverSection(center)!=section)continue;
                    for(int corner=0;corner<3;corner++)
                    {
                        int index=indices[t+corner];
                        if(!map.TryGetValue(index,out var mapped))
                        {
                            mapped=vertices.Count;map.Add(index,mapped);
                            vertices.Add(matrix.MultiplyPoint3x4(sourceVertices[index]));
                            if(sourceNormals.Length==sourceVertices.Length)normals.Add(normalMatrix.MultiplyVector(sourceNormals[index]).normalized);
                            if(sourceUv.Length==sourceVertices.Length)uv.Add(sourceUv[index]);
                        }
                        triangles.Add(mapped);
                    }
                }
            }
            if(triangles.Count==0)throw new Exception("Empty Su-57 cover section: "+names[section]+" LOD"+lod);
            var mesh=new Mesh{name="SU57_"+names[section]+"_LOD"+lod,indexFormat=IndexFormat.UInt32};
            mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);
            if(normals.Count==vertices.Count)mesh.SetNormals(normals);else mesh.RecalculateNormals();
            if(uv.Count==vertices.Count){mesh.SetUVs(0,uv);mesh.RecalculateTangents();}
            mesh.RecalculateBounds();
            mesh=SaveMesh(mesh,Root+"/"+mesh.name+".asset");
            renderers.Add(MeshObject(parents[section],mesh.name,mesh,new[]{skin}));
        }
        var result=renderers.ToArray();
        rig.lodRenderers.Add(result);
        return result;
    }

    static void ConfigureGearDoorLods(GearDoorRig rig,LODGroup aircraftLods)
    {
        // Each moving panel owns its renderers/LODGroup below the same native
        // damage part; do not put external renderers in SU57_Model's LODGroup.
        var parents=new[]{rig.noseLeft,rig.noseRight,rig.mainLeft,rig.mainRight};
        for(int panel=0;panel<parents.Length;panel++)
        {
            var lods=new LOD[3];
            for(int lod=0;lod<3;lod++)
                lods[lod]=new LOD(new[]{.35f,.12f,.015f}[lod],new[]{rig.lodRenderers[lod][panel+1]});
            var group=parents[panel].gameObject.AddComponent<LODGroup>();
            group.SetLODs(lods);group.RecalculateBounds();
            // Use aircraft-sized LOD distances so a small door is not culled
            // while the rest of the aircraft is still displayed nearby.
            group.localReferencePoint=parents[panel].InverseTransformPoint(aircraftLods.transform.TransformPoint(aircraftLods.localReferencePoint));
            group.size=aircraftLods.size;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static partial class BuildSu57
{
    const string Root = "Assets/Blueprinter/Mods/SU57";
    const string Game = "Assets/Blueprinter/_donotship";
    static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../Builds/SU57"));
    const string Version = "0.2.34";
    static readonly CultureInfo CI = CultureInfo.InvariantCulture;
    static readonly List<string> Notes = new List<string>();
    // Prototype mixing, not measured Su-57 actuator data. The native surface
    // driver adds these channels, so their absolute sum stays within 36 deg.
    const float VectorCant = 30f;
    const float VectorPitch = -22f;
    const float VectorDifferential = 7f;
    const float VectorServoSpeed = 45f;
    static readonly Vector3 ModelOffset = new Vector3(0,-2.2f,0);

    [MenuItem("Blueprinter/Su-57/Build and Validate")]
    public static void Run() => Build();
    public static void Rebuild() => Build();

    static void Build()
    {
        Notes.Clear();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        // Use the configured project render pipeline; game placeholders remain read-only.
        var baseDefinition = AssetDatabase.LoadAssetAtPath<AircraftDefinition>(Game + "/MonoBehaviour/Multirole1_PLACEHOLDER.asset");
        if (baseDefinition == null || baseDefinition.unitPrefab == null || baseDefinition.aircraftParameters == null)
            throw new Exception("Vanilla flight template has unresolved definition/prefab/parameters");
        var basePrefab = baseDefinition.unitPrefab;
        var missing = Missing(basePrefab);
        if (missing != 0) throw new Exception("Vanilla template missing scripts: " + missing);
        var definition = SaveCopy(baseDefinition, Root + "/SU57_Definition.asset");
        var parameters = SaveCopy(baseDefinition.aircraftParameters, Root + "/SU57_Params.asset");
        definition.name = "SU57_Definition";
        definition.jsonKey = "SU57"; definition.unitName = "Su-57"; definition.bogeyName = "Su-57";
        definition.code = "SU57"; definition.WorkshopTag = "Su-57"; definition.value = 100f;
        definition.description = "Su-57 multirole fighter. Prototype flight systems and weapons use the vanilla twin-engine KR-67 baseline.";
        definition.length = 19.51f; definition.width = 13.93f; definition.height = 4.46f;
        // The inherited KR-67 z=-1.4 puts the longer Su-57 tail through the
        // shelter's rear door. Center the authored hull between both doors.
        // Keep the established vertical offset and landing gear datum.
        var spawnOffset = definition.spawnOffset;
        spawnOffset.z = .23f;
        definition.spawnOffset = spawnOffset;
        var iconPath=Root+"/SU57_MapIcon.png";
        var iconImporter=AssetImporter.GetAtPath(iconPath) as TextureImporter;
        if(iconImporter!=null && iconImporter.textureType!=TextureImporterType.Sprite)
        {
            iconImporter.textureType=TextureImporterType.Sprite;
            iconImporter.spriteImportMode=SpriteImportMode.Single;
            iconImporter.alphaIsTransparency=true;
            iconImporter.SaveAndReimport();
        }
        definition.mapIcon=AssetDatabase.LoadAssetAtPath<Sprite>(iconPath);
        definition.aircraftParameters = parameters;
        parameters.name = "SU57_Params"; parameters.aircraftName = "Su-57"; parameters.rankRequired = 4;
        ConfigureLivery(parameters);
        var instance = Object.Instantiate(basePrefab);
        instance.name = "SU57";
        var aircraft = instance.GetComponent<Aircraft>();
        if (aircraft == null || instance.GetComponent<Rigidbody>() == null || instance.GetComponentInChildren<WeaponManager>(true) == null)
            throw new Exception("Template lacks aircraft physics or weapons");
        foreach (var c in instance.GetComponentsInChildren<Component>(true))
        {
            if (c == null) continue;
            var so = new SerializedObject(c); var p = so.GetIterator(); var changed = false;
            while (p.Next(true))
            {
                if (p.propertyType != SerializedPropertyType.ObjectReference) continue;
                if (p.objectReferenceValue == baseDefinition) { p.objectReferenceValue = definition; changed = true; }
                else if (p.objectReferenceValue == baseDefinition.aircraftParameters) { p.objectReferenceValue = parameters; changed = true; }
            }
            if (changed) so.ApplyModifiedPropertiesWithoutUndo();
        }
        ((Unit)aircraft).definition = definition;
        var cockpitSet = new HashSet<Renderer>();
        var aircraftSo = new SerializedObject(aircraft);
        var cockpits = aircraftSo.FindProperty("cockpitRenderers");
        if (cockpits != null) for (var i=0; i<cockpits.arraySize; i++)
        {
            var renderer = cockpits.GetArrayElementAtIndex(i).objectReferenceValue as Renderer;
            if (renderer != null) cockpitSet.Add(renderer);
        }
        var retained = new List<Renderer>();
        foreach (var r in instance.GetComponentsInChildren<Renderer>(true))
        {
            if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
            string path = AnimationUtility.CalculateTransformPath(r.transform, instance.transform).ToLowerInvariant();
            bool keepCockpit = cockpitSet.Contains(r) && !path.Contains("canopy");
            bool keep = keepCockpit || path.Contains("gearhinge") || path.Contains("gearlight") || path.Contains("wheel") || path.Contains("afterburn") || path.Contains("thrusttransform") || path.Contains("navlight");
            if (keep) {retained.Add(r); continue;}
            // Remove old geometry while preserving component references used by game systems.
            if (r is SkinnedMeshRenderer skinned) skinned.sharedMesh = null;
            var filter = r.GetComponent<MeshFilter>(); if (filter != null) filter.sharedMesh = null;
        }
        AlignCockpit(instance.transform);
        AlignNavigationLights(instance.transform);
        var gearDoors = ConfigureLandingGear(instance.transform);
        ValidatePhysicsPartTransforms(instance.transform,basePrefab.transform);
        var skin = CreateSkin();
        var glass = CreateGlass();
        var model = new GameObject("SU57_Model"); model.layer=instance.layer;
        model.transform.SetParent(instance.transform, false);
        model.transform.localPosition = ModelOffset;
        // Original BA engine origins, including the source body's +1.023153 Y.
        // The physical nozzle parents, their colliders and joints stay in place.
        var nozzleL = ConfigureVectoring(instance.transform,"L",new Vector3(-1.367776f,2.185259f,-7.989593f));
        var nozzleR = ConfigureVectoring(instance.transform,"R",new Vector3(1.367780f,2.185260f,-7.989590f));
        var wingData=JsonUtility.FromJson<WingSkinData>(File.ReadAllText(Root+"/Source/SU57_WingSkin.json"));
        if(wingData==null || wingData.bones==null || wingData.bones.Length!=12 || wingData.lods==null || wingData.lods.Length!=3)
            throw new Exception("Missing twelve-surface, three-LOD wing data; run Tools/prepare_su57_wing_skin.py");
        var wingRig=ConfigureWingRig(instance.transform,wingData);
        var damageRenderers=CreateDamageGeometry(instance.transform,model.transform,wingRig,nozzleL,nozzleR,skin,glass);
        var weaponRenderers=ConfigureSu57Weapons(instance.transform,model.transform,skin,parameters);
        var lods = new List<LOD>();
        var covers = new[]{"RU_SU_57_closed_-3781013679314548165.obj", "02_RU_SU_57_closed_-6969719833142860783.obj", "02_RU_SU_57_closed_-6969719833142860783.obj"};
        for (var i=0; i<3; i++)
        {
            var coverRenderers=CreateLandingGearCovers(model.transform,LoadMesh(covers[i]),skin,gearDoors,i);
            // Weapon covers now belong to four independent native BayDoors.
            // The root group remains as the reference for existing gear LODs.
            lods.Add(new LOD(new[]{.35f,.12f,.015f}[i],Array.Empty<Renderer>()));
        }
        var group=model.AddComponent<LODGroup>(); group.SetLODs(lods.ToArray()); group.RecalculateBounds();
        group.size=19.51f;
        ConfigureGearDoorLods(gearDoors,group);
        // Read offline solid-mesh references. Inactive editor rigidbodies do
        // not provide reliable computed inertia in the authoring scene.
        WriteNativeMassProperties(basePrefab.transform);
        ConfigureDamageColliders(instance.transform,model.transform);
        ConnectDamageRenderers(instance.transform);
        ValidatePhysicsPartTransforms(instance.transform,basePrefab.transform);
        var exterior = aircraftSo.FindProperty("exteriorRenderers");
        if (exterior != null)
        {
            var all=damageRenderers.Concat(weaponRenderers).Concat(model.GetComponentsInChildren<Renderer>()).Concat(gearDoors.lodRenderers.SelectMany(r=>r)).Concat(gearDoors.braceRenderers).Concat(retained.Where(r=>!cockpitSet.Contains(r))).Where(r=>r!=null).Distinct().ToArray();
            exterior.arraySize=all.Length;
            for(var i=0;i<all.Length;i++) exterior.GetArrayElementAtIndex(i).objectReferenceValue=all[i];
            aircraftSo.ApplyModifiedPropertiesWithoutUndo();
        }
        var prefab = PrefabUtility.SaveAsPrefabAsset(instance,Root+"/SU57.prefab");
        definition.unitPrefab = prefab;
        EditorUtility.SetDirty(definition);EditorUtility.SetDirty(parameters);
        var opPath=Root+"/SU57_HardenedShelter.asset";
        var op=AssetDatabase.LoadAssetAtPath<Blueprinter.OpAddAircraftToHangars>(opPath);
        if(op==null){op=ScriptableObject.CreateInstance<Blueprinter.OpAddAircraftToHangars>();AssetDatabase.CreateAsset(op,opPath);}
        op.aircraftJsonKey="SU57";
        op.hangars=new List<Blueprinter.OpAddAircraftToHangars.HangarTarget>{new Blueprinter.OpAddAircraftToHangars.HangarTarget{hangarUnitJsonKey="shelter1",hangarNames=new List<string>{"shelter1"}}};
        EditorUtility.SetDirty(op);AssetDatabase.SaveAssets();
        Directory.CreateDirectory(Output);
        // Sections and their motion pivots live below physical parts. Preview
        // the entire aircraft so each independent section remains present.
        RenderPreview(instance,Output+"/SU57_preview.png",new Vector3(25,16,28));
        RenderPreview(instance,Output+"/SU57_complete.png",new Vector3(25,16,28));
        RenderPreview(instance,Output+"/SU57_top.png",new Vector3(0,38,.01f));
        RenderCanopyComparison(instance,glass);
        Notes.Add("Template: KR-67 Ifrit, vanilla twin-engine flight/weapon/cockpit systems");
        Notes.Add("Vectoring: native ControlSurface drives mirrored "+VectorCant.ToString("0.#",CI)+" deg canted single-axis nozzle pivots; pitch="+VectorPitch.ToString("0.#",CI)+", differential roll/yaw=+/-"+VectorDifferential.ToString("0.#",CI)+", per-channel servo="+VectorServoSpeed.ToString("0.#",CI)+" deg/s; combined limit="+(Mathf.Abs(VectorPitch)+2*Mathf.Abs(VectorDifferential)).ToString("0.#",CI)+" deg");
        Notes.Add("Vectoring uses native ControlSurface; native Turbojet transform writes disabled to avoid competing drivers. Native 250 m/s vector cutoff is not used by the new surface driver; in-game handling awaits user acceptance. The single-file mod embeds this aircraft bundle, the marked gear/brace patches and selectable Su-57 paint-material updates in the same DLL.");
        Notes.Add("Aircraft components: "+prefab.GetComponentsInChildren<Aircraft>(true).Length);
        int engineCount=prefab.GetComponentsInChildren<MonoBehaviour>(true).Count(c=>c is IEngine);
        Notes.Add("Engines: "+engineCount);
        Notes.Add("Landing gear: "+prefab.GetComponentsInChildren<LandingGear>(true).Length);
        Notes.Add("Missing scripts: "+Missing(prefab));
        Notes.Add("Rank: "+parameters.rankRequired+"; value (million): "+definition.value);
        Notes.Add("Definition points to SU57 prefab: "+(definition.unitPrefab==prefab));
        Notes.Add("Prefab points to SU57 definition: "+(((Unit)prefab.GetComponent<Aircraft>()).definition==definition));
        Notes.Add("Hangar: shelter1/shelter1");
        ValidatePrefab(prefab,definition,parameters);
        Object.DestroyImmediate(instance);
        Blueprinter.ModInfo.Save("SU57","Su-57",Version);
        // Only Install/Su-57.dll is a distributable. Keep authoring exports and
        // the intermediate bundle out of that folder to avoid partial installs.
        string internalOutput=Output+"/Internal";
        Directory.CreateDirectory(internalOutput);
        string bundle=internalOutput+"/Su-57_"+Version+".nobp";
        if(File.Exists(bundle))File.Delete(bundle);
        Blueprinter.ModBuilder.Build("SU57","Su-57",Version,internalOutput);
        if(!File.Exists(bundle))throw new Exception("Blueprinter did not produce the expected bundle");
        string sourceOutput=Output+"/Source";
        Directory.CreateDirectory(sourceOutput);
        Blueprinter.SourceExporter.ExportMod("SU57", "Su-57", Version, sourceOutput+"/Su-57_"+Version+".source.zip");
        var install=BuildSingleFileMod(bundle);
        File.WriteAllLines(Output+"/SU57_preflight.txt",Notes);
        Debug.Log("SU57_BUILD_COMPLETE: "+install);
        Debug.Log("SU57_INSTALL_PACKAGE: "+install+" (single DLL; aircraft bundle and gear animation embedded)");
    }

    static int Missing(GameObject p) => p.GetComponentsInChildren<Transform>(true).Sum(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));

    static Transform Child(Transform parent,string name)
    {
        var go=new GameObject(name);go.layer=parent.gameObject.layer;
        go.transform.SetParent(parent,false);return go.transform;
    }

    static Transform ConfigureVectoring(Transform root,string side,Vector3 sourceOrigin)
    {
        var engine=root.Find("fuselage_R/engine_"+side);
        var nozzle=engine!=null?engine.Find("nozzle_"+side):null;
        if(nozzle==null)throw new Exception("Missing native nozzle: "+side);
        var part=nozzle.GetComponent<AeroPart>();
        var turbojet=engine.GetComponent<Turbojet>();
        var jet=nozzle.GetComponent<JetNozzle>();
        if(part==null || turbojet==null || jet==null)throw new Exception("Unresolved nozzle physics: "+side);
        var axis=Child(nozzle,"SU57_VectorAxis_"+side);
        axis.position=root.TransformPoint(sourceOrigin+ModelOffset);
        axis.rotation=root.rotation*Quaternion.Euler(0,0,side=="L"?VectorCant:-VectorCant);
        var pivot=Child(axis,"SU57_VectorPivot_"+side);
        // Counter-rotate the neutral bone, keeping the source skin and thrust
        // direction unchanged at zero input while the hinge axis remains canted.
        var bone=Child(pivot,"SU57_NozzleBone_"+side);
        bone.localRotation=Quaternion.Inverse(axis.localRotation);

        var drive=pivot.gameObject.AddComponent<ControlSurface>();
        var so=new SerializedObject(drive);
        so.FindProperty("attachedSurface").objectReferenceValue=part;
        so.FindProperty("visibleMesh").objectReferenceValue=pivot.gameObject;
        so.FindProperty("pitchRange").floatValue=VectorPitch;
        so.FindProperty("rollRange").floatValue=side=="L"?-VectorDifferential:VectorDifferential;
        so.FindProperty("yawRange").floatValue=side=="L"?-VectorDifferential:VectorDifferential;
        so.FindProperty("servoSpeed").floatValue=VectorServoSpeed;
        so.ApplyModifiedPropertiesWithoutUndo();
        // Only one system writes the common nozzle pivot. Engine thrust, fuel,
        // afterburners and critical-part damage still use the native Turbojet.
        so=new SerializedObject(turbojet);
        so.FindProperty("thrustVectoring").vector3Value=Vector3.zero;
        so.FindProperty("vectoringTransforms").arraySize=0;
        so.ApplyModifiedPropertiesWithoutUndo();

        var jetSo=new SerializedObject(jet);
        var thrust=jetSo.FindProperty("thrustTransform").objectReferenceValue as Transform;
        if(thrust==null)throw new Exception("Missing thrust transform: "+side);
        thrust.SetParent(bone,false);
        thrust.localPosition=new Vector3(0,0,-.665f);
        thrust.localRotation=Quaternion.identity;thrust.localScale=Vector3.one;
        var burners=jetSo.FindProperty("afterburners");
        for(int i=0;i<burners.arraySize;i++)
        {
            var burner=burners.GetArrayElementAtIndex(i);
            burner.FindPropertyRelative("thrustDirection").objectReferenceValue=thrust;
            ConfigureCircularFlame(burner,thrust,side,i);
            var glow=burner.FindPropertyRelative("nozzleGlowRenderer").objectReferenceValue as Renderer;
            if(glow!=null)
            {
                glow.transform.SetParent(bone,false);glow.transform.localPosition=thrust.localPosition;
                glow.transform.localRotation=Quaternion.identity;glow.transform.localScale=Vector3.one;
                var mf=glow.GetComponent<MeshFilter>();
                if(mf!=null)mf.sharedMesh=NozzleGlowMesh();
            }
        }
        var particles=jetSo.FindProperty("glow").objectReferenceValue as ParticleSystem;
        if(particles!=null && particles.transform!=thrust)
        {
            particles.transform.SetParent(thrust,false);particles.transform.localPosition=Vector3.zero;
            particles.transform.localRotation=Quaternion.identity;
        }
        jetSo.ApplyModifiedPropertiesWithoutUndo();
        Notes.Add("Nozzle "+side+": skin/force/flame share canted pivot; hinge="+(sourceOrigin+ModelOffset).ToString("F4")+"; force outlet moved to BA nozzle exit; native physical parent unchanged");
        return bone;
    }

    static Mesh SaveMesh(Mesh mesh,string path)
    {
        var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(existing==null){AssetDatabase.CreateAsset(mesh,path);return mesh;}
        EditorUtility.CopySerialized(mesh,existing);Object.DestroyImmediate(mesh);
        EditorUtility.SetDirty(existing);return existing;
    }

    static Mesh NozzleGlowMesh()
    {
        const int segments=32;
        var vertices=new Vector3[(segments+1)*2];var uv=new Vector2[vertices.Length];
        var triangles=new List<int>();
        for(int i=0;i<=segments;i++)
        {
            float a=2*Mathf.PI*i/segments;
            for(int j=0;j<2;j++)
            {
                float r=j==0?.495f:.455f;int k=2*i+j;
                vertices[k]=new Vector3(Mathf.Cos(a)*r,Mathf.Sin(a)*r,0);
                uv[k]=new Vector2(.5f+Mathf.Cos(a)*r,.5f+Mathf.Sin(a)*r);
            }
            if(i==segments)continue;
            int n=i*2;
            // Both sides are visible without changing the native emissive material.
            triangles.AddRange(new[]{n,n+2,n+1,n+1,n+2,n+3,n+1,n+2,n,n+3,n+2,n+1});
        }
        var mesh=new Mesh{name="SU57_NozzleGlow",vertices=vertices,uv=uv,triangles=triangles.ToArray()};
        mesh.RecalculateNormals();mesh.RecalculateBounds();mesh.RecalculateTangents();
        return SaveMesh(mesh,Root+"/SU57_NozzleGlow.asset");
    }

    static void AlignCockpit(Transform root)
    {
        // Keep the functional Ifrit instruments, seat, view and ejection system together.
        var cockpit = root.Find("fuselage_F/cockpit");
        foreach (var name in new[]{"cockpit_int", "pilot", "canopyFrame_F", "canopyFrame_R"})
        {
            var t = cockpit.Find(name);
            if(t != null) t.localPosition += new Vector3(0,-.25f,-1.2f);
        }
    }

    static void ValidatePhysicsPartTransforms(Transform root,Transform template)
    {
        // AeroPart transforms anchor live rigidbodies, colliders and joints.
        // Moving the whole nozzle to match a skin breaks it during spawn and
        // disables its engine through Turbojet.criticalParts. Keep the native
        // physics layout; cosmetic exhaust alignment belongs to the model stage.
        var parts=template.GetComponentsInChildren<AeroPart>(true);
        foreach(var part in parts)
        {
            string path=AnimationUtility.CalculateTransformPath(part.transform,template);
            var actual=path.Length==0?root:root.Find(path);
            if(actual==null || Vector3.Distance(actual.localPosition,part.transform.localPosition)>.00001f || Quaternion.Angle(actual.localRotation,part.transform.localRotation)>.001f || Vector3.Distance(actual.localScale,part.transform.localScale)>.00001f)
                throw new Exception("Native physics part transform changed: "+path);
        }
        Notes.Add("Native AeroPart transforms preserved: "+parts.Length+" (including nose and both nozzle joints)");
    }

    static void AlignNavigationLights(Transform root)
    {
        var lensPath=Root+"/SU57_NavLens.asset";
        var lens=AssetDatabase.LoadAssetAtPath<Mesh>(lensPath);
        if(lens==null)
        {
            const float r=.045f;
            lens=new Mesh{name="SU57_NavLens"};
            lens.vertices=new[]{Vector3.right*r,Vector3.left*r,Vector3.up*r,Vector3.down*r,Vector3.forward*r,Vector3.back*r};
            lens.triangles=new[]{2,0,4,2,4,1,2,1,5,2,5,0,3,4,0,3,1,4,3,5,1,3,0,5};
            lens.RecalculateNormals();lens.RecalculateBounds();
            AssetDatabase.CreateAsset(lens,lensPath);
        }
        foreach(var side in new[]{"L","R"})
        {
            var light=root.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="navlight_"+side);
            // Place the lens on the actual BA Su-57 outer wing edge, whose source
            // vertices are y=2.04, z=-4.89 before the model's -2.2 pivot offset.
            light.position=root.TransformPoint(new Vector3(side=="L" ? -6.94f : 6.94f,-.135f,-4.89f));
            light.GetComponent<MeshFilter>().sharedMesh=lens;
            var effects=light.Find("navlight_"+side+"_effects");
            if(effects==null)throw new Exception("Navigation light effects missing: "+side);
            effects.localPosition=Vector3.zero;
            Notes.Add("Navigation light "+side+": "+root.InverseTransformPoint(light.position).ToString("F3"));
        }
    }

    static void ValidatePrefab(GameObject prefab,AircraftDefinition definition,AircraftParameters parameters)
    {
        var a=prefab.GetComponent<Aircraft>();
        if(parameters.liveries==null || parameters.liveries.Count==0 || parameters.liveries.Any(l=>l.assetReference==null))
            throw new Exception("Selectable built-in livery entries are required for LoadoutSelector.CurrentLivery");
        Notes.Add("Selectable native livery keys: "+parameters.liveries.Count);
        if(Missing(prefab)!=0 || a==null || ((Unit)a).definition!=definition || definition.unitPrefab!=prefab || definition.aircraftParameters!=parameters)
            throw new Exception("Broken aircraft registration or script links");
        var engines=prefab.GetComponentsInChildren<MonoBehaviour>(true).Where(c=>c is IEngine).ToArray();
        if(engines.Length!=2 || prefab.GetComponentsInChildren<LandingGear>(true).Length!=3 || prefab.GetComponentInChildren<WeaponManager>(true)==null || prefab.GetComponentInChildren<Cockpit>(true)==null)
            throw new Exception("Expected two engines, three landing gear, cockpit and weapon manager");
        var so=new SerializedObject(a);
        foreach(var property in new[]{"cockpitViewPoint","weaponManager","cockpit","radar","targetCam","powerSupply"})
        {
            var p=so.FindProperty(property);
            if(p==null || p.objectReferenceValue==null) throw new Exception("Missing required aircraft reference: "+property);
        }
        foreach(var gear in prefab.GetComponentsInChildren<LandingGear>(true))
        {
            so=new SerializedObject(gear);
            foreach(var property in new[]{"aircraft","gearHinge","unsprung","castPoint"})
                if(so.FindProperty(property).objectReferenceValue==null) throw new Exception("Broken gear reference: "+property);
        }
        var wm=prefab.GetComponentInChildren<WeaponManager>(true);
        so=new SerializedObject(wm);
        var sets=so.FindProperty("hardpointSets");
        if(sets==null || sets.arraySize<5) throw new Exception("Missing cannon, internal bays or wing stations");
        Notes.Add("Hardpoint sets: "+sets.arraySize);
        for(int i=0;i<sets.arraySize;i++)
        {
            var set=sets.GetArrayElementAtIndex(i);
            Notes.Add("  "+set.FindPropertyRelative("name").stringValue+": "+set.FindPropertyRelative("weaponOptions").arraySize+" options");
        }
        File.WriteAllLines(Output+"/SU57_preflight.txt",Notes);
    }
    static T SaveCopy<T>(T source,string path) where T:Object
    {
        var existing=AssetDatabase.LoadAssetAtPath<T>(path);
        if(existing!=null){EditorUtility.CopySerialized(source,existing);return existing;}
        var copy=Object.Instantiate(source);AssetDatabase.CreateAsset(copy,path);return copy;
    }
    static Material CreateGlass()
    {
        var path=Root+"/SU57_Canopy.mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(mat==null){mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(mat,path);}
        // URP needs a transparent shader variant AND matching blend/depth state.
        // Alpha alone leaves the old Opaque material blocking the cockpit.
        mat.SetTexture("_BaseMap",null);
        mat.SetColor("_BaseColor",new Color(.68f,.79f,.82f,.16f));
        mat.SetColor("_Color",mat.GetColor("_BaseColor"));
        mat.SetFloat("_Metallic",0f);mat.SetFloat("_Smoothness",.88f);
        mat.SetFloat("_Surface",1f);mat.SetFloat("_Blend",0f);
        mat.SetFloat("_AlphaClip",0f);mat.SetFloat("_AlphaToMask",0f);
        mat.SetFloat("_BlendModePreserveSpecular",1f);
        mat.SetFloat("_SrcBlend",(float)BlendMode.One);
        mat.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_SrcBlendAlpha",(float)BlendMode.One);
        mat.SetFloat("_DstBlendAlpha",(float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite",0f);mat.SetFloat("_Cull",(float)CullMode.Off);
        mat.SetFloat("_ReceiveShadows",0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.EnableKeyword("_RECEIVE_SHADOWS_OFF");
        mat.DisableKeyword("_ALPHATEST_ON");mat.DisableKeyword("_ALPHAMODULATE_ON");
        mat.SetOverrideTag("RenderType","Transparent");
        mat.renderQueue=(int)RenderQueue.Transparent;
        mat.SetShaderPassEnabled("ShadowCaster",false);
        mat.SetShaderPassEnabled("DepthOnly",false);
        Notes.Add("Canopy: transparent alpha=0.16, metallic=0, queue=3000, no depth/shadow writes; all three LODs");
        EditorUtility.SetDirty(mat);return mat;
    }

    static void RenderCanopyComparison(GameObject instance,Material glass)
    {
        var target=new Vector3(0,.6f,5.85f);
        var camera=target+new Vector3(3.5f,2.1f,4.8f);
        RenderPreview(instance,Output+"/SU57_canopy_after.png",camera,target);
        var old=new Material(glass);
        old.SetColor("_BaseColor",new Color(.13f,.18f,.2f,1f));old.SetFloat("_Metallic",.65f);
        old.SetFloat("_Surface",0);old.SetFloat("_SrcBlend",1);old.SetFloat("_DstBlend",0);
        old.SetFloat("_ZWrite",1);old.SetFloat("_Cull",2);
        old.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");old.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        old.DisableKeyword("_RECEIVE_SHADOWS_OFF");old.SetFloat("_ReceiveShadows",1);
        old.SetOverrideTag("RenderType","Opaque");old.renderQueue=(int)RenderQueue.Geometry;
        old.SetShaderPassEnabled("ShadowCaster",true);old.SetShaderPassEnabled("DepthOnly",true);
        var renderers=instance.GetComponentsInChildren<Renderer>(true).Where(r=>r.sharedMaterials.Contains(glass)).ToArray();
        try
        {
            foreach(var r in renderers)r.sharedMaterials=r.sharedMaterials.Select(m=>m==glass?old:m).ToArray();
            RenderPreview(instance,Output+"/SU57_canopy_before.png",camera,target);
        }
        finally
        {
            foreach(var r in renderers)r.sharedMaterials=r.sharedMaterials.Select(m=>m==old?glass:m).ToArray();
            Object.DestroyImmediate(old);
        }
    }
    static MeshRenderer MeshObject(Transform parent,string name,Mesh mesh,Material[] mats)
    {
        var go=new GameObject(name);go.layer=parent.gameObject.layer;go.transform.SetParent(parent,false);
        go.AddComponent<MeshFilter>().sharedMesh=mesh;
        var r=go.AddComponent<MeshRenderer>();r.sharedMaterials=mats;r.shadowCastingMode=ShadowCastingMode.On;return r;
    }
    static Mesh LoadMesh(string name)
    {
        var path=Root+"/"+Path.GetFileNameWithoutExtension(name)+".asset";
        var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(existing!=null)return existing;
        var vertices=new List<Vector3>();var normals=new List<Vector3>();var uv=new List<Vector2>();
        var submeshes=new List<List<int>>();var triangles=new List<int>();
        foreach(var line in File.ReadLines(Root+"/Source/"+name))
        {
            var a=line.Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries);if(a.Length==0)continue;
            if(a[0]=="v") vertices.Add(new Vector3(-float.Parse(a[1],CI),float.Parse(a[2],CI),float.Parse(a[3],CI)));
            else if(a[0]=="vn")normals.Add(new Vector3(-float.Parse(a[1],CI),float.Parse(a[2],CI),float.Parse(a[3],CI)));
            else if(a[0]=="vt")uv.Add(new Vector2(float.Parse(a[1],CI),float.Parse(a[2],CI)));
            else if(a[0]=="g"&&triangles.Count>0){submeshes.Add(triangles);triangles=new List<int>();}
            else if(a[0]=="f"){for(int i=1;i+2<a.Length;i++){triangles.Add(int.Parse(a[1].Split('/')[0])-1);triangles.Add(int.Parse(a[i+2].Split('/')[0])-1);triangles.Add(int.Parse(a[i+1].Split('/')[0])-1);}}
        }
        if(triangles.Count>0)submeshes.Add(triangles);
        var mesh=new Mesh{name=Path.GetFileNameWithoutExtension(name),indexFormat=IndexFormat.UInt32};
        mesh.SetVertices(vertices);if(uv.Count==vertices.Count)mesh.SetUVs(0,uv);
        mesh.subMeshCount=submeshes.Count;for(var i=0;i<submeshes.Count;i++)mesh.SetTriangles(submeshes[i],i);
        if(normals.Count==vertices.Count)mesh.SetNormals(normals);else mesh.RecalculateNormals();
        mesh.RecalculateBounds();mesh.RecalculateTangents();AssetDatabase.CreateAsset(mesh,path);
        Notes.Add(mesh.name+" vertices="+mesh.vertexCount+" bounds="+mesh.bounds.size+" submeshes="+mesh.subMeshCount);
        return mesh;
    }
    static void RenderPreview(GameObject model,string path,Vector3 cameraOffset,Vector3 target=default(Vector3))
    {
        var preview=new PreviewRenderUtility();
        var copy=Object.Instantiate(model);copy.transform.position=Vector3.zero;
        foreach(var lod in copy.GetComponentsInChildren<LODGroup>())lod.ForceLOD(0);
        preview.AddSingleGO(copy);
        preview.camera.transform.position=cameraOffset;preview.camera.transform.LookAt(target);
        preview.camera.nearClipPlane=.1f;preview.camera.farClipPlane=200;
        preview.camera.clearFlags=CameraClearFlags.SolidColor;preview.camera.backgroundColor=new Color(.075f,.085f,.105f);
        preview.cameraFieldOfView=32;
        preview.lights[0].intensity=1.4f;preview.lights[0].transform.rotation=Quaternion.Euler(35,140,0);
        preview.lights[1].intensity=.8f;preview.lights[1].transform.rotation=Quaternion.Euler(330,30,0);
        preview.ambientColor=new Color(.45f,.45f,.45f);
        preview.BeginStaticPreview(new Rect(0,0,1100,850));preview.Render(true);var image=preview.EndStaticPreview();
        File.WriteAllBytes(path,image.EncodeToPNG());Object.DestroyImmediate(image);preview.Cleanup();
    }
}

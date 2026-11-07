using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

// Fracture interiors are damage geometry, not permanent exterior trim.
// Older aircraft bundles store them in submesh one of each painted shell.
// Split those meshes without changing the source asset or any collider.
static class Su57FractureDisplay
{
    sealed class SplitMesh
    {
        internal Mesh exterior,fracture;
    }

    sealed class Link
    {
        internal AeroPart other;
        internal Vector3 initialPosition;
    }

    sealed class Section
    {
        internal string name;
        internal AeroPart owner;
        internal readonly List<GameObject> caps=new List<GameObject>();
        internal readonly List<Link> links=new List<Link>();
        bool shown;

        internal void Refresh()
        {
            if(owner==null)return;
            bool exposed=links.Count==0 && owner.IsDetached();
            foreach(var link in links)
            {
                if(link.other==null){exposed=true;break;}
                if(!owner.IsDetached() && !link.other.IsDetached())continue;
                // A whole assembly may detach with its own interfaces still
                // closed. Require actual relative separation as well.
                var current=owner.transform.InverseTransformPoint(link.other.transform.position);
                if((current-link.initialPosition).sqrMagnitude>.0004f)
                {
                    exposed=true;break;
                }
            }
            if(exposed==shown)return;
            shown=exposed;
            foreach(var cap in caps)if(cap!=null)cap.SetActive(shown);
        }
    }

    sealed class State
    {
        internal readonly Aircraft aircraft;
        readonly Dictionary<string,Section> sections=new Dictionary<string,Section>();
        internal int rendererCount,legacyCount;

        internal State(Aircraft aircraft)
        {
            this.aircraft=aircraft;
            var extraRenderers=new List<Renderer>();
            // Capture owners before complex physics unparents the AeroParts.
            foreach(var holder in aircraft.GetComponentsInChildren<Transform>(true))
            {
                const string prefix="SU57_Section_";
                if(!holder.name.StartsWith(prefix,System.StringComparison.Ordinal))continue;
                var owner=holder.GetComponentInParent<AeroPart>();
                if(owner==null)continue;
                var section=new Section{name=holder.name.Substring(prefix.Length),owner=owner};
                sections.Add(section.name,section);
                var lod=holder.GetComponent<LODGroup>();
                foreach(var filter in holder.GetComponentsInChildren<MeshFilter>(true))
                {
                    var renderer=filter.GetComponent<MeshRenderer>();
                    if(renderer==null)continue;
                    if(filter.name.StartsWith("SU57_Fracture_",System.StringComparison.Ordinal))
                    {
                        renderer.gameObject.SetActive(false);
                        section.caps.Add(renderer.gameObject);rendererCount++;
                        continue;
                    }
                    var source=filter.sharedMesh;
                    var materials=renderer.sharedMaterials;
                    if(source==null || source.subMeshCount!=2 || materials.Length<2 ||
                        materials[1]==null || !materials[1].name.StartsWith("SU57_FractureInterior",System.StringComparison.Ordinal))continue;
                    if(!source.isReadable)
                    {
                        Su57StructuralContacts.Report?.Invoke("Su-57 could not split unreadable fracture mesh: "+source.name);
                        continue;
                    }
                    var split=GetSplit(source);
                    var cap=new GameObject("SU57_Fracture_"+renderer.name);
                    cap.SetActive(false);cap.layer=renderer.gameObject.layer;
                    cap.transform.SetParent(renderer.transform,false);
                    cap.AddComponent<MeshFilter>().sharedMesh=split.fracture;
                    var capRenderer=cap.AddComponent<MeshRenderer>();
                    capRenderer.sharedMaterial=materials[1];
                    capRenderer.shadowCastingMode=renderer.shadowCastingMode;
                    capRenderer.receiveShadows=renderer.receiveShadows;
                    capRenderer.lightProbeUsage=renderer.lightProbeUsage;
                    capRenderer.reflectionProbeUsage=renderer.reflectionProbeUsage;
                    filter.sharedMesh=split.exterior;
                    renderer.sharedMaterials=new[]{materials[0]};
                    section.caps.Add(cap);extraRenderers.Add(capRenderer);
                    AddToLods(lod,renderer,capRenderer);
                    rendererCount++;legacyCount++;
                }
            }
            foreach(var section in sections.Values)
            {
                string[] neighbours;
                if(!Neighbours.TryGetValue(section.name,out neighbours))continue;
                var owners=new HashSet<AeroPart>();
                foreach(var name in neighbours)
                {
                    Section other;
                    if(!sections.TryGetValue(name,out other) || other.owner==section.owner || !owners.Add(other.owner))continue;
                    section.links.Add(new Link{other=other.owner,
                        initialPosition=section.owner.transform.InverseTransformPoint(other.owner.transform.position)});
                }
            }
            // Keep the native cockpit/exterior visibility list complete.
            if(ExteriorField!=null && extraRenderers.Count>0)
            {
                var existing=ExteriorField.GetValue(aircraft) as Renderer[];
                var combined=new List<Renderer>(existing??new Renderer[0]);
                combined.AddRange(extraRenderers);ExteriorField.SetValue(aircraft,combined.ToArray());
            }
        }

        internal bool Refresh()
        {
            bool alive=false;
            foreach(var section in sections.Values)
            {
                if(section.owner==null)continue;
                alive=true;section.Refresh();
            }
            return alive;
        }
    }

    static readonly FieldInfo ExteriorField=AccessTools.Field(typeof(Aircraft),"exteriorRenderers");
    static readonly Dictionary<Mesh,SplitMesh> Meshes=new Dictionary<Mesh,SplitMesh>();
    static readonly Dictionary<Aircraft,State> States=new Dictionary<Aircraft,State>();
    static readonly List<State> Live=new List<State>();

    static SplitMesh GetSplit(Mesh source)
    {
        SplitMesh split;
        if(Meshes.TryGetValue(source,out split))return split;
        var indices=source.GetTriangles(1);
        split=new SplitMesh{exterior=Object.Instantiate(source),fracture=Object.Instantiate(source)};
        split.exterior.name=source.name+"_Exterior";
        split.exterior.subMeshCount=1;
        split.fracture.name=source.name+"_Fracture";
        split.fracture.subMeshCount=1;
        split.fracture.SetTriangles(indices,0);
        Meshes.Add(source,split);return split;
    }

    static void AddToLods(LODGroup group,Renderer exterior,Renderer fracture)
    {
        if(group==null)return;
        var lods=group.GetLODs();
        for(int i=0;i<lods.Length;i++)
        {
            var renderers=new List<Renderer>(lods[i].renderers);
            if(!renderers.Contains(exterior))continue;
            renderers.Add(fracture);lods[i].renderers=renderers.ToArray();
        }
        group.SetLODs(lods);
    }

    internal static void Install(Aircraft aircraft)
    {
        if(!Su57MassProperties.IsSu57(aircraft) || States.ContainsKey(aircraft))return;
        try
        {
            var state=new State(aircraft);
            if(state.rendererCount==0)return;
            States.Add(aircraft,state);Live.Add(state);state.Refresh();
            Su57StructuralContacts.Report?.Invoke("Su-57 fracture display prepared: "+state.rendererCount+
                " independent LOD renderers ("+state.legacyCount+" split from older resource); closed interfaces hidden");
        }
        catch(System.Exception error)
        {
            // A visual compatibility failure must not abort native spawning.
            Su57StructuralContacts.Report?.Invoke("Su-57 fracture display setup failed: "+error);
        }
    }

    internal static void Tick()
    {
        for(int i=Live.Count-1;i>=0;i--)
        {
            var state=Live[i];
            if(state.aircraft==null)States.Remove(state.aircraft);
            // Detached wreckage can outlive the aircraft's root object.
            if(!state.Refresh())Live.RemoveAt(i);
        }
    }

    internal static void Remove(Aircraft aircraft)
    {
        if(object.ReferenceEquals(aircraft,null))return;
        States.Remove(aircraft);
    }

    internal static void Clear()
    {
        States.Clear();Live.Clear();
        foreach(var split in Meshes.Values)
        {
            if(split.exterior!=null)Object.Destroy(split.exterior);
            if(split.fracture!=null)Object.Destroy(split.fracture);
        }
        Meshes.Clear();
    }

    // Authored from shared source surface points, not the native part tree:
    // fore/aft fuselage sections are siblings in that tree. See the matching
    // section-adjacency.json source record under Reference/SU57/DamageModel.
    static readonly Dictionary<string,string[]> Neighbours=new Dictionary<string,string[]>
    {
        {"CanopyGlass",new[]{"FuselageForward"}},
        {"Center",new[]{"FuselageForward","FuselageRear","Gearbay_L","Gearbay_R","Intake_L","Intake_R","WingRoot_L","WingRoot_R"}},
        {"Cockpit",new[]{"FuselageForward","WingRoot_L","WingRoot_R"}},
        {"Engine_L",new[]{"FuselageRear","Gearbay_L","Intake_L","Nozzle_L","Tail","WingInner_L"}},
        {"Engine_R",new[]{"FuselageRear","Gearbay_R","Intake_R","Nozzle_R","Tail","WingInner_R"}},
        {"FuselageForward",new[]{"CanopyGlass","Center","Cockpit","Nose","WingRoot_L","WingRoot_R"}},
        {"FuselageRear",new[]{"Center","Engine_L","Engine_R","Gearbay_L","Gearbay_R","Intake_L","Intake_R","Tail"}},
        {"Gearbay_L",new[]{"Center","Engine_L","FuselageRear","Intake_L","WingInner_L","WingRoot_L"}},
        {"Gearbay_R",new[]{"Center","Engine_R","FuselageRear","Intake_R","WingInner_R","WingRoot_R"}},
        {"Intake_L",new[]{"Center","Engine_L","FuselageRear","Gearbay_L","WingInner_L","WingRoot_L"}},
        {"Intake_R",new[]{"Center","Engine_R","FuselageRear","Gearbay_R","WingInner_R","WingRoot_R"}},
        {"LeadingInner_L",new[]{"LeadingOuter_L"}},
        {"LeadingInner_R",new[]{"LeadingOuter_R"}},
        {"LeadingOuter_L",new[]{"LeadingInner_L"}},
        {"LeadingOuter_R",new[]{"LeadingInner_R"}},
        {"Nose",new[]{"FuselageForward"}},
        {"Nozzle_L",new[]{"Engine_L"}},
        {"Nozzle_R",new[]{"Engine_R"}},
        {"Tail",new[]{"Engine_L","Engine_R","FuselageRear"}},
        {"WingInner_L",new[]{"Engine_L","Gearbay_L","Intake_L","WingOuter_L","WingRoot_L"}},
        {"WingInner_R",new[]{"Engine_R","Gearbay_R","Intake_R","WingOuter_R","WingRoot_R"}},
        {"WingOuter_L",new[]{"WingInner_L","Wingtip_L"}},
        {"WingOuter_R",new[]{"WingInner_R","Wingtip_R"}},
        {"WingRoot_L",new[]{"Center","Cockpit","FuselageForward","Gearbay_L","Intake_L","WingInner_L"}},
        {"WingRoot_R",new[]{"Center","Cockpit","FuselageForward","Gearbay_R","Intake_R","WingInner_R"}},
        {"Wingtip_L",new[]{"WingOuter_L"}},
        {"Wingtip_R",new[]{"WingOuter_R"}},
    };
}

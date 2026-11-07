using BepInEx;
using HarmonyLib;
using UnityEngine;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

// The editor embeds the aircraft .nobp into this plugin assembly. Blueprinter
// discovers its manifest resource directly; no loose companion is installed.
// No custom component is serialized into the aircraft prefab.
[BepInPlugin(PluginId, "Su-57", "0.2.34")]
[BepInDependency("com.nikkorap.blueprinter")]
public sealed class Su57GearMotion : BaseUnityPlugin
{
    public const string PluginId = "local.su57.gearmotion";
    Harmony patches;

    void Awake()
    {
        Su57StructuralContacts.Report = message => Logger.LogWarning(message);
        try
        {
            Su57MassProperties.Load();
            Logger.LogInfo("Su-57 mass references loaded: "+Su57MassProperties.Count+" (34 structural + merged)");
        }
        catch(System.Exception error)
        {
            // Mass data is optional to installing contact/animation patches.
            // Never leave the aircraft registered with all safeguards absent.
            Su57MassProperties.Clear();
            Logger.LogError("Su-57 mass reference unavailable; using native automatic mass properties. Contact/animation patches will still be installed. "+error);
        }
        patches = new Harmony(PluginId);
        patches.PatchAll(typeof(Su57GearMotion).Assembly);
        Logger.LogInfo("Su-57 0.2.34: contact/animation patches installed; fracture faces hidden at intact interfaces, shelter spawn z=0.23m and mass references; single embedded aircraft DLL");
    }

    void FixedUpdate()
    {
        Su57StructuralContacts.Tick();
        Su57FractureDisplay.Tick();
    }

    void OnDestroy()
    {
        Su57FractureDisplay.Clear();
        Su57StructuralContacts.Clear();
        patches?.UnpatchSelf();
    }
}

// Also repair definitions from already-built embedded aircraft resources.
// Apply before Hangar computes the server position and before Aircraft uses
// the definition again to place a newly spawned aircraft on the client.
static class Su57ShelterSpawn
{
    internal static void Configure(AircraftDefinition definition)
    {
        if(definition==null || definition.jsonKey!="SU57")return;
        var offset=definition.spawnOffset;
        if(Mathf.Abs(offset.z-.23f)<.0001f)return;
        float previous=offset.z;
        offset.z=.23f;
        definition.spawnOffset=offset;
        Su57StructuralContacts.Report?.Invoke("Su-57 shelter spawn offset corrected: z="+
            previous.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+
            " -> 0.230m; centered between shelter doors");
    }
}

[HarmonyPatch(typeof(Hangar), "SpawnAircraft")]
static class Su57HangarSpawn
{
    [HarmonyPrefix] static void Before(AircraftDefinition definition)
    {
        Su57ShelterSpawn.Configure(definition);
    }
}

[HarmonyPatch(typeof(Aircraft), "OnStartClient")]
static class Su57ClientSpawn
{
    [HarmonyPrefix] static void Before(Aircraft __instance)
    {
        Su57ShelterSpawn.Configure(__instance.definition);
        Su57FractureDisplay.Install(__instance);
    }
}

// Complex physics unparents the AeroParts: enumerate the native registry,
// never only the aircraft transform hierarchy. Filter structural colliders
// and their compound airframe colliders. World/other-aircraft contacts remain.
static class Su57StructuralContacts
{
    sealed class Pair
    {
        internal AeroPart firstPart,secondPart;
        internal Collider first,second;
        internal bool originalIgnore,active;
        internal Rigidbody firstBody,secondBody;
    }

    sealed class ColliderStatus
    {
        internal bool ready,owned;
        internal Rigidbody body;
    }

    sealed class State
    {
        readonly List<AeroPart> parts=new List<AeroPart>();
        readonly HashSet<AeroPart> known=new HashSet<AeroPart>();
        readonly List<Pair> pairs=new List<Pair>();
        readonly List<Collider> colliders=new List<Collider>();
        readonly Dictionary<Collider,AeroPart> owners=new Dictionary<Collider,AeroPart>();
        readonly Dictionary<Collider,ColliderStatus> status=new Dictionary<Collider,ColliderStatus>();
        readonly Aircraft aircraft;
        readonly float createdAt=Time.time;
        int diagnosticCount;
        bool setupReported;

        internal State(Aircraft aircraft)
        {
            this.aircraft=aircraft;
            Synchronize(aircraft);
            Live.Add(this);
        }

        internal void Synchronize(Aircraft aircraft)
        {
            foreach(var item in aircraft.partLookup)
            {
                var part=item as AeroPart;
                if(part==null)continue;
                var a=part.GetComponent<MeshCollider>();
                // Restrict this to the 34 authored aircraft shells, including
                // when a mount registers an additional AeroPart at runtime.
                if(a==null || a.sharedMesh==null ||
                    !a.sharedMesh.name.StartsWith("SU57_",System.StringComparison.Ordinal))continue;
                if(known.Add(part))
                {
                    part.onPartDetached+=OnDetached;
                    part.onParentDetached+=OnDetached;
                    part.onJointBroken+=OnJointBroken;
                    parts.Add(part);
                }
                // Claim only compound airframe colliders. Nested structural
                // parts claim their own; launched weapons remain independent.
                foreach(var collider in part.GetComponentsInChildren<Collider>(true))
                {
                    if(collider.isTrigger || owners.ContainsKey(collider) ||
                        collider.GetComponentInParent<AeroPart>()!=part ||
                        collider.GetComponentInParent<Weapon>()!=null)continue;
                    foreach(var previous in colliders)
                    {
                        var previousPart=owners[previous];
                        if(previous==null || previousPart==part)continue;
                        pairs.Add(new Pair{firstPart=part,secondPart=previousPart,first=collider,second=previous,
                            originalIgnore=Physics.GetIgnoreCollision(collider,previous)});
                    }
                    owners.Add(collider,part);colliders.Add(collider);
                    status.Add(collider,new ColliderStatus());
                }
            }
        }

        static bool Ready(Collider collider)
        {
            return collider!=null && collider.enabled && collider.gameObject.activeInHierarchy;
        }

        internal void Apply(bool force)
        {
            // Query Unity once per collider, not twice for every pair on each
            // physics step. Pair decisions below use only managed snapshots.
            foreach(var collider in colliders)
            {
                var snapshot=status[collider];
                snapshot.ready=Ready(collider);
                if(collider==null){snapshot.owned=false;continue;}
                var owner=owners[collider];
                snapshot.body=collider.attachedRigidbody;
                snapshot.owned=owner!=null && collider.GetComponentInParent<AeroPart>()==owner &&
                    snapshot.body==owner.rb;
            }
            foreach(var pair in pairs)
            {
                if(pair.firstPart==null || pair.secondPart==null || pair.first==null || pair.second==null)continue;
                var first=status[pair.first];var second=status[pair.second];
                bool attached=!pair.firstPart.IsDetached() && !pair.secondPart.IsDetached() &&
                    first.owned && second.owned;
                if(attached)
                {
                    if(!first.ready || !second.ready)
                    {
                        pair.active=false;continue;
                    }
                    var firstBody=first.body;
                    var secondBody=second.body;
                    if(!force && pair.active && pair.firstBody==firstBody && pair.secondBody==secondBody)continue;
                    // Reassert after rigidbodies are created/merged. Unity can
                    // discard ignore state when collider ownership changes.
                    Physics.IgnoreCollision(pair.first,pair.second,true);
                    pair.active=true;
                    pair.firstBody=firstBody;pair.secondBody=secondBody;
                }
                else Restore(pair);
            }
        }

        static void Restore(Pair pair)
        {
            if(!pair.active)return;
            if(Ready(pair.first) && Ready(pair.second))
                Physics.IgnoreCollision(pair.first,pair.second,pair.originalIgnore);
            pair.active=false;
        }

        void OnDetached(UnitPart part)
        {
            foreach(var pair in pairs)
                if(pair.firstPart==part || pair.secondPart==part)Restore(pair);
        }

        void OnJointBroken(UnitPart part)
        {
            Diagnostic("joint broke: part="+part.name+", hp="+part.hitPoints.ToString("F1")+
                ", velocity="+(part.rb==null?"none":part.rb.velocity.ToString("F2")));
        }

        internal void Diagnostic(string message)
        {
            // Bounded evidence for the next user-controlled manual spawn.
            if(Time.time-createdAt>10f || diagnosticCount>=8)return;
            diagnosticCount++;
            Report?.Invoke("SU57 spawn ["+aircraft.GetInstanceID()+", +"+
                (Time.time-createdAt).ToString("F3")+"s] "+message);
        }

        internal void Describe()
        {
            if(setupReported)return;
            setupReported=true;
            var bodies=new HashSet<Rigidbody>();
            foreach(var part in parts)if(part!=null && part.rb!=null)bodies.Add(part.rb);
            int filtered=0;foreach(var pair in pairs)if(pair.active)filtered++;
            Diagnostic("contact setup: structural parts="+parts.Count+", colliders="+colliders.Count+
                ", bodies="+bodies.Count+", filtered pairs="+filtered);
        }

        internal void Dispose()
        {
            foreach(var pair in pairs)Restore(pair);
            foreach(var part in parts)
            {
                if(part==null)continue;
                part.onPartDetached-=OnDetached;
                part.onParentDetached-=OnDetached;
                part.onJointBroken-=OnJointBroken;
            }
        }
    }

    static readonly ConditionalWeakTable<Aircraft,State> States=new ConditionalWeakTable<Aircraft,State>();
    static readonly List<State> Live=new List<State>();
    internal static System.Action<string> Report;

    internal static void Tick()
    {
        foreach(var state in Live)state.Apply(false);
    }

    internal static void Clear()
    {
        foreach(var state in Live)state.Dispose();
        Live.Clear();Report=null;
    }

    internal static void Diagnostic(Aircraft aircraft,string message)
    {
        State state;
        if(aircraft!=null && States.TryGetValue(aircraft,out state))state.Diagnostic(message);
    }

    internal static void Describe(Aircraft aircraft)
    {
        State state;
        if(aircraft!=null && States.TryGetValue(aircraft,out state))state.Describe();
    }

    internal static void Apply(Aircraft aircraft)
    {
        if(aircraft==null || aircraft.definition==null || aircraft.definition.jsonKey!="SU57" ||
            aircraft.partLookup==null || aircraft.partLookup.Count==0)return;
        var state=States.GetValue(aircraft,a=>new State(a));
        // Child Awake registration can finish after Aircraft.Awake. Admit
        // newly registered shells before each native physics transition.
        state.Synchronize(aircraft);state.Apply(true);
    }

    internal static void Remove(Aircraft aircraft)
    {
        State state;
        if(aircraft!=null && States.TryGetValue(aircraft,out state))
        {
            state.Dispose();Live.Remove(state);States.Remove(aircraft);
        }
    }
}

[HarmonyPatch(typeof(Aircraft), "SetComplexPhysics")]
static class Su57ComplexPhysicsContacts
{
    [HarmonyPrefix] static void Before(Aircraft __instance){Su57StructuralContacts.Apply(__instance);}
    [HarmonyPostfix] static void After(Aircraft __instance)
    {
        Su57MassProperties.Restore(__instance,true);
        Su57StructuralContacts.Apply(__instance);
        Su57StructuralContacts.Describe(__instance);
    }
}

[HarmonyPatch(typeof(Aircraft), "SetSimplePhysics")]
static class Su57SimplePhysicsContacts
{
    [HarmonyPostfix] static void After(Aircraft __instance)
    {
        Su57MassProperties.Restore(__instance,false);
        Su57StructuralContacts.Apply(__instance);
    }
}

[HarmonyPatch(typeof(Aircraft), "Awake")]
static class Su57InitialContacts
{
    [HarmonyPostfix] static void After(Aircraft __instance)
    {
        Su57StructuralContacts.Apply(__instance);
        Su57FractureDisplay.Install(__instance);
    }
}

[HarmonyPatch(typeof(AeroPart), "Repair")]
static class Su57RepairedContacts
{
    [HarmonyPostfix] static void After(AeroPart __instance)
    {
        Su57MassProperties.MassChanged(__instance);
        Su57StructuralContacts.Apply(__instance.GetUnit() as Aircraft);
        if(__instance.IsDetached())return;
        foreach(var door in __instance.GetComponentsInChildren<BayDoor>(true))
            if(door.name.StartsWith("SU57_BayDoor_",System.StringComparison.Ordinal))door.enabled=true;
    }
}

[HarmonyPatch(typeof(Aircraft), "OnDestroy")]
static class Su57DestroyContacts
{
    [HarmonyPrefix] static void Before(Aircraft __instance)
    {
        Su57StructuralContacts.Remove(__instance);
        Su57FractureDisplay.Remove(__instance);
    }
}

[HarmonyPatch(typeof(AeroPart), "OnCollisionEnter")]
static class Su57AttachedImpact
{
    [HarmonyPrefix]
    static bool Before(AeroPart __instance,Collision collision)
    {
        var aircraft=__instance.GetUnit() as Aircraft;
        if(!Su57MassProperties.IsSu57(aircraft) || collision==null)return true;
        var other=collision.collider==null?null:collision.collider.GetComponentInParent<AeroPart>();
        bool own=other!=null && other.GetUnit()==aircraft &&
            !other.IsDetached() && !__instance.IsDetached();
        if(own)
        {
            // Queued callbacks can survive actor replacement. Detached debris
            // and external impacts continue through native damage processing.
            // Collision exposes only the incoming collider in this Unity
            // version. ContactPoint carries both shapes, including compounds.
            for(int i=0;i<collision.contactCount;i++)
            {
                var contact=collision.GetContact(i);
                if(contact.thisCollider!=null && contact.otherCollider!=null)
                    Physics.IgnoreCollision(contact.thisCollider,contact.otherCollider,true);
            }
            Su57StructuralContacts.Diagnostic(aircraft,"blocked attached self-impact: "+
                __instance.name+" <-> "+other.name+", impulse="+collision.impulse.magnitude.ToString("F1"));
            return false;
        }
        if(collision.impulse.sqrMagnitude>1000000f)
            Su57StructuralContacts.Diagnostic(aircraft,"external impact: part="+__instance.name+
                ", other="+(collision.collider==null?"none":collision.collider.name)+
                ", impulse="+collision.impulse.magnitude.ToString("F1")+
                ", point="+(collision.contactCount==0?"none":collision.GetContact(0).point.ToString("F2")));
        return true;
    }
}

[HarmonyPatch(typeof(LiveryBehaviour), "SetLivery")]
static class Su57SelectableLivery
{
    static readonly System.Reflection.FieldInfo ExteriorField =
        AccessTools.Field(typeof(Aircraft), "exteriorRenderers");

    [HarmonyPostfix]
    static void AfterLiveryLoaded(LiveryData livery, Aircraft ___aircraft, MaterialCleanup ___materialCleanup)
    {
        if (___aircraft == null || ((Unit)___aircraft).definition == null ||
            ((Unit)___aircraft).definition.jsonKey != "SU57" || livery == null || livery.Texture == null ||
            !livery.name.StartsWith("SU57_Livery", System.StringComparison.Ordinal) ||
            ExteriorField == null || ___materialCleanup == null) return;
        var renderers = ExteriorField.GetValue(___aircraft) as Renderer[];
        if (renderers == null) return;

        // The builder wires EVERY Su-57 paint renderer to its native
        // UnitPart.damageMaterial list, with paint in slot zero. SetLivery has
        // already created and registered the per-renderer material instances.
        // Update those instances in place, keeping independent damage state
        // and the native cleanup lifecycle when a structural part detaches.
        var updated = new System.Collections.Generic.HashSet<Material>();
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null) continue;
            var part = renderer.GetComponentInParent<AeroPart>();
            if (part != null && part.IsDetached()) continue;
            foreach (Material material in renderer.sharedMaterials)
            {
                if (!IsSkin(material) || !updated.Add(material)) continue;
                if (material.HasProperty("_Livery"))
                {
                    material.SetTexture("_Livery", livery.Texture);
                    material.SetFloat("_Glossiness", livery.Glossiness);
                    if (material.HasProperty("_BasecolorDmg"))
                    {
                        // Reassert against the restored game shader; the
                        // editor placeholder has no keyword shader variants.
                        material.EnableKeyword("_ALPHATEST_ON");
                        material.EnableKeyword("_NORMALMAP");
                    }
                }
                else
                {
                    // Existing older URP materials remain usable on reload.
                    if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", livery.Texture);
                    if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", livery.Texture);
                    if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 1f);
                }
            }
        }
    }

    static bool IsSkin(Material material)
    {
        return material != null && material.name.StartsWith("SU57_Camouflage", System.StringComparison.Ordinal);
    }
}

[HarmonyPatch(typeof(MountedMissile), "AttachToHardpoint")]
static class Su57BayLaunch
{
    internal static bool IsMain(Hardpoint hardpoint)
    {
        return hardpoint!=null && hardpoint.transform!=null &&
            hardpoint.transform.name.StartsWith("SU57_Hardpoint_Main",System.StringComparison.Ordinal);
    }

    internal static bool IsSide(Hardpoint hardpoint)
    {
        return hardpoint!=null && hardpoint.transform!=null &&
            hardpoint.transform.name.StartsWith("SU57_Hardpoint_Side_",System.StringComparison.Ordinal);
    }

    [HarmonyPostfix]
    static void AfterAttach(Aircraft aircraft, Hardpoint hardpoint,
        ref float ___railDelay, ref float ___railLength, ref float ___railSpeed,
        ref MountedMissile.RailDirection ___railDirection)
    {
        if (aircraft == null || ((Unit)aircraft).definition == null ||
            ((Unit)aircraft).definition.jsonKey != "SU57" ||
            (!IsMain(hardpoint) && !IsSide(hardpoint))) return;
        // Instance fields only; never modify shared vanilla WeaponMount assets.
        // Main/side doors reach fully open in ~.167/.143 s. Keep at least
        // .30 s before downward extraction, then native flight takes over.
        ___railDelay = Mathf.Max(___railDelay, .30f);
        ___railLength = Mathf.Max(___railLength, IsSide(hardpoint) ? .65f : .90f);
        ___railSpeed = Mathf.Max(___railSpeed, 8f);
        ___railDirection = MountedMissile.RailDirection.Down;
    }
}

[HarmonyPatch(typeof(MountedMissile), "Fire")]
static class Su57BayDoorOpen
{
    [HarmonyPrefix]
    static void BeforeFire(Unit owner, Hardpoint ___hardpoint, bool ___fired)
    {
        if (owner == null || owner.definition == null || owner.definition.jsonKey != "SU57" ||
            ___fired || (!Su57BayLaunch.IsMain(___hardpoint) && !Su57BayLaunch.IsSide(___hardpoint)) ||
            ___hardpoint.part == null || ___hardpoint.part.IsDetached()) return;
        // Some vanilla weapon prefabs have their own folding-door list;
        // native Fire otherwise skips the aircraft's hardpoint doors for those.
        ___hardpoint.SpringOpenBayDoors();
    }
}

[HarmonyPatch(typeof(BayDoor), "Update")]
static class Su57DetachedBayDoor
{
    [HarmonyPrefix]
    static bool BeforeUpdate(BayDoor __instance)
    {
        if(__instance==null || !__instance.name.StartsWith("SU57_BayDoor_",System.StringComparison.Ordinal))return true;
        var part=__instance.GetComponentInParent<AeroPart>();
        if(part==null || !part.IsDetached())return true;
        // A detached pod/airframe section keeps its last mechanical pose.
        // No custom serialized component or per-frame standalone driver needed.
        __instance.enabled=false;return false;
    }
}

[HarmonyPatch(typeof(LandingGear), "UpdateMovingParts")]
static class Su57NoseBraceReach
{
    const float SectionReach = .95f;

    [HarmonyPrefix]
    static void BeforeMovingParts(Transform ___gearHinge, GearPart[] ___movingParts)
    {
        if (___gearHinge == null || ___gearHinge.name != "SU57_NoseFold_gearHinge_F" ||
            ___movingParts == null) return;
        // The nose uses native motion throughout. Fit only its two visible
        // brace sections to the instantaneous pin span, including locked poses.
        foreach (GearPart part in ___movingParts)
        {
            if (part == null || part.transform == null || part.target == null) continue;
            string name = part.transform.name;
            if (name != "gear_arm_F" && name != "SU57_NoseBracePiston") continue;
            float span = Vector3.Distance(part.transform.position, part.target.position);
            Vector3 scale = part.transform.localScale;
            scale.z = Mathf.Min(1f, .65f * span / SectionReach);
            part.transform.localScale = scale;
        }
    }
}

[HarmonyPatch(typeof(LandingGear), "UpdateMovingParts")]
static class Su57MainGearSequence
{
    // The prefab gives the native coroutine a 4.6 s stroke on all three legs.
    // Lift to the full angle with 0.15 m aircraft-lateral inward travel over
    // 2.8 s, then tuck the remainder over 1.8 s. Keep the native stroke clock;
    // never replace it with the visual lift progress (physics also reads it).
    // Extension reverses this path, including the last 0.15 m while lowering.
    const float LiftSeconds = 2.8f;
    const float LiftInwardMeters = .15f;
    const float BraceReach = .74f;

    [HarmonyPrefix]
    static void BeforeMovingParts(Transform ___gearHinge, Vector3 ___hingeBasePos,
        Vector3 ___hingeBaseAngles, Vector3 ___hingeFoldMotion,
        float ___foldAmount, float ___foldDegrees, float ___foldSpeed,
        GearPart[] ___movingParts, Aircraft ___aircraft)
    {
        if (___gearHinge == null) return;
        string name = ___gearHinge.name;
        if (name != "SU57_StagedMainFold_gearMount_L" &&
            name != "SU57_StagedMainFold_gearMount_R") return;
        if (___aircraft == null || ___gearHinge.parent == null) return;

        float duration = Mathf.Abs(___foldDegrees) / Mathf.Max(Mathf.Abs(___foldSpeed), .001f);
        float liftEnd = Mathf.Clamp(LiftSeconds / duration, .001f, .999f);
        float progress = Mathf.Clamp01(___foldAmount);
        float lift = Mathf.Clamp01(progress / liftEnd);
        float liftInward = lift * lift * (3f - 2f * lift);
        float inward = Mathf.Clamp01((progress - liftEnd) / (1f - liftEnd));
        inward = inward * inward * (3f - 2f * inward);
        float inwardSign = name == "SU57_StagedMainFold_gearMount_L" ? 1f : -1f;
        // Encode exactly 15 cm along aircraft X into the hinge PARENT space.
        // The first stage must not include the total motion's Y/Z correction.
        Vector3 firstStage = ___gearHinge.parent.InverseTransformVector(
            ___aircraft.transform.TransformVector(new Vector3(inwardSign * LiftInwardMeters, 0f, 0f)));
        ___gearHinge.localEulerAngles = ___hingeBaseAngles + new Vector3(___foldDegrees * lift, 0f, 0f);
        // Subtract the first-stage offset before adding stage two, so the
        // accepted final pocket position does not move another 15 cm inward.
        ___gearHinge.localPosition = ___hingeBasePos + firstStage * liftInward
            + (___hingeFoldMotion - firstStage) * inward;

        // Apply before the native LookAt calls, including locked end states.
        // The delayed tuck brings the pins closer mid-stroke than the old
        // simultaneous path. Shorten both overlapping sections as needed so
        // neither reaches past the opposite pin. Preserve their thickness.
        if (___movingParts == null) return;
        foreach (GearPart part in ___movingParts)
        {
            if (part == null || part.transform == null || part.target == null) continue;
            string partName = part.transform.name;
            if (partName != "gear_L_arm" && partName != "gear_R_arm" &&
                partName != "SU57_MainBracePiston_L" && partName != "SU57_MainBracePiston_R") continue;
            float span = Vector3.Distance(part.transform.position, part.target.position);
            Vector3 scale = part.transform.localScale;
            scale.z = Mathf.Min(1f, .65f * span / BraceReach);
            part.transform.localScale = scale;
        }
    }
}

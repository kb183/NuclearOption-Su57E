using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BepInEx;
using UnityEngine;
using UnityEngine.AddressableAssets;
using NuclearOption.Networking;
using NuclearOption.SavedMission;

[BepInPlugin("local.su57.runtimeprobe", "Su-57 local validation", "0.2.3")]
[BepInDependency("com.nikkorap.blueprinter")]
public class Su57RuntimeProbe : BaseUnityPlugin
{
    static readonly bool FlightTest=Environment.GetCommandLineArgs().Contains("-su57-flight-test");
    static readonly bool CanopyTest=Environment.GetCommandLineArgs().Contains("-su57-canopy-test");
    static readonly string FlightRun=Environment.GetCommandLineArgs().FirstOrDefault(s=>s.StartsWith("-su57-flight-run="))?.Substring("-su57-flight-run=".Length) ?? "01";
    static readonly string OutputRoot=Path.Combine(Environment.GetEnvironmentVariable("SU57_PROJECT_ROOT") ?? BepInEx.Paths.GameRootPath,"Builds","SU57");
    static readonly string FlightOutput=Path.Combine(OutputRoot,"FlightPhase2","run-"+FlightRun);
    StreamWriter telemetry;
    Aircraft observedAircraft;
    string phase="spawn";
    float flightStart,nextSample;
    float minimumFuel=float.PositiveInfinity;
    readonly List<string> flightErrors=new List<string>();
    void Update()
    {
        if(telemetry==null || observedAircraft==null || Time.realtimeSinceStartup<nextSample)return;
        nextSample=Time.realtimeSinceStartup+.5f;
        var a=observedAircraft;var v=a.rb.velocity;var inputs=a.GetInputs();
        minimumFuel=Mathf.Min(minimumFuel,a.GetFuelQuantity());
        telemetry.WriteLine(string.Join(",",new[]{
            (Time.realtimeSinceStartup-flightStart).ToString("F2",System.Globalization.CultureInfo.InvariantCulture),phase,
            Time.timeSinceLevelLoad.ToString("F2",System.Globalization.CultureInfo.InvariantCulture),
            a.transform.GlobalPosition().y.ToString("F2",System.Globalization.CultureInfo.InvariantCulture),
            a.radarAlt.ToString("F2",System.Globalization.CultureInfo.InvariantCulture),
            a.speed.ToString("F2",System.Globalization.CultureInfo.InvariantCulture),
            v.y.ToString("F2",System.Globalization.CultureInfo.InvariantCulture),
            a.transform.eulerAngles.y.ToString("F2",System.Globalization.CultureInfo.InvariantCulture),
            inputs.throttle.ToString("F3",System.Globalization.CultureInfo.InvariantCulture),
            inputs.pitch.ToString("F3",System.Globalization.CultureInfo.InvariantCulture),
            inputs.roll.ToString("F3",System.Globalization.CultureInfo.InvariantCulture),
            inputs.yaw.ToString("F3",System.Globalization.CultureInfo.InvariantCulture),
            inputs.brake.ToString("F3",System.Globalization.CultureInfo.InvariantCulture),
            a.gearState.ToString(),(a.pilots[0].GetCurrentState()??"<transition>").Replace(",",";"),
            a.GetFuelQuantity().ToString("F1",System.Globalization.CultureInfo.InvariantCulture),
            a.disabled.ToString(),a.HasEjected().ToString(),
            string.Join(";",a.engineStates.Select(e=>e.GetThrust().ToString("F0",System.Globalization.CultureInfo.InvariantCulture))),
            string.Join(";",a.partLookup.Select(p=>Convert.ToSingle(Get(p,"hitPoints")).ToString("F1",System.Globalization.CultureInfo.InvariantCulture)))
        }));
        telemetry.Flush();
    }
    async void Start()
    {
        // The temporary plugin only drives an isolated test when explicitly launched.
        if(!Environment.GetCommandLineArgs().Contains("-su57-test") && !FlightTest) return;
        if(FlightTest)Directory.CreateDirectory(FlightOutput);
        var deadline=Time.realtimeSinceStartup+90;
        AircraftDefinition definition=null;
        while(Time.realtimeSinceStartup<deadline)
        {
            definition=Resources.FindObjectsOfTypeAll<AircraftDefinition>().FirstOrDefault(d=>d.jsonKey=="SU57");
            if(definition!=null && definition.unitPrefab!=null) break;
            await Task.Delay(1000);
        }
        // Allow the loader to finish patches and ops before checking runtime references.
        await Task.Delay(8000);
        var lines=new List<string>();
        int exitCode=0;
        try
        {
            Require(definition!=null,"SU57 definition not registered");
            var prefab=definition.unitPrefab;
            Require(prefab!=null && prefab.name=="SU57","Definition has wrong prefab");
            Require(Resources.FindObjectsOfTypeAll<AircraftDefinition>().Count(d=>d.jsonKey=="SU57")==1,"Duplicate SU57 definitions");
            var a=prefab.GetComponent<Aircraft>();
            Require(a!=null && ((Unit)a).definition==definition,"Prefab definition link");
            var engines=prefab.GetComponentsInChildren<MonoBehaviour>(true).Count(c=>c is IEngine);
            Require(engines==2,"Expected twin engines");
            Require(prefab.GetComponentsInChildren<LandingGear>(true).Length==3,"Landing gear count");
            var body=prefab.transform.Find("SU57_Model");
            Require(body!=null,"Missing Su-57 geometry");
            var mesh=body.Find("SU57_Body_LOD0").GetComponent<MeshFilter>().sharedMesh;
            Require(mesh!=null && mesh.vertexCount==11932 && mesh.bounds.size.z>19 && mesh.bounds.size.x>13,"Incorrect aircraft mesh");
            foreach(var property in new[]{"cockpitViewPoint","weaponManager","cockpit","radar","targetCam","powerSupply"})
                Require(Get(a,property)!=null,"Unresolved runtime reference: "+property);
            var cockpit=prefab.GetComponentInChildren<Cockpit>(true);
            Require(cockpit!=null && Get(cockpit,"tacScreenUIPrefab")!=null,"Cockpit screen game asset unresolved");
            var mats=body.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).ToArray();
            Require(mats.All(m=>m!=null && m.shader!=null && (SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null || m.shader.isSupported)),"Invalid material or shader");
            if(CanopyTest)ValidateCanopy(prefab,lines,true);
            Require(prefab.GetComponentsInChildren<MonoBehaviour>(true).All(c=>c!=null),"Missing runtime scripts");
            Require(!prefab.GetComponentsInChildren<MonoBehaviour>(true).Any(c=>c.GetType().Assembly.GetName().Name.StartsWith("Aryx")),"Unexpected Aryx dependency");
            var shelter=Encyclopedia.Lookup["shelter1"];
            Require(shelter!=null && shelter.unitPrefab!=null,"Shelter template unavailable");
            ValidateHangarHook(shelter,definition);
            await ValidateLivery(definition,lines);
            ValidateLights(prefab,lines);
            lines.Add("PASS: Blueprinter 2.1.1 loaded and patched Su-57 in Nuclear Option 0.34.6");
            lines.Add("Definition SU57 -> prefab SU57 -> definition SU57");
            lines.Add("Correct aircraft mesh: "+mesh.name+", vertices="+mesh.vertexCount+", dimensions="+mesh.bounds.size);
            lines.Add("Engines="+engines+"; landing gear=3; cockpit/radar/target camera/power supply=resolved");
            lines.Add("Cockpit UI prefab="+((UnityEngine.Object)Get(cockpit,"tacScreenUIPrefab")).name);
            lines.Add("No missing scripts or custom Aryx component dependencies");
            lines.Add("Hangar op: actual Blueprinter Airbase.AddHangar registration hook adds SU57 to an inactive shelter1 clone");
            await ValidateSortie(definition,lines);
            Logger.LogInfo("SU57_RUNTIME_VALIDATION_PASS");
        }
        catch(Exception e) { exitCode=1; lines.Add("FAIL: "+e); Logger.LogError(e); }
        if(telemetry!=null){telemetry.Dispose();telemetry=null;}
        File.WriteAllLines(FlightTest?FlightOutput+"/flight-validation.txt":Path.Combine(OutputRoot,"runtime-validation.txt"),lines);
        Application.Quit(exitCode);
    }
    async Task ValidateLivery(AircraftDefinition definition,List<string> lines)
    {
        var p=definition.aircraftParameters;
        Require(p.liveries!=null && p.liveries.Count>0,"Missing selectable livery");
        var method=typeof(LoadoutSelector).GetMethod("GetLiveryOptions",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic);
        foreach(var faction in new[]{"PALA","BDF",""})
        {
            var options=new List<(LiveryKey,string)>();
            method.Invoke(null,new object[]{options,definition,faction,true});
            Require(options.Count>0,"No selectable livery for faction "+faction);
        }
        var handle=Addressables.LoadAssetAsync<LiveryData>(p.liveries[0].assetReference.AssetGUID);
        var data=await handle.Task;
        Require(data!=null && data.Texture!=null && data.Colors!=null && data.Colors.Length>0,"Unresolved livery data/texture/palette");
        lines.Add("Livery: native GetLiveryOptions returns nonempty options for PALA/BDF/unassigned; addressable data/texture/palette loaded");
    }
    static void ValidateLights(GameObject prefab,List<string> lines)
    {
        foreach(var side in new[]{"L","R"})
        {
            var lens=prefab.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="navlight_"+side);
            var target=new Vector3(side=="L"?-6.94f:6.94f,-.135f,-4.89f);
            Require(Vector3.Distance(prefab.transform.InverseTransformPoint(lens.position),target)<.002f,"Wingtip light misplaced: "+side);
            var fx=lens.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="navlight_"+side+"_effects");
            Require(fx.localPosition.sqrMagnitude<.000001f,"Light effect offset: "+side);
            Require(lens.GetComponent<MeshFilter>().sharedMesh.name=="SU57_NavLens","Wrong light lens mesh");
        }
        lines.Add("Navigation lights: left/right lens and effect coincide at wingtip coordinates (+/-6.94,-0.135,-4.89)");
    }
    async Task ValidateSortie(AircraftDefinition definition,List<string> lines)
    {
        Logger.LogInfo("SU57_PROBE_START_OFFLINE_MISSION");
        var missions=MissionGroup.BuiltIn.GetMissions().Concat(MissionGroup.Default.GetMissions()).ToArray();
        Logger.LogInfo("Available missions: "+string.Join(", ",missions.Select(m=>m.Name)));
        var key=missions.FirstOrDefault(m=>m.Name.IndexOf("Free Flight",StringComparison.OrdinalIgnoreCase)>=0);
        if(!key.IsValid()) key=missions.First(m=>m.Name.IndexOf("Escalation",StringComparison.OrdinalIgnoreCase)>=0);
        Mission mission;string error;
        Require(key.TryLoad(out mission,out error),"Mission load: "+error);
        MissionManager.SetMission(mission,false);
        NetworkManagerNuclearOption.i.StartHost(new HostOptions(SocketType.Offline,GameState.SinglePlayer,mission.MapKey));
        Player player=null;
        await WaitUntil(()=>GameManager.GetLocalPlayer<Player>(out player) && MissionManager.IsRunning && Resources.FindObjectsOfTypeAll<Airbase>().Any(b=>b.gameObject.scene.isLoaded && b.CurrentHQ!=null && b.GetAvailableAircraft().Contains(definition)),150,"Offline mission/player/airbase");
        await Task.Delay(5000);
        var airbase=Resources.FindObjectsOfTypeAll<Airbase>().Where(b=>b.gameObject.scene.isLoaded && b.CurrentHQ!=null && b.GetAvailableAircraft().Contains(definition) && b.CanSpawnAircraft(definition)).OrderByDescending(b=>b.SavedAirbase.DisplayName.Contains("Agrapol")).FirstOrDefault();
        Require(airbase!=null,"No operational SU57 hangar in loaded mission");
        ((NuclearOption.UI.LeaderboardMenu)Get(GameplayUI.i,"leaderboardMenu")).JoinFactionCallback(airbase.CurrentHQ);
        Require(player.HQ==airbase.CurrentHQ,"Native faction selection failed");
        player.SetRank(4,true);
        player.SetAllocation(1000);
        player.CreditAirframe(definition,1,false);
        await Task.Delay(1500);
        GameplayUI.i.SelectAirbase(airbase);
        if(FlightTest)
        {
            // Select empty stations through the existing native loadout defaults.
            // This is in-memory test setup and does not alter the shipped bundle.
            var empty=new Loadout();
            for(int i=0;i<definition.unitPrefab.GetComponentInChildren<WeaponManager>(true).hardpointSets.Length;i++)empty.weapons.Add(null);
            GameManager.aircraftCustomization[definition]=new AircraftCustomization(empty,1f,0);
        }
        GameplayUI.i.SelectAircraft();
        var menu=Resources.FindObjectsOfTypeAll<AircraftSelectionMenu>().First(m=>m.gameObject.scene.isLoaded && m.gameObject.activeInHierarchy);
        Require(menu.CanFlyAircraft(definition),"SU57 absent from selection menu");
        ((AircraftInventoryMenu)Get(menu,"aircraftInventoryMenu")).SetSelectedType(definition);
        await Task.Delay(4000);
        Require(menu.GetSelectedType()==definition,"Selection did not switch to SU57");
        var selector=(LoadoutSelector)Get(menu,"loadoutSelector");
        Require(selector.CurrentLivery.Index==0,"CurrentLivery invalid");
        Require(((TMPro.TMP_Text)Get(menu,"info")).text==definition.description,"Description stale after aircraft selection");
        Require(((UnityEngine.UI.Button)Get(menu,"flyButton")).interactable,"Fly button is disabled");
        lines.Add("Selection: "+key.Name+" / "+airbase.SavedAirbase.DisplayName+"; native menu selects SU57, CurrentLivery=0, description refreshes, FLY enabled");
        var preview=(Aircraft)Get(menu,"previewAircraft");
        CameraStateManager.i.FocusPosition(preview.transform.position+Vector3.up,preview.transform.rotation*Quaternion.Euler(24,225,0),24);
        await Task.Delay(1500);
        ScreenCapture.CaptureScreenshot(FlightTest?FlightOutput+"/selection.png":Path.Combine(OutputRoot,"SU57_0.2.3_selection.png"));
        await Task.Delay(1500);
        menu.FlyAircraft();
        await WaitUntil(()=>player.Aircraft!=null && player.Aircraft.definition==definition,45,"Native FLY spawn");
        var aircraft=player.Aircraft;
        if(FlightTest)
        {
            foreach(var part in aircraft.partLookup)
            {
                var name=part.name;
                part.onApplyDamage+=d=>File.AppendAllText(FlightOutput+"/part-damage.txt",Time.timeSinceLevelLoad+" "+name+" HP="+d.hitPoints+" impact="+d.impactDamage+" detached="+d.detached+"\n");
                part.onJointBroken+=p=>File.AppendAllText(FlightOutput+"/part-damage.txt",Time.timeSinceLevelLoad+" "+name+" JOINT BROKEN HP="+Get(p,"hitPoints")+" detached="+Get(p,"detachedFromUnit")+"\n");
            }
        }
        await Task.Delay(8000);
        Require(aircraft!=null && player.Aircraft==aircraft && GameManager.IsLocalAircraft(aircraft),"Player did not attach to spawned SU57");
        Require(!float.IsNaN(aircraft.transform.position.y),"Spawned aircraft physics invalid");
        if(FlightTest)DumpFlightState(aircraft);
        if(CanopyTest)ValidateCanopy(aircraft.gameObject,lines,false);
        var parts=aircraft.partLookup.ToArray();
        lines.Add("Spawn health: "+string.Join("; ",parts.Select(p=>p.name+"="+Get(p,"hitPoints")+",detached="+Get(p,"detachedFromUnit"))));
        foreach(var engine in aircraft.engineStates.OfType<Turbojet>())
        {
            lines.Add("Engine "+engine.name+": fire="+Get(engine,"engineFire")+", damageFactor="+Get(engine,"damageFactor"));
            Require(!(bool)Get(engine,"engineFire"),"Engine caught fire after spawn: "+engine.name);
            Require(Equals(Get(engine,"operable"),true),"Engine inoperable after spawn: "+engine.name);
        }
        Require(parts.All(p=>Convert.ToSingle(Get(p,"hitPoints"))>=99 && !(bool)Get(p,"detachedFromUnit")),"Aircraft sustained damage after spawn");
        ScreenCapture.CaptureScreenshot(FlightTest?FlightOutput+"/spawn.png":Path.Combine(OutputRoot,"SU57_0.2.3_sortie.png"));
        await Task.Delay(1500);
        lines.Add("Sortie: AircraftSelectionMenu.FlyAircraft -> native Spawner -> local player attached to SU57; aircraft remains valid after 8 seconds");
        if(FlightTest) await ValidateFlight(aircraft,lines);
        else lines.Add("Scope: actual offline selection and sortie entry; manual takeoff and flight handling not tested");
    }
    async Task ValidateFlight(Aircraft a,List<string> lines)
    {
        Require(a.loadout.weapons.All(w=>w==null),"Flight test must have empty weapon stations");
        Require(!a.weaponStations.SelectMany(s=>s.Weapons).Any(w=>w!=null),"Weapons remain registered in empty loadout");
        lines.Add("Flight setup: empty native loadout, no registered weapons, 100% fuel; original physics and damage enabled");
        // A hidden test window can still receive Rewired/UI navigation keys.
        // Disable input maps only in this diagnostic process; the native pilot
        // continues writing the aircraft inputs normally. No saved bindings change.
        foreach(var inputPlayer in Rewired.ReInput.players.AllPlayers)
            inputPlayer.controllers.maps.SetAllMapsEnabled(false);
        foreach(var inputModule in Resources.FindObjectsOfTypeAll<UnityEngine.InputSystem.UI.InputSystemUIInputModule>())
            if(inputModule.gameObject.scene.isLoaded)inputModule.enabled=false;
        if(UnityEngine.EventSystems.EventSystem.current!=null)
            UnityEngine.EventSystems.EventSystem.current.sendNavigationEvents=false;
        lines.Add("Test isolation: Rewired input maps and UI input modules disabled in this process after native sortie entry; no saved key bindings changed");
        observedAircraft=a;flightStart=Time.realtimeSinceStartup;
        DumpFlightState(a);
        telemetry=new StreamWriter(FlightOutput+"/telemetry.csv");
        telemetry.WriteLine("realtime_s,phase,simulation_s,altitude_m,agl_m,speed_mps,vertical_mps,heading_deg,throttle,pitch_input,roll_input,yaw_input,brake,gear,pilot_state,fuel_kg,disabled,ejected,engine_thrust_N,part_health");
        Application.logMessageReceived+=OnFlightLog;
        var pilot=a.pilots[0];
        try
        {
            a.NetworkIgnition=true;
            var initialFuel=a.GetFuelQuantity();
            var startAltitude=a.transform.GlobalPosition().y;
            bool touchedDown=false;
            a.OnTouchdown+=()=>touchedDown=true;
            // Use the game's normal AI initialization so taxi/takeoff and landing
            // retain their native state transitions and scheduled safety checks.
            typeof(Pilot).GetMethod("SetStartingAiState",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(pilot,null);
            SetPhase("taxi-and-takeoff");
            await WatchFlight(a,()=>a.radarAlt>80f,240,"Native taxi and takeoff");
            CheckHealth(a);
            lines.Add("PASS takeoff: native taxi/takeoff, AGL="+a.radarAlt.ToString("F1")+"m, speed="+a.speed.ToString("F1")+"m/s, elapsed="+(Time.realtimeSinceStartup-flightStart).ToString("F1")+"s");
            var heading=a.transform.eulerAngles.y;
            var cruise=new Su57ProbeCruiseState{Heading=heading,Altitude=startAltitude+650f,TargetSpeed=150f};
            pilot.flightInfo.HasTakenOff=true;
            pilot.SwitchState(cruise);
            a.SetGear(false);
            SetPhase("climb");
            await WatchFlight(a,()=>Mathf.Abs(a.transform.GlobalPosition().y-cruise.Altitude)<60f && a.speed>100f,90,"Climb to target altitude");
            lines.Add("PASS climb: gained "+(a.transform.GlobalPosition().y-startAltitude).ToString("F1")+"m under engine power");
            await CaptureFlight(a,"takeoff-climb");
            SetPhase("level");
            float stableSince=-1;
            await WatchFlight(a,()=>{
                bool stable=Mathf.Abs(a.transform.GlobalPosition().y-cruise.Altitude)<60f && Mathf.Abs(a.rb.velocity.y)<5f && a.speed>100f;
                if(!stable)stableSince=-1;else if(stableSince<0)stableSince=Time.realtimeSinceStartup;
                return stableSince>=0 && Time.realtimeSinceStartup-stableSince>10f;
            },90,"Ten seconds of stable level flight");
            Require(a.gearState==LandingGear.GearState.LockedRetracted,"Landing gear did not retract");
            lines.Add("PASS level: >=10s continuous altitude error<60m, |vertical speed|<5m/s, speed>100m/s; gear retracted");
            SetPhase("left-turn");cruise.Heading=heading-50f;
            await WatchHeading(a,cruise.Heading,60,"Left heading response and five-second hold");
            lines.Add("PASS left turn: target="+cruise.Heading.ToString("F1")+", actual="+a.transform.eulerAngles.y.ToString("F1"));
            await CaptureFlight(a,"left-turn");
            SetPhase("right-turn");cruise.Heading=heading+50f;
            await WatchHeading(a,cruise.Heading,90,"Right heading response and five-second hold");
            lines.Add("PASS right turn: target="+cruise.Heading.ToString("F1")+", actual="+a.transform.eulerAngles.y.ToString("F1"));
            await CaptureFlight(a,"right-turn");
            touchedDown=false;
            SetPhase("landing");pilot.SwitchState(pilot.AILandingState);
            await WatchFlight(a,()=>touchedDown && a.radarAlt<5f && a.speed<2.5f,420,"Native runway landing and stop");
            CheckHealth(a);
            Require(a.gearState==LandingGear.GearState.LockedExtended,"Landing gear did not extend");
            Require(minimumFuel<initialFuel,"Engines did not consume fuel");
            lines.Add("PASS landing: native touchdown event, speed="+a.speed.ToString("F2")+"m/s, AGL="+a.radarAlt.ToString("F2")+"m, gear extended, all parts intact");
            lines.Add("Fuel consumed before any service-point refill="+(initialFuel-minimumFuel).ToString("F1")+"kg; no teleports, added forces, invulnerability or physics changes");
            await CaptureFlight(a,"landed");
            Require(flightErrors.Count==0,"Runtime errors during flight: "+string.Join(" | ",flightErrors.Take(5)));
            lines.Add("PASS: automated native-physics flight cycle; manual controls/handling and multiplayer remain untested");
        }
        finally
        {
            Application.logMessageReceived-=OnFlightLog;
            if(a!=null)DumpFlightState(a,"end-systems.txt");
            else File.WriteAllText(FlightOutput+"/end-systems.txt","Aircraft was unloaded during the test; primary failure is preserved in flight-validation.txt");
            if(flightErrors.Count>0)File.WriteAllLines(FlightOutput+"/runtime-errors.txt",flightErrors);
            lines.Add("Flight telemetry: "+FlightOutput+"/telemetry.csv");
        }
    }
    void OnFlightLog(string condition,string stackTrace,LogType type)
    {
        if(type==LogType.Error || type==LogType.Exception || type==LogType.Assert)flightErrors.Add(condition+"\n"+stackTrace);
    }
    void SetPhase(string value)
    {
        phase=value;Logger.LogInfo("SU57_FLIGHT_PHASE: "+value);
        File.WriteAllText(FlightOutput+"/progress.txt",value+" at "+DateTime.UtcNow.ToString("O"));
    }
    static void CheckHealth(Aircraft a)
    {
        Require(a!=null && !a.disabled && !a.HasEjected(),"Aircraft lost/disabled/ejected");
        Require(!float.IsNaN(a.transform.position.y) && !float.IsInfinity(a.rb.velocity.sqrMagnitude),"Nonfinite physics state");
        var damaged=a.partLookup.Where(p=>Convert.ToSingle(Get(p,"hitPoints"))<99f || (bool)Get(p,"detachedFromUnit")).ToArray();
        Require(damaged.Length==0,"Damage sustained during flight: "+string.Join("; ",damaged.Select(p=>p.name+" HP="+Get(p,"hitPoints")+" detached="+Get(p,"detachedFromUnit")))+"; state="+a.pilots[0].GetCurrentState()+" speed="+a.speed+" AGL="+a.radarAlt);
        Require(a.engineStates.All(e=>!Equals(Get(e,"engineFire"),true)),"Engine fire during flight");
        Require(a.engineStates.Count==2 && a.engineStates.All(e=>!Equals(Get(e,"operable"),false)),"Engine inoperable during flight");
    }
    static void DumpFlightState(Aircraft a,string filename="spawn-systems.txt")
    {
        var details=new List<string>{"Aircraft "+a.name+"; components="+a.GetComponentsInChildren<Component>(true).Length+"; registered parts="+a.partLookup.Count+"; engineStates="+a.engineStates.Count+"; engines="+a.engines.Count+"; localSim="+a.LocalSim+"; rigidbodyKinematic="+a.rb.isKinematic+"; ignition="+a.Ignition};
        foreach(var p in a.partLookup)details.Add("PART "+p.name+" active="+p.gameObject.activeInHierarchy+" type="+p.GetType().Name+" parent="+p.transform.parent?.name+" health="+Get(p,"hitPoints")+" detached="+Get(p,"detachedFromUnit")+" position="+p.transform.GlobalPosition());
        foreach(var e in a.engineStates)
        {
            var b=(MonoBehaviour)e;
            details.Add("ENGINE "+b.name+"; type="+e.GetType().Name+" active="+b.gameObject.activeInHierarchy+" enabled="+b.enabled+"; RPM="+e.GetRPM()+" thrust="+e.GetThrust()+"; "+string.Join("; ",new[]{"aircraft","part","hasFuel","operable","hitPoints","condition","currentThrust","damageFactor","engineFire"}.Select(k=>k+"="+Get(e,k))));
        }
        foreach(var t in a.GetComponentsInChildren<Transform>(true))
            if(t.name.Contains("engine") || t.name.Contains("fuselage") || t.name.Contains("nozzle"))details.Add("NODE "+t.name+" activeSelf="+t.gameObject.activeSelf+" activeHierarchy="+t.gameObject.activeInHierarchy+"; components="+string.Join(";",t.GetComponents<Component>().Select(c=>c==null?"MISSING":c.GetType().Name)));
        File.WriteAllLines(FlightOutput+"/"+filename,details);
    }
    static async Task WatchFlight(Aircraft a,Func<bool> predicate,int seconds,string context)
    {
        var deadline=Time.realtimeSinceStartup+seconds;
        while(Time.realtimeSinceStartup<deadline)
        {
            CheckHealth(a);
            if(predicate())return;
            await Task.Delay(250);
        }
        throw new Exception("Timeout: "+context+"; state="+a.pilots[0].GetCurrentState()+"; speed="+a.speed+"; AGL="+a.radarAlt);
    }
    static async Task WatchHeading(Aircraft a,float heading,int seconds,string context)
    {
        float stableSince=-1;
        await WatchFlight(a,()=>{
            bool stable=Mathf.Abs(Mathf.DeltaAngle(a.transform.eulerAngles.y,heading))<8f && a.radarAlt>60f;
            if(!stable)stableSince=-1;else if(stableSince<0)stableSince=Time.realtimeSinceStartup;
            return stableSince>=0 && Time.realtimeSinceStartup-stableSince>=5f;
        },seconds,context);
    }
    static async Task CaptureFlight(Aircraft a,string label)
    {
        CameraStateManager.i.SetFollowingUnit(a);
        await Task.Delay(500);
        ScreenCapture.CaptureScreenshot(FlightOutput+"/"+label+".png");
        await Task.Delay(500);
    }
    static async Task WaitUntil(Func<bool> predicate,int seconds,string context)
    {
        var deadline=Time.realtimeSinceStartup+seconds;
        while(Time.realtimeSinceStartup<deadline) { if(predicate())return;await Task.Delay(500); }
        throw new Exception("Timeout: "+context);
    }
    static object Get(object instance,string name)
    {
        for(var t=instance.GetType();t!=null;t=t.BaseType)
        {
            var field=t.GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
            if(field!=null) return field.GetValue(instance);
        }
        return null;
    }
    static void ValidateHangarHook(UnitDefinition shelter,AircraftDefinition aircraft)
    {
        // Blueprinter applies this op when a map hangar registers, not to its prefab.
        // Exercise the actual registration hook on inactive copies without starting a mission.
        var root=new GameObject("SU57_validation_airbase");root.SetActive(false);
        try
        {
            var airbase=root.AddComponent<Airbase>();
            var copy=Instantiate(shelter.unitPrefab,root.transform);
            copy.name="shelter1";
            var hangar=copy.GetComponentsInChildren<Hangar>(true).FirstOrDefault(h=>h.name.StartsWith("shelter1"));
            Require(hangar!=null,"No shelter1 hangar");
            // Normally initialized during Unit/Airbase setup in a loaded map.
            hangar.attachedUnit=copy.GetComponent<Unit>();
            Require(hangar.attachedUnit!=null && hangar.attachedUnit.definition==shelter,"Shelter unit definition missing");
            var patch=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Blueprinter.Ops.OpAddAircraftToHangarsHandler+AirbaseAddHangarPatch")).First(t=>t!=null);
            patch.GetMethod("Prefix",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic).Invoke(null,new object[]{airbase,hangar});
            Require(((AircraftDefinition[])Get(hangar,"availableAircraft")).Contains(aircraft),"Hangar registration hook did not add SU57");
        }
        finally { Destroy(root); }
    }
    static void Require(bool condition,string message) { if(!condition) throw new Exception(message); }

    static void ValidateCanopy(GameObject aircraft,List<string> lines,bool render)
    {
        var body=aircraft.transform.Find("SU57_Model");
        Material glass=null;
        for(int i=0;i<3;i++)
        {
            var mats=body.Find("SU57_Body_LOD"+i).GetComponent<Renderer>().sharedMaterials;
            Require(mats.Length==2 && mats[0].name.StartsWith("SU57_Canopy") && mats[1].name.StartsWith("SU57_Camouflage"),"Incorrect glass/skin LOD material assignment");
            var m=mats[0];glass=m;
            Require(m.GetFloat("_Surface")==1 && m.GetFloat("_ZWrite")==0 && m.renderQueue==3000 && m.GetColor("_BaseColor").a<.3f,"Canopy lost transparency after runtime loading/livery");
            Require(m.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT") && !m.GetShaderPassEnabled("ShadowCaster"),"Incorrect canopy shader variant/shadow state");
        }
        lines.Add("PASS canopy "+(render?"loaded prefab":"spawned aircraft after livery")+": all three LOD slots transparent, alpha="+glass.GetColor("_BaseColor").a+", metallic="+glass.GetFloat("_Metallic")+", queue="+glass.renderQueue+", no depth/shadow writes");
        if(!render)return;
        Require(SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null,"Canopy pixel test needs a real GPU");
        string output=FlightOutput+"/canopy";Directory.CreateDirectory(output);
        var root=new GameObject("Temporary canopy GPU test");root.transform.position=new Vector3(0,-10000,0);
        var quad=GameObject.CreatePrimitive(PrimitiveType.Quad);quad.name="Loaded canopy material";
        quad.transform.SetParent(root.transform,false);quad.layer=31;
        DestroyImmediate(quad.GetComponent<Collider>());
        quad.GetComponent<Renderer>().sharedMaterial=glass;
        var cameraObject=new GameObject("Canopy test camera");cameraObject.transform.SetParent(root.transform,false);
        var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;
        camera.transform.localPosition=new Vector3(0,0,-2);camera.orthographic=true;camera.orthographicSize=.75f;
        camera.clearFlags=CameraClearFlags.SolidColor;camera.cullingMask=1<<31;camera.nearClipPlane=.1f;camera.farClipPlane=10;
        camera.allowHDR=false;camera.allowMSAA=false;
        var rt=new RenderTexture(256,256,24,RenderTextureFormat.ARGB32);camera.targetTexture=rt;
        var previous=RenderTexture.active;
        try
        {
            var red=new Color(.8f,.1f,.05f);var blue=new Color(.05f,.65f,.85f);
            var clearRed=CanopyPixelCapture(camera,rt,quad,false,red,output+"/clear-red.png");
            var clearBlue=CanopyPixelCapture(camera,rt,quad,false,blue,output+"/clear-blue.png");
            var glassRed=CanopyPixelCapture(camera,rt,quad,true,red,output+"/glass-red.png");
            var glassBlue=CanopyPixelCapture(camera,rt,quad,true,blue,output+"/glass-blue.png");
            float backgroundDifference=Vector3.Distance(new Vector3(clearRed.r,clearRed.g,clearRed.b),new Vector3(clearBlue.r,clearBlue.g,clearBlue.b));
            float transmittedDifference=Vector3.Distance(new Vector3(glassRed.r,glassRed.g,glassRed.b),new Vector3(glassBlue.r,glassBlue.g,glassBlue.b));
            float ratio=transmittedDifference/backgroundDifference;
            File.WriteAllText(output+"/pixel-transmission.txt","GPU="+SystemInfo.graphicsDeviceName+"\nclear delta="+backgroundDifference+"\nglass delta="+transmittedDifference+"\ntransmission ratio="+ratio+"\n");
            Require(backgroundDifference>.3f && ratio>.55f && ratio<.98f,"GPU canopy pixel transmission outside expected range: "+ratio);
            lines.Add("PASS canopy GPU: rendered loaded material over two background colors; measured transmission ratio="+ratio.ToString("F3")+" (0=opaque, 1=invisible), GPU="+SystemInfo.graphicsDeviceName);
        }
        finally
        {
            RenderTexture.active=previous;camera.targetTexture=null;rt.Release();DestroyImmediate(rt);DestroyImmediate(root);
        }
    }
    static Color CanopyPixelCapture(Camera camera,RenderTexture rt,GameObject quad,bool visible,Color background,string path)
    {
        quad.SetActive(visible);camera.backgroundColor=background;camera.Render();RenderTexture.active=rt;
        var texture=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);
        texture.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);texture.Apply();
        File.WriteAllBytes(path,texture.EncodeToPNG());
        Color sample=Color.clear;for(int y=120;y<136;y++)for(int x=120;x<136;x++)sample+=texture.GetPixel(x,y);
        DestroyImmediate(texture);return sample/256f;
    }
}

// The test commands the existing autopilot and never moves the rigidbody itself.
public sealed class Su57ProbeCruiseState : PilotBaseState
{
    public float Heading,Altitude,TargetSpeed;
    public override void EnterState(Pilot p)
    {
        pilot=p;aircraft=p.aircraft;controlInputs=aircraft.GetInputs();stateDisplayName="Su-57 flight validation";
        aircraft.SetFlightAssist(true);
    }
    public override void UpdateState(Pilot p){}
    public override void LeaveState(){}
    public override void FixedUpdateState(Pilot p)
    {
        var position=aircraft.GlobalPosition();
        var direction=Quaternion.Euler(0,Heading,0)*Vector3.forward;
        var destination=position+direction*5000f;
        destination.y=Altitude;
        controlInputs.throttle=Mathf.Clamp(.65f+(TargetSpeed-aircraft.speed)*.03f,.2f,1f);
        controlInputs.brake=0;controlInputs.customAxis1=1;
        aircraft.autopilot.AutoAim(destination,true,false,false,.95f,50f,false,0,Vector3.zero);
    }
}

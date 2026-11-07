using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public static partial class BuildSu57
{
    // Blueprinter scans loaded BepInEx plugin assemblies for .nobp manifest
    // resources. Ship the aircraft and its animation in one plugin assembly.
    // Use the existing Unity compiler and game references; no extra SDK.
    static string BuildSingleFileMod(string bundle)
    {
        string project=Path.GetFullPath(Path.Combine(Application.dataPath,".."));
        string game=Environment.GetEnvironmentVariable("SU57_GAME_DIR");
        if(string.IsNullOrWhiteSpace(game))
        {
            string executable=EditorPrefs.GetString(Blueprinter.BlueprinterSettings.GameExecutablePrefsKey,string.Empty);
            if(!string.IsNullOrWhiteSpace(executable))game=Path.GetDirectoryName(executable);
        }
        if(string.IsNullOrWhiteSpace(game))
            throw new Exception("Set the NuclearOption.exe path in Blueprinter > Project Setup, or set SU57_GAME_DIR to the game directory before starting Unity.");
        game=Path.GetFullPath(game);
        string managed=Path.Combine(game,"NuclearOption_Data/Managed");
        string core=Path.Combine(game,"BepInEx/core");
        string compiler=Path.Combine(EditorApplication.applicationContentsPath,"DotNetSdkRoslyn/csc.dll");
        string source=Path.Combine(project,"Tools/SU57GearMotion/Su57GearMotion.cs");
        string physicsSource=Path.Combine(project,"Tools/SU57GearMotion/Su57MassProperties.cs");
        string fractureSource=Path.Combine(project,"Tools/SU57GearMotion/Su57FractureDisplay.cs");
        string massProfile=Path.Combine(project,NativeMassProfilePath);
        string work=Output+"/Internal/SingleFile";
        string delivery=Output+"/Install";
        string staged=work+"/Su-57.dll";
        string dll=delivery+"/Su-57.dll";
        var references=Directory.GetFiles(managed,"*.dll").OrderBy(p=>p,StringComparer.Ordinal).ToList();
        references.Add(Path.Combine(core,"BepInEx.dll"));
        references.Add(Path.Combine(core,"0Harmony.dll"));
        foreach(string path in references.Concat(new[]{source,physicsSource,fractureSource,massProfile,compiler,bundle}))
            if(!File.Exists(path))throw new Exception("Missing Su-57 single-file build input: "+path);

        Directory.CreateDirectory(work);
        Directory.CreateDirectory(delivery);
        string response=work+"/Su-57.rsp";
        var args=new List<string>{"/utf8output","/nostdlib+","/target:library","/optimize+","/deterministic+",
            "/out:\""+staged+"\"",
            "/resource:\""+Path.GetFullPath(bundle)+"\",SU57.Aircraft.nobp",
            "/resource:\""+massProfile+"\",SU57.NativeMassProperties.json"};
        args.AddRange(references.Select(path=>"/reference:\""+path+"\""));
        args.Add("\""+source+"\"");
        args.Add("\""+physicsSource+"\"");
        args.Add("\""+fractureSource+"\"");
        File.WriteAllLines(response,args,new UTF8Encoding(false));
        // Compile away from the delivery file. Failed builds leave the last
        // successful DLL intact and never publish a code-only companion.
        if(File.Exists(staged))File.Delete(staged);
        var start=new System.Diagnostics.ProcessStartInfo
        {
            FileName="dotnet", Arguments="\""+compiler+"\" /noconfig @\""+response+"\"",
            WorkingDirectory=project, UseShellExecute=false, CreateNoWindow=true,
            RedirectStandardOutput=true, RedirectStandardError=true,
            StandardOutputEncoding=Encoding.UTF8, StandardErrorEncoding=Encoding.UTF8
        };
        using(var process=System.Diagnostics.Process.Start(start))
        {
            if(process==null)throw new Exception("Could not start the Su-57 single-file compiler");
            var stdout=process.StandardOutput.ReadToEndAsync();
            var stderr=process.StandardError.ReadToEndAsync();
            if(!process.WaitForExit(120000))
            {
                process.Kill();
                throw new Exception("Su-57 single-file compilation timed out");
            }
            string log=stdout.GetAwaiter().GetResult()+Environment.NewLine+stderr.GetAwaiter().GetResult();
            string logPath=Path.Combine(project,"Logs/su57-singlefile-build.log");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath));
            File.WriteAllText(logPath,log);
            if(process.ExitCode!=0 || !File.Exists(staged))
                throw new Exception("Su-57 single-file compilation failed; see "+logPath+Environment.NewLine+log);
        }
        if(File.Exists(dll))File.Replace(staged,dll,null);
        else File.Move(staged,dll);
        Notes.Add("Install only Install/Su-57.dll: aircraft embedded as SU57.Aircraft.nobp plus main-gear animation; keep existing Blueprinter; remove old loose Su-57 nobp and SU57.GearMotion.dll when switching to this single-file mod");
        return dll;
    }
}

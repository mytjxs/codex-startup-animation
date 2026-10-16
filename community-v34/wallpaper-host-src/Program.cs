using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CodexDragonWallpaper {
internal sealed class Configuration {
    internal string Exe,Renderer,Media,AlternateMedia;internal bool Enabled;internal int AlternateDiagnostic;
    internal string[] MediaFiles {get{return String.IsNullOrEmpty(AlternateMedia)?new[]{Media}:new[]{Media,AlternateMedia};}}
    internal static Configuration Load(string directory){
        string file=Files.Regular(Path.Combine(directory,"wallpaper-host-config.json"));if(new FileInfo(file).Length>16384)throw new HostFailure(50);
        var m=Json.Map(Json.Serializer().DeserializeObject(File.ReadAllText(file,Encoding.UTF8)));
        if(Json.Number(m,"version")!=1)throw new HostFailure(50);
        string package=Json.Text(m,"packageFullName"),exeHash=Json.Text(m,"executableSha256"),rendererHash=Json.Text(m,"rendererSha256");
        if(!ValidHash(exeHash)||!ValidHash(rendererHash))throw new HostFailure(50);
        string exe=Path.Combine(Native.PackagePath(package),"app","ChatGPT.exe");Native.VerifyOfficial(exe,exeHash);
        string renderer=Files.Regular(Path.Combine(directory,"renderer.js"));if(new FileInfo(renderer).Length>131072||Files.Hash(renderer)!=rendererHash.ToLowerInvariant())throw new HostFailure(51);
        string media=Files.Regular(Json.Text(m,"mediaPath"));
        long mediaBytes=Json.Number(m,"mediaBytes");string mediaHash=Json.Text(m,"mediaSha256");
        if(Path.GetFileName(media)!="dragon-wallpaper-ambient-2k-144fps.mp4"||mediaBytes<=0||mediaBytes>256L*1024*1024||!ValidHash(mediaHash)||new FileInfo(media).Length!=mediaBytes||Files.Hash(media)!=mediaHash.ToLowerInvariant())throw new HostFailure(52);
        object enabled=Json.Get(m,"wallpaperEnabled");if(!(enabled is bool))throw new HostFailure(50);
        int alternateCode;string alternate=LoadAlternateMedia(m,out alternateCode);
        // Optional-media damage must not disable the established wallpaper.
        // 253=present but rejected, 254=verified; absence is legacy-compatible.
        return new Configuration{Exe=exe,Renderer=File.ReadAllText(renderer,Encoding.UTF8),Media=media,AlternateMedia=alternate,AlternateDiagnostic=alternateCode,Enabled=(bool)enabled};
    }
    internal static string LoadAlternateMedia(Dictionary<string,object> m,out int diagnostic){
        diagnostic=0;
        if(!m.ContainsKey("alternateMediaPath")&&!m.ContainsKey("alternateMediaBytes")&&!m.ContainsKey("alternateMediaSha256"))return null;
        diagnostic=253;
        try{
            string path=Json.Text(m,"alternateMediaPath"),hash=Json.Text(m,"alternateMediaSha256");
            long bytes=Json.Number(m,"alternateMediaBytes");
            if(String.IsNullOrEmpty(path)||Path.GetFileName(path)!="dragon-wallpaper-particles-1440p48.mp4"||bytes<=0||bytes>256L*1024*1024||!ValidHash(hash))return null;
            string media=Files.Regular(path);
            if(new FileInfo(media).Length!=bytes||Files.Hash(media)!=hash.ToLowerInvariant())return null;
            diagnostic=254;return media;
        }catch(HostFailure){return null;}
        catch(IOException){return null;}
        catch(UnauthorizedAccessException){return null;}
        catch(ArgumentException){return null;}
        catch(NotSupportedException){return null;}
        catch(System.Security.SecurityException){return null;}
        catch(System.Security.Cryptography.CryptographicException){return null;}
    }
    static bool ValidHash(string hash){return Regex.IsMatch(hash??"","^[A-Fa-f0-9]{64}$");}
}
internal static class HostEnvironment {
    internal static Dictionary<string,string> Build(string isolatedRoot){
        var env=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        string[] allow={"SystemRoot","WINDIR","SystemDrive","COMSPEC","TEMP","TMP","USERPROFILE","APPDATA","LOCALAPPDATA","HOMEDRIVE","HOMEPATH","PROGRAMDATA","ProgramFiles","ProgramFiles(x86)","ProgramW6432","PATH","PATHEXT","PROCESSOR_ARCHITECTURE","OS","NUMBER_OF_PROCESSORS","USERNAME","USERDOMAIN","USERDOMAIN_ROAMINGPROFILE","SESSIONNAME","LOGONSERVER","ALLUSERSPROFILE","COMMONPROGRAMFILES","COMMONPROGRAMFILES(X86)","COMMONPROGRAMW6432","CODEX_HOME","CODEX_ELECTRON_USER_DATA_PATH"};
        foreach(string key in allow){string value=Environment.GetEnvironmentVariable(key);if(value!=null&&value.IndexOf('\0')<0)env[key]=value;}
        // API keys, NODE_OPTIONS, ELECTRON_RUN_AS_NODE and arbitrary inherited
        // instrumentation are intentionally absent. No global env is changed.
        if(isolatedRoot!=null){env["CODEX_HOME"]=Path.Combine(isolatedRoot,"codex-home");env["CODEX_ELECTRON_USER_DATA_PATH"]=Path.Combine(isolatedRoot,"electron-profile");}
        return env;
    }
}
internal static class PreparationWatchdog {
    // The launcher can show a living loading loop for up to 90 seconds. Keep
    // its independent crash cleanup bounded, without disabling healthy loading.
    internal const int TimeoutMilliseconds=120000;
    internal static bool Expire(HostState state,long elapsedMilliseconds){
        lock(state.Gate){
            if(elapsedMilliseconds<TimeoutMilliseconds||state.Disabled||state.RevealEverRequested||state.Revealed)return false;
            state.Disabled=true;state.RequestedPause=true;state.Work.Set();return true;
        }
    }
}
internal static class PreparationDiagnostics {
    // Only immutable, allowlisted renderer state may reach the log. The raw
    // status dictionary, strings, document IDs and exception text never do.
    internal sealed class Snapshot {
        internal readonly long SampledElapsedMs;
        internal readonly string Fields;
        internal Snapshot(long sampledElapsedMs,string fields){SampledElapsedMs=sampledElapsedMs;Fields=fields;}
    }
    static readonly string[] SnapshotBooleans={"attached","mediaReady","prepared","disposed","playing","wallpaperApplied","revealed","layoutSupported","firstFrameConfirmed","documentHidden","stageVisible","recoveryNeeded","leaseNeedsRenew","layoutSuspended","memoryPressure","manuallyPaused","videoPaused","videoSeeking","playStarted","frameCallbackPending","preparationTimerPending","frozen"};
    static readonly string[] SnapshotCodes={"idle","loading","prepared","playing","reveal-pending","ready-hidden","resuming","paused","hibernating","disposed","unsupported-layout","unverified","media-error","play-rejected","first-frame-timeout","reveal-timeout","resume-timeout","playback-stalled","invalid-file","file-too-large","retired","recovery-budget-exhausted"};
    static object SnapshotValue(Dictionary<string,object> status,string key){object value;return status!=null&&status.TryGetValue(key,out value)?value:null;}
    static string SnapshotBoolean(object value){return value is bool?((bool)value?"1":"0"):"-1";}
    static string SnapshotNumber(object value,long maximum){
        long number;
        if(value is int)number=(int)value;
        else if(value is long)number=(long)value;
        else if(value is double){double n=(double)value;if(Double.IsNaN(n)||Double.IsInfinity(n)||n<0||n>maximum||Math.Floor(n)!=n)return "-1";number=(long)n;}
        else if(value is decimal){decimal n=(decimal)value;if(n<0||n>maximum||Decimal.Truncate(n)!=n)return "-1";number=(long)n;}
        else return "-1";
        return number>=0&&number<=maximum?number.ToString(System.Globalization.CultureInfo.InvariantCulture):"-1";
    }
    static string SnapshotEnum(object value,string[] known){string text=value as string;return text!=null&&Array.IndexOf(known,text)>=0?text:"unknown";}
    internal static Snapshot CapturePreparationSnapshot(Dictionary<string,object> status,string expectedGeneration,long elapsedMilliseconds){
        if(status==null||elapsedMilliseconds<0||elapsedMilliseconds>31536000000L)return null;
        var fields=new StringBuilder();
        foreach(string key in SnapshotBooleans)fields.Append(' ').Append(key).Append('=').Append(SnapshotBoolean(SnapshotValue(status,key)));
        foreach(string key in new[]{"videoReadyState","videoErrorCode"})fields.Append(' ').Append(key).Append('=').Append(SnapshotNumber(SnapshotValue(status,key),4));
        fields.Append(" videoNetworkState=").Append(SnapshotNumber(SnapshotValue(status,"videoNetworkState"),3));
        fields.Append(" mediaTimeMs=").Append(SnapshotNumber(SnapshotValue(status,"mediaTimeMs"),86400000));
        fields.Append(" mediaGeneration=").Append(SnapshotNumber(SnapshotValue(status,"mediaGeneration"),Int32.MaxValue));
        fields.Append(" presentedFrames=").Append(SnapshotNumber(SnapshotValue(status,"presentedFrames"),Int32.MaxValue));
        fields.Append(" protocolVersion=").Append(SnapshotNumber(SnapshotValue(status,"protocolVersion"),2));
        object generation=SnapshotValue(status,"documentGeneration");
        fields.Append(" generationMatches=").Append(generation is string&&expectedGeneration!=null?((string)generation==expectedGeneration?"1":"0"):"-1");
        fields.Append(" rendererCode=").Append(SnapshotEnum(SnapshotValue(status,"code"),SnapshotCodes));
        fields.Append(" actualPhase=").Append(SnapshotEnum(SnapshotValue(status,"actualPhase"),new[]{"retired","faulted","hibernating","paused","resuming","gap","preparing","prepared","visible"}));
        fields.Append(" health=").Append(SnapshotEnum(SnapshotValue(status,"health"),new[]{"faulted","stale","fresh"}));
        return new Snapshot(elapsedMilliseconds,fields.ToString());
    }
    internal static string FormatPreparationSnapshot(Snapshot snapshot,long elapsedMilliseconds,int kind,int ordinal){
        if(snapshot==null||elapsedMilliseconds<snapshot.SampledElapsedMs||elapsedMilliseconds>31536000000L||kind<0||kind>2||ordinal<1||ordinal>8)throw new ArgumentOutOfRangeException();
        // kind: 0=periodic observation, 1=phase10 completed, 2=phase10 failed.
        // Boolean/number -1 and enum unknown mean missing/invalid, never false.
        string line=DateTime.UtcNow.ToString("o",System.Globalization.CultureInfo.InvariantCulture)+" phase=10 snapshotKind="+kind.ToString(System.Globalization.CultureInfo.InvariantCulture)+
            " ordinal="+ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture)+" elapsedMs="+elapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture)+
            " sampleAgeMs="+(elapsedMilliseconds-snapshot.SampledElapsedMs).ToString(System.Globalization.CultureInfo.InvariantCulture)+" pid="+processId.ToString(System.Globalization.CultureInfo.InvariantCulture)+" trace="+trace+snapshot.Fields+Environment.NewLine;
        if(line.Length>1024)throw new ArgumentOutOfRangeException();return line;
    }
    internal static void RecordPreparationSnapshot(Snapshot snapshot,long elapsedMilliseconds,int kind,int ordinal){try{Enqueue(FormatPreparationSnapshot(snapshot,elapsedMilliseconds,kind,ordinal));}catch{}}
    static int writerBusy;
    static readonly object writerGate=new object();
    static readonly Queue<string> pending=new Queue<string>();
    static int dropped;
    const int Capacity=64;
    static readonly string trace=ReadTrace();
    static readonly int processId=ReadPid();
    static int ReadPid(){try{using(var process=Process.GetCurrentProcess())return process.Id;}catch{return 0;}}
    static string ReadTrace(){
        try{string value=Environment.GetEnvironmentVariable("CODEX_DRAGON_STARTUP_TRACE");if(value!=null&&value.Length==32&&Regex.IsMatch(value,"^[a-f0-9]{32}$"))return value;}catch{}
        return Guid.NewGuid().ToString("N");
    }
    static string ExceptionType(Exception error){
        if(error==null)return "None";Type t=error.GetType();
        if(t==typeof(HostFailure)||t==typeof(InvalidOperationException)||t==typeof(ArgumentException)||t==typeof(ArgumentOutOfRangeException)||
            t==typeof(TimeoutException)||t==typeof(IOException)||t==typeof(InvalidDataException)||t==typeof(EndOfStreamException)||
            t==typeof(FileNotFoundException)||t==typeof(DirectoryNotFoundException)||t==typeof(UnauthorizedAccessException)||
            t==typeof(System.ComponentModel.Win32Exception)||t==typeof(System.Security.SecurityException)||
            t==typeof(System.Security.Cryptography.CryptographicException)||t==typeof(ObjectDisposedException)||t==typeof(AggregateException))return t.Name;
        return "OtherException";
    }
    internal static string FormatEvent(int phase,long elapsedMilliseconds,int code,Exception error){
        if(phase<0||phase>63||elapsedMilliseconds<0||code<0||code>65535)throw new ArgumentOutOfRangeException();
        return DateTime.UtcNow.ToString("o",System.Globalization.CultureInfo.InvariantCulture)+" phase="+phase.ToString(System.Globalization.CultureInfo.InvariantCulture)+
            " elapsedMs="+elapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture)+" code="+code.ToString(System.Globalization.CultureInfo.InvariantCulture)+
            " pid="+processId.ToString(System.Globalization.CultureInfo.InvariantCulture)+" trace="+trace+
            " exceptionType="+ExceptionType(error)+" hresult="+(error==null?0:error.HResult).ToString(System.Globalization.CultureInfo.InvariantCulture)+Environment.NewLine;
    }
    internal static void RecordEvent(int phase,long elapsedMilliseconds,int code,Exception error){try{Enqueue(FormatEvent(phase,elapsedMilliseconds,code,error));}catch{}}
    internal static string FormatRotation(string clip,long cycles,bool available,bool failed,long elapsedMilliseconds){
        if((clip!="ambient"&&clip!="particles")||cycles<0||cycles>Int32.MaxValue||elapsedMilliseconds<0)throw new ArgumentOutOfRangeException();
        return DateTime.UtcNow.ToString("o",System.Globalization.CultureInfo.InvariantCulture)+" phase=35 elapsedMs="+elapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture)+
            " pid="+processId.ToString(System.Globalization.CultureInfo.InvariantCulture)+" trace="+trace+" activeClip="+clip+
            " completedCycles="+cycles.ToString(System.Globalization.CultureInfo.InvariantCulture)+" alternateAvailable="+(available?"1":"0")+" alternateFailed="+(failed?"1":"0")+Environment.NewLine;
    }
    internal static void RecordRotation(string clip,long cycles,bool available,bool failed,long elapsedMilliseconds){try{Enqueue(FormatRotation(clip,cycles,available,failed,elapsedMilliseconds));}catch{}}
    internal static string FormatMediaBindingFallback(long elapsedMilliseconds,bool succeeded){
        return FormatEvent(9,elapsedMilliseconds,succeeded?256:255,null).TrimEnd('\r','\n')+
            " mediaFallbackCause=cdp-file-input-rejected-32 mediaFallbackTarget=ambient"+Environment.NewLine;
    }
    internal static void RecordMediaBindingFallback(long elapsedMilliseconds,bool succeeded){try{Enqueue(FormatMediaBindingFallback(elapsedMilliseconds,succeeded));}catch{}}
    internal static void RecordFailure(int phase,long elapsedMilliseconds,Exception error){
        var failure=error as HostFailure;RecordEvent(phase,elapsedMilliseconds,failure==null?199:100+failure.Code,error);
    }
    internal static string FormatLine(int phase,long elapsedMilliseconds,int code){
        if(phase<0||phase>15||elapsedMilliseconds<0||elapsedMilliseconds>31536000000L||code<0||code>65535)throw new ArgumentOutOfRangeException();
        return DateTime.UtcNow.ToString("o",System.Globalization.CultureInfo.InvariantCulture)+" phase="+phase.ToString(System.Globalization.CultureInfo.InvariantCulture)+" elapsedMs="+elapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture)+" code="+code.ToString(System.Globalization.CultureInfo.InvariantCulture)+Environment.NewLine;
    }
    internal static void Record(int phase,long elapsedMilliseconds,int code){
        // Fixed numeric telemetry only: no session secret, paths, DOM, text or
        // exception strings. Logging cannot affect startup or grow indefinitely.
        try{
            Enqueue(FormatLine(phase,elapsedMilliseconds,code).TrimEnd('\r','\n')+" pid="+processId.ToString(System.Globalization.CultureInfo.InvariantCulture)+" trace="+trace+Environment.NewLine);
        }catch{}
    }
    static void Enqueue(string line){
        try{
            bool schedule=false;
            lock(writerGate){
                if(pending.Count>=Capacity){dropped=Math.Min(65535,dropped+1);return;}
                pending.Enqueue(line);
                if(writerBusy==0){writerBusy=1;schedule=true;}
            }
            if(schedule)try{if(!ThreadPool.QueueUserWorkItem(_=>Drain()))ResetWriter();}catch{ResetWriter();}
        }catch{}
    }
    static void ResetWriter(){lock(writerGate){dropped=Math.Min(65535,dropped+pending.Count);pending.Clear();writerBusy=0;}}
    static void Drain(){
        try{for(;;){
            string line;
            lock(writerGate){
                if(pending.Count!=0)line=pending.Dequeue();
                // Phase zero is reserved for numeric queue-overflow evidence.
                // No exception, content, path, or session identifier is logged.
                else if(dropped!=0){line=FormatEvent(0,0,dropped,null);dropped=0;}
                else{writerBusy=0;return;}
            }
            WriteLine(line);
        }}catch{ResetWriter();}
    }
    static void WriteLine(string line){
        try{
            string output=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"output");if(!Directory.Exists(output))return;
            string cursor=output;while(!String.IsNullOrEmpty(cursor)){if((File.GetAttributes(cursor)&FileAttributes.ReparsePoint)!=0)return;cursor=Path.GetDirectoryName(cursor);}
            string file=Path.Combine(output,"wallpaper-readiness.log"),archive=file+".1",writer=file+".lock";
            byte[] bytes=Encoding.ASCII.GetBytes(line);if(bytes.Length>1024)return;
            if(File.Exists(writer))Files.Regular(writer);
            // Cross-process exclusive acquisition is immediate: no retry loop
            // and no wait on the startup thread. A busy/unwritable sink drops
            // this diagnostic only. Keep the latest file and one archive.
            using(var held=new FileStream(writer,FileMode.OpenOrCreate,FileAccess.Write,FileShare.None)){
                if(File.Exists(file)){
                    Files.Regular(file);
                    if(new FileInfo(file).Length+bytes.Length>1048576){
                        if(File.Exists(archive)){Files.Regular(archive);File.Delete(archive);}
                        File.Move(file,archive);
                    }
                }
                using(var stream=new FileStream(file,FileMode.OpenOrCreate,FileAccess.Write,FileShare.Read)){stream.Seek(0,SeekOrigin.End);stream.Write(bytes,0,bytes.Length);}
            }
        }catch{}
    }
}
internal sealed class RotationSnapshotRecorder {
    readonly Action<string,long,bool,bool,long> record;
    string lastClip;long lastCycles,lastRecorded=-1;bool lastAvailable,lastFailed;
    internal RotationSnapshotRecorder():this(PreparationDiagnostics.RecordRotation){}
    internal RotationSnapshotRecorder(Action<string,long,bool,bool,long> record){this.record=record;}
    internal void Observe(Dictionary<string,object> status,long elapsedMilliseconds){try{
        // Additive diagnostics: old renderers or malformed optional fields never
        // change readiness. Only fixed enums, booleans and bounded integers log.
        if(status==null||elapsedMilliseconds<0)return;
        string clip=Json.Text(status,"activeClip");object count=Json.Get(status,"completedCycles"),available=Json.Get(status,"alternateAvailable"),failed=Json.Get(status,"alternateFailed");
        if((clip!="ambient"&&clip!="particles")||(!(count is int)&&!(count is long))||!(available is bool)||!(failed is bool))return;
        long cycles=count is int?(int)count:(long)count;if(cycles<0||cycles>Int32.MaxValue)return;
        if(lastRecorded>=0&&(elapsedMilliseconds-lastRecorded<5000||(lastClip==clip&&lastCycles==cycles&&lastAvailable==(bool)available&&lastFailed==(bool)failed)))return;
        lastClip=clip;lastCycles=cycles;lastAvailable=(bool)available;lastFailed=(bool)failed;lastRecorded=elapsedMilliseconds;
        record(clip,cycles,(bool)available,(bool)failed,elapsedMilliseconds);
    }catch{} }
}
internal sealed class PreparationSnapshotRecorder {
    readonly Action<PreparationDiagnostics.Snapshot,long,int,int> record;
    PreparationDiagnostics.Snapshot last;long lastRecorded=-1;int count;bool finished;
    internal PreparationSnapshotRecorder():this(PreparationDiagnostics.RecordPreparationSnapshot){}
    internal PreparationSnapshotRecorder(Action<PreparationDiagnostics.Snapshot,long,int,int> record){this.record=record;}
    internal void Observe(Dictionary<string,object> status,string expectedGeneration,long elapsedMilliseconds){
        try{
            if(finished)return;
            var next=PreparationDiagnostics.CapturePreparationSnapshot(status,expectedGeneration,elapsedMilliseconds);if(next==null)return;last=next;
            // Reserve one slot for the final cached state even if a future
            // preparation budget exceeds today's 8.5 seconds.
            if(count>=7||(lastRecorded>=0&&elapsedMilliseconds-lastRecorded<2000))return;
            lastRecorded=elapsedMilliseconds;count++;record(last,elapsedMilliseconds,0,count);
        }catch{} // Diagnostics never change preparation or recovery decisions.
    }
    internal void Finish(int kind,long elapsedMilliseconds){
        try{if(finished||kind<1||kind>2)return;finished=true;if(last==null||count>=8)return;count++;record(last,elapsedMilliseconds,kind,count);}catch{}
    }
}
internal static class Program {
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe,out uint pid);
    static string directory=AppDomain.CurrentDomain.BaseDirectory;
    static string Discovery {get{return Path.Combine(directory,"wallpaper-host-session.json");}}
    internal static bool Existing(string exe,out int pid,out long created){
        pid=0;created=0;bool unknown=false;
        foreach(var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe))){using(process){try{
            if(process.HasExited)continue;
            if(String.Equals(Path.GetFullPath(process.MainModule.FileName),exe,StringComparison.OrdinalIgnoreCase)){
                // Any official descendant blocks a second launch. Prefer a
                // main-window PID for the status hint, but never require HWND.
                if(pid==0||process.MainWindowHandle!=IntPtr.Zero){pid=process.Id;created=Native.Creation(process.Handle);}
            }
        }catch{try{if(!process.HasExited)unknown=true;}catch{unknown=true;}}}}
        if(pid!=0)return true;if(unknown)throw new HostFailure(53);return false;
    }
    static Mutex LaunchLock(string session,bool isolated){
        var security=new MutexSecurity();security.SetAccessRuleProtection(true,false);using(var identity=WindowsIdentity.GetCurrent()){security.AddAccessRule(new MutexAccessRule(identity.User,MutexRights.FullControl,AccessControlType.Allow));}bool created;
        return new Mutex(false,"Local\\CodexDragon.Wallpaper.Launch."+ControlPipe.SidHash+(isolated?"."+session:""),out created,security);
    }
    static void GateIsolated(){
        // The outer supervisor assigns this host to its Job BEFORE sending START.
        // This gate is never used in a normal production launch.
        Task<string> start=Task.Factory.StartNew(()=>Console.In.ReadLine());
        if(!start.Wait(10000)||start.Result!="START")throw new HostFailure(54);
    }
    static void Publish(string session,HostState state){ControlPipe.PrivateFile(Discovery,Json.Serializer().Serialize(Json.Obj("version",1,"session",session,"hostPid",state.HostPid,"hostCreationFileTime",state.HostCreated)));}
    static void RemoveDiscovery(string session,HostState state){try{
        if(!File.Exists(Discovery)||new FileInfo(Discovery).Length>1024)return;
        var m=Json.Map(Json.Serializer().DeserializeObject(File.ReadAllText(Discovery,Encoding.UTF8)));
        if(Json.Text(m,"session")==session&&Json.Number(m,"hostPid")==state.HostPid&&Json.Number(m,"hostCreationFileTime")==state.HostCreated)File.Delete(Discovery);
    }catch{}}
    static void StopWallpaper(){
        string file=Files.Regular(Discovery);if(new FileInfo(file).Length>1024)throw new HostFailure(55);
        var m=Json.Map(Json.Serializer().DeserializeObject(File.ReadAllText(file,Encoding.UTF8)));if(Json.Number(m,"version")!=1)throw new HostFailure(55);
        string session=Json.Text(m,"session");int pid=checked((int)Json.Number(m,"hostPid"));long created=Json.Number(m,"hostCreationFileTime");
        using(var process=Process.GetProcessById(pid))using(var currentProcess=Process.GetCurrentProcess()){
            if(Native.Creation(process.Handle)!=created||!String.Equals(Path.GetFullPath(process.MainModule.FileName),Path.GetFullPath(currentProcess.MainModule.FileName),StringComparison.OrdinalIgnoreCase))throw new HostFailure(55);
            using(var pipe=new NamedPipeClientStream(".",ControlPipe.Name(session),PipeDirection.InOut,PipeOptions.Asynchronous)){
                pipe.Connect(1000);uint server;if(!GetNamedPipeServerProcessId(pipe.SafePipeHandle,out server)||server!=(uint)pid)throw new HostFailure(55);
                byte[] bytes=Encoding.UTF8.GetBytes("disable\n");Task write=Task.Factory.FromAsync(pipe.BeginWrite,pipe.EndWrite,bytes,0,bytes.Length,null);if(!write.Wait(1000))throw new HostFailure(55);
                var response=new byte[512];Task<int> read=Task<int>.Factory.FromAsync(pipe.BeginRead,pipe.EndRead,response,0,response.Length,null);if(!read.Wait(1000)||read.Result==0)throw new HostFailure(55);
            }
        }
    }
    [STAThread] static int Main(string[] args){
        var startupClock=Stopwatch.StartNew();int startupPhase=20;PreparationDiagnostics.RecordEvent(startupPhase,0,0,null);
        if(args.Length==1&&args[0]=="--stop-wallpaper"){try{StopWallpaper();PreparationDiagnostics.RecordEvent(33,startupClock.ElapsedMilliseconds,0,null);return 0;}catch(Exception error){PreparationDiagnostics.RecordEvent(33,startupClock.ElapsedMilliseconds,55,error);return 55;}}
        if(args.Length!=3||(args[0]!="--launch"&&args[0]!="--isolated-test")||args[1]!="--session"||!Regex.IsMatch(args[2],"^[a-f0-9]{32}$")){PreparationDiagnostics.RecordEvent(34,startupClock.ElapsedMilliseconds,56,null);return 56;}
        bool isolated=args[0]=="--isolated-test";string session=args[2];var state=new HostState();OwnedClient owned=null;ControlPipe control=null;Mutex launch=null;bool locked=false,published=false;
        try{
            startupPhase=21;if(isolated)GateIsolated();
            startupPhase=22;control=new ControlPipe(state,session);control.Start();
            startupPhase=23;launch=LaunchLock(session,isolated);try{locked=launch.WaitOne(0);}catch(AbandonedMutexException){locked=true;}
            if(!locked){PreparationDiagnostics.RecordEvent(startupPhase,startupClock.ElapsedMilliseconds,57,null);state.MediaError(57);state.TerminalRead.WaitOne(10000);return 57;} // UNKNOWN, never signals safe fallback.
            startupPhase=24;try{if(!isolated){Publish(session,state);published=true;}}catch(Exception error){PreparationDiagnostics.RecordEvent(startupPhase,startupClock.ElapsedMilliseconds,55,error);} // Discovery failure never affects owned app.
            startupPhase=25;PreparationDiagnostics.RecordEvent(startupPhase,startupClock.ElapsedMilliseconds,0,null);Configuration config=Configuration.Load(directory);
            if(config.AlternateDiagnostic!=0)PreparationDiagnostics.RecordEvent(startupPhase,startupClock.ElapsedMilliseconds,config.AlternateDiagnostic,null);
            startupPhase=26;PreparationDiagnostics.RecordEvent(startupPhase,startupClock.ElapsedMilliseconds,0,null);
            int existingPid;long existingCreated;
            if(!isolated&&Existing(config.Exe,out existingPid,out existingCreated)){PreparationDiagnostics.RecordEvent(startupPhase,startupClock.ElapsedMilliseconds,3,null);state.Existing(existingPid,existingCreated);state.TerminalRead.WaitOne(10000);Thread.Sleep(150);return 0;}
            string isolatedRoot=null;
            if(isolated){isolatedRoot=Path.Combine(directory,"isolated-"+session);if(Directory.Exists(isolatedRoot))throw new HostFailure(58);Directory.CreateDirectory(isolatedRoot);Directory.CreateDirectory(Path.Combine(isolatedRoot,"electron-profile"));Directory.CreateDirectory(Path.Combine(isolatedRoot,"codex-home"));}
            startupPhase=27;var env=HostEnvironment.Build(isolatedRoot);
            if(!isolated&&Existing(config.Exe,out existingPid,out existingCreated)){PreparationDiagnostics.RecordEvent(startupPhase,startupClock.ElapsedMilliseconds,3,null);state.Existing(existingPid,existingCreated);state.TerminalRead.WaitOne(10000);Thread.Sleep(150);return 0;}
            startupPhase=28;PreparationDiagnostics.RecordEvent(startupPhase,startupClock.ElapsedMilliseconds,0,null);owned=OwnedClient.Start(config.Exe,env,isolatedRoot==null?null:Path.Combine(isolatedRoot,"electron-profile"),state.Started,
                ()=>{lock(state.Gate){return state.Disabled||state.MediaCode>=100?2:state.RevealEverRequested?1:0;}},
                (reason,elapsed)=>PreparationDiagnostics.Record(15,elapsed,200+reason));
            state.Started(owned.Pid,owned.Created); // Idempotent ownership commit before any other fallible work.
            startupPhase=29;PreparationDiagnostics.RecordEvent(startupPhase,startupClock.ElapsedMilliseconds,0,null);var transport=new CdpTransport(owned,isolated);
            startupPhase=30;var preparationClock=Stopwatch.StartNew();
            var worker=new Thread(()=>{try{new WallpaperSession(transport,state,config,isolated).Run();}catch(HostFailure e){PreparationDiagnostics.RecordFailure(15,preparationClock.ElapsedMilliseconds,e);state.MediaError(100+e.Code);}catch(Exception error){PreparationDiagnostics.RecordFailure(15,preparationClock.ElapsedMilliseconds,error);state.MediaError(199);}}){IsBackground=true,Name="Wallpaper prepare/control"};worker.Start();
            var watchdog=new Timer(_=>{if(PreparationWatchdog.Expire(state,preparationClock.ElapsedMilliseconds))PreparationDiagnostics.Record(14,preparationClock.ElapsedMilliseconds,1);},null,PreparationWatchdog.TimeoutMilliseconds,Timeout.Infinite);
            Timer isolatedClose=null;if(isolated)isolatedClose=new Timer(_=>{try{transport.Send("Browser.close",Json.Obj(),null,1000);}catch{}},null,35000,Timeout.Infinite);
            // This wait is intentional and uses no polling or foreground loop.
            // Recoverable renderer/control failures never close the CDP handles.
            startupPhase=31;PreparationDiagnostics.RecordEvent(startupPhase,startupClock.ElapsedMilliseconds,0,null);owned.Wait();watchdog.Dispose();if(isolatedClose!=null)isolatedClose.Dispose();return 0;
        }catch(HostFailure e){
            PreparationDiagnostics.RecordEvent(startupPhase,startupClock.ElapsedMilliseconds,e.Code,e);
            if(owned!=null){state.MediaError(100+e.Code);owned.Wait();}else{state.NoStart(e.Code);state.TerminalRead.WaitOne(10000);Thread.Sleep(150);}return e.Code;
        }catch(Exception error){
            PreparationDiagnostics.RecordEvent(startupPhase,startupClock.ElapsedMilliseconds,59,error);
            if(owned!=null){state.MediaError(199);owned.Wait();}else{state.NoStart(59);state.TerminalRead.WaitOne(10000);Thread.Sleep(150);}return 59;
        }finally{
            PreparationDiagnostics.RecordEvent(32,startupClock.ElapsedMilliseconds,0,null);
            // At this point a successfully spawned official process has exited.
            if(owned!=null)owned.Dispose();if(control!=null)control.Dispose();if(published)RemoveDiscovery(session,state);if(locked&&launch!=null)try{launch.ReleaseMutex();}catch{}if(launch!=null)launch.Dispose();
        }
    }
}
}

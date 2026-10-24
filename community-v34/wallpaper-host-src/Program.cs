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
    internal CodexDragon.Official.OfficialIdentitySnapshot Official;
    internal static bool AutomaticPolicy(Dictionary<string,object> value){
        return Json.Number(value,"version")==2&&Json.Text(value,"officialFamilyName")=="OpenAI.Codex_2p2nqsd0c76g0"&&
            Json.Text(value,"officialIdentityMode")=="registered-signed"&&!value.ContainsKey("packageFullName")&&!value.ContainsKey("executableSha256");
    }
    internal void ReleaseOfficial(){if(Official!=null){Official.Dispose();Official=null;}}
    internal void VerifyStarted(int pid,long created){
        // The Host alone selects the cold-start executable. Subsequent layers
        // attest the exact child generation, never reselect a newer package.
        if(Official==null)throw new HostFailure(60);
        using(var actual=CodexDragon.Official.OfficialIdentityResolver.VerifyProcess(pid,created)){
            if(!String.Equals(actual.Executable,Official.Executable,StringComparison.OrdinalIgnoreCase)||
                !String.Equals(actual.Sha256,Official.Sha256,StringComparison.OrdinalIgnoreCase)||
                actual.PackageFullName!=Official.PackageFullName)throw new HostFailure(60);
        }
    }
    internal string[] MediaFiles {get{return String.IsNullOrEmpty(AlternateMedia)?new[]{Media}:new[]{Media,AlternateMedia};}}
    internal static Configuration Load(string directory){
        string file=Files.Regular(Path.Combine(directory,"wallpaper-host-config.json"));if(new FileInfo(file).Length>16384)throw new HostFailure(50);
        var m=Json.Map(Json.Serializer().DeserializeObject(File.ReadAllText(file,Encoding.UTF8)));
        if(!AutomaticPolicy(m))throw new HostFailure(50);
        string rendererHash=Json.Text(m,"rendererSha256");
        if(!ValidHash(rendererHash))throw new HostFailure(50);
        string renderer=Files.Regular(Path.Combine(directory,"renderer.js"));if(new FileInfo(renderer).Length>131072||Files.Hash(renderer)!=rendererHash.ToLowerInvariant())throw new HostFailure(51);
        string media=Files.Regular(Json.Text(m,"mediaPath"));
        long mediaBytes=Json.Number(m,"mediaBytes");string mediaHash=Json.Text(m,"mediaSha256");
        if(Path.GetFileName(media)!="dragon-wallpaper-ambient-2k-144fps.mp4"||mediaBytes<=0||mediaBytes>256L*1024*1024||!ValidHash(mediaHash)||new FileInfo(media).Length!=mediaBytes||Files.Hash(media)!=mediaHash.ToLowerInvariant())throw new HostFailure(52);
        object enabled=Json.Get(m,"wallpaperEnabled");if(!(enabled is bool))throw new HostFailure(50);
        int alternateCode;string alternate=LoadAlternateMedia(m,out alternateCode);
        // Optional-media damage must not disable the established wallpaper.
        // 253=present but rejected, 254=verified; absence is legacy-compatible.
        var result=new Configuration{Renderer=File.ReadAllText(renderer,Encoding.UTF8),Media=media,AlternateMedia=alternate,AlternateDiagnostic=alternateCode,Enabled=(bool)enabled};
        try{
            result.Official=CodexDragon.Official.OfficialIdentityResolver.ResolveCurrent();
            result.Exe=result.Official.Executable;
            // These markers admit only the supported Electron family. The
            // existing bounded CDP/DOM/media probes remain authoritative.
            string app=Path.GetDirectoryName(result.Exe);
            if(!File.Exists(Path.Combine(app,"chrome.dll"))||!File.Exists(Path.Combine(app,"resources","app.asar")))throw new HostFailure(61);
            PreparationDiagnostics.RecordEvent(44,0,1,null);
            return result;
        }catch{result.ReleaseOfficial();throw;}
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
    static readonly string[] LayoutReasons={"not-checked","root-missing","main-count","main-token","workspace-shape","shell-shape","composer-ambiguous","frame-count","frame-token","frame-row","frame-shape","ready"};
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
        fields.Append(" layoutReason=").Append(SnapshotEnum(SnapshotValue(status,"layoutReason"),LayoutReasons));
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
    internal static string FormatLoss(int sourcePhase,long elapsed,string kind,int fault,int exited){
        if(sourcePhase<0||sourcePhase>63||fault< -1||fault>99||exited< -1||exited>1||Array.IndexOf(new[]{"transport-fault","target-detached","target-crashed","inspector-detached","unknown"},kind)<0)throw new ArgumentOutOfRangeException();
        return FormatEvent(46,elapsed,0,null).TrimEnd('\r','\n')+String.Format(System.Globalization.CultureInfo.InvariantCulture," sourcePhase={0} lossKind={1} transportFaultCode={2} processExited={3}",sourcePhase,kind,fault,exited)+Environment.NewLine;
    }
    internal static void RecordLoss(int sourcePhase,long elapsed,string kind,int fault,int exited){try{Enqueue(FormatLoss(sourcePhase,elapsed,kind,fault,exited));}catch{}}
    internal static string FormatProcessExit(long elapsed,bool known,uint code){
        return FormatEvent(47,elapsed,0,null).TrimEnd('\r','\n')+" processExited=1 exitCodeKnown="+(known?"1":"0")+" processExitCode="+(known?code:0).ToString(System.Globalization.CultureInfo.InvariantCulture)+Environment.NewLine;
    }
    internal static void RecordProcessExit(long elapsed,bool known,uint code){try{Enqueue(FormatProcessExit(elapsed,known,code));}catch{}}
    internal static string FormatInitialVisibility(int outcome,long elapsed,int reason,int hidden,int visible,bool markerOwned,bool complete=true){
        if(outcome<1||outcome>8||reason<0||reason>6||hidden<0||hidden>129||visible<0||visible>129)throw new ArgumentOutOfRangeException();
        return FormatEvent(49,elapsed,outcome,null).TrimEnd('\r','\n')+String.Format(System.Globalization.CultureInfo.InvariantCulture," releaseReason={0} eligibleHidden={1} visible={2} markerOwned={3} observationComplete={4}",reason,hidden,visible,markerOwned?1:0,complete?1:0)+Environment.NewLine;
    }
    internal static void RecordInitialVisibility(int outcome,long elapsed,int reason,int hidden,int visible,bool markerOwned,bool complete=true){try{Enqueue(FormatInitialVisibility(outcome,elapsed,reason,hidden,visible,markerOwned,complete));}catch{}}
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
    internal static string FormatTargetRequest(int kind,long elapsed,int budget,int code,int timeoutStage,long bytes,long frames,long matched,long late){
        if(kind<1||kind>2||budget<1||budget>12000||timeoutStage<0||timeoutStage>3||bytes<0||frames<0||matched<0||late<0)throw new ArgumentOutOfRangeException();
        var culture=System.Globalization.CultureInfo.InvariantCulture;
        // No CDP payload, URL, target/session identifier or exception text.
        return FormatEvent(36,elapsed,code,null).TrimEnd('\r','\n')+
            " commandKind="+kind.ToString(culture)+" requestBudgetMs="+budget.ToString(culture)+" timeoutStage="+timeoutStage.ToString(culture)+
            " bytesReceived="+bytes.ToString(culture)+" framesReceived="+frames.ToString(culture)+
            " matchedResponses="+matched.ToString(culture)+" lateResponses="+late.ToString(culture)+Environment.NewLine;
    }
    internal static void RecordTargetRequest(int kind,long elapsed,int budget,int code,int timeoutStage,CdpTransport transport){
        try{Enqueue(FormatTargetRequest(kind,elapsed,budget,code,timeoutStage,transport.BytesReceived,transport.FramesReceived,transport.MatchedResponses,transport.LateResponses));}catch{}
    }
    internal static string FormatPreparationRead(int sourcePhase,int kind,long elapsed,int budget,int code,int timeoutStage,long bytes,long frames,long matched,long late){
        if((sourcePhase!=2&&sourcePhase!=6&&sourcePhase!=10)||kind<1||kind>3||budget<1||budget>1500||timeoutStage<0||timeoutStage>3||bytes<0||frames<0||matched<0||late<0)throw new ArgumentOutOfRangeException();
        var c=System.Globalization.CultureInfo.InvariantCulture;
        return FormatEvent(37,elapsed,code,null).TrimEnd('\r','\n')+" sourcePhase="+sourcePhase.ToString(c)+" observationKind="+kind.ToString(c)+
            " requestBudgetMs="+budget.ToString(c)+" timeoutStage="+timeoutStage.ToString(c)+" bytesReceived="+bytes.ToString(c)+
            " framesReceived="+frames.ToString(c)+" matchedResponses="+matched.ToString(c)+" lateResponses="+late.ToString(c)+Environment.NewLine;
    }
    internal static void RecordPreparationRead(int sourcePhase,int kind,long elapsed,int budget,int code,int timeoutStage,CdpTransport transport){
        try{Enqueue(FormatPreparationRead(sourcePhase,kind,elapsed,budget,code,timeoutStage,transport.BytesReceived,transport.FramesReceived,transport.MatchedResponses,transport.LateResponses));}catch{}
    }
    internal static string FormatReadinessObservation(int sourcePhase,long elapsed,Dictionary<string,object> status,string generation){
        if(sourcePhase!=2&&sourcePhase!=6&&sourcePhase!=10)throw new ArgumentOutOfRangeException();
        var fields=new StringBuilder(FormatEvent(38,elapsed,0,null).TrimEnd('\r','\n'));
        fields.Append(" sourcePhase=").Append(sourcePhase.ToString(System.Globalization.CultureInfo.InvariantCulture));
        fields.Append(" layoutReason=").Append(SnapshotEnum(SnapshotValue(status,"layoutReason"),LayoutReasons));
        foreach(string key in new[]{"rootReady","bodyReady","layoutSupported","documentHidden","attached","mediaReady","prepared","disposed","firstFrameConfirmed","layoutWindowElectron"})fields.Append(' ').Append(key).Append('=').Append(SnapshotBoolean(SnapshotValue(status,key)));
        fields.Append(" layoutSurfaceKind=").Append(SnapshotEnum(SnapshotValue(status,"layoutSurfaceKind"),new[]{"none","main","frame"}));
        foreach(string key in new[]{"layoutMainCount","layoutActiveMainCount","layoutFrameCount","layoutActiveFrameCount","layoutWorkspaceRowCount"})fields.Append(' ').Append(key).Append('=').Append(SnapshotNumber(SnapshotValue(status,key),129));
        object actual=SnapshotValue(status,"documentGeneration");fields.Append(" generationMatches=").Append(actual is string&&generation!=null?((string)actual==generation?"1":"0"):"-1");
        return fields.Append(Environment.NewLine).ToString();
    }
    internal static void RecordReadinessObservation(int sourcePhase,long elapsed,Dictionary<string,object> status,string generation){
        // Both records share ReadPreparation's existing one-second throttle.
        // Split the fixed shape audit from phase 38 to retain the 1 KiB sink
        // limit; neither call performs another renderer request or measurement.
        try{Enqueue(FormatReadinessObservation(sourcePhase,elapsed,status,generation));Enqueue(FormatLayoutAudit(sourcePhase,elapsed,status));}catch{}
    }
    static readonly string[] LayoutAuditCounts={"layoutAuditFrameCount","layoutAuditActiveFrameCount","layoutAuditAnyMainCount","layoutAuditActiveAnyMainCount","layoutAuditPageSurfaceFrameCount","layoutAuditDirectRowCount","layoutAuditDecorationCount","layoutAuditComposerCount"};
    static readonly string[] LayoutAuditFlags={"layoutAuditLayoutTokenValid","layoutAuditPageLayoutTokenValid","layoutAuditFrameHashMatches","layoutAuditPageSurfaceFlag","layoutAuditDecorationValid","layoutAuditComposerAmbiguous"};
    internal static string FormatLayoutAudit(int sourcePhase,long elapsed,Dictionary<string,object> status){
        if((sourcePhase!=2&&sourcePhase!=6&&sourcePhase!=10&&sourcePhase!=12)||elapsed<0||elapsed>31536000000L)throw new ArgumentOutOfRangeException();
        var fields=new StringBuilder(FormatEvent(45,elapsed,0,null).TrimEnd('\r','\n'));
        fields.Append(" sourcePhase=").Append(sourcePhase.ToString(System.Globalization.CultureInfo.InvariantCulture));
        fields.Append(" layoutReason=").Append(SnapshotEnum(SnapshotValue(status,"layoutReason"),LayoutReasons));
        fields.Append(" layoutSurfaceKind=").Append(SnapshotEnum(SnapshotValue(status,"layoutSurfaceKind"),new[]{"none","main","frame"}));
        // The renderer protocol uses numeric flags; arbitrary strings, booleans,
        // non-integral and out-of-range values remain unknown (-1), not false.
        foreach(string key in LayoutAuditCounts)fields.Append(' ').Append(key).Append('=').Append(SnapshotNumber(SnapshotValue(status,key),129));
        foreach(string key in LayoutAuditFlags)fields.Append(' ').Append(key).Append('=').Append(SnapshotNumber(SnapshotValue(status,key),1));
        string line=fields.Append(Environment.NewLine).ToString();
        if(line.Length>1024)throw new ArgumentOutOfRangeException();return line;
    }
    internal static string FormatRuntimeObservation(Dictionary<string,object> status,string generation,long elapsed){
        var snapshot=CapturePreparationSnapshot(status,generation,elapsed);
        if(snapshot==null)throw new ArgumentOutOfRangeException();
        return FormatEvent(39,elapsed,0,null).TrimEnd('\r','\n')+snapshot.Fields+
            " commitAccepted="+SnapshotBoolean(SnapshotValue(status,"commitAccepted"))+Environment.NewLine;
    }
    internal static string FormatPlaybackQuality(Dictionary<string,object> status,long elapsed){
        if(status==null)throw new ArgumentNullException("status");
        var line=new StringBuilder(FormatEvent(40,elapsed,0,null).TrimEnd('\r','\n'));
        foreach(string key in new[]{"videoTotalFrames","videoDroppedFrames","videoCorruptedFrames","mediaGeneration","presentedFrames"})line.Append(' ').Append(key).Append('=').Append(SnapshotNumber(SnapshotValue(status,key),Int32.MaxValue));
        foreach(string key in new[]{"videoWidth","videoHeight"})line.Append(' ').Append(key).Append('=').Append(SnapshotNumber(SnapshotValue(status,key),16384));
        return line.Append(Environment.NewLine).ToString();
    }
    internal static void RecordRuntimeObservation(Dictionary<string,object> status,string generation,long elapsed){
        // Reuse the existing runtime snapshot and rate limit. Route failures
        // after preparation need the same structural evidence as cold start.
        try{Enqueue(FormatRuntimeObservation(status,generation,elapsed));Enqueue(FormatPlaybackQuality(status,elapsed));Enqueue(FormatLayoutAudit(12,elapsed,status));}catch{}
    }
    internal static string FormatRuntimeRequest(int kind,long elapsed,long requestElapsed,int budget,HostFailure error){
        if(kind<1||kind>4||elapsed<0||elapsed>31536000000L||requestElapsed<0||requestElapsed>31536000000L||budget<1||budget>750||error==null||error.TimeoutStage<0||error.TimeoutStage>3)throw new ArgumentOutOfRangeException();
        var c=System.Globalization.CultureInfo.InvariantCulture;
        return FormatEvent(41,elapsed,100+error.Code,error).TrimEnd('\r','\n')+" operationKind="+kind.ToString(c)+
            " requestElapsedMs="+requestElapsed.ToString(c)+" requestBudgetMs="+budget.ToString(c)+" timeoutStage="+error.TimeoutStage.ToString(c)+Environment.NewLine;
    }
    internal static void RecordRuntimeRequest(int kind,long elapsed,long requestElapsed,int budget,HostFailure error){
        try{Enqueue(FormatRuntimeRequest(kind,elapsed,requestElapsed,budget,error));}catch{}
    }
    internal static void RecordMemoryPressure(string pressureState,long elapsed,long availableCommitBytes,long totalCommitBytes){
        try{
            if((pressureState!="suspended"&&pressureState!="resume-requested")||elapsed<0||elapsed>31536000000L)return;
            // This is the caller's pressure sample and command acknowledgement,
            // not a new measurement or confirmation that playback has resumed.
            Enqueue(FormatEvent(43,elapsed,0,null).TrimEnd('\r','\n')+" pressureState="+pressureState+
                " availableCommitBytes="+SnapshotNumber(availableCommitBytes,281474976710656L)+
                " totalCommitBytes="+SnapshotNumber(totalCommitBytes,281474976710656L)+Environment.NewLine);
        }catch{}
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
    static void ResetWriter(){lock(writerGate){dropped=Math.Min(65535,dropped+pending.Count);pending.Clear();writerBusy=0;Monitor.PulseAll(writerGate);}}
    internal static void FlushOnExit(){
        // Only process termination waits for its last numeric records. Never
        // block startup or synchronously perform disk IO on the main thread.
        var clock=Stopwatch.StartNew();lock(writerGate){while(writerBusy!=0){int left=Deadline.Remaining(200,clock.ElapsedMilliseconds);if(left<=0)return;Monitor.Wait(writerGate,left);}}
    }
    static void Drain(){
        try{for(;;){
            string line;
            lock(writerGate){
                if(pending.Count!=0)line=pending.Dequeue();
                // Phase zero is reserved for numeric queue-overflow evidence.
                // No exception, content, path, or session identifier is logged.
                else if(dropped!=0){line=FormatEvent(0,0,dropped,null);dropped=0;}
                else{writerBusy=0;Monitor.PulseAll(writerGate);return;}
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
internal sealed class RuntimeSnapshotRecorder {
    readonly Action<Dictionary<string,object>,string,long> record;
    string lastState;long lastRecorded=-1;
    internal RuntimeSnapshotRecorder():this(PreparationDiagnostics.RecordRuntimeObservation){}
    internal RuntimeSnapshotRecorder(Action<Dictionary<string,object>,string,long> record){this.record=record;}
    internal void Observe(Dictionary<string,object> status,string generation,long elapsed){try{
        if(status==null||elapsed<0||elapsed>31536000000L)return;
        string signature=Json.Text(status,"actualPhase")+"|"+Json.Text(status,"health")+"|"+Json.Text(status,"code")+"|"+
            Json.Bool(status,"commitAccepted")+"|"+Json.Bool(status,"firstFrameConfirmed")+"|"+Json.Bool(status,"revealed")+"|"+
            Json.Bool(status,"documentHidden")+"|"+Json.Bool(status,"memoryPressure")+"|"+Json.Bool(status,"wallpaperApplied")+"|"+
            Json.Bool(status,"layoutSupported")+"|"+Json.Text(status,"layoutReason")+"|"+Json.Text(status,"layoutSurfaceKind");
        // Reuse already validated observations. No timer or extra CDP request.
        // At most one trio per 2 s on transitions, or per 30 s in steady state.
        if(lastRecorded>=0&&(elapsed-lastRecorded<2000||(signature==lastState&&elapsed-lastRecorded<30000)))return;
        lastRecorded=elapsed;lastState=signature;record(status,generation,elapsed);
    }catch{}}
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
    internal static bool ExistingOfficial(out int pid,out long created){
        pid=0;created=0;
        var snapshots=CodexDragon.Official.OfficialIdentityResolver.FindRunning();
        try{if(snapshots.Length==0)return false;pid=snapshots[0].Pid;created=snapshots[0].Created;return true;}
        finally{foreach(var snapshot in snapshots)snapshot.Dispose();}
    }
    internal static bool Existing(string exe,out int pid,out long created){
        pid=0;created=0;bool unknown=false;
        int ownSession;using(var current=Process.GetCurrentProcess()){ownSession=current.SessionId;}
        foreach(var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe))){using(process){try{
            if(process.HasExited||process.SessionId!=ownSession)continue;
            if(String.Equals(Path.GetFullPath(process.MainModule.FileName),exe,StringComparison.OrdinalIgnoreCase)){
                // Any official descendant in this session blocks a second launch. Prefer a
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
        bool isolated=args[0]=="--isolated-test";string session=args[2];var state=new HostState();OwnedClient owned=null;ControlPipe control=null;Mutex launch=null;Configuration config=null;bool locked=false,published=false;
        try{
            startupPhase=21;if(isolated)GateIsolated();
            startupPhase=22;control=new ControlPipe(state,session);control.Start();
            startupPhase=23;launch=LaunchLock(session,isolated);try{locked=launch.WaitOne(0);}catch(AbandonedMutexException){locked=true;}
            if(!locked){PreparationDiagnostics.RecordEvent(startupPhase,startupClock.ElapsedMilliseconds,57,null);state.MediaError(57);state.TerminalRead.WaitOne(10000);return 57;} // UNKNOWN, never signals safe fallback.
            startupPhase=24;try{if(!isolated){Publish(session,state);published=true;}}catch(Exception error){PreparationDiagnostics.RecordEvent(startupPhase,startupClock.ElapsedMilliseconds,55,error);} // Discovery failure never affects owned app.
            startupPhase=25;PreparationDiagnostics.RecordEvent(startupPhase,startupClock.ElapsedMilliseconds,0,null);config=Configuration.Load(directory);
            if(config.AlternateDiagnostic!=0)PreparationDiagnostics.RecordEvent(startupPhase,startupClock.ElapsedMilliseconds,config.AlternateDiagnostic,null);
            startupPhase=26;PreparationDiagnostics.RecordEvent(startupPhase,startupClock.ElapsedMilliseconds,0,null);
            int existingPid;long existingCreated;
            if(!isolated&&ExistingOfficial(out existingPid,out existingCreated)){config.ReleaseOfficial();PreparationDiagnostics.RecordEvent(44,startupClock.ElapsedMilliseconds,3,null);PreparationDiagnostics.RecordEvent(startupPhase,startupClock.ElapsedMilliseconds,3,null);state.Existing(existingPid,existingCreated);state.TerminalRead.WaitOne(10000);Thread.Sleep(150);return 0;}
            string isolatedRoot=null;
            if(isolated){isolatedRoot=Path.Combine(directory,"isolated-"+session);if(Directory.Exists(isolatedRoot))throw new HostFailure(58);Directory.CreateDirectory(isolatedRoot);Directory.CreateDirectory(Path.Combine(isolatedRoot,"electron-profile"));Directory.CreateDirectory(Path.Combine(isolatedRoot,"codex-home"));}
            startupPhase=27;var env=HostEnvironment.Build(isolatedRoot);
            if(!isolated&&ExistingOfficial(out existingPid,out existingCreated)){config.ReleaseOfficial();PreparationDiagnostics.RecordEvent(44,startupClock.ElapsedMilliseconds,3,null);PreparationDiagnostics.RecordEvent(startupPhase,startupClock.ElapsedMilliseconds,3,null);state.Existing(existingPid,existingCreated);state.TerminalRead.WaitOne(10000);Thread.Sleep(150);return 0;}
            startupPhase=28;PreparationDiagnostics.RecordEvent(startupPhase,startupClock.ElapsedMilliseconds,0,null);owned=OwnedClient.Start(config.Exe,env,isolatedRoot==null?null:Path.Combine(isolatedRoot,"electron-profile"),state.Started,
                ()=>{lock(state.Gate){return state.Disabled||state.MediaCode>=100?2:state.RevealEverRequested?1:0;}},
                (reason,elapsed)=>PreparationDiagnostics.Record(15,elapsed,200+reason));
            state.Started(owned.Pid,owned.Created); // Idempotent ownership commit before any other fallible work.
            try{config.VerifyStarted(owned.Pid,owned.Created);PreparationDiagnostics.RecordEvent(44,startupClock.ElapsedMilliseconds,2,null);}finally{config.ReleaseOfficial();}
            startupPhase=29;PreparationDiagnostics.RecordEvent(startupPhase,startupClock.ElapsedMilliseconds,0,null);var transport=new CdpTransport(owned,isolated);
            startupPhase=30;var preparationClock=Stopwatch.StartNew();
            var worker=new Thread(()=>{try{new WallpaperSession(transport,state,config,isolated).Run();}catch(HostFailure e){PreparationDiagnostics.RecordFailure(15,preparationClock.ElapsedMilliseconds,e);state.MediaError(100+e.Code);}catch(Exception error){PreparationDiagnostics.RecordFailure(15,preparationClock.ElapsedMilliseconds,error);state.MediaError(199);}}){IsBackground=true,Name="Wallpaper prepare/control"};worker.Start();
            var watchdog=new Timer(_=>{if(PreparationWatchdog.Expire(state,preparationClock.ElapsedMilliseconds))PreparationDiagnostics.Record(14,preparationClock.ElapsedMilliseconds,1);},null,PreparationWatchdog.TimeoutMilliseconds,Timeout.Infinite);
            Timer isolatedClose=null;if(isolated)isolatedClose=new Timer(_=>{try{transport.Send("Browser.close",Json.Obj(),null,1000);}catch{}},null,35000,Timeout.Infinite);
            // This wait is intentional and uses no polling or foreground loop.
            // Recoverable renderer/control failures never close the CDP handles.
            startupPhase=31;PreparationDiagnostics.RecordEvent(startupPhase,startupClock.ElapsedMilliseconds,0,null);owned.Wait();watchdog.Dispose();if(isolatedClose!=null)isolatedClose.Dispose();return 0;
        }catch(HostFailure e){
            if(e.Code==60||e.Code==61)PreparationDiagnostics.RecordEvent(44,startupClock.ElapsedMilliseconds,e.Code==61?14:13,e);
            PreparationDiagnostics.RecordEvent(startupPhase,startupClock.ElapsedMilliseconds,e.Code,e);
            if(owned!=null){state.MediaError(100+e.Code);owned.Wait();}else{state.NoStart(e.Code);state.TerminalRead.WaitOne(10000);Thread.Sleep(150);}return e.Code;
        }catch(Exception error){
            PreparationDiagnostics.RecordEvent(44,startupClock.ElapsedMilliseconds,CodexDragon.Official.OfficialIdentityResolver.DiagnosticCode(error),error);
            PreparationDiagnostics.RecordEvent(startupPhase,startupClock.ElapsedMilliseconds,59,error);
            if(owned!=null){state.MediaError(199);owned.Wait();}else{state.NoStart(59);state.TerminalRead.WaitOne(10000);Thread.Sleep(150);}return 59;
        }finally{
            if(owned!=null&&owned.ProcessExitState==1){uint exitCode;bool known=owned.TryGetExitCode(out exitCode);PreparationDiagnostics.RecordProcessExit(startupClock.ElapsedMilliseconds,known,exitCode);}
            PreparationDiagnostics.RecordEvent(32,startupClock.ElapsedMilliseconds,0,null);
            if(config!=null)config.ReleaseOfficial();
            // At this point a successfully spawned official process has exited.
            if(owned!=null)owned.Dispose();if(control!=null)control.Dispose();if(published)RemoveDiscovery(session,state);if(locked&&launch!=null)try{launch.ReleaseMutex();}catch{}if(launch!=null)launch.Dispose();
            PreparationDiagnostics.FlushOnExit();
        }
    }
}
}

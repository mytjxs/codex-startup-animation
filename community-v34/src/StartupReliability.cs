using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace DragonLauncher
{
    // A replay is pinned to one verified existing native window and process
    // generation. It neither owns the client nor discovers a replacement if
    // that client closes while the optional animation is playing.
    internal sealed class ExistingReplayTarget
    {
        internal readonly IntPtr Window;
        private readonly uint pid;
        private readonly long created;
        private readonly string executable;
        private ExistingReplayTarget(IntPtr window,uint pid,long created,string executable)
        {Window=window;this.pid=pid;this.created=created;this.executable=executable;}
        internal static ExistingReplayTarget Find(Settings settings,string root)
        {
            IntPtr window=NativeHandoff.FindTarget(settings,0);
            if(window==IntPtr.Zero){
                // The official executable started by the wallpaper Host may
                // have no AppModel identity. Reuse existing read-only bridge
                // attestation to authorize that exact signed package instance.
                // Discovery sends only status; no prepare/commit/reveal/disable.
                using(var session=BridgeActivation.Discover(root,0,null)){
                    var identity=BridgeClientIdentity.Attest(settings,root,session);
                    if(identity!=null)NativeHandoff.RegisterBridgeTarget(identity);
                }
                window=NativeHandoff.FindTarget(settings,0);
            }
            if(window==IntPtr.Zero)return null;
            try {
                uint processId=BridgeActivation.WindowProcessId(window);
                if(processId==0||processId>Int32.MaxValue)return null;
                using(var process=System.Diagnostics.Process.GetProcessById((int)processId)){
                    long start;IntPtr handle=process.Handle;
                    if(!BridgeNative.IsAlive(handle)||!BridgeNative.CreationTime(handle,out start))return null;
                    var result=new ExistingReplayTarget(window,processId,start,BridgeNative.ImagePath(handle));
                    return result.IsCurrent?result:null;
                }
            }catch{return null;}
        }
        internal bool IsCurrent
        {
            get {
                if(Window==IntPtr.Zero||pid==0||pid>Int32.MaxValue||created<=0)return false;
                try {
                    if(!NativeHandoff.FindCurrentStillValid(Window)||BridgeActivation.WindowProcessId(Window)!=pid)return false;
                    using(var process=System.Diagnostics.Process.GetProcessById((int)pid)){
                        long start;IntPtr handle=process.Handle;
                        return BridgeNative.IsAlive(handle)&&BridgeNative.CreationTime(handle,out start)&&
                            BridgeClientIdentity.SameSnapshot(pid,created,executable,(uint)process.Id,start,BridgeNative.ImagePath(handle));
                    }
                }catch{return false;}
            }
        }
        internal uint Focus()
        {if(!IsCurrent)return 0;NativeHandoff.FocusReadyTarget(Window);return pid;}
    }

    // All project entry points use this gate before reading mutable installation
    // state. Setup releases it immediately after creating Launcher; Launcher
    // validates and retains it through its WPF lifetime and Host spawn. Host
    // does not acquire this gate. Never wait on a child that needs the same gate.
    internal sealed class MaintenanceLaunchLease : IDisposable
    {
        private Mutex mutex;
        internal readonly bool WasAbandoned;
        private MaintenanceLaunchLease(Mutex mutex,bool abandoned)
        {this.mutex=mutex;WasAbandoned=abandoned;}
        private static string ShortHash(string text)
        {
            using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-","").Substring(0,16);
        }
        internal static string NameFor(string root,string sid)
        {
            if(String.IsNullOrWhiteSpace(root)||String.IsNullOrWhiteSpace(sid))throw new ArgumentException("maintenance-identity-missing");
            return "Local\\CodexDragon.MaintenanceLaunch."+ShortHash(sid)+"."+ShortHash(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar).ToUpperInvariant());
        }
        internal static MaintenanceLaunchLease Acquire(string root,int milliseconds)
        {
            SecurityIdentifier sid;using(var identity=WindowsIdentity.GetCurrent()){sid=identity.User;}
            if(sid==null)throw new InvalidOperationException("maintenance-user-unavailable");
            var security=new MutexSecurity();security.SetAccessRuleProtection(true,false);
            security.AddAccessRule(new MutexAccessRule(sid,MutexRights.FullControl,AccessControlType.Allow));
            bool created;var gate=new Mutex(false,NameFor(root,sid.Value),out created,security);
            bool held=false,abandoned=false;
            try {
                try{held=gate.WaitOne(Math.Max(0,Math.Min(5000,milliseconds)));}
                catch(AbandonedMutexException){held=true;abandoned=true;}
                if(!held)throw new TimeoutException("installation-maintenance-in-progress");
                return new MaintenanceLaunchLease(gate,abandoned);
            }catch{if(held)gate.ReleaseMutex();gate.Dispose();throw;}
        }
        internal static void AssertInstallationReady(string root)
        {InstallationReadiness.Verify(root);}
        public void Dispose(){Mutex gate=Interlocked.Exchange(ref mutex,null);if(gate==null)return;try{gate.ReleaseMutex();}finally{gate.Dispose();}}
    }

    // A direct executable entry must not cross an interrupted maintenance
    // transaction. Call while holding MaintenanceLaunchLease. The updater owns
    // recovery; this check deliberately never tries to roll files forward.
    internal static class InstallationReadiness
    {
        [DataContract] private sealed class Journal {
            [DataMember] public int schemaVersion {get;set;}
            [DataMember] public string releaseId {get;set;} [DataMember] public string manifestSha256 {get;set;} [DataMember] public string phase {get;set;}
        }
        [DataContract] private sealed class Manifest {
            [DataMember] public int schemaVersion {get;set;}
            [DataMember] public string releaseId {get;set;} [DataMember] public string installedRoot {get;set;}
            [DataMember] public List<Item> files {get;set;}
        }
        [DataContract] private sealed class Item {
            [DataMember] public string relative {get;set;} [DataMember] public string beforeSha256 {get;set;} [DataMember] public string afterSha256 {get;set;}
        }
        private sealed class Release {
            internal int Version;
            internal string Id;
            internal Manifest Manifest;
            internal bool ProductAffected,Before;
        }
        private const int MaxReleaseDirectories=64,MaxProductFiles=512;
        private const long MaxProductFileBytes=32L*1024*1024,MaxProductBytes=128L*1024*1024;
        private static bool HashValid(string hash){if(hash==null||hash.Length!=64)return false;foreach(char c in hash)if(!Uri.IsHexDigit(c))return false;return true;}
        private static string Hash(string file,long limit,out long length)
        {
            using(var sha=SHA256.Create())using(var input=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read)){
                length=input.Length;if(length>limit)throw new InvalidDataException("maintenance-file-oversize");
                return BitConverter.ToString(sha.ComputeHash(input)).Replace("-","");
            }
        }
        private static string Inside(string root,string relative)
        {
            if(String.IsNullOrWhiteSpace(relative)||relative.Length>512||Path.IsPathRooted(relative)||relative.IndexOf(':')>=0)throw new InvalidDataException("maintenance-invalid-path");
            foreach(string part in relative.Replace('\\','/').Split('/'))if(part.Length==0||part=="."||part=="..")throw new InvalidDataException("maintenance-invalid-path");
            string path=Path.GetFullPath(Path.Combine(root,relative));
            if(!path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("maintenance-outside-root");
            for(string current=path;current!=null;current=Path.GetDirectoryName(current))
                if((File.Exists(current)||Directory.Exists(current))&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("maintenance-linked-path");
            return path;
        }
        private static T Read<T>(string path,int limit)
        {
            if(new FileInfo(path).Length>limit)throw new InvalidDataException("maintenance-record-oversize");
            using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)) {
                if(file.Length>limit)throw new InvalidDataException("maintenance-record-oversize");
                // PowerShell UTF-8 files can have a BOM; DataContract JSON does
                // not reliably consume it in every installed .NET build.
                var bytes=new byte[file.Length];int count=0;while(count<bytes.Length){int n=file.Read(bytes,count,bytes.Length-count);if(n<=0)throw new EndOfStreamException();count+=n;}
                int offset=bytes.Length>=3&&bytes[0]==239&&bytes[1]==187&&bytes[2]==191?3:0;
                using(var body=new MemoryStream(bytes,offset,bytes.Length-offset))return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(body);
            }
        }
        private static int ReleaseVersion(string name)
        {
            int suffix=name.LastIndexOf("-v",StringComparison.Ordinal),version;
            if(suffix<1||suffix+2>=name.Length||name[suffix+2]=='0')return -1;
            for(int i=0;i<suffix;i++)if(!((name[i]>='a'&&name[i]<='z')||(name[i]>='0'&&name[i]<='9')||name[i]=='-'))return -1;
            for(int i=suffix+2;i<name.Length;i++)if(name[i]<'0'||name[i]>'9')return -1;
            if(!Int32.TryParse(name.Substring(suffix+2),out version)||version>1000000)return -1;
            return version;
        }
        private static bool MarkerExists(string path)
        {
            // A directory in place of a record is corruption, not absence.
            if(Directory.Exists(path))throw new InvalidDataException("maintenance-record-invalid");
            return File.Exists(path);
        }
        private static Journal ReadJournal(string path,int limit,string releaseId,string phaseA,string phaseB,string failure)
        {
            if(!MarkerExists(path))return null;
            var journal=Read<Journal>(path,limit);
            if(journal==null||journal.schemaVersion!=1||journal.releaseId!=releaseId||
                (journal.phase!=phaseA&&(phaseB==null||journal.phase!=phaseB))||!HashValid(journal.manifestSha256))throw new InvalidDataException(failure);
            return journal;
        }
        private static Release ReadRelease(string root,string id,int version)
        {
            string prefix="updates/"+id+"/";
            string txPath=Inside(root,prefix+"transaction/journal.json"),stagePath=Inside(root,prefix+"stage/journal.json"),cancelPath=Inside(root,prefix+"recovery/cancelled.json");
            var stage=ReadJournal(stagePath,32768,id,"applied",null,"maintenance-stage-incomplete");
            var cancelled=ReadJournal(cancelPath,4096,id,"cancelled",null,"maintenance-recovery-incomplete");
            var transaction=ReadJournal(txPath,4096,id,"applied","rolledback","maintenance-transaction-incomplete");
            if(stage==null&&cancelled==null&&transaction==null)return null; // Directory creation alone made no mutation.
            if(version<9)throw new InvalidDataException("maintenance-release-invalid");
            string manifestPath=Inside(root,prefix+"release.json");
            long ignored;string actualManifestHash=Hash(manifestPath,65536,out ignored);
            foreach(var journal in new[]{stage,cancelled,transaction})if(journal!=null&&!String.Equals(actualManifestHash,journal.manifestSha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("maintenance-manifest-changed");
            var manifest=Read<Manifest>(manifestPath,65536);
            if(manifest==null||manifest.schemaVersion!=2||manifest.releaseId!=id||manifest.files==null||manifest.files.Count==0||manifest.files.Count>128||String.IsNullOrWhiteSpace(manifest.installedRoot)||!String.Equals(root,Path.GetFullPath(manifest.installedRoot).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("maintenance-manifest-invalid");
            var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var item in manifest.files){
                if(item==null)throw new InvalidDataException("maintenance-manifest-invalid");
                string path=Inside(root,item.relative);
                if(!seen.Add(path))throw new InvalidDataException("maintenance-duplicate-file");
                if(!HashValid(item.afterSha256)||(item.beforeSha256!=null&&!HashValid(item.beforeSha256)))throw new InvalidDataException("maintenance-manifest-invalid");
            }
            return new Release{Version=version,Id=id,Manifest=manifest,ProductAffected=transaction!=null||cancelled!=null,Before=cancelled!=null||(transaction!=null&&transaction.phase=="rolledback")};
        }
        internal static void Verify(string installRoot)
        {
            string root=Path.GetFullPath(installRoot).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar),updates=Inside(root,"updates");
            if(!Directory.Exists(updates)){if(File.Exists(updates))throw new InvalidDataException("maintenance-updates-invalid");return;}
            var releases=new List<Release>();var versions=new HashSet<int>();int count=0;
            foreach(string directory in Directory.EnumerateDirectories(updates)){
                if(++count>MaxReleaseDirectories)throw new InvalidDataException("maintenance-release-limit");
                string id=Path.GetFileName(directory);int version=ReleaseVersion(id);
                if(version>0&&version<9)continue; // Pre-schema-2 history remains owned by its legacy updater.
                var release=ReadRelease(root,id,version);
                if(release==null)continue;
                if(!versions.Add(version))throw new InvalidDataException("maintenance-duplicate-version");
                releases.Add(release);
            }
            releases.Sort((a,b)=>a.Version.CompareTo(b.Version));
            // A shortcut-stage record alone does not supersede an applied
            // product. Compose in release order so a later terminal transaction
            // supersedes only the paths it owns, including its rollback baseline.
            var expectedFiles=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            foreach(var release in releases)if(release.ProductAffected)foreach(var item in release.Manifest.files){
                expectedFiles[Inside(root,item.relative)]=release.Before?item.beforeSha256:item.afterSha256;
                if(expectedFiles.Count>MaxProductFiles)throw new InvalidDataException("maintenance-product-file-limit");
            }
            long remaining=MaxProductBytes;
            foreach(var entry in expectedFiles){
                if(entry.Value==null){if(File.Exists(entry.Key)||Directory.Exists(entry.Key))throw new InvalidDataException("maintenance-new-file-remains");continue;}
                if(!File.Exists(entry.Key))throw new InvalidDataException("maintenance-installation-mixed");
                long length;string actual=Hash(entry.Key,Math.Min(MaxProductFileBytes,remaining),out length);remaining-=length;
                if(!String.Equals(actual,entry.Value,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("maintenance-installation-mixed");
            }
            // Media content is verified by staged installation/Setup; ordinary
            // startup does not rehash hundreds of MB on every double click.
            // Mutable launcher.config.xml is parsed by Settings, not hash-pinned.
        }
    }

    // UI-owned preparation evidence. The request origin is captured before any
    // queue/IO work; delivery to Dispatcher can only make a sample older.
    // Native target replacement and terminal cancellation invalidate every stamp.
    // Focus is best effort, distinct from the ability to present an already
    // prepared client. A foreign foreground window is never activated over.
    internal static class StartupFocusPolicy
    {
        internal static bool Settled(bool targetForeground,bool coverForeground,bool prepared,double preparedAt,int attempts,double lastAttempt,double now)
        {
            if(targetForeground)return prepared&&now-preparedAt>=0.18;
            if(!coverForeground)return true; // non-activating handoff
            return attempts>=2&&now-lastAttempt>=0.5;
        }
        internal static bool MayRequest(bool targetForeground,bool coverForeground,bool inFinale,int attempts,double lastAttempt,double now)
        {return !inFinale&&attempts<2&&(targetForeground||coverForeground)&&now-lastAttempt>=0.75;}
    }

    internal sealed class StartupPreparation
    {
        internal sealed class Request
        {
            internal readonly string Epoch,OperationId;
            internal readonly object Session;
            internal readonly double Started;
            internal Request(string epoch,string operationId,object session,double started)
            {Epoch=epoch;OperationId=operationId;Session=session;Started=started;}
        }
        private string epoch=Guid.NewGuid().ToString("N"),operationId=Guid.NewGuid().ToString("N");
        private string document;
        private long mediaGeneration;
        private bool terminal,committed;
        private double committedAt=-1;
        private BridgeStatus committedState;
        private Request acceptedRequest;
        private BridgeStatus accepted;
        internal string OperationId {get{return operationId;}}
        internal BridgeStatus Snapshot {get{return accepted;}}
        internal bool IsTerminal {get{return terminal;}}
        internal bool HasCommit {get{return committed;}}
        internal Request Begin(object session,double now)
        {
            if(terminal||session==null||Double.IsNaN(now)||Double.IsInfinity(now)||now<0)return null;
            return new Request(epoch,operationId,session,now);
        }
        internal void InvalidateTarget()
        {
            if(terminal)return;
            if(committed){Cancel();return;}
            epoch=Guid.NewGuid().ToString("N");operationId=Guid.NewGuid().ToString("N");
            document=null;mediaGeneration=0;accepted=null;acceptedRequest=null;
        }
        internal void Cancel(){terminal=true;accepted=null;acceptedRequest=null;committedState=null;}
        internal static bool ValidId(string value)
        {
            if(value==null||value.Length!=32)return false;
            foreach(char c in value)if(!((c>='0'&&c<='9')||(c>='a'&&c<='f')))return false;
            return true;
        }
        private static bool SameProcess(BridgeStatus a,BridgeStatus b)
        {return a!=null&&b!=null&&a.HostPid==b.HostPid&&a.HostCreated==b.HostCreated&&a.CodexPid==b.CodexPid&&a.CodexCreated==b.CodexCreated;}
        private static double Age(Request request,BridgeStatus status,double now)
        {
            if(request==null||status==null||status.ObservationAgeMs<0||Double.IsNaN(now)||Double.IsInfinity(now)||now<request.Started)return Double.PositiveInfinity;
            return status.ObservationAgeMs+(now-request.Started)*1000;
        }
        internal bool Accept(Request request,object session,BridgeStatus status,double now)
        {
            if(terminal||request==null||request.Epoch!=epoch||request.OperationId!=operationId||!Object.ReferenceEquals(request.Session,session))return false;
            if(status==null||status.ProtocolVersion!=2||!ValidId(status.DocumentGeneration)||status.MediaGeneration<=0||status.Health!="fresh"||Age(request,status,now)>=4000)return false;
            if(accepted!=null&&!SameProcess(accepted,status)){InvalidateTarget();return false;}
            if(document!=null&&(document!=status.DocumentGeneration||mediaGeneration!=status.MediaGeneration)){
                // A generation change after finale commit is an invalidation,
                // never an invitation to prepare another finale in this attempt.
                if(committed)Cancel();else InvalidateTarget();
                return false;
            }
            // Cached status may legitimately repeat a sequence. Older actual
            // observations must never overwrite a newer sample in this epoch.
            if(accepted!=null&&status.Sequence<accepted.Sequence)return false;
            document=status.DocumentGeneration;mediaGeneration=status.MediaGeneration;
            accepted=status;acceptedRequest=request;return true;
        }
        internal bool IsPrepared(object session,double now)
        {
            BridgeStatus state=accepted;
            return !terminal&&state!=null&&acceptedRequest!=null&&Object.ReferenceEquals(acceptedRequest.Session,session)&&
                Age(acceptedRequest,state,now)<4000&&state.ProtocolVersion==2&&state.Health=="fresh"&&
                state.Prepared&&!state.CommitAccepted&&state.ActualPhase=="prepared"&&state.PrepareOperationId==operationId&&state.Disposition==2&&state.Ready&&!state.Disabled&&
                (state.MediaCode==3||state.MediaCode==4)&&state.RefreshCode==1&&state.DiagnosticsVersion==1&&
                state.LayoutSupported==true&&state.ProbeOnly==false&&state.Attached==true;
            // Prepared means a decoded buffer in this exact generation. A
            // splash-covered window need not have submitted a compositor frame.
            // WallpaperReadiness still requires fresh visible playback later.
        }
        // Preparation samples stay strict. Once an authored finale starts, a
        // six-second lease survives a missed scheduling interval without borrowing
        // readiness from a new renderer, decoder, process or operation.
        internal bool Commit(object session,double now)
        {
            if(committed||!IsPrepared(session,now))return false;
            committed=true;committedState=accepted;committedAt=now;return true;
        }
        internal bool CanHandoff(object session,BridgeStatus latest,double now,bool revealSent)
        {
            if(terminal||!committed||committedState==null||acceptedRequest==null||!Object.ReferenceEquals(acceptedRequest.Session,session)||
                Double.IsNaN(now)||Double.IsInfinity(now)||now<committedAt||now-committedAt>6)return false;
            BridgeStatus state=latest??accepted;
            if(state==null||state.ProtocolVersion!=2||!SameProcess(committedState,state)||state.DocumentGeneration!=committedState.DocumentGeneration||
                state.MediaGeneration!=committedState.MediaGeneration||state.Sequence<committedState.Sequence||state.PrepareOperationId!=operationId||
                state.Health!="fresh"||state.Disabled||!state.Ready||!state.Prepared||state.Disposition!=2||state.RefreshCode!=1||
                state.DiagnosticsVersion!=1||state.LayoutSupported!=true||state.ProbeOnly!=false||state.Attached!=true)return false;
            // Explicit failure or an observed preparation restart invalidates the
            // lease. Expiration alone is tolerated only within six seconds.
            if(!state.CommitAccepted)return state.ActualPhase=="prepared"&&(state.MediaCode==3||state.MediaCode==4);
            // reveal() resets firstFrameConfirmed while the approved decoder
            // presents its first visible frame. The matching commit reply is
            // an acknowledgement, not playback evidence; Setup observes that.
            return revealSent&&state.Revealed&&state.MediaCode==4&&
                (state.ActualPhase=="visible"||state.ActualPhase=="gap"||state.ActualPhase=="resuming");
        }
    }
}

namespace DragonLauncher
{
    // Optional startup visuals have a bounded lifetime and resource budget.
    // All thresholds are protective policy, not a measurement of normal usage.
    internal sealed class StartupResourcePolicy
    {
        internal const long MiB=1024L*1024;
        internal const long PrivateLimit=1536*MiB;
        internal const double CpuLimit=0.55,MemoryHoldSeconds=1,CpuHoldSeconds=6;
        private readonly int processors;
        private double lastAt=-1,lastCpu,highMemory,highCpu;
        internal string Reason {get;private set;}
        internal StartupResourcePolicy(int processors){this.processors=System.Math.Max(1,processors);}
        internal bool Evaluate(double now,double cpu,long bytes,bool lowMemory)
        {
            if(Reason!=null)return false;
            if(System.Double.IsNaN(now)||System.Double.IsInfinity(now)||System.Double.IsNaN(cpu)||System.Double.IsInfinity(cpu)||now<0||cpu<0||bytes<0){Reset();return false;}
            double dt=lastAt<0?0:now-lastAt,deltaCpu=cpu-lastCpu;
            lastAt=now;lastCpu=cpu;
            if(dt<0.05||deltaCpu<0){highCpu=0;highMemory=0;return false;}
            // A sampling gap cannot prove sustained CPU usage. However two
            // observed excessive memory samples must still trip on an overloaded
            // scheduler; repeatedly missing 1.5s must not disable the breaker.
            highMemory=lowMemory||bytes>=PrivateLimit?highMemory+(dt>1.5?0.5:dt):0;
            highCpu=dt<=1.5&&deltaCpu/(dt*processors)>CpuLimit?highCpu+dt:0;
            if(highMemory>=MemoryHoldSeconds)Reason=lowMemory?"system-memory-pressure":"startup-private-memory-limit";
            else if(highCpu>=CpuHoldSeconds)Reason="startup-sustained-cpu-limit";
            return Reason!=null;
        }
        private void Reset(){lastAt=-1;lastCpu=0;highMemory=highCpu=0;}
    }

    internal sealed class StartupSamplingHealth
    {
        private int failures;
        internal bool Tripped {get;private set;}
        internal bool Record(bool healthy){if(Tripped)return false;failures=healthy?0:failures+1;if(failures<3)return false;Tripped=true;return true;}
    }
    internal sealed class StartupResourceBreaker:System.IDisposable
    {
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct MemoryStatus
        {
            internal uint Length,Load;
            internal ulong TotalPhysical,AvailablePhysical,TotalPageFile,AvailablePageFile,TotalVirtual,AvailableVirtual,AvailableExtendedVirtual;
        }
        [System.Runtime.InteropServices.DllImport("kernel32.dll",SetLastError=true)]
        private static extern System.IntPtr CreateMemoryResourceNotification(int type);
        [System.Runtime.InteropServices.DllImport("kernel32.dll",SetLastError=true)]
        private static extern bool QueryMemoryResourceNotification(System.IntPtr handle,out bool low);
        [System.Runtime.InteropServices.DllImport("kernel32.dll",SetLastError=true)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool CloseHandle(System.IntPtr handle);
        private readonly object sync=new object();
        private readonly StartupResourcePolicy policy=new StartupResourcePolicy(System.Environment.ProcessorCount);
        private readonly StartupSamplingHealth samplingHealth=new StartupSamplingHealth();
        private readonly System.Diagnostics.Stopwatch clock=System.Diagnostics.Stopwatch.StartNew();
        private readonly System.Action<string> trip;
        private System.Threading.Timer timer;
        private System.IntPtr notification;
        private bool disposed,started;
        private bool sampleErrorLogged;
        internal StartupResourceBreaker(System.Action<string> trip){this.trip=trip;}
        internal void Start()
        {
            lock(sync){
                if(disposed||started)return;started=true;
                try{notification=CreateMemoryResourceNotification(0);}catch(System.EntryPointNotFoundException){}
                if(notification==System.IntPtr.Zero)LauncherDiagnostics.Write("handoff-latest.log","Startup memory notification unavailable; global memory fallback active");
                // Creation and assignment are under the same lock as disposal;
                // a callback cannot dispose an as-yet-unassigned timer.
                timer=new System.Threading.Timer(Sample,null,0,500);
            }
        }
        private bool LowMemory()
        {
            bool low=false;
            bool notificationValid=notification!=System.IntPtr.Zero&&QueryMemoryResourceNotification(notification,out low);
            if(notificationValid&&low)return true;
            var status=new MemoryStatus{Length=(uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(MemoryStatus))};
            if(GlobalMemoryStatusEx(ref status))return status.AvailablePhysical<1024UL*1024*1024 || status.AvailablePageFile<512UL*1024*1024;
            if(notificationValid)return false;
            throw new System.IO.IOException("startup-memory-observation-unavailable");
        }
        private void Sample(object ignored)
        {
            string reason=null;
            if(!System.Threading.Monitor.TryEnter(sync))return;
            try{
                if(disposed||policy.Reason!=null||samplingHealth.Tripped)return;
                using(var process=System.Diagnostics.Process.GetCurrentProcess()){
                    if(policy.Evaluate(clock.Elapsed.TotalSeconds,process.TotalProcessorTime.TotalSeconds,process.PrivateMemorySize64,LowMemory()))reason=policy.Reason;
                    samplingHealth.Record(true);
                }
            }catch(System.Exception e){
                if(!sampleErrorLogged){sampleErrorLogged=true;LauncherDiagnostics.Write("handoff-latest.log","Startup resource sample unavailable: "+e.GetType().Name);}
                if(samplingHealth.Record(false))reason="startup-resource-monitor-unavailable";
            }finally{System.Threading.Monitor.Exit(sync);}
            if(reason!=null){try{trip(reason);}catch(System.Exception e){LauncherDiagnostics.Write("handoff-latest.log","Startup resource trip dispatch failed: "+e.GetType().Name);}}
        }
        public void Dispose()
        {
            lock(sync){
                if(disposed)return;disposed=true;
                if(timer!=null){timer.Dispose();timer=null;}
                if(notification!=System.IntPtr.Zero){CloseHandle(notification);notification=System.IntPtr.Zero;}
            }
        }
    }
}

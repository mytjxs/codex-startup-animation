using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace DragonLauncher
{
    [DataContract]
    internal sealed class BridgeStatus
    {
        [DataMember(Name="version", IsRequired=true)] public int Version { get; set; }
        [DataMember(Name="disposition", IsRequired=true)] public int Disposition { get; set; }
        [DataMember(Name="hostPid", IsRequired=true)] public int HostPid { get; set; }
        [DataMember(Name="hostCreationFileTime", IsRequired=true)] public long HostCreated { get; set; }
        [DataMember(Name="codexPid", IsRequired=true)] public int CodexPid { get; set; }
        [DataMember(Name="creationFileTime", IsRequired=true)] public long CodexCreated { get; set; }
        [DataMember(Name="mediaCode", IsRequired=true)] public int MediaCode { get; set; }
        [DataMember(Name="ready", IsRequired=true)] public bool Ready { get; set; }
        [DataMember(Name="revealed", IsRequired=true)] public bool Revealed { get; set; }
        [DataMember(Name="disabled", IsRequired=true)] public bool Disabled { get; set; }
        [DataMember(Name="refreshCode", IsRequired=false)] public int RefreshCode { get; set; }
        [DataMember(Name="diagnosticsVersion", IsRequired=false, EmitDefaultValue=false)] public int? DiagnosticsVersion { get; set; }
        [DataMember(Name="layoutSupported", IsRequired=false, EmitDefaultValue=false)] public bool? LayoutSupported { get; set; }
        [DataMember(Name="probeOnly", IsRequired=false, EmitDefaultValue=false)] public bool? ProbeOnly { get; set; }
        [DataMember(Name="attached", IsRequired=false, EmitDefaultValue=false)] public bool? Attached { get; set; }
        [DataMember(Name="playing", IsRequired=false, EmitDefaultValue=false)] public bool? Playing { get; set; }
        [DataMember(Name="wallpaperApplied", IsRequired=false, EmitDefaultValue=false)] public bool? WallpaperApplied { get; set; }
        [DataMember(Name="firstFrameConfirmed", IsRequired=false, EmitDefaultValue=false)] public bool? FirstFrameConfirmed { get; set; }
        [DataMember(Name="documentHidden", IsRequired=false, EmitDefaultValue=false)] public bool? DocumentHidden { get; set; }
        [DataMember(Name="stageVisible", IsRequired=false, EmitDefaultValue=false)] public bool? StageVisible { get; set; }
        [DataMember(Name="mediaTimeMs", IsRequired=false, EmitDefaultValue=false)] public int? MediaTimeMs { get; set; }
        [DataMember(Name="protocolVersion", IsRequired=false)] public int ProtocolVersion { get; set; }
        [DataMember(Name="documentGeneration", IsRequired=false)] public string DocumentGeneration { get; set; }
        [DataMember(Name="mediaGeneration", IsRequired=false)] public long MediaGeneration { get; set; }
        [DataMember(Name="observationAgeMs", IsRequired=false)] public long ObservationAgeMs { get; set; }
        [DataMember(Name="prepareOperationId", IsRequired=false)] public string PrepareOperationId { get; set; }
        [DataMember(Name="prepared", IsRequired=false)] public bool Prepared { get; set; }
        [DataMember(Name="commitAccepted", IsRequired=false)] public bool CommitAccepted { get; set; }
        [DataMember(Name="actualPhase", IsRequired=false)] public string ActualPhase { get; set; }
        [DataMember(Name="health", IsRequired=false)] public string Health { get; set; }
        [DataMember(Name="sequence", IsRequired=false)] public long Sequence { get; set; }
    }

    internal enum WallpaperReadinessState
    {
        Unknown, Disabled, Failed, Released, UnsupportedLayout, Hidden, ProbeOnly,
        NotAttached, PendingHandoff, StageHidden, FirstFramePending, Paused,
        AwaitingProgress, Advancing
    }

    // Fixed local diagnostics only. These checks confirm attachment and video
    // progress, not unobscured pixels, aesthetics or an achieved display FPS.
    internal static class WallpaperReadiness
    {
        internal static string Describe(WallpaperReadinessState state)
        {
            switch(state){
                case WallpaperReadinessState.Disabled:return "本次壁纸已关闭\n重新启用需在下次正常启动 Codex 后生效";
                case WallpaperReadinessState.Failed:return "壁纸接入未成功\nCodex 仍可正常使用";
                case WallpaperReadinessState.Released:return "壁纸已释放\n重新接入需在下次启动 Codex 后生效";
                case WallpaperReadinessState.UnsupportedLayout:return "当前客户端布局不适合接入壁纸\n已保留原有界面";
                case WallpaperReadinessState.Hidden:return "Codex 页面报告当前不可见\n可能仍被隐藏或最小化 壁纸已暂停\n若屏幕上是空白窗口 则启动交接尚未完成";
                case WallpaperReadinessState.ProbeOnly:return "当前是媒体探测状态\n尚未作为客户端背景显示";
                case WallpaperReadinessState.NotAttached:return "壁纸尚未挂载到聊天区域\n客户端仍可正常使用";
                case WallpaperReadinessState.PendingHandoff:return "壁纸已准备\n等待启动动画交接";
                case WallpaperReadinessState.StageHidden:return "壁纸层已挂载但当前不可见\n尚不能确认播放";
                case WallpaperReadinessState.FirstFramePending:return "壁纸正在准备首帧\n尚不能确认播放";
                case WallpaperReadinessState.Paused:return "壁纸已暂停";
                case WallpaperReadinessState.AwaitingProgress:return "壁纸已挂载并取得首帧\n单次状态检查尚未确认视频进度";
                case WallpaperReadinessState.Advancing:return "已挂载并检测到视频进度推进";
                default:return "壁纸诊断信息不完整\n尚不能确认已挂载或播放";
            }
        }
        internal static WallpaperReadinessState Inspect(BridgeStatus state)
        {
            if(state==null || state.RefreshCode!=1)return WallpaperReadinessState.Unknown;
            if(state.Disabled)return WallpaperReadinessState.Disabled;
            if(state.MediaCode>=100 || state.MediaCode==8)return WallpaperReadinessState.Failed;
            if(state.MediaCode==6)return WallpaperReadinessState.Released;
            if(state.MediaCode==7)return WallpaperReadinessState.UnsupportedLayout;
            if(state.Version!=1 || state.Disposition!=2 || state.HostPid<=0 || state.HostCreated<=0 ||
                state.CodexPid<=0 || state.CodexCreated<=0)return WallpaperReadinessState.Unknown;
            if(state.DiagnosticsVersion!=1 || !state.LayoutSupported.HasValue || !state.ProbeOnly.HasValue ||
                !state.Attached.HasValue || !state.Playing.HasValue || !state.WallpaperApplied.HasValue ||
                !state.FirstFrameConfirmed.HasValue || !state.DocumentHidden.HasValue ||
                !state.StageVisible.HasValue || !state.MediaTimeMs.HasValue || state.MediaTimeMs<0 || state.MediaTimeMs>86400000)
                return WallpaperReadinessState.Unknown;
            if(state.ProbeOnly.Value)return WallpaperReadinessState.ProbeOnly;
            if(state.DocumentHidden.Value)return WallpaperReadinessState.Hidden;
            if(!state.LayoutSupported.Value)return WallpaperReadinessState.UnsupportedLayout;
            if(!state.Attached.Value)return WallpaperReadinessState.NotAttached;
            if(!state.Revealed || !state.WallpaperApplied.Value)return WallpaperReadinessState.PendingHandoff;
            if(!state.StageVisible.Value)return WallpaperReadinessState.StageHidden;
            if(!state.Ready || !state.FirstFrameConfirmed.Value)return WallpaperReadinessState.FirstFramePending;
            if(state.MediaCode==5 || !state.Playing.Value)return WallpaperReadinessState.Paused;
            return WallpaperReadinessState.AwaitingProgress;
        }
        private static bool SameGeneration(BridgeStatus previous,BridgeStatus current)
        {
            return previous!=null && current!=null && previous.Version==1 && current.Version==1 &&
                previous.Disposition==2 && current.Disposition==2 && current.HostPid>0 && current.CodexPid>0 &&
                current.HostCreated>0 && current.CodexCreated>0 && previous.HostPid==current.HostPid &&
                previous.CodexPid==current.CodexPid && previous.HostCreated==current.HostCreated &&
                previous.CodexCreated==current.CodexCreated && SameObservationGeneration(previous,current);
        }
        private static bool SameObservationGeneration(BridgeStatus previous,BridgeStatus current)
        {
            if(previous.ProtocolVersion!=current.ProtocolVersion)return false;
            // Legacy diagnostics have no renderer generation or sample sequence.
            if(current.ProtocolVersion==0)return true;
            // A restarted document/decoder can have a later media position. It
            // cannot provide the second half of an older playback observation.
            return current.ProtocolVersion==2 && StartupPreparation.ValidId(current.DocumentGeneration) &&
                String.Equals(previous.DocumentGeneration,current.DocumentGeneration,StringComparison.Ordinal) &&
                current.MediaGeneration>=0 && previous.MediaGeneration==current.MediaGeneration &&
                previous.Sequence>0 && current.Sequence>previous.Sequence;
        }
        internal static WallpaperReadinessState Evaluate(BridgeStatus previous,BridgeStatus current,int loopMilliseconds=20000)
        {
            WallpaperReadinessState state=Inspect(current);
            if(state!=WallpaperReadinessState.AwaitingProgress)return state;
            if(Inspect(previous)!=WallpaperReadinessState.AwaitingProgress || !SameGeneration(previous,current))return state;
            int before=previous.MediaTimeMs.Value, after=current.MediaTimeMs.Value;
            if(after>before)return WallpaperReadinessState.Advancing;
            // A bounded wrap is allowed only for the reviewed loop duration.
            // Other backward jumps require new forward samples; a seek or a
            // stale/hidden snapshot never becomes proof of playback.
            if(loopMilliseconds>4000 && loopMilliseconds<=86400000 && before<loopMilliseconds &&
                before>=loopMilliseconds-2000 && after<2000 && before>after &&
                (long)loopMilliseconds-before+after>0 && (long)loopMilliseconds-before+after<=4000)
                return WallpaperReadinessState.Advancing;
            return state;
        }
        internal static bool ConfirmPlayback(BridgeStatus first,BridgeStatus second,BridgeStatus third,int loopMilliseconds=20000)
        {
            return Evaluate(first,second,loopMilliseconds)==WallpaperReadinessState.Advancing &&
                Evaluate(second,third,loopMilliseconds)==WallpaperReadinessState.Advancing;
        }
    }

    [DataContract]
    internal sealed class BridgeDiscovery
    {
        [DataMember(Name="version", IsRequired=true)] public int Version { get; set; }
        [DataMember(Name="session", IsRequired=true)] public string Session { get; set; }
        [DataMember(Name="hostPid", IsRequired=true)] public int HostPid { get; set; }
        [DataMember(Name="hostCreationFileTime", IsRequired=true)] public long HostCreated { get; set; }
    }

    internal sealed class BridgeResult : IDisposable
    {
        internal uint Pid;
        internal Exception Error;
        internal string Code;
        internal BridgeSession Session;
        internal BridgeClientIdentity ClientIdentity=null;
        public void Dispose() { BridgeSession held=Interlocked.Exchange(ref Session,null); if(held!=null)held.Dispose(); }
    }

    // The original Process handle is retained for this entire session. Disposing
    // it only releases our wrapper; neither the host nor Codex is ever killed.
    internal sealed class BridgeSession : IDisposable
    {
        private readonly Process host;
        private readonly string pipeName;
        private readonly long hostCreated;
        private readonly object lifetimeGate=new object(), ioGate=new object();
        private bool disposalRequested, disposed, disableRequested;
        private int pendingCommands, synchronousCommands;
        private bool workerRunning;
        private readonly Queue<string> commands=new Queue<string>();
        private readonly Action<string> log;
        private volatile BridgeStatus lastStatus;
        internal BridgeStatus LastStatus { get {return lastStatus;} private set {lastStatus=value;} }
        internal Process RetainedProcess { get { return host; } }
        // Only a signaled retained process handle proves this exact host ended.
        // Pipe timeouts, access errors and a reused PID are not exit evidence.
        internal bool HostExited { get { lock(lifetimeGate) {
            return !disposed && BridgeNative.HasExited(host.Handle);
        } } }

        internal BridgeSession(Process process,string session,Action<string> log)
        {
            host=process;this.log=log;
            // Accessing Handle makes Process retain the specific instance.
            IntPtr retained=host.Handle;
            if(!BridgeNative.IsAlive(retained) || !BridgeNative.CreationTime(retained,out hostCreated))
                throw new InvalidOperationException("bridge-instance-unavailable");
            pipeName=BridgeActivation.PipeName(session);
        }
        private static int Remaining(Stopwatch watch,int total)
        {
            long remaining=(long)total-watch.ElapsedMilliseconds;
            if(remaining<=0)throw new TimeoutException("bridge-ipc-timeout");
            return (int)remaining;
        }
        private static void Await(Task task,Stopwatch watch,int total)
        {
            if(!task.Wait(Remaining(watch,total))) {
                task.ContinueWith(t=>{ var ignored=t.Exception; },TaskContinuationOptions.OnlyOnFaulted);
                throw new TimeoutException("bridge-ipc-timeout");
            }
            task.GetAwaiter().GetResult();
        }
        private BridgeStatus ExchangeCore(string command,int timeout)
        {
            if(!ValidCommand(command))
                throw new InvalidOperationException("bridge-command-rejected");
            var watch=Stopwatch.StartNew();
            if(!BridgeNative.IsAlive(host.Handle))throw new InvalidOperationException("bridge-instance-ended");
            using(var pipe=new NamedPipeClientStream(".",pipeName,PipeDirection.InOut,PipeOptions.Asynchronous,TokenImpersonationLevel.Identification)) {
                pipe.Connect(Remaining(watch,timeout));
                uint server;
                if(!BridgeNative.GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(),out server) ||
                    server!=(uint)host.Id || !BridgeNative.IsAlive(host.Handle))
                    throw new InvalidOperationException("bridge-server-identity-rejected");
                byte[] request=Encoding.UTF8.GetBytes(command+"\n");
                Await(pipe.WriteAsync(request,0,request.Length),watch,timeout);
                byte[] buffer=new byte[2048];int used=0;
                while(true) {
                    if(used==buffer.Length)throw new InvalidDataException("bridge-response-too-large");
                    Task<int> read=pipe.ReadAsync(buffer,used,buffer.Length-used);Await(read,watch,timeout);
                    int count=read.Result;if(count==0)throw new EndOfStreamException("bridge-response-incomplete");
                    int end=Array.IndexOf(buffer,(byte)10,used,count);used+=count;
                    if(end<0)continue;
                    if(end!=used-1)throw new InvalidDataException("bridge-response-trailing-data");
                    BridgeStatus status=BridgeActivation.Decode<BridgeStatus>(buffer,end);
                    if(status.Version!=1 || status.HostPid!=host.Id || status.HostCreated!=hostCreated ||
                        status.Disposition<0 || status.Disposition>3 || status.CodexPid<0 || status.CodexCreated<0 ||
                        status.MediaCode<0 || status.MediaCode>65535 ||
                        status.RefreshCode<0 || status.RefreshCode>4 ||
                        (status.Disposition==2 && (status.CodexPid==0 || status.CodexCreated==0)))
                        throw new InvalidDataException("bridge-status-rejected");
                    if(LastStatus!=null && LastStatus.Disposition==2 && status.Disposition!=2)
                        throw new InvalidDataException("bridge-started-state-regressed");
                    if(LastStatus!=null && LastStatus.Disposition==2 &&
                        (status.CodexPid!=LastStatus.CodexPid || status.CodexCreated!=LastStatus.CodexCreated))
                        throw new InvalidDataException("bridge-client-generation-changed");
                    LastStatus=status;return status;
                }
            }
        }
        internal BridgeStatus Status(int timeout)
        {return Control("status",timeout);}
        internal BridgeStatus Refresh(int timeout)
        {return Control("refresh",timeout);}
        internal BridgeStatus Prepare(string operationId,int timeout)
        {if(!StartupPreparation.ValidId(operationId))throw new ArgumentException("bridge-operation-rejected");return Control("prepare "+operationId,timeout);}
        internal static bool ValidCommand(string command)
        {
            if(command=="status"||command=="reveal"||command=="pause"||command=="disable"||command=="refresh")return true;
            return command!=null&&((command.StartsWith("prepare ",StringComparison.Ordinal)&&StartupPreparation.ValidId(command.Substring(8)))||
                (command.StartsWith("commit ",StringComparison.Ordinal)&&StartupPreparation.ValidId(command.Substring(7))));
        }
        private static bool ChangesDecoration(string command)
        {return command=="reveal"||command=="pause"||command.StartsWith("prepare ",StringComparison.Ordinal)||command.StartsWith("commit ",StringComparison.Ordinal);}
        internal BridgeStatus Control(string command,int timeout)
        {
            if(!ValidCommand(command))throw new InvalidOperationException("bridge-command-rejected");
            var watch=Stopwatch.StartNew();bool entered=false;
            try {
                if(!Monitor.TryEnter(ioGate,Remaining(watch,timeout)))throw new TimeoutException("bridge-ipc-lock-timeout");
                entered=true;
                lock(lifetimeGate) {
                    if(disposalRequested)throw new ObjectDisposedException("BridgeSession");
                    if(disableRequested&&ChangesDecoration(command))throw new InvalidOperationException("bridge-disabled-terminal");
                    if(command=="disable")disableRequested=true;
                    synchronousCommands++;
                }
                try {return ExchangeCore(command,Remaining(watch,timeout));}
                finally {lock(lifetimeGate){synchronousCommands--;ReleaseIfIdle();}}
            }finally {if(entered)Monitor.Exit(ioGate);}
        }
        internal BridgeStatus Handshake(int milliseconds)
        {
            var watch=Stopwatch.StartNew();BridgeStatus status=null;
            do {
                status=Status(Remaining(watch,milliseconds));
                if(status.Disposition!=0)return status;
                Thread.Sleep(Math.Min(60,Remaining(watch,milliseconds)));
            }while(watch.ElapsedMilliseconds<milliseconds);
            return status;
        }
        internal void Queue(string command)
        {
            if(!ValidCommand(command))return;
            lock(lifetimeGate) {
                if(disposalRequested)return;
                if(disableRequested&&ChangesDecoration(command))return;
                // Latest terminal intent supersedes stale queued refresh/reveal
                // requests. Bound retained work even if the pipe is unresponsive.
                if(command=="disable") {disableRequested=true;pendingCommands-=commands.Count;commands.Clear();}
                // The single terminal commit must not be silently lost behind
                // routine diagnostics. It still shares the bounded IO drain.
                if(command.StartsWith("commit ",StringComparison.Ordinal)){pendingCommands-=commands.Count;commands.Clear();}
                if(commands.Contains(command) || pendingCommands>=4)return;
                commands.Enqueue(command);pendingCommands++;
                if(workerRunning)return;workerRunning=true;
                // A launcher normally exits immediately after its last window
                // closes. ThreadPool work is abandoned at process exit, which
                // could drop the final reveal/disable. Keep just this bounded
                // pipe drain alive, without blocking the animation dispatcher.
                var worker=new Thread(DrainCommands) {IsBackground=false,Name="Wallpaper command drain"};
                try {worker.Start();}
                catch {workerRunning=false;pendingCommands=0;commands.Clear();ReleaseIfIdle();}
            }
        }
        private void DrainCommands(object unused)
        {
            var deadline=Stopwatch.StartNew();
            while(true) {
                string command;
                lock(lifetimeGate) {
                    if(deadline.ElapsedMilliseconds>=3000) {pendingCommands-=commands.Count;commands.Clear();}
                    if(commands.Count==0){workerRunning=false;ReleaseIfIdle();return;}
                    command=commands.Dequeue();
                }
                bool entered=false;
                try {
                    int budget=Math.Min(1500,Remaining(deadline,3000));var watch=Stopwatch.StartNew();
                    if(!Monitor.TryEnter(ioGate,Remaining(watch,budget)))throw new TimeoutException("bridge-command-lock-timeout");
                    entered=true;
                    bool rejected;lock(lifetimeGate){rejected=disableRequested&&ChangesDecoration(command);}
                    if(!rejected)ExchangeCore(command,Remaining(watch,budget));
                }
                catch {try{if(log!=null)log("Wallpaper bridge command failed: "+SafeCommand(command));}catch{}}
                finally {if(entered)Monitor.Exit(ioGate);lock(lifetimeGate) {pendingCommands--;ReleaseIfIdle();} }
            }
        }
        private static string SafeCommand(string command)
        { if(command!=null&&command.StartsWith("prepare ",StringComparison.Ordinal))return "prepare";
          if(command!=null&&command.StartsWith("commit ",StringComparison.Ordinal))return "commit";
          return command=="reveal" || command=="pause" || command=="disable" || command=="status" || command=="refresh" ? command : "invalid"; }
        private void ReleaseIfIdle()
        {if(disposalRequested && !disposed && pendingCommands==0 && synchronousCommands==0){disposed=true;host.Dispose();}}
        public void Dispose(){lock(lifetimeGate){disposalRequested=true;ReleaseIfIdle();}}
    }

    // Diagnostics contain only bounded fixed fields. Never pass exception
    // messages, pipe/session names, paths, command lines or UI content here.
    internal static class BridgeDiagnostics
    {
        internal static readonly string Trace=ReadTrace();
        private static string ReadTrace()
        {
            try {string value=Environment.GetEnvironmentVariable("CODEX_DRAGON_STARTUP_TRACE");
                if(value!=null && value.Length==32){bool valid=true;foreach(char c in value)if(!((c>='0'&&c<='9')||(c>='a'&&c<='f')))valid=false;if(valid)return value;}
            }catch{}
            return Guid.NewGuid().ToString("N");
        }
        internal static string ExceptionType(Exception error)
        {
            if(error==null)return "None";
            Type t=error.GetType();
            if(t==typeof(InvalidOperationException)||t==typeof(ArgumentException)||t==typeof(ArgumentOutOfRangeException)||
                t==typeof(TimeoutException)||t==typeof(IOException)||t==typeof(InvalidDataException)||t==typeof(EndOfStreamException)||
                t==typeof(FileNotFoundException)||t==typeof(DirectoryNotFoundException)||t==typeof(UnauthorizedAccessException)||
                t==typeof(System.ComponentModel.Win32Exception)||t==typeof(System.Security.SecurityException)||
                t==typeof(System.Runtime.Serialization.SerializationException)||t==typeof(System.Xml.XmlException)||
                t==typeof(ObjectDisposedException)||t==typeof(AggregateException))return t.Name;
            return "OtherException";
        }
        internal static void Record(Action<string> log,string operation,int phase,long elapsed,int reason,string outcome,Exception error,BridgeStatus status)
        {
            try {
                if(log==null || (operation!="discover"&&operation!="open") || phase<0||phase>16||reason<0||reason>16)return;
                string safe="none";
                if(!String.IsNullOrEmpty(outcome)&&outcome.Length<=64){bool valid=true;foreach(char c in outcome)if(!((c>='a'&&c<='z')||c=='-'))valid=false;if(valid)safe=outcome;}
                int pid;using(var process=Process.GetCurrentProcess())pid=process.Id;
                log("bridge-diagnostic trace="+Trace+" pid="+pid.ToString(CultureInfo.InvariantCulture)+" operation="+operation+
                    " phase="+phase.ToString(CultureInfo.InvariantCulture)+" elapsedMs="+Math.Max(0,elapsed).ToString(CultureInfo.InvariantCulture)+
                    " reason="+reason.ToString(CultureInfo.InvariantCulture)+" outcome="+safe+" exceptionType="+ExceptionType(error)+
                    " hresult="+(error==null?0:error.HResult).ToString(CultureInfo.InvariantCulture)+
                    " hostPid="+(status==null?0:status.HostPid).ToString(CultureInfo.InvariantCulture)+
                    " disposition="+(status==null?-1:status.Disposition).ToString(CultureInfo.InvariantCulture)+
                    " mediaCode="+(status==null?-1:status.MediaCode).ToString(CultureInfo.InvariantCulture));
            }catch{} // A broken diagnostic sink cannot alter activation.
        }
    }

    internal static class BridgeActivation
    {
        internal static string PipeName(string session)
        {
            if(session==null || session.Length!=32)throw new InvalidDataException("bridge-session-rejected");
            for(int i=0;i<session.Length;i++)if(!((session[i]>='0' && session[i]<='9') || (session[i]>='a' && session[i]<='f')))
                throw new InvalidDataException("bridge-session-rejected");
            using(var identity=WindowsIdentity.GetCurrent())using(var sha=SHA256.Create()) {
                byte[] bytes=sha.ComputeHash(Encoding.UTF8.GetBytes(identity.User.Value));
                var prefix=new StringBuilder();for(int i=0;i<8;i++)prefix.Append(bytes[i].ToString("x2",CultureInfo.InvariantCulture));
                return "CodexDragon.Wallpaper."+prefix+"."+session;
            }
        }
        internal static T Decode<T>(byte[] data,int count)
        {
            // Older hosts omit diagnostics. Missing data is unknown, never
            // playback evidence. Validate lexical JSON types before the data
            // contract serializer can coerce strings, nulls or numeric forms.
            if(typeof(T)==typeof(BridgeStatus))using(var reader=JsonReaderWriterFactory.CreateJsonReader(data,0,count,Encoding.UTF8,XmlDictionaryReaderQuotas.Max,null)){
                var seen=new HashSet<string>(StringComparer.Ordinal);int diagnostics=-1, diagnosticFields=0;
                while(!reader.EOF){
                    if(reader.NodeType==XmlNodeType.Element && reader.Depth==1){
                        string name=reader.LocalName;
                        bool flag=name=="layoutSupported" || name=="probeOnly" || name=="attached" || name=="playing" ||
                            name=="wallpaperApplied" || name=="firstFrameConfirmed" || name=="documentHidden" || name=="stageVisible";
                        bool number=name=="refreshCode" || name=="diagnosticsVersion" || name=="mediaTimeMs";
                        if(flag || number){
                            if(!seen.Add(name) || reader.GetAttribute("type")!=(flag?"boolean":"number"))
                                throw new InvalidDataException("bridge-diagnostic-field-rejected");
                            string value=reader.ReadElementContentAsString();
                            if(flag){if(value!="true" && value!="false")throw new InvalidDataException("bridge-diagnostic-boolean-rejected");diagnosticFields++;}
                            else {
                                int maximum=name=="refreshCode"?4:name=="diagnosticsVersion"?1:86400000;
                                int parsed=ParseDiagnosticInteger(value,maximum);
                                if(name=="diagnosticsVersion")diagnostics=parsed;
                                if(name=="mediaTimeMs")diagnosticFields++;
                            }
                            continue;
                        }
                    }
                    reader.Read();
                }
                if((diagnostics==1 && diagnosticFields!=9) || (diagnostics<0 && diagnosticFields!=0))
                    throw new InvalidDataException("bridge-diagnostics-incomplete");
            }
            using(var stream=new MemoryStream(data,0,count,false))return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
        }
        private static int ParseDiagnosticInteger(string value,int maximum)
        {
            if(value.Length==0 || value.Length>8 || (value.Length>1 && value[0]=='0'))throw new InvalidDataException("bridge-diagnostic-integer-rejected");
            for(int i=0;i<value.Length;i++)if(value[i]<'0' || value[i]>'9')throw new InvalidDataException("bridge-diagnostic-integer-rejected");
            int parsed;if(!int.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out parsed) || parsed>maximum)
                throw new InvalidDataException("bridge-diagnostic-integer-rejected");
            return parsed;
        }
        private static string NewSession()
        {
            byte[] bytes=new byte[16];using(var rng=RandomNumberGenerator.Create())rng.GetBytes(bytes);
            var text=new StringBuilder();foreach(byte b in bytes)text.Append(b.ToString("x2",CultureInfo.InvariantCulture));return text.ToString();
        }
        private static bool Supported(Settings settings)
        {return settings.WallpaperEnabled && string.IsNullOrWhiteSpace(settings.Exe) && string.Equals(settings.AppId,"OpenAI.Codex_2p2nqsd0c76g0!App",StringComparison.OrdinalIgnoreCase);}
        internal static uint WindowProcessId(IntPtr window)
        {uint pid;BridgeNative.GetWindowThreadProcessId(window,out pid);return pid;}
        internal static BridgeSession FindExisting(Settings settings,uint expectedPid,Action<string> log)
        {return Supported(settings) ? Discover(AppDomain.CurrentDomain.BaseDirectory,expectedPid,log) : null;}
        internal static BridgeSession Discover(string root,uint expectedPid,Action<string> log,bool allowUnresolved=false)
        {
            Process process=null;BridgeSession session=null;BridgeStatus observed=null;
            var clock=Stopwatch.StartNew();int phase=1,reason=0;Exception failure=null;
            try {
                string file=Path.Combine(root,"wallpaper-host-session.json");var info=new FileInfo(file);
                if(!info.Exists){reason=1;return null;}
                if(info.Length>2048 || (info.Attributes&FileAttributes.ReparsePoint)!=0){reason=2;return null;}
                phase=2;byte[] bytes=File.ReadAllBytes(file);var item=Decode<BridgeDiscovery>(bytes,bytes.Length);
                PipeName(item.Session);
                if(item.Version!=1 || item.HostPid<=0 || item.HostCreated<=0){reason=3;return null;}
                phase=3;process=Process.GetProcessById(item.HostPid);IntPtr handle=process.Handle;long created;
                string expected=Path.GetFullPath(Path.Combine(root,"WallpaperHost.exe"));
                if(!BridgeNative.IsAlive(handle) || !BridgeNative.CreationTime(handle,out created) || created!=item.HostCreated ||
                    !string.Equals(BridgeNative.ImagePath(handle),expected,StringComparison.OrdinalIgnoreCase)){reason=4;return null;}
                session=new BridgeSession(process,item.Session,log);process=null;
                phase=4;
                try {observed=session.Status(700);}catch(Exception error){failure=error;if(!allowUnresolved)throw;reason=7;}
                if(!allowUnresolved && (observed.Disposition!=2 || (expectedPid!=0 && observed.CodexPid!=(int)expectedPid))){reason=5;return null;}
                phase=5;BridgeSession result=session;session=null;return result;
            }catch(Exception error){failure=error;reason=6;return null;}
            finally {
                BridgeDiagnostics.Record(log,"discover",phase,clock.ElapsedMilliseconds,reason,null,failure,observed);
                if(process!=null)process.Dispose();if(session!=null)session.Dispose();
            }
        }
        internal static BridgeResult OpenOnce(Settings settings,Action<string> log)
        {return OpenCore(settings,AppDomain.CurrentDomain.BaseDirectory,()=>AppActivation.Open(settings),()=>BridgeNative.ExistingOfficialProcess(settings.AppId),log,4000);}
        private static BridgeResult OpenOrdinary(BridgeResult result,Func<uint> ordinary,string code)
        {
            result.Code=code;
            try {result.Pid=ordinary();}catch(Exception failure){result.Error=failure;result.Code="activation-failed";}
            return result;
        }
        // Injected functions are used only by the local fake-host executable;
        // production has no command-line option to override these boundaries.
        internal static BridgeResult OpenCore(Settings settings,string root,Func<uint> ordinary,Func<uint> existing,Action<string> log,int timeout,Action<Process> testObserveSpawn=null)
        {
            var result=new BridgeResult();Process host=null;BridgeStatus observed=null;Exception diagnosticFailure=null;
            var clock=Stopwatch.StartNew();int phase=1;
            try {
                if(!Supported(settings))return OpenOrdinary(result,ordinary,"wallpaper-disabled");
                phase=2;result.Session=Discover(root,0,log,true);
                if(result.Session!=null){
                    BridgeStatus discovered=result.Session.LastStatus;observed=discovered;
                    if(discovered!=null && discovered.Disposition==2){result.Pid=(uint)discovered.CodexPid;result.Code="bridge-reused";return result;}
                    if(discovered!=null && (discovered.Disposition==1 || discovered.Disposition==3)){
                        result.Dispose();return OpenOrdinary(result,ordinary,"bridge-discovery-normal-fallback");
                    }
                    result.Code="bridge-unknown-no-second-launch";return result;
                }
                phase=3;if(existing()!=0)return OpenOrdinary(result,ordinary,"wallpaper-unavailable-existing-process");
                phase=4;string file=Path.Combine(root,"WallpaperHost.exe");
                if(!File.Exists(file))return OpenOrdinary(result,ordinary,"wallpaper-host-missing");
                string session=NewSession();
                try {
                    phase=5;host=Process.Start(new ProcessStartInfo(file,"--launch --session "+session) {
                        UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=root
                    });
                }catch(Exception error){diagnosticFailure=error;return OpenOrdinary(result,ordinary,"wallpaper-host-start-failed");}
                if(host==null)throw new InvalidOperationException("bridge-launch-no-handle");
                if(testObserveSpawn!=null)testObserveSpawn(host);
                phase=6;result.Session=new BridgeSession(host,session,log);host=null;
                BridgeStatus status;
                phase=7;try {status=result.Session.Handshake(timeout);observed=status;}
                catch(Exception error) {diagnosticFailure=error;result.Code="bridge-unknown-no-second-launch";return result;}
                if(status.Disposition==2){result.Pid=(uint)status.CodexPid;result.Code="bridge-started";return result;}
                if(status.Disposition==1 || status.Disposition==3) {
                    result.Dispose();return OpenOrdinary(result,ordinary,status.Disposition==1 ? "bridge-not-started-normal-fallback" : "bridge-existing-normal-reuse");
                }
                result.Code="bridge-unknown-no-second-launch";return result;
            }
            catch(Exception failure) {
                diagnosticFailure=failure;
                if(host!=null || result.Session!=null) {result.Code="bridge-unknown-no-second-launch";}
                else {result.Error=failure;result.Code="activation-failed";}
                return result;
            }
            finally {BridgeDiagnostics.Record(log,"open",phase,clock.ElapsedMilliseconds,0,result.Code,diagnosticFailure??result.Error,observed);if(host!=null)host.Dispose();}
        }
    }

    // This object can only be minted from an authenticated bridge session.
    // A PID returned by ordinary activation is deliberately insufficient.
    internal sealed class BridgeClientIdentity
    {
        internal const string OfficialAppId="OpenAI.Codex_2p2nqsd0c76g0!App";
        internal readonly uint Pid;
        internal readonly long Created;
        internal readonly string Executable;
        private BridgeClientIdentity(uint pid,long created,string executable)
        {Pid=pid;Created=created;Executable=executable;}

        [DataContract] private sealed class OfficialPin
        {
            [DataMember(Name="version",IsRequired=true)] public int Version {get;set;}
            [DataMember(Name="packageFullName",IsRequired=true)] public string PackageFullName {get;set;}
            [DataMember(Name="executableSha256",IsRequired=true)] public string Sha256 {get;set;}
        }
        private static string RegularFile(string file)
        {
            string full=Path.GetFullPath(file);
            if(!Path.IsPathRooted(file) || full.StartsWith(@"\\",StringComparison.Ordinal) || !File.Exists(full))
                throw new InvalidDataException("official-file-unavailable");
            for(string cursor=full;!string.IsNullOrEmpty(cursor);cursor=Path.GetDirectoryName(cursor))
                if((File.GetAttributes(cursor)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("official-file-link-rejected");
            return full;
        }
        private static string PinnedExecutable(string root)
        {
            string config=RegularFile(Path.Combine(Path.GetFullPath(root),"wallpaper-host-config.json"));
            OfficialPin pin;
            using(var input=new FileStream(config,FileMode.Open,FileAccess.Read,FileShare.Read))
            {
                if(input.Length==0 || input.Length>16384)throw new InvalidDataException("official-config-size-rejected");
                // Windows PowerShell 5.1 may write a single UTF-8 BOM. The
                // JSON stream reader does not consume it; accept only that
                // exact prefix and leave every other byte to the same parser.
                int first=input.ReadByte(),second=input.ReadByte(),third=input.ReadByte();
                input.Position=first==0xEF && second==0xBB && third==0xBF ? 3 : 0;
                pin=(OfficialPin)new DataContractJsonSerializer(typeof(OfficialPin)).ReadObject(input);
            }
            if(pin.Version!=1 || pin.Sha256==null || !System.Text.RegularExpressions.Regex.IsMatch(pin.Sha256,@"\A[0-9a-fA-F]{64}\z"))
                throw new InvalidDataException("official-pin-rejected");
            string executable=BridgeNative.RegisteredExecutable(pin.PackageFullName);
            if(executable==null)throw new InvalidDataException("official-package-not-registered");
            executable=RegularFile(executable);
            string hash;
            using(var input=new FileStream(executable,FileMode.Open,FileAccess.Read,FileShare.Read,65536,FileOptions.SequentialScan))
            using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(input)).Replace("-","");
            if(!string.Equals(hash,pin.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("official-executable-hash-rejected");
            return executable;
        }
        internal bool Matches(uint pid,long created,string executable)
        {return SameSnapshot(Pid,Created,Executable,pid,created,executable);}
        internal static bool SameSnapshot(uint expectedPid,long expectedCreated,string expectedExe,uint actualPid,long actualCreated,string actualExe)
        {
            return expectedPid>0 && expectedCreated>0 && expectedPid==actualPid && expectedCreated==actualCreated &&
                !string.IsNullOrWhiteSpace(expectedExe) && !string.IsNullOrWhiteSpace(actualExe) &&
                string.Equals(expectedExe,actualExe,StringComparison.OrdinalIgnoreCase);
        }
        private static bool IsLiveInstance(uint pid,long created,string executable)
        {
            if(pid==0 || pid>int.MaxValue || created<=0)return false;
            try {using(var process=Process.GetProcessById((int)pid))using(var own=Process.GetCurrentProcess())
            {
                long actualCreated;IntPtr handle=process.Handle;
                return process.SessionId==own.SessionId && BridgeNative.IsAlive(handle) &&
                    BridgeNative.CreationTime(handle,out actualCreated) &&
                    SameSnapshot(pid,created,executable,(uint)process.Id,actualCreated,BridgeNative.ImagePath(handle));
            }}catch{return false;}
        }
        // Read-only validation is also used by the isolated test executable.
        // It cannot mint a window-control capability without a bridge session.
        internal static bool ValidateOfficialProcess(string root,uint pid,long created)
        {try{return IsLiveInstance(pid,created,PinnedExecutable(root));}catch{return false;}}
        internal static BridgeClientIdentity Attest(Settings settings,string root,BridgeSession session)
        {
            try
            {
                if(settings==null || !settings.WallpaperEnabled || !string.IsNullOrWhiteSpace(settings.Exe) ||
                    !string.Equals(settings.AppId,OfficialAppId,StringComparison.OrdinalIgnoreCase) || session==null)return null;
                BridgeStatus status=session.LastStatus;
                if(status==null || status.Version!=1 || status.Disposition!=2 || status.CodexPid<=0 || status.CodexCreated<=0)return null;
                // Session construction and pipe replies bind the retained host
                // handle; its own identity must still be alive at attestation.
                Process host=session.RetainedProcess;long hostCreated;
                if(host.Id!=status.HostPid || !BridgeNative.IsAlive(host.Handle) ||
                    !BridgeNative.CreationTime(host.Handle,out hostCreated) || hostCreated!=status.HostCreated)return null;
                string executable=PinnedExecutable(root);
                if(!IsLiveInstance((uint)status.CodexPid,status.CodexCreated,executable))return null;
                return new BridgeClientIdentity((uint)status.CodexPid,status.CodexCreated,executable);
            }
            catch{return null;}
        }
    }

    internal static class BridgeNative
    {
        [DllImport("kernel32.dll",SetLastError=true)] internal static extern bool GetNamedPipeServerProcessId(IntPtr pipe,out uint pid);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
        [DllImport("kernel32.dll",SetLastError=true)] private static extern bool GetExitCodeProcess(IntPtr process,out uint code);
        [DllImport("kernel32.dll",SetLastError=true)] private static extern uint WaitForSingleObject(IntPtr handle,uint milliseconds);
        [DllImport("kernel32.dll",SetLastError=true)] private static extern bool GetProcessTimes(IntPtr process,out long created,out long exited,out long kernel,out long user);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern bool QueryFullProcessImageName(IntPtr process,uint flags,StringBuilder name,ref uint size);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] private static extern int GetApplicationUserModelId(IntPtr process,ref uint length,StringBuilder appId);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] private static extern int GetPackageFamilyName(IntPtr process,ref uint length,StringBuilder family);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] private static extern int GetPackagesByPackageFamily(string family,ref uint count,IntPtr names,ref uint bufferLength,IntPtr buffer);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] private static extern int GetPackagePathByFullName(string name,ref uint length,StringBuilder path);
        private static List<string> RegisteredPackageNames()
        {
            const string family="OpenAI.Codex_2p2nqsd0c76g0";
            var result=new List<string>();uint count=0,chars=0;
            if(GetPackagesByPackageFamily(family,ref count,IntPtr.Zero,ref chars,IntPtr.Zero)!=122 || count==0 || count>32 || chars<2 || chars>32768)return result;
            IntPtr names=Marshal.AllocHGlobal(checked((int)count*IntPtr.Size)),buffer=Marshal.AllocHGlobal(checked((int)chars*2));
            try
            {
                uint capacity=count;
                if(GetPackagesByPackageFamily(family,ref count,names,ref chars,buffer)!=0 || count>capacity)return result;
                for(int i=0;i<count;i++)
                {
                    string name=Marshal.PtrToStringUni(Marshal.ReadIntPtr(names,i*IntPtr.Size));
                    if(name!=null && System.Text.RegularExpressions.Regex.IsMatch(name,@"\AOpenAI\.Codex_[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+_(?:x64|x86|arm64)__2p2nqsd0c76g0\z"))result.Add(name);
                }
            }
            finally{Marshal.FreeHGlobal(names);Marshal.FreeHGlobal(buffer);}
            return result;
        }
        private static string ExecutableForRegisteredName(string name)
        {
            uint length=0;if(GetPackagePathByFullName(name,ref length,null)!=122 || length<2 || length>32768)return null;
            var path=new StringBuilder((int)length);
            if(GetPackagePathByFullName(name,ref length,path)!=0)return null;
            return Path.GetFullPath(Path.Combine(path.ToString(),"app","ChatGPT.exe"));
        }
        internal static string RegisteredExecutable(string fullName)
        {
            if(fullName==null)return null;
            foreach(string name in RegisteredPackageNames())if(string.Equals(name,fullName,StringComparison.Ordinal))return ExecutableForRegisteredName(name);
            return null;
        }
        internal static bool IsAlive(IntPtr process){uint code;return GetExitCodeProcess(process,out code) && code==259;}
        internal static bool HasExited(IntPtr process){return process!=IntPtr.Zero && WaitForSingleObject(process,0)==0;}
        internal static bool CreationTime(IntPtr process,out long created){long exited,kernel,user;return GetProcessTimes(process,out created,out exited,out kernel,out user);}
        internal static string ImagePath(IntPtr process)
        {
            // Most installed paths fit in 512 characters. Do not allocate a
            // 64 KiB buffer on every window identity check or process scan.
            // Long paths still receive the full Windows-supported capacity.
            for(int capacity=512;capacity<=32768;capacity*=2) {
                uint size=(uint)capacity;var path=new StringBuilder(capacity);
                if(QueryFullProcessImageName(process,0,path,ref size))return path.ToString();
                if(Marshal.GetLastWin32Error()!=122)return "";
            }
            return "";
        }
        internal static uint ExistingOfficialProcess(string appId)
        {
            int ownSession;using(var own=Process.GetCurrentProcess())ownSession=own.SessionId;
            string family=appId.Split('!')[0];
            var registeredExecutables=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if(string.Equals(appId,BridgeClientIdentity.OfficialAppId,StringComparison.OrdinalIgnoreCase))
                foreach(string name in RegisteredPackageNames())
                {string path=ExecutableForRegisteredName(name);if(path!=null)registeredExecutables.Add(path);}
            foreach(Process process in Process.GetProcesses())using(process)try {
                if(process.SessionId!=ownSession || !IsAlive(process.Handle))continue;
                uint length=512;var actual=new StringBuilder(512);
                if(GetApplicationUserModelId(process.Handle,ref length,actual)==0 && string.Equals(actual.ToString(),appId,StringComparison.OrdinalIgnoreCase))return (uint)process.Id;
                length=512;actual.Clear();
                if(GetPackageFamilyName(process.Handle,ref length,actual)==0 && string.Equals(actual.ToString(),family,StringComparison.OrdinalIgnoreCase))return (uint)process.Id;
                // Duplicate-launch prevention only. This broad registered-path
                // check never grants HWND control; that needs the exact bridge
                // capability above, including creation time and pinned hash.
                if(registeredExecutables.Contains(ImagePath(process.Handle)))return (uint)process.Id;
            }catch{ /* inaccessible/unrelated processes are never touched */ }
            return 0;
        }
    }
}


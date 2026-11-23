using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace CodexDragonWallpaper {
internal sealed class HostState {
    internal readonly object Gate=new object();
    internal readonly int HostPid;
    internal readonly long HostCreated;
    internal HostState(){using(var process=Process.GetCurrentProcess()){HostPid=process.Id;HostCreated=process.StartTime.ToUniversalTime().ToFileTimeUtc();}}
    internal int Disposition,CodexPid,MediaCode;internal long Created;
    internal bool Ready,Revealed,Disabled,RequestedReveal,RequestedPause,RevealEverRequested;
    internal int DiagnosticsVersion,MediaTimeMs;
    internal bool LayoutSupported,ProbeOnly,Attached,Playing,WallpaperApplied,FirstFrameConfirmed,DocumentHidden,StageVisible;
    internal bool RefreshAvailable;internal long RefreshRequested,RefreshTaken,RefreshCompleted;int refreshResult;
    internal readonly string DocumentGeneration=Guid.NewGuid().ToString("N");
    internal long MediaGeneration,ObservedAt,Sequence,RendererClockMs,RendererSampleReceived;internal string ActualPhase="preparing",Health="stale",DesiredMode="enabled",Reason="",PrepareOperationId="";
    internal bool CommitAccepted,Prepared;internal string RequestedPrepare,RequestedCommit;
    internal long OperationRequested,OperationCompleted,IntentRevision,OperationStartedAt;internal int OperationTimeout;int operationResult;
    internal readonly RecoveryBudget Recovery=new RecoveryBudget();
    internal readonly AutoResetEvent Work=new AutoResetEvent(false);
    internal readonly ManualResetEvent TerminalRead=new ManualResetEvent(false);
    internal string Snapshot(){return Snapshot(0);}
    string Snapshot(int refreshCode){lock(Gate){long age=ObservedAt==0?86400000:Math.Min(86400000,Math.Max(0,(Stopwatch.GetTimestamp()-ObservedAt)*1000/Stopwatch.Frequency));return Json.Serializer().Serialize(Json.Obj("version",1,"protocolVersion",2,"documentGeneration",DocumentGeneration,"mediaGeneration",MediaGeneration,"observationAgeMs",age,"actualPhase",ActualPhase,"health",age>30000&&Health=="fresh"?"stale":Health,"desiredMode",DesiredMode,"reason",Reason,"sequence",Sequence,"prepareOperationId",PrepareOperationId,"prepared",Prepared,"commitAccepted",CommitAccepted,
        "disposition",Disposition,"hostPid",HostPid,"hostCreationFileTime",HostCreated,"codexPid",CodexPid,"creationFileTime",Created,"mediaCode",MediaCode,"ready",Ready&&age<=30000,"revealed",Revealed,"disabled",Disabled,"refreshCode",refreshCode,
        "diagnosticsVersion",DiagnosticsVersion,"layoutSupported",LayoutSupported,"probeOnly",ProbeOnly,"attached",Attached,"playing",Playing,"wallpaperApplied",WallpaperApplied,"firstFrameConfirmed",FirstFrameConfirmed,"documentHidden",DocumentHidden,"stageVisible",StageVisible,"mediaTimeMs",MediaTimeMs));}}
    internal void ClearDiagnostics(){lock(Gate){DiagnosticsVersion=0;MediaTimeMs=0;LayoutSupported=ProbeOnly=Attached=Playing=WallpaperApplied=FirstFrameConfirmed=DocumentHidden=StageVisible=false;}}
    internal string RefreshSnapshot(int timeout){lock(Gate){
        // status remains an immediate startup snapshot. Only this explicit
        // user command waits for one fixed, guarded renderer status query.
        if(!RefreshAvailable||Disabled)return Snapshot(2);
        long request=++RefreshRequested;Work.Set();var clock=Stopwatch.StartNew();
        while(RefreshCompleted<request&&RefreshAvailable&&!Disabled){int remaining=Deadline.Remaining(timeout,clock.ElapsedMilliseconds);if(remaining<=0)break;Monitor.Wait(Gate,remaining);}
        return Snapshot(RefreshCompleted>=request?refreshResult:(!RefreshAvailable||Disabled?2:3));
    }}
    internal void CompleteRefresh(long request,int result){lock(Gate){
        if(request>RefreshCompleted&&request<=RefreshRequested){RefreshCompleted=request;refreshResult=result;if(result==4){RefreshAvailable=false;Ready=false;MediaCode=8;ClearDiagnostics();}Monitor.PulseAll(Gate);}
    }}
    internal void EndRefresh(){lock(Gate){RefreshAvailable=false;Monitor.PulseAll(Gate);}}
    internal void Stale(string reason){lock(Gate){Health="stale";Reason=reason;Ready=false;Prepared=false;Sequence++;Monitor.PulseAll(Gate);}Work.Set();}
    internal string OperationSnapshot(string command,string operation,int timeout){lock(Gate){
        if(Disabled||DesiredMode!="enabled"||!RefreshAvailable)return Snapshot(2);
        long request=++OperationRequested;if(command=="prepare")RequestedPrepare=operation;else RequestedCommit=operation;
        OperationStartedAt=Stopwatch.GetTimestamp();OperationTimeout=timeout;
        Work.Set();var clock=Stopwatch.StartNew();
        while(OperationCompleted<request&&!Disabled&&RefreshAvailable){int left=Deadline.Remaining(timeout,clock.ElapsedMilliseconds);if(left<=0)break;Monitor.Wait(Gate,left);}
        return Snapshot(OperationCompleted>=request?operationResult:3);
    }}
    internal void CompleteOperation(long request,int result){lock(Gate){if(request>OperationCompleted){OperationCompleted=request;operationResult=result;Monitor.PulseAll(Gate);}}}
    internal void Started(int pid,long created){lock(Gate){CodexPid=pid;Created=created;Disposition=2;MediaCode=1;}}
    internal void NoStart(int code){lock(Gate){if(Disposition!=2){Disposition=1;MediaCode=code;}}}
    internal void Existing(int pid,long created){lock(Gate){if(Disposition!=2){CodexPid=pid;Created=created;Disposition=3;MediaCode=0;}}}
    internal void MediaError(int code){lock(Gate){MediaCode=code;Ready=false;RefreshAvailable=false;Disabled=true;RequestedReveal=false;RequestedPause=false;RequestedPrepare=RequestedCommit=null;Prepared=false;Health="faulted";ActualPhase="retired";ClearDiagnostics();Monitor.PulseAll(Gate);}Work.Set();}
    internal void Command(string command){lock(Gate){
        if(command=="status")return;
        if(command=="disable"){IntentRevision++;Disabled=true;DesiredMode="disabled";RequestedReveal=false;RequestedPause=true;RequestedPrepare=RequestedCommit=null;Ready=Prepared=false;Health="stale";ClearDiagnostics();}
        else if(command=="pause"){IntentRevision++;DesiredMode="user-paused";RequestedReveal=false;RequestedPause=true;RequestedPrepare=RequestedCommit=null;PrepareOperationId="";CommitAccepted=false;}
        else if(command=="reveal"){if(!Disabled){DesiredMode="enabled";RequestedReveal=true;RequestedPause=false;RevealEverRequested=true;}}
        else if(command!="status")throw new HostFailure(40);
    }Work.Set();}
}
internal sealed class ControlPipe : IDisposable {
    readonly HostState state;readonly string fullName;readonly object pipeGate=new object();volatile bool stop;NamedPipeServerStream server;
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string descriptor,uint revision,out IntPtr security,out uint size);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr memory);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern SafePipeHandle CreateNamedPipe(string name,uint openMode,uint pipeMode,uint instances,uint outSize,uint inSize,uint timeout,ref Native.SecurityAttributes attrs);
    internal static string Sid {get{using(var identity=WindowsIdentity.GetCurrent())return identity.User.Value;}}
    internal static string SidHash {get{return Files.HashText(Sid).Substring(0,16);}}
    internal static string Name(string session){if(!System.Text.RegularExpressions.Regex.IsMatch(session??"","^[a-f0-9]{32}$"))throw new HostFailure(40);return "CodexDragon.Wallpaper."+SidHash+"."+session;}
    internal ControlPipe(HostState status,string session){state=status;fullName=@"\\.\pipe\"+Name(session);}
    NamedPipeServerStream CreateServer(){
        IntPtr descriptor;uint length;if(!ConvertStringSecurityDescriptorToSecurityDescriptor("D:P(A;;GA;;;"+Sid+")",1,out descriptor,out length))throw new HostFailure(41);
        try {var sa=new Native.SecurityAttributes{Size=Marshal.SizeOf(typeof(Native.SecurityAttributes)),Descriptor=descriptor,Inherit=false};
            // Duplex, overlapped, first-instance; byte protocol, remote rejected.
            SafePipeHandle handle=CreateNamedPipe(fullName,3|0x40000000|0x00080000,0x00000008,1,4096,128,1000,ref sa);
            if(handle.IsInvalid){handle.Dispose();throw new HostFailure(41);}
            return new NamedPipeServerStream(PipeDirection.InOut,true,false,handle);
        }finally{LocalFree(descriptor);}
    }
    internal void Start(){
        lock(pipeGate){if(stop||server!=null)throw new HostFailure(41);server=CreateServer();}
        var thread=new Thread(Loop){IsBackground=true,Name="Wallpaper local control"};thread.Start();
    }
    void Loop(){
        while(!stop){NamedPipeServerStream connection;lock(pipeGate){connection=server;}if(connection==null){if(!stop)state.Command("disable");return;}
        try{
            connection.WaitForConnection();var input=new byte[81];int used=0;bool complete=false;var deadline=Stopwatch.StartNew();
            while(used<80){int remaining=Deadline.Remaining(1500,deadline.ElapsedMilliseconds);if(remaining<=0)throw new TimeoutException();Task<int> read=Task<int>.Factory.FromAsync(connection.BeginRead,connection.EndRead,input,used,1,null);
                if(!read.Wait(remaining)){read.ContinueWith(t=>{var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);throw new TimeoutException();}int n=read.Result;if(n==0)break;if(input[used++]==10){complete=true;break;}}
            if(complete){string command=new UTF8Encoding(false,true).GetString(input,0,used).TrimEnd('\r','\n');
                string[] parts=command.Split(' ');bool operation=parts.Length==2&&(parts[0]=="prepare"||parts[0]=="commit")&&System.Text.RegularExpressions.Regex.IsMatch(parts[1],"^[a-f0-9]{32}$");
                if(operation||command=="status"||command=="refresh"||command=="reveal"||command=="pause"||command=="disable"){
                    int remaining=Deadline.Remaining(1500,deadline.ElapsedMilliseconds);if(remaining<=0)throw new TimeoutException();
                    string snapshot;if(operation)snapshot=state.OperationSnapshot(parts[0],parts[1],Math.Min(1000,remaining));else if(command=="refresh")snapshot=state.RefreshSnapshot(Math.Min(1000,remaining));else{state.Command(command);snapshot=state.Snapshot();}
                    byte[] reply=Encoding.UTF8.GetBytes(snapshot+"\n");Task write=Task.Factory.FromAsync(connection.BeginWrite,connection.EndWrite,reply,0,reply.Length,null);
                    remaining=Deadline.Remaining(1500,deadline.ElapsedMilliseconds);if(remaining<=0||!write.Wait(remaining)){write.ContinueWith(t=>{var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);throw new TimeoutException();}
                    // Keep the response available until the one-shot client
                    // closes. WaitForPipeDrain uses uncancellable synchronous
                    // FlushFileBuffers: an unread reply can pin that handle
                    // forever and prevent the first-instance pipe reopening.
                    // This overlapped read is cancelled by Dispose on timeout.
                    var tail=new byte[1];Task<int> closed=Task<int>.Factory.FromAsync(connection.BeginRead,connection.EndRead,tail,0,1,null);
                    remaining=Deadline.Remaining(1500,deadline.ElapsedMilliseconds);if(remaining<=0||!closed.Wait(remaining)){closed.ContinueWith(t=>{var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);throw new TimeoutException();}
                    lock(state.Gate){if(state.Disposition==1||state.Disposition==3)state.TerminalRead.Set();}
                }
            }
        }catch(Exception error){
            Exception cause=error.GetBaseException();int code=cause.HResult&0xffff;
            bool clientFailure=cause is TimeoutException||cause is DecoderFallbackException||
                (cause is IOException&&(code==109||code==232||code==233));
            if(!clientFailure)stop=true;
            // An abandoned, stalled or malformed client only loses its own
            // connection. Dispose cancels outstanding IO before replacing it.
            // Unexpected server faults stop only control, never CDP.
        }
        finally{
            lock(pipeGate){
                try{connection.Dispose();}catch{}
                if(Object.ReferenceEquals(server,connection))server=null;
                if(!stop){try{server=CreateServer();}catch{stop=true;}}
            }
        }
        }
        // Unexpected server failure must not leave an uncontrollable animation
        // decoding forever. The session removes decoration; Main keeps the
        // official process and its CDP pipe alive until the user exits it.
        state.Command("disable");
    }
    internal static void PrivateFile(string file,string text){
        string temp=file+"."+Guid.NewGuid().ToString("N")+".tmp";var security=new FileSecurity();security.SetAccessRuleProtection(true,false);using(var identity=WindowsIdentity.GetCurrent()){security.AddAccessRule(new FileSystemAccessRule(identity.User,FileSystemRights.FullControl,AccessControlType.Allow));}
        using(var stream=new FileStream(temp,FileMode.CreateNew,FileSystemRights.Write,FileShare.None,4096,FileOptions.None,security)){byte[] bytes=Encoding.UTF8.GetBytes(text);stream.Write(bytes,0,bytes.Length);stream.Flush(true);}
        try{if(File.Exists(file))File.Replace(temp,file,null);else File.Move(temp,file);}finally{if(File.Exists(temp))File.Delete(temp);}
    }
    public void Dispose(){lock(pipeGate){stop=true;if(server!=null){server.Dispose();server=null;}}}
}
}

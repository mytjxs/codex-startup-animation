using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace DragonLauncher {
// Independent, short-lived compensation for this launcher's native mutations.
// It never starts/stops Codex. Parent crash, suspended/blocked parent UI, and a
// failed restore can be handled while this helper and Windows are responsive.
// Simultaneous parent/helper death, OS failure and window-thread failure remain
// explicit boundaries; a stale on-disk record is never blindly replayed.
internal sealed class NativeRestoreGuard:IDisposable {
    internal const int UiHeartbeatFreshMilliseconds=1500,HeartbeatReadDeadlineMilliseconds=2500,RecoveryBudgetMilliseconds=800;
    [StructLayout(LayoutKind.Sequential)] internal struct Point {internal int X,Y;}
    [StructLayout(LayoutKind.Sequential)] internal struct Rect {internal int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)] internal struct Placement {internal uint Length,Flags,Show;internal Point Min,Max;internal Rect Normal;}
    [DataContract] internal sealed class Record {
        [DataMember] public string Token {get;set;}
        [DataMember] public long Window {get;set;}
        [DataMember] public int Pid {get;set;}
        [DataMember] public long Created {get;set;}
        [DataMember] public string Exe {get;set;}
        [DataMember] public int Left {get;set;}
        [DataMember] public int Top {get;set;}
        [DataMember] public int Right {get;set;}
        [DataMember] public int Bottom {get;set;}
        [DataMember] public uint Show {get;set;}
        [DataMember] public int ExpectedLeft {get;set;}
        [DataMember] public int ExpectedTop {get;set;}
        [DataMember] public int ExpectedRight {get;set;}
        [DataMember] public int ExpectedBottom {get;set;}
        [DataMember] public int Transition {get;set;}
        [DataMember] public int ScreenLeft {get;set;}
        [DataMember] public int ScreenTop {get;set;}
        [DataMember] public int ScreenRight {get;set;}
        [DataMember] public int ScreenBottom {get;set;}
        internal bool PositionRestoreRequested;
    }
    internal sealed class Lease {
        internal readonly Record Record;
        private readonly NativeRestoreGuard owner;
        internal volatile bool Armed,Failed;
        internal Lease(Record record,NativeRestoreGuard owner){Record=record;this.owner=owner;}
        internal bool Available {get{return !Failed&&owner.Ready;}}
        internal bool Owned {get{return GetProp(new IntPtr(Record.Window),Property(Record.Token))==new IntPtr(1);}}
        internal void Release(){RemoveProp(new IntPtr(Record.Window),Property(Record.Token));}
    }
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr w);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr w);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr w);
    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr w);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr w,out uint pid);
    [DllImport("user32.dll")] static extern bool GetWindowPlacement(IntPtr w,ref Placement p);
    [DllImport("user32.dll")] static extern bool SetWindowPlacement(IntPtr w,ref Placement p);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr w,out Rect p);
    [DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr w,int command);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool SetProp(IntPtr w,string name,IntPtr value);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr GetProp(IntPtr w,string name);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr RemoveProp(IntPtr w,string name);
    [DllImport("dwmapi.dll",EntryPoint="DwmGetWindowAttribute")] static extern int GetTransition(IntPtr w,int a,out int value,int size);
    [DllImport("dwmapi.dll",EntryPoint="DwmSetWindowAttribute")] static extern int SetTransition(IntPtr w,int a,ref int value,int size);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetNamedPipeClientProcessId(IntPtr pipe,out uint pid);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetNamedPipeServerProcessId(IntPtr pipe,out uint pid);
    [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr value);
    static IntPtr Physical(){try{return SetThreadDpiAwarenessContext(new IntPtr(-4));}catch(EntryPointNotFoundException){return IntPtr.Zero;}}
    static void LeavePhysical(IntPtr old){if(old!=IntPtr.Zero)SetThreadDpiAwarenessContext(old);}
    private readonly object gate=new object();
    private readonly Queue<Lease> pending=new Queue<Lease>();
    private readonly NamedPipeServerStream pipe;
    private readonly Process child;
    private readonly string journalFolder;
    private readonly Stopwatch clock=Stopwatch.StartNew();
    private long uiHeartbeat;
    private volatile bool closing,ready,failed;
    internal string FailureDetail {get;private set;}
    internal bool Ready {get{return ready&&!failed&&!closing;}}
    internal bool Faulted {get{return failed||closing;}}
    internal static NativeRestoreGuard Current;
    private static string Property(string token){return "CodexDragon.Restore."+token;}
    internal NativeRestoreGuard(string executable) {
        string token=Guid.NewGuid().ToString("N"),name="CodexDragon.Restore."+token;
        journalFolder=JournalFolder(executable,token);
        var security=new PipeSecurity();security.SetAccessRuleProtection(true,false);
        using(var identity=WindowsIdentity.GetCurrent())security.AddAccessRule(new PipeAccessRule(identity.User,PipeAccessRights.FullControl,AccessControlType.Allow));
        pipe=new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,4096,4096,security);
        try {
            using(var own=Process.GetCurrentProcess())child=Process.Start(new ProcessStartInfo(executable,"--restore-guard "+own.Id+" "+own.StartTime.ToUniversalTime().ToFileTimeUtc()+" "+token){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden});
            if(child==null)throw new IOException("restore-guard-process-unavailable");
            new Thread(Worker){IsBackground=true,Name="Native restore guard connection"}.Start();
        } catch {pipe.Dispose();if(child!=null)child.Dispose();throw;}
    }
    internal void Pulse(){Interlocked.Exchange(ref uiHeartbeat,clock.ElapsedMilliseconds);}
    internal Lease Arm(IntPtr window,int l,int t,int r,int b) {
        IntPtr dpi=Physical();try{return ArmPhysical(window,l,t,r,b);}finally{LeavePhysical(dpi);}
    }
    private Lease ArmPhysical(IntPtr window,int l,int t,int r,int b) {
        if(!Ready)return null;
        uint pid;if(!IsWindow(window)||GetWindowThreadProcessId(window,out pid)==0)return null;
        Placement p=new Placement{Length=(uint)Marshal.SizeOf(typeof(Placement))};if(!GetWindowPlacement(window,ref p))return null;
        Record record;
        using(var process=Process.GetProcessById((int)pid))record=new Record{Token=Guid.NewGuid().ToString("N"),Window=window.ToInt64(),Pid=(int)pid,Created=process.StartTime.ToUniversalTime().ToFileTimeUtc(),Exe=process.MainModule.FileName,
            Left=p.Normal.Left,Top=p.Normal.Top,Right=p.Normal.Right,Bottom=p.Normal.Bottom,Show=p.Show,ExpectedLeft=l,ExpectedTop=t,ExpectedRight=r,ExpectedBottom=b,Transition=-1};
        Rect screen;if(!GetWindowRect(window,out screen))return null;
        record.ScreenLeft=screen.Left;record.ScreenTop=screen.Top;record.ScreenRight=screen.Right;record.ScreenBottom=screen.Bottom;
        int transition;if(GetTransition(window,3,out transition,4)==0)record.Transition=transition;
        if(!SetProp(window,Property(record.Token),new IntPtr(1)))return null;
        var lease=new Lease(record,this);
        lock(gate){if(closing||failed||pending.Count>=8){lease.Release();return null;}pending.Enqueue(lease);}
        return lease;
    }
    private void Worker() {
        Lease activeLease=null;
        try {
            CompleteIo(pipe,callback=>pipe.BeginWaitForConnection(callback,null),result=>{pipe.EndWaitForConnection(result);return 0;},5000,"restore-guard-connect-deadline");uint pid;
            if(!GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(),out pid)||pid!=(uint)child.Id)throw new InvalidDataException("restore-guard-client-identity");
            WriteLine(pipe,"hello");if(ReadLine(pipe,3000)!="ready")throw new InvalidDataException("restore-guard-handshake");ready=true;
            while(!closing){
                Lease lease=null;lock(gate)if(pending.Count>0)lease=pending.Dequeue();
                if(lease!=null){
                    activeLease=lease;
                    WriteLine(pipe,"arm "+Serialize(lease.Record));
                    string reply=ReadLine(pipe,3000);
                    if(reply=="armed "+lease.Record.Token){lease.Armed=true;activeLease=null;}else{lease.Failed=true;throw new IOException("restore-guard-arm-rejected "+reply);}
                }else if(clock.ElapsedMilliseconds-Interlocked.Read(ref uiHeartbeat)<UiHeartbeatFreshMilliseconds){
                    WriteLine(pipe,"pulse");if(ReadLine(pipe,2000)!="ok")throw new IOException("restore-guard-heartbeat-rejected");
                }
                Thread.Sleep(100);
            }
        }catch(Exception e){FailureDetail=e.GetType().Name+" "+e.Message;failed=true;if(activeLease!=null)activeLease.Failed=true;LauncherDiagnostics.Write("handoff-latest.log","Native recovery guard stopped: "+FailureDetail);}
        finally{ready=false;lock(gate)while(pending.Count>0)pending.Dequeue().Failed=true;pipe.Dispose();child.Dispose();}
    }
    public void Dispose(){closing=true;if(Object.ReferenceEquals(Current,this))Current=null;}
    private static string JournalFolder(string executable,string token){
        string key;using(var sha=SHA256.Create())key=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(executable).ToUpperInvariant()))).Replace("-","").Substring(0,16);
        string root=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if(String.IsNullOrWhiteSpace(root))throw new IOException("restore-guard-profile-unavailable");
        // Installation paths can already be near MAX_PATH. Keep durable
        // records in a short per-user, per-executable session directory.
        string folder=Path.Combine(root,"CodexDragon","RestoreGuard",key,token);
        if(folder.Length+38>=260)throw new PathTooLongException("restore-guard-profile-path");
        return folder;
    }
    static string Serialize(Record r){using(var s=new MemoryStream()){new DataContractJsonSerializer(typeof(Record)).WriteObject(s,r);return Encoding.UTF8.GetString(s.ToArray());}}
    static Record Deserialize(string s){using(var input=new MemoryStream(Encoding.UTF8.GetBytes(s)))return (Record)new DataContractJsonSerializer(typeof(Record)).ReadObject(input);}
    // A timeout cancels this entire pipe. FromAsync retains the native APM
    // operation until End* completes; no pending AsyncWaitHandle is disposed.
    // Every connection has at most one in-flight operation, and faults from a
    // cancelled operation are observed without creating a dedicated thread.
    private static int CompleteIo(Stream pipe,Func<AsyncCallback,IAsyncResult> begin,Func<IAsyncResult,int> end,int ms,string message){
        var task=System.Threading.Tasks.Task<int>.Factory.FromAsync((callback,state)=>begin(callback),end,null);
        task.ContinueWith(done=>{var observed=done.Exception;},System.Threading.CancellationToken.None,System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted|System.Threading.Tasks.TaskContinuationOptions.ExecuteSynchronously,System.Threading.Tasks.TaskScheduler.Default);
        try{if(!task.Wait(ms)){pipe.Dispose();throw new TimeoutException(message);}return task.GetAwaiter().GetResult();}
        catch(AggregateException error){throw error.InnerException??error;}
    }
    static void WriteLine(Stream pipe,string value){byte[] b=Encoding.UTF8.GetBytes(value+"\n");CompleteIo(pipe,callback=>pipe.BeginWrite(b,0,b.Length,callback,null),result=>{pipe.EndWrite(result);return 0;},2000,"restore-guard-write-deadline");}
    static string ReadLine(Stream pipe,int ms){var bytes=new List<byte>();var byteOne=new byte[1];var elapsed=Stopwatch.StartNew();while(bytes.Count<8192){long remainingLong=(long)ms-elapsed.ElapsedMilliseconds;if(remainingLong<=0)throw new TimeoutException("restore-guard-read-deadline");int remaining=(int)remainingLong;if(CompleteIo(pipe,callback=>pipe.BeginRead(byteOne,0,1,callback,null),result=>pipe.EndRead(result),remaining,"restore-guard-read-deadline")==0)throw new EndOfStreamException();if(byteOne[0]==10)return new UTF8Encoding(false,true).GetString(bytes.ToArray());bytes.Add(byteOne[0]);}throw new InvalidDataException("restore-guard-message-oversize");}
    static bool Identity(Record r){try{uint pid;if(r==null||!StartupPreparation.ValidId(r.Token)||!IsWindow(new IntPtr(r.Window))||GetWindowThreadProcessId(new IntPtr(r.Window),out pid)==0||pid!=r.Pid||GetProp(new IntPtr(r.Window),Property(r.Token))!=new IntPtr(1))return false;using(var p=Process.GetProcessById(r.Pid))return p.StartTime.ToUniversalTime().ToFileTimeUtc()==r.Created&&String.Equals(p.MainModule.FileName,r.Exe,StringComparison.OrdinalIgnoreCase);}catch(ArgumentException){return false;}catch(InvalidOperationException){return false;}}
    internal static bool Restore(Record r){
        IntPtr dpi=Physical();try{return RestorePhysical(r);}finally{LeavePhysical(dpi);}
    }
    private static bool RestorePhysical(Record r){
        if(!Identity(r))return false;IntPtr w=new IntPtr(r.Window);bool restored=true,restorePosition=false;
        Placement current=new Placement{Length=(uint)Marshal.SizeOf(typeof(Placement))};Rect bounds;
        bool hidden=!IsWindowVisible(w),atOwned=GetWindowRect(w,out bounds)&&bounds.Left==r.ExpectedLeft&&bounds.Top==r.ExpectedTop&&bounds.Right==r.ExpectedRight&&bounds.Bottom==r.ExpectedBottom;
        if(GetWindowPlacement(w,ref current)){
            bool atOriginal=current.Normal.Left==r.Left&&current.Normal.Top==r.Top&&current.Normal.Right==r.Right&&current.Normal.Bottom==r.Bottom;
            restorePosition=atOwned||(hidden&&atOriginal);
            if(restorePosition&&!r.PositionRestoreRequested){current.Flags|=4;current.Show=r.Show;current.Normal=new Rect{Left=r.Left,Top=r.Top,Right=r.Right,Bottom=r.Bottom};restored=SetWindowPlacement(w,ref current);r.PositionRestoreRequested=restored;}
            // Never move a user-modified window. Hidden state is independently
            // compensated even when its rectangle has been changed externally.
            if(hidden)ShowWindowAsync(w,r.Show==0?4:(int)r.Show);
        }else restored=false;
        restored=RestoreTransition(r)&&restored;
        // Async native requests are not completion. Keep the marker while the
        // target still has not processed its show; helper cleanup retries with
        // a small bounded budget, off the animation thread.
        restored=restored&&IsWindowVisible(w);
        if(restorePosition||r.PositionRestoreRequested){
            Placement confirmed=new Placement{Length=(uint)Marshal.SizeOf(typeof(Placement))};Rect actual;
            bool originalPlacement=GetWindowPlacement(w,ref confirmed)&&confirmed.Normal.Left==r.Left&&confirmed.Normal.Top==r.Top&&confirmed.Normal.Right==r.Right&&confirmed.Normal.Bottom==r.Bottom;
            bool originalSurface=r.Show==2?IsIconic(w):r.Show==3?IsZoomed(w):GetWindowRect(w,out actual)&&actual.Left==r.ScreenLeft&&actual.Top==r.ScreenTop&&actual.Right==r.ScreenRight&&actual.Bottom==r.ScreenBottom;
            restored=restored&&originalPlacement&&originalSurface;
        }
        if(restored)RemoveProp(w,Property(r.Token));return restored;
    }
    internal static bool RestoreTransition(Record r){if(!Identity(r))return false;int transition;IntPtr w=new IntPtr(r.Window);if(r.Transition<0)return true;if(GetTransition(w,3,out transition,4)!=0)return false;if(transition==1){int original=r.Transition;return SetTransition(w,3,ref original,4)==0&&GetTransition(w,3,out transition,4)==0&&transition==original;}return true;}
    static void Persist(Record r,string folder){Directory.CreateDirectory(folder);for(string path=folder;path!=null;path=Path.GetDirectoryName(path))if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("restore-guard-linked-journal");
        var security=new DirectorySecurity();security.SetAccessRuleProtection(true,false);using(var identity=WindowsIdentity.GetCurrent())security.AddAccessRule(new FileSystemAccessRule(identity.User,FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));Directory.SetAccessControl(folder,security);
        using(var file=new FileStream(Path.Combine(folder,r.Token+".json"),FileMode.CreateNew,FileAccess.Write,FileShare.Read)){new DataContractJsonSerializer(typeof(Record)).WriteObject(file,r);file.Flush(true);}}
    internal static bool IsMode(string[] args){return args.Length>0&&args[0]=="--restore-guard";}
    internal static int Run(string[] args){try{return RunCore(args);}catch(Exception error){LauncherDiagnostics.Write("handoff-latest.log","Native helper terminal cleanup failure: "+error.GetType().Name);return 23;}}
    private static int RunCore(string[] args){
        if(args.Length!=4||!StartupPreparation.ValidId(args[3]))return 21;int parentPid;long created;if(!Int32.TryParse(args[1],out parentPid)||!Int64.TryParse(args[2],out created))return 21;
        var records=new List<Record>();string folder;
        using(var own=Process.GetCurrentProcess())folder=JournalFolder(own.MainModule.FileName,args[3]);
        bool expired=false;Process parent=null;int resultCode=0;
        try {
            parent=Process.GetProcessById(parentPid);
            using(var own=Process.GetCurrentProcess())if(parent.StartTime.ToUniversalTime().ToFileTimeUtc()!=created||!String.Equals(parent.MainModule.FileName,own.MainModule.FileName,StringComparison.OrdinalIgnoreCase))return 22;
            using(var client=new NamedPipeClientStream(".","CodexDragon.Restore."+args[3],PipeDirection.InOut,PipeOptions.Asynchronous)){
                client.Connect(5000);uint server;if(!GetNamedPipeServerProcessId(client.SafePipeHandle.DangerousGetHandle(),out server)||server!=parentPid)return 22;
                if(ReadLine(client,3000)!="hello")return 22;WriteLine(client,"ready");
                while(!parent.HasExited){string command;try{command=ReadLine(client,HeartbeatReadDeadlineMilliseconds);}catch(TimeoutException){expired=true;resultCode=24;break;}
                    if(command=="pulse"){WriteLine(client,"ok");continue;}
                    if(!command.StartsWith("arm ",StringComparison.Ordinal)||records.Count>=8)throw new InvalidDataException("restore-guard-command");
                    string phase="decode";
                    try {
                        Record r=Deserialize(command.Substring(4));phase="target-identity";
                        if(!Identity(r))throw new InvalidDataException("restore-guard-target-identity");
                        phase="journal";Persist(r,folder);records.Add(r);WriteLine(client,"armed "+r.Token);
                    }catch(Exception error){try{WriteLine(client,"error "+phase+"-"+error.GetType().Name);}catch(IOException){}throw;}
                }
            }
        }catch(EndOfStreamException){ /* An authenticated owner can exit normally. */ }
        catch(Exception e){resultCode=23;LauncherDiagnostics.Write("handoff-latest.log","Native recovery helper ending: "+e.GetType().Name);}
        finally {
            // Stop the authenticated stale owner before compensation. Restoring
            // first allows an abruptly resumed old UI to hide the target again.
            if(expired&&parent!=null)try{if(!parent.HasExited&&parent.StartTime.ToUniversalTime().ToFileTimeUtc()==created){parent.Kill();if(!parent.WaitForExit(500))LauncherDiagnostics.Write("handoff-latest.log","Launcher fail-stop remains unconfirmed");}}catch(Exception e){LauncherDiagnostics.Write("handoff-latest.log","Launcher fail-stop incomplete: "+e.GetType().Name);}
            var recoveryClock=Stopwatch.StartNew();
            foreach(var r in records){try{while(Identity(r)&&!Restore(r)&&recoveryClock.ElapsedMilliseconds<RecoveryBudgetMilliseconds)Thread.Sleep(20);if(Identity(r))LauncherDiagnostics.Write("handoff-latest.log","Native recovery still unconfirmed after bounded retry");}catch(Exception e){LauncherDiagnostics.Write("handoff-latest.log","Native recovery incomplete: "+e.GetType().Name);}}
            if(parent!=null)parent.Dispose();
            foreach(var r in records)try{string file=Path.Combine(folder,r.Token+".json");if(File.Exists(file)&&!Identity(r))File.Delete(file);}catch(IOException){}
            try{if(Directory.Exists(folder)&&Directory.GetFileSystemEntries(folder).Length==0)Directory.Delete(folder);}catch(IOException){}
        }
        return resultCode;
    }
}
}

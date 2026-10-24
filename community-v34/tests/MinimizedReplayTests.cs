using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Interop;

namespace DragonLauncher {
// All windows are owned fixtures on a separate NON-INPUT desktop. This code
// never calls SwitchDesktop, enumerates the input desktop, or starts Codex.
internal static class MinimizedReplayTests {
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    [StructLayout(LayoutKind.Sequential)] struct Pt{public int X,Y;}
    [StructLayout(LayoutKind.Sequential)] struct Rect{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)] struct Place{public uint Length,Flags,Show;public Pt Min,Max;public Rect Normal;}
    [StructLayout(LayoutKind.Sequential)] struct Message{public IntPtr Hwnd;public uint Code;public IntPtr WParam,LParam;public uint Time;public Pt Point;public uint Private;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct StartInfo{public int Size;public string Reserved,Desktop,Title;public uint X,Y,XSize,YSize,XCount,YCount,Fill,Flags;public short Show,ReservedSize;public IntPtr ReservedBytes,Input,Output,Error;}
    [StructLayout(LayoutKind.Sequential)] struct ProcessInfo{public IntPtr Process,Thread;public uint Pid,Tid;}
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool CreateProcess(string application,StringBuilder command,IntPtr processAttributes,IntPtr threadAttributes,bool inherit,uint flags,IntPtr environment,string directory,ref StartInfo startup,out ProcessInfo result);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr CreateDesktop(string name,IntPtr device,IntPtr mode,uint flags,uint access,IntPtr attrs);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr OpenDesktop(string name,uint flags,bool inherit,uint access);
    [DllImport("user32.dll",SetLastError=true)] static extern bool SetThreadDesktop(IntPtr desktop);
    [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr CreateWindowEx(uint ex,string cls,string title,uint style,int x,int y,int width,int height,IntPtr parent,IntPtr menu,IntPtr instance,IntPtr param);
    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr w);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr w,int command);
    [DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr w,int command);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr w);
    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr w);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr w);
    [DllImport("user32.dll")] static extern bool GetWindowPlacement(IntPtr w,ref Place p);
    [DllImport("user32.dll")] static extern bool SetWindowPlacement(IntPtr w,ref Place p);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr w,out Rect r);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr w,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr RemoveProp(IntPtr w,string name);
    [DllImport("user32.dll")] static extern bool PeekMessage(out Message m,IntPtr w,uint first,uint last,uint remove);
    [DllImport("user32.dll")] static extern bool TranslateMessage(ref Message m);
    [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref Message m);
    static int checks;static readonly List<object> cases=new List<object>();
    static void Check(bool ok,string text){checks++;if(!ok)throw new Exception(text);}
    static object Read(object o,string f){return o.GetType().GetField(f,Private).GetValue(o);}
    static void Write(object o,string f,object v){o.GetType().GetField(f,Private).SetValue(o,v);}
    static bool Same(Rect a,Rect b){return a.Left==b.Left&&a.Top==b.Top&&a.Right==b.Right&&a.Bottom==b.Bottom;}
    static Rect Bounds(IntPtr w){Rect r;if(!GetWindowRect(w,out r))throw new Exception("fixture bounds unavailable");return r;}
    static Place Placement(IntPtr w){var p=new Place{Length=44};if(!GetWindowPlacement(w,ref p))throw new Exception("fixture placement unavailable");return p;}
    static void Pump(int milliseconds){var clock=Stopwatch.StartNew();do{Message m;while(PeekMessage(out m,IntPtr.Zero,0,0,1)){TranslateMessage(ref m);DispatchMessage(ref m);}Thread.Sleep(2);}while(clock.ElapsedMilliseconds<milliseconds);}
    static bool Until(Func<bool> predicate,int budget){var clock=Stopwatch.StartNew();do{if(predicate())return true;Pump(10);}while(clock.ElapsedMilliseconds<budget);return predicate();}
    static Process StartPrivate(string desktop,string arguments){
        string executable=Process.GetCurrentProcess().MainModule.FileName;var startup=new StartInfo{Size=Marshal.SizeOf(typeof(StartInfo)),Desktop=desktop,Flags=1,Show=0};ProcessInfo info;
        if(!CreateProcess(executable,new StringBuilder("\""+executable+"\" "+arguments),IntPtr.Zero,IntPtr.Zero,false,0x08000000,IntPtr.Zero,AppDomain.CurrentDomain.BaseDirectory,ref startup,out info))throw new Exception("private fixture process creation error "+Marshal.GetLastWin32Error());
        try{return Process.GetProcessById((int)info.Pid);}finally{CloseHandle(info.Thread);CloseHandle(info.Process);}
    }
    static int Child(string[] args){
        IntPtr desktop=OpenDesktop(args[1],0,false,0x1ff),window=IntPtr.Zero;
        if(desktop==IntPtr.Zero)return 3;
        try{using(var ready=EventWaitHandle.OpenExisting(args[2]))using(var stop=EventWaitHandle.OpenExisting(args[3])){
            window=CreateWindowEx(0x08000000,"STATIC","Owned minimized replay fixture",0x00cf0000,160,100,900,550,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);
            if(window==IntPtr.Zero)return 4;if(args[4]=="maximized-minimized"){ShowWindow(window,3);ShowWindow(window,7);}else ShowWindow(window,args[4]=="minimized"?7:4);ready.Set();
            var deadline=Stopwatch.StartNew();while(!stop.WaitOne(0)&&deadline.ElapsedMilliseconds<30000)Pump(5);return 0;
        }}finally{if(window!=IntPtr.Zero)DestroyWindow(window);CloseDesktop(desktop);}
    }
    sealed class Fixture:IDisposable {
        internal readonly Process Child;internal readonly IntPtr Target;internal readonly Window Cover;
        internal readonly NativeRestoreGuard Guard;internal readonly ExistingReplayTarget Replay;
        internal readonly Place Before;internal NativeHandoff.CompactPlacement Scope;internal NativeRestoreGuard.Lease Lease;
        readonly EventWaitHandle ready,stop;
        internal Fixture(string desktop,bool initialMinimized=true,bool useReplay=true,bool maximizedBeforeMinimize=false){
            string suffix=Guid.NewGuid().ToString("N"),readyName="Local\\CodexReplayReady"+suffix,stopName="Local\\CodexReplayStop"+suffix;
            ready=new EventWaitHandle(false,EventResetMode.ManualReset,readyName);stop=new EventWaitHandle(false,EventResetMode.ManualReset,stopName);
            string executable=Process.GetCurrentProcess().MainModule.FileName;
            Child=StartPrivate(desktop,"--fixture-child "+desktop+" "+readyName+" "+stopName+" "+(maximizedBeforeMinimize?"maximized-minimized":initialMinimized?"minimized":"normal"));
            Check(ready.WaitOne(3000),"private desktop child is ready");
            var settings=new Settings{Exe=executable,AppId="",WallpaperEnabled=false};
            Replay=useReplay?ExistingReplayTarget.Find(settings,AppDomain.CurrentDomain.BaseDirectory):null;
            Target=Replay!=null?Replay.Window:NativeHandoff.FindTarget(settings,(uint)Child.Id);
            Check(Target!=IntPtr.Zero&&BridgeActivation.WindowProcessId(Target)==(uint)Child.Id,"only exact fixture child target is selected");
            Before=Placement(Target);Check(IsIconic(Target)==initialMinimized,"requested initial minimized state confirmed");
            Cover=new Window{Width=1280,Height=720,Left=300,Top=140,WindowStartupLocation=WindowStartupLocation.Manual,WindowStyle=WindowStyle.None,ShowActivated=false,ShowInTaskbar=false};
            Cover.Show();Pump(30);
            Guard=(NativeRestoreGuard)FormatterServices.GetUninitializedObject(typeof(NativeRestoreGuard));Write(Guard,"ready",true);Write(Guard,"gate",new object());Write(Guard,"pending",new Queue<NativeRestoreGuard.Lease>());
            NativeRestoreGuard.Current=Guard;
            var ctor=typeof(NativeHandoff.CompactPlacement).GetConstructor(Private,null,new[]{typeof(IntPtr),typeof(Window),typeof(int),typeof(ExistingReplayTarget)},null);
            Scope=ctor==null?NativeHandoff.PrepareTargetInOverlay(Target,Cover,4000):(NativeHandoff.CompactPlacement)ctor.Invoke(new object[]{Target,Cover,4000,Replay});
        }
        internal bool Step(){bool readyNow=Scope.TryPrepare();if(Lease==null){Lease=(NativeRestoreGuard.Lease)Read(Scope,"recovery");if(Lease!=null)Lease.Armed=true;}return readyNow;}
        internal bool Prepare(){return Until(()=>Step()||Scope.Failed,4500)&&Scope.Ready;}
        internal void ReachPhase(int phase){Check(Until(()=>{Step();return Scope.Failed||Scope.PreparationPhase==phase;},2500)&&!Scope.Failed&&Scope.PreparationPhase==phase,"owned preparation reaches requested phase "+phase);}
        public void Dispose(){
            if(Scope!=null)Scope.Dispose();if(Lease!=null)Lease.Release();NativeRestoreGuard.Current=null;
            if(Cover!=null)Cover.Close();stop.Set();if(Child!=null){Check(Child.WaitForExit(2500),"owned fixture child exited cooperatively");Child.Dispose();}ready.Dispose();stop.Dispose();
        }
    }
    static void Success(string desktop){using(var f=new Fixture(desktop)){
        IntPtr foreground=GetForegroundWindow();Check(f.Prepare(),"initially minimized replay must reach ready compact presentation; "+f.Scope.FailureReason);
        Check(!IsIconic(f.Target)&&IsWindowVisible(f.Target),"one nonactivating show restores initial minimized replay");
        Rect cover=Bounds(new WindowInteropHelper(f.Cover).Handle),actual=Bounds(f.Target);
        Check(actual.Left==cover.Left+6&&actual.Top==cover.Top+6&&actual.Right==cover.Right-6&&actual.Bottom==cover.Bottom-6,"restored client uses exact compact physical rectangle");
        Check(GetForegroundWindow()==foreground,"initial replay restoration never activates foreground");
        Check(f.Scope.Commit(),"successful replay releases owned compact lease");
        cases.Add(new{name="initial-minimized-replay",passed=true});
    }}
    static void RefusedModes(string desktop){
        using(var f=new Fixture(desktop,true,false)){Check(!f.Prepare()&&f.Scope.Failed&&f.Scope.FailureReason.Contains("minimized"),"cold startup has no initial-minimized restore capability");Check(IsIconic(f.Target),"cold refusal preserves minimized presentation");}
        using(var f=new Fixture(desktop,false,true)){
            Check(ShowWindowAsync(f.Target,7)&&Until(()=>IsIconic(f.Target),1000),"new user minimize after replay snapshot is observed");
            Check(!f.Prepare()&&f.Scope.Failed&&f.Scope.FailureReason.Contains("minimized"),"originally visible replay cannot restore a later minimize");Check(IsIconic(f.Target),"new minimize remains minimized");
        }
        using(var f=new Fixture(desktop)){
            Check(!f.Replay.ClaimInitialMinimizedRestore(f.Target),"initial restoration capability is consumed only once by first scope");
            Check(!f.Replay.ClaimInitialMinimizedRestore(IntPtr.Zero),"capability never authorizes another HWND");
        }
        cases.Add(new{name="cold-normal-replay-new-minimize-and-duplicate-capability-refused",passed=true});
    }
    static void Rollback(string desktop,string stage){using(var f=new Fixture(desktop)){
        if(stage=="before-hide"){f.Step();Check(f.Lease!=null&&f.Lease.Armed,"native recovery lease exists before mutation");}
        else if(stage=="hide-requested"){f.Step();f.Step();Check(f.Scope.PreparationPhase==0,"owned hide was requested");}
        else if(stage=="hidden-placement"||stage=="guardian-lost")f.ReachPhase(2);
        else if(stage=="show-ordered")f.ReachPhase(1);
        else Check(f.Prepare(),"fixture becomes ready before terminal rollback");
        IntPtr foreground=GetForegroundWindow();
        if(stage=="guardian-lost"){Write(f.Guard,"ready",false);f.Step();Check(f.Scope.Failed,"loss of guardian stops preparation");}
        f.Scope.Dispose();
        bool rolledBack=Until(()=>IsWindowVisible(f.Target)&&IsIconic(f.Target)&&Same(Placement(f.Target).Normal,f.Before.Normal),1200);
        Check(rolledBack,"abort restores exact original minimized normal rectangle at "+stage+"; "+new JavaScriptSerializer().Serialize(new{visible=IsWindowVisible(f.Target),minimized=IsIconic(f.Target),before=f.Before,current=Placement(f.Target),positionRequested=f.Lease==null?false:f.Lease.Record.PositionRestoreRequested}));
        Check(GetForegroundWindow()==foreground,"rollback never activates at "+stage);
        cases.Add(new{name="owned-rollback-"+stage,passed=true});
    }}
    static void NewMinimize(string desktop){using(var f=new Fixture(desktop)){
        Check(f.Prepare(),"initial restore succeeds before later user minimize");var compact=Placement(f.Target).Normal;
        Check(ShowWindowAsync(f.Target,7)&&Until(()=>IsIconic(f.Target),1000),"user minimizes restored window");
        IntPtr foreground=GetForegroundWindow();Check(!NativeHandoff.RaiseTargetBehindOverlay(f.Target),"raise never revives a new minimized target");Pump(60);
        Check(IsIconic(f.Target)&&GetForegroundWindow()==foreground,"ready-to-raise minimize race preserves minimized and foreground states");
        for(int n=0;n<4;n++){Check(!f.Step(),"new minimized observation is never ready");Pump(15);}
        f.Scope.Dispose();Pump(60);
        Check(IsIconic(f.Target)&&Same(Placement(f.Target).Normal,compact),"postready user minimize is not undone or moved back");
        Check(!f.Replay.ClaimInitialMinimizedRestore(f.Target),"old capability cannot restore second user minimize");
        cases.Add(new{name="new-user-minimize-after-restoration-is-preserved",passed=true});
    }}
    static void MaximizeHistory(string desktop,bool ready){using(var f=new Fixture(desktop,true,true,true)){
        Check((f.Before.Flags&2)!=0,"native maximize-to-minimize fixture retains restore-maximized flag");
        if(ready)Check(f.Prepare(),"maximized-to-minimized replay prepares before rollback");else f.ReachPhase(2);
        IntPtr foreground=GetForegroundWindow();f.Scope.Dispose();Check(Until(()=>IsWindowVisible(f.Target)&&IsIconic(f.Target)&&Same(Placement(f.Target).Normal,f.Before.Normal),1200),"maximized-to-minimized abort restores original minimized normal rectangle");
        Check(GetForegroundWindow()==foreground,"maximized history rollback does not activate foreground");
        uint afterFlags=Placement(f.Target).Flags;
        Check(ShowWindowAsync(f.Target,9)&&Until(()=>!IsIconic(f.Target),1000),"test-only next user restore is acknowledged on private desktop");
        Check(IsZoomed(f.Target),"abort preserves next user restore-to-maximized behavior; originalFlags="+f.Before.Flags+"; afterFlags="+afterFlags);
        cases.Add(new{name=ready?"minimized-from-maximized-ready-rollback-next-user-restore":"minimized-from-maximized-hidden-rollback-next-user-restore",passed=true});
    }}
    static void UserResize(string desktop){using(var f=new Fixture(desktop)){
        Check(f.Prepare(),"initial restore succeeds before user resize");Rect owned=Bounds(f.Target);Rect altered=owned;altered.Left+=20;altered.Right-=35;altered.Bottom-=15;
        Check(SetWindowPos(f.Target,IntPtr.Zero,altered.Left,altered.Top,altered.Right-altered.Left,altered.Bottom-altered.Top,0x4214),"owned fixture user resize queued");
        Check(Until(()=>Same(Bounds(f.Target),altered),1000),"user resize acknowledged");f.Scope.Dispose();Pump(80);
        Check(Same(Bounds(f.Target),altered)&&!IsIconic(f.Target),"rollback never overwrites later user resize");
        Check(!f.Lease.Record.PositionRestoreRequested,"foreign physical rectangle gains no position compensation");
        cases.Add(new{name="new-user-resize-after-restoration-is-preserved",passed=true});
    }}
    static void ForeignNormal(string desktop){using(var f=new Fixture(desktop)){
        f.ReachPhase(2);Check(Until(()=>!IsWindowVisible(f.Target)&&IsIconic(f.Target),1000),"initial minimized target stays hidden before normal acknowledgement");
        Place p=Placement(f.Target);Rect changed=p.Normal;changed.Right+=17;p.Normal=changed;p.Flags=4;p.Show=0;
        Check(SetWindowPlacement(f.Target,ref p)&&Until(()=>Same(Placement(f.Target).Normal,changed),1000),"foreign pending normal rectangle acknowledged");f.Scope.Dispose();Pump(80);
        Check(IsIconic(f.Target)&&Same(Placement(f.Target).Normal,changed),"foreign pending normal geometry is not rolled back");Check(!f.Lease.Record.PositionRestoreRequested,"foreign normal does not gain position ownership");
        cases.Add(new{name="foreign-hidden-normal-rectangle-is-not-restored",passed=true});
    }}
    static void LostOwnership(string desktop,bool generation){using(var f=new Fixture(desktop)){
        f.ReachPhase(2);Check(Until(()=>{var p=Placement(f.Target);var b=Bounds(f.Target);var r=f.Lease.Record;return !IsWindowVisible(f.Target)&&p.Normal.Left==r.ExpectedNormalLeft&&p.Normal.Top==r.ExpectedNormalTop&&p.Normal.Right==r.ExpectedNormalRight&&p.Normal.Bottom==r.ExpectedNormalBottom&&b.Left==r.ExpectedLeft&&b.Top==r.ExpectedTop;},1000),"fixture hidden placement and queued position are acknowledged before ownership refusal");var before=Placement(f.Target);var bounds=Bounds(f.Target);
        if(generation)f.Lease.Record.Created++;else RemoveProp(f.Target,"CodexDragon.Restore."+f.Lease.Record.Token);
        Check(!NativeRestoreGuard.Restore(f.Lease.Record),"guardian rejects changed process generation or missing native token");Pump(30);
        Check(!IsWindowVisible(f.Target)&&IsIconic(f.Target)&&Same(Placement(f.Target).Normal,before.Normal)&&Same(Bounds(f.Target),bounds),"unowned guardian action does not change fixture state; "+new JavaScriptSerializer().Serialize(new{generation=generation,visible=IsWindowVisible(f.Target),iconic=IsIconic(f.Target),before=before,after=Placement(f.Target),bounds=bounds,actual=Bounds(f.Target)}));
        if(generation)f.Lease.Record.Created--;else{f.Step();Check(f.Scope.Failed,"compact placement rejects lost native token");}
        cases.Add(new{name=generation?"guard-process-generation-rejected":"guard-native-lease-lost",passed=true});
    }}
    static void OwnershipMatrix(){
        var r=new NativeRestoreGuard.Record{Show=2,InitialMinimizedReplay=true,ExpectedNormalCaptured=true,ExpectedNormalLeft=-900,ExpectedNormalTop=40,ExpectedNormalRight=600,ExpectedNormalBottom=760};
        var p=new NativeRestoreGuard.Placement{Show=2,Normal=new NativeRestoreGuard.Rect{Left=-900,Top=40,Right=600,Bottom=760}};
        foreach(uint original in new uint[]{0,1,2,3,4,7,8,9})foreach(uint current in new uint[]{0,1,2,3,4,7,8,9})foreach(bool hidden in new[]{false,true})foreach(bool minimized in new[]{false,true}){
            r.Show=original;p.Show=current;Check(NativeRestoreGuard.OwnsPendingMinimizedPlacement(r,p,hidden,minimized)==(original==2&&current==2&&hidden&&minimized),"strict minimized normal ownership matrix");
        }
        r.Show=2;p.Show=2;
        for(int edge=0;edge<4;edge++)foreach(int shift in new[]{-1,1}){var different=p;if(edge==0)different.Normal.Left+=shift;else if(edge==1)different.Normal.Top+=shift;else if(edge==2)different.Normal.Right+=shift;else different.Normal.Bottom+=shift;Check(!NativeRestoreGuard.OwnsPendingMinimizedPlacement(r,different,true,true),"one-pixel foreign normal edge refused");}
        r.InitialMinimizedReplay=false;Check(!NativeRestoreGuard.OwnsPendingMinimizedPlacement(r,p,true,true),"ordinary arm and older record have no initial minimized capability");r.InitialMinimizedReplay=true;
        r.ExpectedNormalCaptured=false;Check(!NativeRestoreGuard.OwnsPendingMinimizedPlacement(r,p,true,true),"unknown workspace normal has no ownership");Check(!NativeRestoreGuard.OwnsPendingMinimizedPlacement(null,p,true,true),"missing guardian record refused");
        cases.Add(new{name="strict-original-current-show-state-and-four-edge-ownership-matrix",passed=true});
    }
    [STAThread] static int Main(string[] args){
        if(args.Length>0&&args[0]=="--fixture-child")return Child(args);
        if(args.Length==2&&args[0]=="--fixture-run")return RunPrivate(args[1]);
        IntPtr desktop=IntPtr.Zero;string name="CodexReplayFixture"+Guid.NewGuid().ToString("N");var elapsed=Stopwatch.StartNew();
        try{
            desktop=CreateDesktop(name,IntPtr.Zero,IntPtr.Zero,0,0x1ff,IntPtr.Zero);Check(desktop!=IntPtr.Zero,"private desktop create error "+Marshal.GetLastWin32Error());
            using(var runner=StartPrivate(name,"--fixture-run "+name)){if(!runner.WaitForExit(90000))throw new Exception("private runner time bound");string result=File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"private-results.json"));Console.WriteLine(result);return result.StartsWith("FAIL:",StringComparison.Ordinal)?1:0;}
        }catch(Exception error){Console.WriteLine("FAIL: "+error.GetBaseException().Message);return 1;}finally{if(desktop!=IntPtr.Zero)CloseDesktop(desktop);}
    }
    static int RunPrivate(string name){var elapsed=Stopwatch.StartNew();string output;int code=0;try{
        Success(name);RefusedModes(name);foreach(string stage in new[]{"before-hide","hide-requested","hidden-placement","show-ordered","ready","guardian-lost"})Rollback(name,stage);
        NewMinimize(name);MaximizeHistory(name,false);MaximizeHistory(name,true);UserResize(name);ForeignNormal(name);LostOwnership(name,false);LostOwnership(name,true);OwnershipMatrix();
        output=new JavaScriptSerializer().Serialize(new{passed=true,checks=checks,elapsedMs=elapsed.ElapsedMilliseconds,isolatedNonInputDesktop=true,actualCodexStarted=false,cases=cases});
    }catch(Exception error){output="FAIL: "+error.GetBaseException().Message;code=1;}File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"private-results.json"),output);return code;}
}
}

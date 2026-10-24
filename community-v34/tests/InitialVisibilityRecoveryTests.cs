using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace CodexDragonWallpaper {
// The production sampler is tested with its numeric sink replaced, so this
// fixture never opens a release log or discovery endpoint.
internal static class PreparationDiagnostics {
    internal sealed class Sample {internal int Outcome,Reason,Hidden,Visible;internal bool Marker,Complete;}
    internal static readonly List<Sample> Samples=new List<Sample>();
    internal static void RecordInitialVisibility(int outcome,long elapsed,int reason,int hidden,int visible,bool markerOwned,bool complete=true){lock(Samples)Samples.Add(new Sample{Outcome=outcome,Reason=reason,Hidden=hidden,Visible=visible,Marker=markerOwned,Complete=complete});}
}
internal static class InitialVisibilityRecoveryTests {
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static int passed;
    static void Check(bool value,string name){if(!value)throw new Exception(name);passed++;}
    static bool Until(Func<bool> check,int budget=2000){var timer=Stopwatch.StartNew();while(timer.ElapsedMilliseconds<budget){if(check())return true;Thread.Sleep(10);}return check();}
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr CreateDesktop(string name,IntPtr device,IntPtr mode,uint flags,uint access,IntPtr security);
    [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll")] static extern bool SetThreadDesktop(IntPtr desktop);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window,int command);
    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr GetProp(IntPtr window,string key);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr RemoveProp(IntPtr window,string key);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool SetProp(IntPtr window,string key,IntPtr value);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern ushort RegisterClass(ref WindowClass data);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool UnregisterClass(string name,IntPtr instance);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr CreateWindowEx(uint extended,string type,string title,uint style,int x,int y,int width,int height,IntPtr parent,IntPtr menu,IntPtr instance,IntPtr parameter);
    [DllImport("user32.dll")] static extern IntPtr DefWindowProc(IntPtr window,uint message,IntPtr w,IntPtr l);
    [DllImport("user32.dll")] static extern bool PeekMessage(out Message message,IntPtr window,uint first,uint last,uint remove);
    [DllImport("user32.dll")] static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref Message message);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
    delegate IntPtr Procedure(IntPtr window,uint message,IntPtr w,IntPtr l);
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct WindowClass {internal uint Style;internal Procedure Procedure;internal int ClassExtra,WindowExtra;internal IntPtr Instance,Icon,Cursor,Background;internal string Menu,Class;}
    [StructLayout(LayoutKind.Sequential)] struct Message {internal IntPtr Window;internal uint Code;internal IntPtr W,L;internal uint Time;internal int X,Y;internal uint Private;}
    sealed class Fixture:IDisposable {
        internal readonly OwnedClient Client;
        internal readonly string Marker;
        internal readonly List<IntPtr> Windows=new List<IntPtr>();
        internal int Release;
        internal int Ticks;
        readonly IntPtr desktop;
        readonly Thread ui;
        Thread worker;
        readonly ConcurrentQueue<Action> tasks=new ConcurrentQueue<Action>();
        readonly AutoResetEvent wake=new AutoResetEvent(false);
        readonly ManualResetEvent ready=new ManualResetEvent(false);
        readonly Procedure procedure=DefWindowProc;
        volatile bool stop;
        Exception fault;
        internal Fixture(){
            lock(PreparationDiagnostics.Samples)PreparationDiagnostics.Samples.Clear();
            desktop=CreateDesktop("CodexDragonFixture"+Guid.NewGuid().ToString("N"),IntPtr.Zero,IntPtr.Zero,0,0x10000000,IntPtr.Zero);
            Check(desktop!=IntPtr.Zero,"Create owned non-input desktop");
            ui=new Thread(delegate(){try{
                if(!SetThreadDesktop(desktop))throw new Exception("Cannot bind fixture desktop");
                var wc=new WindowClass{Procedure=procedure,Instance=GetModuleHandle(null),Class="Chrome_WidgetWin_1"};
                if(RegisterClass(ref wc)==0)throw new Exception("Cannot register owned fixture class");
                ready.Set();
                while(!stop){Action action;while(tasks.TryDequeue(out action))action();Message message;while(PeekMessage(out message,IntPtr.Zero,0,0,1)){TranslateMessage(ref message);DispatchMessage(ref message);}Thread.Sleep(2);}
                foreach(var window in Windows)DestroyWindow(window);
                UnregisterClass(wc.Class,wc.Instance);
            }catch(Exception ex){fault=ex;ready.Set();}}){IsBackground=true};ui.Start();
            Check(ready.WaitOne(2000)&&fault==null,"Fixture message pump initialized");
            using(var own=Process.GetCurrentProcess()){
                Client=(OwnedClient)Activator.CreateInstance(typeof(OwnedClient),Private,null,new object[]{null,null,own.MainModule.FileName},null);
                Client.ProcessHandle=OpenProcess(0x00101000,false,own.Id);Client.Pid=own.Id;Client.Created=Native.Creation(Client.ProcessHandle);
            }
            Marker="CodexDragon.InitialHidden.v25."+Client.Pid+"."+Client.Created;
            typeof(OwnedClient).GetField("visibilityWake",Private).SetValue(Client,wake);
        }
        internal void Invoke(Action action){using(var done=new ManualResetEvent(false)){Exception error=null;tasks.Enqueue(delegate{try{action();}catch(Exception ex){error=ex;}finally{done.Set();}});if(!done.WaitOne(2000))throw new Exception("Fixture command timed out");if(error!=null)throw error;}}
        internal IntPtr Add(uint style=0x00cf0000,uint extended=0,IntPtr parent=default(IntPtr),int width=320){IntPtr window=IntPtr.Zero;Invoke(delegate{window=CreateWindowEx(extended,"Chrome_WidgetWin_1","Owned isolated test",style,20,20,width,240,parent,IntPtr.Zero,GetModuleHandle(null),IntPtr.Zero);Windows.Add(window);});Check(window!=IntPtr.Zero,"Create own hidden fixture");return window;}
        internal void Start(){worker=new Thread(delegate(){try{if(!SetThreadDesktop(desktop))throw new Exception("Cannot bind worker desktop");typeof(OwnedClient).GetMethod("InitialVisibilityLoop",Private).Invoke(Client,new object[]{new Func<int>(delegate{Interlocked.Increment(ref Ticks);return Volatile.Read(ref Release);}),new Action<int,long>((reason,elapsed)=>{})});}catch(Exception ex){fault=ex;}}){IsBackground=true};worker.Start();}
        internal void Request(int reason=2){Volatile.Write(ref Release,reason);wake.Set();}
        internal void Sample(){int before=Volatile.Read(ref Ticks);wake.Set();Check(Until(()=>Volatile.Read(ref Ticks)>before),"Owned worker observes changed fixture state");Thread.Sleep(20);}
        internal bool Bound(IntPtr window){return GetProp(window,Marker)==new IntPtr(1);}
        internal bool Stopped {get{return worker!=null&&!worker.IsAlive;}}
        internal void WaitBound(IntPtr window){Check(Until(()=>Bound(window)),"Unique initial window marker bound");Check(fault==null,"Visibility worker healthy");}
        internal void Show(IntPtr window,int command){Invoke(()=>ShowWindow(window,command));}
        public void Dispose(){
            object gate=typeof(OwnedClient).GetField("visibilityGate",Private).GetValue(Client);
            lock(gate){typeof(OwnedClient).GetField("disposed",Private).SetValue(Client,true);try{wake.Set();}catch(ObjectDisposedException){}}
            if(worker!=null&&!worker.Join(3000))throw new Exception("Fixture worker did not stop");
            Native.Close(ref Client.ProcessHandle);stop=true;if(!ui.Join(3000))throw new Exception("Fixture UI did not stop");
            ready.Dispose();wake.Dispose();CloseDesktop(desktop);if(fault!=null)throw fault;
        }
    }
    static void RetainedMarkerSurvivesAuxiliaryWindows(){
        using(var fixture=new Fixture()){
            var primary=fixture.Add();fixture.Start();fixture.WaitBound(primary);
            for(int i=0;i<6;i++)fixture.Add();
            fixture.Request();Check(Until(()=>IsWindowVisible(primary)),"Fail-open must show the originally marked primary despite later auxiliary candidates");
            Check(Until(()=>fixture.Stopped),"Successful recovery stops its worker");
            for(int i=1;i<fixture.Windows.Count;i++)Check(!IsWindowVisible(fixture.Windows[i]),"Unmarked auxiliary remains hidden");
            Check(!fixture.Bound(primary),"Recovery consumes initial marker");
            fixture.Show(primary,0);Thread.Sleep(350);Check(!IsWindowVisible(primary),"Later user hide is never undone");
            lock(PreparationDiagnostics.Samples){Check(PreparationDiagnostics.Samples.Count<=8,"Initial visibility sampling bounded to eight");Check(PreparationDiagnostics.Samples.Exists(x=>x.Outcome==6&&x.Hidden==7&&x.Marker),"Success records retained marker provenance and bounded candidate count");}
        }
    }
    static void InitialAmbiguityRemainsUntouched(){using(var fixture=new Fixture()){
        var first=fixture.Add();var second=fixture.Add();fixture.Start();fixture.Request();Thread.Sleep(600);
        Check(!IsWindowVisible(first)&&!IsWindowVisible(second),"Initially ambiguous hidden windows remain hidden");
        Check(!fixture.Bound(first)&&!fixture.Bound(second),"No arbitrary first candidate marker");
        lock(PreparationDiagnostics.Samples){Check(PreparationDiagnostics.Samples.Exists(x=>x.Outcome==3&&x.Hidden==2&&!x.Marker),"Ambiguity diagnosis emitted");Check(PreparationDiagnostics.Samples.Count<=2,"No unchanged steady-state diagnostics");}
    }}
    static void RemovedMarkerNeverRestores(){using(var fixture=new Fixture()){
        var primary=fixture.Add();fixture.Start();fixture.WaitBound(primary);RemoveProp(primary,fixture.Marker);fixture.Add();fixture.Request();
        Check(Until(()=>fixture.Stopped),"Accepted or removed marker ends ownership");Check(!IsWindowVisible(primary),"Removed marker cannot restore subsequent user hide");
        lock(PreparationDiagnostics.Samples)Check(PreparationDiagnostics.Samples.Exists(x=>x.Outcome==5),"Consumed marker diagnosis emitted");
    }}
    static void VisibleMainEndsOwnership(bool minimize){using(var fixture=new Fixture()){
        var primary=fixture.Add();fixture.Start();fixture.WaitBound(primary);fixture.Show(primary,minimize?7:4);
        Check(IsWindowVisible(primary)&&(!minimize||IsIconic(primary)),"Own fixture shown without switching input desktop");
        Check(Until(()=>fixture.Stopped),"Visible or minimized main yields initial presentation");
        fixture.Show(primary,0);Thread.Sleep(350);Check(!IsWindowVisible(primary),"Post-visible hide remains user controlled");
    }}
    static void GenerationMismatchCannotAuthorizeAmbiguousRecovery(){using(var fixture=new Fixture()){
        var primary=fixture.Add();fixture.Start();fixture.WaitBound(primary);fixture.Add();fixture.Client.Created++;fixture.Request();Thread.Sleep(600);
        Check(!IsWindowVisible(primary),"Changed creation identity cannot authorize retained marker recovery");
    }}
    static void PredicateRejectsAuxiliaryTypes(){using(var fixture=new Fixture()){
        var primary=fixture.Add();fixture.Add(0x00cf0000,0x80);fixture.Add(0x00cf0000,0,primary);fixture.Add(0x40cf0000,0,primary);fixture.Add(0x80000000,0,IntPtr.Zero,80);
        fixture.Start();fixture.WaitBound(primary);fixture.Request();Check(Until(()=>IsWindowVisible(primary)),"Only valid unowned normal main participates");
        for(int i=1;i<fixture.Windows.Count;i++)Check(!IsWindowVisible(fixture.Windows[i]),"Rejected auxiliary never shown");
    }}
    static void DiagnosticBudgetReservesFinalOutcome(){using(var fixture=new Fixture()){
        var primary=fixture.Add();fixture.Start();fixture.WaitBound(primary);
        for(int i=0;i<10;i++){fixture.Add();fixture.Sample();}
        lock(PreparationDiagnostics.Samples)Check(PreparationDiagnostics.Samples.Count==7,"Only seven changing intermediate samples consume budget");
        fixture.Sample();fixture.Sample();lock(PreparationDiagnostics.Samples)Check(PreparationDiagnostics.Samples.Count==7,"Steady state adds no diagnostic sample");
        fixture.Request();Check(Until(()=>fixture.Stopped&&IsWindowVisible(primary)),"Final fail-open still runs after sample budget fills");
        lock(PreparationDiagnostics.Samples){Check(PreparationDiagnostics.Samples.Count==8,"Reserved eighth record captures final outcome");var final=PreparationDiagnostics.Samples[7];Check(final.Outcome==6&&final.Hidden==11&&final.Visible==0&&final.Marker&&final.Reason==2&&final.Complete,"Final record reports exact bounded numeric evidence");}
    }}
    static void IncompleteObservationNeverSelects(){
        var predicate=typeof(OwnedClient).GetMethod("CanSelectInitialHiddenWindow",BindingFlags.NonPublic|BindingFlags.Static);
        Check(predicate!=null,"Initial selection has an explicit complete-observation boundary");
        foreach(int hidden in new[]{0,1,2,7})foreach(bool retained in new[]{false,true})
            Check(!(bool)predicate.Invoke(null,new object[]{false,hidden,0,retained}),"Incomplete observation cannot authorize any unique or retained candidate");
        Check(!(bool)predicate.Invoke(null,new object[]{true,0,0,true}),"Zero observed hidden windows cannot authorize a stale retained candidate");
        Check(!(bool)predicate.Invoke(null,new object[]{true,2,1,true}),"Visible main always precludes hidden recovery");
        Check(!(bool)predicate.Invoke(null,new object[]{true,2,0,false}),"Complete initial ambiguity still requires marker provenance");
        Check((bool)predicate.Invoke(null,new object[]{true,2,0,true}),"Complete retained ambiguity can use owned initial marker");
        Check((bool)predicate.Invoke(null,new object[]{true,1,0,false}),"Existing unique candidate recovery remains eligible");
    }
    static void ProcessEvidence(){using(var fixture=new Fixture()){
        var property=typeof(OwnedClient).GetProperty("ProcessExitState",Private);var method=typeof(OwnedClient).GetMethod("TryGetExitCode",Private);
        Check(property!=null&&method!=null,"Retained process exit evidence accessors exist");
        Check((int)property.GetValue(fixture.Client,null)==0,"Retained live handle reports alive");
        object[] args={0u};Check(!(bool)method.Invoke(fixture.Client,args),"Live process has no reported exit code");
        var retained=fixture.Client.ProcessHandle;fixture.Client.ProcessHandle=IntPtr.Zero;
        Check((int)property.GetValue(fixture.Client,null)==-1,"Missing handle reports unknown, never exited");
        Check(!(bool)method.Invoke(fixture.Client,args),"Missing handle does not invent exit code");fixture.Client.ProcessHandle=retained;
        using(var child=Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName,"--exit-fixture"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden})){
            IntPtr childHandle=OpenProcess(0x00101000,false,child.Id);Check(childHandle!=IntPtr.Zero&&child.WaitForExit(3000),"Owned evidence-only child exited");
            fixture.Client.ProcessHandle=childHandle;
            Check((int)property.GetValue(fixture.Client,null)==1,"Retained exited handle reports exited");args=new object[]{0u};
            Check((bool)method.Invoke(fixture.Client,args)&&(uint)args[0]==23,"Native retained handle supplies actual child exit code");Native.Close(ref childHandle);fixture.Client.ProcessHandle=retained;
        }
    }}
    public static int Main(string[] args){if(args.Length==1&&args[0]=="--exit-fixture"){Thread.Sleep(250);return 23;}try{
        var foreground=GetForegroundWindow();RetainedMarkerSurvivesAuxiliaryWindows();InitialAmbiguityRemainsUntouched();RemovedMarkerNeverRestores();VisibleMainEndsOwnership(false);VisibleMainEndsOwnership(true);GenerationMismatchCannotAuthorizeAmbiguousRecovery();PredicateRejectsAuxiliaryTypes();DiagnosticBudgetReservesFinalOutcome();IncompleteObservationNeverSelects();ProcessEvidence();
        Check(GetForegroundWindow()==foreground,"All fixtures leave user foreground unchanged");Console.WriteLine("PASS: "+passed+" native initial visibility and retained process assertions");return 0;
    }catch(Exception ex){Console.WriteLine("FAIL after "+passed+" assertions: "+ex.Message);return 1;}}
}
}

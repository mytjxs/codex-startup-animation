using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace DragonLauncher {
    internal static class NativeRestoreVisibilityTests {
        static int passed;
        static void Check(bool value,string message){if(!value)throw new Exception(message);passed++;}
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr w);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr w);
        [DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr w,int command);
        [DllImport("user32.dll")] static extern bool GetWindowPlacement(IntPtr w,ref NativeRestoreGuard.Placement p);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr w,out NativeRestoreGuard.Rect r);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr handle,uint milliseconds);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool SetProp(IntPtr w,string name,IntPtr value);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr GetProp(IntPtr w,string name);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr RemoveProp(IntPtr w,string name);
        sealed class Window:Form {
            internal volatile int Hides;
            internal volatile bool Ending;
            protected override bool ShowWithoutActivation{get{return true;}}
            internal Window(){ShowInTaskbar=false;Opacity=0;FormBorderStyle=FormBorderStyle.None;StartPosition=FormStartPosition.Manual;Location=new Point(40,40);Size=new Size(180,140);}
            protected override void OnVisibleChanged(EventArgs e){base.OnVisibleChanged(e);if(!Visible&&!Ending)Hides++;}
        }
        sealed class Fixture:IDisposable {
            internal Window Form;
            internal IntPtr Handle;
            readonly Thread thread;
            readonly ManualResetEvent ready=new ManualResetEvent(false);
            internal uint InitialHiddenShow;
            internal Fixture(){
                thread=new Thread(delegate(){Form=new Window();Handle=Form.Handle;InitialHiddenShow=Placement(Handle).Show;Form.Shown+=delegate{ready.Set();};Application.Run(Form);});
                thread.IsBackground=true;thread.SetApartmentState(ApartmentState.STA);thread.Start();
                if(!ready.WaitOne(2000))throw new Exception("Owned fixture did not start");
            }
            public void Dispose(){if(Form!=null&&!Form.IsDisposed){Form.Ending=true;Form.BeginInvoke(new Action(Form.Close));}if(!thread.Join(2500))throw new Exception("Owned fixture did not close");ready.Dispose();}
        }
        static NativeRestoreGuard.Placement Placement(IntPtr w){var p=new NativeRestoreGuard.Placement{Length=(uint)Marshal.SizeOf(typeof(NativeRestoreGuard.Placement))};if(!GetWindowPlacement(w,ref p))throw new Exception("Fixture placement unavailable");return p;}
        static bool Until(Func<bool> check,int milliseconds){var elapsed=Stopwatch.StartNew();while(elapsed.ElapsedMilliseconds<milliseconds){if(check())return true;Thread.Sleep(10);}return check();}
        static string Marker(NativeRestoreGuard.Record r){return "CodexDragon.Restore."+r.Token;}
        static NativeRestoreGuard.Rect Bounds(IntPtr w){NativeRestoreGuard.Rect r;Check(GetWindowRect(w,out r),"Fixture bounds available");return r;}
        static bool Same(NativeRestoreGuard.Rect a,NativeRestoreGuard.Rect b){return a.Left==b.Left&&a.Top==b.Top&&a.Right==b.Right&&a.Bottom==b.Bottom;}
        static void Block(Fixture f,ManualResetEvent entered,ManualResetEvent release){f.Form.BeginInvoke(new Action(delegate{entered.Set();WaitForSingleObject(release.SafeWaitHandle.DangerousGetHandle(),2500);}));Check(entered.WaitOne(1000),"Owned message pump blocked");}
        static NativeRestoreGuard.Record Lease(Fixture f,uint show){
            var p=Placement(f.Handle);NativeRestoreGuard.Rect s;Check(GetWindowRect(f.Handle,out s),"Fixture rectangle captured");
            NativeRestoreGuard.Record r;
            using(var own=Process.GetCurrentProcess())r=new NativeRestoreGuard.Record{Token=Guid.NewGuid().ToString("N"),Window=f.Handle.ToInt64(),Pid=own.Id,Created=own.StartTime.ToUniversalTime().ToFileTimeUtc(),Exe=own.MainModule.FileName,Left=p.Normal.Left,Top=p.Normal.Top,Right=p.Normal.Right,Bottom=p.Normal.Bottom,Show=show,ExpectedLeft=s.Left,ExpectedTop=s.Top,ExpectedRight=s.Right,ExpectedBottom=s.Bottom,ScreenLeft=s.Left,ScreenTop=s.Top,ScreenRight=s.Right,ScreenBottom=s.Bottom,Transition=-1};
            Check(SetProp(f.Handle,Marker(r),new IntPtr(1)),"Own fixture lease acquired");return r;
        }
        static void DeferredHiddenSnapshot(){
            using(var f=new Fixture())using(var entered=new ManualResetEvent(false))using(var release=new ManualResetEvent(false)){
                var record=Lease(f,0);IntPtr foreground=GetForegroundWindow();
                Console.WriteLine("fixture-initial-hidden-show="+f.InitialHiddenShow);
                f.Form.BeginInvoke(new Action(delegate{entered.Set();WaitForSingleObject(release.SafeWaitHandle.DangerousGetHandle(),2000);}));Check(entered.WaitOne(1000),"Owned message pump blocked");
                bool early=false,leaseBeforeRelease=false;
                try{var time=Stopwatch.StartNew();early=NativeRestoreGuard.Restore(record);leaseBeforeRelease=GetProp(f.Handle,Marker(record))==new IntPtr(1);Console.WriteLine("blocked-probe: elapsedMs="+time.ElapsedMilliseconds+" visible="+IsWindowVisible(f.Handle)+" show="+Placement(f.Handle).Show+" hides="+f.Form.Hides);}
                finally{release.Set();}
                Thread.Sleep(200);
                bool finalVisible=IsWindowVisible(f.Handle);
                Console.WriteLine("defensive-synthetic-show-zero: returned="+early+" retained="+leaseBeforeRelease+" finalVisible="+finalVisible+" hides="+f.Form.Hides);
                Check(!early&&leaseBeforeRelease,"Synthetic hidden snapshot cannot certify queued restoration");
                Check(finalVisible,"Original SW_HIDE must never be replayed after fallback became visible");
                Check(GetForegroundWindow()==foreground,"Fallback must not steal foreground");
                RemoveProp(f.Handle,Marker(record));
            }
        }
        static void DeferredOwnedHide(){
            using(var f=new Fixture())using(var entered=new ManualResetEvent(false))using(var release=new ManualResetEvent(false)){
                var record=Lease(f,Placement(f.Handle).Show);record.ExpectedLeft+=50;record.ExpectedRight+=50;
                f.Form.BeginInvoke(new Action(delegate{entered.Set();WaitForSingleObject(release.SafeWaitHandle.DangerousGetHandle(),2000);}));Check(entered.WaitOne(1000),"Owned message pump blocked before delayed hide");
                bool early=false,retained=false;
                IntPtr foreground=GetForegroundWindow();
                try{Check(ShowWindowAsync(f.Handle,0),"Owned preparation hide queued");early=NativeRestoreGuard.Restore(record);retained=GetProp(f.Handle,Marker(record))==new IntPtr(1);Check(!early&&retained,"Queued compensation retains unconfirmed lease before native execution");}
                finally{release.Set();}
                Thread.Sleep(200);bool visible=IsWindowVisible(f.Handle);
                Console.WriteLine("deferred-real-show-one: originalShow="+record.Show+" returned="+early+" retained="+retained+" finalVisible="+visible+" hides="+f.Form.Hides);
                Check(visible,"A queued owned hide must be compensated even when current visible rectangle is still original");
                Check(GetForegroundWindow()==foreground,"Delayed fallback never activates the fixture");
                Check(!NativeRestoreGuard.Restore(record)&&GetProp(f.Handle,Marker(record))==new IntPtr(1),"A later visible observation is not misrepresented as native queue acknowledgement");
                RemoveProp(f.Handle,Marker(record));
            }
        }
        static void NoRepeatedCompensation(){
            using(var f=new Fixture())using(var entered=new ManualResetEvent(false))using(var release=new ManualResetEvent(false)){
                var r=Lease(f,Placement(f.Handle).Show);r.ExpectedLeft+=50;r.ExpectedRight+=50;Block(f,entered,release);
                try{
                    Check(ShowWindowAsync(f.Handle,0),"First owned hide queued");
                    Check(!NativeRestoreGuard.Restore(r)&&r.VisibilityRestoreRequested,"First compensation is queued and unconfirmed");
                    Check(ShowWindowAsync(f.Handle,0),"Later user hide ordered after first compensation");
                    for(int i=0;i<4;i++)Check(!NativeRestoreGuard.Restore(r),"Repeated recovery cannot certify blocked queue");
                    Check(GetProp(f.Handle,Marker(r))==new IntPtr(1),"Repeated calls retain lease");
                }finally{release.Set();}
                Check(Until(delegate{return f.Form.Hides>=2&&!IsWindowVisible(f.Handle);},1000),"Repeated restoration issued no extra show after the later intentional hide");
                RemoveProp(f.Handle,Marker(r));
            }
        }
        static void PreserveMinimize(bool hidden){
            using(var f=new Fixture()){
                var r=Lease(f,Placement(f.Handle).Show);var original=Placement(f.Handle).Normal;IntPtr foreground=GetForegroundWindow();
                Check(ShowWindowAsync(f.Handle,7)&&Until(delegate{return IsIconic(f.Handle);},1000),"Fixture intentionally minimized without activation");
                if(hidden)Check(ShowWindowAsync(f.Handle,0)&&Until(delegate{return !IsWindowVisible(f.Handle);},1000),"Minimized fixture also hidden");
                Check(!NativeRestoreGuard.Restore(r),"Minimized recovery remains unconfirmed");
                Check(Until(delegate{return IsWindowVisible(f.Handle)&&IsIconic(f.Handle);},1000),"Compensation preserves actual minimized state");
                Check(!r.PositionRestoreRequested&&Same(original,Placement(f.Handle).Normal),"Minimize does not trigger original placement replay");
                Check(GetForegroundWindow()==foreground,"Minimized recovery leaves foreground unchanged");
                Check(GetProp(f.Handle,Marker(r))==new IntPtr(1),"Minimized compensation retains lease");RemoveProp(f.Handle,Marker(r));
            }
        }
        static void PreserveUserMove(){
            using(var f=new Fixture())using(var entered=new ManualResetEvent(false))using(var release=new ManualResetEvent(false)){
                var r=Lease(f,Placement(f.Handle).Show);f.Form.Invoke(new Action(delegate{f.Form.Location=new Point(280,180);}));
                var moved=Bounds(f.Handle);IntPtr foreground=GetForegroundWindow();Block(f,entered,release);
                try{Check(ShowWindowAsync(f.Handle,0),"Moved fixture owned hide queued");Check(!NativeRestoreGuard.Restore(r),"Moved-window compensation stays unconfirmed");}
                finally{release.Set();}
                Check(Until(delegate{return f.Form.Hides>0&&IsWindowVisible(f.Handle);},1000),"Moved fixture becomes visible after queued hide");
                Check(Same(moved,Bounds(f.Handle))&&!r.PositionRestoreRequested,"User position is never moved back to original or compact rectangle");
                Check(GetForegroundWindow()==foreground,"Moved-window recovery does not activate");RemoveProp(f.Handle,Marker(r));
            }
        }
        static void HiddenNormalRestoration(){
            using(var f=new Fixture()){
                var r=Lease(f,Placement(f.Handle).Show);var original=Bounds(f.Handle);IntPtr foreground=GetForegroundWindow();
                Check(ShowWindowAsync(f.Handle,0)&&Until(delegate{return !IsWindowVisible(f.Handle);},1000),"Normal fixture hidden before recovery");
                Check(!NativeRestoreGuard.Restore(r),"Hidden target recovery does not fake acknowledgement");
                Check(Until(delegate{return IsWindowVisible(f.Handle);},1000)&&Same(original,Bounds(f.Handle)),"Hidden normal target is restored at original geometry");
                Check(GetForegroundWindow()==foreground,"Normal placement recovery does not activate");RemoveProp(f.Handle,Marker(r));
            }
        }
        static void RestoreOwnedGeometry(){
            using(var f=new Fixture())using(var entered=new ManualResetEvent(false))using(var release=new ManualResetEvent(false)){
                var r=Lease(f,Placement(f.Handle).Show);var original=Bounds(f.Handle);r.ExpectedLeft=80;r.ExpectedTop=90;r.ExpectedRight=300;r.ExpectedBottom=260;
                f.Form.Invoke(new Action(delegate{f.Form.Bounds=new Rectangle(80,90,220,170);}));IntPtr foreground=GetForegroundWindow();Block(f,entered,release);
                try{Check(!NativeRestoreGuard.Restore(r)&&r.PositionRestoreRequested,"Owned compact position receives one asynchronous restoration");Check(GetProp(f.Handle,Marker(r))==new IntPtr(1),"Owned asynchronous placement does not clear lease");}
                finally{release.Set();}
                Check(Until(delegate{return IsWindowVisible(f.Handle)&&Same(original,Bounds(f.Handle));},1000),"Owned compact geometry returns to original");
                Check(GetForegroundWindow()==foreground,"Owned geometry compensation never activates");RemoveProp(f.Handle,Marker(r));
            }
        }
        static void IdentityRefusal(){
            using(var f=new Fixture()){
                for(int scenario=0;scenario<4;scenario++){
                    var r=Lease(f,Placement(f.Handle).Show);string marker=Marker(r);
                    if(scenario==0)RemoveProp(f.Handle,marker);
                    if(scenario==1)SetProp(f.Handle,marker,new IntPtr(2));
                    if(scenario==2)r.Created++;
                    if(scenario==3)r.Exe="not-the-owned-executable";
                    Check(!NativeRestoreGuard.Restore(r)&&!r.PositionRestoreRequested&&!r.VisibilityRestoreRequested,"Identity or lease mismatch refuses all native requests");
                    Check(IsWindowVisible(f.Handle),"Identity refusal leaves fixture untouched");RemoveProp(f.Handle,marker);
                }
            }
        }
        static void QueueSurvivesRecoveryBudget(){
            using(var f=new Fixture())using(var entered=new ManualResetEvent(false))using(var release=new ManualResetEvent(false)){
                var r=Lease(f,Placement(f.Handle).Show);r.ExpectedLeft+=50;r.ExpectedRight+=50;Block(f,entered,release);
                try{
                    Check(ShowWindowAsync(f.Handle,0),"Slow owned hide queued");var elapsed=Stopwatch.StartNew();
                    Check(!NativeRestoreGuard.Restore(r)&&elapsed.ElapsedMilliseconds<500,"Compensation returns without waiting for target UI");
                    Thread.Sleep(NativeRestoreGuard.RecoveryBudgetMilliseconds+100);
                    Check(IsWindowVisible(f.Handle)&&GetProp(f.Handle,Marker(r))==new IntPtr(1),"Beyond helper budget stale visible sample retains unconfirmed lease");
                }finally{release.Set();}
                Check(Until(delegate{return f.Form.Hides>0&&IsWindowVisible(f.Handle);},1000),"Already queued compensation still follows late hide beyond helper budget");
                RemoveProp(f.Handle,Marker(r));
            }
        }
        [STAThread] public static int Main(){try{
            DeferredOwnedHide();NoRepeatedCompensation();PreserveMinimize(false);PreserveMinimize(true);PreserveUserMove();HiddenNormalRestoration();RestoreOwnedGeometry();IdentityRefusal();QueueSurvivesRecoveryBudget();DeferredHiddenSnapshot();
            Console.WriteLine("PASS "+passed+" assertions; transparent no-activate fixture windows only; no Codex entry invoked.");return 0;
        }catch(Exception e){Console.WriteLine("FAIL "+e.Message);return 1;}}
    }
}

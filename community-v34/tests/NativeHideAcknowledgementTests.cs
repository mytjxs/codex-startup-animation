using System;
using System.Drawing;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace DragonLauncher
{
    internal static class NativeHideAcknowledgementTests
    {
        static int passed;
        static Type policyType;
        const BindingFlags Any=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        static void Check(bool condition,string message){if(!condition)throw new Exception(message);passed++;}
        static object Policy(long now){return Activator.CreateInstance(policyType,Any,null,new object[]{now},null);}
        static string Observe(object policy,long now,bool visible,bool minimized,bool owned)
        {return policyType.GetMethod("Observe",Any).Invoke(policy,new object[]{now,visible,minimized,owned}).ToString();}
        static int Requests(object policy){return (int)policyType.GetProperty("Requests",Any).GetValue(policy,null);}
        static bool NeedsCompensation(int requests,bool ordered)
        {return (bool)typeof(NativeHandoff).GetMethod("NeedsHideCompensation",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{requests,ordered});}
        [DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr window,int command);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        sealed class Window : Form
        {
            internal volatile int ReShowRemaining,Hides;
            internal volatile bool Ending;
            internal Action QueueBarrier;
            bool barrierEntered;
            protected override bool ShowWithoutActivation {get{return true;}}
            internal Window(int reshow){ReShowRemaining=reshow;ShowInTaskbar=false;FormBorderStyle=FormBorderStyle.None;StartPosition=FormStartPosition.Manual;Location=new Point(-30000,-30000);Size=new Size(180,140);}
            protected override void OnVisibleChanged(EventArgs e)
            {
                base.OnVisibleChanged(e);
                if(!Visible&&!Ending){Hides++;if(ReShowRemaining>0){ReShowRemaining--;BeginInvoke(new Action(delegate{if(!Ending)Show();}));}}
            }
            protected override void WndProc(ref Message message)
            {
                base.WndProc(ref message);
                if(message.Msg==0x47&&QueueBarrier!=null&&!barrierEntered&&!IsWindowVisible(Handle)){
                    barrierEntered=true;QueueBarrier();
                }
            }
        }
        sealed class Fixture : IDisposable
        {
            internal Window Form;
            internal IntPtr Handle;
            readonly Thread thread;
            readonly ManualResetEvent ready=new ManualResetEvent(false);
            internal Fixture(int reshow)
            {
                thread=new Thread(delegate(){Form=new Window(reshow);Form.Shown+=delegate{Handle=Form.Handle;ready.Set();};Application.Run(Form);});thread.IsBackground=true;thread.SetApartmentState(ApartmentState.STA);thread.Start();
                if(!ready.WaitOne(2000))throw new Exception("Owned fixture window did not start");
            }
            public void Dispose(){if(Form!=null&&!Form.IsDisposed){Form.Ending=true;Form.BeginInvoke(new Action(Form.Close));}if(!thread.Join(2000))throw new Exception("Owned fixture window did not close");ready.Dispose();}
        }
        static bool Until(Func<bool> condition,int budget)
        {var clock=Stopwatch.StartNew();while(clock.ElapsedMilliseconds<budget){if(condition())return true;Thread.Sleep(10);}return condition();}
        static void PolicyCases()
        {
            object p=Policy(1000);Check(Observe(p,1000,true,false,true)=="RequestHide","Initial visible target requests hide");
            Check(Observe(p,1499,true,false,true)=="Wait"&&Requests(p)==1,"No request before 500 ms");
            Check(Observe(p,1500,true,false,true)=="RequestHide","Self-show can receive second request");
            Check(Observe(p,1750,false,false,true)=="Hidden","Actual hidden observation advances");
            Check(NeedsCompensation(Requests(p),false),"First hidden acknowledgement does not retire other queued hides");
            Check(!NeedsCompensation(Requests(p),true),"Ordered final show retires only this transaction's hide obligation");
            Check(!NeedsCompensation(0,false),"No compensation for a transaction that never hid");
            Check(Observe(p,80000,true,false,true)=="Hidden"&&Requests(p)==2,"Confirmed operation is terminal");
            p=Policy(0);for(int i=0;i<6;i++)Check(Observe(p,i*500,true,false,true)=="RequestHide","Bounded retry cadence");
            Check(Observe(p,2999,true,false,true)=="Wait"&&Requests(p)==6,"Six request maximum");
            Check(Observe(p,3000,true,false,true)=="Expired"&&Requests(p)==6,"Deadline issues no seventh request");
            Check(Observe(p,3001,false,false,true)=="Expired"&&Requests(p)==6,"Late hidden observation cannot mutate after deadline");
            p=Policy(0);Check(Observe(p,0,false,false,true)=="Hidden"&&Requests(p)==0,"Already hidden owned window needs no extra queued hide");
            p=Policy(0);Observe(p,0,true,false,true);Check(Observe(p,500,true,true,true)=="Minimized"&&Requests(p)==1,"Minimize stops retries");
            Check(Observe(p,1000,true,false,true)=="Minimized","Minimize cancellation is terminal");
            p=Policy(0);Check(Observe(p,0,true,false,false)=="LostOwnership"&&Requests(p)==0,"Unowned window receives no request");
            p=Policy(0);Observe(p,0,true,false,true);Check(Observe(p,500,true,false,false)=="LostOwnership"&&Requests(p)==1,"Lost guardian stops retries");
            p=Policy(1000);Observe(p,1000,true,false,true);Check(Observe(p,999,true,false,true)=="Expired","Clock reversal fails closed");
        }
        static void SelfShowRegression()
        {
            using(var f=new Fixture(1)){
                Check(ShowWindowAsync(f.Handle,0),"Old one-shot hide queued");
                Check(Until(delegate{return f.Form.Hides>=1&&IsWindowVisible(f.Handle);},1500),"Owned target re-shows after old hide");
                Thread.Sleep(550);Check(IsWindowVisible(f.Handle),"Old phase-zero predicate still waits after self-show");
            }
            using(var f=new Fixture(1)){
                object policy=Policy(0);var clock=Stopwatch.StartNew();string step="";
                while(clock.ElapsedMilliseconds<3500){step=Observe(policy,clock.ElapsedMilliseconds,IsWindowVisible(f.Handle),IsIconic(f.Handle),true);if(step=="RequestHide")Check(ShowWindowAsync(f.Handle,0),"Guarded fixture hide queued");else if(step!="Wait")break;Thread.Sleep(250);}
                Check(step=="Hidden"&&Requests(policy)==2&&f.Form.Hides>=2,"Real self-show race recovers through second acknowledged hide");
                Check(clock.ElapsedMilliseconds<1600,"Recovery does not wait overall startup deadline");
                Console.WriteLine("native-self-show: requests="+Requests(policy)+" elapsedMs="+clock.ElapsedMilliseconds+" acknowledged=True");
                Check(ShowWindowAsync(f.Handle,8)&&Until(delegate{return IsWindowVisible(f.Handle);},1000),"Owned fixture restored without activation");
            }
        }
        static void PermanentSelfShow()
        {
            using(var f=new Fixture(100)){
                object policy=Policy(0);var clock=Stopwatch.StartNew();string step="";
                while(clock.ElapsedMilliseconds<4000){step=Observe(policy,clock.ElapsedMilliseconds,IsWindowVisible(f.Handle),IsIconic(f.Handle),true);if(step=="RequestHide")Check(ShowWindowAsync(f.Handle,0),"Persistent self-show fixture request queued");else if(step!="Wait")break;Thread.Sleep(250);}
                Check(step=="Expired"&&Requests(policy)==6&&clock.ElapsedMilliseconds>=3000&&clock.ElapsedMilliseconds<3500,"Real persistent self-show has six-request three-second bound");
                Check(ShowWindowAsync(f.Handle,8)&&Until(delegate{return IsWindowVisible(f.Handle);},1000),"Timed-out self-show fixture remains available");
                int requests=Requests(policy);Check(Observe(policy,clock.ElapsedMilliseconds+500,false,false,true)=="Expired"&&Requests(policy)==requests,"Late native hidden sample cannot restart expired transaction");
                Console.WriteLine("native-permanent-self-show: requests="+requests+" elapsedMs="+clock.ElapsedMilliseconds+" expired=True");
            }
        }
        static void LateRequestCompensation(bool minimize)
        {
            using(var f=new Fixture(0))using(var entered=new ManualResetEvent(false))using(var release=new ManualResetEvent(false)){
                IntPtr foreground=GetForegroundWindow();
                if(minimize){Check(ShowWindowAsync(f.Handle,7)&&Until(delegate{return IsIconic(f.Handle);},1000),"Fixture minimized without activation");}
                f.Form.BeginInvoke(new Action(delegate{entered.Set();release.WaitOne(2000);}));Check(entered.WaitOne(1000),"Fixture message processing intentionally blocked");
                Check(ShowWindowAsync(f.Handle,0),"Delayed hide queued on owned fixture");
                Check(ShowWindowAsync(f.Handle,minimize?7:8),"Ordered compensation queued before blocked hide completes");
                release.Set();Check(Until(delegate{return f.Form.Hides>=1&&IsWindowVisible(f.Handle)&&(!minimize||IsIconic(f.Handle));},1500),"Late hide finishes with compensated presentation");
                Thread.Sleep(100);Check(IsWindowVisible(f.Handle)&&(!minimize||IsIconic(f.Handle)),"Compensation remains settled");
                Check(GetForegroundWindow()==foreground,"Compensation does not activate owned fixture");
            }
        }
        static void QueuedHideAfterVisibleConfirmation(bool useProductionRule,bool failedPlacement)
        {
            using(var f=new Fixture(0))using(var entered=new ManualResetEvent(false))using(var start=new ManualResetEvent(false))
            using(var hidden=new ManualResetEvent(false))using(var observed=new ManualResetEvent(false))using(var reShown=new ManualResetEvent(false))using(var finish=new ManualResetEvent(false)){
                try{
                    f.Form.QueueBarrier=delegate{hidden.Set();observed.WaitOne(2000);f.Form.Show();reShown.Set();finish.WaitOne(2000);};
                    f.Form.BeginInvoke(new Action(delegate{entered.Set();start.WaitOne(2000);}));Check(entered.WaitOne(1000),"Fixture pauses before two queued hides");
                    Check(ShowWindowAsync(f.Handle,0)&&ShowWindowAsync(f.Handle,0),"Two owned hide requests queued");start.Set();
                    Check(hidden.WaitOne(1000)&&!IsWindowVisible(f.Handle),"First hidden acknowledgement observed");observed.Set();
                    Check(reShown.WaitOne(1000)&&IsWindowVisible(f.Handle),"Application self-show occurs with second hide pending");
                    bool show=false;
                    if(useProductionRule){
                        var method=typeof(NativeHandoff).GetMethod("NeedsPreparationShow",BindingFlags.Static|BindingFlags.NonPublic);
                        // Until the fix exists, exercise the original shortcut.
                        show=failedPlacement?NeedsCompensation(2,false):method!=null&&(bool)method.Invoke(null,new object[]{true,2});
                    }
                    if(show)Check(ShowWindowAsync(f.Handle,failedPlacement?8:4),"Production confirmation or cleanup queues final nonactivating show");finish.Set();
                    Check(Until(delegate{return f.Form.Hides>=2;},1000),"Second queued hide processed after visible confirmation");
                    if(useProductionRule)Check(Until(delegate{return IsWindowVisible(f.Handle);},1000),"Confirmed placement survives all queued hides");
                    else{Thread.Sleep(100);Check(!IsWindowVisible(f.Handle),"Old already-visible shortcut leaves late second hide unopposed");}
                    Console.WriteLine("native-already-visible-with-queued-hide: productionRule="+useProductionRule+" failedPlacement="+failedPlacement+" visible="+IsWindowVisible(f.Handle));
                }finally{start.Set();observed.Set();finish.Set();}
            }
        }
        static int Main()
        {
            try{policyType=typeof(NativeHandoff).GetNestedType("HideAcknowledgementPolicy",BindingFlags.NonPublic);Check(policyType!=null,"Missing bounded native hide acknowledgement policy");PolicyCases();SelfShowRegression();PermanentSelfShow();LateRequestCompensation(false);LateRequestCompensation(true);QueuedHideAfterVisibleConfirmation(false,false);QueuedHideAfterVisibleConfirmation(true,false);QueuedHideAfterVisibleConfirmation(true,true);Console.WriteLine("PASS "+passed+" assertions; isolated offscreen no-activate native windows only; no Codex entry invoked.");return 0;}
            catch(Exception error){Console.WriteLine("FAIL "+error.GetBaseException().Message);return 1;}
        }
    }
}

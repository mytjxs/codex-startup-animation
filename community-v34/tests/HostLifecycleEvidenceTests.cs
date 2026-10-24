using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;

namespace CodexDragonWallpaper {
public static class HostLifecycleEvidenceTests {
    const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    static void Set(object target,string name,object value){target.GetType().GetField(name,Hidden).SetValue(target,value);}
    static object Get(object target,string name){return target.GetType().GetField(name,Hidden).GetValue(target);}
    static CdpTransport Transport(){var cdp=(CdpTransport)FormatterServices.GetUninitializedObject(typeof(CdpTransport));Set(cdp,"gate",new object());var f=typeof(CdpTransport).GetField("pending",Hidden);f.SetValue(cdp,Activator.CreateInstance(f.FieldType));return cdp;}
    static void Frame(CdpTransport cdp,object packet){var bytes=Encoding.UTF8.GetBytes(Json.Serializer().Serialize(packet));typeof(CdpTransport).GetMethod("Frame",Hidden).Invoke(cdp,new object[]{bytes,bytes.Length});}
    static void Reject(Action action){bool failed=false;try{action();}catch(ArgumentOutOfRangeException){failed=true;}Check(failed,"invalid diagnostic value was accepted");}
    public static int Main(string[] args){try{
        // Every fixture is in-process. No real app, native window, profile or
        // process is launched, inspected or altered.
        Directory.CreateDirectory(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"output"));
        foreach(string kind in new[]{"transport-fault","target-detached","target-crashed","inspector-detached","unknown"})foreach(int exited in new[]{-1,0,1}){
            string line=PreparationDiagnostics.FormatLoss(12,20000,kind,35,exited);
            Check(line.Contains("phase=46")&&line.Contains("sourcePhase=12")&&line.Contains("lossKind="+kind)&&line.Contains("processExited="+exited),"missing loss classification");
            Check(line.Length<1024&&!line.Contains("app://")&&!line.Contains("sessionId")&&!line.Contains("targetId"),"unexpected diagnostic payload");
        }
        Reject(()=>PreparationDiagnostics.FormatLoss(64,0,"unknown",0,0));
        Reject(()=>PreparationDiagnostics.FormatLoss(0,0,"secret-payload",0,0));
        Reject(()=>PreparationDiagnostics.FormatLoss(0,0,"unknown",100,0));
        Reject(()=>PreparationDiagnostics.FormatLoss(0,0,"unknown",0,2));
        string unknown=PreparationDiagnostics.FormatProcessExit(50,false,uint.MaxValue);
        Check(unknown.Contains("exitCodeKnown=0 processExitCode=0"),"unknown exit exported stale code");
        Check(PreparationDiagnostics.FormatProcessExit(50,true,uint.MaxValue).Contains("processExitCode=4294967295"),"native unsigned code truncated");
        for(int outcome=1;outcome<=8;outcome++){
            string line=PreparationDiagnostics.FormatInitialVisibility(outcome,10,2,129,0,true);
            Check(line.Contains("phase=49")&&line.Contains("eligibleHidden=129 visible=0 markerOwned=1")&&line.Length<1024,"visibility evidence incorrect");
        }
        Reject(()=>PreparationDiagnostics.FormatInitialVisibility(9,0,0,0,0,false));
        Reject(()=>PreparationDiagnostics.FormatInitialVisibility(1,0,7,0,0,false));
        Reject(()=>PreparationDiagnostics.FormatInitialVisibility(1,0,0,130,0,false));
        {
            var cdp=Transport();var state=new HostState();var session=new WallpaperSession(cdp,state,null,true);Set(session,"session","trusted_session");
            var handler=(Action<string,string>)Delegate.CreateDelegate(typeof(Action<string,string>),session,typeof(WallpaperSession).GetMethod("OnSignal",Hidden));cdp.Signal+=handler;
            foreach(string method in new[]{"Target.detachedFromTarget","Inspector.targetCrashed","Inspector.detached"}){
            bool previous=(bool)Get(session,"targetLost");
            object foreign=method=="Target.detachedFromTarget"?Json.Obj("method",method,"params",Json.Obj("sessionId","foreign_session")):Json.Obj("method",method,"sessionId","foreign_session");
            Frame(cdp,foreign);Check((bool)Get(session,"targetLost")==previous,"foreign session marked own target lost");
            object own=method=="Target.detachedFromTarget"?Json.Obj("method",method,"params",Json.Obj("sessionId","trusted_session")):Json.Obj("method",method,"sessionId","trusted_session");
            Frame(cdp,own);Check((bool)Get(session,"targetLost"),"own target loss ignored");
            Check(cdp.TargetLossKind==(method=="Target.detachedFromTarget"?1:method=="Inspector.targetCrashed"?2:3),"loss reason collapsed");
            Frame(cdp,own);
            }
            cdp.Signal-=handler;state.Work.Dispose();state.TerminalRead.Dispose();
        }
        {
            var cdp=Transport();var state=new HostState();var session=new WallpaperSession(cdp,state,null,true);
            var handler=(Action<string,string>)Delegate.CreateDelegate(typeof(Action<string,string>),session,typeof(WallpaperSession).GetMethod("OnSignal",Hidden));cdp.Signal+=handler;
            typeof(CdpTransport).GetMethod("Fail",Hidden).Invoke(cdp,new object[]{35});
            Check((bool)Get(session,"transportLost")&&cdp.FaultCode==35&&cdp.ProcessExitState==-1,"unknown process exit misreported");
            typeof(CdpTransport).GetMethod("Fail",Hidden).Invoke(cdp,new object[]{33});Check(cdp.FaultCode==35,"terminal first cause overwritten");
            cdp.Signal-=handler;state.Work.Dispose();state.TerminalRead.Dispose();
        }
        PreparationDiagnostics.RecordProcessExit(100,true,0);var clock=Stopwatch.StartNew();PreparationDiagnostics.FlushOnExit();Check(clock.ElapsedMilliseconds<1000,"termination flush unbounded");
        var log=File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"output","wallpaper-readiness.log"));
        foreach(string kind in new[]{"target-detached","target-crashed","inspector-detached","transport-fault"})Check(log.Split(new[]{"lossKind="+kind},StringSplitOptions.None).Length-1==1,"loss missing or duplicate "+kind);
        Check(log.Contains("phase=47")&&log.Contains("exitCodeKnown=1 processExitCode=0"),"final process evidence lost");
        Check(!log.Contains("trusted_session")&&!log.Contains("foreign_session")&&!log.Contains("secret-payload"),"private fixture values exported");
        // A stuck logging sink must not hold application exit. No IO thread is
        // scheduled: this exercises the production monotonic wait directly.
        var writer=typeof(PreparationDiagnostics).GetField("writerBusy",BindingFlags.Static|BindingFlags.NonPublic);writer.SetValue(null,1);clock.Restart();PreparationDiagnostics.FlushOnExit();writer.SetValue(null,0);
        Check(clock.ElapsedMilliseconds>=150&&clock.ElapsedMilliseconds<1000,"stuck sink did not obey 200ms cap");
        Console.WriteLine(Json.Serializer().Serialize(Json.Obj("passed",true,"checks",checks,"liveAppTouched",false,"lossAndExitSeparated",true)));return 0;
    }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
}

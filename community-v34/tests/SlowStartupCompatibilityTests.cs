using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace DragonLauncher {
internal static class SlowStartupCompatibilityTests {
    static int checks;
    static readonly List<object> cases=new List<object>();
    static void Check(bool condition,string name){checks++;if(!condition)throw new Exception(name);}
    static BridgeStatus Sample(object owner,StartupPreparation flow,long sequence) {
        var bridge=owner as BridgeSession;long created=1;int host=1;
        if(bridge!=null){host=bridge.RetainedProcess.Id;Check(BridgeNative.CreationTime(bridge.RetainedProcess.Handle,out created),"owned host creation time");}
        return new BridgeStatus {Version=1,Disposition=2,HostPid=host,HostCreated=created,CodexPid=42,CodexCreated=99,
            ProtocolVersion=2,DocumentGeneration=new string('a',32),MediaGeneration=1,ObservationAgeMs=0,
            PrepareOperationId=flow.OperationId,Prepared=true,CommitAccepted=false,ActualPhase="prepared",Health="fresh",Sequence=sequence,
            Ready=true,MediaCode=3,RefreshCode=1,DiagnosticsVersion=1,LayoutSupported=true,ProbeOnly=false,Attached=true,
            Playing=false,WallpaperApplied=false,FirstFrameConfirmed=false,DocumentHidden=false,StageVisible=false,MediaTimeMs=200};
    }
    static void Record(string name,int variants){cases.Add(new {name=name,variants=variants,passed=true});}
    static void VirtualLatency() {
        int count=0;
        foreach(int delivery in new[]{0,1,250,750,1499,2000,3999,4000,4001,90000})
        foreach(int age in new[]{0,1,250,1000,3999,4000}){
            var flow=new StartupPreparation();var owner=new object();var request=flow.Begin(owner,0);var status=Sample(owner,flow,1);status.ObservationAgeMs=age;
            bool expected=(long)delivery+age<4000;
            Check(flow.Accept(request,owner,status,delivery/1000.0)==expected,"combined remote and UI queue age boundary");
            Check(flow.IsPrepared(owner,delivery/1000.0)==expected,"late reply never certifies readiness");count++;
        }
        Record("reply-age-plus-dispatch-delay-exact-4s-boundary",count);
        var prep=new StartupPreparation();var session=new object();var first=prep.Begin(session,0);var one=Sample(session,prep,1);
        Check(prep.Accept(first,session,one,0.1),"seed prepared operation");
        foreach(double now in new[]{4.1,6,12,60,90}){
            Check(!prep.IsPrepared(session,now),"scheduler stall expires readiness");
            Check(prep.Begin(session,now).ObserveOnly,"scheduler stall requests observation not prepare");
        }
        var latestRequest=prep.Begin(session,90);var latest=Sample(session,prep,8);
        Check(prep.Accept(latestRequest,session,latest,90.1),"fresh response after scheduling stall accepted");
        Check(!prep.Accept(prep.Begin(session,90.2),session,Sample(session,prep,7),90.3),"older response cannot replace fresh proof");
        Check(prep.Snapshot.Sequence==8&&prep.IsPrepared(session,90.3),"reordered old response preserves newer proof");
        Record("virtual-scheduler-stall-no-reprepare-and-reordered-responses",8);
    }
    static void FinaleAndVisibility() {
        foreach(double lag in new[]{0,0.5,3,5.999,6,6.001,30}){
            var prep=new StartupPreparation();var owner=new object();var s=Sample(owner,prep,1);
            Check(prep.Accept(prep.Begin(owner,0),owner,s,0.1)&&prep.Commit(owner,0.1),"commit exact prepared generation");
            Check(prep.CanHandoff(owner,s,0.1+lag,false)==(lag<=6),"finale lease does not grow with late scheduling");
            Check(prep.Begin(owner,0.1+lag).ObserveOnly,"committed finale never repeats preparation");
        }
        Record("six-second-final-lease-boundary",7);
        var flow=new StartupPreparation();var token=new object();var first=Sample(token,flow,1);
        first.DocumentHidden=true;
        Check(flow.Accept(flow.Begin(token,0),token,first,0.1)&&flow.IsPrepared(token,0.1),"hidden decoded media can be prepared");
        Check(WallpaperReadiness.Inspect(first)==WallpaperReadinessState.Hidden,"hidden preparation is not playback");
        var a=Sample(token,flow,2);a.Revealed=true;a.CommitAccepted=true;a.MediaCode=4;a.ActualPhase="visible";a.WallpaperApplied=true;a.StageVisible=true;a.Playing=true;a.FirstFrameConfirmed=false;
        Check(WallpaperReadiness.Inspect(a)==WallpaperReadinessState.FirstFramePending,"visible stage without frame is pending");
        a.FirstFrameConfirmed=true;var b=Sample(token,flow,3);b.Revealed=true;b.CommitAccepted=true;b.MediaCode=4;b.ActualPhase="visible";b.WallpaperApplied=true;b.StageVisible=true;b.Playing=true;b.FirstFrameConfirmed=true;b.MediaTimeMs=500;
        var c=Sample(token,flow,4);c.Revealed=true;c.CommitAccepted=true;c.MediaCode=4;c.ActualPhase="visible";c.WallpaperApplied=true;c.StageVisible=true;c.Playing=true;c.FirstFrameConfirmed=true;c.MediaTimeMs=800;
        Check(WallpaperReadiness.ConfirmPlayback(a,b,c),"three same-generation visible progressing samples confirm playback");
        b.DocumentHidden=true;Check(!WallpaperReadiness.ConfirmPlayback(a,b,c),"visibility interruption breaks playback proof");
        b.DocumentHidden=false;b.MediaGeneration=2;Check(!WallpaperReadiness.ConfirmPlayback(a,b,c),"decoder restart breaks playback proof");
        b.MediaGeneration=1;b.Sequence=a.Sequence;Check(!WallpaperReadiness.ConfirmPlayback(a,b,c),"duplicate sample cannot prove progression");
        Record("hidden-to-visible-first-frame-and-progression",7);
    }
    static void SlowHideSequence() {
        foreach(int cadence in new[]{1,249,500,777,1499,2999,5000}){
            var policy=new NativeHandoff.HideAcknowledgementPolicy(0);NativeHandoff.HideAcknowledgement step=NativeHandoff.HideAcknowledgement.Wait;
            int requests=0;long terminalAt=0;
            for(long now=0;now<=9000;now+=cadence){step=policy.Observe(now,true,false,true);if(step==NativeHandoff.HideAcknowledgement.RequestHide)requests++;if(step==NativeHandoff.HideAcknowledgement.Expired){terminalAt=now;break;}}
            Check(step==NativeHandoff.HideAcknowledgement.Expired,"delayed hide acknowledgement stops at first expired observation");
            Check(requests<=6&&policy.Requests==requests,"slow timer never creates retry burst");
            Check(terminalAt>=3000&&terminalAt<3000+cadence,"single hide deadline never resets");
            Check(policy.Observe(terminalAt+1,false,false,true)==NativeHandoff.HideAcknowledgement.Expired,"late hidden state cannot revive expired attempt");
        }
        var cancelled=new NativeHandoff.HideAcknowledgementPolicy(0);cancelled.Observe(0,true,false,true);
        Check(cancelled.Observe(2000,true,true,true)==NativeHandoff.HideAcknowledgement.Minimized,"slow minimized sample preserves user intent");
        Check(cancelled.Observe(2500,false,false,true)==NativeHandoff.HideAcknowledgement.Minimized,"hidden arrival does not undo minimize cancellation");
        Record("irregular-native-observation-cadence-no-deadline-extension",8);
    }
    sealed class Peer:IDisposable {
        readonly NamedPipeServerStream pipe;readonly Thread thread;readonly BridgeStatus response;
        internal readonly ManualResetEvent Seen=new ManualResetEvent(false),Release;
        internal string Command;internal Exception Failure;internal bool PeerClosed;
        internal Peer(string token,BridgeStatus response,bool hold){
            this.response=response;Release=new ManualResetEvent(!hold);
            pipe=new NamedPipeServerStream(BridgeActivation.PipeName(token),PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous);
            thread=new Thread(Serve){IsBackground=true};thread.Start();
        }
        void Serve(){try{
            var pending=pipe.BeginWaitForConnection(null,null);try{if(!pending.AsyncWaitHandle.WaitOne(2000))throw new Exception("owned peer connection deadline");pipe.EndWaitForConnection(pending);}finally{pending.AsyncWaitHandle.Close();}
            using(var request=new MemoryStream()){
                for(int i=0;i<128;i++){int value=pipe.ReadByte();if(value<0)throw new EndOfStreamException();if(value==10)break;request.WriteByte((byte)value);}
                Command=Encoding.UTF8.GetString(request.ToArray());Seen.Set();
            }
            if(!Release.WaitOne(2000))throw new Exception("owned peer release deadline");
            using(var data=new MemoryStream()){new DataContractJsonSerializer(typeof(BridgeStatus)).WriteObject(data,response);byte[] bytes=data.ToArray();pipe.Write(bytes,0,bytes.Length);pipe.WriteByte(10);pipe.Flush();}
        }catch(IOException){PeerClosed=true;}catch(Exception error){Failure=error;}finally{Seen.Set();}}
        internal void Finish(){Release.Set();Check(thread.Join(2500),"owned peer thread stopped");Check(Failure==null,"owned peer bounded completion");}
        public void Dispose(){Release.Set();if(!thread.Join(2500)){pipe.Dispose();thread.Join(500);}pipe.Dispose();Seen.Dispose();Release.Dispose();}
    }
    sealed class Call {
        internal BridgeStatus Result;internal Exception Failure;readonly Thread thread;
        internal Call(Func<BridgeStatus> invoke){thread=new Thread(()=>{try{Result=invoke();}catch(Exception e){Failure=e;}}){IsBackground=true};thread.Start();}
        internal void Join(){Check(thread.Join(2000),"bounded launcher call completed");}
    }
    static void Seed(BridgeSession session,string token,StartupPreparation flow){
        var request=flow.Begin(session,0);using(var peer=new Peer(token,Sample(session,flow,1),false)){
            BridgeStatus value=session.ObservePreparation(request,1000);peer.Finish();
            Check(peer.Command=="prepare "+flow.OperationId,"first preparation command only");Check(flow.Accept(request,session,value,0.1),"initial actual IPC sample accepted");
        }
    }
    static void SlowPipeTimeoutRecovery() {
        string token=Guid.NewGuid().ToString("N");using(var session=new BridgeSession(Process.GetCurrentProcess(),token,null)){
            var flow=new StartupPreparation();Seed(session,token,flow);var request=flow.Begin(session,0.5);BridgeStatus old=session.LastStatus;
            using(var peer=new Peer(token,Sample(session,flow,2),true)){
                var call=new Call(()=>session.ObservePreparation(request,120));Check(peer.Seen.WaitOne(1200),"delayed refresh received");call.Join();
                Check(call.Failure is TimeoutException,"delayed refresh fails with bounded timeout");Check(peer.Command=="refresh","delayed confirmed preparation is read only");
                Check(Object.ReferenceEquals(old,session.LastStatus),"timeout cannot invent a fresh reply");peer.Finish();
            }
            Check(!flow.IsPrepared(session,10)&&flow.Begin(session,10).ObserveOnly,"timed-out stale proof is not readiness or new prepare");
            var next=flow.Begin(session,10);using(var peer=new Peer(token,Sample(session,flow,3),false)){
                BridgeStatus fresh=session.ObservePreparation(next,1000);peer.Finish();Check(peer.Command=="refresh","recovery uses read only command");
                Check(flow.Accept(next,session,fresh,10.1)&&flow.IsPrepared(session,10.1),"independent fresh reply recovers readiness");
            }
            Check(!session.HostExited&&!session.RetainedProcess.HasExited,"timeout leaves retained owned host alive");
        }
        Record("actual-private-pipe-response-timeout-then-fresh-observation",1);
    }
    static void SerializedIoBudget() {
        string token=Guid.NewGuid().ToString("N");using(var session=new BridgeSession(Process.GetCurrentProcess(),token,null)){
            var flow=new StartupPreparation();Seed(session,token,flow);var request=flow.Begin(session,0.5);
            using(var peer=new Peer(token,Sample(session,flow,2),true)){
                var first=new Call(()=>session.ObservePreparation(request,1500));Check(peer.Seen.WaitOne(1200),"first serialized request received");
                Exception secondFailure=null;var timer=Stopwatch.StartNew();try{session.ObservePreparation(request,80);}catch(Exception error){secondFailure=error;}
                Check(secondFailure is TimeoutException&&secondFailure.Message=="bridge-ipc-lock-timeout","I/O gate wait consumes request deadline before any second pipe connection");Check(timer.ElapsedMilliseconds<700,"I/O gate timeout remains bounded");
                peer.Finish();first.Join();Check(first.Failure==null&&first.Result.Sequence==2,"slow first response remains usable");Check(peer.Command=="refresh","serialized requests do not mutate preparation");
                Check(flow.Accept(request,session,first.Result,0.8),"serialized response accepted with captured request time");
            }
            Check(!session.HostExited,"serialized timeout does not terminate host");
        }
        Record("actual-private-pipe-lock-wait-shares-caller-deadline",1);
    }
    static void CancelBeforeLateDelivery() {
        string token=Guid.NewGuid().ToString("N");using(var session=new BridgeSession(Process.GetCurrentProcess(),token,null)){
            var flow=new StartupPreparation();Seed(session,token,flow);var request=flow.Begin(session,0.5);
            using(var peer=new Peer(token,Sample(session,flow,2),true)){
                var pending=new Call(()=>session.ObservePreparation(request,1500));Check(peer.Seen.WaitOne(1200),"cancellable refresh received");
                flow.Cancel();peer.Finish();pending.Join();Check(pending.Failure==null,"late IPC reply may finish safely");
                Check(!flow.Accept(request,session,pending.Result,0.7)&&flow.Begin(session,0.8)==null,"late UI delivery cannot undo cancellation");
                Check(peer.Command=="refresh"&&!session.HostExited,"cancellation leaves owned host alive without repeat prepare");
            }
        }
        Record("actual-private-pipe-cancel-before-late-delivery",1);
    }
    [STAThread] public static int Main(){try{
        var watch=Stopwatch.StartNew();VirtualLatency();FinaleAndVisibility();SlowHideSequence();SlowPipeTimeoutRecovery();SerializedIoBudget();CancelBeforeLateDelivery();
        Console.WriteLine(new JavaScriptSerializer().Serialize(new {passed=true,checks=checks,elapsedMs=watch.ElapsedMilliseconds,actualCodexStarted=false,resourceStressApplied=false,mediaModified=false,ownedNamedPipes=true,cases=cases}));return 0;
    }catch(Exception e){Console.Error.WriteLine("FAIL "+e.GetType().Name+": "+e.Message);return 1;}}
}
}

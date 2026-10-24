using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;

namespace DragonLauncher {
internal static class StartupObservationTests {
    static int checks;
    static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
    static bool ObserveOnly(StartupPreparation.Request request){
        var field=typeof(StartupPreparation.Request).GetField("ObserveOnly",BindingFlags.Instance|BindingFlags.NonPublic);
        // The baseline has no observation-only request and always sends Prepare.
        return field!=null&&(bool)field.GetValue(request);
    }
    static BridgeStatus Reply(BridgeSession session,StartupPreparation state,long sequence){
        long created;Check(BridgeNative.CreationTime(session.RetainedProcess.Handle,out created),"own process creation");
        return new BridgeStatus{Version=1,Disposition=2,HostPid=session.RetainedProcess.Id,HostCreated=created,
            CodexPid=42,CodexCreated=99,ProtocolVersion=2,DocumentGeneration=new string('a',32),
            MediaGeneration=1,ObservationAgeMs=0,PrepareOperationId=state.OperationId,Prepared=true,
            CommitAccepted=false,ActualPhase="prepared",Health="fresh",Sequence=sequence,
            Ready=true,MediaCode=3,RefreshCode=1,DiagnosticsVersion=1,LayoutSupported=true,ProbeOnly=false,Attached=true,
            Playing=false,WallpaperApplied=false,FirstFrameConfirmed=false,DocumentHidden=false,StageVisible=false,MediaTimeMs=200};
    }
    static string Exchange(BridgeSession session,string token,StartupPreparation.Request request,BridgeStatus status){
        using(var pipe=new NamedPipeServerStream(BridgeActivation.PipeName(token),PipeDirection.InOut,1,
                PipeTransmissionMode.Byte,PipeOptions.Asynchronous)){
            string seen=null;Exception serverError=null;
            var worker=new Thread(delegate(){
                try{
                    var pending=pipe.BeginWaitForConnection(null,null);
                    if(!pending.AsyncWaitHandle.WaitOne(3000))throw new TimeoutException("synthetic pipe connection");
                    pipe.EndWaitForConnection(pending);
                    var data=new MemoryStream();
                    for(int i=0;i<128;i++){int b=pipe.ReadByte();if(b<0)throw new EndOfStreamException();if(b==10)break;data.WriteByte((byte)b);}
                    seen=Encoding.UTF8.GetString(data.ToArray());
                    var bytes=new MemoryStream();new DataContractJsonSerializer(typeof(BridgeStatus)).WriteObject(bytes,status);
                    byte[] reply=bytes.ToArray();pipe.Write(reply,0,reply.Length);pipe.WriteByte(10);pipe.Flush();
                }catch(Exception e){serverError=e;}
            }){IsBackground=true};
            worker.Start();
            var method=typeof(BridgeSession).GetMethod("ObservePreparation",BindingFlags.Instance|BindingFlags.NonPublic);
            if(method==null)session.Prepare(request.OperationId,1800); // exact legacy dispatch
            else method.Invoke(session,new object[]{request,1800});
            Check(worker.Join(3000),"owned pipe completed");Check(serverError==null,"owned pipe server succeeded");
            return seen;
        }
    }
    static void Run(){
        string token=Guid.NewGuid().ToString("N");
        using(var session=new BridgeSession(Process.GetCurrentProcess(),token,delegate(string ignored){})){
            var prep=new StartupPreparation();var first=prep.Begin(session,0);
            Check(first!=null&&!ObserveOnly(first),"unconfirmed preparation must send prepare");
            var state=Reply(session,prep,1);
            Check(Exchange(session,token,first,state)=="prepare "+prep.OperationId,"first actual pipe command");
            Check(prep.Accept(first,session,state,0.1),"accept prepared sample");Check(prep.IsPrepared(session,0.1),"prepared evidence");
            var observed=prep.Begin(session,0.5);
            Check(ObserveOnly(observed),"confirmed preparation must become observation-only");
            state=Reply(session,prep,2);
            Check(Exchange(session,token,observed,state)=="refresh","confirmed actual pipe command is refresh");
            Check(prep.Accept(observed,session,state,0.6),"refresh accepted");
            var uncertain=prep.Begin(session,0.7);state=Reply(session,prep,3);state.RefreshCode=3;state.Ready=false;
            Check(prep.Accept(uncertain,session,state,0.8),"timed-out observation retains exact prepared operation");
            Check(!prep.IsPrepared(session,0.9),"timed-out acknowledgement is not readiness proof");
            Check(ObserveOnly(prep.Begin(session,0.9)),"uncertain observation never repeats confirmed prepare");
            var stale=prep.Begin(session,10);
            Check(!prep.IsPrepared(session,10),"age still rejects stale readiness");
            Check(ObserveOnly(stale),"old preparation proof requests fresh observation, not mutation");
            state=Reply(session,prep,4);
            Check(prep.Accept(stale,session,state,10.1),"fresh observation accepted");
            Check(prep.Commit(session,10.1),"local finale commit");
            var finale=prep.Begin(session,10.5);Check(ObserveOnly(finale),"committed finale never sends prepare");
            Check(Exchange(session,token,finale,state)=="refresh","committed actual pipe command is refresh");
            Check(prep.CanHandoff(session,state,10.6,false),"same-generation preparation lease retained");
            Check(!prep.CanHandoff(session,state,16.2,false),"six-second finale lease unchanged");
            state=Reply(session,prep,5);state.MediaGeneration=2;
            Check(!prep.Accept(finale,session,state,10.7)&&prep.IsTerminal,"generation change after commit terminal");
            Check(prep.Begin(session,10.8)==null,"terminal request rejected");
        }
        object same=new object();var flow=new StartupPreparation();var request=flow.Begin(same,0);
        using(var session=new BridgeSession(Process.GetCurrentProcess(),Guid.NewGuid().ToString("N"),delegate(string ignored){})){
            var state=Reply(session,flow,1);
            Check(flow.Accept(request,same,state,0.1),"independent prepared sample");
            var fresh=flow.Begin(same,0.2);state=Reply(session,flow,2);state.Prepared=false;state.ActualPhase="preparing";state.Ready=false;
            Check(flow.Accept(fresh,same,state,0.3),"lost-preparation observation accepted");
            Check(!ObserveOnly(flow.Begin(same,0.4)),"lost preparation allows new prepare");
            state=Reply(session,flow,3);request=flow.Begin(same,0.5);Check(flow.Accept(request,same,state,0.6),"recovered preparation accepted");
            Check(!ObserveOnly(flow.Begin(new object(),0.7)),"different session cannot borrow evidence");
            string oldId=flow.OperationId;flow.InvalidateTarget();
            Check(flow.OperationId!=oldId&&!ObserveOnly(flow.Begin(same,0.8)),"target replacement needs fresh operation");
            Check(!flow.Accept(request,same,state,0.9),"inflight old operation rejected");
            flow.Cancel();Check(flow.Begin(same,1)==null,"cancel remains terminal");
        }
    }
    [STAThread] static int Main(){
        try{Run();Console.WriteLine("{\"passed\":true,\"checks\":"+checks+",\"actualCodexStarted\":false,\"ownedNamedPipeUsed\":true}");return 0;}
        catch(Exception e){Console.WriteLine("FAIL: "+e.Message);return 1;}
    }
}
}


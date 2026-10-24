using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;

namespace DragonLauncher {
internal static class BridgeCommandDrainTests {
 static int checks;
 static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
 static void Check(bool value,string text){checks++;if(!value)throw new Exception(text);}
 static object Read(object instance,string field){return instance.GetType().GetField(field,Private).GetValue(instance);}
 static bool Wait(Func<bool> done,int milliseconds){var watch=Stopwatch.StartNew();while(!done()&&watch.ElapsedMilliseconds<milliseconds)Thread.Sleep(10);return done();}
 sealed class Peer:IDisposable {
  readonly NamedPipeServerStream pipe;readonly Thread worker;readonly int delay,expected;
  internal readonly ManualResetEvent FirstRequest=new ManualResetEvent(false);
  internal readonly List<string> Commands=new List<string>();internal Exception Failure;
  internal Peer(string token,int delay,int expected){
   this.delay=delay;this.expected=expected;
   pipe=new NamedPipeServerStream(BridgeActivation.PipeName(token),PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous);
   worker=new Thread(Serve){IsBackground=true};worker.Start();
  }
  void Serve(){try{
   for(int request=0;request<expected;request++){
    var connection=pipe.BeginWaitForConnection(null,null);
    try{if(!connection.AsyncWaitHandle.WaitOne(3500))return;pipe.EndWaitForConnection(connection);}finally{connection.AsyncWaitHandle.Close();}
    using(var bytes=new MemoryStream()){
     for(int i=0;i<96;i++){int value=pipe.ReadByte();if(value<0)throw new EndOfStreamException();if(value==10)break;bytes.WriteByte((byte)value);}
     lock(Commands)Commands.Add(Encoding.UTF8.GetString(bytes.ToArray()));
    }
    if(request==0){FirstRequest.Set();Thread.Sleep(delay);}
    using(var own=Process.GetCurrentProcess()){
     long created;BridgeNative.CreationTime(own.Handle,out created);
     var status=new BridgeStatus{Version=1,Disposition=2,HostPid=own.Id,HostCreated=created,CodexPid=own.Id,CodexCreated=created,MediaCode=4,Ready=true};
     using(var bytes=new MemoryStream()){
      new DataContractJsonSerializer(typeof(BridgeStatus)).WriteObject(bytes,status);byte[] reply=bytes.ToArray();pipe.Write(reply,0,reply.Length);pipe.WriteByte(10);pipe.Flush();
     }
    }
    pipe.Disconnect();
   }
  }catch(Exception error){Failure=error;}}
  internal void Join(){Check(worker.Join(4000),"owned pipe completes within bound");Check(Failure==null,"owned pipe has no protocol error");}
  public void Dispose(){pipe.Dispose();worker.Join(250);FirstRequest.Dispose();}
 }
 static void ConcurrentPrepare(){
  string token=Guid.NewGuid().ToString("N");var logs=new List<string>();
  using(var peer=new Peer(token,1700,2)){
   var session=new BridgeSession(Process.GetCurrentProcess(),token,line=>{lock(logs)logs.Add(line);});Exception prepareError=null;
   var prepare=new Thread(delegate(){try{session.Prepare(Guid.NewGuid().ToString("N"),1800);}catch(Exception error){prepareError=error;}}){IsBackground=true};
   prepare.Start();Check(peer.FirstRequest.WaitOne(1500),"actual prepare owns IPC before reveal is queued");
   var watch=Stopwatch.StartNew();session.Queue("reveal");session.Dispose();
   Check(!(bool)Read(session,"disposed"),"Dispose retains identity while prepare/reveal are outstanding");
   Check(prepare.Join(2200)&&prepareError==null,"prepare reply completes within production 1800ms budget");
   Check(Wait(()=>(bool)Read(session,"disposed"),3500),"all commands drain and retained process is disposed");
   lock(logs)Check(logs.Count==0,"reveal must not expire behind valid in-flight prepare");
   peer.Join();lock(peer.Commands)Check(peer.Commands.Count==2&&peer.Commands[0].StartsWith("prepare ",StringComparison.Ordinal)&&peer.Commands[1]=="reveal","Host receives prepare then reveal exactly once after Dispose");
   Check(watch.ElapsedMilliseconds<3500,"complete reveal drain remains bounded");
  }
 }
 static void TerminalDisable(){
  string token=Guid.NewGuid().ToString("N");using(var peer=new Peer(token,0,1)){
   var session=new BridgeSession(Process.GetCurrentProcess(),token,null);session.Queue("disable");session.Queue("reveal");session.Dispose();peer.Join();
   Check(Wait(()=>(bool)Read(session,"disposed"),1000),"disabled session drains and releases identity");
   lock(peer.Commands)Check(peer.Commands.Count==1&&peer.Commands[0]=="disable","terminal disable still suppresses queued reveal");
  }
 }
 static void SaturatedLock(){
  var session=new BridgeSession(Process.GetCurrentProcess(),Guid.NewGuid().ToString("N"),null);
  object gate=Read(session,"ioGate");var entered=new ManualResetEvent(false);var release=new ManualResetEvent(false);
  var owner=new Thread(delegate(){lock(gate){entered.Set();release.WaitOne(5000);}}){IsBackground=true};owner.Start();
  try{
   Check(entered.WaitOne(1000),"fixture owns isolated session IO lock");var watch=Stopwatch.StartNew();session.Queue("reveal");session.Dispose();
   Check(Wait(()=>(bool)Read(session,"disposed"),3600),"unavailable IO lock cannot outlive total drain deadline");
   Check(watch.ElapsedMilliseconds<3600,"total command drain remains at most 3 seconds plus scheduling tolerance");
  }finally{release.Set();owner.Join(1000);entered.Dispose();release.Dispose();session.Dispose();}
 }
 static int Main(){try{
  ConcurrentPrepare();TerminalDisable();SaturatedLock();
  Console.WriteLine("{\"passed\":true,\"checks\":"+checks+",\"realCodexLaunched\":false,\"ownedAuthenticatedPipe\":true,\"prepareBudgetMs\":1800,\"peerDelayMs\":1700,\"totalDrainBoundMs\":3000}");return 0;
 }catch(Exception error){Console.WriteLine("FAIL: "+error.Message);return 1;}}
}
}

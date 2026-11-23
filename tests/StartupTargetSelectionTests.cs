using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodexDragonWallpaper {
// Executes real unmodified SelectTarget, Send and Frame. No native app is started.
public static class StartupTargetSelectionTests {
 const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
 static readonly List<object> records=new List<object>();
 static void Set(object value,string name,object data){value.GetType().GetField(name,Flags).SetValue(value,data);}
 static object Get(object value,string name){return value.GetType().GetField(name,Flags).GetValue(value);}
 static void Check(bool ok,string text){if(!ok)throw new Exception(text);}
 sealed class Peer:FileStream {
  internal CdpTransport Transport;internal int Calls;internal bool StallWrite;internal Action<Peer,Dictionary<string,object>,int> Script;
  internal readonly List<string> Methods=new List<string>();internal readonly List<Task> Jobs=new List<Task>();
  internal bool NulFrames=true,BrowserCommandsNoSession=true,AttachFlatten=true;
  internal Peer(string path):base(path,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.ReadWrite,1,FileOptions.Asynchronous){}
  internal void Reply(Dictionary<string,object> request,object result,int delay){
   var packet=Json.Obj("id",Json.Get(request,"id"),"result",result);if(Json.Get(request,"sessionId")!=null)packet["sessionId"]=Json.Get(request,"sessionId");
   var bytes=Encoding.UTF8.GetBytes(Json.Serializer().Serialize(packet));
   Action receive=()=>typeof(CdpTransport).GetMethod("Frame",Flags).Invoke(Transport,new object[]{bytes,bytes.Length});
   if(delay==0)receive();else{var t=new Thread(()=>{Thread.Sleep(delay);receive();}){IsBackground=true};var done=new TaskCompletionSource<object>();var old=receive;receive=()=>{try{old();done.SetResult(null);}catch(Exception e){done.SetException(e);}};Jobs.Add(done.Task);t.Start();}
  }
  public override IAsyncResult BeginWrite(byte[] buffer,int offset,int count,AsyncCallback callback,object state){
   NulFrames&=count>0&&buffer[offset+count-1]==0;
   var packet=Json.Map(Json.Serializer().DeserializeObject(Encoding.UTF8.GetString(buffer,offset,count).TrimEnd('\0')));
   string method=Json.Text(packet,"method");Methods.Add(method);if(method.StartsWith("Target."))BrowserCommandsNoSession&=!packet.ContainsKey("sessionId");
   if(method=="Target.attachToTarget")AttachFlatten&=Json.Bool(Json.Map(Json.Get(packet,"params")),"flatten");
   int number=++Calls;var completion=new TaskCompletionSource<object>(state);if(StallWrite)return completion.Task;
   if(Script!=null)Script(this,packet,number);completion.SetResult(null);if(callback!=null)callback(completion.Task);return completion.Task;
  }
  public override void EndWrite(IAsyncResult value){((Task)value).GetAwaiter().GetResult();}
  internal void Join(){Task.WaitAll(Jobs.ToArray());}
 }
 sealed class Fixture:IDisposable {
  internal Peer Peer;internal CdpTransport Transport;internal HostState State;internal WallpaperSession Session;
  internal Fixture(string root){
   Peer=new Peer(Path.Combine(root,Guid.NewGuid().ToString("N")+".fixture"));
   var owned=typeof(OwnedClient).GetConstructor(Flags,null,new[]{typeof(FileStream),typeof(FileStream),typeof(string)},null).Invoke(new object[]{Peer,Peer,Path.Combine(root,"never-launched.exe")});
   Transport=(CdpTransport)FormatterServices.GetUninitializedObject(typeof(CdpTransport));Set(Transport,"client",owned);Set(Transport,"gate",new object());Set(Transport,"writeGate",new object());
   var field=typeof(CdpTransport).GetField("pending",Flags);field.SetValue(Transport,Activator.CreateInstance(field.FieldType));Peer.Transport=Transport;
   State=new HostState();Session=new WallpaperSession(Transport,State,null,true);
  }
  internal HostFailure Select(){try{typeof(WallpaperSession).GetMethod("SelectTarget",Flags).Invoke(Session,null);return null;}catch(TargetInvocationException e){var failure=e.InnerException as HostFailure;if(failure==null)throw;return failure;}}
  public void Dispose(){Peer.Join();Peer.Dispose();State.Work.Dispose();State.TerminalRead.Dispose();}
 }
 static object Target(int number){return Json.Obj("targetInfos",new object[]{Json.Obj("targetId","target_"+number,"type","page","url","app://-/index.html")});}
 static void Record(string name,Fixture f,Stopwatch timer,HostFailure failure,string detail){
  Check(f.Peer.NulFrames&&f.Peer.BrowserCommandsNoSession&&f.Peer.AttachFlatten,"invalid outbound framing or browser command/session");
  Check(((IDictionary)Get(f.Transport,"pending")).Count==0,"pending requests leaked");
  records.Add(Json.Obj("case",name,"passed",true,"elapsedMs",timer.ElapsedMilliseconds,"code",failure==null?0:failure.Code,"faulted",f.Transport.Faulted,"requests",f.Peer.Calls,"methods",f.Peer.Methods,"detail",detail));
 }
 static void Baseline(string root,string name,Action<Peer,Dictionary<string,object>,int> script,int expected,long lower,long upper){using(var f=new Fixture(root)){
  f.Peer.Script=script;var timer=Stopwatch.StartNew();var failure=f.Select();Check((failure==null?0:failure.Code)==expected,"wrong code in "+name);Check(timer.ElapsedMilliseconds>=lower&&timer.ElapsedMilliseconds<upper,"timing mismatch in "+name);
  if(expected==37)Check(!f.Transport.Faulted,"reply timeout unexpectedly terminal");Record(name,f,timer,failure,"unmodified SelectTarget");
 }}
 static void CandidateCase(string root,string name,Action<Peer,Dictionary<string,object>,int> script,int expected,int calls,long lower,long upper){using(var f=new Fixture(root)){
  f.Peer.Script=script;var timer=Stopwatch.StartNew();var failure=f.Select();Check((failure==null?0:failure.Code)==expected,"wrong code in "+name);Check(f.Peer.Calls==calls,"wrong call count in "+name);Check(timer.ElapsedMilliseconds>=lower&&timer.ElapsedMilliseconds<upper,"timing mismatch in "+name);
  Record(name,f,timer,failure,"actual candidate SelectTarget");
 }}
 static void EmitFrame(Peer p,Dictionary<string,object> packet){var bytes=Encoding.UTF8.GetBytes(Json.Serializer().Serialize(packet));typeof(CdpTransport).GetMethod("Frame",Flags).Invoke(p.Transport,new object[]{bytes,bytes.Length});}
 public static int Main(string[] args){try{
  string root=args[0];Directory.CreateDirectory(root);
  CandidateCase(root,"normal-browser-first-request",(p,r,n)=>p.Reply(r,n==1?Target(1):Json.Obj("sessionId","session_1"),0),0,2,0,1000);
  CandidateCase(root,"first-discovery-reply-2400ms",(p,r,n)=>p.Reply(r,Json.Text(r,"method")=="Target.getTargets"?Target(n):Json.Obj("sessionId","session_2"),n==1?2400:n==2?700:0),0,3,2800,4200);
  CandidateCase(root,"three-empty-discoveries-then-timeout",(p,r,n)=>{if(n<=3)p.Reply(r,Json.Obj("targetInfos",new object[0]),0);else if(n>=5)p.Reply(r,Json.Text(r,"method")=="Target.getTargets"?Target(n):Json.Obj("sessionId","session_5"),0);},0,6,3600,5000);
  CandidateCase(root,"discovery-1500ms-then-single-attach-2400ms",(p,r,n)=>p.Reply(r,n==1?Target(1):Json.Obj("sessionId","late_session"),n==1?1500:2400),0,2,3800,5000);
  CandidateCase(root,"explicit-protocol-error",(p,r,n)=>EmitFrame(p,Json.Obj("id",Json.Get(r,"id"),"error",Json.Obj("code",-32601,"message","fixture"))),32,1,0,1000);
  CandidateCase(root,"wrong-browser-session",(p,r,n)=>EmitFrame(p,Json.Obj("id",Json.Get(r,"id"),"sessionId","wrong_session","result",Target(1))),31,1,0,1000);
  CandidateCase(root,"ambiguous-targets",(p,r,n)=>p.Reply(r,Json.Obj("targetInfos",new object[]{Json.Obj("targetId","one","type","page","url","app://-/index.html"),Json.Obj("targetId","two","type","page","url","app://-/index.html")}),0),62,1,0,1000);
  CandidateCase(root,"terminal-read-error",(p,r,n)=>typeof(CdpTransport).GetMethod("Fail",Flags).Invoke(p.Transport,new object[]{35}),35,1,0,1000);
  for(int stage=1;stage<=3;stage++)using(var f=new Fixture(root)){
   f.Peer.StallWrite=stage==2;var held=new ManualResetEvent(false);var release=new ManualResetEvent(false);Thread owner=null;
   if(stage==1){owner=new Thread(()=>{lock(Get(f.Transport,"writeGate")){held.Set();release.WaitOne();}});owner.Start();held.WaitOne();}
   var timer=Stopwatch.StartNew();HostFailure failure=null;try{f.Transport.Send("Target.getTargets",Json.Obj(),null,60);}catch(HostFailure e){failure=e;}finally{release.Set();if(owner!=null)owner.Join();held.Dispose();release.Dispose();}
   Check(failure!=null&&failure.Code==37,"wrong timeout code");var field=typeof(HostFailure).GetField("TimeoutStage",Flags);Check(field!=null&&(int)field.GetValue(failure)==stage,"wrong timeout stage");Check(f.Transport.Faulted==(stage==2),"wrong terminal classification");Record("transport-stage-"+stage,f,timer,failure,"specific stage preserved");
  }
  using(var f=new Fixture(root)){
   f.Peer.StallWrite=true;var timer=Stopwatch.StartNew();var failure=f.Select();Check(failure!=null&&failure.Code==37&&f.Transport.Faulted&&f.Peer.Calls==1,"selection retried terminal write timeout");Check(timer.ElapsedMilliseconds<3000,"terminal write failure delayed");Record("selection-write-timeout-no-retry",f,timer,failure,"one request");
  }
  using(var f=new Fixture(root)){
   f.Peer.Script=(p,r,n)=>p.Reply(r,Target(n),n==1?2400:700);var timer=Stopwatch.StartNew();HostFailure failure=null;try{f.Transport.Send("Target.getTargets",Json.Obj(),null,2000);}catch(HostFailure e){failure=e;}
   Check(failure!=null&&failure.Code==37&&!f.Transport.Faulted,"first delayed reply classification");var response=f.Transport.Send("Target.getTargets",Json.Obj(),null,2000);var values=(object[])Json.Get(response,"targetInfos");Check(Json.Text(Json.Map(values[0]),"targetId")=="target_2","late id accepted for new request");Check(timer.ElapsedMilliseconds>=2600,"late reply shortcut");Check(f.Transport.FramesReceived==2&&f.Transport.MatchedResponses==1&&f.Transport.LateResponses==1,"incorrect reply counters");Record("late-old-id-during-new-discovery",f,timer,null,"only matching fresh ID accepted");
  }
  using(var f=new Fixture(root)){
   var timer=Stopwatch.StartNew();var failure=f.Select();Check(failure!=null,"permanent silence accepted");Check(timer.ElapsedMilliseconds>=11800&&timer.ElapsedMilliseconds<13500,"shared discovery budget exceeded");Check(f.Peer.Calls>=5&&f.Peer.Calls<=6&&!f.Peer.Methods.Contains("Target.attachToTarget"),"unbounded or mutating silent discovery");Record("permanent-discovery-silence",f,timer,failure,"bounded by phase budget");
  }
  using(var f=new Fixture(root)){
   f.Peer.Script=(p,r,n)=>{if(n==1)p.Reply(r,Target(1),1500);};var timer=Stopwatch.StartNew();var failure=f.Select();Check(failure!=null&&failure.Code==37&&f.Peer.Calls==2,"attach replayed or not failed");Check(timer.ElapsedMilliseconds>=11800&&timer.ElapsedMilliseconds<13500,"attach did not share original phase budget");Record("permanent-attach-silence",f,timer,failure,"one attach; no budget reset");
  }
  using(var f=new Fixture(root)){
   var changed=new Thread(()=>{Thread.Sleep(100);f.State.Command("disable");});f.Peer.Script=(p,r,n)=>{if(n==1){changed.Start();p.Reply(r,Target(1),500);}else p.Reply(r,Json.Obj("sessionId","unexpected"),0);};var timer=Stopwatch.StartNew();var failure=f.Select();changed.Join();Check(f.State.Disabled&&f.Peer.Calls==1,"disabled discovery attached target");Check(timer.ElapsedMilliseconds<1200,"disabled discovery delayed");Record("disable-before-target-reply",f,timer,failure,"no attach after disable");
  }
  using(var f=new Fixture(root)){
   var changed=new Thread(()=>{Thread.Sleep(2100);f.State.Command("disable");});f.Peer.Script=(p,r,n)=>{if(n==1)changed.Start();};var timer=Stopwatch.StartNew();var failure=f.Select();changed.Join();Check(f.State.Disabled&&f.Peer.Calls==1,"disable in backoff permitted another request");Check(timer.ElapsedMilliseconds<3000,"disable did not stop backoff");Record("disable-during-discovery-backoff",f,timer,failure,"no retry after disable");
  }
  using(var f=new Fixture(root)){
   var changed=new Thread(()=>{Thread.Sleep(100);f.State.Command("disable");});f.Peer.Script=(p,r,n)=>{if(n==1)p.Reply(r,Target(1),0);else if(n==2){changed.Start();p.Reply(r,Json.Obj("sessionId","after_disable"),500);}else throw new Exception("command after disabled attach");};
   var timer=Stopwatch.StartNew();HostFailure failure=null;try{typeof(WallpaperSession).GetMethod("Prepare",Flags).Invoke(f.Session,null);}catch(TargetInvocationException e){failure=e.InnerException as HostFailure;if(failure==null)throw;}
   changed.Join();Check(f.State.Disabled&&f.Peer.Calls==2&&(failure==null||failure.Code==63),"preparation continued after disabled attach");Check(timer.ElapsedMilliseconds<1200,"disabled preparation delayed after attach reply");Record("disable-during-attach-prevents-renderer-work",f,timer,failure,"one attach; no Runtime/DOM command follows disable");
  }
  for(int kind=1;kind<=2;kind++)for(int stage=0;stage<=3;stage++){
   string line=PreparationDiagnostics.FormatTargetRequest(kind,2001,2000,stage==0?0:137,stage,4096,3,1,2);
   Check(line.Length<1024&&line.Contains("phase=36")&&line.Contains("commandKind="+kind)&&line.Contains("requestBudgetMs=2000")&&line.Contains("timeoutStage="+stage)&&line.Contains("bytesReceived=4096")&&line.Contains("framesReceived=3")&&line.Contains("matchedResponses=1")&&line.Contains("lateResponses=2"),"unsafe or incomplete diagnostic shape");
   Check(!line.Contains(root)&&!line.Contains("targetId")&&!line.Contains("sessionId")&&!line.Contains("app://")&&!line.Contains("fixture"),"diagnostic leaked fixture data");
  }
  records.Add(Json.Obj("case","numeric-diagnostics-format","passed",true,"variants",8,"detail","numeric command/stage/budget/counters; no payload or identity"));  Console.WriteLine(Json.Serializer().Serialize(Json.Obj("passed",true,"productStarted",false,"sourceModified",false,"cases",records)));return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
}
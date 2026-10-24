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
// Runs real Prepare/Run/Send/Frame against a synthetic peer only.
// No client process, network connection, native pipe or application is started.
public static class StartupHandoffTests {
 const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
 static readonly List<object> Results=new List<object>();
 static void Set(object o,string k,object v){o.GetType().GetField(k,Private).SetValue(o,v);}
 static object Get(object o,string k){return o.GetType().GetField(k,Private).GetValue(o);}
 static void Check(bool value,string message){if(!value)throw new Exception(message);}
 sealed class Request {
  internal Dictionary<string,object> Packet;internal string Method,Kind,Expression;internal int Phase,Number;internal bool Stall;
 }
 sealed class Peer:FileStream {
  internal Fixture Owner;internal Func<Request,bool> Intercept;internal readonly Dictionary<string,int> Counts=new Dictionary<string,int>();
  internal readonly List<Thread> Jobs=new List<Thread>(); internal string LastOperationExpression;internal Stopwatch ShellClock,LayoutClock,MediaClock; internal string AcceptedOperation=""; internal bool Committed;
  internal Peer(string path):base(path,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.ReadWrite,1,FileOptions.Asynchronous){}
  internal int Count(string kind){int count;return Counts.TryGetValue(kind,out count)?count:0;}
  internal void Later(int delay,Action action){var t=new Thread(()=>{Thread.Sleep(delay);action();}){IsBackground=true};Jobs.Add(t);t.Start();}
  internal void Reply(Request request,object result,int delay=0,string wrongSession=null,bool error=false){
   var packet=Json.Obj("id",Json.Get(request.Packet,"id"),error?"error":"result",result);
   if(wrongSession!=null)packet["sessionId"]=wrongSession;else if(request.Packet.ContainsKey("sessionId"))packet["sessionId"]=Json.Get(request.Packet,"sessionId");
   byte[] bytes=Encoding.UTF8.GetBytes(Json.Serializer().Serialize(packet));Action frame=()=>typeof(CdpTransport).GetMethod("Frame",Private).Invoke(Owner.Transport,new object[]{bytes,bytes.Length});
   if(delay==0)frame();else Later(delay,frame);
  }
  internal void Value(Request request,object value,int delay=0,string wrongSession=null){Reply(request,Json.Obj("result",Json.Obj("type","object","value",value)),delay,wrongSession);}
  internal Dictionary<string,object> Status(bool media){return Json.Obj(
   "attached",media,"mediaReady",media,"prepared",media,"disposed",false,"playing",Committed,"wallpaperApplied",Committed,"revealed",Committed,"layoutSupported",true,
   "manuallyPaused",false,"probeOnly",false,"firstFrameConfirmed",false,"documentHidden",false,"stageVisible",false,"memoryPressure",false,"code",Committed?"playing":media?"prepared":"preparing","mediaTimeMs",0,
   "documentGeneration",Owner.State.DocumentGeneration,"protocolVersion",2,"leaseNeedsRenew",false,"mediaGeneration",media?1:0,"actualPhase",Committed?"visible":media?"prepared":"preparing","health","fresh",
   "prepareOperationId",AcceptedOperation,"commitAccepted",Committed,"recoveryNeeded",false,"recoveryStable",false,"recoveryReason","","rendererClockMs",1000,
   "layoutWindowElectron",true,"layoutMainCount",1,"layoutActiveMainCount",1);}
  internal void Default(Request r){
   switch(r.Kind){
    case "discover":Reply(r,Json.Obj("targetInfos",new object[]{Json.Obj("targetId","fixture_target","type","page","url","app://-/index.html")}));break;
    case "target-attach":Reply(r,Json.Obj("sessionId","fixture_session"));break;
    case "root":Value(r,Json.Obj("rootReady",true,"bodyReady",true));break;
    case "preflight":Value(r,Json.Obj("namespaceFree",true,"ownNodeCount",0));break;
    case "install":Value(r,Json.Obj("ok",true,"code","installed"));break;
    case "binding":case "files":Reply(r,Json.Obj());break;
    case "configure":case "media-attach":case "renew":case "pause":Value(r,Json.Obj("ok",true));break;
    case "style":Value(r,Json.Obj("styleAccepted",true));break;
    case "layout":Value(r,Status(false));break;
    case "document":Reply(r,Json.Obj("root",Json.Obj("nodeId",1)));break;
    case "query":Reply(r,Json.Obj("nodeId",2));break;
    case "describe":Reply(r,Json.Obj("node",Json.Obj("backendNodeId",3,"nodeName","INPUT","attributes",new object[]{"id","codex-dragon-file-v1","type","file"})));break;
    case "input":Value(r,Json.Obj("inputReady",true,"multiple",false));break;
    case "media-status":case "runtime-status":Value(r,Status(true));break; case "operation":LastOperationExpression=r.Expression;Check(AcceptedOperation==Owner.Operation,"commit has no prior preparation");Committed=r.Expression.Contains(".commit(");Value(r,Json.Obj("ok",true));break; case "dispose":Value(r,Json.Obj("ok",true));break; case "cleanup":Value(r,Json.Obj("apiPresent",false,"ownNodeCount",0,"markerPresent",false));break;
    default:throw new Exception("unexpected request kind "+r.Kind);
   }
  }
  public override IAsyncResult BeginWrite(byte[] buffer,int offset,int count,AsyncCallback callback,object state){
   Check(count>0&&buffer[offset+count-1]==0,"missing NUL frame");var packet=Json.Map(Json.Serializer().DeserializeObject(Encoding.UTF8.GetString(buffer,offset,count-1)));
   var r=new Request{Packet=packet,Method=Json.Text(packet,"method"),Phase=(int)Get(Owner.Session,"phase"),Expression=Json.Text(Json.Map(Json.Get(packet,"params")),"expression")??""};
   if(r.Method=="Target.getTargets")r.Kind="discover";else if(r.Method=="Target.attachToTarget")r.Kind="target-attach";else if(r.Method=="Runtime.addBinding")r.Kind="binding";
   else if(r.Method=="DOM.getDocument")r.Kind="document";else if(r.Method=="DOM.querySelector")r.Kind="query";else if(r.Method=="DOM.describeNode")r.Kind="describe";else if(r.Method=="DOM.setFileInputFiles")r.Kind="files";
   else if(r.Expression.Contains(".prepare(")||r.Expression.Contains(".commit("))r.Kind="operation";else if(r.Expression.Contains(".renewLease("))r.Kind="renew";else if(r.Expression.Contains(".pause()"))r.Kind="pause";else if(r.Phase==12)r.Kind="runtime-status";else if(r.Phase==13)r.Kind=r.Expression.Contains("apiPresent:")?"cleanup":"dispose";else if(r.Phase==2)r.Kind="root";else if(r.Phase==3)r.Kind="preflight";else if(r.Phase==4)r.Kind=r.Expression.Contains(".configure(")?"configure":"install";
   else if(r.Phase==5)r.Kind="style";else if(r.Phase==6)r.Kind="layout";else if(r.Phase==8)r.Kind="input";
   else if(r.Phase==10)r.Kind=r.Expression.Contains(".attach(")?"media-attach":r.Expression.Contains(".renewLease(")?"renew":"media-status";
   else throw new Exception("unexpected phase "+r.Phase);
   Counts[r.Kind]=Count(r.Kind)+1;r.Number=Count(r.Kind);
   if(r.Kind=="root"&&ShellClock==null)ShellClock=Stopwatch.StartNew();if(r.Kind=="layout"&&LayoutClock==null)LayoutClock=Stopwatch.StartNew();if(r.Kind=="media-attach"&&MediaClock==null)MediaClock=Stopwatch.StartNew();
   if(Intercept==null||!Intercept(r))Default(r);
   var completion=new TaskCompletionSource<object>(state);if(r.Stall)return completion.Task;completion.SetResult(null);if(callback!=null)callback(completion.Task);return completion.Task;
  }
  public override void EndWrite(IAsyncResult result){((Task)result).GetAwaiter().GetResult();}
  internal void Join(){for(int i=0;i<Jobs.Count;i++)Jobs[i].Join();}
 }
 sealed class Fixture:IDisposable {
  internal readonly Peer Peer;internal readonly CdpTransport Transport;internal readonly HostState State;internal readonly WallpaperSession Session;internal string Operation=Guid.NewGuid().ToString("N"); internal string OperationReply; internal Exception RunError;
  internal Fixture(string root){
   Peer=new Peer(Path.Combine(root,Guid.NewGuid().ToString("N")+".fixture"));Peer.Owner=this;Peer.AcceptedOperation=Operation;
   var owned=typeof(OwnedClient).GetConstructor(Private,null,new[]{typeof(FileStream),typeof(FileStream),typeof(string)},null).Invoke(new object[]{Peer,Peer,Path.Combine(root,"never-launched.exe")});
   Transport=(CdpTransport)FormatterServices.GetUninitializedObject(typeof(CdpTransport));Set(Transport,"client",owned);Set(Transport,"gate",new object());Set(Transport,"writeGate",new object());
   var field=typeof(CdpTransport).GetField("pending",Private);field.SetValue(Transport,Activator.CreateInstance(field.FieldType));
   State=new HostState();Session=new WallpaperSession(Transport,State,new Configuration{Enabled=true,Media="fixture.mp4",Renderer="({ok:true,code:'installed'})"},false);
  }
  internal void Enqueue(string command,int timeout=1000){long expected=State.OperationRequested+1;Peer.Later(0,()=>OperationReply=State.OperationSnapshot(command,Operation,timeout));var wait=Stopwatch.StartNew();while(State.OperationRequested<expected&&wait.ElapsedMilliseconds<500)Thread.Sleep(1);Check(State.OperationRequested==expected,"operation not enqueued");} internal void Run(){bool timedOut=false;using(var bound=new Timer(_=>{timedOut=true;State.Command("disable");},null,6000,Timeout.Infinite)){try{Session.Run();}catch(Exception e){RunError=e;}}Check(!timedOut,"fixture exceeded 6 s bound");} internal HostFailure Prepare(){try{typeof(WallpaperSession).GetMethod("Prepare",Private).Invoke(Session,null);return null;}catch(TargetInvocationException e){var failure=e.InnerException as HostFailure;if(failure==null)throw;return failure;}}
  public void Dispose(){Peer.Join();Peer.Dispose();State.Work.Dispose();State.TerminalRead.Dispose();}
 }
 static void StopAfterOperation(Fixture f){f.Peer.Later(0,()=>{var wait=Stopwatch.StartNew();while(f.State.OperationCompleted<1&&wait.ElapsedMilliseconds<1500)Thread.Sleep(1);f.State.Command("disable");});}
 static void WaitFor(Func<bool> ready){var wait=Stopwatch.StartNew();while(!ready()&&wait.ElapsedMilliseconds<2500)Thread.Sleep(1);Check(ready(),"fixture rendezvous timeout");}
 static void FinaleRead(string root,bool forceRedundantPrepare){using(var f=new Fixture(root)){
  // These two launcher classes are extracted from the same candidate at build
  // time. The separate named-pipe suite tests BridgeSession's command routing.
  var lease=new DragonLauncher.StartupPreparation();var owner=new object();var total=Stopwatch.StartNew();var first=lease.Begin(owner,0);
  f.Operation=lease.OperationId;f.Peer.AcceptedOperation=f.Operation;f.State.Started(1,1);
  bool committed=false,observeOnly=false;long finaleAt=-1;
  f.Peer.Intercept=r=>{
   if(r.Kind=="runtime-status"&&r.Number==1)f.Enqueue("prepare");
   if(r.Kind=="runtime-status"&&r.Number==2)f.Peer.Later(0,()=>{
    WaitFor(()=>f.State.OperationCompleted>=1);
    var s=new DragonLauncher.BridgeStatus{Version=1,ProtocolVersion=2,DocumentGeneration=f.State.DocumentGeneration,MediaGeneration=f.State.MediaGeneration,Health=f.State.Health,ObservationAgeMs=0,Sequence=f.State.Sequence,PrepareOperationId=f.State.PrepareOperationId,Prepared=f.State.Prepared,CommitAccepted=f.State.CommitAccepted,Disposition=2,Ready=f.State.Ready,Disabled=f.State.Disabled,MediaCode=f.State.MediaCode,RefreshCode=1,DiagnosticsVersion=1,LayoutSupported=f.State.LayoutSupported,ProbeOnly=f.State.ProbeOnly,Attached=f.State.Attached,HostPid=f.State.HostPid,HostCreated=f.State.HostCreated,CodexPid=1,CodexCreated=1,ActualPhase=f.State.ActualPhase};
    Check(lease.Accept(first,owner,s,total.Elapsed.TotalSeconds)&&lease.Commit(owner,total.Elapsed.TotalSeconds),"healthy prepared sample did not commit finale");
    committed=true;finaleAt=total.ElapsedMilliseconds;var next=lease.Begin(owner,total.Elapsed.TotalSeconds);
    var decision=typeof(DragonLauncher.StartupPreparation.Request).GetField("ObserveOnly",Private);observeOnly=decision!=null&&(bool)decision.GetValue(next);
    Check(observeOnly,"committed finale must use observation-only requests");Thread.Sleep(400);
    if(forceRedundantPrepare){f.Enqueue("prepare");WaitFor(()=>f.State.OperationCompleted>=2);}
    else if(observeOnly){f.State.RefreshSnapshot(1000);WaitFor(()=>f.State.RefreshCompleted>=1);}
    else throw new Exception("actual preparation decision selected a repeated mutation");
    f.State.Command("disable");
   });
   if(forceRedundantPrepare&&r.Kind=="operation"&&r.Number==2){f.Peer.Value(r,Json.Obj("ok",true),900);return true;}
   if(!forceRedundantPrepare&&r.Kind=="runtime-status"&&r.Number==3){f.Peer.Value(r,f.Peer.Status(true),900);return true;}
   if(!forceRedundantPrepare&&r.Kind=="runtime-status"&&r.Number==4){f.Peer.Value(r,f.Peer.Status(true),200);return true;}
   return false;
  };
  f.Run();Check(committed&&observeOnly&&f.RunError==null,"delayed finale observation failed: "+f.RunError);
  Check(f.Peer.Count("operation")== (forceRedundantPrepare?2:1),"unexpected prepare mutation or replay");
  Check(!f.State.RevealEverRequested,"local finale commitment implied renderer reveal");
  if(forceRedundantPrepare)Check((int)Get(f.State,"operationResult")==1,"late redundant prepare could not be confirmed");
  else Check(f.State.RefreshCompleted==1,"finale refresh did not complete");
  Results.Add(Json.Obj("case",forceRedundantPrepare?"in-flight-redundant-prepare-after-finale-is-observed":"actual-finale-decision-uses-refresh-and-survives-delay","passed",true,"finaleCommitted",committed,"observeOnly",observeOnly,"elapsedSinceFinaleMs",total.ElapsedMilliseconds-finaleAt,"operationResult",Get(f.State,"operationResult"),"counts",f.Peer.Counts,"lateResponses",f.Transport.LateResponses));
 }}
 static void Exercise(string root,string name,bool slow,bool expectFixed,string command="commit"){using(var f=new Fixture(root)){
  f.Peer.Intercept=r=>{if(r.Kind=="runtime-status"&&r.Number==1)f.Enqueue(command);
   if(r.Kind=="runtime-status"&&r.Number==2)StopAfterOperation(f);
   if(slow&&r.Kind=="renew"&&r.Number==2){f.Peer.Value(r,Json.Obj("ok",true),850);return true;}
   if(slow&&r.Kind=="renew"&&r.Number==3){f.Peer.Value(r,Json.Obj("ok",true),200);return true;}return false;};
  var watch=Stopwatch.StartNew();f.Run();int result=(int)Get(f.State,"operationResult");
  Check(f.RunError==null,"Run failed: "+f.RunError);Check(f.Peer.Count("operation")==((!slow||expectFixed)?1:0),"unexpected operation count");
  Check(result==((!slow||expectFixed)?1:2),"wrong operation result");Check(f.Peer.Count("install")==1&&f.Peer.Count("media-attach")==1,"replayed startup mutation");
  Results.Add(Json.Obj("case",name,"passed",true,"elapsedMs",watch.ElapsedMilliseconds,"operationResult",result,"refreshCode",Json.Number(Json.Map(Json.Serializer().DeserializeObject(f.OperationReply)),"refreshCode"),"counts",f.Peer.Counts,"lateResponses",f.Transport.LateResponses));
 }}
 static void Fault(string root,string name,Action<Fixture,Request> action,int code,int stage=0,string command="commit"){using(var f=new Fixture(root)){
  f.Peer.Intercept=r=>{if(r.Kind=="runtime-status"&&r.Number==1)f.Enqueue(command);
   if(r.Kind=="operation"){action(f,r);return true;}return false;};
  var watch=Stopwatch.StartNew();f.Run();var error=f.RunError as HostFailure;
  Check(error!=null&&error.Code==code&&(stage==0||error.TimeoutStage==stage),"wrong failure "+name+": "+f.RunError);
  Check(f.Peer.Count("operation")==1&&!f.State.RevealEverRequested,"operation replay or accepted invalid proof");
  Check(watch.ElapsedMilliseconds<2200,"unbounded operation");
  Results.Add(Json.Obj("case",name,"passed",true,"elapsedMs",watch.ElapsedMilliseconds,"code",error.Code,"timeoutStage",error.TimeoutStage,"counts",f.Peer.Counts));
 }}
 static void PrepareReplyTimeout(string root,string name,string stateKind,int statusDelay,int expectedResult,bool statusSilence=false){using(var f=new Fixture(root)){
  f.Peer.Intercept=r=>{
   if(r.Kind=="runtime-status"&&r.Number==1)f.Enqueue("prepare");
   if(r.Kind=="operation"){f.Peer.Value(r,Json.Obj("ok",true),900);return true;}
   if(r.Kind=="runtime-status"&&r.Number>=2){
    if(statusSilence)return true;
    var value=f.Peer.Status(true);
    if(stateKind=="other-operation")value["prepareOperationId"]=Guid.NewGuid().ToString("N");
    if(stateKind=="new-media")value["mediaGeneration"]=2;
    if(stateKind=="not-ready"){value["mediaReady"]=value["prepared"]=false;value["actualPhase"]="preparing";value["code"]="loading";}
    f.Peer.Value(r,value,statusDelay);StopAfterOperation(f);return true;
   }return false;
  };
  var watch=Stopwatch.StartNew();f.Run();var error=f.RunError as HostFailure;
  if(statusSilence)Check(error!=null&&error.Code==37&&error.TimeoutStage==3,"permanent status silence was accepted");
  else Check(f.RunError==null&&(int)Get(f.State,"operationResult")==expectedResult,"wrong prepare-timeout result "+name+": "+f.RunError);
  Check(f.Peer.Count("operation")==1&&!f.State.RevealEverRequested,"prepare replayed or implied reveal");
  Check(watch.ElapsedMilliseconds<4400,"prepare timeout reset a deadline");
  Results.Add(Json.Obj("case",name,"passed",true,"elapsedMs",watch.ElapsedMilliseconds,"operationResult",Get(f.State,"operationResult"),"code",error==null?0:error.Code,"counts",f.Peer.Counts));
 }}
 static void Context(string root,string name,Action<Fixture> change,int code){using(var f=new Fixture(root)){
  f.Peer.Intercept=r=>{if(r.Kind=="runtime-status"&&r.Number==1)f.Enqueue("commit");
   if(r.Kind=="runtime-status"&&r.Number==2)StopAfterOperation(f);
   if(r.Kind=="operation"){f.Peer.Later(50,()=>change(f));f.Peer.Value(r,Json.Obj("ok",true),250);return true;}return false;};
  var watch=Stopwatch.StartNew();f.Run();var error=f.RunError as HostFailure;
  Check(code==0?f.RunError==null:error!=null&&error.Code==code,"wrong context result "+name+": "+f.RunError);
  Check(f.Peer.Count("operation")==1&&!f.State.RevealEverRequested,"context change accepted or replayed operation");
  Results.Add(Json.Obj("case",name,"passed",true,"elapsedMs",watch.ElapsedMilliseconds,"code",error==null?0:error.Code,"counts",f.Peer.Counts));
 }}
 static void Rejected(string root,string name,bool expired){using(var f=new Fixture(root)){
  f.Peer.Intercept=r=>{if(r.Kind=="runtime-status"&&r.Number==1){f.Enqueue("commit");if(expired)f.State.OperationStartedAt-=2*Stopwatch.Frequency;}
   if(r.Kind=="runtime-status"&&r.Number==2)StopAfterOperation(f);
   if(r.Kind=="operation"){f.Peer.Value(r,Json.Obj("ok",false));return true;}return false;};
  f.Run();Check(f.RunError==null&&(int)Get(f.State,"operationResult")==2&&!f.State.RevealEverRequested,"rejection accepted");
  Check(f.Peer.Count("operation")== (expired?0:1),"rejected operation replayed");
  Results.Add(Json.Obj("case",name,"passed",true,"counts",f.Peer.Counts));
 }}
 public static int Main(string[] args){try{string root=args[0];Directory.CreateDirectory(root);bool fix=args.Length>1&&args[1]=="fixed";
 Exercise(root,"normal-commit",false,fix);Exercise(root,"slow-redundant-renewal-before-commit",true,fix);
 Exercise(root,"normal-prepare",false,fix,"prepare");Exercise(root,"slow-redundant-renewal-before-prepare",true,fix,"prepare");
 if(fix){
  FinaleRead(root,false);FinaleRead(root,true);
  Fault(root,"operation-response-timeout-no-replay",(f,r)=>{},37,3);
  Fault(root,"late-operation-reply-no-replay",(f,r)=>f.Peer.Value(r,Json.Obj("ok",true),900),37,3);
  Fault(root,"operation-write-fault-no-replay",(f,r)=>r.Stall=true,37,2);
  Fault(root,"operation-protocol-rejection",(f,r)=>f.Peer.Reply(r,Json.Obj("code",-32601),0,null,true),32);
  Fault(root,"operation-wrong-session",(f,r)=>f.Peer.Value(r,Json.Obj("ok",true),0,"wrong_session"),31);
  Fault(root,"operation-guard-rejection",(f,r)=>f.Peer.Value(r,Json.Obj("targetAccepted",false,"guardMask",0)),64072);
  Fault(root,"operation-eof",(f,r)=>typeof(CdpTransport).GetMethod("Fail",Private).Invoke(f.Transport,new object[]{35}),35);
  Context(root,"disable-in-flight",f=>f.State.Command("disable"),0);
  Context(root,"pause-in-flight",f=>{f.State.Command("pause");f.Peer.Later(400,()=>f.State.Command("disable"));},0);
  Context(root,"session-change-in-flight",f=>Set(f.Session,"session","changed_session"),72);
  Context(root,"generation-change-in-flight",f=>Set(f.State,"DocumentGeneration",Guid.NewGuid().ToString("N")),72);
  Context(root,"target-lost-in-flight",f=>Set(f.Session,"targetLost",true),72);
  Context(root,"transport-lost-in-flight",f=>Set(f.Session,"transportLost",true),72);
  Context(root,"operation-replaced-in-flight",f=>{lock(f.State.Gate){f.State.OperationRequested++;}f.Peer.Later(400,()=>f.State.Command("disable"));},0);
  Rejected(root,"expired-before-send",true);Rejected(root,"lease-or-operation-rejected",false);
  PrepareReplyTimeout(root,"prepare-timeout-fresh-same-operation-confirms",null,0,1);
  PrepareReplyTimeout(root,"prepare-timeout-other-operation-remains-unknown","other-operation",0,3);
  PrepareReplyTimeout(root,"prepare-timeout-new-media-remains-unknown","new-media",0,3);
  PrepareReplyTimeout(root,"prepare-timeout-not-ready-remains-unknown","not-ready",0,3);
  PrepareReplyTimeout(root,"prepare-timeout-late-proof-does-not-extend-deadline",null,350,3);
  PrepareReplyTimeout(root,"prepare-timeout-status-silence-still-fails",null,0,0,true);
  Fault(root,"prepare-write-fault-still-fails",(f,r)=>r.Stall=true,37,2,"prepare");
  Fault(root,"prepare-protocol-rejection-still-fails",(f,r)=>f.Peer.Reply(r,Json.Obj("code",-32601),0,null,true),32,0,"prepare");
  Fault(root,"prepare-wrong-session-still-fails",(f,r)=>f.Peer.Value(r,Json.Obj("ok",true),0,"wrong_session"),31,0,"prepare");
  Fault(root,"prepare-guard-rejection-still-fails",(f,r)=>f.Peer.Value(r,Json.Obj("targetAccepted",false,"guardMask",0)),64072,0,"prepare");
  Fault(root,"prepare-timeout-after-cancellation-is-not-recovered",(f,r)=>f.Peer.Later(50,()=>f.State.Command("disable")),37,3,"prepare");
  Fault(root,"prepare-timeout-after-generation-change-is-not-recovered",(f,r)=>f.Peer.Later(50,()=>Set(f.State,"DocumentGeneration",Guid.NewGuid().ToString("N"))),37,3,"prepare");
  for(int kind=1;kind<=4;kind++){
   string line=PreparationDiagnostics.FormatRuntimeRequest(kind,20,750,750,new HostFailure(37,3));
   Check(line.Length<1024&&line.Contains("phase=41")&&line.Contains("operationKind="+kind)&&line.Contains("timeoutStage=3")&&!line.Contains("fixture_"),"runtime failure diagnostic unsafe");
  }
  Results.Add(Json.Obj("case","runtime-request-failure-diagnostics","passed",true,"variants",4));
  var method=typeof(WallpaperSession).GetMethod("LeasedOperation",BindingFlags.NonPublic|BindingFlags.Static);
  Check(method!=null,"missing leased operation helper");
  string expression=(string)method.Invoke(null,new object[]{"commit","0123456789abcdef0123456789abcdef","abcdef0123456789abcdef0123456789",1750L});
  File.WriteAllText(Path.Combine(root,"leased-operation.js"),expression,Encoding.UTF8);
  string prepareExpression=(string)method.Invoke(null,new object[]{"prepare","0123456789abcdef0123456789abcdef","abcdef0123456789abcdef0123456789",1750L});
  File.WriteAllText(Path.Combine(root,"leased-prepare.js"),prepareExpression,Encoding.UTF8);
 }
 Console.WriteLine(Json.Serializer().Serialize(Json.Obj("passed",true,"productStarted",false,"candidateMode",fix,"cases",Results)));return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
}

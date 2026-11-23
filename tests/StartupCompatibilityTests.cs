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
// Runs real Prepare/ReadPreparation/Send/Frame against a synthetic peer only.
// No client process, network connection, native pipe or application is started.
public static class StartupCompatibilityTests {
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
  internal readonly List<Thread> Jobs=new List<Thread>();internal Stopwatch ShellClock,LayoutClock,MediaClock;
  internal Peer(string path):base(path,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.ReadWrite,1,FileOptions.Asynchronous){}
  internal int Count(string kind){int count;return Counts.TryGetValue(kind,out count)?count:0;}
  internal void Later(int delay,Action action){var t=new Thread(()=>{Thread.Sleep(delay);action();}){IsBackground=true};Jobs.Add(t);t.Start();}
  internal void Reply(Request request,object result,int delay=0,string wrongSession=null,bool error=false,bool omitSession=false){
   var packet=Json.Obj("id",Json.Get(request.Packet,"id"),error?"error":"result",result);
   if(!omitSession){if(wrongSession!=null)packet["sessionId"]=wrongSession;else if(request.Packet.ContainsKey("sessionId"))packet["sessionId"]=Json.Get(request.Packet,"sessionId");}
   byte[] bytes=Encoding.UTF8.GetBytes(Json.Serializer().Serialize(packet));Action frame=()=>typeof(CdpTransport).GetMethod("Frame",Private).Invoke(Owner.Transport,new object[]{bytes,bytes.Length});
   if(delay==0)frame();else Later(delay,frame);
  }
  internal void Value(Request request,object value,int delay=0,string wrongSession=null){Reply(request,Json.Obj("result",Json.Obj("type","object","value",value)),delay,wrongSession);}
  internal Dictionary<string,object> Status(bool media){return Json.Obj(
   "attached",media,"mediaReady",media,"prepared",media,"disposed",false,"playing",false,"wallpaperApplied",false,"revealed",false,"layoutSupported",true,
   "manuallyPaused",false,"probeOnly",false,"firstFrameConfirmed",false,"documentHidden",false,"stageVisible",false,"code",media?"prepared":"preparing","mediaTimeMs",0,
   "documentGeneration",Owner.State.DocumentGeneration,"protocolVersion",2,"leaseNeedsRenew",false,"mediaGeneration",media?1:0,"actualPhase",media?"prepared":"preparing","health","fresh",
   "prepareOperationId","","commitAccepted",false,"recoveryNeeded",false,"recoveryStable",false,"recoveryReason","","rendererClockMs",1000,
   "layoutWindowElectron",true,"layoutMainCount",1,"layoutActiveMainCount",1);}
  internal void Default(Request r){
   switch(r.Kind){
    case "discover":Reply(r,Json.Obj("targetInfos",new object[]{Json.Obj("targetId","fixture_target","type","page","url","app://-/index.html")}));break;
    case "target-attach":Reply(r,Json.Obj("sessionId","fixture_session"));break;
    case "root":Value(r,Json.Obj("rootReady",true,"bodyReady",true,"workspaceReady",true));break;
    case "preflight":Value(r,Json.Obj("namespaceFree",true,"ownNodeCount",0));break;
    case "install":Value(r,Json.Obj("ok",true,"code","installed"));break;
    case "binding":case "files":Reply(r,Json.Obj());break;
    case "configure":case "media-attach":case "renew":Value(r,Json.Obj("ok",true));break;
    case "style":Value(r,Json.Obj("styleAccepted",true));break;
    case "layout":Value(r,Status(false));break;
    case "document":Reply(r,Json.Obj("root",Json.Obj("nodeId",1)));break;
    case "query":Reply(r,Json.Obj("nodeId",2));break;
    case "describe":Reply(r,Json.Obj("node",Json.Obj("backendNodeId",3,"nodeName","INPUT","attributes",new object[]{"id","codex-dragon-file-v1","type","file"})));break;
    case "input":Value(r,Json.Obj("inputReady",true,"multiple",false));break;
    case "media-status":Value(r,Status(true));break;
    default:throw new Exception("unexpected request kind "+r.Kind);
   }
  }
  public override IAsyncResult BeginWrite(byte[] buffer,int offset,int count,AsyncCallback callback,object state){
   Check(count>0&&buffer[offset+count-1]==0,"missing NUL frame");var packet=Json.Map(Json.Serializer().DeserializeObject(Encoding.UTF8.GetString(buffer,offset,count-1)));
   var r=new Request{Packet=packet,Method=Json.Text(packet,"method"),Phase=(int)Get(Owner.Session,"phase"),Expression=Json.Text(Json.Map(Json.Get(packet,"params")),"expression")??""};
   if(r.Method=="Target.getTargets")r.Kind="discover";else if(r.Method=="Target.attachToTarget")r.Kind="target-attach";else if(r.Method=="Runtime.addBinding")r.Kind="binding";
   else if(r.Method=="DOM.getDocument")r.Kind="document";else if(r.Method=="DOM.querySelector")r.Kind="query";else if(r.Method=="DOM.describeNode")r.Kind="describe";else if(r.Method=="DOM.setFileInputFiles")r.Kind="files";
   else if(r.Phase==2)r.Kind="root";else if(r.Phase==3)r.Kind="preflight";else if(r.Phase==4)r.Kind=r.Expression.Contains(".configure(")?"configure":"install";
   else if(r.Phase==5)r.Kind="style";else if(r.Phase==6)r.Kind="layout";else if(r.Phase==8)r.Kind="input";
   else if(r.Phase==10)r.Kind=r.Expression.Contains(".attach(")?"media-attach":r.Expression.Contains(".renewLease(")?"renew":"media-status";
   else throw new Exception("unexpected phase "+r.Phase);
   Counts[r.Kind]=Count(r.Kind)+1;r.Number=Count(r.Kind);
   if(r.Kind=="root"&&ShellClock==null)ShellClock=Stopwatch.StartNew();if(r.Kind=="layout"&&LayoutClock==null)LayoutClock=Stopwatch.StartNew();if(r.Kind=="media-attach"&&MediaClock==null)MediaClock=Stopwatch.StartNew();
   if(Intercept==null||!Intercept(r))Default(r);
   var completion=new TaskCompletionSource<object>(state);if(r.Stall)return completion.Task;completion.SetResult(null);if(callback!=null)callback(completion.Task);return completion.Task;
  }
  public override void EndWrite(IAsyncResult result){((Task)result).GetAwaiter().GetResult();}
  internal void Join(){foreach(var t in Jobs)t.Join();}
 }
 sealed class Fixture:IDisposable {
  internal readonly Peer Peer;internal readonly CdpTransport Transport;internal readonly HostState State;internal readonly WallpaperSession Session;internal MemoryPressureMonitor Memory;internal volatile bool Pressure;
  internal Fixture(string root){
   Peer=new Peer(Path.Combine(root,Guid.NewGuid().ToString("N")+".fixture"));Peer.Owner=this;
   var owned=typeof(OwnedClient).GetConstructor(Private,null,new[]{typeof(FileStream),typeof(FileStream),typeof(string)},null).Invoke(new object[]{Peer,Peer,Path.Combine(root,"never-launched.exe")});
   Transport=(CdpTransport)FormatterServices.GetUninitializedObject(typeof(CdpTransport));Set(Transport,"client",owned);Set(Transport,"gate",new object());Set(Transport,"writeGate",new object());
   var field=typeof(CdpTransport).GetField("pending",Private);field.SetValue(Transport,Activator.CreateInstance(field.FieldType));
   State=new HostState();Session=new WallpaperSession(Transport,State,new Configuration{Enabled=true,Media="fixture.mp4",Renderer="({ok:true,code:'installed'})"},false);
  }
  internal void EnablePressure(){Memory=new MemoryPressureMonitor(State.Work,new ManualResetEvent(false),new ManualResetEvent(false),()=>Pressure,()=>!Pressure);Set(Session,"preparationMemory",Memory);}
  internal HostFailure Prepare(){try{typeof(WallpaperSession).GetMethod("Prepare",Private).Invoke(Session,null);return null;}catch(TargetInvocationException e){var failure=e.InnerException as HostFailure;if(failure==null)throw;return failure;}}
  public void Dispose(){Peer.Join();Peer.Dispose();if(Memory!=null)Memory.Dispose();State.Work.Dispose();State.TerminalRead.Dispose();}
 }
 static void NoReplay(Fixture f){foreach(string kind in new[]{"target-attach","install","binding","configure","files","media-attach"})Check(f.Peer.Count(kind)<=1,"mutation replayed: "+kind);}
 static void Success(Fixture f,HostFailure e,long ms){Check(e==null&&f.State.Ready&&f.State.Prepared&&(int)Get(f.Session,"phase")==11,"preparation did not succeed");Check(!f.State.Playing&&!f.State.Revealed&&!f.State.WallpaperApplied,"prepared state claimed playback");}
 static void Failure(Fixture f,HostFailure e,int phase,int code){Check(e!=null&&e.Code==code&&(int)Get(f.Session,"phase")==phase,"wrong failure boundary");Check(!f.State.Ready&&!f.State.Prepared&&!f.State.CommitAccepted,"failure published proof");}
 static void Case(string root,string name,Action<Fixture> configure,Action<Fixture,HostFailure,long> verify){using(var f=new Fixture(root)){
  if(configure!=null)configure(f);var clock=Stopwatch.StartNew();var error=f.Prepare();long elapsed=clock.ElapsedMilliseconds;verify(f,error,elapsed);NoReplay(f);Check(((IDictionary)Get(f.Transport,"pending")).Count==0,"pending request leak");
  Results.Add(Json.Obj("case",name,"passed",true,"phase",Get(f.Session,"phase"),"code",error==null?0:error.Code,"timeoutStage",error==null?0:error.TimeoutStage,"elapsedMs",elapsed,"shellElapsedMs",f.Peer.ShellClock==null?0:f.Peer.ShellClock.ElapsedMilliseconds,"layoutElapsedMs",f.Peer.LayoutClock==null?0:f.Peer.LayoutClock.ElapsedMilliseconds,"counts",f.Peer.Counts,"lateResponses",f.Transport.LateResponses));
 }}
 static void SlowRead(string root,string kind){Case(root,kind+"-delayed-response-recovers",f=>{
  f.Peer.Intercept=r=>{if(r.Kind!=kind)return false;if(r.Number==1){f.Peer.Value(r,kind=="root"?Json.Obj("rootReady",true,"bodyReady",true,"workspaceReady",true):f.Peer.Status(kind=="media-status"),1800);return true;}if(r.Number==2){f.Peer.Value(r,kind=="root"?Json.Obj("rootReady",true,"bodyReady",true,"workspaceReady",true):f.Peer.Status(kind=="media-status"),400);return true;}return false;};
 },(f,e,ms)=>{Success(f,e,ms);Check(f.Peer.Count(kind)==2&&f.Transport.LateResponses==1,"slow observation not retried or old response accepted");Check(ms>=1900&&ms<3200,"retry timing unexpected");});}
 static void Silence(string root,string kind,int phase,int budget){Case(root,kind+"-permanent-response-silence",f=>f.Peer.Intercept=r=>r.Kind==kind,(f,e,ms)=>{
  Failure(f,e,phase,37);Check(e.TimeoutStage==3&&!f.Transport.Faulted,"response silence classification changed");Check(ms>=budget-150&&ms<budget+1300,"observation reset total budget");Check(f.Peer.Count(kind)>=2&&f.Peer.Count(kind)<=9,"unbounded retries");});}
 static void MutationTimeout(string root,string kind,int phase,int timeout){Case(root,kind+"-timeout-is-not-replayed",f=>{
  f.Peer.Intercept=r=>{if(kind=="renew"&&r.Kind=="media-status"){var value=f.Peer.Status(false);value["attached"]=true;value["leaseNeedsRenew"]=true;f.Peer.Value(r,value);return true;}return r.Kind==kind;};
 },(f,e,ms)=>{Failure(f,e,phase,37);Check(e.TimeoutStage==3&&f.Peer.Count(kind)==1,"mutation timeout retried");Check(ms>=timeout-100&&ms<timeout+1000,"mutation timeout changed");});}
 static void Context(string root,string kind,Action<Fixture> change,int expected){Case(root,"layout-"+kind,f=>{
  if(kind=="pressure")f.EnablePressure();f.Peer.Intercept=r=>{if(r.Kind!="layout")return false;f.Peer.Later(100,()=>change(f));f.Peer.Value(r,f.Peer.Status(false),400);return true;};
 },(f,e,ms)=>{if(expected==0)Check(e==null&&f.State.Disabled&&!f.State.Ready,"disable did not return safely");else Failure(f,e,6,expected);Check(f.Peer.Count("layout")==1&&f.Peer.Count("document")==0,"invalid context continued");Check(ms<1300,"context rejection delayed");});}
 public static int Main(string[] args){try{
 string root=args[0];Directory.CreateDirectory(root);
 Case(root,"workspace-ready-after-17s",f=>f.Peer.Intercept=r=>{
  if(r.Kind!="root")return false;f.Peer.Value(r,Json.Obj("rootReady",true,"bodyReady",true,"workspaceReady",f.Peer.ShellClock.ElapsedMilliseconds>=17000));return true;
 },(f,e,ms)=>{Success(f,e,ms);Check(ms>=16800&&ms<20500,"slow workspace was not awaited");});
 foreach(bool persistent in new[]{false,true})Case(root,persistent?"persistent-root-script-error-stops-after-three-reads":"transient-root-script-error-recovers",f=>f.Peer.Intercept=r=>{
  if(r.Kind!="root"||(!persistent&&r.Number>1))return false;
  f.Peer.Reply(r,Json.Obj("result",Json.Obj("type","undefined"),"exceptionDetails",Json.Obj("text","fixture","exception",Json.Obj("className","TypeError","description","not logged"))));return true;
 },(f,e,ms)=>{if(persistent){Failure(f,e,2,60);Check(f.Peer.Count("root")==3&&f.Peer.Count("install")==0,"persistent script error was hidden");}else{Success(f,e,ms);Check(f.Peer.Count("root")==2,"transient read was not retried exactly once");}Check(ms<2000,"script retries unbounded");});
 Case(root,"syntax-error-is-not-retried",f=>f.Peer.Intercept=r=>{
  if(r.Kind!="root")return false;f.Peer.Reply(r,Json.Obj("result",Json.Obj("type","undefined"),"exceptionDetails",Json.Obj("exception",Json.Obj("className","SyntaxError"))));return true;
 },(f,e,ms)=>{Failure(f,e,2,60);Check(f.Peer.Count("root")==1&&f.Peer.Count("install")==0,"syntax error was retried");});
 using(var f=new Fixture(root)){
  f.Peer.Intercept=r=>{
   if(r.Kind=="root"&&r.Number<=2){f.Peer.Value(r,Json.Obj("rootReady",true,"bodyReady",true,"workspaceReady",false));typeof(WallpaperSession).GetMethod("OnSignal",Private).Invoke(f.Session,new object[]{"target-detached",(string)Get(f.Session,"session")});return true;}
   if(r.Kind=="discover"&&r.Number>=2){f.Peer.Reply(r,Json.Obj("targetInfos",new object[]{Json.Obj("targetId","fixture_target","type","page","url","app://-/index.html"),Json.Obj("targetId","workspace_2","type","page","url","app://-/index.html"),r.Number>=3?Json.Obj("targetId","workspace_3","type","page","url","app://-/index.html"):Json.Obj("targetId","ignored","type","worker","url","app://-/index.html")}));return true;}
   if(r.Kind=="target-attach"&&r.Number>=2){Check(Json.Text(Json.Map(Json.Get(r.Packet,"params")),"targetId")=="workspace_"+r.Number,"retired page was selected again");f.Peer.Reply(r,Json.Obj("sessionId","workspace_session_"+r.Number));return true;}
   return false;
  };
  var clock=Stopwatch.StartNew();var e=f.Prepare();Success(f,e,clock.ElapsedMilliseconds);Check(f.Peer.Count("target-attach")==3&&f.Peer.Count("install")==1&&f.Peer.Count("files")==1,"replacement replayed a renderer mutation");Results.Add(Json.Obj("case","two-explicit-replacements-exclude-all-retired-pages","passed",true,"targetAttaches",3,"installs",1));
 }
 using(var f=new Fixture(root)){
  Set(f.Session,"workspaceReadyBudget",900);
  f.Peer.Intercept=r=>{
   if(r.Kind=="discover"&&r.Number>=2){f.Peer.Reply(r,Json.Obj("targetInfos",new object[]{Json.Obj("targetId","workspace_"+r.Number,"type","page","url","app://-/index.html")}));return true;}
   if(r.Kind=="target-attach"&&r.Number>=2){f.Peer.Reply(r,Json.Obj("sessionId","workspace_session_"+r.Number));return true;}
   if(r.Kind=="root"){
    string current=(string)Get(f.Session,"session");f.Peer.Later(320,()=>{typeof(WallpaperSession).GetMethod("OnSignal",Private).Invoke(f.Session,new object[]{"target-detached",current});f.Peer.Value(r,Json.Obj("rootReady",true,"bodyReady",true,"workspaceReady",false));});return true;
   }return false;
  };
  var clock=Stopwatch.StartNew();var e=f.Prepare();Check(e!=null&&clock.ElapsedMilliseconds<1600&&f.Peer.Count("install")==0,"replacement reset shared startup deadline");Results.Add(Json.Obj("case","replacements-share-one-startup-deadline","passed",true,"elapsedMs",clock.ElapsedMilliseconds,"targetAttaches",f.Peer.Count("target-attach")));
 }
 for(int n=1;n<=6;n++){
  var payload=Json.Obj("workspaceReady",false,"rootReady",true,"bodyReady",true,"sensitive","must-not-leak");
  string line=PreparationDiagnostics.FormatReadinessObservation(2,1,payload,"hidden");Check(line.Contains("workspaceReady=0")&&!line.Contains("must-not-leak"),"unsafe or missing workspace diagnostics");
  string exception=PreparationDiagnostics.FormatEvent(41,1,n,null);Check(exception.Contains("phase=41")&&exception.Length<300,"unbounded exception category diagnostic");
 }
 Results.Add(Json.Obj("case","numeric-workspace-and-exception-diagnostics","passed",true));
 Console.WriteLine(Json.Serializer().Serialize(Json.Obj("passed",true,"productStarted",false,"cases",Results)));return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
}

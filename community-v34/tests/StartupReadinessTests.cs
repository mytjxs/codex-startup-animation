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
public static class StartupReadinessTests {
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
  internal readonly List<Thread> Jobs=new List<Thread>();internal Stopwatch ShellClock,LayoutClock,MediaClock;bool memoryPressure;
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
   "attached",media,"mediaReady",media,"prepared",media,"disposed",false,"playing",false,"wallpaperApplied",false,"revealed",false,"layoutSupported",true,
   "manuallyPaused",false,"probeOnly",false,"firstFrameConfirmed",false,"documentHidden",false,"stageVisible",false,"memoryPressure",memoryPressure,"code",media?"prepared":"preparing","mediaTimeMs",0,
   "documentGeneration",Owner.State.DocumentGeneration,"protocolVersion",2,"leaseNeedsRenew",false,"mediaGeneration",media?1:0,"actualPhase",media?"prepared":"preparing","health","fresh",
   "prepareOperationId","","commitAccepted",false,"recoveryNeeded",false,"recoveryStable",false,"recoveryReason","","rendererClockMs",1000,
   "layoutWindowElectron",true,"layoutMainCount",1,"layoutActiveMainCount",1);}
  internal void Default(Request r){
   switch(r.Kind){
    case "discover":Reply(r,Json.Obj("targetInfos",new object[]{Json.Obj("targetId","fixture_target","type","page","url","app://-/index.html")}));break;
    case "target-attach":Reply(r,Json.Obj("sessionId","fixture_session"));break;
    case "root":Value(r,Json.Obj("rootReady",true,"bodyReady",true));break;
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
    case "pressure-on":memoryPressure=true;Value(r,Json.Obj("ok",true));break;
    case "media-status":var status=Status(true);if(memoryPressure){status["mediaReady"]=false;status["prepared"]=false;status["dormant"]=true;status["actualPhase"]="hibernating";status["code"]="hibernating";}Value(r,status);break;
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
   else if(r.Phase==10)r.Kind=r.Expression.Contains(".memoryPressure(true)")?"pressure-on":r.Expression.Contains(".attach(")?"media-attach":r.Expression.Contains(".renewLease(")?"renew":"media-status";
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
  f.Peer.Intercept=r=>{if(r.Kind!=kind)return false;if(r.Number==1){f.Peer.Value(r,kind=="root"?Json.Obj("rootReady",true,"bodyReady",true):f.Peer.Status(kind=="media-status"),1800);return true;}if(r.Number==2){f.Peer.Value(r,kind=="root"?Json.Obj("rootReady",true,"bodyReady",true):f.Peer.Status(kind=="media-status"),400);return true;}return false;};
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
  Case(root,"normal-full-preparation",null,Success);
  foreach(string kind in new[]{"root","layout","media-status"})SlowRead(root,kind);
  Case(root,"layout-ready-at-8100ms-borrows-root-slack",f=>f.Peer.Intercept=r=>{
   if(r.Kind!="layout")return false;long elapsed=f.Peer.LayoutClock.ElapsedMilliseconds;
   if(elapsed<5700){var value=f.Peer.Status(false);value["layoutSupported"]=false;f.Peer.Value(r,value);}
   else f.Peer.Value(r,f.Peer.Status(false),Math.Max(0,8100-(int)elapsed));return true;
  },(f,e,ms)=>{Success(f,e,ms);Check(ms>=8000&&ms<10500&&f.Transport.LateResponses>=1,"unused root slack not applied");});
  Case(root,"root-never-ready-stops-at-4s",f=>f.Peer.Intercept=r=>{if(r.Kind!="root")return false;f.Peer.Value(r,Json.Obj("rootReady",false,"bodyReady",true));return true;},(f,e,ms)=>{Failure(f,e,2,63);Check(ms>=3900&&ms<5200&&f.Peer.Count("install")==0,"root budget changed");});
  Case(root,"slow-root-plus-install-and-layout-share-12s",f=>f.Peer.Intercept=r=>{
   if(r.Kind=="root"){f.Peer.Value(r,Json.Obj("rootReady",f.Peer.ShellClock.ElapsedMilliseconds>=3000,"bodyReady",true));return true;}
   if(r.Kind=="install"){f.Peer.Value(r,Json.Obj("ok",true,"code","installed"),1000);return true;}
   if(r.Kind=="layout"){var value=f.Peer.Status(false);value["layoutSupported"]=false;f.Peer.Value(r,value);return true;}return false;
  },(f,e,ms)=>{Failure(f,e,6,66);Check(ms>=11800&&ms<13300&&f.Peer.LayoutClock.ElapsedMilliseconds<8500,"root/mutations/layout reset shared budget");Check(f.Peer.Count("document")==0,"unsupported layout bound files");});
  Silence(root,"root",2,4000);Silence(root,"layout",6,12000);Silence(root,"media-status",10,8500);
  MutationTimeout(root,"install",4,2000);MutationTimeout(root,"configure",4,1500);MutationTimeout(root,"files",9,2000);MutationTimeout(root,"media-attach",10,1500);MutationTimeout(root,"renew",10,750);
  Case(root,"layout-guard-rejection",f=>f.Peer.Intercept=r=>{if(r.Kind!="layout")return false;f.Peer.Value(r,Json.Obj("targetAccepted",false,"guardMask",0));return true;},(f,e,ms)=>{Failure(f,e,6,62536);Check(f.Peer.Count("layout")==1,"guard rejection retried");});
  Case(root,"layout-wrong-session-response",f=>f.Peer.Intercept=r=>{if(r.Kind!="layout")return false;f.Peer.Value(r,f.Peer.Status(false),0,"wrong_session");return true;},(f,e,ms)=>{Failure(f,e,6,31);Check(f.Peer.Count("layout")==1,"wrong session retried");});
  Case(root,"layout-protocol-rejection",f=>f.Peer.Intercept=r=>{if(r.Kind!="layout")return false;f.Peer.Reply(r,Json.Obj("code",-32601,"message","fixture"),0,null,true);return true;},(f,e,ms)=>{Failure(f,e,6,32);Check(f.Peer.Count("layout")==1,"protocol rejection retried");});
  Case(root,"layout-eof-fault",f=>f.Peer.Intercept=r=>{if(r.Kind!="layout")return false;typeof(CdpTransport).GetMethod("Fail",Private).Invoke(f.Transport,new object[]{35});return true;},(f,e,ms)=>{Failure(f,e,6,35);Check(f.Transport.Faulted&&f.Peer.Count("layout")==1,"terminal fault retried");});
  Case(root,"layout-write-fault",f=>f.Peer.Intercept=r=>{if(r.Kind!="layout")return false;r.Stall=true;return true;},(f,e,ms)=>{Failure(f,e,6,37);Check(e.TimeoutStage==2&&f.Transport.Faulted&&f.Peer.Count("layout")==1,"write fault retried");});
  Context(root,"disabled-inflight",f=>f.State.Command("disable"),0);
  Case(root,"layout-pressure-defers-decoder-only",f=>{f.EnablePressure();f.Peer.Intercept=r=>{if(r.Kind!="layout")return false;f.Peer.Later(100,()=>f.Pressure=true);f.Peer.Value(r,f.Peer.Status(false),400);return true;};},(f,e,ms)=>{Check(e==null&&!f.State.Disabled&&!f.State.Ready&&!f.State.Prepared&&f.State.MemoryPressure&&f.State.Attached&&f.State.ActualPhase=="hibernating","pressure did not defer decoder safely");Check(f.Peer.Count("document")==1&&f.Peer.Count("media-attach")==1&&f.Peer.Count("pressure-on")==1,"lightweight preparation was restarted or skipped");Check(ms<1500,"pressure introduced extra preparation wait");});
  Context(root,"session-changed",f=>Set(f.Session,"session","changed_session"),72);
  Context(root,"document-generation-changed",f=>Set(f.State,"DocumentGeneration",Guid.NewGuid().ToString("N")),72);
  Context(root,"target-lost",f=>Set(f.Session,"targetLost",true),72);
  Context(root,"transport-lost",f=>Set(f.Session,"transportLost",true),72);
  foreach(string field in new[]{"documentGeneration","protocolVersion"}){string key=field;Case(root,"layout-rejects-wrong-"+key,f=>f.Peer.Intercept=r=>{if(r.Kind!="layout")return false;var value=f.Peer.Status(false);value[key]=key=="documentGeneration"?(object)"wrong_generation":3;f.Peer.Value(r,value);return true;},(f,e,ms)=>{Failure(f,e,6,70);Check(f.Peer.Count("layout")==1&&f.Peer.Count("document")==0,"invalid snapshot accepted");});}
  Case(root,"late-invalid-layout-snapshot-is-ignored",f=>f.Peer.Intercept=r=>{
   if(r.Kind!="layout")return false;var value=f.Peer.Status(false);if(r.Number==1){value["documentGeneration"]="wrong_generation";f.Peer.Value(r,value,1800);}else if(r.Number==2){value["layoutSupported"]=false;f.Peer.Value(r,value,400);}else f.Peer.Value(r,value);return true;
  },(f,e,ms)=>{Success(f,e,ms);Check(f.Transport.LateResponses==1&&f.Peer.Count("layout")==3,"late invalid snapshot affected fresh observation");});
  Case(root,"disable-during-response-retry-backoff",f=>f.Peer.Intercept=r=>{if(r.Kind!="layout")return false;if(r.Number==1)f.Peer.Later(1540,()=>f.State.Command("disable"));return true;},(f,e,ms)=>{Check(e==null&&f.State.Disabled&&!f.State.Ready&&f.Peer.Count("layout")==1,"disable failed to stop retry");Check(ms<2400,"disable backoff delayed");});
  for(int phase=2;phase<=10;phase+=phase==2?4:4){
   var value=Json.Obj("rootReady",true,"bodyReady",false,"layoutSupported",false,"layoutWindowElectron",true,"layoutMainCount",129,"layoutActiveMainCount",1,"documentGeneration","sensitive_generation","extra","sensitive_payload");
   string line=PreparationDiagnostics.FormatReadinessObservation(phase,10,value,"sensitive_generation");Check(line.Length<1024&&!line.Contains("sensitive")&&line.Contains("sourcePhase="+phase)&&line.Contains("generationMatches=1"),"unsafe snapshot diagnostic");
   string read=PreparationDiagnostics.FormatPreparationRead(phase,phase==2?1:phase==6?2:3,1501,1500,137,3,10,2,1,1);Check(read.Length<1024&&read.Contains("phase=37")&&read.Contains("timeoutStage=3")&&read.Contains("sourcePhase="+phase),"read diagnostic incomplete");
  }
  Results.Add(Json.Obj("case","bounded-numeric-diagnostics","passed",true,"variants",6));
  Console.WriteLine(Json.Serializer().Serialize(Json.Obj("passed",true,"sourceModified",false,"productStarted",false,"cases",Results)));return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
}

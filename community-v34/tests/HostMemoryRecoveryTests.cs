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
internal static class HostMemoryRecoveryTests {
 static int checks;
 const ulong MiB=1024UL*1024,GiB=1024UL*MiB;
 const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
 static void Set(object value,string name,object field){value.GetType().GetField(name,Private).SetValue(value,field);}
 static object Get(object value,string name){return value.GetType().GetField(name,Private).GetValue(value);}
 static void Check(bool value,string name){Interlocked.Increment(ref checks);if(!value)throw new Exception(name);}
 static bool Until(Func<bool> condition,int timeout){var clock=Stopwatch.StartNew();while(clock.ElapsedMilliseconds<timeout){if(condition())return true;Thread.Sleep(10);}return condition();}
 sealed class Request {internal Dictionary<string,object> Packet;internal string Kind,Expression;internal int Phase;}
 sealed class Peer:FileStream {
  internal Fixture Owner;internal Action<Request> Before;internal readonly Dictionary<string,int> Counts=new Dictionary<string,int>();
  internal bool Attached,PressureApplied,ManualPause,Revealed,Disposed;internal long Generation;internal bool GuardRejected;
  internal Peer(string path):base(path,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.ReadWrite,1,FileOptions.Asynchronous){}
  internal int Count(string key){lock(Counts){int value;return Counts.TryGetValue(key,out value)?value:0;}}
  void Reply(Request r,object value,bool raw=false){var packet=Json.Obj("id",Json.Get(r.Packet,"id"),"result",raw?value:Json.Obj("result",Json.Obj("type","object","value",value)));if(r.Packet.ContainsKey("sessionId"))packet["sessionId"]=Json.Get(r.Packet,"sessionId");var bytes=Encoding.UTF8.GetBytes(Json.Serializer().Serialize(packet));typeof(CdpTransport).GetMethod("Frame",Private).Invoke(Owner.Transport,new object[]{bytes,bytes.Length});}
  internal Dictionary<string,object> Status(){bool ready=Attached&&!PressureApplied&&!Disposed;bool playing=ready&&Revealed&&!ManualPause;return Json.Obj(
   "attached",Attached&&!Disposed,"mediaReady",ready,"prepared",ready,"disposed",Disposed,"playing",playing,"wallpaperApplied",playing,"revealed",Revealed,"layoutSupported",true,
   "manuallyPaused",ManualPause,"probeOnly",false,"firstFrameConfirmed",playing,"documentHidden",false,"stageVisible",playing,"code",Disposed?"disposed":PressureApplied&&Attached?"hibernating":ready?Revealed?"playing":"prepared":"loading","mediaTimeMs",playing?100:0,
   "documentGeneration",Owner.State.DocumentGeneration,"protocolVersion",2,"leaseNeedsRenew",false,"mediaGeneration",Generation,"actualPhase",Disposed?"retired":PressureApplied&&Attached?"hibernating":ready?Revealed?"visible":"prepared":"preparing","health","fresh",
   "prepareOperationId","","commitAccepted",false,"recoveryNeeded",false,"recoveryStable",false,"recoveryReason","","rendererClockMs",1000,"dormant",PressureApplied&&Attached,"memoryPressure",PressureApplied);}
  void Default(Request r){switch(r.Kind){
   case "discover":Reply(r,Json.Obj("targetInfos",new object[]{Json.Obj("targetId","fixture_target","type","page","url","app://-/index.html")}),true);break;
   case "target-attach":Reply(r,Json.Obj("sessionId","fixture_session"),true);break;
   case "root":Reply(r,Json.Obj("rootReady",true,"bodyReady",true));break;
   case "preflight":Reply(r,Json.Obj("namespaceFree",true,"ownNodeCount",0));break;
   case "install":Reply(r,Json.Obj("ok",true,"code","installed"));break;
   case "binding":case "files":Reply(r,Json.Obj(),true);break;
   case "configure":case "renew":Reply(r,Json.Obj("ok",true));break;
   case "pressure-on":PressureApplied=true;Reply(r,Json.Obj("ok",true));break;
   case "pressure-off":PressureApplied=false;Generation++;Reply(r,Json.Obj("ok",true));break;
   case "media-attach":Attached=true;Generation=1;Reply(r,Json.Obj("ok",true));break;
   case "style":Reply(r,Json.Obj("styleAccepted",true));break;
   case "layout":case "status":Reply(r,Status());break;
   case "document":Reply(r,Json.Obj("root",Json.Obj("nodeId",1)),true);break;
   case "query":Reply(r,Json.Obj("nodeId",2),true);break;
   case "describe":Reply(r,Json.Obj("node",Json.Obj("backendNodeId",3,"nodeName","INPUT","attributes",new object[]{"id","codex-dragon-file-v1","type","file"})),true);break;
   case "input":Reply(r,Json.Obj("inputReady",true,"multiple",false));break;
   case "reveal":Revealed=true;ManualPause=false;Reply(r,Json.Obj("ok",true));break;
   case "pause":ManualPause=true;Reply(r,Json.Obj("ok",true));break;
   case "dispose":Disposed=true;Attached=false;Reply(r,Json.Obj("ok",true));break;
   case "cleanup":Reply(r,Json.Obj("apiPresent",false,"ownNodeCount",0,"markerPresent",false));break;
   default:throw new Exception("Unexpected fixture command "+r.Kind);
  }}
  public override IAsyncResult BeginWrite(byte[] buffer,int offset,int count,AsyncCallback callback,object state){
   Check(count>0&&buffer[offset+count-1]==0,"Real transport emits NUL-delimited frame");var p=Json.Map(Json.Serializer().DeserializeObject(Encoding.UTF8.GetString(buffer,offset,count-1)));string method=Json.Text(p,"method");
   var r=new Request{Packet=p,Phase=(int)Get(Owner.Session,"phase"),Expression=Json.Text(Json.Map(Json.Get(p,"params")),"expression")??""};string e=r.Expression;
   if(method=="Target.getTargets")r.Kind="discover";else if(method=="Target.attachToTarget")r.Kind="target-attach";else if(method=="Runtime.addBinding")r.Kind="binding";
   else if(method=="DOM.getDocument")r.Kind="document";else if(method=="DOM.querySelector")r.Kind="query";else if(method=="DOM.describeNode")r.Kind="describe";else if(method=="DOM.setFileInputFiles")r.Kind="files";
   else if(e.Contains(".memoryPressure(true)"))r.Kind="pressure-on";else if(e.Contains(".memoryPressure(false)"))r.Kind="pressure-off";else if(e.Contains(".attach("))r.Kind="media-attach";
   else if(e.Contains(".renewLease("))r.Kind="renew";else if(e.Contains(".reveal()"))r.Kind="reveal";else if(e.Contains(".pause()"))r.Kind="pause";else if(e.Contains("a.dispose()"))r.Kind="dispose";else if(e.Contains("apiPresent:"))r.Kind="cleanup";
   else if(r.Phase==2)r.Kind="root";else if(r.Phase==3)r.Kind="preflight";else if(r.Phase==4)r.Kind=e.Contains(".configure(")?"configure":"install";else if(r.Phase==5)r.Kind="style";else if(r.Phase==6)r.Kind="layout";else if(r.Phase==8)r.Kind="input";else r.Kind="status";
   lock(Counts){Counts[r.Kind]=Count(r.Kind)+1;}if(Before!=null)Before(r);
   if(GuardRejected&&r.Kind=="pressure-on")Reply(r,Json.Obj("targetAccepted",false,"guardMask",0));else Default(r);
   var completion=new TaskCompletionSource<object>(state);completion.SetResult(null);if(callback!=null)callback(completion.Task);return completion.Task;
  }
  public override void EndWrite(IAsyncResult result){((Task)result).GetAwaiter().GetResult();}
 }
 sealed class Fixture:IDisposable {
  internal readonly Peer Peer;internal readonly CdpTransport Transport;internal readonly HostState State;internal readonly WallpaperSession Session;internal MemoryPressureMonitor Memory;internal volatile bool Critical;internal Exception Failure;Thread worker;
  internal Fixture(string root,bool critical){
   Critical=critical;Peer=new Peer(Path.Combine(root,Guid.NewGuid().ToString("N")+".fixture"));Peer.Owner=this;
   var owned=typeof(OwnedClient).GetConstructor(Private,null,new[]{typeof(FileStream),typeof(FileStream),typeof(string)},null).Invoke(new object[]{Peer,Peer,Path.Combine(root,"never-launched.exe")});
   Transport=(CdpTransport)FormatterServices.GetUninitializedObject(typeof(CdpTransport));Set(Transport,"client",owned);Set(Transport,"gate",new object());Set(Transport,"writeGate",new object());
   var pending=typeof(CdpTransport).GetField("pending",Private);pending.SetValue(Transport,Activator.CreateInstance(pending.FieldType));State=new HostState();Session=new WallpaperSession(Transport,State,new Configuration{Enabled=true,Media="fixture.mp4",Renderer="({ok:true,code:'installed'})"},false);
   Memory=new MemoryPressureMonitor(State.Work,null,null,null,null,()=>{Memory.AvailableCommitBytes=Critical?256L*1024*1024:12L*1024*1024*1024;Memory.TotalCommitBytes=32L*1024*1024*1024;return Critical?1:2;});Set(Session,"preparationMemory",Memory);
  }
  internal Exception Prepare(){try{typeof(WallpaperSession).GetMethod("Prepare",Private).Invoke(Session,null);return null;}catch(TargetInvocationException e){return e.InnerException;}}
  internal void Start(){Set(Session,"memoryFactory",new Func<WaitHandle,MemoryPressureMonitor>(_=>Memory));worker=new Thread(()=>{try{Session.Run();}catch(Exception e){Failure=e;}}){IsBackground=true};worker.Start();}
  internal void Stop(){State.Command("disable");Check(worker==null||worker.Join(3000),"Owned controller stops promptly");}
  internal void NoReplay(){foreach(string key in new[]{"target-attach","install","binding","configure","files","media-attach"})Check(Peer.Count(key)<=1,"No mutation replay: "+key);Check(((IDictionary)Get(Transport,"pending")).Count==0,"Transport has no abandoned request");}
  public void Dispose(){if(worker!=null&&worker.IsAlive)Stop();Peer.Dispose();Memory.Dispose();State.Work.Dispose();State.TerminalRead.Dispose();}
 }
 static void CommitBoundaries(){
  Check(MemoryPressureMonitor.FallbackState(16*GiB,512*MiB,32*GiB,12*GiB)==2,"Low physical RAM with abundant process commit must not disable or pause wallpaper");
  Check(MemoryPressureMonitor.FallbackState(64*GiB,0,96*GiB,2*GiB)==2,"Physical notification alone cannot override healthy commit");
  Check(MemoryPressureMonitor.FallbackState(16*GiB,8*GiB,32*GiB,512*MiB-1)==1,"Critical available commit remains protected");
  Check(MemoryPressureMonitor.FallbackState(16*GiB,8*GiB,32*GiB,512*MiB)==0,"Exactly 512 MiB is neutral, not critical");
  Check(MemoryPressureMonitor.FallbackState(16*GiB,8*GiB,32*GiB,GiB-1)==0,"Recovery threshold retains a neutral band");
  Check(MemoryPressureMonitor.FallbackState(16*GiB,8*GiB,32*GiB,GiB)==2,"One GiB available commit permits recovery");
  foreach(var pair in new[]{new ulong[]{0,0},new ulong[]{GiB,GiB+1}}){bool refused=false;try{MemoryPressureMonitor.FallbackState(16*GiB,GiB,pair[0],pair[1]);}catch(HostFailure e){refused=e.Code==71;}Check(refused,"Invalid commit observations remain fail closed");}
 }
 static void Hysteresis(){
  var p=new MemoryPressurePolicy();Check(p.Update(true,false,0)==1&&p.Pressured,"Critical pressure pauses once");
  Check(p.Update(true,false,1000)==0&&p.Pressured,"Continuous critical pressure does not repeatedly mutate");
  Check(p.Update(false,true,2000)==0&&p.Pressured,"High commit starts existing recovery hold");
  Check(p.Update(false,true,31999)==0&&p.Pressured,"Recovery cannot bypass sustained high confirmation");
  Check(p.Update(false,true,32000)==-1&&!p.Pressured,"Recovered commit resumes once after bounded hysteresis");
  Check(p.Update(false,true,33000)==0&&!p.Pressured,"Steady recovered state does not repeat resume");
  p.Update(true,false,34000);p.Update(false,true,35000);p.Update(false,false,36000);
  Check(p.Update(false,true,66000)==0&&p.Pressured,"Neutral interval resets continuous recovery proof");
 }
 static void Suspended(Fixture f){Check(!f.State.Disabled&&f.State.Attached&&f.State.MemoryPressure&&!f.State.Ready&&!f.State.Prepared&&!f.State.Playing&&!f.State.WallpaperApplied,"Suspended attachment does not claim playback or disable");Check(f.State.ActualPhase=="hibernating"&&f.State.Health=="fresh"&&f.State.RefreshAvailable,"Suspension keeps same fresh controller");var snapshot=Json.Map(Json.Serializer().DeserializeObject(f.State.Snapshot()));Check(Json.Get(snapshot,"memoryPressure") is bool&&Json.Bool(snapshot,"memoryPressure"),"Pipe snapshot exports validated pressure bool");}
 static void DeferredPreparation(string root){
  using(var f=new Fixture(root,true)){Check(f.Prepare()==null,"Initial critical prepare is recoverable");Suspended(f);Check(f.Peer.Count("pressure-on")==1&&f.Peer.Count("media-attach")==1,"Pressure applied once before one attachment");f.NoReplay();}
  using(var f=new Fixture(root,false)){f.Peer.Before=r=>{if(r.Kind=="media-attach")f.Critical=true;};Check(f.Prepare()==null,"Pressure during attach is recoverable");Suspended(f);f.NoReplay();}
  using(var f=new Fixture(root,true)){f.Peer.Before=r=>{if(r.Kind=="pressure-on")f.State.Command("disable");};Check(f.Prepare()==null&&f.State.Disabled&&f.Peer.Count("media-attach")==0,"Disable during pressure acknowledgement prevents attach");f.NoReplay();}
  foreach(string change in new[]{"session","generation","target","transport","guard"})using(var f=new Fixture(root,true)){
   f.Peer.Before=r=>{if(r.Kind!="pressure-on")return;if(change=="session")Set(f.Session,"session","replaced_session");else if(change=="generation")Set(f.State,"DocumentGeneration",Guid.NewGuid().ToString("N"));else if(change=="target")Set(f.Session,"targetLost",true);else if(change=="transport")Set(f.Session,"transportLost",true);else f.Peer.GuardRejected=true;};
   var e=f.Prepare() as HostFailure;Check(e!=null&&e.Code==(change=="guard"?63560:72)&&f.Peer.Count("media-attach")==0,"Rejected pressure context cannot attach: "+change);Check(!f.State.Ready&&!f.State.Prepared,"Rejected context publishes no proof");f.NoReplay();
  }
 }
 static void StrictPressureStatus(string root){
  using(var f=new Fixture(root,true)){Check(f.Prepare()==null,"Status fixture prepared");foreach(object bad in new object[]{null,"true",1}){var status=f.Peer.Status();status["memoryPressure"]=bad;bool rejected=false;try{typeof(WallpaperSession).GetMethod("UpdateStatus",Private).Invoke(f.Session,new object[]{status});}catch(TargetInvocationException e){var h=e.InnerException as HostFailure;rejected=h!=null&&h.Code==70;}Check(rejected,"Missing or malformed pressure state rejected");}f.State.ClearDiagnostics();Check(!f.State.MemoryPressure&&!Json.Bool(Json.Map(Json.Serializer().DeserializeObject(f.State.Snapshot())),"memoryPressure"),"Diagnostics clear pressure proof");}
 }
 static void ControllerRecovery(string root){
  using(var f=new Fixture(root,true)){
   f.State.Command("reveal");f.Start();Check(Until(()=>f.Peer.Count("reveal")==1&&f.State.MemoryPressure,2500),"Early reveal reaches suspended controller promptly");Suspended(f);
   Check(!PreparationWatchdog.Expire(f.State,120000)&&!PreparationWatchdog.Expire(f.State,600000)&&!f.State.Disabled&&!f.State.Ready,"Revealed critical suspension survives old startup watchdog without fabricated readiness");
   string document=f.State.DocumentGeneration;f.Critical=false;f.State.Work.Set();
   Check(Until(()=>f.Peer.Count("pressure-off")==1&&f.State.Ready&&f.State.Playing,34000),"Same controller resumes after existing 30 s high-commit hold");
   Check(f.State.DocumentGeneration==document&&f.Peer.Count("media-attach")==1&&f.Peer.Count("pressure-on")==1,"Recovery keeps one attach and document");Check(!f.State.MemoryPressure&&f.State.MediaGeneration==2&&f.Peer.Count("reveal")==1,"Fresh generation confirms resumed reveal without replay");f.NoReplay();f.Stop();Check(f.Failure==null&&f.Peer.Count("dispose")==1,"Disable after recovery disposes exactly once");
  }
  using(var f=new Fixture(root,true)){f.Start();Check(Until(()=>f.State.MemoryPressure,2500),"Critical controller starts");f.State.Command("pause");Check(Until(()=>f.Peer.Count("pause")==1,1000),"Manual pause remains prompt during pressure");Check(f.Peer.Count("pressure-off")==0&&!f.State.Ready,"User pause cannot be mistaken for recovery");f.Stop();Check(f.Failure==null&&f.State.Disabled&&f.Peer.Count("dispose")==1,"Disable remains terminal during pressure");f.NoReplay();}
  using(var f=new Fixture(root,true)){f.Start();Check(Until(()=>f.State.MemoryPressure,2500),"Target-loss fixture suspended");Set(f.Session,"targetLost",true);f.State.Work.Set();Check(Until(()=>f.Failure!=null,2000),"Lost target remains terminal during pressure");Check(f.Failure is HostFailure&&((HostFailure)f.Failure).Code==72&&f.State.Disabled,"Target identity protection unchanged");f.NoReplay();}
 }
 static void WaitBoundaries(){
  using(var work=new AutoResetEvent(false)){int reads=0;using(var memory=new MemoryPressureMonitor(work,null,null,null,null,()=>{Interlocked.Increment(ref reads);return 1;})){
   var signal=new Thread(()=>{Thread.Sleep(120);work.Set();}){IsBackground=true};signal.Start();var clock=Stopwatch.StartNew();Check(memory.Wait(true,0)==0&&clock.ElapsedMilliseconds<700,"Pressure wait remains interruptible");signal.Join();Check(reads<=2,"Stable critical wait does not busy poll");
  }}
 }
 public static int Main(string[] args){try{string root=args[0];Directory.CreateDirectory(root);CommitBoundaries();Hysteresis();DeferredPreparation(root);StrictPressureStatus(root);ControllerRecovery(root);WaitBoundaries();Console.WriteLine("{\"passed\":true,\"checks\":"+checks+",\"realMemoryPressureCreated\":false,\"realCodexLaunched\":false}");return 0;}catch(Exception e){Console.WriteLine("FAIL: "+e.Message);return 1;}}
}
}

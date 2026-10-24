using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Windows.Threading;

namespace DragonLauncher {
internal static class ResourceProtectionRecoveryTests {
 static int checks,emergencyExits;
 const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
 static void Check(bool value,string description){checks++;if(!value)throw new Exception(description);}
 static object Read(object instance,string field){return instance.GetType().GetField(field,Private).GetValue(instance);}
 static void Write(object instance,string field,object value){instance.GetType().GetField(field,Private).SetValue(instance,value);}
 static void Invoke(object instance,string method,params object[] args){instance.GetType().GetMethod(method,Private).Invoke(instance,args);}
 static void Pump(Func<bool> complete){
  var frame=new DispatcherFrame();var clock=Stopwatch.StartNew();var timer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(10)};
  timer.Tick+=delegate{if(complete()||clock.ElapsedMilliseconds>=2000)frame.Continue=false;};
  timer.Start();try{Dispatcher.PushFrame(frame);}finally{timer.Stop();}Check(complete(),"bounded Dispatcher cleanup completes");
 }
 sealed class Peer:IDisposable {
  readonly NamedPipeServerStream pipe;readonly Thread worker;readonly bool pressure;readonly int requests;internal string Command;internal Exception Failure;internal bool Completed;
  internal Peer(string token):this(token,false){}
  internal Peer(string token,bool pressure){
   this.pressure=pressure;requests=pressure?2:1;
   pipe=new NamedPipeServerStream(BridgeActivation.PipeName(token),PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous);
   worker=new Thread(Serve){IsBackground=true};worker.Start();
  }
  void Serve(){try{for(int request=0;request<requests;request++){
   var waiting=pipe.BeginWaitForConnection(null,null);try{if(!waiting.AsyncWaitHandle.WaitOne(1800))return;pipe.EndWaitForConnection(waiting);}finally{waiting.AsyncWaitHandle.Close();}
   var bytes=new MemoryStream();for(int i=0;i<96;i++){int value=pipe.ReadByte();if(value<0)throw new EndOfStreamException();if(value==10)break;bytes.WriteByte((byte)value);}Command=Encoding.UTF8.GetString(bytes.ToArray());
   using(var own=Process.GetCurrentProcess()){long created;BridgeNative.CreationTime(own.Handle,out created);var status=new BridgeStatus{Version=1,Disposition=2,HostPid=own.Id,HostCreated=created,CodexPid=own.Id,CodexCreated=created,MediaCode=4,Ready=true};
    if(pressure){status.ProtocolVersion=2;status.DiagnosticsVersion=1;status.DocumentGeneration="0123456789abcdef0123456789abcdef";status.MediaGeneration=1;status.Sequence=1;status.ObservationAgeMs=0;status.MediaCode=5;status.Ready=false;status.Prepared=false;status.Attached=true;status.Playing=false;status.FirstFrameConfirmed=false;status.LayoutSupported=true;status.ProbeOnly=false;status.WallpaperApplied=false;status.DocumentHidden=false;status.StageVisible=false;status.MediaTimeMs=0;status.ActualPhase="hibernating";status.Health="fresh";}
    using(var result=new MemoryStream()){new DataContractJsonSerializer(typeof(BridgeStatus)).WriteObject(result,status);byte[] reply=result.ToArray();if(pressure){string json=Encoding.UTF8.GetString(reply);json=json.Substring(0,json.Length-1)+",\"desiredMode\":\"enabled\",\"memoryPressure\":true}";reply=Encoding.UTF8.GetBytes(json);}pipe.Write(reply,0,reply.Length);pipe.WriteByte(10);pipe.Flush();}}
   if(request+1<requests)pipe.Disconnect();
  }}catch(Exception error){Failure=error;}finally{Completed=true;}}
  internal void Join(){Check(worker.Join(2200),"owned pipe request completes within deadline");Check(Failure==null,"owned pipe response succeeds");}
  public void Dispose(){pipe.Dispose();worker.Join(250);}
 }
 static FilmSplash Film(string media,bool flipPreview,bool replay){
  var settings=new Settings{StartupIntroFile=media,StartupIdleFile=media,StartupFinaleFile=media,WallpaperEnabled=true};
  if(replay)settings.ReplayTarget=(ExistingReplayTarget)FormatterServices.GetUninitializedObject(typeof(ExistingReplayTarget));
  // Never show this WPF Window. Preview construction suppresses the recovery
  // helper; fake existing activation suppresses all official launch routes.
  var film=new FilmSplash(media,settings,true,true,delegate{Interlocked.Increment(ref emergencyExits);});
  if(flipPreview)Write(film,"preview",false);Write(film,"activationRequested",true);
  Check(Read(film,"recoveryGuard")==null,"preview constructor creates no external restore helper");
  return film;
 }
 static void Trip(FilmSplash film){Invoke(film,"OnResourceTrip","system-memory-pressure");Pump(()=>(bool)Read(film,"cleaned"));}
 static void VerifyClosed(FilmSplash film){
  Check((bool)Read(film,"cleaned")&&(bool)Read(film,"done"),"optional animation is closed and cleaned");
  Check((bool)Read(film,"playbackReleased"),"animation decoder is released immediately");
  Check(!(bool)Read(film,"transitioning")&&!film.HandoffSucceeded,"resource stop never reports a successful finale");
  Check(!(bool)Read(Read(film,"living"),"started"),"fixture never starts a media decoder");
  Check((bool)Read(Read(film,"living"),"disposed"),"living-film controller disposed after stop");
 }
 static void ExistingSession(string media){
  string token=Guid.NewGuid().ToString("N");using(var peer=new Peer(token)){
   var film=Film(media,true,false);var session=new BridgeSession(Process.GetCurrentProcess(),token,null);Write(film,"wallpaperSession",session);
   int closed=0;film.Closed+=delegate{closed++;};
   Invoke(film,"OnResourceTrip","system-memory-pressure");Invoke(film,"OnResourceTrip","system-memory-pressure");Pump(()=>(bool)Read(film,"cleaned"));peer.Join();
   Check(peer.Command=="reveal","resource protection must preserve wallpaper; observed "+(peer.Command??"none"));
   Check((int)Read(film,"wallpaperRevealRequested")==1,"wallpaper intent is reveal, never permanent disable");
   Check((int)Read(film,"resourceTrip")==1&&closed==1,"repeated protection callback closes once");VerifyClosed(film);
  }
 }
 static void HostOnlyPressure(string media){
  string token=Guid.NewGuid().ToString("N");using(var peer=new Peer(token,true)){
   var film=Film(media,true,false);var session=new BridgeSession(Process.GetCurrentProcess(),token,null);Write(film,"wallpaperSession",session);
   BridgeStatus status=session.Status(1000);PressureEvidence(session,status);Write(film,"wallpaperPollPending",1);
   Check((int)Read(film,"resourceTrip")==0&&Read(film,"resourceBreaker")==null,"no launcher resource breaker or real pressure is active");
   var clock=Stopwatch.StartNew();Invoke(film,"OnLivingWatchdog",0.0);Pump(()=>(bool)Read(film,"cleaned"));peer.Join();
   Check(clock.ElapsedMilliseconds<2500,"host-only pressure releases cover without waiting thirty-second recovery");
   Check(peer.Command=="reveal","host-only pressure keeps future reveal and never disables wallpaper");
   Check((int)Read(film,"wallpaperRevealRequested")==1&&(int)Read(film,"resourceTrip")==1,"host pressure uses ordinary resource skip exactly once");VerifyClosed(film);
  }
 }
 static void RejectPressure(BridgeSession session,BridgeStatus status,Action change,Action restore,string label){
  change();try{Check(!session.IsPreparationPressureSuspended(status),label);}finally{restore();}
 }
 static void PressureEvidence(BridgeSession session,BridgeStatus status){
  Check(session.IsPreparationPressureSuspended(status),"fresh authenticated pressure suspension accepted");
  Check(!session.IsPreparationPressureSuspended(null),"missing observation rejected");
  Check(!session.IsPreparationPressureSuspended(new BridgeStatus()),"unowned observation rejected");
  RejectPressure(session,status,()=>status.MemoryPressure=null,()=>status.MemoryPressure=true,"legacy missing pressure flag is unknown");
  RejectPressure(session,status,()=>status.MemoryPressure=false,()=>status.MemoryPressure=true,"hibernation without pressure does not skip");
  RejectPressure(session,status,()=>status.DesiredMode="user-paused",()=>status.DesiredMode="enabled","manual pause is not overridden");
  RejectPressure(session,status,()=>status.Health="stale",()=>status.Health="fresh","stale renderer evidence rejected");
  RejectPressure(session,status,()=>status.ProtocolVersion=1,()=>status.ProtocolVersion=2,"legacy protocol not used as pressure proof");
  RejectPressure(session,status,()=>status.DiagnosticsVersion=null,()=>status.DiagnosticsVersion=1,"missing diagnostics rejected");
  RejectPressure(session,status,()=>status.Attached=false,()=>status.Attached=true,"unattached decoder not pressure suspended");
  RejectPressure(session,status,()=>status.Ready=true,()=>status.Ready=false,"ready contradiction rejected");
  RejectPressure(session,status,()=>status.Prepared=true,()=>status.Prepared=false,"prepared contradiction rejected");
  RejectPressure(session,status,()=>status.Disabled=true,()=>status.Disabled=false,"terminal disable never revived");
  RejectPressure(session,status,()=>status.Playing=true,()=>status.Playing=false,"playing contradiction rejected");
  RejectPressure(session,status,()=>status.FirstFrameConfirmed=true,()=>status.FirstFrameConfirmed=false,"previous first frame is not fresh suspension proof");
  RejectPressure(session,status,()=>status.ActualPhase="preparing",()=>status.ActualPhase="hibernating","ordinary preparation not skipped");
  RejectPressure(session,status,()=>status.MediaGeneration=0,()=>status.MediaGeneration=1,"missing media generation rejected");
  RejectPressure(session,status,()=>status.Sequence=0,()=>status.Sequence=1,"missing sequence rejected");
  string generation=status.DocumentGeneration;RejectPressure(session,status,()=>status.DocumentGeneration="invalid",()=>status.DocumentGeneration=generation,"malformed document generation rejected");
  RejectPressure(session,status,()=>status.ObservationAgeMs=1501,()=>status.ObservationAgeMs=0,"host stale observation rejected");
  RejectPressure(session,status,()=>status.ObservationAgeMs=-1,()=>status.ObservationAgeMs=0,"negative age rejected");
  long requested=status.ObservationRequestedAt;RejectPressure(session,status,()=>status.ObservationRequestedAt=Stopwatch.GetTimestamp()-Stopwatch.Frequency*2,()=>status.ObservationRequestedAt=requested,"local cached snapshot ages out");
  RejectPressure(session,status,()=>{status.ObservationAgeMs=1000;status.ObservationRequestedAt=Stopwatch.GetTimestamp()-Stopwatch.Frequency;},()=>{status.ObservationAgeMs=0;status.ObservationRequestedAt=requested;},"host and complete request ages are combined");
  int pid=status.HostPid;RejectPressure(session,status,()=>status.HostPid=pid+1,()=>status.HostPid=pid,"host PID mismatch rejected");
  long created=status.HostCreated;RejectPressure(session,status,()=>status.HostCreated=created+1,()=>status.HostCreated=created,"host creation mismatch rejected");
  using(var serialized=new MemoryStream()){
   new DataContractJsonSerializer(typeof(BridgeStatus)).WriteObject(serialized,status);string json=Encoding.UTF8.GetString(serialized.ToArray());
   Check(json.IndexOf("ObservationRequestedAt",StringComparison.Ordinal)<0,"local request clock never serialized");
   foreach(string malformed in new[]{json.Replace("\"memoryPressure\":true","\"memoryPressure\":\"true\""),json.Replace("\"memoryPressure\":true","\"memoryPressure\":null"),json.Replace("\"memoryPressure\":true","\"memoryPressure\":1"),json.Replace("\"memoryPressure\":true","\"memoryPressure\":true,\"memoryPressure\":true"),json.Replace("\"desiredMode\":\"enabled\"","\"desiredMode\":\"unknown\""),json.Replace("\"desiredMode\":\"enabled\"","\"desiredMode\":false")}){
    bool rejected=false;byte[] data=Encoding.UTF8.GetBytes(malformed);try{BridgeActivation.Decode<BridgeStatus>(data,data.Length);}catch(InvalidDataException){rejected=true;}
    Check(rejected,"malformed pressure fields rejected without coercion");
   }
  }
  Check(session.IsPreparationPressureSuspended(status),"valid pressure evidence remains after rejection fixtures");
 }
 static void LateSession(string media){
  var film=Film(media,true,false);Trip(film);Check((int)Read(film,"wallpaperRevealRequested")==1,"early protection preserves late wallpaper reveal intent");VerifyClosed(film);
  string token=Guid.NewGuid().ToString("N");using(var peer=new Peer(token)){
   var result=new BridgeResult{Session=new BridgeSession(Process.GetCurrentProcess(),token,null)};
   Invoke(film,"ReleaseUnclaimedBridge",result);peer.Join();Check(peer.Command=="reveal","late owned activation result receives reveal after animation closed");Check(result.Session==null,"late session wrapper released");
  }
 }
 static void ReplayAndPreview(string media){
  var replay=Film(media,true,true);Trip(replay);Check((int)Read(replay,"wallpaperRevealRequested")==0,"replay resource stop never mutates existing wallpaper");VerifyClosed(replay);
  var preview=Film(media,false,false);Trip(preview);Check((int)Read(preview,"wallpaperRevealRequested")==0,"preview protection sends no wallpaper intent");VerifyClosed(preview);
 }
 static void TerminalDisable(string media){
  string token=Guid.NewGuid().ToString("N");using(var peer=new Peer(token)){
   var film=Film(media,true,false);Write(film,"wallpaperSession",new BridgeSession(Process.GetCurrentProcess(),token,null));
   Invoke(film,"DisableUnpreparedWallpaper",new object[]{null});Trip(film);peer.Join();
   Check(peer.Command=="disable","previous explicit disable remains the only existing-session request");
   Check((int)Read(film,"wallpaperRevealRequested")==-1,"resource stop cannot revive a terminal preparation failure");VerifyClosed(film);
  }
  var late=Film(media,true,false);Invoke(late,"DisableUnpreparedWallpaper",new object[]{null});Trip(late);
  Check((int)Read(late,"wallpaperRevealRequested")==-1,"early terminal disable persists through resource cleanup");
  token=Guid.NewGuid().ToString("N");using(var peer=new Peer(token)){
   var result=new BridgeResult{Session=new BridgeSession(Process.GetCurrentProcess(),token,null)};
   Invoke(late,"ReleaseUnclaimedBridge",result);peer.Join();Check(peer.Command=="disable","late session retains previously terminal disable");
   Check(result.Session==null,"terminal late session wrapper released");
  }
 }
 [STAThread] static int Main(string[] args){try{
  string media=Path.Combine(args[0],"never-played-fixture.mp4");File.WriteAllBytes(media,new byte[]{0});
  HostOnlyPressure(media);ExistingSession(media);LateSession(media);ReplayAndPreview(media);TerminalDisable(media);Check(emergencyExits==0,"no emergency exit was needed");
  Console.WriteLine("{\"passed\":true,\"checks\":"+checks+",\"realCodexLaunched\":false,\"nativeWindowsShown\":false,\"realPrivatePipeUsed\":true,\"mediaDecoded\":false}");return 0;
 }catch(Exception error){Console.WriteLine("FAIL: "+error.Message);return 1;}}
}
}

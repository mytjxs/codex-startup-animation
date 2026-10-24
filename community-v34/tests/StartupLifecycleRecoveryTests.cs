using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace DragonLauncher {
// Exercises the actual FilmSplash orchestration without calling Show, starting
// video playback, or identifying/activating an installed Codex process. Each IPC
// peer belongs to this test process and authenticates with its retained handle.
internal static class StartupLifecycleRecoveryTests {
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static int checks,emergencyExits;
    static readonly List<object> cases=new List<object>();
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    static void Check(bool ok,string text){checks++;if(!ok)throw new Exception(text);}
    static object Read(object owner,string field){return owner.GetType().GetField(field,Private).GetValue(owner);}
    static void Write(object owner,string field,object value){owner.GetType().GetField(field,Private).SetValue(owner,value);}
    static object Invoke(object owner,string method,params object[] args){return owner.GetType().GetMethod(method,Private).Invoke(owner,args);}
    static void Record(string name){cases.Add(new {name=name,passed=true});}
    static bool Until(Func<bool> done,int bound){var watch=Stopwatch.StartNew();while(!done()&&watch.ElapsedMilliseconds<bound)Thread.Sleep(5);return done();}

    sealed class Peer:IDisposable {
        readonly NamedPipeServerStream pipe;readonly Thread worker;readonly ManualResetEvent release;
        internal readonly ManualResetEvent Seen=new ManualResetEvent(false);
        internal readonly List<string> Commands=new List<string>();internal Exception Failure;
        readonly int expected;readonly bool holdFirst;
        internal Peer(string token,int expected=1,bool holdFirst=false){
            this.expected=expected;this.holdFirst=holdFirst;release=new ManualResetEvent(!holdFirst);
            pipe=new NamedPipeServerStream(BridgeActivation.PipeName(token),PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous);
            worker=new Thread(Serve){IsBackground=true};worker.Start();
        }
        void Serve(){try{for(int count=0;count<expected;count++){
            var connection=pipe.BeginWaitForConnection(null,null);
            try{if(!connection.AsyncWaitHandle.WaitOne(2000))throw new TimeoutException("fixture connection bound");pipe.EndWaitForConnection(connection);}finally{connection.AsyncWaitHandle.Close();}
            using(var input=new MemoryStream()){
                for(int n=0;n<96;n++){int value=pipe.ReadByte();if(value<0)throw new EndOfStreamException();if(value==10)break;input.WriteByte((byte)value);}
                lock(Commands)Commands.Add(Encoding.UTF8.GetString(input.ToArray()));
            }
            if(count==0){Seen.Set();if(holdFirst&&!release.WaitOne(1800))throw new TimeoutException("fixture release bound");}
            using(var own=Process.GetCurrentProcess()){
                long created;BridgeNative.CreationTime(own.Handle,out created);
                var value=new BridgeStatus{Version=1,Disposition=2,HostPid=own.Id,HostCreated=created,CodexPid=own.Id,CodexCreated=created,MediaCode=4,Ready=true};
                using(var output=new MemoryStream()){new DataContractJsonSerializer(typeof(BridgeStatus)).WriteObject(output,value);byte[] bytes=output.ToArray();pipe.Write(bytes,0,bytes.Length);pipe.WriteByte(10);pipe.Flush();}
            }
            pipe.Disconnect();
        }}catch(Exception error){Failure=error;}finally{Seen.Set();}}
        internal void Release(){release.Set();}
        internal void Finish(){Release();Check(worker.Join(2500),"owned peer completed within bound");Check(Failure==null,"owned peer protocol completed");}
        public void Dispose(){release.Set();pipe.Dispose();worker.Join(500);Seen.Dispose();release.Dispose();}
    }
    static FilmSplash Film(string media,bool replay=false,bool preview=false){
        var settings=new Settings{StartupIntroFile=media,StartupIdleFile=media,StartupFinaleFile=media,WallpaperEnabled=true,StartupReadyTimeoutSeconds=12};
        if(replay)settings.ReplayTarget=(ExistingReplayTarget)FormatterServices.GetUninitializedObject(typeof(ExistingReplayTarget));
        // Preview construction prevents guardian-process creation. The exact
        // production methods can then be exercised against owned fake state.
        var film=new FilmSplash(media,settings,true,true,()=>Interlocked.Increment(ref emergencyExits));
        Write(film,"preview",preview);Write(film,"activationRequested",true);
        Check(Read(film,"recoveryGuard")==null&&!film.IsVisible,"fixture has no helper or visible window");
        return film;
    }
    static void Closed(FilmSplash film){
        Check((bool)Read(film,"cleaned")&&(bool)Read(film,"done"),"cover cleanup is terminal");
        Check((bool)Read(film,"playbackReleased"),"unused media controller is released");
        Check(!(bool)Read(Read(film,"living"),"started")&&(bool)Read(Read(film,"living"),"disposed"),"fixture never started decoder and disposed controller");
        Check(!film.HandoffSucceeded&&!film.PlaybackSucceeded,"skip/failure is not claimed as successful animation or handoff");
        Check(Read(film,"wallpaperSession")==null,"film relinquishes session wrapper");
        Check(((StartupPreparation)Read(film,"preparation")).IsTerminal,"closed preparation cannot accept a late observation");
    }
    static void ExistingSkip(string media){
        string token=Guid.NewGuid().ToString("N");using(var peer=new Peer(token)){
            var film=Film(media);var session=new BridgeSession(Process.GetCurrentProcess(),token,null);Write(film,"wallpaperSession",session);
            int closed=0;film.Closed+=delegate{closed++;};
            Invoke(film,"SkipAnimation");Invoke(film,"SkipAnimation");peer.Finish();
            Check(peer.Commands.Count==1&&peer.Commands[0]=="reveal","repeated explicit ESC sends one reveal, never disable");
            Check((bool)Read(film,"userSkipped")&&!(bool)Read(film,"resourceSkipped")&&closed==1,"explicit skip closes exactly once with correct reason");
            Check((int)Read(film,"wallpaperRevealRequested")==1,"ESC preserves wallpaper reveal intent");Closed(film);
            Check(Until(()=>(bool)Read(session,"disposed"),1000),"queued reveal releases retained identity after completion");
        }Record("explicit-esc-existing-session-is-idempotent");
    }
    static void LateSession(string media,bool terminalFailure){
        var film=Film(media);if(terminalFailure)Invoke(film,"DisableUnpreparedWallpaper",new object[]{null});
        Invoke(film,"SkipAnimation");Closed(film);
        Check((int)Read(film,"wallpaperRevealRequested")== (terminalFailure?-1:1),"late session keeps terminal cleanup intent");
        string token=Guid.NewGuid().ToString("N");using(var peer=new Peer(token)){
            var session=new BridgeSession(Process.GetCurrentProcess(),token,null);var result=new BridgeResult{Session=session};
            Invoke(film,"ReleaseUnclaimedBridge",result);Invoke(film,"ReleaseUnclaimedBridge",result);peer.Finish();
            Check(peer.Commands.Count==1&&peer.Commands[0]==(terminalFailure?"disable":"reveal"),"late result follows prior ESC or terminal failure exactly once");
            Check(result.Session==null,"late result wrapper does not retain disposed session");
            Check(Until(()=>(bool)Read(session,"disposed"),1000),"late command drain disposes retained identity");
        }Record(terminalFailure?"terminal-disable-before-esc-cannot-be-revived-by-late-result":"esc-before-activation-result-retains-wallpaper");
    }
    static void QueuedPreparationSkip(string media){
        string token=Guid.NewGuid().ToString("N");using(var peer=new Peer(token,2,true)){
            var film=Film(media);var session=new BridgeSession(Process.GetCurrentProcess(),token,null);Write(film,"wallpaperSession",session);Exception error=null;
            var pending=new Thread(()=>{try{session.Prepare(Guid.NewGuid().ToString("N"),1800);}catch(Exception failure){error=failure;}}){IsBackground=true};pending.Start();
            Check(peer.Seen.WaitOne(1000),"owned prepare holds IPC before explicit ESC");
            var clock=Stopwatch.StartNew();Invoke(film,"SkipAnimation");
            Check(clock.ElapsedMilliseconds<1000,"ESC cleanup does not wait for in-flight preparation");Closed(film);
            Check(!(bool)Read(session,"disposed"),"retained identity remains alive until pending prepare and reveal finish");
            peer.Release();Check(pending.Join(2000)&&error==null,"in-flight prepare completes safely after cover closed");peer.Finish();
            Check(peer.Commands.Count==2&&peer.Commands[0].StartsWith("prepare ",StringComparison.Ordinal)&&peer.Commands[1]=="reveal","explicit ESC orders reveal after in-flight prepare");
            Check(Until(()=>(bool)Read(session,"disposed"),1000),"final command drain releases retained identity");
        }Record("esc-during-inflight-prepare-is-nonblocking-and-reveal-is-drained");
    }
    static void PreviewAndReplay(string media){
        foreach(bool replay in new[]{false,true}){
            var film=Film(media,replay,!replay);Invoke(film,"SkipAnimation");Closed(film);
            Check((int)Read(film,"wallpaperRevealRequested")==0,"preview/replay never publishes wallpaper mutation intent");
            string token=Guid.NewGuid().ToString("N");var result=new BridgeResult{Session=new BridgeSession(Process.GetCurrentProcess(),token,null)};
            // Only replay owns a late result in production. A preview never
            // activates an application, so it cannot legitimately receive one.
            if(replay){var session=result.Session;Invoke(film,"ReleaseUnclaimedBridge",result);Check((bool)Read(session,"disposed"),"replay late session closes read-only without queued control");}
            else result.Dispose();
        }Record("explicit-esc-preview-and-existing-replay-do-not-mutate-wallpaper");
    }
    static void SharedDeadline(string media,bool replay,bool invalidate){
        var film=Film(media,replay);var state=(StartupPreparation)Read(film,"preparation");
        foreach(double elapsed in new[]{0,2,7,11,11.999}){
            if(invalidate)state.InvalidateTarget();Invoke(film,"OnLivingWatchdog",elapsed);
            Check(!(bool)Read(film,"done"),"predeadline gate remains pending without invented success");
        }
        var watch=Stopwatch.StartNew();Invoke(film,"OnLivingWatchdog",12.0);Closed(film);
        Check(watch.ElapsedMilliseconds<1000,"shared deadline closes immediately, not another client preparation budget");
        Check((int)Read(film,"wallpaperRevealRequested")== (replay?0:-1),"deadline failure preserves replay or disables only unprepared new wallpaper");
        Invoke(film,"OnLivingWatchdog",120.0);Check((bool)Read(film,"done"),"late watchdog cannot revive expired startup");
        Record(invalidate?"repeated-target-generation-invalidations-do-not-reset-ui-deadline":replay?"replay-missing-readiness-retains-shared-ui-deadline":"cold-missing-readiness-stops-at-shared-ui-deadline");
    }
    static void FailureAfterSkip(string media){
        var film=Film(media);Invoke(film,"SkipAnimation");Invoke(film,"FailLiving","late unrelated failure",true);
        Check((int)Read(film,"wallpaperRevealRequested")==1,"late failure callback cannot overwrite previously published ESC reveal");Closed(film);
        Record("late-media-or-native-failure-cannot-reverse-completed-esc");
    }
    static void ReadinessLeaseMatrix(){
        foreach(long now in new[]{999L,1000L,1001L,6999L,7000L,7001L,100000L})
        foreach(long observed in new[]{999L,1000L,1001L})
        foreach(bool same in new[]{false,true})foreach(bool responsive in new[]{false,true}){
            bool expected=now>=1000&&now-1000<=6000&&!(same&&!responsive&&observed>=1000);
            Check(NativeHandoff.HandoffProbeLease(1000,now,same,responsive,observed)==expected,"post-arm explicit-negative and absolute expiry matrix");
        }
        // A negative observation from before arming is not a later regression.
        // An explicit new negative beats the lease; cache age alone never extends it.
        Check(!NativeHandoff.HandoffProbeLease(-1,1000,true,true,1000),"unarmed probe cannot authorize handoff");
        var loss=new NativeHandoff.ReadinessLossPolicy();
        Check(!loss.Expired(false,false,1000)&&!loss.Expired(false,false,5999),"postready loss has fixed grace");
        Check(!loss.Expired(true,false,6000)&&!loss.Expired(false,false,6001),"actual ready observation resets transient-loss grace");
        Check(!loss.Expired(false,true,20000)&&!loss.Expired(false,false,30000),"armed finale owns its independent lease and later loss has separate origin");
        Check(loss.Expired(false,false,35000),"unarmed continuous loss expires at exact fixed grace");
        Record("native-final-lease-cross-product-and-transient-loss-recovery");
    }
    [STAThread] static int Main(string[] args){try{
        string media=Path.Combine(args[0],"never-played-fixture.mp4");File.WriteAllBytes(media,new byte[]{0});
        IntPtr foreground=GetForegroundWindow();var watch=Stopwatch.StartNew();
        ExistingSkip(media);LateSession(media,false);LateSession(media,true);QueuedPreparationSkip(media);PreviewAndReplay(media);
        SharedDeadline(media,false,false);SharedDeadline(media,true,false);SharedDeadline(media,false,true);FailureAfterSkip(media);ReadinessLeaseMatrix();
        Check(emergencyExits==0,"no emergency-exit path used");Check(GetForegroundWindow()==foreground,"test did not change foreground window");
        Console.WriteLine(new JavaScriptSerializer().Serialize(new {passed=true,checks=checks,elapsedMs=watch.ElapsedMilliseconds,nativeWindowsShown=false,mediaDecoded=false,actualCodexStarted=false,installedClientMutated=false,privateOwnedIpc=true,cases=cases}));return 0;
    }catch(Exception error){Console.WriteLine("FAIL: "+error.GetBaseException().Message);return 1;}}
}
}

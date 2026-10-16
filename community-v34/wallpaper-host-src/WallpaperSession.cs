using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CodexDragonWallpaper {
// Resource notifications are level-triggered. Wait only for the opposite
// state, and require a 30 s recovery cooldown before consulting the high event.
// Two notification handles share the existing session worker. If notification
// APIs fail, memory status is sampled on that same worker's bounded waits;
// there is no extra timer thread or ownership of the client's CDP handles.
internal sealed class MemoryPressureMonitor : IDisposable {
    [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr CreateMemoryResourceNotification(int type);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool QueryMemoryResourceNotification(SafeWaitHandle handle,out bool signaled);
    sealed class ResourceWait : WaitHandle {internal ResourceWait(IntPtr value){SafeWaitHandle=new SafeWaitHandle(value,true);}}
    [StructLayout(LayoutKind.Sequential)] internal struct MemoryStatus {internal uint Length,Load;internal ulong TotalPhysical,AvailablePhysical,TotalPageFile,AvailablePageFile,TotalVirtual,AvailableVirtual,AvailableExtendedVirtual;}
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    readonly WaitHandle low,high; readonly Func<bool> readLow,readHigh; readonly WaitHandle[] normal,pressured;readonly Func<int> fallback;int mismatches;bool fallbackOnly;
    internal MemoryPressureMonitor(WaitHandle work,WaitHandle low,WaitHandle high,Func<bool> query):this(work,low,high,query,()=>high.WaitOne(0)){}
    internal MemoryPressureMonitor(WaitHandle work,WaitHandle low,WaitHandle high,Func<bool> query,Func<bool> highQuery):this(work,low,high,query,highQuery,null){}
    internal MemoryPressureMonitor(WaitHandle work,WaitHandle low,WaitHandle high,Func<bool> query,Func<bool> highQuery,Func<int> fallbackQuery){this.low=low;this.high=high;readLow=query;readHigh=highQuery;fallback=fallbackQuery;fallbackOnly=low==null||high==null;normal=new[]{work,low};pressured=new[]{work,high};}
    // Only used if Windows notification creation/query fails. These are
    // conservative physical/available-commit headroom thresholds, not a
    // per-client RAM budget (Windows may report a lower process commit limit).
    // Preserve a neutral band and the existing 30 s recovery hysteresis.
    internal static int FallbackState(ulong total,ulong available,ulong commit,ulong commitAvailable){
        if(total==0||commit==0||available>total||commitAvailable>commit)throw new HostFailure(71);
        if(available<=total/20||commitAvailable<=commit/50)return 1;
        return available>=total/10&&commitAvailable>=commit/20?2:0;
    }
    static int ReadFallback(){var status=new MemoryStatus{Length=(uint)Marshal.SizeOf(typeof(MemoryStatus))};if(!GlobalMemoryStatusEx(ref status))throw new HostFailure(71);return FallbackState(status.TotalPhysical,status.AvailablePhysical,status.TotalPageFile,status.AvailablePageFile);}
    internal static MemoryPressureMonitor Create(WaitHandle work){
        ResourceWait low=null,high=null;
        try{
            IntPtr value=CreateMemoryResourceNotification(0);if(value==IntPtr.Zero||value==new IntPtr(-1))throw new HostFailure(71);low=new ResourceWait(value);
            value=CreateMemoryResourceNotification(1);if(value==IntPtr.Zero||value==new IntPtr(-1))throw new HostFailure(71);high=new ResourceWait(value);
            ResourceWait queryHandle=low,highHandle=high;
            return new MemoryPressureMonitor(work,low,high,()=>{bool active;if(!QueryMemoryResourceNotification(queryHandle.SafeWaitHandle,out active))throw new HostFailure(71);return active;},()=>{bool active;if(!QueryMemoryResourceNotification(highHandle.SafeWaitHandle,out active))throw new HostFailure(71);return active;},ReadFallback);
        }catch{if(low!=null)low.Dispose();if(high!=null)high.Dispose();return new MemoryPressureMonitor(work,null,null,null,null,ReadFallback);}
    }
    bool Read(bool highQuery){if(!fallbackOnly){try{return highQuery?readHigh():readLow();}catch{if(fallback==null)throw;fallbackOnly=true;}}if(fallback==null)throw new HostFailure(71);return (fallback()&(highQuery?2:1))!=0;}
    internal bool IsLow(){return Read(false);}
    internal bool IsHigh(){return Read(true);}
    internal void ValidateSignal(int signal,bool underPressure){
        // Wait and query are not atomic. A changed condition is a normal race,
        // not permanent failure. Back off while keeping disable interruptible.
        bool expected=underPressure?IsHigh():IsLow();
        if(signal==1&&!expected){mismatches=Math.Min(6,mismatches+1);normal[0].WaitOne(Math.Min(1000,25<<mismatches));}else mismatches=0;
    }
    internal int Wait(bool underPressure,int cooldownRemaining){
        if(fallbackOnly)return normal[0].WaitOne(cooldownRemaining>0?Math.Min(15000,cooldownRemaining):15000)?0:2;
        if(underPressure&&cooldownRemaining>0){int result=WaitHandle.WaitAny(normal,cooldownRemaining);return result==WaitHandle.WaitTimeout?2:result;}
        int index=WaitHandle.WaitAny(underPressure?pressured:normal,15000);return index==WaitHandle.WaitTimeout?2:index;
    }
    public void Dispose(){if(low!=null)low.Dispose();if(high!=null)high.Dispose();}
}
internal sealed class RecoveryBudget {
    readonly Queue<long> attempts=new Queue<long>();readonly Func<long> clock;bool incident;
    internal RecoveryBudget():this(()=>Stopwatch.GetTimestamp()*1000/Stopwatch.Frequency){}
    internal RecoveryBudget(Func<long> clock){this.clock=clock;}
    internal int Count {get{return attempts.Count;}}
    internal bool TryBegin(){long now=clock();while(attempts.Count>0&&now-attempts.Peek()>=1800000)attempts.Dequeue();if(incident||attempts.Count>=2)return false;attempts.Enqueue(now);incident=true;return true;}
    internal void HealthyCycle(){incident=false;}
}
internal sealed class MemoryPressurePolicy {
    internal bool Pressured;long highSince=-1,lastSample=-1;
    internal int Update(bool low,bool high,long now){
        if(lastSample>=0&&(now<lastSample||now-lastSample>45000))highSince=-1;
        lastSample=now;
        if(low){highSince=-1;if(!Pressured){Pressured=true;return 1;}return 0;}
        if(!Pressured)return 0;
        if(!high){highSince=-1;return 0;}
        if(highSince<0)highSince=now;
        if(now-highSince>=30000){Pressured=false;highSince=-1;return -1;}return 0;
    }
    internal int Cooldown(long now){return Pressured&&highSince>=0?Math.Max(1,Deadline.Remaining(30000,now-highSince)):0;}
}
internal static class MediaFileBinding {
    internal static bool Assign(Configuration config,Action<string[],int> assign,Func<bool> fallbackAllowed,Action fallbackDiagnostic){
        var clock=Stopwatch.StartNew();string original=config.Media;string[] files=config.MediaFiles;
        try{assign(files,2000);return false;}
        catch(HostFailure error){
            // Only an explicit CDP rejection of the verified two-file request
            // may drop the optional file. Never replay a timeout, transport
            // error, unverified configuration, or request onto another target.
            if(error.Code!=32||files.Length!=2||config.AlternateDiagnostic!=254||!fallbackAllowed())throw;
            int left=Deadline.Remaining(2000,clock.ElapsedMilliseconds);if(left<=0)throw;
            try{fallbackDiagnostic();}catch{} // A diagnostic cannot block fallback.
            left=Deadline.Remaining(2000,clock.ElapsedMilliseconds);if(left<=0)throw;
            assign(new[]{original},left);return true;
        }
    }
}
internal sealed class WallpaperSession {
    readonly CdpTransport cdp;readonly HostState state;readonly Configuration config;readonly bool isolated;string session;bool installed,disposed;int phase;
    readonly Stopwatch preparationClock=Stopwatch.StartNew();volatile bool transportLost,targetLost;long observationStarted,observationReceived;MemoryPressureMonitor preparationMemory;
    readonly PreparationSnapshotRecorder preparationSnapshots=new PreparationSnapshotRecorder();
    readonly RotationSnapshotRecorder rotationSnapshots=new RotationSnapshotRecorder();
    void CheckPreparationResources(){if(preparationMemory!=null&&preparationMemory.IsLow())throw new HostFailure(74);}
    void SetPhase(int next){if(phase==next)return;phase=next;PreparationDiagnostics.Record(phase,preparationClock.ElapsedMilliseconds,0);}
    const string Key="window.__codexDragonWallpaperV1";
    const string RootReady="(() => ({rootReady:!!document.getElementById('root'),bodyReady:!!document.body}))()";
    const string Preflight="(() => ({namespaceFree:!('__codexDragonWallpaperV1' in window),ownNodeCount:['codex-dragon-stage-v1','codex-dragon-file-v1','codex-dragon-style-v1'].filter(id=>document.getElementById(id)).length}))()";
    // The initial hidden stage belongs to the verified workspace surface and
    // is absolute. attach() later uses fixed positioning only for its 1px probe.
    internal const string Style="(() => {const s=document.getElementById('codex-dragon-style-v1'),n=document.getElementById('codex-dragon-stage-v1');let styleAccepted=false;try{if(s&&s.sheet&&s.sheet.cssRules.length>0&&n){const c=getComputedStyle(n);styleAccepted=c.position==='absolute'&&c.pointerEvents==='none';}}catch{}return {styleAccepted};})()";
    const string InputReady="(() => {const n=document.getElementById('codex-dragon-file-v1'),a=window.__codexDragonWallpaperV1;return {inputReady:!!n&&n.tagName==='INPUT'&&n.type==='file'&&!!a&&typeof a.attach==='function',multiple:!!n&&n.multiple===true};})()";
    const string Dispose="(() => {const a=window.__codexDragonWallpaperV1;if(a&&typeof a.dispose==='function')a.dispose();return {ok:true};})()";
    const string Cleanup="(() => ({apiPresent:'__codexDragonWallpaperV1' in window,ownNodeCount:['codex-dragon-stage-v1','codex-dragon-file-v1','codex-dragon-style-v1'].filter(id=>document.getElementById(id)).length,markerPresent:document.documentElement.getAttribute('data-codex-dragon-background')==='v1'}))()";
    internal WallpaperSession(CdpTransport transport,HostState status,Configuration settings,bool test){cdp=transport;state=status;config=settings;isolated=test;}
    void OnSignal(string kind,string eventSession){
        if(kind=="transport-fault"){transportLost=true;state.Stale(kind);}
        else if(session!=null&&eventSession==session){if(kind=="target-lost"){targetLost=true;state.Stale(kind);}state.Work.Set();}
    }
    internal static bool AcceptedTarget(Dictionary<string,object> target){
        string id=Json.Text(target,"targetId"),url=Json.Text(target,"url");if(Json.Text(target,"type")!="page"||!Regex.IsMatch(id??"","^[A-Za-z0-9_-]{1,128}$")||url==null||url.Length>8192)return false;
        if(url=="app://-/index.html")return true;const string prefix="app://-/index.html?initialRoute=";
        if(!url.StartsWith(prefix,StringComparison.Ordinal)||url.IndexOf('&')>=0||url.IndexOf('#')>=0)return false;
        try{string route=Uri.UnescapeDataString(url.Substring(prefix.Length));return route.Length<=2048&&route.StartsWith("/",StringComparison.Ordinal)&&!route.StartsWith("//",StringComparison.Ordinal)&&!Regex.IsMatch(route,"[\\x00-\\x1f\\x7f]");}catch{return false;}
    }
    internal static string Guard(string inner){
        return "(() => {let targetAccepted=false,guardMask=0;try{const u=new URL(location.href),q=Array.from(u.searchParams.entries());guardMask=64;if(u.protocol==='app:')guardMask|=1;if(u.hostname==='-')guardMask|=2;if(u.pathname==='/index.html')guardMask|=4;if(!u.port&&!u.username&&!u.password)guardMask|=8;if(!u.hash)guardMask|=16;if((!q.length&&!u.search)||(q.length===1&&q[0][0]==='initialRoute'&&q[0][1].length<=2048&&q[0][1].startsWith('/')&&!q[0][1].startsWith('//')&&!/[\\x00-\\x1f\\x7f]/.test(q[0][1])))guardMask|=32;targetAccepted=guardMask===127;}catch{}if(!targetAccepted)return {targetAccepted:false,guardMask};return ("+inner.Trim().TrimEnd(';')+");})()";
    }
    Dictionary<string,object> Eval(string expression,bool promise,int timeout){
        observationStarted=Stopwatch.GetTimestamp();
        var m=cdp.Send("Runtime.evaluate",Json.Obj("expression",Guard(expression),"returnByValue",true,"awaitPromise",promise,"userGesture",false),session,timeout);
        observationReceived=Stopwatch.GetTimestamp();
        if(Json.Get(m,"exceptionDetails")!=null)throw new HostFailure(60);
        var result=Json.Map(Json.Get(m,"result"));var value=Json.Map(Json.Get(result,"value"));
        if(Json.Get(value,"targetAccepted") is bool&&!Json.Bool(value,"targetAccepted")){long mask=Json.Number(value,"guardMask");if(mask<0||mask>127)throw new HostFailure(61);throw new HostFailure(61000+phase*256+(int)mask);}
        return value;
    }
    // Only the two idempotent runtime observations use retries. Preparation,
    // reveal/commit, injection, cleanup and decoder recovery retain their limits.
    Dictionary<string,object> RuntimeRead(string expression,long intent){
        string trustedSession=session;var total=Stopwatch.StartNew();
        for(int attempt=0;attempt<3;attempt++){
            if(transportLost||targetLost||cdp.Faulted||session!=trustedSession)throw new HostFailure(72);
            lock(state.Gate){if(state.Disabled||state.IntentRevision!=intent)return null;}
            int left=Deadline.Remaining(2600,total.ElapsedMilliseconds);if(left<=0)throw new HostFailure(37);
            try{
                var result=Eval(expression,false,Math.Min(750,left));
                if(transportLost||targetLost||cdp.Faulted||session!=trustedSession)throw new HostFailure(72);
                lock(state.Gate){if(state.Disabled||state.IntentRevision!=intent)return null;}
                return result;
            }
            catch(HostFailure error){
                if(error.Code!=37)throw;
                // A reply timeout is different from a terminal write/transport
                // failure. No previous playback proof survives either one.
                lock(state.Gate){state.Health="stale";state.Reason="response-timeout";state.Ready=state.Prepared=state.Revealed=state.CommitAccepted=false;state.ClearDiagnostics();state.ObservedAt=state.RendererSampleReceived=0;state.Sequence++;Monitor.PulseAll(state.Gate);}
                if(transportLost||targetLost||cdp.Faulted||session!=trustedSession)throw;
                lock(state.Gate){if(state.Disabled||state.IntentRevision!=intent)return null;}
                if(attempt==2)throw;
                var wait=Stopwatch.StartNew();int delay=attempt==0?100:250;
                while(wait.ElapsedMilliseconds<delay){
                    if(transportLost||targetLost||cdp.Faulted||session!=trustedSession)throw new HostFailure(72);
                    lock(state.Gate){if(state.Disabled||state.IntentRevision!=intent)return null;}
                    int remaining=Math.Min(Deadline.Remaining(delay,wait.ElapsedMilliseconds),Deadline.Remaining(2600,total.ElapsedMilliseconds));
                    // Do not consume AutoReset work notifications: reveal and
                    // refresh can arrive without changing the intent revision.
                    if(remaining<=0)throw new HostFailure(37);Thread.Sleep(Math.Min(25,remaining));
                }
            }
        }
        throw new HostFailure(37);
    }
    bool Disabled {get{lock(state.Gate)return state.Disabled;}}
    void SelectTarget(){
        var clock=Stopwatch.StartNew();string id=null;
        while(clock.ElapsedMilliseconds<12000&&!Disabled){
            CheckPreparationResources();
            var response=cdp.Send("Target.getTargets",Json.Obj(),null,2000);var values=Json.Get(response,"targetInfos") as IEnumerable;if(values==null)throw new HostFailure(62);
            int found=0;foreach(var value in values){var target=value as Dictionary<string,object>;if(target!=null&&AcceptedTarget(target)){found++;id=Json.Text(target,"targetId");}}
            if(found>1)throw new HostFailure(62);if(found==1)break;Thread.Sleep(500);
        }
        if(id==null)throw new HostFailure(62);
        var attached=cdp.Send("Target.attachToTarget",Json.Obj("targetId",id,"flatten",true),null,2000);session=Json.Text(attached,"sessionId");
        if(!Regex.IsMatch(session??"","^[A-Za-z0-9_-]{1,128}$"))throw new HostFailure(62);
    }
    void Prepare(){
        CheckPreparationResources();
        SetPhase(1);SelectTarget();SetPhase(2);var clock=Stopwatch.StartNew();bool rootReady=false;HostFailure lastGuard=null;
        while(clock.ElapsedMilliseconds<4000&&!Disabled){
            CheckPreparationResources();
            try{var ready=Eval(RootReady,false,1500);if(Json.Bool(ready,"rootReady")&&Json.Bool(ready,"bodyReady")){rootReady=true;break;}}
            catch(HostFailure e){if(e.Code<61000||e.Code>64999)throw;lastGuard=e;}
            Thread.Sleep(200);
        }
        if(!rootReady&&lastGuard!=null)throw lastGuard;
        if(!rootReady)throw new HostFailure(63);
        SetPhase(3);
        var preflight=Eval(Preflight,false,1500);if(!Json.Bool(preflight,"namespaceFree")||Json.Number(preflight,"ownNodeCount")!=0)throw new HostFailure(64);
        // The whole local renderer executes within the same synchronous
        // location guard. No separate navigation check/injection TOCTOU.
        CheckPreparationResources();SetPhase(4);var result=Eval(config.Renderer,false,2000);if(!Json.Bool(result,"ok")||Json.Text(result,"code")!="installed")throw new HostFailure(64);installed=true;
        cdp.Send("Runtime.addBinding",Json.Obj("name","__codexDragonNotifyV2"),session,1500);
        if(!Json.Bool(Eval(Key+".configure({documentGeneration:'"+state.DocumentGeneration+"',managed:true})",false,1500),"ok"))throw new HostFailure(64);
        SetPhase(5);
        if(!Json.Bool(Eval(Style,false,1500),"styleAccepted"))throw new HostFailure(65);
        // Main shell layout can appear after #root. Only fixed structural
        // booleans are read; no UI text, screenshot or account data.
        SetPhase(6);bool layout=false;clock.Restart();
        do{CheckPreparationResources();layout=Json.Bool(Eval(Key+".status()",false,1500),"layoutSupported");if(layout||isolated||Disabled)break;Thread.Sleep(250);}while(clock.ElapsedMilliseconds<8000);
        if(!layout&&!isolated)throw new HostFailure(66);if(Disabled)return;
        SetPhase(7);var document=cdp.Send("DOM.getDocument",Json.Obj("depth",0),session,1500);
        long root=Json.Number(Json.Map(Json.Get(document,"root")),"nodeId");
        var input=cdp.Send("DOM.querySelector",Json.Obj("nodeId",root,"selector","#codex-dragon-file-v1"),session,1500);long nodeId=Json.Number(input,"nodeId");if(nodeId<=0)throw new HostFailure(67);
        var described=cdp.Send("DOM.describeNode",Json.Obj("nodeId",nodeId,"depth",0),session,1500);var node=Json.Map(Json.Get(described,"node"));
        long backend=Json.Number(node,"backendNodeId");if(backend<=0||Json.Text(node,"nodeName")!="INPUT")throw new HostFailure(67);
        var attributes=Json.Get(node,"attributes") as object[];bool idOk=false,typeOk=false;
        if(attributes!=null)for(int i=0;i+1<attributes.Length;i+=2){if(attributes[i] as string=="id"&&attributes[i+1] as string=="codex-dragon-file-v1")idOk=true;if(attributes[i] as string=="type"&&attributes[i+1] as string=="file")typeOk=true;}
        SetPhase(8);var inputReady=Eval(InputReady,false,1500);if(!idOk||!typeOk||!Json.Bool(inputReady,"inputReady")||(config.AlternateMedia!=null&&!Json.Bool(inputReady,"multiple")))throw new HostFailure(67);
        // backendNodeId is the already validated input, never queried again
        // after navigation. Stale nodes fail; no retry onto a new document.
        CheckPreparationResources();SetPhase(9);string bindingSession=session;
        bool mediaFallback=MediaFileBinding.Assign(config,
            (files,timeout)=>{CheckPreparationResources();if(Disabled||transportLost||targetLost||cdp.Faulted||session!=bindingSession)throw new HostFailure(72);cdp.Send("DOM.setFileInputFiles",Json.Obj("files",files,"backendNodeId",backend),bindingSession,timeout);},
            ()=>!Disabled&&!transportLost&&!targetLost&&!cdp.Faulted&&session==bindingSession,
            ()=>PreparationDiagnostics.RecordMediaBindingFallback(preparationClock.ElapsedMilliseconds,false));
        if(mediaFallback)PreparationDiagnostics.RecordMediaBindingFallback(preparationClock.ElapsedMilliseconds,true);
        if(Disabled)return;
        CheckPreparationResources();SetPhase(10);clock.Restart();
        // Never hold an 8.5 s awaitPromise call while memory pressure or disable
        // is waiting. Start exactly one attach and observe its existing state;
        // the initial-frame deadline remains bounded and no extra thread is used.
        var started=Eval("(() => {"+Key+(isolated?".attach({deferReveal:true,mediaProbe:true})":".attach({deferReveal:true})")+".catch(() => {});return {ok:true};})()",false,1500);
        if(!Json.Bool(started,"ok"))throw new HostFailure(68);
        Dictionary<string,object> status=null;
        for(;;){
            if(Disabled)return;CheckPreparationResources();int left=Deadline.Remaining(8500,clock.ElapsedMilliseconds);if(left<=0)throw new HostFailure(68);
            status=Eval(Key+".status()",false,Math.Min(1500,left));
            preparationSnapshots.Observe(status,state.DocumentGeneration,preparationClock.ElapsedMilliseconds);
            if(Disabled)return;CheckPreparationResources();
            left=Deadline.Remaining(8500,clock.ElapsedMilliseconds);if(left<=0)throw new HostFailure(68);
            // A visibility/freeze resume can revoke the renderer lease before
            // the first frame arrives. Run() cannot renew it until Prepare()
            // finishes, so renew here only for this exact document generation.
            // Status, renewal and waiting all consume the original 8.5 s budget.
            if(Json.Text(status,"documentGeneration")!=state.DocumentGeneration||Json.Number(status,"protocolVersion")!=2||!(Json.Get(status,"leaseNeedsRenew") is bool))throw new HostFailure(70);
            if(Json.Bool(status,"leaseNeedsRenew")){
                long intent;lock(state.Gate)intent=state.IntentRevision;
                if(MutationAllowed(intent)){
                    if(!Json.Bool(Eval(Key+".renewLease('"+state.DocumentGeneration+"')",false,Math.Min(750,left)),"ok"))throw new HostFailure(70);
                    if(Disabled)return;CheckPreparationResources();
                    left=Deadline.Remaining(8500,clock.ElapsedMilliseconds);if(left<=0)throw new HostFailure(68);
                    // The pre-renewal snapshot is stale, even if it had a frame.
                    // Observe again rather than publishing stale readiness.
                    state.Work.WaitOne(Math.Min(250,left));continue;
                }
            }
            if(Json.Bool(status,"mediaReady")||Json.Bool(status,"recoveryNeeded"))break;
            if(Json.Bool(status,"disposed")||!Json.Bool(status,"attached"))throw new HostFailure(68);
            left=Deadline.Remaining(8500,clock.ElapsedMilliseconds);if(left<=0)throw new HostFailure(68);state.Work.WaitOne(Math.Min(250,left));
        }
        CheckPreparationResources();preparationSnapshots.Finish(1,preparationClock.ElapsedMilliseconds);SetPhase(11);
        // Decoder failure can arrive after attach resolved but before this
        // status sample. Preserve that narrowly identified recoverable state
        // for the same bounded recovery path used during normal playback.
        bool recoveryPending=RecoverablePreparation(status);
        if(!recoveryPending&&(!Json.Bool(status,"mediaReady")||!Json.Bool(status,"prepared")||!Json.Bool(status,"attached")||Json.Bool(status,"disposed")||Json.Bool(status,"playing")||Json.Bool(status,"wallpaperApplied")))throw new HostFailure(68);
        UpdateStatus(status);
        lock(state.Gate){if(!state.Disabled){state.RefreshAvailable=true;}}
    }
    internal static bool RecoverablePreparation(Dictionary<string,object> status){
        string code=Json.Text(status,"code");
        bool known=code=="media-error"||code=="play-rejected"||code=="first-frame-timeout"||code=="reveal-timeout"||code=="resume-timeout"||code=="playback-stalled";
        return known&&Json.Bool(status,"recoveryNeeded")&&Json.Text(status,"actualPhase")=="faulted"&&Json.Text(status,"health")=="faulted"&&
            !Json.Bool(status,"attached")&&!Json.Bool(status,"mediaReady")&&!Json.Bool(status,"prepared")&&!Json.Bool(status,"disposed")&&!Json.Bool(status,"playing")&&!Json.Bool(status,"wallpaperApplied");
    }
    void UpdateStatus(Dictionary<string,object> status){
        foreach(string field in new[]{"attached","mediaReady","prepared","disposed","playing","wallpaperApplied","revealed","layoutSupported","manuallyPaused","probeOnly","firstFrameConfirmed","documentHidden","stageVisible"})if(!(Json.Get(status,field) is bool))throw new HostFailure(70);
        string code=Json.Text(status,"code");if(String.IsNullOrEmpty(code)||code.Length>32)throw new HostFailure(70);
        object time=Json.Get(status,"mediaTimeMs");if(!(time is int)&&!(time is long))throw new HostFailure(70);
        long milliseconds=Json.Number(status,"mediaTimeMs");if(milliseconds<0||milliseconds>86400000)throw new HostFailure(70);
        bool attached=Json.Bool(status,"attached")&&!Json.Bool(status,"disposed");
        lock(state.Gate){
            if(state.Disabled){state.ClearDiagnostics();return;}
            // A rejected reply must not publish some of its fields while the
            // session unwinds into retirement. Validate the whole observation
            // before changing either the snapshot or its freshness anchors.
            if(Json.Text(status,"documentGeneration")!=state.DocumentGeneration||Json.Number(status,"protocolVersion")!=2)throw new HostFailure(70);
            long mediaGeneration=Json.Number(status,"mediaGeneration");string actualPhase=Json.Text(status,"actualPhase"),health=Json.Text(status,"health"),operation=Json.Text(status,"prepareOperationId")??"";
            if(mediaGeneration<0||mediaGeneration<state.MediaGeneration||!Regex.IsMatch(actualPhase??"","^(retired|faulted|hibernating|resuming|gap|paused|preparing|prepared|visible)$")||!Regex.IsMatch(health??"","^(fresh|stale|faulted)$")||!Regex.IsMatch(operation??"","^([a-f0-9]{32})?$")||!(Json.Get(status,"commitAccepted") is bool)||!(Json.Get(status,"recoveryNeeded") is bool)||!(Json.Get(status,"recoveryStable") is bool))throw new HostFailure(70);
            string reason=Json.Text(status,"recoveryReason")??"";if(reason.Length>32)throw new HostFailure(70);
            long rendererClock=Json.Number(status,"rendererClockMs");if(rendererClock<0||rendererClock>9007199254740991L)throw new HostFailure(70);
            bool layout=Json.Bool(status,"layoutSupported"),ready=attached&&Json.Bool(status,"mediaReady")&&(isolated||layout),revealed=attached&&Json.Bool(status,"revealed");
            bool probe=Json.Bool(status,"probeOnly"),playing=Json.Bool(status,"playing"),applied=Json.Bool(status,"wallpaperApplied"),firstFrame=Json.Bool(status,"firstFrameConfirmed");
            bool hidden=Json.Bool(status,"documentHidden"),visible=Json.Bool(status,"stageVisible"),committed=Json.Bool(status,"commitAccepted");
            int mediaCode=Json.Bool(status,"recoveryNeeded")?2:Code(code);
            state.Ready=ready;state.Revealed=revealed;state.MediaCode=mediaCode;
            state.DiagnosticsVersion=1;state.LayoutSupported=layout;state.ProbeOnly=probe;state.Attached=attached;
            state.Playing=playing;state.WallpaperApplied=applied;state.FirstFrameConfirmed=firstFrame;
            state.DocumentHidden=hidden;state.StageVisible=visible;state.MediaTimeMs=(int)milliseconds;
            state.MediaGeneration=mediaGeneration;state.ActualPhase=actualPhase;state.Health=health;state.Reason=reason;
            state.Prepared=ready;state.PrepareOperationId=operation;state.CommitAccepted=committed;
            state.ObservedAt=observationStarted;state.Sequence++;
            state.RendererClockMs=rendererClock;state.RendererSampleReceived=observationReceived;
        }
        rotationSnapshots.Observe(status,preparationClock.ElapsedMilliseconds);
    }
    void DisposeRenderer(){if(installed&&!disposed){SetPhase(13);Eval(Dispose,false,1500);var result=Eval(Cleanup,false,1500);if(Json.Bool(result,"apiPresent")||Json.Number(result,"ownNodeCount")!=0||Json.Bool(result,"markerPresent"))throw new HostFailure(69);disposed=true;}}
    internal static int Code(string text){switch(text){case "idle":return 0;case "loading":return 2;case "prepared":return 3;case "playing":case "reveal-pending":case "ready-hidden":case "resuming":return 4;case "paused":case "hibernating":return 5;case "disposed":return 6;case "unsupported-layout":return 7;default:return 8;}}
    bool MutationAllowed(long revision){lock(state.Gate)return !state.Disabled&&state.IntentRevision==revision&&state.DesiredMode=="enabled";}
    int OperationRemaining(long operation){lock(state.Gate){if(state.Disabled||state.OperationRequested!=operation||state.DesiredMode!="enabled")return 0;long elapsed=(long)((Stopwatch.GetTimestamp()-state.OperationStartedAt)*1000.0/Stopwatch.Frequency);return Deadline.Remaining(state.OperationTimeout,elapsed);}}
    long RendererExpiry(long operation,out int remaining){lock(state.Gate){
        // Translate durations, not clock origins. The renderer sampled its
        // clock before we received the reply, so receipt anchors produce an
        // earlier (conservative) renderer-domain deadline including queue time.
        remaining=0;long now=Stopwatch.GetTimestamp();
        if(state.Disabled||state.OperationRequested!=operation||state.DesiredMode!="enabled")return 0;
        long elapsed=(long)((now-state.RendererSampleReceived)*1000.0/Stopwatch.Frequency);
        if(state.RendererSampleReceived==0||elapsed<0||elapsed>30000)return 0;
        remaining=Math.Min(750,Deadline.Remaining(state.OperationTimeout,(long)((now-state.OperationStartedAt)*1000.0/Stopwatch.Frequency)));if(remaining<=0)return 0;
        return state.RendererClockMs+elapsed+remaining-1;
    }}
    internal void Run(){
        MemoryPressureMonitor memory=null;
        try{
            cdp.Signal+=OnSignal;
            lock(state.Gate){if(!config.Enabled)state.Disabled=true;state.MediaCode=2;}
            memory=MemoryPressureMonitor.Create(state.Work);preparationMemory=memory;
            if(!Disabled)Prepare();
            preparationMemory=null;var pressure=new MemoryPressurePolicy();var clock=Stopwatch.StartNew();bool appliedPressure=false;
            for(;;){
                bool disable,reveal,pause;long refresh,operation,intent;string prepare,commit;
                lock(state.Gate){disable=state.Disabled;reveal=state.RequestedReveal;pause=state.RequestedPause;state.RequestedReveal=false;state.RequestedPause=false;refresh=state.RefreshRequested>state.RefreshTaken?state.RefreshRequested:0;if(refresh>0)state.RefreshTaken=refresh;prepare=state.RequestedPrepare;commit=state.RequestedCommit;state.RequestedPrepare=state.RequestedCommit=null;operation=state.OperationRequested;intent=state.IntentRevision;}
                if(transportLost||targetLost||cdp.Faulted)throw new HostFailure(72);
                if(disable){DisposeRenderer();lock(state.Gate){state.Ready=false;state.MediaCode=6;state.Disabled=true;state.ClearDiagnostics();}return;}
                if(memory!=null){
                    bool low=memory.IsLow(),high=memory.IsHigh();pressure.Update(low,high,clock.ElapsedMilliseconds);
                    if(pressure.Pressured!=appliedPressure&&(pressure.Pressured?!Disabled:MutationAllowed(intent))){if(!Json.Bool(Eval(Key+".memoryPressure("+(pressure.Pressured?"true":"false")+")",false,1500),"ok"))throw new HostFailure(71);appliedPressure=pressure.Pressured;}
                }
                if(MutationAllowed(intent)){
                    var renewed=RuntimeRead(Key+".renewLease('"+state.DocumentGeneration+"')",intent);
                    if(renewed==null){if(refresh>0)state.CompleteRefresh(refresh,3);if(prepare!=null||commit!=null)state.CompleteOperation(operation,2);continue;}
                    if(!Json.Bool(renewed,"ok"))throw new HostFailure(70);
                }
                SetPhase(12);if(reveal&&MutationAllowed(intent)){var reply=Eval(Key+".reveal()",false,750);if(!Json.Bool(reply,"ok"))throw new HostFailure(70);}
                else if(pause){var reply=Eval(Key+".pause()",false,1500);if(!Json.Bool(reply,"ok"))throw new HostFailure(70);}
                int operationResult=1;
                if(prepare!=null||commit!=null){int left=OperationRemaining(operation);if(left<50||!MutationAllowed(intent))operationResult=2;
                    else{string action=prepare!=null?"prepare":"commit",op=prepare??commit;int budget;long expires=RendererExpiry(operation,out budget);if(expires==0||!Json.Bool(Eval(Key+"."+action+"('"+op+"',"+expires.ToString(System.Globalization.CultureInfo.InvariantCulture)+")",false,budget),"ok"))operationResult=2;else if(commit!=null){lock(state.Gate){state.RevealEverRequested=true;}}}}
                var current=RuntimeRead(Key+".status()",intent);
                if(current==null){if(refresh>0)state.CompleteRefresh(refresh,3);if(prepare!=null||commit!=null)state.CompleteOperation(operation,2);continue;}
                UpdateStatus(current);
                if(Json.Bool(current,"recoveryStable"))state.Recovery.HealthyCycle();
                if(Json.Bool(current,"recoveryNeeded")&&MutationAllowed(intent)){
                    if(!state.Recovery.TryBegin()){Eval(Key+".retire('recovery-budget-exhausted')",false,750);throw new HostFailure(73);}
                    long media=Json.Number(current,"mediaGeneration");
                    if(!Json.Bool(Eval(Key+".recover("+media.ToString(System.Globalization.CultureInfo.InvariantCulture)+")",false,750),"ok"))throw new HostFailure(73);
                    var recovered=RuntimeRead(Key+".status()",intent);
                    if(recovered==null){if(refresh>0)state.CompleteRefresh(refresh,3);if(prepare!=null||commit!=null)state.CompleteOperation(operation,2);continue;}
                    UpdateStatus(recovered);
                }
                if(refresh>0)state.CompleteRefresh(refresh,1);if(prepare!=null||commit!=null)state.CompleteOperation(operation,operationResult);
                if(memory==null)state.Work.WaitOne(15000);
                else{
                    int cooldown=pressure.Cooldown(clock.ElapsedMilliseconds);
                    int signal=memory.Wait(pressure.Pressured,cooldown);
                    if(cooldown==0)memory.ValidateSignal(signal,pressure.Pressured);
                }
            }
        }catch(Exception error){if(phase==10)preparationSnapshots.Finish(2,preparationClock.ElapsedMilliseconds);PreparationDiagnostics.RecordFailure(phase,preparationClock.ElapsedMilliseconds,error);state.Command("disable");state.ClearDiagnostics();try{DisposeRenderer();}catch(Exception cleanupError){PreparationDiagnostics.RecordFailure(13,preparationClock.ElapsedMilliseconds,cleanupError);}throw;}finally{preparationMemory=null;cdp.Signal-=OnSignal;if(memory!=null)memory.Dispose();state.EndRefresh();}
        // CDP transport is deliberately NOT disposed here, even on errors.
    }
}
}

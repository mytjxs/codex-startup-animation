using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Web.Script.Serialization;
namespace CodexDragonWallpaper {
internal static class RuntimeDiagnosticsTests {
    static int checks;
    static int layoutAuditBytes;
    static void Check(bool value,string name){if(!value)throw new Exception(name);checks++;}
    static readonly string[] AuditCounts={"layoutAuditFrameCount","layoutAuditActiveFrameCount","layoutAuditAnyMainCount","layoutAuditActiveAnyMainCount","layoutAuditPageSurfaceFrameCount","layoutAuditDirectRowCount","layoutAuditDecorationCount","layoutAuditComposerCount"};
    static readonly string[] AuditFlags={"layoutAuditLayoutTokenValid","layoutAuditPageLayoutTokenValid","layoutAuditFrameHashMatches","layoutAuditPageSurfaceFlag","layoutAuditDecorationValid","layoutAuditComposerAmbiguous"};
    static string[] CaptureReadiness(Dictionary<string,object> status,bool runtime=false){
        Type type=typeof(PreparationDiagnostics);const BindingFlags flags=BindingFlags.Static|BindingFlags.NonPublic;
        object gate=type.GetField("writerGate",flags).GetValue(null);var busy=type.GetField("writerBusy",flags);
        var pending=(Queue<string>)type.GetField("pending",flags).GetValue(null);
        lock(gate){
            Check((int)busy.GetValue(null)==0&&pending.Count==0,"readiness test queue initially idle");busy.SetValue(null,1);
            try{if(runtime)PreparationDiagnostics.RecordRuntimeObservation(status,"PRIVATE_SENTINEL_GENERATION",100);else PreparationDiagnostics.RecordReadinessObservation(6,100,status,"PRIVATE_SENTINEL_GENERATION");return pending.ToArray();}
            finally{pending.Clear();busy.SetValue(null,0);}
        }
    }
    static void LayoutAuditDiagnostics(){
        var status=Json.Obj("layoutReason","frame-shape","layoutSurfaceKind","frame","layoutFrameCount",1,"layoutActiveFrameCount",1,"layoutWorkspaceRowCount",1,"rootReady",true,"bodyReady",true,"documentGeneration","PRIVATE_SENTINEL_GENERATION","PRIVATE_SENTINEL_EXTRA","PRIVATE_SENTINEL_VALUE");
        foreach(string key in AuditCounts)status[key]=129;
        foreach(string key in AuditFlags)status[key]=1;
        string before=new JavaScriptSerializer().Serialize(status);
        foreach(int phase in new[]{2,6,10}){
            string line=PreparationDiagnostics.FormatLayoutAudit(phase,31536000000L,status);
            layoutAuditBytes=Math.Max(layoutAuditBytes,System.Text.Encoding.ASCII.GetByteCount(line));
            Check(line.Contains("phase=45 ")&&line.Contains(" sourcePhase="+phase+" "),"audit has a separate fixed phase and original source phase");
            Check(line.Contains(" layoutReason=frame-shape ")&&line.Contains(" layoutSurfaceKind=frame "),"audit uses fixed layout enums");
            foreach(string key in AuditCounts)Check(line.Contains(" "+key+"=129"),"audit maximum count: "+key);
            foreach(string key in AuditFlags)Check(line.Contains(" "+key+"=1"),"audit maximum flag: "+key);
            Check(System.Text.Encoding.ASCII.GetByteCount(line)<=1024,"audit maximum event fits 1 KiB sink");
            Check(!line.Contains("PRIVATE_SENTINEL")&&line.Split('\n').Length==2,"audit has no private strings or multiline payload");
        }
        Check(new JavaScriptSerializer().Serialize(status)==before,"audit observations never mutate renderer status");
        string[] pair=CaptureReadiness(status);
        Check(pair.Length==2&&pair[0].Contains("phase=38 ")&&pair[1].Contains("phase=45 "),"one throttled readiness call emits exactly phase 38 plus phase 45");
        Check(pair[0].Contains("layoutSurfaceKind=frame")&&pair[0].Contains("layoutFrameCount=1")&&pair[0].Contains("layoutActiveFrameCount=1")&&pair[0].Contains("layoutWorkspaceRowCount=1"),"phase 38 retains the host frame counters and renderer kind");
        Check(pair[0].Length<=1024&&pair[1].Length<=1024&&!pair[0].Contains("PRIVATE_SENTINEL")&&!pair[1].Contains("PRIVATE_SENTINEL"),"paired records fit sink and exclude raw identity");
        string[] runtime=CaptureReadiness(Status(),true);
        Check(runtime.Length==3&&runtime[0].Contains("phase=39 ")&&runtime[1].Contains("phase=40 ")&&runtime[2].Contains("phase=45 ")&&runtime[2].Contains(" sourcePhase=12 "),"runtime route changes retain shape evidence in the same throttled observation");
        foreach(string entry in runtime)Check(entry.Length<=1024&&!entry.Contains("PRIVATE_SENTINEL"),"runtime trio bounded and private fields omitted");
        foreach(object invalid in new object[]{-1,-2,130,Double.NaN,Double.PositiveInfinity,Double.NegativeInfinity,1.5,1.5m,"1",true,null}){
            foreach(string key in AuditCounts)status[key]=invalid;
            foreach(string key in AuditFlags)status[key]=invalid;
            string line=PreparationDiagnostics.FormatLayoutAudit(6,0,status);
            foreach(string key in AuditCounts)Check(line.Contains(" "+key+"=-1"),"invalid audit count is unknown: "+key);
            foreach(string key in AuditFlags)Check(line.Contains(" "+key+"=-1"),"invalid audit flag is unknown: "+key);
        }
        foreach(object invalidFlag in new object[]{2,129,2.0,2m}){
            foreach(string key in AuditFlags)status[key]=invalidFlag;
            string line=PreparationDiagnostics.FormatLayoutAudit(6,0,status);
            foreach(string key in AuditFlags)Check(line.Contains(" "+key+"=-1"),"numeric flag never accepts values above one: "+key);
        }
        foreach(object valid in new object[]{0,1,0L,1L,0.0,1.0,0m,1m}){
            foreach(string key in AuditCounts)status[key]=valid;
            foreach(string key in AuditFlags)status[key]=valid;
            string expected=Convert.ToInt32(valid,CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),line=PreparationDiagnostics.FormatLayoutAudit(6,0,status);
            Check(line.Contains(" layoutAuditFrameCount="+expected)&&line.Contains(" layoutAuditLayoutTokenValid="+expected),"integer numeric representations retain zero or one");
        }
        status["layoutReason"]="PRIVATE_SENTINEL_REASON";status["layoutSurfaceKind"]="PRIVATE_SENTINEL_KIND";
        string unknown=PreparationDiagnostics.FormatLayoutAudit(6,0,status);
        Check(unknown.Contains("layoutReason=unknown")&&unknown.Contains("layoutSurfaceKind=unknown")&&!unknown.Contains("PRIVATE_SENTINEL"),"unknown audit enums are never exported");
        unknown=PreparationDiagnostics.FormatLayoutAudit(2,0,null);
        foreach(string key in AuditCounts)Check(unknown.Contains(" "+key+"=-1"),"missing audit count stays unknown: "+key);
        foreach(string key in AuditFlags)Check(unknown.Contains(" "+key+"=-1"),"missing audit flag stays unknown: "+key);
        foreach(int phase in new[]{0,1,3,5,7,11,45}){bool rejected=false;try{PreparationDiagnostics.FormatLayoutAudit(phase,0,status);}catch(ArgumentOutOfRangeException){rejected=true;}Check(rejected,"invalid audit source phase rejected");}
        foreach(long elapsed in new[]{-1L,31536000001L}){bool rejected=false;try{PreparationDiagnostics.FormatLayoutAudit(6,elapsed,status);}catch(ArgumentOutOfRangeException){rejected=true;}Check(rejected,"invalid audit elapsed rejected");}
    }
    static string CaptureMemoryPressure(string state,long elapsed,long available,long total){
        Type type=typeof(PreparationDiagnostics);const BindingFlags flags=BindingFlags.Static|BindingFlags.NonPublic;
        var method=type.GetMethod("RecordMemoryPressure",flags);Check(method!=null,"missing recoverable memory-pressure diagnostics");
        object gate=type.GetField("writerGate",flags).GetValue(null);var busy=type.GetField("writerBusy",flags);
        var pending=(Queue<string>)type.GetField("pending",flags).GetValue(null);
        lock(gate){
            Check((int)busy.GetValue(null)==0&&pending.Count==0,"isolated diagnostic queue initially idle");busy.SetValue(null,1);
            try{method.Invoke(null,new object[]{state,elapsed,available,total});Check(pending.Count<=1,"one pressure transition emits at most one bounded record");return pending.Count==0?null:pending.Dequeue();}
            finally{pending.Clear();busy.SetValue(null,0);}
        }
    }
    static void MemoryPressureDiagnostics(){
        foreach(string state in new[]{"suspended","resume-requested"}){
            string line=CaptureMemoryPressure(state,31536000000L,536870911,34359738368);
            Check(line!=null&&line.Contains("phase=43")&&line.Contains("code=0")&&line.Contains("pressureState="+state),"recoverable pressure has a distinct fixed event");
            Check(line.Contains("availableCommitBytes=536870911")&&line.Contains("totalCommitBytes=34359738368"),"same-sample commit counters retained without resampling");
            Check(line.Length<=1024&&!System.Text.RegularExpressions.Regex.IsMatch(line,@"(?:^|\s)code=174(?:\s|$)"),"pressure event remains bounded and is not legacy terminal failure");
        }
        string maximum=CaptureMemoryPressure("suspended",0,281474976710656L,281474976710656L);
        Check(maximum.Contains("availableCommitBytes=281474976710656")&&maximum.Contains("totalCommitBytes=281474976710656"),"commit counter maximum is bounded at 2^48");
        string unknown=CaptureMemoryPressure("suspended",0,-2,Int64.MaxValue);
        Check(unknown.Contains("availableCommitBytes=-1")&&unknown.Contains("totalCommitBytes=-1"),"invalid commit counters stay unknown");
        Check(CaptureMemoryPressure("PRIVATE_SENTINEL_KEY",0,1,2)==null,"arbitrary pressure string is never logged");
        Check(CaptureMemoryPressure("suspended",-1,1,2)==null&&CaptureMemoryPressure("suspended",31536000001L,1,2)==null,"invalid elapsed time is ignored without changing operation");
    }
    static Dictionary<string,object> Status(){return Json.Obj("attached",true,"mediaReady",true,"prepared",true,"disposed",false,"playing",true,"wallpaperApplied",true,"revealed",true,"layoutSupported",true,"firstFrameConfirmed",true,"documentHidden",false,"stageVisible",true,"recoveryNeeded",false,"leaseNeedsRenew",false,"layoutSuspended",false,"memoryPressure",false,"manuallyPaused",false,"videoPaused",false,"videoSeeking",false,"playStarted",true,"frameCallbackPending",false,"preparationTimerPending",false,"frozen",false,"videoReadyState",4,"videoErrorCode",0,"videoNetworkState",2,"mediaTimeMs",86400000,"mediaGeneration",Int32.MaxValue,"presentedFrames",Int32.MaxValue,"protocolVersion",2,"documentGeneration","PRIVATE_SENTINEL_GENERATION","code","recovery-budget-exhausted","actualPhase","visible","health","fresh","commitAccepted",true,"videoTotalFrames",Int32.MaxValue,"videoDroppedFrames",Int32.MaxValue,"videoCorruptedFrames",Int32.MaxValue,"videoWidth",16384,"videoHeight",16384);}
    static int Main(){try{
        foreach(string reason in new[]{"frame-count","frame-token","frame-row","frame-shape"}){
            var shape=Json.Obj("layoutReason",reason,"layoutSurfaceKind","frame","layoutFrameCount",1,"layoutActiveFrameCount",1,"layoutWorkspaceRowCount",129);
            string observation=PreparationDiagnostics.FormatReadinessObservation(6,100,shape,"generation");
            Check(observation.Contains("layoutReason="+reason),"new frame shape diagnostic: "+reason);
            Check(observation.Contains("layoutSurfaceKind=frame")&&observation.Contains("layoutFrameCount=1")&&observation.Contains("layoutActiveFrameCount=1")&&observation.Contains("layoutWorkspaceRowCount=129"),"bounded frame shape counters");
            Check(observation.Length<=1024,"frame evidence fits sink");
            shape["layoutSurfaceKind"]="PRIVATE_SENTINEL";shape["layoutFrameCount"]=130;shape["layoutActiveFrameCount"]=Double.NaN;shape["layoutWorkspaceRowCount"]="1";
            observation=PreparationDiagnostics.FormatReadinessObservation(6,100,shape,"generation");
            Check(!observation.Contains("PRIVATE_SENTINEL")&&observation.Contains("layoutSurfaceKind=unknown"),"untrusted frame kind is not exported");
            Check(observation.Contains("layoutFrameCount=-1")&&observation.Contains("layoutActiveFrameCount=-1")&&observation.Contains("layoutWorkspaceRowCount=-1"),"invalid frame counts remain unknown");
        }
        var status=Status();status["layoutReason"]="composer-ambiguous";var before=new JavaScriptSerializer().Serialize(status);
        string line=PreparationDiagnostics.FormatRuntimeObservation(status,"PRIVATE_SENTINEL_GENERATION",31536000000L);
        Check(line.Contains("phase=39")&&line.Contains("commitAccepted=1"),"runtime state and commit");
        Check(line.Contains("layoutReason=composer-ambiguous"),"bounded layout reason");
        Check(PreparationDiagnostics.FormatReadinessObservation(6,1,status,"other").Contains("layoutReason=composer-ambiguous"),"preparation layout reason");
        Check(line.Length<=1024,"runtime line fits bounded sink: "+line.Length);
        Check(!line.Contains("PRIVATE_SENTINEL"),"document identity never logged");
        Check(line.Contains("generationMatches=1")&&line.Contains("firstFrameConfirmed=1"),"generation and first frame evidence");
        Check(new JavaScriptSerializer().Serialize(status)==before,"observations do not mutate state");
        string quality=PreparationDiagnostics.FormatPlaybackQuality(status,31536000000L);
        Check(quality.Contains("phase=40")&&quality.Contains("videoTotalFrames=2147483647")&&quality.Contains("videoWidth=16384"),"bounded quality maximums");
        Check(quality.Length<=1024&&!quality.Contains("PRIVATE_SENTINEL"),"quality sink and privacy");
        status["videoDroppedFrames"]=Double.NaN;status["videoTotalFrames"]=-1;status["videoCorruptedFrames"]=(long)Int32.MaxValue+1;status["videoWidth"]=16385;status.Remove("videoHeight");
        quality=PreparationDiagnostics.FormatPlaybackQuality(status,1);
        foreach(string key in new[]{"videoDroppedFrames","videoTotalFrames","videoCorruptedFrames","videoWidth","videoHeight"})Check(quality.Contains(key+"=-1"),"unknown "+key);
        status["code"]="PRIVATE_SENTINEL_CODE";status["health"]="PRIVATE_SENTINEL_HEALTH";status["actualPhase"]="PRIVATE_SENTINEL_PHASE";status["commitAccepted"]="true";status["layoutReason"]="PRIVATE_SENTINEL_LAYOUT";
        line=PreparationDiagnostics.FormatRuntimeObservation(status,"different",2);
        Check(!line.Contains("PRIVATE_SENTINEL")&&line.Contains("rendererCode=unknown")&&line.Contains("commitAccepted=-1"),"strict runtime fields");
        Check(line.Contains("generationMatches=0"),"wrong generation remains explicit");
        Check(line.Contains("layoutReason=unknown"),"unknown layout reason remains unknown");
        var stamps=new List<long>();var recorder=new RuntimeSnapshotRecorder((s,g,t)=>stamps.Add(t));status=Status();
        recorder.Observe(status,"generation",0);for(int i=1;i<2000;i++)recorder.Observe(status,"generation",i);
        Check(stamps.Count==1,"rapid queries do not flood log");
        status["commitAccepted"]=false;recorder.Observe(status,"generation",1999);recorder.Observe(status,"generation",2000);
        Check(stamps.Count==2&&stamps[1]==2000,"transition rate bounded at two seconds");
        recorder.Observe(status,"generation",31999);Check(stamps.Count==2,"steady period not shortened");
        recorder.Observe(status,"generation",32000);Check(stamps.Count==3,"thirty-second observation heartbeat");
        recorder.Observe(null,"generation",34000);recorder.Observe(status,"generation",-1);recorder.Observe(status,"generation",31536000001L);
        Check(stamps.Count==3,"invalid elapsed and null ignored");
        status["layoutReason"]="frame-token";recorder.Observe(status,"generation",34000);
        Check(stamps.Count==4,"changed layout reason recorded without waiting for steady heartbeat");
        status["layoutSurfaceKind"]="frame";recorder.Observe(status,"generation",35999);
        Check(stamps.Count==4,"changed surface still obeys two-second minimum");
        recorder.Observe(status,"generation",36000);Check(stamps.Count==5,"changed surface recorded once minimum interval reached");
        var throwing=new RuntimeSnapshotRecorder((s,g,t)=>{throw new InvalidOperationException();});throwing.Observe(status,"generation",0);
        Check(true,"diagnostic failure never changes operation");
        MemoryPressureDiagnostics();
        LayoutAuditDiagnostics();
        Console.WriteLine("{\"passed\":true,\"checks\":"+checks.ToString(CultureInfo.InvariantCulture)+",\"runtimeLineBytes\":"+line.Length+",\"layoutAuditBytes\":"+layoutAuditBytes+"}");return 0;
    }catch(Exception e){Console.WriteLine("{\"passed\":false,\"checks\":"+checks+",\"error\":"+new JavaScriptSerializer().Serialize(e.Message)+"}");return 1;}}
}}

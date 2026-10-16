using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Runtime.Serialization.Json;
using System.Xml;
using System.Xml.Linq;
using System.Runtime.InteropServices;

namespace DragonLauncher {
internal static class WallpaperSetup {
    internal const string OfficialAppId="OpenAI.Codex_2p2nqsd0c76g0!App";
    static readonly string Root=AppDomain.CurrentDomain.BaseDirectory;
    static bool diagnostics;
    static readonly Stopwatch StartupClock=Stopwatch.StartNew();
    static int setupPid;
    static void TraceStartup(string phase,long began=-1) {
        long elapsed=StartupClock.ElapsedMilliseconds;
        LauncherDiagnostics.Write("handoff-latest.log","Setup timing phase="+phase+"; pid="+setupPid+
            "; elapsedMs="+elapsed+"; durationMs="+(began<0?0:Math.Max(0,elapsed-began)));
    }
    internal enum Completion { Confirmed, Unconfirmed, AlreadyRunning, Error }
    internal sealed class AlreadyRunningException:InvalidOperationException {
        internal AlreadyRunningException(string message):base(message){}
    }
    internal static string FileHash(string path) {
        using(var sha=SHA256.Create()) using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,65536))
            return BitConverter.ToString(sha.ComputeHash(file)).Replace("-","");
    }
    internal static string RegularInside(string root,string relative) {
        root=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        string path=Path.GetFullPath(Path.Combine(root,relative));
        if(!path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("组件路径不在安装目录内");
        string current=path;
        while(current!=null) {
            if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("组件目录中存在不支持的链接");
            current=Path.GetDirectoryName(current);
        }
        return path;
    }
    internal static void VerifyComponents(string root,IDictionary<string,string> expected) {
        foreach(var item in expected) if(!String.Equals(FileHash(RegularInside(root,item.Key)),item.Value,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("组件校验未通过："+item.Key+"\n请使用配套安装版本 不要混用文件");
    }
    internal static void AssertNoRunningClient(string root,Func<uint> existing) {
        if(existing()!=0)throw new AlreadyRunningException("Codex 已在运行 本次重复请求不再启动");
        foreach(string name in new[]{"Start-Codex-Dragon","WallpaperHost"}) {
            foreach(var p in Process.GetProcessesByName(name))using(p) {
                try {if(!p.HasExited&&String.Equals(p.MainModule.FileName,Path.Combine(root,name+".exe"),StringComparison.OrdinalIgnoreCase))
                    throw new AlreadyRunningException("此安装的启动或壁纸组件正在运行 本次重复请求不再启动");}
                catch(System.ComponentModel.Win32Exception){throw new InvalidOperationException("无法确认启动组件状态\n当前不启动新的客户端");}
            }
        }
    }
    internal static Settings VerifySettings(string root) {
        string configPath=RegularInside(root,"launcher.config.xml");
        ReadConfig(configPath);
        var settings=Settings.Load(root);
        if(settings.AppId!=OfficialAppId || !String.IsNullOrWhiteSpace(settings.Exe) || !String.IsNullOrWhiteSpace(settings.Arguments))
            throw new InvalidDataException("当前配置不是已核验的官方 Codex 入口");
        string movie=RegularInside(root,settings.VideoFile);
        if(!String.Equals(movie,Path.Combine(root,"output","codex-startup-2k-144fps-crf16.mp4"),StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("启动片源与已核验版本不一致");
        if(settings.StartupIntroFile.Length>0){
            var segments=new[]{settings.StartupIntroFile,settings.StartupIdleFile,settings.StartupFinaleFile};
            var names=new[]{"startup-intro.mp4","startup-idle.mp4","startup-finale.mp4"};
            for(int i=0;i<segments.Length;i++)
                if(!String.Equals(RegularInside(root,segments[i]),Path.Combine(root,"output","versions","startup-living-v6",names[i]),StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("持续眨眼启动片源与已核验版本不一致");
        }
        if(!String.Equals(RegularInside(root,settings.Image),Path.Combine(root,"assets","dragon-master-open-cutout-v1.png"),StringComparison.OrdinalIgnoreCase) ||
           !String.Equals(RegularInside(root,settings.ClosedImage),Path.Combine(root,"assets","dragon-master-closed-cutout-v1.png"),StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("角色素材配置与已核验版本不一致");
        return settings;
    }
    static XDocument ReadConfig(string path) {
        var options=new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=65536};
        using(var reader=XmlReader.Create(path,options))return XDocument.Load(reader);
    }
    internal sealed class ConfigChange {
        internal string Path,Backup,WrittenHash,OriginalHash,Pending;
        internal bool Changed,Required;
        internal void Apply() {
            if(FileHash(Path)!=OriginalHash)throw new InvalidDataException("配置刚刚发生变化 请重新打开启用入口");
            if(!Required)return;
            if(FileHash(Pending)!=WrittenHash)throw new InvalidDataException("待安装配置校验失败");
            File.Replace(Pending,Path,null);Changed=true;
        }
        internal void Cleanup() {if(Pending!=null&&File.Exists(Pending))File.Delete(Pending);}
        internal void RollbackIfUnchanged() {
            if(!Changed||FileHash(Path)!=WrittenHash)return;
            if(FileHash(Backup)!=OriginalHash)throw new InvalidDataException("配置备份校验失败 未覆盖当前配置");
            string temp=Path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try {File.Copy(Backup,temp,false);File.Replace(temp,Path,null);}
            finally {if(File.Exists(temp))File.Delete(temp);}
        }
    }
    internal static ConfigChange PrepareEnable(string root) {
        string configPath=RegularInside(root,"launcher.config.xml");
        string originalHash=FileHash(configPath);
        var config=VerifySettings(root);
        if(FileHash(configPath)!=originalHash)throw new InvalidDataException("校验期间配置发生变化 请重新打开启用入口");
        var change=new ConfigChange{Path=configPath,Required=!config.WallpaperEnabled,OriginalHash=originalHash};
        if(config.WallpaperEnabled)return change;
        string backups=Path.Combine(root,"setup-backups");Directory.CreateDirectory(backups);
        RegularInside(root,"setup-backups");
        change.Backup=Path.Combine(backups,"launcher-config-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")+".xml");
        File.Copy(change.Path,change.Backup,false);
        if(FileHash(change.Backup)!=change.OriginalHash)throw new InvalidDataException("备份期间配置发生变化");
        XDocument xml=ReadConfig(change.Backup);
        xml.Root.SetElementValue("wallpaperEnabled","true");
        change.Pending=change.Path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {
            using(var stream=new FileStream(change.Pending,FileMode.CreateNew,FileAccess.Write,FileShare.None)){xml.Save(stream);stream.Flush(true);}
            change.WrittenHash=FileHash(change.Pending);return change;
        }catch{change.Cleanup();throw;}
    }
    internal static void WriteReceipt(string root,Dictionary<string,object> result) {
        string written=DateTime.UtcNow.ToString("o");
        if(!result.ContainsKey("createdUtc"))result["createdUtc"]=result.ContainsKey("utc")?result["utc"]:written;
        result["writtenUtc"]=written;result["utc"]=written;
        string folder=Path.Combine(root,"output");Directory.CreateDirectory(folder);RegularInside(root,"output");
        string path=Path.Combine(folder,"wallpaper-setup-result.json");
        if(File.Exists(path))RegularInside(root,"output\\wallpaper-setup-result.json");
        string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {
            using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))new DataContractJsonSerializer(typeof(Dictionary<string,object>),new DataContractJsonSerializerSettings{UseSimpleDictionaryFormat=true}).WriteObject(file,result);
            if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
        }finally{if(File.Exists(temp))File.Delete(temp);}
    }
    static Dictionary<string,object> Receipt(string stage,string detail) {
        string created=DateTime.UtcNow.ToString("o");
        return new Dictionary<string,object>{{"schemaVersion",1},{"utc",created},{"createdUtc",created},{"stage",stage},{"detail",detail},{"wallpaperPlaybackConfirmed",false},{"visualOr144FpsVerified",false},{"forcedClientExit",false}};
    }
    static void Preflight(string root) {
        Preflight(root,SetupReleaseManifest.Expected,()=>BridgeNative.ExistingOfficialProcess(OfficialAppId));
    }
    internal static void Preflight(string root,IDictionary<string,string> expected,Func<uint> existing) {
        // A duplicate request is benign only after this installation passed
        // its ordinary configuration and component integrity checks.
        VerifySettings(root);
        VerifyComponents(root,expected);
        // The host performs its pinned official package/signature/executable checks
        // before it creates Codex. Do not duplicate or weaken those checks here.
        AssertNoRunningClient(root,existing);
    }
    internal static Completion HandleFailure(string root,Exception error,bool launched,ConfigChange change,Dictionary<string,object> receipt,out string problem) {
        Exception failure=error;bool rollbackFailed=false;
        if(!launched&&change!=null)try{using(var maintenance=MaintenanceLaunchLease.Acquire(root,2000)){change.RollbackIfUnchanged();}}catch(Exception rollbackError){
            rollbackFailed=true;receipt["rollbackDetail"]=rollbackError.Message;
            failure=new InvalidOperationException(error.Message+"\n配置回退未完成："+rollbackError.Message,rollbackError);
        }
        if(!launched&&!rollbackFailed&&error is AlreadyRunningException){
            // Do not replace a successful first launch's receipt with a second
            // click's unconfirmed status, and never activate the existing app.
            problem=null;return Completion.AlreadyRunning;
        }
        problem=failure.Message;receipt["stage"]=launched?"unconfirmed":"not-started";receipt["detail"]=problem;receipt["wallpaperPlaybackConfirmed"]=false;
        try{WriteReceipt(root,receipt);}catch{}
        return Completion.Error;
    }
    internal sealed class OrdinaryFallbackAttempt {
        int attempted;
        sealed class Result {internal string Code,ErrorType;internal int HResult;internal uint Pid;internal bool ActivationAttempted;}
        internal async Task RunAsync(bool launcherCreated,bool dispatchAttempted,Completion completion,Dictionary<string,object> receipt,
            Func<IDisposable> acquire,Action ensureIdle,Func<uint> activate,Action<string> trace,int deadlineMilliseconds=5000) {
            if(deadlineMilliseconds<1||deadlineMilliseconds>5000)throw new ArgumentOutOfRangeException("deadlineMilliseconds");
            // A failed Process.Start can have an unknown outcome. Only failures
            // before dispatch are eligible; existing/replayed clients never enter.
            if(launcherCreated||dispatchAttempted||completion!=Completion.Error||Interlocked.CompareExchange(ref attempted,1,0)!=0)
                return;
            var completionSource=new TaskCompletionSource<Result>();int phase=0;
            Action<string> record=code=>{try{trace("ordinary-fallback-"+code);}catch{}};
            record("checking");
            var worker=new Thread(()=>{
                var result=new Result{Code="blocked"};
                try {
                    // Recheck admission under the same gate as every other entry.
                    // The official activation does not execute the decorated chain.
                    using(acquire()) {
                        ensureIdle();
                        // Timeout closes admission atomically. A delayed probe
                        // cannot create the app after the caller already left.
                        if(Interlocked.CompareExchange(ref phase,1,0)==0){
                            result.ActivationAttempted=true;record("requested");
                            result.Pid=activate();result.Code="accepted";
                        }
                    }
                }catch(Exception failure) {
                    result.ErrorType=failure.GetType().Name;result.HResult=failure.HResult;
                    result.Code=!result.ActivationAttempted?"blocked":failure is TimeoutException?"unknown":"failed";
                }finally{
                    completionSource.TrySetResult(result);
                }
            }){IsBackground=true,Name="Codex ordinary fallback"};
            Result observed;
            try{worker.SetApartmentState(ApartmentState.STA);worker.Start();}
            catch(Exception failure){Interlocked.Exchange(ref phase,3);completionSource.TrySetResult(new Result{Code="blocked",ErrorType=failure.GetType().Name,HResult=failure.HResult});}
            if(await Task.WhenAny(completionSource.Task,Task.Delay(deadlineMilliseconds)).ConfigureAwait(false)==completionSource.Task)
                observed=await completionSource.Task.ConfigureAwait(false);
            else {
                int previous=Interlocked.CompareExchange(ref phase,3,0);
                observed=completionSource.Task.IsCompleted?completionSource.Task.GetAwaiter().GetResult():new Result{Code=previous==1?"unknown":"blocked",ActivationAttempted=previous==1};
                // In-flight COM remains on the background STA with its gate.
                // No second activation, early gate release, or worker abort.
                // Process shutdown may end the worker after this bounded wait.
            }
            receipt["ordinaryFallback"]=observed.Code;receipt["ordinaryFallbackAttempted"]=observed.ActivationAttempted;
            if(observed.Pid!=0)receipt["ordinaryFallbackPid"]=observed.Pid;
            if(observed.ErrorType!=null){receipt["ordinaryFallbackErrorType"]=observed.ErrorType;receipt["ordinaryFallbackHResult"]=observed.HResult;}
            record(observed.Code);
        }
    }
    [ComImport,Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")] class OrdinaryActivationManager { }
    [ComImport,Guid("2e941141-7f97-4756-ba1d-9decde894a3d"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IOrdinaryActivationManager {
        [PreserveSig]int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)]string id,[MarshalAs(UnmanagedType.LPWStr)]string args,uint options,out uint pid);
    }
    static uint ActivateOrdinaryOfficial() {
        var manager=(IOrdinaryActivationManager)new OrdinaryActivationManager();
        try{uint pid;Marshal.ThrowExceptionForHR(manager.ActivateApplication(OfficialAppId,"",2,out pid));return pid;}
        finally{Marshal.ReleaseComObject(manager);}
    }
    static async void Begin(object sender,StartupEventArgs eventArgs) {
        ConfigChange change=null;Process launcher=null;bool launched=false,launchDispatchAttempted=false;string problem=null;Completion completion=Completion.Unconfirmed;
        var fallback=new OrdinaryFallbackAttempt();
        var receipt=Receipt("checking","正在校验本机壁纸组件");
        try {
            // The authoritative validation runs once under the maintenance gate below.
            await Task.Run(()=>{
                long gateBegan=StartupClock.ElapsedMilliseconds;
                TraceStartup("gate-requested");
                using(var maintenance=MaintenanceLaunchLease.Acquire(Root,2000)) {
                    TraceStartup("gate-acquired",gateBegan);
                    long validationBegan=StartupClock.ElapsedMilliseconds;
                    TraceStartup("validation-started");
                    MaintenanceLaunchLease.AssertInstallationReady(Root);
                    // Validate under the same installation gate used by update
                    // and direct entry, before configuration or process mutations.
                    Preflight(Root);
                    TraceStartup("validation-complete",validationBegan);
                    change=PrepareEnable(Root);change.Apply();
                    if(change.Changed)receipt["configBackup"]=change.Backup;
                    AssertNoRunningClient(Root,()=>BridgeNative.ExistingOfficialProcess(OfficialAppId));
                    long spawnBegan=StartupClock.ElapsedMilliseconds;
                    TraceStartup("launcher-start-requested");
                    launchDispatchAttempted=true;
                    launcher=Process.Start(new ProcessStartInfo(Path.Combine(Root,"Start-Codex-Dragon.exe")){WorkingDirectory=Root,UseShellExecute=true});
                    if(launcher==null)throw new InvalidOperationException("未能启动龙娘入口");
                    launched=true; // Successful process creation commits before fallible handle/ID access.
                    IntPtr retained=launcher.Handle; // Keep this exact generation until observation ends.
                    receipt["launcherPid"]=launcher.Id;
                    TraceStartup("launcher-spawned",spawnBegan);
                    // Child waits briefly for this gate; never await its UI or
                    // bridge handshake while holding the parent's ownership.
                }
            });
            var observed=await Task.Run(()=>Observe(Root,launcher));
            foreach(var item in observed)if(item.Key!="createdUtc"&&item.Key!="utc")receipt[item.Key]=item.Value;
            WriteReceipt(Root,receipt);
            completion=(bool)receipt["wallpaperPlaybackConfirmed"]?Completion.Confirmed:Completion.Unconfirmed;
        } catch(Exception error) {
            TraceStartup("failed");
            completion=HandleFailure(Root,error,launched,change,receipt,out problem);
        } finally {if(launcher!=null)try{launcher.Dispose();}catch{ }if(change!=null)try{change.Cleanup();}catch{ }}
        if(completion==Completion.Error)try {
                await fallback.RunAsync(launched,launchDispatchAttempted,completion,receipt,
                    ()=>MaintenanceLaunchLease.Acquire(Root,2000),
                    ()=>AssertNoRunningClient(Root,()=>BridgeNative.ExistingOfficialProcess(OfficialAppId)),
                    ActivateOrdinaryOfficial,phase=>TraceStartup(phase));
                WriteReceipt(Root,receipt);
            }catch { /* Optional fallback diagnostics cannot create another launch. */ }
        // Normal startup is silent even on failure. The receipt remains the
        // authority for diagnostics; silence never turns an error into success.
        PresentCompletion(completion,problem,delegate(string detail){
            Window notice=CreateProblemWindow(detail);
            notice.Closed+=delegate {Application.Current.Shutdown(2);};
            notice.Show();notice.Activate();
        },delegate {Application.Current.Shutdown(completion==Completion.Error?2:0);},diagnostics);
    }
    internal static void PresentCompletion(Completion completion,string detail,Action<string> showProblem,Action quietExit,bool showDiagnostics=false) {
        if(completion==Completion.Error&&showDiagnostics){showProblem(detail);return;}
        quietExit();
    }
    internal static Window CreateProblemWindow(string detail) {
        if(String.IsNullOrWhiteSpace(detail))throw new ArgumentException("A problem detail is required","detail");
        var panel=new StackPanel{Margin=new Thickness(28)};
        panel.Children.Add(new TextBlock{Text="壁纸启动需要处理",FontSize=23});
        panel.Children.Add(new TextBlock{Text=detail,FontSize=15,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,18,0,18)});
        var close=new Button{Content="关闭",Padding=new Thickness(15,7,15,7),HorizontalAlignment=HorizontalAlignment.Right};
        panel.Children.Add(close);
        var notice=new Window{Title="Codex 龙娘 · 壁纸启动提示",Content=panel,Width=470,SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterScreen};
        close.Click+=delegate {notice.Close();};
        return notice;
    }
    internal static BridgeStatus ReadObservation(BridgeStatus snapshot,Func<BridgeStatus> refresh) {
        // Discovery already fetched an authenticated status snapshot. During
        // preparation, the launcher owns active renderer probes; observation
        // must not enqueue a second CDP refresh behind every status request.
        return snapshot!=null&&!snapshot.Revealed?snapshot:refresh();
    }
    internal static Dictionary<string,object> Observe(string root) {
        return Observe(root,null);
    }
    internal static Dictionary<string,object> Observe(string root,Process launcher) {
        return ObserveCore(()=>BridgeActivation.Discover(root,0,null),
            ()=>launcher!=null&&BridgeNative.HasExited(launcher.Handle),35000);
    }
    internal static Dictionary<string,object> ObserveCore(Func<BridgeSession> discover,Func<bool> launcherExited,int timeoutMilliseconds) {
        if(discover==null||launcherExited==null||timeoutMilliseconds<1||timeoutMilliseconds>35000)throw new ArgumentException("Invalid observer bounds");
        var result=Receipt("unconfirmed","尚未取得壁纸会话 暂不能确认接入成功");
        var watch=Stopwatch.StartNew();BridgeStatus first=null,previous=null;BridgeSession boundHost=null;long launcherEndedAt=-1;
        try {while(watch.ElapsedMilliseconds<timeoutMilliseconds) {
            // Liveness is process-handle evidence, never a single missed IPC reply.
            // Keep a small grace for an activation worker that is still publishing.
            if(boundHost!=null&&boundHost.HostExited){result["stage"]="host-ended";result["detail"]="本次壁纸宿主已退出 停止等待";return result;}
            if(launcherExited()){
                if(launcherEndedAt<0)launcherEndedAt=watch.ElapsedMilliseconds;
                if(boundHost==null&&watch.ElapsedMilliseconds-launcherEndedAt>=2000){result["stage"]="launcher-ended";result["detail"]="启动动画已结束且未发现壁纸会话 停止等待";return result;}
            }
            BridgeSession session=discover();
            try {
                if(session!=null) {
                    try {
                        if(boundHost==null)boundHost=session;
                        var current=ReadObservation(session.LastStatus,()=>session.Refresh(1800));
                        result["hostPid"]=current.HostPid;result["codexPid"]=current.CodexPid;
                        result["hostCreationFileTime"]=current.HostCreated;result["codexCreationFileTime"]=current.CodexCreated;
                        result["layoutSupported"]=current.LayoutSupported;
                        result["mediaCode"]=current.MediaCode;result["refreshCode"]=current.RefreshCode;
                        result["diagnosticsVersion"]=current.DiagnosticsVersion;
                        result["attached"]=current.Attached;result["playing"]=current.Playing;
                        result["wallpaperApplied"]=current.WallpaperApplied;result["stageVisible"]=current.StageVisible;
                        result["firstFrameConfirmed"]=current.FirstFrameConfirmed;result["documentHidden"]=current.DocumentHidden;
                        result["probeOnly"]=current.ProbeOnly;result["mediaTimeMs"]=current.MediaTimeMs;
                        // Fixed, validated diagnostics only; no arbitrary page evaluation.
                        var verdict=WallpaperReadiness.Evaluate(previous,current);
                        result["detail"]=WallpaperReadiness.Describe(verdict);
                        result["stage"]=verdict.ToString();
                        if(WallpaperReadiness.ConfirmPlayback(first,previous,current)) {
                            result["stage"]="playback-progress-confirmed";
                            result["detail"]="已接入背景 并连续检测到视频进度";
                            result["firstMediaTimeMs"]=first.MediaTimeMs;
                            result["secondMediaTimeMs"]=previous.MediaTimeMs;
                            result["thirdMediaTimeMs"]=current.MediaTimeMs;
                            result["wallpaperPlaybackConfirmed"]=true;return result;
                        }
                        if(verdict==WallpaperReadinessState.Disabled || verdict==WallpaperReadinessState.Failed || verdict==WallpaperReadinessState.UnsupportedLayout || verdict==WallpaperReadinessState.Released) return result;
                        first=previous;previous=current;
                    }catch {first=null;previous=null;result["detail"]="状态查询暂未确认 客户端保持运行";}
                } else {first=null;previous=null;}
            }finally{if(session!=null&&!Object.ReferenceEquals(session,boundHost))session.Dispose();}
            Thread.Sleep(Math.Min(900,Math.Max(1,timeoutMilliseconds-(int)watch.ElapsedMilliseconds)));
        }return result;}finally{if(boundHost!=null)boundHost.Dispose();}
    }
    [STAThread] internal static int Main(string[] args) {
        using(var process=Process.GetCurrentProcess())setupPid=process.Id;
        TraceStartup("entered");
        bool created;
        using(var mutex=new Mutex(true,"Local\\CodexDragon.WallpaperSetup",out created)) {
            if(!created)return 3;
            if(args.Length==1&&args[0]=="--preflight-only") {
                try{using(var maintenance=MaintenanceLaunchLease.Acquire(Root,2000)){MaintenanceLaunchLease.AssertInstallationReady(Root);Preflight(Root);}WriteReceipt(Root,Receipt("preflight-passed","只检查 未启用或启动客户端"));return 0;}
                catch(Exception e){try{WriteReceipt(Root,Receipt("preflight-blocked",e.Message));}catch{}return 2;}
            }
            diagnostics=args.Length==1&&args[0]=="--diagnostics";
            if(args.Length!=0&&!diagnostics)return 64;
            // Explicit lifetime allows asynchronous validation/observation to
            // run without a progress window or a brief white taskbar surface.
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
            app.Startup+=Begin;return app.Run();
        }
    }
}
}

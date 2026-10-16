using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using System.Xml;

namespace DragonLauncher
{
    // This guard may only terminate the explicitly owning launcher process.
    // The production entry point supplies its action; library/tests do not.
    internal sealed class LauncherShutdownGuard : IDisposable
    {
        private readonly ManualResetEvent finished=new ManualResetEvent(false);
        private int state;
        private LauncherShutdownGuard(Action timeoutAction,int milliseconds)
        {
            var worker=new Thread(delegate() {
                try {if(!finished.WaitOne(milliseconds) && Interlocked.CompareExchange(ref state,2,0)==0)timeoutAction();}
                catch { }
                finally {finished.Dispose();}
            }){IsBackground=true,Name="Launcher media shutdown bound"};
            try{worker.Start();}catch{finished.Dispose();throw;}
        }
        internal static LauncherShutdownGuard Begin(Action timeoutAction,int milliseconds)
        {try{return timeoutAction==null?null:new LauncherShutdownGuard(timeoutAction,Math.Max(1,milliseconds));}catch{return null;}}
        public void Dispose()
        {if(Interlocked.CompareExchange(ref state,1,0)==0)try{finished.Set();}catch(ObjectDisposedException){}}
    }

    // Diagnostics must not perform filesystem work on the animation dispatcher.
    // A single background writer and a bounded queue also contain a stalled disk.
    internal static class LauncherDiagnostics
    {
        private sealed class Entry {internal string File,Text;}
        private static readonly object Gate=new object();
        private static readonly Queue<Entry> Entries=new Queue<Entry>();
        private static bool running;
        private static long dropped,writeFailures,workerFailures;
        private static long reportedDropped,reportedWriteFailures,reportedWorkerFailures;
        internal static long Dropped {get{return Interlocked.Read(ref dropped);}}
        internal static long WriteFailures {get{return Interlocked.Read(ref writeFailures);}}
        internal static long WorkerFailures {get{return Interlocked.Read(ref workerFailures);}}
        internal static void Write(string file,string text)
        {
            try {
                if(file!="handoff-latest.log" && file!="launcher-error.txt" && file!="launcher-media-error.txt")return;
                if(text==null)text="";if(text.Length>4096)text=text.Substring(0,4096);
                var entry=new Entry{File=file,Text=DateTime.UtcNow.ToString("o")+" "+text+"\n"};
                lock(Gate) {
                    if(Entries.Count>=128){Entries.Dequeue();Interlocked.Increment(ref dropped);}Entries.Enqueue(entry);
                    if(running)return;running=true;
                    try {new Thread(Drain){IsBackground=true,Name="Bounded launcher diagnostics"}.Start();}
                    catch {running=false;Interlocked.Increment(ref workerFailures);Interlocked.Add(ref dropped,Entries.Count);Entries.Clear();}
                }
            }catch {Interlocked.Increment(ref dropped);}
        }
        private static void Drain()
        {
            while(true) {
                Entry entry;
                lock(Gate){if(Entries.Count==0){running=false;return;}entry=Entries.Dequeue();}
                try {
                    string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"output");Directory.CreateDirectory(folder);
                    string path=Path.Combine(folder,entry.File);
                    if(File.Exists(path) && new FileInfo(path).Length>128*1024)File.WriteAllText(path,"");
                    long lost=Dropped,io=WriteFailures,worker=WorkerFailures;
                    if(lost!=reportedDropped||io!=reportedWriteFailures||worker!=reportedWorkerFailures){
                        File.AppendAllText(path,DateTime.UtcNow.ToString("o")+" diagnostics-counters dropped="+lost+" ioFailures="+io+" workerFailures="+worker+"\n");
                        reportedDropped=lost;reportedWriteFailures=io;reportedWorkerFailures=worker;
                    }
                    File.AppendAllText(path,entry.Text);
                }catch {Interlocked.Increment(ref writeFailures);}
            }
        }
    }

    public sealed class Settings
    {
        public double Duration = 8.0;
        public int FrameRate = 60;
        public string AppId = "OpenAI.Codex_2p2nqsd0c76g0!App";
        public string Exe = "", Arguments = "";
        public string Image = "assets/dragon-cinematic-open.png";
        public string ClosedImage = "assets/dragon-cinematic-closed.png";
        public string VideoFile = "output/gpt-dragon-cinematic-2k-60fps.mp4";
        public bool RespectReducedMotion = true;
        public bool SmoothHandoff = true;
        public bool WallpaperEnabled = true;
        public double HandoffAtSeconds = 7.60;
        public double HandoffFadeSeconds = 0.85;
        public double HandoffWaitSeconds = 6.0;
        public double PrewarmSeconds = 0.65;
        public string StartupIntroFile = "", StartupIdleFile = "", StartupFinaleFile = "";
        public double FinaleImpactSeconds = 382.0 / 144.0;
        public double StartupReadyTimeoutSeconds = 90.0;
        // Runtime-only: never written to launcher.config.xml. A replay borrows
        // an existing client, so it owns no wallpaper session or activation.
        internal ExistingReplayTarget ReplayTarget;
        internal Settings WithExistingReplay(ExistingReplayTarget target)
        {
            if(target==null)throw new ArgumentNullException("target");
            var copy=(Settings)MemberwiseClone();
            copy.ReplayTarget=target;copy.WallpaperEnabled=false;
            return copy;
        }
        public static Settings Load(string root)
        {
            var result = new Settings();
            var file = Path.Combine(root, "launcher.config.xml");
            if (!File.Exists(file)) return result;
            XElement xml;
            using(var reader=XmlReader.Create(file,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=65536}))xml=XDocument.Load(reader).Root;
            if (xml == null || xml.Name!="launcher") throw new InvalidDataException("启动配置根节点无效。");
            Func<string, string, string> value = (name, fallback) =>
                xml.Element(name) == null ? fallback : xml.Element(name).Value.Trim();
            result.Duration = double.Parse(value("durationSeconds", "8.0"), CultureInfo.InvariantCulture);
            if (double.IsNaN(result.Duration) || double.IsInfinity(result.Duration) || result.Duration < 1 || result.Duration > 30)
                throw new InvalidDataException("durationSeconds 必须为 1 到 30 秒。");
            result.AppId = value("appUserModelId", result.AppId);
            result.Exe = value("executablePath", "");
            result.Arguments = value("executableArguments", "");
            result.Image = value("characterImage", result.Image);
            result.ClosedImage = value("closedEyeImage", result.ClosedImage);
            result.VideoFile = value("videoFile", result.VideoFile);
            result.StartupIntroFile = value("startupIntroFile", "");
            result.StartupIdleFile = value("startupIdleFile", "");
            result.StartupFinaleFile = value("startupFinaleFile", "");
            result.FinaleImpactSeconds = ReadBounded(value("finaleImpactSeconds", "2.652777778"), 0.5, 10, "finaleImpactSeconds");
            result.StartupReadyTimeoutSeconds = ReadBounded(value("startupReadyTimeoutSeconds", "90"), 10, 90, "startupReadyTimeoutSeconds");
            int livingFiles = (result.StartupIntroFile.Length > 0 ? 1 : 0) + (result.StartupIdleFile.Length > 0 ? 1 : 0) + (result.StartupFinaleFile.Length > 0 ? 1 : 0);
            if (livingFiles != 0 && livingFiles != 3) throw new InvalidDataException("持续眨眼启动需要完整的开场 等待和落泪素材。");
            result.FrameRate = int.Parse(value("frameRate", "60"), CultureInfo.InvariantCulture);
            if (result.FrameRate < 1 || result.FrameRate > 60)
                throw new InvalidDataException("frameRate 必须为 1 到 60 帧/秒。");
            result.RespectReducedMotion = bool.Parse(value("respectReducedMotion", "true"));
            result.SmoothHandoff = bool.Parse(value("smoothHandoff", "true"));
            result.WallpaperEnabled = bool.Parse(value("wallpaperEnabled", "true"));
            result.HandoffAtSeconds = ReadBounded(value("handoffAtSeconds", "7.60"), 0.5, 60, "handoffAtSeconds");
            result.HandoffFadeSeconds = ReadBounded(value("handoffFadeSeconds", "0.85"), 0.2, 2.0, "handoffFadeSeconds");
            result.HandoffWaitSeconds = ReadBounded(value("handoffWaitSeconds", "6.0"), 0.5, 15.0, "handoffWaitSeconds");
            result.PrewarmSeconds = ReadBounded(value("prewarmSeconds", "0.65"), 0, 5, "prewarmSeconds");
            if (string.IsNullOrWhiteSpace(result.AppId) && string.IsNullOrWhiteSpace(result.Exe))
                throw new InvalidDataException("请配置 appUserModelId 或 executablePath。");
            return result;
        }
        private static double ReadBounded(string text, double min, double max, string name)
        {
            double number;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number) ||
                double.IsNaN(number) || double.IsInfinity(number) || number < min || number > max)
                throw new InvalidDataException(name + " 必须在 " + min + " 到 " + max + " 之间。");
            return number;
        }
    }

    public static class AppActivation
    {
        internal static uint OpenBounded(Settings settings,int milliseconds)
        {return OpenBoundedCore(delegate{return Open(settings);},milliseconds);}
        internal static uint OpenBoundedCore(Func<uint> activate,int milliseconds)
        {
            var completion=new TaskCompletionSource<uint>();
            var worker=new Thread(delegate() {
                try{completion.TrySetResult(activate());}catch(Exception error){completion.TrySetException(error);}
            }){IsBackground=true,Name="Bounded ordinary activation"};
            worker.SetApartmentState(ApartmentState.STA);worker.Start();
            // The worker is never aborted and its application is never killed.
            // A late exception is observed even after the bounded caller exits.
            completion.Task.ContinueWith(task=>{var ignored=task.Exception;},TaskContinuationOptions.OnlyOnFaulted);
            if(!completion.Task.Wait(Math.Max(1,milliseconds)))throw new TimeoutException("ordinary-activation-timeout");
            return completion.Task.GetAwaiter().GetResult();
        }
        [ComImport, Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
        private class ActivationManager { }
        [ComImport, Guid("2e941141-7f97-4756-ba1d-9decde894a3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IActivationManager
        {
            [PreserveSig] int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string id,
                [MarshalAs(UnmanagedType.LPWStr)] string args, uint options, out uint pid);
            [PreserveSig] int ActivateForFile([MarshalAs(UnmanagedType.LPWStr)] string id,
                IntPtr items, [MarshalAs(UnmanagedType.LPWStr)] string verb, out uint pid);
            [PreserveSig] int ActivateForProtocol([MarshalAs(UnmanagedType.LPWStr)] string id,
                IntPtr items, out uint pid);
        }
        public static uint Open(Settings settings)
        {
            // All fallback paths, including reduced motion and missing media,
            // must preserve replay ownership. A vanished client is not a new
            // launch request, and no replacement process may inherit this pin.
            if(settings.ReplayTarget!=null)return settings.ReplayTarget.Focus();
            if (!string.IsNullOrWhiteSpace(settings.Exe))
            {
                var exe = Environment.ExpandEnvironmentVariables(settings.Exe);
                if (!File.Exists(exe)) throw new FileNotFoundException("找不到配置的桌面应用，请检查 executablePath。", exe);
                using (var process = Process.Start(new ProcessStartInfo(exe, settings.Arguments) { UseShellExecute = true }))
                    return process == null ? 0 : (uint)process.Id;
            }
            var activation = (IActivationManager)new ActivationManager();
            try
            {
                uint pid;
                var result = activation.ActivateApplication(settings.AppId, "", 0, out pid);
                Marshal.ThrowExceptionForHR(result);
                return pid;
            }
            finally { Marshal.ReleaseComObject(activation); }
        }
    }

    public sealed class Splash : Window
    {
        private readonly Scene scene;
        private readonly Settings settings;
        private readonly bool preview;
        private readonly Stopwatch stopwatch = new Stopwatch();
        private bool done, closed;
        public Splash(Scene graphic, Settings config, bool onlyPreview)
        {
            scene = graphic; settings = config; preview = onlyPreview;
            Title = "GPT 龙娘 · Codex 启动";
            Width = Math.Min(Scene.DesignWidth, SystemParameters.WorkArea.Width * 0.9);
            Height = Width * Scene.DesignHeight / Scene.DesignWidth;
            if (Height > SystemParameters.WorkArea.Height * 0.9)
            { Height = SystemParameters.WorkArea.Height * 0.9; Width = Height * Scene.DesignWidth / Scene.DesignHeight; }
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true; Background = Brushes.Transparent;
            Topmost = true; ShowInTaskbar = false;
            Content = new System.Windows.Controls.Viewbox { Child = scene, Stretch = Stretch.Uniform };
            ContentRendered += delegate { stopwatch.Start(); CompositionTarget.Rendering += Tick; Activate(); };
            Closed += delegate { closed = true; done = true; CompositionTarget.Rendering -= Tick; stopwatch.Stop(); };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            { if (e.Key == Key.Escape || e.Key == Key.Enter || e.Key == Key.Space) { e.Handled = true; Finish(); } };
            MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                var p = e.GetPosition(scene);
                if (p.X > Scene.DesignWidth - 55 && p.Y < 67) { done = true; Close(); }
                else if (p.Y < 73) DragMove();
            };
        }
        private void Tick(object sender, EventArgs e)
        {
            scene.SetTime(stopwatch.Elapsed.TotalSeconds);
            if (stopwatch.Elapsed.TotalSeconds >= settings.Duration) Finish();
        }
        private void Finish()
        {
            if (done) return; done = true; CompositionTarget.Rendering -= Tick;
            if (preview) { Close(); return; }
            scene.Launching = true; scene.SetTime(settings.Duration - 0.3);
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (closed) return;
                Close();
                try { AppActivation.OpenBounded(settings,5000); }
                catch (Exception error)
                {
                    LauncherDiagnostics.Write("launcher-error.txt","Reduced-motion activation failed: "+error);
                }
            }), DispatcherPriority.Background);
        }
    }

    public static class Program
    {
        private static void RecordStartupStage(string stage, Stopwatch timer)
        {
            try { using(var current=Process.GetCurrentProcess())
                LauncherDiagnostics.Write("handoff-latest.log","startup-stage="+stage+" pid="+current.Id+" elapsedMs="+timer.ElapsedMilliseconds);
            } catch { }
        }
        private static void SaveFrame(Scene scene, double time, string path)
        {
            scene.SetTime(time); scene.UpdateLayout();
            // Render the vector scene directly at QHD. A VisualBrush scales
            // the scene before rasterization, rather than enlarging a 720p PNG.
            var capture = new DrawingVisual();
            using (var dc = capture.RenderOpen())
                dc.DrawRectangle(new VisualBrush(scene) { Stretch = Stretch.Fill }, null,
                    new Rect(0, 0, Scene.ExportWidth, Scene.ExportHeight));
            var bitmap = new RenderTargetBitmap(Scene.ExportWidth, Scene.ExportHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(capture);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(path)) encoder.Save(file);
        }
        [STAThread]
        public static int Main(string[] args)
        {
            if(NativeRestoreGuard.IsMode(args))return NativeRestoreGuard.Run(args);
            var startupTimer=Stopwatch.StartNew();
            RecordStartupStage("launcher-entry",startupTimer);
            try
            {
                var root = AppDomain.CurrentDomain.BaseDirectory;
                using(var maintenance=MaintenanceLaunchLease.Acquire(root,2000)) {
                MaintenanceLaunchLease.AssertInstallationReady(root);
                var config = Settings.Load(root);
                RecordStartupStage("launcher-validated",startupTimer);
                bool previewTest = Array.IndexOf(args, "--preview-test") >= 0;
                bool preview = Array.IndexOf(args, "--preview") >= 0 || previewTest;
                bool reduced = Array.IndexOf(args, "--reduced-motion") >= 0 || (config.RespectReducedMotion && !SystemParameters.ClientAreaAnimation);
                int render = Array.IndexOf(args, "--render");
                bool selfTest = Array.IndexOf(args, "--self-test") >= 0;
                bool eyeTest = Array.IndexOf(args, "--eye-test") >= 0;
                bool lineTest = Array.IndexOf(args, "--line-test") >= 0;
                bool replayExisting = Array.IndexOf(args, "--replay-existing") >= 0;
                if(replayExisting&&(preview||render>=0||selfTest||eyeTest||lineTest||Array.IndexOf(args,"--activation-test")>=0))
                    throw new ArgumentException("Existing-client replay cannot be combined with preview or diagnostic modes.");
                if (Array.IndexOf(args, "--activation-test") >= 0)
                {
                    uint pid = AppActivation.Open(config);
                    var logFolder = Path.Combine(root, "output");
                    Directory.CreateDirectory(logFolder);
                    File.WriteAllText(Path.Combine(logFolder, "activation-test.txt"), "PASS: desktop app activation accepted. PID: " + pid);
                    return 0;
                }
                if (render >= 0 || selfTest || eyeTest || lineTest)
                {
                    var scene = new Scene(root, config, false);
                    if (render >= 0 && render + 1 >= args.Length) throw new ArgumentException("--render 后需要指定输出目录。");
                    string folder = selfTest ? Path.Combine(root, "output", "self-test") : eyeTest ? Path.Combine(root, "output", "eye-test") : lineTest ? Path.Combine(root, "output", "line-test") : Path.GetFullPath(args[render + 1]);
                    Directory.CreateDirectory(folder);
                    scene.Measure(new Size(Scene.DesignWidth, Scene.DesignHeight));
                    scene.Arrange(new Rect(0, 0, Scene.DesignWidth, Scene.DesignHeight));
                    scene.UpdateLayout();
                    if (lineTest)
                    {
                        double[] samples = { 0.15, 0.30, 0.45, 0.60, 0.85, 1.10, 1.35, 1.60, 1.90, 2.15, 2.40, 2.90 };
                        string report = "Directional light-path samples rendered for visual inspection. App activation was not executed.\n";
                        for (int i = 0; i < samples.Length; i++)
                        {
                            double seconds = samples[i] * config.Duration / 8;
                            SaveFrame(scene, seconds, Path.Combine(folder, "line-" + i.ToString("D2") + ".png"));
                            report += scene.LineSample(seconds) + "\n";
                        }
                        File.WriteAllText(Path.Combine(folder, "result.txt"), report);
                    }
                    else if (eyeTest)
                    {
                        for (int i = 0; i <= 12; i++)
                            SaveFrame(scene, (3.25 + i * 0.125) * config.Duration / 8, Path.Combine(folder, "eye-" + i.ToString("D2") + ".png"));
                        File.WriteAllText(Path.Combine(folder, "result.txt"), "PASS: 13 samples across the complete eyelid motion rendered for visual inspection. App activation was not executed.");
                    }
                    else if (selfTest)
                    {
                        SaveFrame(scene, config.Duration * 0.2, Path.Combine(folder, "wireframe.png"));
                        SaveFrame(scene, config.Duration * 0.4, Path.Combine(folder, "materialize.png"));
                        SaveFrame(scene, config.Duration * 0.7, Path.Combine(folder, "scene.png"));
                        File.WriteAllText(Path.Combine(folder, "result.txt"), "PASS: config, both eye textures, WPF wireframe/materialize/awake rendering. App activation was not executed.\nImage: " + config.Image + "\nFPS: " + config.FrameRate);
                    }
                    else
                    {
                        int frames = (int)Math.Ceiling(config.Duration * config.FrameRate);
                        for (int i = 0; i < frames; i++) SaveFrame(scene, i / (double)config.FrameRate, Path.Combine(folder, "frame-" + i.ToString("D4") + ".png"));
                        SaveFrame(scene, config.Duration * 0.7, Path.Combine(folder, "poster.png"));
                    }
                    return 0;
                }
                bool acquired;
                using (var mutex = new Mutex(true, "Local\\GPTDragonCodexLauncher", out acquired))
                {
                    if (!acquired)
                    {
                        if (previewTest) throw new InvalidOperationException("已有启动器正在运行，无法执行独立播放验证。");
                        return 0;
                    }
                    try
                    {
                        if(replayExisting){
                            var existing=ExistingReplayTarget.Find(config,root);
                            if(existing==null){RecordStartupStage("replay-target-unavailable-no-activation",startupTimer);return 0;}
                            config=config.WithExistingReplay(existing);
                            RecordStartupStage("replay-existing-window-pinned",startupTimer);
                        }
                        string film = Path.GetFullPath(Path.Combine(root, config.VideoFile));
                        if (!reduced || previewTest)
                        {
                            if (!File.Exists(film))
                            {
                                if (preview) throw new FileNotFoundException("启动视频不存在，请恢复原 MP4 文件。", film);
                                // A missing packaged film must not trigger expensive realtime
                                // contour generation or prevent access to the target application.
                                try
                                {
                                    LauncherDiagnostics.Write("launcher-media-error.txt","Missing startup film; animation skipped and application activation attempted.\nVideo: " + film);
                                }
                                catch { }
                                AppActivation.OpenBounded(config,5000);
                            }
                            else
                            {
                                // Only this standalone normal-startup entry may
                                // fail-stop itself after native state is restored.
                                RecordStartupStage("launcher-create-film",startupTimer);
                                var window = new FilmSplash(film, config, preview, previewTest,
                                    preview ? (Action)null : delegate {Environment.Exit(2);});
                                RecordStartupStage("launcher-show-film",startupTimer);
                                new Application { ShutdownMode = ShutdownMode.OnMainWindowClose }.Run(window);
                                if (previewTest && !window.PlaybackSucceeded) return 1;
                            }
                        }
                        else
                        {
                            Scene graphic;
                            try { graphic = new Scene(root, config, true); }
                            catch (Exception error)
                            {
                                // A damaged optional still-scene asset must not
                                // prevent normal access to the target app. Keep
                                // this catch around construction only: a later
                                // UI failure must never cause double activation.
                                if (preview) throw;
                                try
                                {
                                    LauncherDiagnostics.Write("launcher-media-error.txt","Reduced-motion scene could not load; animation skipped and application activation attempted.\n" + error);
                                }
                                catch { }
                                AppActivation.OpenBounded(config,5000);
                                return 0;
                            }
                            new Application { ShutdownMode = ShutdownMode.OnMainWindowClose }.Run(new Splash(graphic, config, preview));
                        }
                    }
                    finally { mutex.ReleaseMutex(); }
                }
                return 0;
                }
            }
            catch (Exception error)
            {
                LauncherDiagnostics.Write("launcher-error.txt",error.ToString());
                // Automatic startup is optional decoration. Diagnostics are
                // available in logs; only an explicit interactive preview may
                // show an error dialog and hold up the user.
                if(Array.IndexOf(args,"--preview")>=0 && Array.IndexOf(args,"--preview-test")<0)
                    MessageBox.Show(error.Message, "GPT 龙娘启动器", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
        }
    }
}

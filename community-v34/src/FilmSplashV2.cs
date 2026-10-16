using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DragonLauncher
{
    // The approved movie owns the portrait, text layout and handoff. This small
    // vector layer modulates existing letter shapes and draws sparse signals in
    // the left negative space: no opaque panels, extra video surfaces, per-frame
    // text shaping, telemetry claims, or persistent wallpaper work.
    internal sealed class TerminalTypography : FrameworkElement, IDisposable
    {
        private readonly Geometry header, greeting, english, title;
        private readonly Geometry[] letters;
        private readonly Brush cyan, white, violet, ink, sweep;
        private readonly Pen halo, edge, fine;
        private readonly Geometry[] microRows;
        private readonly Geometry signalRoute, registrationMarks, signalNodes;
        private readonly Brush mint;
        private readonly Pen routePen, markPen, mintPen;
        private readonly Point[] signalPoints;
        private readonly double[] signalLengths;
        private readonly double signalTotal;
        private Func<double> mediaPosition;
        private readonly Stopwatch rhythm = new Stopwatch();
        private bool disposed, subscribed;
        private double lastFrame = -1, story, seconds;
        internal bool IsRunning { get { return subscribed; } }

        internal TerminalTypography(string root)
        {
            Width=1280;Height=720;IsHitTestVisible=false;Focusable=false;ClipToBounds=true;
            var mono=new Typeface("Consolas");
            var display=new Typeface(new FontFamily("DengXian, Microsoft YaHei UI"),FontStyles.Normal,FontWeights.Bold,FontStretches.Normal);
            string fontFolder=Path.GetFullPath(Path.Combine(root,"assets","fonts"))+Path.DirectorySeparatorChar;
            if(!File.Exists(Path.Combine(fontFolder,"LXGWWenKaiTC-Light.ttf")))throw new FileNotFoundException("Missing typography font");
            var chinese=new Typeface(new FontFamily(new Uri(fontFolder),"./#LXGW WenKai TC"),FontStyles.Normal,FontWeights.Light,FontStretches.Normal);
            GlyphTypeface glyph;
            if(!chinese.TryGetGlyphTypeface(out glyph))throw new InvalidDataException("Typography font unavailable");
            header=TextGeometry("EXPLORE WITH REASON   RESPOND WITH KINDNESS",mono,13.5,53,24,1);
            letters=TrackedLetters("Codex",display,132,64,249,0.3);title=Join(letters);
            greeting=Join(TrackedLetters("盡情瘋狂吧　我的架構師",chinese,25,87,430,3));
            english=Join(TrackedLetters("GO WILD  MY ARCHITECT",display,16,88,468,1));
            cyan=Solid(116,238,255);white=Solid(225,255,255);violet=Solid(177,163,255);ink=Solid(3,14,18);
            halo=new Pen(cyan,3.8);halo.Freeze();edge=new Pen(cyan,0.75);edge.Freeze();fine=new Pen(cyan,0.65);fine.Freeze();
            var gradient=new LinearGradientBrush();
            gradient.GradientStops.Add(new GradientStop(Colors.Transparent,0));
            gradient.GradientStops.Add(new GradientStop(Color.FromArgb(145,113,235,255),0.36));
            gradient.GradientStops.Add(new GradientStop(Color.FromArgb(245,230,255,255),0.52));
            gradient.GradientStops.Add(new GradientStop(Colors.Transparent,1));gradient.Freeze();sweep=gradient;
            // These fragments are graphic texture, not status or load estimates.
            // All shaping/path construction is completed once before rendering.
            microRows=new Geometry[] {
                TextGeometry("0x4C  :  1011 0010  /  0x91",mono,10.5,154,110,1),
                TextGeometry("[ 0101 : 1100 ]  < 0011 >",mono,10.5,154,126,1),
                TextGeometry("0x2A  /  0010 1101  :  0xE7",mono,10.5,154,142,1)
            };
            mint=Solid(117,245,181);
            routePen=new Pen(cyan,0.65);routePen.Freeze();
            markPen=new Pen(cyan,0.6);markPen.Freeze();
            mintPen=new Pen(mint,1.15);mintPen.StartLineCap=PenLineCap.Round;mintPen.EndLineCap=PenLineCap.Round;mintPen.Freeze();
            signalPoints=new Point[] {new Point(82,610),new Point(176,610),new Point(188,598),new Point(278,598),new Point(290,610),new Point(420,610)};
            signalLengths=new double[signalPoints.Length-1];
            double total=0;
            for(int i=0;i<signalLengths.Length;i++){signalLengths[i]=(signalPoints[i+1]-signalPoints[i]).Length;total+=signalLengths[i];}
            signalTotal=total;
            var route=new StreamGeometry();
            using(var ctx=route.Open()){
                ctx.BeginFigure(signalPoints[0],false,false);
                for(int i=1;i<signalPoints.Length;i++)ctx.LineTo(signalPoints[i],true,false);
            }
            route.Freeze();signalRoute=route;
            var marks=new StreamGeometry();
            using(var ctx=marks.Open()){
                double[] anchors=new double[] {193,402,625};
                for(int i=0;i<anchors.Length;i++){
                    double y=anchors[i];
                    ctx.BeginFigure(new Point(34,y),false,false);ctx.LineTo(new Point(27,y),true,false);ctx.LineTo(new Point(27,y+17),true,false);
                    ctx.BeginFigure(new Point(27,y+24),false,false);ctx.LineTo(new Point(31,y+24),true,false);
                }
            }
            marks.Freeze();registrationMarks=marks;
            var nodes=new GeometryGroup();
            nodes.Children.Add(new EllipseGeometry(signalPoints[0],1.45,1.45));
            nodes.Children.Add(new EllipseGeometry(signalPoints[3],1.45,1.45));
            nodes.Children.Add(new EllipseGeometry(signalPoints[5],1.45,1.45));
            nodes.Freeze();signalNodes=nodes;
        }
        private static Brush Solid(byte r,byte g,byte b){var bsh=new SolidColorBrush(Color.FromRgb(r,g,b));bsh.Freeze();return bsh;}
        private static Geometry TextGeometry(string text,Typeface face,double size,double x,double y,double dpi)
        {
            var shape=new FormattedText(text,System.Globalization.CultureInfo.GetCultureInfo("zh-CN"),FlowDirection.LeftToRight,face,size,Brushes.White,dpi).BuildGeometry(new Point(x,y));
            shape.Freeze();return shape;
        }
        private static Geometry[] TrackedLetters(string text,Typeface face,double size,double x,double y,double tracking)
        {
            var shapes=new Geometry[text.Length];
            for(int i=0;i<text.Length;i++){
                var formatted=new FormattedText(text[i].ToString(),System.Globalization.CultureInfo.GetCultureInfo("zh-CN"),FlowDirection.LeftToRight,face,size,Brushes.White,2);
                shapes[i]=formatted.BuildGeometry(new Point(x,y));shapes[i].Freeze();x+=formatted.WidthIncludingTrailingWhitespace+tracking;
            }
            return shapes;
        }
        private static Geometry Join(Geometry[] shapes){var group=new GeometryGroup();foreach(var shape in shapes)group.Children.Add(shape);group.Freeze();return group;}
        private static double Clamp(double value){return Math.Max(0,Math.Min(1,value));}
        private static double Smooth(double value){value=Clamp(value);return value*value*(3-2*value);}
        private static double Pulse(double value,double start,double width){if(value<=start||value>=start+width)return 0;double s=Math.Sin((value-start)/width*Math.PI);return s*s;}
        // A composed short double pulse, not a uniform on/off blink. Nothing
        // completely erases a word and the intact movie remains underneath.
        private static double DoublePulse(double phase){return Pulse(phase,0.04,0.10)+0.64*Pulse(phase,0.22,0.14);}
        internal void Start(Func<double> readPosition)
        {
            if(disposed||subscribed)return;
            if(readPosition==null)throw new ArgumentNullException("readPosition");
            mediaPosition=readPosition;rhythm.Start();subscribed=true;CompositionTarget.Rendering+=OnFrame;
        }
        private void OnFrame(object sender,EventArgs args)
        {
            if(disposed||!subscribed||!IsVisible)return;
            double now=rhythm.Elapsed.TotalSeconds;
            if(lastFrame>=0&&now-lastFrame<1.0/144)return;
            lastFrame=now;
            try { SetFrame(mediaPosition(),now); }
            catch { Dispose(); } // Cosmetic failure must never interrupt launch.
        }
        internal void SetFrame(double mediaSeconds,double effectSeconds)
        {
            if(disposed)return;
            story=Double.IsNaN(mediaSeconds)||Double.IsInfinity(mediaSeconds)?0:Math.Max(0,mediaSeconds);
            seconds=Double.IsNaN(effectSeconds)||Double.IsInfinity(effectSeconds)?0:Math.Max(0,effectSeconds);
            InvalidateVisual();
        }
        protected override void OnRender(DrawingContext dc)
        {
            if(disposed||story<=0)return;
            // Fully settle before the existing baked impact snapshot is used.
            double intensity=Smooth((story-0.18)/0.55)*(1-Smooth((story-6.75)/0.55));
            if(intensity<=0)return;
            dc.PushOpacity(intensity);
            DrawHeader(dc);
            DrawObservatory(dc);
            double visible=Smooth((story-4.25)/0.5);
            if(visible>0){
                dc.PushOpacity(visible);DrawTitle(dc);DrawGreeting(dc);dc.Pop();
            }
            dc.Pop();
        }
        private void DrawObservatory(DrawingContext dc)
        {
            double phase=seconds%6.8;
            dc.PushOpacity(0.16);dc.DrawGeometry(null,markPen,registrationMarks);dc.Pop();
            dc.PushOpacity(0.18);dc.DrawGeometry(null,routePen,signalRoute);dc.Pop();
            dc.PushOpacity(0.18);dc.DrawGeometry(mint,null,signalNodes);dc.Pop();
            for(int i=0;i<microRows.Length;i++){
                double acknowledge=Pulse(phase,0.68+i*0.23,0.42);
                dc.PushOpacity(0.20+0.08*acknowledge);dc.DrawGeometry(cyan,null,microRows[i]);dc.Pop();
                dc.PushOpacity(0.21+0.36*acknowledge);
                dc.DrawRectangle(mint,null,new Rect(145,114+i*16,2.4,3.6));dc.Pop();
                if(acknowledge>0){
                    dc.PushOpacity(0.34*acknowledge);dc.DrawLine(mintPen,new Point(154,123+i*16),new Point(178,123+i*16));dc.Pop();
                }
            }
            double travel=(phase-2.10)/1.45;
            if(travel>0&&travel<1){
                double fade=Smooth(travel/0.12)*(1-Smooth((travel-0.85)/0.15));
                double distance=Smooth(travel)*signalTotal;
                // A short packet is sampled along the polyline; the constant
                // maximum draw count does not grow with frame rate or wait time.
                dc.PushOpacity(0.58*fade);
                for(int i=0;i<5;i++){
                    double d=distance-i*3.5;
                    if(d<0)break;
                    Point p=SignalPoint(d);
                    dc.PushOpacity(1-i*0.16);dc.DrawEllipse(mint,null,p,i==0?1.65:0.85,i==0?1.65:0.85);dc.Pop();
                }
                dc.Pop();
            }
        }
        private Point SignalPoint(double distance)
        {
            distance=Math.Max(0,Math.Min(signalTotal,distance));
            for(int i=0;i<signalLengths.Length;i++){
                if(distance<=signalLengths[i]){
                    double ratio=distance/signalLengths[i];
                    return signalPoints[i]+(signalPoints[i+1]-signalPoints[i])*ratio;
                }
                distance-=signalLengths[i];
            }
            return signalPoints[signalPoints.Length-1];
        }
        private void DrawHeader(DrawingContext dc)
        {
            double cycle=Math.Floor(seconds/4.7),phase=seconds-cycle*4.7;
            double signal=DoublePulse(phase-0.65);
            // Three word groups acknowledge in order, leaving the others legible.
            for(int i=0;i<3;i++){
                double p=DoublePulse(phase-0.65-i*0.12);
                if(p<=0)continue;
                dc.PushClip(new RectangleGeometry(new Rect(53+i*110,22,110,20)));
                dc.PushOpacity(0.52*p);dc.DrawGeometry(ink,null,header);dc.Pop();
                dc.PushOpacity(0.6*p);dc.PushTransform(new TranslateTransform(0.6*p,0));dc.DrawGeometry(cyan,null,header);dc.Pop();dc.Pop();dc.Pop();
            }
            double scan=phase-1.22;
            if(scan>=0&&scan<0.75){
                dc.PushClip(header);dc.DrawRectangle(sweep,null,new Rect(20+scan/0.75*390,22,64,20));dc.Pop();
            }
            if(signal>0){
                dc.PushOpacity(0.55*signal);
                dc.DrawLine(fine,new Point(386,34),new Point(414,34));
                dc.DrawRectangle(white,null,new Rect(421,32.7,3,2.2));
                dc.DrawRectangle(cyan,null,new Rect(428,32.7,9,2.2));dc.Pop();
            }
        }
        private void DrawTitle(DrawingContext dc)
        {
            double t=Math.Max(0,seconds-4.65),cycle=Math.Floor(t/5.4),phase=t-cycle*5.4;
            double signal=DoublePulse(phase);
            Geometry selected=letters[((int)(cycle%letters.Length)+1)%letters.Length];
            if(signal>0){
                dc.PushOpacity(0.63*signal);dc.DrawGeometry(ink,null,selected);dc.Pop();
                dc.PushOpacity(0.11*signal);dc.DrawGeometry(null,halo,title);dc.Pop();
                // One thin displaced scan slice is enough to suggest a signal
                // relocking. It never moves the whole word or its background.
                dc.PushClip(new RectangleGeometry(new Rect(60,306+cycle%3*12,410,2.4)));
                dc.PushTransform(new TranslateTransform(1.8*signal,0));
                dc.PushOpacity(0.55*signal);dc.DrawGeometry(violet,null,title);dc.Pop();dc.Pop();
                dc.PushTransform(new TranslateTransform(-0.8*signal,0));
                dc.PushOpacity(0.75*signal);dc.DrawGeometry(cyan,null,title);dc.Pop();dc.Pop();dc.Pop();
            }
            double recovery=Pulse(phase,0.37,0.55);
            if(recovery>0){
                dc.PushOpacity(0.085*recovery);dc.DrawGeometry(null,halo,title);dc.Pop();
                dc.PushOpacity(0.48*recovery);dc.DrawGeometry(null,edge,title);dc.Pop();
                dc.PushOpacity(0.32*recovery);dc.DrawGeometry(white,null,selected);dc.Pop();
            }
            double scan=phase-0.42;
            if(scan>=0&&scan<1.05){
                dc.PushClip(title);dc.PushOpacity(0.85);
                dc.DrawRectangle(sweep,null,new Rect(20+scan/1.05*450,260,72,111));dc.Pop();dc.Pop();
            }
        }
        private void DrawGreeting(DrawingContext dc)
        {
            double t=Math.Max(0,seconds-4.9),phase=t%4.3;
            double pulse=DoublePulse(phase-0.45);
            if(pulse>0){
                dc.PushClip(new RectangleGeometry(new Rect(87,430,155,31)));
                dc.PushOpacity(0.45*pulse);dc.DrawGeometry(ink,null,greeting);dc.Pop();dc.Pop();
                dc.PushOpacity(0.48*pulse);dc.DrawGeometry(null,edge,greeting);dc.Pop();
            }
            double scan=phase-0.88;
            if(scan>=0&&scan<0.85){
                dc.PushClip(greeting);dc.DrawRectangle(sweep,null,new Rect(50+scan/0.85*370,430,58,33));dc.Pop();
                dc.PushOpacity(0.45);dc.PushClip(english);dc.DrawRectangle(sweep,null,new Rect(50+scan/0.85*280,468,46,22));dc.Pop();dc.Pop();
            }
            // Small packets travel along the existing separator, then settle.
            double packet=Pulse(phase,0.76,0.9);
            if(packet>0){
                double x=82+Smooth((phase-0.76)/0.9)*280;
                dc.PushOpacity(packet*0.7);dc.DrawRectangle(cyan,null,new Rect(x,414.4,18,1.1));
                dc.DrawRectangle(white,null,new Rect(x+22,414.4,3,1.1));dc.Pop();
            }
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            if(subscribed){CompositionTarget.Rendering-=OnFrame;subscribed=false;}
            rhythm.Stop();mediaPosition=null;InvalidateVisual();
        }
    }

    // Two hardware video surfaces. Loading never changes playback speed or
    // holds a blink: intro -> normally playing idle -> one prepared finale.
    internal sealed class LivingFilmPlayback : Grid, IDisposable
    {
        private sealed class Slot
        {
            internal MediaElement Media;
            internal string Role;
            internal bool Opened, Ended, Closed;
            internal int RenderCycles;
            internal double OpenedAt, Duration, LastPosition, LastAdvance;
            internal RoutedEventHandler OpenHandler, EndHandler;
            internal EventHandler<ExceptionRoutedEventArgs> FailHandler;
        }
        private readonly string introPath, idlePath, finalePath;
        private readonly Action<string> log, fail;
        private readonly Stopwatch clock = new Stopwatch();
        private Slot active, next, retiring;
        private bool disposed, started, finalizing, handoffAcknowledged;
        private int loops;
        private double switchAt=-1;
        internal bool InIdle { get { return active!=null && active.Role=="idle"; } }
        internal bool InFinale { get { return active!=null && active.Role=="finale"; } }
        internal double FinalePosition { get { return InFinale ? active.Media.Position.TotalSeconds : 0; } }
        internal double TypographyPosition { get { return active==null||!active.Opened?0:active.Role=="intro"?active.Media.Position.TotalSeconds:active.Role=="idle"?4.75:4.75+active.Media.Position.TotalSeconds; } }
        internal bool FinaleReady { get { return InIdle && Warm(next) && next.Role=="finale"; } }
        internal LivingFilmPlayback(string root,Settings config,Action<string> logger,Action<string> failure)
        {
            log=logger;fail=failure;ClipToBounds=true;Background=Brushes.Black;IsHitTestVisible=false;
            introPath=Resolve(root,config.StartupIntroFile);idlePath=Resolve(root,config.StartupIdleFile);finalePath=Resolve(root,config.StartupFinaleFile);
        }
        private static string Resolve(string root,string relative)
        {
            string path=Path.GetFullPath(Path.Combine(root,relative));
            if(!File.Exists(path))throw new FileNotFoundException("缺少持续眨眼启动素材",path);
            return path;
        }
        internal void Start()
        {
            if(started || disposed)return;started=true;clock.Start();
            CompositionTarget.Rendering+=OnRenderCycle;
            active=Create(introPath,"intro",true);next=Create(idlePath,"idle",false);
            log("Living intro started; speed=1; maximumVideoSurfaces=2");
        }
        private Slot Create(string path,string role,bool front)
        {
            var slot=new Slot {Role=role,LastAdvance=clock.Elapsed.TotalSeconds};
            slot.Media=new MediaElement {LoadedBehavior=MediaState.Manual,UnloadedBehavior=MediaState.Close,
                Stretch=Stretch.Uniform,IsMuted=true,Volume=0,IsHitTestVisible=false,ScrubbingEnabled=true,SpeedRatio=1};
            slot.OpenHandler=delegate {
                if(disposed || slot.Closed)return;
                slot.Opened=true;slot.OpenedAt=clock.Elapsed.TotalSeconds;
                slot.LastAdvance=slot.OpenedAt;slot.LastPosition=slot.Media.Position.TotalSeconds;
                slot.Duration=slot.Media.NaturalDuration.HasTimeSpan?slot.Media.NaturalDuration.TimeSpan.TotalSeconds:0;
                if(slot.Duration<0.1 || slot.Duration>30 || slot.Media.NaturalVideoWidth<1){fail("启动素材时长或尺寸无效");return;}
                if(slot!=active){slot.Media.Pause();slot.Media.Position=TimeSpan.Zero;}
                log("Living media opened "+slot.Role+"; duration="+slot.Duration.ToString("F3",System.Globalization.CultureInfo.InvariantCulture));
            };
            slot.EndHandler=delegate {
                if(disposed || slot.Closed || slot!=active)return;
                slot.Ended=true;
                if(slot.Role=="idle"){
                    // Identical neutral boundary frames hide decoder seek time.
                    // Never rewind a partially closed eye or slow a blink.
                    slot.Media.Position=TimeSpan.Zero;slot.Media.Play();slot.Ended=false;
                    slot.LastPosition=0;slot.LastAdvance=clock.Elapsed.TotalSeconds;
                    log("Living idle loop "+(++loops)+"; speed=1");
                } else if(slot.Role=="finale" && !handoffAcknowledged)fail("落泪结束前未完成客户端交接");
            };
            slot.FailHandler=delegate(object sender,ExceptionRoutedEventArgs args){if(!disposed && !slot.Closed)fail("启动素材解码失败 "+slot.Role);};
            slot.Media.MediaOpened+=slot.OpenHandler;slot.Media.MediaEnded+=slot.EndHandler;slot.Media.MediaFailed+=slot.FailHandler;
            Children.Add(slot.Media);SetZIndex(slot.Media,front?2:0);
            slot.Media.Source=new Uri(path);slot.Media.Play();return slot;
        }
        private bool Warm(Slot slot)
        {
            return slot!=null && !slot.Closed && slot.Opened && slot.RenderCycles>=2 &&
                clock.Elapsed.TotalSeconds-slot.OpenedAt>=0.08 && slot.Media.NaturalVideoWidth>0;
        }
        private void OnRenderCycle(object sender,EventArgs args)
        { if(!disposed && next!=null && next.Opened && next.RenderCycles<4)next.RenderCycles++; }
        internal void Tick()
        {
            if(disposed || !started)return;
            double now=clock.Elapsed.TotalSeconds;
            if(retiring!=null && switchAt>=0 && now-switchAt>=0.16){
                CloseSlot(retiring);retiring=null;switchAt=-1;
                if(InIdle && next==null)next=Create(finalePath,"finale",false);
            }
            if(active==null)return;
            double position=active.Media.Position.TotalSeconds;
            if(position>active.LastPosition+0.003 || position<active.LastPosition-0.1){active.LastPosition=position;active.LastAdvance=now;}
            if(!active.Opened && now>10){fail("启动视频解码器未能就绪");return;}
            if(active.Role=="intro" && active.Ended && Warm(next)){SwitchToNext();log("Living idle entered; speed=1");}
            if(active.Opened && now-active.LastAdvance>5){fail("启动视频解码停止超过五秒");return;}
            if(next!=null && !next.Opened && now-active.OpenedAt>15)fail("下一段启动视频未能加载");
        }
        internal bool TryStartFinale()
        {
            if(disposed || finalizing || retiring!=null || !FinaleReady)return false;
            double position=active.Media.Position.TotalSeconds;
            // Let a natural blink finish before the matched open-eye finale.
            // Authored four-second loop: blink 1.35..1.73, with a short guard.
            if(position>=1.25 && position<=1.85)return false;
            finalizing=true;SwitchToNext();return true;
        }
        internal void HandoffStarted(){handoffAcknowledged=true;}
        private void SwitchToNext()
        {
            if(disposed || !Warm(next) || retiring!=null)return;
            retiring=active;active=next;next=null;
            active.Media.Position=TimeSpan.Zero;active.Media.Play();active.LastAdvance=clock.Elapsed.TotalSeconds;active.LastPosition=0;
            SetZIndex(active.Media,1);SetZIndex(retiring.Media,2);
            // All three assets share framing and the neutral boundary image.
            // The old video keeps moving during this short compositor blend.
            retiring.Media.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(1,0,TimeSpan.FromMilliseconds(120)));
            switchAt=clock.Elapsed.TotalSeconds;
        }
        private void CloseSlot(Slot slot)
        {
            if(slot==null || slot.Closed)return;slot.Closed=true;
            slot.Media.MediaOpened-=slot.OpenHandler;slot.Media.MediaEnded-=slot.EndHandler;slot.Media.MediaFailed-=slot.FailHandler;
            slot.Media.BeginAnimation(UIElement.OpacityProperty,null);
            try{slot.Media.Close();slot.Media.Source=null;}catch{}
            Children.Remove(slot.Media);
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;CompositionTarget.Rendering-=OnRenderCycle;
            CloseSlot(active);CloseSlot(next);CloseSlot(retiring);active=next=retiring=null;clock.Stop();
            log("Living playback released; videoSurfaces=0");
        }
    }

    public sealed class FilmSplash : Window
    {
        private readonly string filmPath;
        private readonly Settings settings;
        private readonly bool preview, quietErrors;
        private readonly Action emergencyExit;
        private NativeRestoreGuard recoveryGuard;
        private StartupResourceBreaker resourceBreaker;
        private int resourceTrip;
        private readonly MediaElement player;
        private readonly LivingFilmPlayback living;
        private readonly Grid surface;
        private readonly TerminalTypography typography;
        private readonly Button cancel;
        private readonly DispatcherTimer watchdog, handoffTimer, recoveryHeartbeat;
        private readonly Stopwatch clock = new Stopwatch(), playbackClock = new Stopwatch(), handoffClock = new Stopwatch();
        private bool done, cleaned, opened, playbackReleased, transitioning, activationRequested, activationFinished;
        private bool shieldReady, pulseRendered, pulseLayoutConfigured, revealStarted, targetRaised, targetPreparationFailed, targetFocusPrepared;
        private bool pulsePreparationAttempted, pulsePreparationFailed, observingImpact;
        private double duration, lastPosition, lastProgress, lastTargetPoll = -1, readySince = -1;
        private double targetFocusPreparedAt;
        private uint activatedPid;
        private IntPtr target;
        private Exception activationError;
        private PulseReveal pulse;
        private IDisposable suppressedTransitions;
        private NativeHandoff.CompactPlacement targetPlacement;
        private NativeHandoff.KeyboardGuard inputGuard;
        private bool inputGuardFailed;
        private BridgeSession wallpaperSession;
        private readonly StartupPreparation preparation=new StartupPreparation();
        private int activationClosed, wallpaperRevealRequested;
        private readonly ManualResetEvent activationCompleted=new ManualResetEvent(false),activationClosing=new ManualResetEvent(false);
        private int wallpaperPollPending;
        private bool wallpaperLookupFinished, coverPresented, mediaCoverInitialized, mediaCoverUpdateQueued;
        private double lastWallpaperPoll = -1;
        // Absorb a slow client while the progress animation is still moving,
        // before the tear falls. Never turn the impact frame into a loading screen.
        private double preImpactGateStarted = -1, rateUpdatedAt = -1, playbackRate = 1;
        private bool preImpactReleased;
        private bool livingFailed, userSkipped;
        private double lastFocusAttempt = -1;
        private int focusAttempts;
        private double lastPlacementWaitLog = -1;
        private double lastPreparationGateLog = -1;
        private double lastPreparationGateCheck = -1;
        private string lastPreparationGate;
        public bool PlaybackSucceeded { get; private set; }
        public bool HandoffSucceeded { get; private set; }
        public FilmSplash(string path, Settings config, bool onlyPreview, bool quietErrors = false, Action emergencyExit = null)
        {
            if (config == null) throw new ArgumentNullException("config");
            filmPath = Path.GetFullPath(path);
            if (!File.Exists(filmPath)) throw new FileNotFoundException("找不到启动动画视频。", filmPath);
            settings = config; preview = onlyPreview; this.quietErrors = quietErrors; this.emergencyExit=emergencyExit; duration = settings.Duration;
            Title = "Codex 龙娘 · 启动动画";
            Width = Math.Min(Scene.DesignWidth, SystemParameters.WorkArea.Width * 0.9); Height = Width * 720 / 1280;
            if (Height > SystemParameters.WorkArea.Height * 0.9) { Height = SystemParameters.WorkArea.Height * 0.9; Width = Height * 1280 / 720; }
            WindowStartupLocation = WindowStartupLocation.CenterScreen; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            // Keep hardware-friendly opaque video until the brief pulse compositor.
            AllowsTransparency = false; Background = Brushes.Black; Topmost = true; ShowInTaskbar = false;
            player = new MediaElement { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Close, Stretch = Stretch.Uniform, IsMuted = true, Volume = 0, IsHitTestVisible = false };
            surface = new Grid { Background = Brushes.Black };
            if (!String.IsNullOrEmpty(settings.StartupIntroFile)) {
                living = new LivingFilmPlayback(AppDomain.CurrentDomain.BaseDirectory, settings, Log,
                    delegate(string message) { FailLiving(message); });
                surface.Children.Add(living);
            } else surface.Children.Add(player);
            if(!(settings.RespectReducedMotion&&!SystemParameters.ClientAreaAnimation)){
                try{
                    typography=new TerminalTypography(AppDomain.CurrentDomain.BaseDirectory);
                    surface.Children.Add(new Viewbox {Stretch=Stretch.Uniform,IsHitTestVisible=false,Child=typography});
                }catch(Exception error){Log("Optional terminal typography unavailable: "+error.GetType().Name);}
            }
            cancel = new Button { Content = "×", FontSize = 24, Foreground = Brushes.LightBlue, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Width = 48, Height = 48, Margin = new Thickness(0,10,14,0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, ToolTip = "关闭动画（已准备的 Codex 会保留）", Focusable = false };
            surface.Children.Add(cancel); Content = surface;
            watchdog = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(50) };
            handoffTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(16) };
            watchdog.Tick += OnWatchdog; handoffTimer.Tick += OnHandoffTick;
            player.MediaOpened += OnMediaOpened; player.MediaEnded += OnMediaEnded; player.MediaFailed += OnMediaFailed;
            cancel.Click += OnCancel; PreviewKeyDown += OnPreviewKeyDown; ContentRendered += OnContentRendered; Closed += OnClosed;
            SourceInitialized+=delegate { Log("Movie native show/hide transitions disabled="+NativeHandoff.DisableOwnTransitions(this)); };
            SizeChanged+=OnMediaCoverSizeChanged;DpiChanged+=OnMediaCoverDpiChanged;
            // This UI heartbeat covers intro, compatibility waits and pulse
            // reveal. Playback timers intentionally stop before the reveal;
            // that must not resemble a stalled Dispatcher to the helper.
            recoveryHeartbeat=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(250)};
            recoveryHeartbeat.Tick+=OnRecoveryHeartbeat;
            if(!onlyPreview){
                try{
                    using(var process=Process.GetCurrentProcess())recoveryGuard=new NativeRestoreGuard(process.MainModule.FileName);
                    NativeRestoreGuard.Current=recoveryGuard;recoveryGuard.Pulse();recoveryHeartbeat.Start();
                }catch(Exception error){
                    if(recoveryGuard!=null){recoveryGuard.Dispose();recoveryGuard=null;}
                    Log("Native restore guard could not prewarm: "+error.GetType().Name);
                }
            }
        }
        private void OnRecoveryHeartbeat(object sender,EventArgs e){if(!cleaned&&recoveryGuard!=null)recoveryGuard.Pulse();}
        private void OnResourceTrip(string reason)
        {
            if(Interlocked.CompareExchange(ref activationClosed,0,0)!=0||Interlocked.CompareExchange(ref resourceTrip,1,0)!=0)return;
            Log("Startup resource protection: "+reason);
            // Only this launcher can be terminated. The independent native
            // guardian restores its owned window mutations if the UI is stuck.
            // Do not mutate UI-owned preparation/session state on this callback.
            var exitBound=LauncherShutdownGuard.Begin(emergencyExit,5000);
            try {
                Dispatcher.BeginInvoke(DispatcherPriority.Send,new Action(delegate {
                    try {
                        if(done||cleaned)return;
                        if(!preview&&!activationRequested&&settings.ReplayTarget==null){
                            activationRequested=true;
                            StartActivationWorker(delegate {return new BridgeResult{Pid=AppActivation.Open(settings),Code="resource-protection-ordinary-launch"};},true);
                        }
                        FailLiving("启动资源保护已触发 本次停止动画并保留 Codex");
                    }finally{if(exitBound!=null)exitBound.Dispose();}
                }));
            }catch(InvalidOperationException){if(Interlocked.CompareExchange(ref activationClosed,0,0)!=0&&exitBound!=null)exitBound.Dispose();}
        }
        private void Log(string text)
        { LauncherDiagnostics.Write("handoff-latest.log",text); }
        private void UpdateMediaCover()
        {
            if(cleaned||done||!mediaCoverInitialized||preview||!settings.SmoothHandoff||!settings.WallpaperEnabled)return;
            // Chromium defers the first video load behind an opaque rectangular
            // window. A one-physical-pixel corner notch belongs only to our
            // cover, outside the target's six-pixel inset. Keep the existing
            // opaque WPF video pipeline and all client focus/throttling settings.
            bool prepared=NativeHandoff.PrepareOwnMediaCover(this);
            Log("Movie media cover prepared="+prepared+"; opaquePipeline=True; cornerPixels=1");
        }
        private void QueueMediaCoverUpdate()
        {
            if(cleaned||done||!mediaCoverInitialized||mediaCoverUpdateQueued||preview||!settings.SmoothHandoff||!settings.WallpaperEnabled)return;
            mediaCoverUpdateQueued=true;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(delegate {
                mediaCoverUpdateQueued=false;UpdateMediaCover();
            }));
        }
        private void OnMediaCoverSizeChanged(object sender,SizeChangedEventArgs e){QueueMediaCoverUpdate();}
        private void OnMediaCoverDpiChanged(object sender,DpiChangedEventArgs e){QueueMediaCoverUpdate();}
        private void OnContentRendered(object sender, EventArgs e)
        {
            ContentRendered -= OnContentRendered; if (done) return;
            mediaCoverInitialized=true;UpdateMediaCover();
            resourceBreaker=new StartupResourceBreaker(OnResourceTrip);resourceBreaker.Start();
            if (!preview && settings.SmoothHandoff)
            {
                // The movie itself is the only opaque guard. Never cover the
                // surrounding desktop. Prepare the target within this rectangle.
                Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,new Action(delegate {
                    if(cleaned)return;
                    NativeHandoff.FlushCompositor();shieldReady=IsVisible && ActualWidth>0 && ActualHeight>0;
                    bool activated=Activate(),focused=Focus();
                    Log("Local movie guard ready="+shieldReady+"; no desktop shield; activateAccepted="+activated+"; focusAccepted="+focused+
                        "; foreground="+NativeHandoff.ForegroundCategory(this,pulse,target)+"; filmWpfActive="+IsActive);
                }));
            }
            else { shieldReady = true; Activate(); Focus(); }
            clock.Start(); watchdog.Start();
            try { if(living!=null)living.Start();else {player.Source = new Uri(filmPath); player.Play();}
                if(typography!=null)typography.Start(delegate {return living!=null?living.TypographyPosition:opened?player.Position.TotalSeconds:0;});
            } catch (Exception error) { Fail("启动动画无法播放。", error); }
        }
        private void OnMediaOpened(object sender, RoutedEventArgs e)
        {
            if (done) return; opened = true;
            if (player.NaturalDuration.HasTimeSpan) duration = player.NaturalDuration.TimeSpan.TotalSeconds;
            if (double.IsNaN(duration) || double.IsInfinity(duration) || duration <= 0 || duration > 60) { Fail("启动动画时长无效。", null); return; }
            lastPosition = player.Position.TotalSeconds; lastProgress = clock.Elapsed.TotalSeconds; playbackClock.Restart();
        }
        private void OnWatchdog(object sender, EventArgs e)
        {
            if(recoveryGuard!=null)recoveryGuard.Pulse();
            if (done || transitioning) return; double elapsed = clock.Elapsed.TotalSeconds;
            if(living!=null){OnLivingWatchdog(elapsed);return;}
            if (!opened) { if (elapsed >= 10) Fail("视频解码器未能打开启动动画。", null); return; }
            if (!preview && settings.SmoothHandoff && shieldReady && elapsed >= settings.PrewarmSeconds)
            { EnsureActivation(); if (elapsed-lastTargetPoll >= 0.25) { lastTargetPoll=elapsed; UpdateTarget(); } }
            PollWallpaperReadiness();
            double position = player.Position.TotalSeconds;
            if (position > lastPosition+0.005) { lastPosition=position; lastProgress=elapsed; }
            if(!preview && settings.SmoothHandoff && shieldReady){
                AdvancePreparation(position,elapsed);
                PrepareTargetFocus();
                if(position>=Math.Max(0,Math.Min(settings.HandoffAtSeconds,duration-0.05)-1.0)){
                    PreparePulse();
                    if(!observingImpact){observingImpact=true;CompositionTarget.Rendering+=OnImpactFrame;}
                }
            }
            if (!preview && settings.SmoothHandoff && shieldReady && position >= Math.Min(settings.HandoffAtSeconds,duration-0.05)) { BeginHandoff(); return; }
            if (elapsed-lastProgress > 5) Fail("启动动画播放停止超过 5 秒。",null);
            else if (elapsed > duration+settings.HandoffWaitSeconds+10) Fail("启动动画超出预期时长。",null);
        }
        private double PreparationPosition()
        { return Math.Max(0,Math.Min(settings.HandoffAtSeconds,duration-0.05)*0.67); }
        private double FilmPosition { get { return living==null ? player.Position.TotalSeconds : living.FinalePosition; } }
        private double ImpactPosition { get { return living==null ? Math.Min(settings.HandoffAtSeconds,duration-0.05) : settings.FinaleImpactSeconds; } }
        private bool StrictWallpaperPrepared()
        {
            if(!settings.WallpaperEnabled)return true;
            return Interlocked.CompareExchange(ref wallpaperRevealRequested,0,0)!=-1 &&
                preparation.IsPrepared(wallpaperSession,clock.Elapsed.TotalSeconds);
        }
        private void OnLivingWatchdog(double elapsed)
        {
            if(recoveryGuard!=null)recoveryGuard.Pulse();
            living.Tick(); if(done || cleaned)return;
            if(preview){
                if(living.InIdle && elapsed>=8 && living.FinaleReady)living.TryStartFinale();
                if(living.FinalePosition>=settings.FinaleImpactSeconds){PlaybackSucceeded=true;done=true;Close();}
                return;
            }
            if(shieldReady && elapsed>=settings.PrewarmSeconds){
                EnsureActivation();
                if(elapsed-lastTargetPoll>=0.25){lastTargetPoll=elapsed;UpdateTarget();}
            }
            if(settings.ReplayTarget==null && wallpaperSession!=null && wallpaperSession.HostExited){
                Log("Wallpaper host process ended during startup; stale preparation discarded");
                FailLiving("壁纸宿主已退出 本次结束启动动画并保留客户端");return;
            }
            PollWallpaperReadiness();
            if(living.InIdle || living.InFinale)PreparePulse();
            if(targetPreparationFailed){FailLiving("客户端窗口准备失败 已保留 Codex 请查看启动日志",true);return;}
            if(activationError!=null || inputGuardFailed || pulsePreparationFailed){FailLiving("启动准备失败 已保留 Codex 请查看启动日志");return;}
            BridgeStatus state=wallpaperSession==null?null:wallpaperSession.LastStatus;
            if(settings.WallpaperEnabled && wallpaperLookupFinished && wallpaperSession==null){FailLiving("未找到可用的壁纸会话 本次没有播放成功转场");return;}
            // Recoverable renderer rebuild reports MediaCode=2/4. Terminal
            // errors retain >=6; neither state can satisfy prepared evidence.
            if(state!=null && (state.Disabled || state.MediaCode>=6)){FailLiving("壁纸准备失败 本次没有播放成功转场");return;}
            if(!living.InFinale){
                bool nativeStable=TargetStable(),wallpaperPrepared=StrictWallpaperPrepared(),finaleReady=living.FinaleReady;
                // Do not direct keyboard input behind a long preparation cover.
                // Request focus only when the rest of the visual handoff is ready.
                if(nativeStable&&wallpaperPrepared&&pulseRendered&&finaleReady)PrepareTargetFocus();
                bool focusSettled=FocusSettled();
                LogPreparationGate(elapsed,nativeStable,focusSettled,wallpaperPrepared,finaleReady,state);
                if(nativeStable && focusSettled && wallpaperPrepared && pulseRendered && finaleReady){
                    double evidenceCheckedAt=clock.Elapsed.TotalSeconds;
                    // Failed/expired evidence must still reach the shared
                    // deadline below; it must never keep the cover alive by
                    // returning early on every otherwise-ready tick.
                    if((!settings.WallpaperEnabled||preparation.IsPrepared(wallpaperSession,evidenceCheckedAt))&&
                        targetPlacement!=null&&targetPlacement.ArmHandoff()){
                        if(living.TryStartFinale()){
                            if(settings.WallpaperEnabled&&!preparation.Commit(wallpaperSession,evidenceCheckedAt)){
                                FailLiving("准备证据已过期 本次普通显示 Codex");return;
                            }
                            preImpactReleased=true;
                            Log("Living wallpaper preparation evidence="+(settings.ReplayTarget!=null?"existing-wallpaper-preserved-not-reprepared":
                                state!=null && state.FirstFrameConfirmed==true?"decoded-frame-confirmed":"hidden-decoder-ready"));
                            Log("Living finale committed; nativeStable=True; focusSettled=True; actualForeground="+NativeHandoff.IsForegroundTarget(target)+
                                (settings.ReplayTarget!=null?"; existingWallpaperPreserved=True":"; wallpaperReady=True")+
                                "; finalePrerolled=True; pulseReady=True; speed=1");
                            observingImpact=true;CompositionTarget.Rendering+=OnImpactFrame;
                        }
                        else targetPlacement.DisarmHandoff();
                    }
                }
                if(!living.InFinale && elapsed>=settings.StartupReadyTimeoutSeconds)FailLiving("等待客户端或壁纸准备超时 本次没有播放成功转场");
            } else if(FilmPosition>=ImpactPosition){lastPosition=FilmPosition;BeginHandoff();}
        }
        private void LogPreparationGate(double elapsed,bool nativeStable,bool focusSettled,bool wallpaperPrepared,bool finaleReady,BridgeStatus state)
        {
            // Fixed structural facts only: no titles, account text or session tokens.
            // Log changed gates at most once per second and an unchanged wait
            // at most once per five seconds within the existing startup deadline.
            if(lastPreparationGateCheck>=0 && elapsed-lastPreparationGateCheck<1)return;
            lastPreparationGateCheck=elapsed;
            bool foreground=NativeHandoff.IsForegroundTarget(target),overlay=OwnOverlayActive;
            string gate="nativeStable="+nativeStable+"; focusSettled="+focusSettled+
                "; targetForeground="+foreground+"; ownOverlayActive="+overlay+
                "; foreground="+NativeHandoff.ForegroundCategory(this,pulse,target)+"; filmWpfActive="+IsActive+
                "; filmNativeForeground="+NativeHandoff.IsForegroundOwnWindow(this)+"; pulseWpfActive="+(pulse!=null&&pulse.IsActive)+
                "; pulseNativeForeground="+NativeHandoff.IsForegroundOwnWindow(pulse)+"; focusAttempts="+focusAttempts+
                "; wallpaperPrepared="+wallpaperPrepared+"; pulseReady="+pulseRendered+
                "; finaleReady="+finaleReady+"; mediaCode="+(state==null?-1:state.MediaCode)+
                "; refreshCode="+(state==null?-1:state.RefreshCode);
            if(gate==lastPreparationGate && elapsed-lastPreparationGateLog<5)return;
            lastPreparationGate=gate;lastPreparationGateLog=elapsed;
            Log("Living preparation gates; elapsedMs="+((long)(elapsed*1000))+"; "+gate);
        }
        private void FailLiving(string message,bool nativeFailure=false)
        {
            if(done || cleaned)return;
            livingFailed=true;HandoffSucceeded=false;done=true;
            Log("Living preparation failed: "+message);
            DisableUnpreparedWallpaper(nativeFailure ? "Wallpaper reveal cancelled because native target preparation failed; ordinary client preserved" : null);
            // Restore the ordinary client without a success ripple. Never keep
            // the user trapped behind an infinite loading cover.
            Close();
            if(preview && !quietErrors)MessageBox.Show(message,"Codex 启动器",MessageBoxButton.OK,MessageBoxImage.Warning);
        }
        private bool FocusSettled()
        {
            if(living==null)return targetFocusPrepared && clock.Elapsed.TotalSeconds-targetFocusPreparedAt>=0.18;
            return StartupFocusPolicy.Settled(NativeHandoff.IsForegroundTarget(target),OwnOverlayActive,
                targetFocusPrepared,targetFocusPreparedAt,focusAttempts,lastFocusAttempt,clock.Elapsed.TotalSeconds);
        }
        // Hiding an activating client can cause Windows to select our already
        // visible prewarmed pulse. It is the same owned cover, not a user focus
        // switch to an unrelated application. Do not strand a prepared launch.
        private bool OwnOverlayActive {get{return NativeHandoff.IsForegroundOwnWindow(this)||(pulse!=null&&NativeHandoff.IsForegroundOwnWindow(pulse));}}
        private void AdvancePreparation(double position,double elapsed)
        {
            if(position<PreparationPosition())return;
            if(preImpactGateStarted<0){
                preImpactGateStarted=elapsed;rateUpdatedAt=elapsed;
                PreparePulse();
                Log("Pre-impact preparation begins; filmPosition="+position.ToString("F3",System.Globalization.CultureInfo.InvariantCulture));
            }
            PrepareTargetFocus();
            bool ready=TargetStable() && FocusSettled() && WallpaperPrepared() && pulseRendered;
            if(!preImpactReleased && (ready || activationError!=null || targetPreparationFailed || inputGuardFailed || pulsePreparationFailed || elapsed-preImpactGateStarted>=settings.HandoffWaitSeconds)){
                preImpactReleased=true;
                if(!WallpaperPrepared())DisableUnpreparedWallpaper();
                Log("Pre-impact preparation released; ready="+ready+"; waitSeconds="+(elapsed-preImpactGateStarted).ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+"; filmPosition="+position.ToString("F3",System.Globalization.CultureInfo.InvariantCulture));
            }
            // Leave headroom for the smooth deceleration and dispatcher jitter.
            // Even a full bounded wait stays before tear release (~6.35 s in
            // the 8 s film). No Pause, Seek, CPU frame synthesis or extra decoder.
            double impact=Math.Min(settings.HandoffAtSeconds,duration-0.05);
            double slow=Math.Max(0.03,Math.Min(0.22,(impact*0.80-PreparationPosition())*0.70/(settings.HandoffWaitSeconds+0.5)));
            double desired=preImpactReleased?1:slow;
            double dt=Math.Max(0,Math.Min(0.1,elapsed-rateUpdatedAt));rateUpdatedAt=elapsed;
            double delta=dt*(preImpactReleased?2.6:4.5);
            double next=playbackRate<desired?Math.Min(desired,playbackRate+delta):Math.Max(desired,playbackRate-delta);
            if(Math.Abs(next-playbackRate)>0.001){playbackRate=next;player.SpeedRatio=playbackRate;}
        }
        private void EnsureActivation()
        {
            if (preview || cleaned || activationRequested) return;
            if(settings.ReplayTarget!=null){
                activationRequested=true;activationFinished=true;wallpaperLookupFinished=true;
                if(!settings.ReplayTarget.IsCurrent){
                    activationError=new InvalidOperationException("existing-replay-target-ended");
                    Log("Replay target ended; no application activation or wallpaper mutation");return;
                }
                target=settings.ReplayTarget.Window;PrepareTarget();
                Log("Replaying into pinned existing window; existing wallpaper untouched; no application activation");return;
            }
            activationRequested=true; target=NativeHandoff.FindTarget(settings,0);
            if (target != IntPtr.Zero) {
                activationFinished=true;PrepareTarget();Log("Reusing verified existing window "+target+"; no application activation");
                uint existingPid=BridgeActivation.WindowProcessId(target);
                StartActivationWorker(delegate {return new BridgeResult {Session=BridgeActivation.FindExisting(settings,existingPid,Log),Code="existing-window-wallpaper-lookup"};},false);
                return;
            }
            Log("Requesting application activation once; shieldReady="+shieldReady);
            StartActivationWorker(delegate {return BridgeActivation.OpenOnce(settings,Log);},true);
        }
        private void ReleaseUnclaimedBridge(BridgeResult result)
        {
            if(result==null)return;
            if(result.Session!=null&&settings.ReplayTarget==null)result.Session.Queue(Interlocked.CompareExchange(ref wallpaperRevealRequested,0,0)==1 ? RevealCommand : "disable");
            result.Dispose();
        }
        private void StartActivationWorker(Func<BridgeResult> activate,bool updateActivation)
        {
            var thread = new Thread(delegate() {
                try {
                BridgeResult result;
                try {result=activate();}catch(Exception failure){result=new BridgeResult {Error=failure,Code="activation-worker-failed"};}
                if(result.Session!=null){
                    // A failed optional attestation never discards a started
                    // client's session or its required reveal/disable cleanup.
                    try {result.ClientIdentity=BridgeClientIdentity.Attest(settings,AppDomain.CurrentDomain.BaseDirectory,result.Session);}catch{}
                    Log("Bridge client window identity verified="+(result.ClientIdentity!=null));
                }
                if(Interlocked.CompareExchange(ref activationClosed,0,0)!=0){ReleaseUnclaimedBridge(result);return;}
                int claimed=0;
                try {
                    DispatcherOperation operation=Dispatcher.BeginInvoke(new Action(delegate {
                        if(Interlocked.Exchange(ref claimed,1)!=0)return;
                        if(cleaned){ReleaseUnclaimedBridge(result);return;}
                        wallpaperSession=result.Session;result.Session=null;
                        if(result.ClientIdentity!=null)NativeHandoff.RegisterBridgeTarget(result.ClientIdentity);
                        wallpaperLookupFinished=true;
                        int disposition=Interlocked.CompareExchange(ref wallpaperRevealRequested,0,0);
                        if(settings.ReplayTarget==null && wallpaperSession!=null && disposition!=0)wallpaperSession.Queue(disposition==1 ? RevealCommand : "disable");
                        if(updateActivation){activatedPid=result.Pid;activationError=result.Error;activationFinished=true;}
                        Log("Activation result="+result.Code+"; PID="+result.Pid+"; wallpaperBridge="+(wallpaperSession!=null));
                        result.Dispose();
                    }));
                    operation.Aborted+=delegate {if(Interlocked.Exchange(ref claimed,1)==0)ReleaseUnclaimedBridge(result);};
                    if(operation.Status==DispatcherOperationStatus.Aborted && Interlocked.Exchange(ref claimed,1)==0)ReleaseUnclaimedBridge(result);
                }catch(InvalidOperationException){if(Interlocked.Exchange(ref claimed,1)==0)ReleaseUnclaimedBridge(result);}
                } catch(Exception failure) {Log("Activation worker cleanup failed: "+failure.GetType().Name);}
                finally {activationCompleted.Set();}
            });
            // COM/Shell activation cannot always be cancelled. It runs on a
            // background STA, while a bounded lifetime guard grants in-flight
            // handshake cleanup time without keeping the launcher alive forever.
            thread.IsBackground=true; thread.Name="Codex activation handoff";
            thread.SetApartmentState(ApartmentState.STA); thread.Start();
            var lifetime=new Thread(delegate() {
                int budget=(int)Math.Max(1,Math.Min(93000,Math.Ceiling((settings.StartupReadyTimeoutSeconds-clock.Elapsed.TotalSeconds)*1000)+3000));
                if(WaitHandle.WaitAny(new WaitHandle[]{activationCompleted,activationClosing},budget)==1){
                    // The ordinary bridge handshake itself is bounded at four
                    // seconds. An early skip closes the UI immediately, but must
                    // allow its final reveal to outlive that handshake plus
                    // discovery/attestation. The close event publishes skip intent.
                    int cleanupBudget=userSkipped&&settings.WallpaperEnabled?7000:3000;
                    if(!activationCompleted.WaitOne(cleanupBudget))
                        Log("Activation cleanup bound expired; waitMs="+cleanupBudget+"; explicitSkip="+userSkipped+"; no second client launch");
                }
            }){IsBackground=false,Name="Bounded activation cleanup"};
            lifetime.Start();
        }
        private string RevealCommand {get{return living==null || userSkipped ? "reveal" : "commit "+preparation.OperationId;}}
        private void RevealWallpaper()
        {
            if(settings.ReplayTarget!=null)return;
            if(Interlocked.CompareExchange(ref wallpaperRevealRequested,1,0)!=0)return;
            if(wallpaperSession!=null)wallpaperSession.Queue(RevealCommand);
        }
        private void DisableUnpreparedWallpaper(string reason=null)
        {
            if(settings.ReplayTarget!=null)return;
            if(Interlocked.CompareExchange(ref wallpaperRevealRequested,-1,0)!=0)return;
            preparation.Cancel();
            if(wallpaperSession!=null)wallpaperSession.Queue("disable");
            Log(reason??"Wallpaper preparation did not complete within handoff bound; disabled for this launch");
        }
        private bool WallpaperPrepared()
        {
            if(living!=null)return preparation.HasCommit ?
                preparation.CanHandoff(wallpaperSession,wallpaperSession==null?null:wallpaperSession.LastStatus,clock.Elapsed.TotalSeconds,
                    Interlocked.CompareExchange(ref wallpaperRevealRequested,0,0)==1) : StrictWallpaperPrepared();
            if(Interlocked.CompareExchange(ref wallpaperRevealRequested,0,0)==-1)return true;
            if(wallpaperSession==null)return wallpaperLookupFinished || !settings.WallpaperEnabled;
            BridgeStatus status=wallpaperSession.LastStatus;
            if(status!=null && (status.Disabled || status.MediaCode>=6)){
                DisableUnpreparedWallpaper();return true;
            }
            return status!=null && status.Ready;
        }
        private void PollWallpaperReadiness()
        {
            if(settings.ReplayTarget!=null)return;
            if(cleaned || wallpaperSession==null || Interlocked.CompareExchange(ref wallpaperRevealRequested,0,0)!=0 ||
                (living==null && WallpaperPrepared()) || clock.Elapsed.TotalSeconds-lastWallpaperPoll<(living==null?0.2:0.5) ||
                Interlocked.CompareExchange(ref wallpaperPollPending,1,0)!=0)return;
            lastWallpaperPoll=clock.Elapsed.TotalSeconds;
            BridgeSession session=wallpaperSession;
            StartupPreparation.Request request=preparation.Begin(session,clock.Elapsed.TotalSeconds);
            ThreadPool.QueueUserWorkItem(delegate {
                try {
                    if(living==null)session.Status(500);
                    else {
                        if(request==null)return;
                        BridgeStatus fresh=session.Prepare(request.OperationId,1800);
                        Dispatcher.BeginInvoke(new Action(delegate {
                            if(!cleaned&&Object.ReferenceEquals(wallpaperSession,session))
                                preparation.Accept(request,session,fresh,clock.Elapsed.TotalSeconds);
                        }));
                    }
                }catch { }
                finally {Interlocked.Exchange(ref wallpaperPollPending,0);}
            });
        }
        private bool PresentPulseCover()
        {
            if(coverPresented)return true;
            if(!NativeHandoff.RaiseOwnCover(pulse))return false;
            NativeHandoff.FlushCompositor();Hide();coverPresented=true;
            Log("Movie hidden beneath prepared cover; decoder retained through reveal");
            return true;
        }
        private void PrepareTarget()
        {
            if(target==IntPtr.Zero || targetPreparationFailed)return;
            if(targetPlacement==null){
                // One overall living-startup budget, including activation time.
                // Rediscovering/replacing a window never starts a new 90s wait.
                int budget=living==null ? 2000 : (int)Math.Max(1,Math.Min(90000,
                    Math.Ceiling((settings.StartupReadyTimeoutSeconds-clock.Elapsed.TotalSeconds)*1000)));
                targetPlacement=NativeHandoff.PrepareTargetInOverlay(target,this,budget);
                lastPlacementWaitLog=-1;
                Log("Compact placement started; budgetMs="+budget+"; living="+(living!=null));
            }
            if(targetPlacement==null){targetPreparationFailed=true;return;}
            bool prepared=targetPlacement.TryPrepare();
            if(targetPlacement.CanSuppressTransitions&&suppressedTransitions==null)suppressedTransitions=NativeHandoff.SuppressTransitions(target);
            if(prepared && !targetRaised){
                NativeHandoff.RaiseTargetBehindOverlay(target);
                targetRaised=true;Log("Target ready inside local movie rectangle "+target);
            }
            if(targetPlacement.Failed){targetPreparationFailed=true;targetPlacement.Dispose();Log("Compact placement failed; cleanup requested: "+targetPlacement.FailureReason);}
            else if(!prepared && living!=null && targetPlacement.PreparationElapsedMilliseconds>=2000 &&
                (lastPlacementWaitLog<0 || clock.Elapsed.TotalSeconds-lastPlacementWaitLog>=5)){
                lastPlacementWaitLog=clock.Elapsed.TotalSeconds;
                Log((targetPlacement.Ready?"Native readiness waiting after placement":"Compact placement waiting")+
                    "; phase="+targetPlacement.PreparationPhase+"; elapsedMs="+targetPlacement.PreparationElapsedMilliseconds+
                    "; "+targetPlacement.ReadinessDiagnostic+"; idle animation continues");
            }
        }
        private void RetainOverlayInput()
        {
            if(targetFocusPrepared)return;
            if(!NativeHandoff.IsForegroundTarget(target))return;
            if(living!=null && inputGuard!=null && inputGuard.Installed){
                // Windows may accept the foreground request asynchronously.
                // Once covered input is guarded, do not steal focus back and
                // turn the readiness check into a focus ping-pong loop.
                targetFocusPrepared=true;targetFocusPreparedAt=clock.Elapsed.TotalSeconds;return;
            }
            Window activeOverlay=transitioning && !IsVisible && pulseRendered && pulse!=null ? (Window)pulse : this;
            if(activeOverlay.IsVisible){activeOverlay.Activate();activeOverlay.Focus();}
        }
        private void UpdateTarget()
        {
            if(!activationRequested || !activationFinished || activationError!=null)return;
            if(settings.ReplayTarget!=null&&!settings.ReplayTarget.IsCurrent){
                targetPreparationFailed=true;Log("Pinned replay target ended; no target substitution");return;
            }
            IntPtr found=settings.ReplayTarget!=null ? settings.ReplayTarget.Window :
                target!=IntPtr.Zero && NativeHandoff.FindCurrentStillValid(target) ? target : NativeHandoff.FindTarget(settings,activatedPid);
            if(found!=target) {
                preparation.InvalidateTarget();
                if(inputGuard!=null){inputGuard.Dispose();inputGuard=null;}inputGuardFailed=false;
                if(targetPlacement!=null){targetPlacement.Dispose();targetPlacement=null;}
                if(suppressedTransitions!=null){suppressedTransitions.Dispose();suppressedTransitions=null;}
                target=found;targetRaised=false;targetPreparationFailed=false;readySince=-1;
                targetFocusPrepared=false;targetFocusPreparedAt=0;
                focusAttempts=0;lastFocusAttempt=-1;
            }
            PrepareTarget();
            RetainOverlayInput();
            if(targetPlacement!=null && targetPlacement.Ready && NativeHandoff.IsReady(target)) { if(readySince<0)readySince=clock.Elapsed.TotalSeconds; } else readySince=-1;
        }
        private bool TargetStable() { return targetPlacement!=null && targetPlacement.Ready && readySince>=0 && clock.Elapsed.TotalSeconds-readySince>=0.5 && NativeHandoff.IsReady(target); }
        private void PrepareTargetFocus()
        {
            if(living!=null && targetFocusPrepared && !NativeHandoff.IsForegroundTarget(target))targetFocusPrepared=false;
            if(targetFocusPrepared || !TargetStable())return;
            if(living==null && !transitioning && player.Position.TotalSeconds<PreparationPosition())return;
            if(living!=null && !StartupFocusPolicy.MayRequest(NativeHandoff.IsForegroundTarget(target),OwnOverlayActive,
                living.InFinale,focusAttempts,lastFocusAttempt,clock.Elapsed.TotalSeconds))return;
            if(inputGuardFailed)return;
            if(inputGuard==null){
                inputGuard=new NativeHandoff.KeyboardGuard(target,
                    delegate {return !done && !cleaned && (IsVisible || (pulse!=null && pulse.IsVisible));},
                    delegate {Dispatcher.BeginInvoke(new Action(SkipAnimation));});
                if(!inputGuard.Installed){inputGuardFailed=true;Log("Temporary input guard unavailable; defer to bounded handoff fallback");return;}
            }
            bool focused=NativeHandoff.FocusReadyTarget(target);
            focusAttempts++;
            lastFocusAttempt=clock.Elapsed.TotalSeconds;
            targetFocusPrepared=living==null || focused || NativeHandoff.IsForegroundTarget(target);targetFocusPreparedAt=clock.Elapsed.TotalSeconds;
            Log("Codex focus prepared under playing movie; foregroundAccepted="+focused+"; filmPosition="+FilmPosition.ToString("F3",System.Globalization.CultureInfo.InvariantCulture));
        }
        private void OnImpactFrame(object sender,EventArgs e)
        {
            if(done || cleaned || transitioning)return;
            if(living!=null && !living.InFinale)return;
            double position=FilmPosition;
            if(position>=ImpactPosition){
                lastPosition=position;BeginHandoff();
            }
        }
        private void StopImpactObserver()
        { CompositionTarget.Rendering-=OnImpactFrame;observingImpact=false; }
        private void PreparePulse()
        {
            if(done || cleaned || pulsePreparationAttempted)return;
            pulsePreparationAttempted=true;
            Point a=PointToScreen(new Point(0,0)), b=PointToScreen(new Point(ActualWidth,ActualHeight));
            try {
                pulse=new PulseReveal(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"output","handoff-impact.png"),settings.HandoffFadeSeconds,living!=null);
                // Showing a non-topmost, non-activating window cannot cover the
                // topmost movie. Its final ordering is then set explicitly.
                pulse.Topmost=false;
                pulse.Left=Left;pulse.Top=Top;pulse.Width=ActualWidth;pulse.Height=ActualHeight;
                pulse.RevealFinished+=delegate {CompleteHandoff();};pulse.Dismissed+=delegate {CancelHandoff();};
                pulse.SkipRequested+=delegate {SkipAnimation();};
                pulse.Closed+=delegate(object sender,EventArgs args){
                    if(object.ReferenceEquals(pulse,sender))pulse=null;
                    if(!done && !cleaned){pulsePreparationFailed=true;if(transitioning)CancelHandoff();}
                };
                pulse.OpaqueCoverFailed+=delegate {
                    if(cleaned)return;
                    pulsePreparationFailed=true;
                    Log("Opaque pulse readiness failed: "+pulse.OpaqueCoverFailureReason);
                    if(transitioning)CompleteHandoff();
                };
                pulse.OpaqueCoverReady+=delegate {
                    if(cleaned || pulseRendered || pulse==null)return;
                    NativeHandoff.FlushCompositor();
                    pulseRendered=true;
                    Log("Opaque pulse pre-rendered behind playing movie; coverCycles="+pulse.CoverRenderCycles+"; filmPosition="+FilmPosition.ToString("F3",System.Globalization.CultureInfo.InvariantCulture));
                    if(transitioning)OnHandoffTick(null,EventArgs.Empty);
                };
                pulse.ContentRendered+=delegate {
                    if(cleaned || pulseLayoutConfigured)return; pulseLayoutConfigured=true;
                    if(!NativeHandoff.PlaceOverTarget(pulse,new WindowInteropHelper(this).Handle) || !NativeHandoff.PlaceBehindOwnWindow(pulse,this)){
                        pulsePreparationFailed=true;if(transitioning)CompleteHandoff();return;
                    }
                    pulse.SetFilmScreenRect(a,b);
                    pulse.ArmOpaqueCover();
                };
                pulse.Show();NativeHandoff.PlaceOverTarget(pulse,new WindowInteropHelper(this).Handle);pulse.SetFilmScreenRect(a,b);
                if(!NativeHandoff.PlaceBehindOwnWindow(pulse,this))pulsePreparationFailed=true;
                Log("Pulse compositor prewarmed below movie at film position "+FilmPosition.ToString("F3",System.Globalization.CultureInfo.InvariantCulture));
            } catch(Exception error){pulsePreparationFailed=true;Log("Pulse prewarm fallback: "+error.Message);if(transitioning)CompleteHandoff();}
        }
        private void BeginHandoff()
        {
            if(done || transitioning)return;
            if(typography!=null)typography.Dispose();
            transitioning=true;watchdog.Stop();StopImpactObserver();cancel.Visibility=Visibility.Collapsed;EnsureActivation();
            if(living!=null)living.HandoffStarted();
            handoffClock.Restart();handoffTimer.Start();
            PreparePulse();
            Log("Impact handoff requested at film position "+lastPosition.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+"; preRendered="+pulseRendered);
            OnHandoffTick(null,EventArgs.Empty);
        }
        private void OnHandoffTick(object sender,EventArgs e)
        {
            if(cleaned || revealStarted)return;
            if(inputGuardFailed){Log("Input guard unavailable at impact; bounded fallback");CompleteHandoff();return;}
            if(pulsePreparationFailed){Log("Pulse unavailable at impact; bounded fallback");CompleteHandoff();return;}
            if(!pulseRendered){
                if(transitioning && pulse!=null && handoffClock.Elapsed.TotalSeconds>2){Log("Pulse initial layout timeout");CompleteHandoff();}
                return;
            }
            UpdateTarget();PollWallpaperReadiness();
            bool ready=living!=null ? targetPlacement!=null&&targetPlacement.HandoffReady() : TargetStable();
            bool mediaReady=WallpaperPrepared();
            if(living!=null && (!ready || !mediaReady)){FailLiving("转场前客户端或壁纸状态发生变化 本次没有播放成功转场");return;}
            if(!preImpactReleased && ready && !targetFocusPrepared){
                PrepareTargetFocus();
                return;
            }
            if(!preImpactReleased && ready && clock.Elapsed.TotalSeconds-targetFocusPreparedAt<0.18)return;
            if(preImpactReleased || (ready && mediaReady) || activationError!=null || targetPreparationFailed || handoffClock.Elapsed.TotalSeconds>=settings.HandoffWaitSeconds) {
                if(!ready)Log("Target not ready at handoff: "+NativeHandoff.DescribeTargetAvailability(settings));
                if(!mediaReady)DisableUnpreparedWallpaper();
                revealStarted=true;handoffTimer.Stop();HandoffSucceeded=ready && (living==null || mediaReady);
                if(!PresentPulseCover()){Log("Cannot raise prepared cover");CompleteHandoff();return;}
                RevealWallpaper();
                // Keep the hidden decoder until completion. Closing MediaElement
                // here can block the dispatcher and stall the first ripple frame.
                Log("Pulse reveal begins; targetReady="+ready+"; focusPrepared="+targetFocusPrepared+"; waitSeconds="+handoffClock.Elapsed.TotalSeconds.ToString("F3",System.Globalization.CultureInfo.InvariantCulture));pulse.StartReveal();
            } else {
                // An explicit early skip can reach here before preparation.
                // Natural playback has already released its pre-impact gate;
                // it must reveal immediately instead of waiting on this still.
                if(!PresentPulseCover()){CompleteHandoff();return;}
                pulse.ShowWaiting();
            }
        }
        private void CompleteHandoff()
        {
            if(done)return;done=true;
            bool mediaReady=WallpaperPrepared();
            if(!mediaReady){DisableUnpreparedWallpaper();if(living!=null)HandoffSucceeded=false;}
            RevealWallpaper();
            if(targetPlacement!=null)HandoffSucceeded=(living==null ? (HandoffSucceeded || TargetStable()) : (HandoffSucceeded && !livingFailed && !userSkipped)) && targetPlacement.Commit();
            if(living==null && target!=IntPtr.Zero && !targetFocusPrepared)NativeHandoff.FocusReadyTarget(target);
            BridgeStatus completionStatus=wallpaperSession==null?null:wallpaperSession.LastStatus;
            bool wallpaperCommitted=completionStatus!=null&&completionStatus.CommitAccepted&&completionStatus.PrepareOperationId==preparation.OperationId&&mediaReady;
            Log("Handoff complete; success="+HandoffSucceeded+"; pulseFrames="+(pulse==null?0:pulse.PresentedFrames)+
                "; wallpaperCommitConfirmed="+wallpaperCommitted+"; wallpaperPlaybackRequiresObserver="+(settings.ReplayTarget==null)+
                "; replayExisting="+(settings.ReplayTarget!=null));Close();
            if(activationError!=null && preview && !quietErrors)MessageBox.Show("无法打开 Codex。请检查启动配置。\n\n"+activationError.Message,"Codex 启动器",MessageBoxButton.OK,MessageBoxImage.Error);
        }
        private void CancelHandoff(){if(done)return;done=true;HandoffSucceeded=false;Log("Animation cancelled; restoring target placement");Close();}
        private void OnMediaEnded(object sender,RoutedEventArgs e)
        {
            if(done || transitioning)return;PlaybackSucceeded=true;playbackClock.Stop();
            if(preview)try {
                string output=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"output");Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output,"native-preview-validation.txt"),"PASS: MediaEnded; preview never activated an app.\nNaturalVideoWidth: "+player.NaturalVideoWidth+"\nNaturalVideoHeight: "+player.NaturalVideoHeight+"\nNaturalDurationSeconds: "+duration);
            } catch(Exception error){if(quietErrors){PlaybackSucceeded=false;Fail("无法保存验证日志。",error);return;}}
            LaunchClose();
        }
        private void OnMediaFailed(object sender,ExceptionRoutedEventArgs e){Fail("启动动画无法解码。",e.ErrorException);}
        private void OnPreviewKeyDown(object sender,KeyEventArgs e)
        {
            if(e.Key!=Key.Escape && e.Key!=Key.Enter && e.Key!=Key.Space)return;
            e.Handled=true;SkipAnimation();
        }
        private void OnCancel(object sender,RoutedEventArgs e){e.Handled=true;if(!cleaned)Close();}
        private void SkipAnimation()
        {
            if(done || cleaned)return;
            if(preview){done=true;Close();return;}
            userSkipped=true;HandoffSucceeded=false;EnsureActivation();
            // Skipping the optional movie is not a failed wallpaper preparation.
            // The Host retains an early reveal until its decoder is ready. Publish
            // this intent before Close so late activation uses the same command.
            if(settings.WallpaperEnabled)RevealWallpaper();
            if(targetPlacement!=null)targetPlacement.Commit();
            Log("Animation explicitly skipped; wallpaper preparation continues; no success finale");
            done=true;Close();
        }
        private void LaunchClose()
        {
            if(done || transitioning)return;
            if(preview){done=true;Close();return;}
            if(living!=null){SkipAnimation();return;}
            if(settings.SmoothHandoff && shieldReady){BeginHandoff();return;}
            EnsureActivation();transitioning=true;ReleasePlayback();handoffClock.Start();handoffTimer.Tick+=OnCompatibilityWait;handoffTimer.Start();
        }
        private void OnCompatibilityWait(object sender,EventArgs e){if(activationFinished || handoffClock.Elapsed.TotalSeconds>=settings.HandoffWaitSeconds)CompleteHandoff();}
        private void Fail(string message,Exception error)
        {
            if(living!=null){FailLiving(message+(error==null?"":" "+error.Message));return;}
            if(done || transitioning)return;Log(message+(error==null?"":" "+error.Message));
            if(!preview){LaunchClose();return;}done=true;Close();if(!quietErrors)MessageBox.Show(message,"Codex 启动器",MessageBoxButton.OK,MessageBoxImage.Error);
        }
        private void ReleasePlayback()
        {
            if(playbackReleased)return;playbackReleased=true;watchdog.Stop();watchdog.Tick-=OnWatchdog;playbackClock.Stop();StopImpactObserver();
            if(typography!=null)typography.Dispose();
            if(living!=null)living.Dispose();
            player.MediaOpened-=OnMediaOpened;player.MediaEnded-=OnMediaEnded;player.MediaFailed-=OnMediaFailed;
            try{player.Close();}catch{}try{player.Source=null;}catch{}
        }
        private void OnClosed(object sender,EventArgs e)
        {
            if(cleaned)return;cleaned=true;done=true;
            preparation.Cancel();
            Interlocked.Exchange(ref activationClosed,1);
            if(resourceBreaker!=null){resourceBreaker.Dispose();resourceBreaker=null;}
            activationClosing.Set();
            // Restore user input and the real client before any media shutdown.
            // A slow decoder must never leave the user behind an input guard.
            watchdog.Stop();handoffTimer.Stop();recoveryHeartbeat.Stop();recoveryHeartbeat.Tick-=OnRecoveryHeartbeat;StopImpactObserver();
            if(inputGuard!=null){try{inputGuard.Dispose();}catch{}inputGuard=null;}
            if(targetPlacement!=null){try{targetPlacement.Dispose();}catch{}targetPlacement=null;}
            if(suppressedTransitions!=null){try{suppressedTransitions.Dispose();}catch{}suppressedTransitions=null;}
            if(recoveryGuard!=null){recoveryGuard.Dispose();recoveryGuard=null;}
            if(pulse!=null){try{pulse.Close();}catch{}pulse=null;}
            using(LauncherShutdownGuard.Begin(emergencyExit,5000)) {
            if(wallpaperSession!=null){
                try{if(settings.ReplayTarget==null&&Interlocked.CompareExchange(ref wallpaperRevealRequested,0,0)==0)wallpaperSession.Queue("disable");}catch{}
                try{wallpaperSession.Dispose();}catch{}wallpaperSession=null;
            }
            ReleasePlayback();handoffTimer.Tick-=OnHandoffTick;handoffTimer.Tick-=OnCompatibilityWait;
            clock.Stop();handoffClock.Stop();
            cancel.Click-=OnCancel;PreviewKeyDown-=OnPreviewKeyDown;ContentRendered-=OnContentRendered;Closed-=OnClosed;
            SizeChanged-=OnMediaCoverSizeChanged;DpiChanged-=OnMediaCoverDpiChanged;
            Log("Cleanup complete: decoder, local pulse and timers released; no desktop shield");
            }
        }
    }
}

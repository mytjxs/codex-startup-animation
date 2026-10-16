using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DragonLauncher
{
    // A short-lived compositor. Only the already-packaged movie's impact frame
    // is rasterized; no desktop capture and no modification of the target app.
    public sealed class PulseReveal : Window
    {
        private readonly PulseSurface picture;
        private readonly Button dismiss;
        private readonly Stopwatch elapsed = new Stopwatch();
        private readonly Stopwatch coverStable = new Stopwatch();
        private readonly Stopwatch waitingElapsed = new Stopwatch();
        private readonly DispatcherTimer coverTimeout, revealTimeout;
        private readonly double seconds;
        private double lastFrame = -1;
        private bool running, stopped, coverArmed;
        private int observedCoverRevision = -1;
        private TimeSpan lastCoverRenderingTime = TimeSpan.MinValue;
        public event EventHandler RevealFinished;
        public event EventHandler Dismissed;
        public event EventHandler SkipRequested;
        public event EventHandler OpaqueCoverReady;
        public event EventHandler OpaqueCoverFailed;
        public int PresentedFrames { get; private set; }
        public int WaitingFrames { get; private set; }
        public bool OpaqueCoverConfirmed { get; private set; }
        public int CoverRenderCycles { get; private set; }
        public string OpaqueCoverFailureReason { get; private set; }
        public double RevealElapsed { get { return elapsed.Elapsed.TotalSeconds; } }

        public PulseReveal(string stillPath, double duration, bool singleTear = false)
        {
            seconds = duration;
            Title = "Codex · 脉冲衔接";
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true; Background = Brushes.Transparent;
            Topmost = true; ShowInTaskbar = false; ShowActivated = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            if (!File.Exists(stillPath)) throw new FileNotFoundException("The handoff still is missing; refusing a black-only cover.", stillPath);
            BitmapImage still = new BitmapImage(); still.BeginInit();
            still.CacheOption = BitmapCacheOption.OnLoad;
            still.UriSource = new Uri(Path.GetFullPath(stillPath));
            still.EndInit(); still.Freeze();
            if (still.PixelWidth <= 0 || still.PixelHeight <= 0) throw new InvalidDataException("The handoff still has no usable pixels.");
            picture = new PulseSurface(still, singleTear);
            coverTimeout = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            coverTimeout.Tick += OnCoverTimeout;
            // A responsive Dispatcher can outlive a stalled compositor. Keep
            // completion bounded even if no further Rendering callbacks arrive.
            // Normal rendering still owns the visual timeline and finishes first.
            revealTimeout = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(seconds + 1.0) };
            revealTimeout.Tick += OnRevealTimeout;
            var layers = new Grid();
            layers.Children.Add(picture);
            var controls = new Canvas();
            dismiss = new Button { Content = "×", FontSize = 24, Foreground = Brushes.LightBlue,
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), Width = 48, Height = 48,
                ToolTip = "关闭动画并返回已准备的 Codex", Focusable = true };
            dismiss.Click += delegate { if(Dismissed!=null)Dismissed(this,EventArgs.Empty); };
            controls.Children.Add(dismiss);layers.Children.Add(controls);Content=layers;
            SourceInitialized += OnSourceInitialized;
            Closed += delegate { Stop(); };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape || e.Key == Key.Enter || e.Key == Key.Space)
                { e.Handled = true; if (SkipRequested != null) SkipRequested(this, EventArgs.Empty); }
            };
        }
        public void ShowWaiting()
        {
            if(stopped || running || waitingElapsed.IsRunning)return;
            picture.Waiting=true;waitingElapsed.Restart();lastFrame=-1;
            CompositionTarget.Rendering+=RenderWaiting;picture.InvalidateVisual();
        }
        private void RenderWaiting(object sender,EventArgs e)
        {
            double time=waitingElapsed.Elapsed.TotalSeconds;
            double frameNumber=Math.Floor(time*144.0);
            if(frameNumber==lastFrame)return;lastFrame=frameNumber;WaitingFrames++;
            picture.WaitingSeconds=time;picture.InvalidateVisual();
        }
        private void OnSourceInitialized(object sender, EventArgs e)
        { NativeHandoff.DisableOwnTransitions(this); }
        public void SetFilmScreenRect(Point topLeft, Point bottomRight)
        {
            picture.FilmBounds = new Rect(PointFromScreen(topLeft), PointFromScreen(bottomRight));
            Canvas.SetLeft(dismiss,picture.FilmBounds.Right-62);
            Canvas.SetTop(dismiss,picture.FilmBounds.Top+10);
            picture.InvalidateVisual();
        }
        public void ArmOpaqueCover()
        {
            if (stopped || running || OpaqueCoverConfirmed) return;
            coverArmed = true; observedCoverRevision = -1;
            CoverRenderCycles = 0; lastCoverRenderingTime = TimeSpan.MinValue;
            OpaqueCoverFailureReason = null; coverStable.Reset();
            CompositionTarget.Rendering -= RenderOpaqueCover;
            CompositionTarget.Rendering += RenderOpaqueCover;
            coverTimeout.Stop(); coverTimeout.Start();
            picture.InvalidateVisual();
        }
        private void RenderOpaqueCover(object sender, EventArgs e)
        {
            if (!coverArmed || stopped) return;
            var rendering = e as RenderingEventArgs;
            if (rendering == null) return;
            if (!picture.HasCurrentOpaqueRender)
            {
                observedCoverRevision = -1; CoverRenderCycles = 0;
                lastCoverRenderingTime = TimeSpan.MinValue; coverStable.Reset();
                return;
            }
            if (observedCoverRevision != picture.GeometryRevision)
            {
                observedCoverRevision = picture.GeometryRevision;
                CoverRenderCycles = 0; lastCoverRenderingTime = rendering.RenderingTime;
                coverStable.Restart(); return;
            }
            if (rendering.RenderingTime == lastCoverRenderingTime) return;
            lastCoverRenderingTime = rendering.RenderingTime; CoverRenderCycles++;
            // Rendering is a pre-presentation notification, not a GPU fence.
            // Require an actual opaque draw at the final geometry, then allow
            // distinct composition cycles and a short settling interval. The
            // caller keeps its movie alive and adds its compositor flush.
            if (CoverRenderCycles < 2 || coverStable.Elapsed.TotalMilliseconds < 40) return;
            OpaqueCoverConfirmed = true; EndCoverWatch();
            if (OpaqueCoverReady != null) OpaqueCoverReady(this, EventArgs.Empty);
        }
        private void OnCoverTimeout(object sender, EventArgs e)
        {
            if (!coverArmed || stopped) return;
            OpaqueCoverFailureReason = "The correctly sized opaque cover did not settle within one second.";
            EndCoverWatch();
            if (OpaqueCoverFailed != null) OpaqueCoverFailed(this, EventArgs.Empty);
        }
        private void EndCoverWatch()
        {
            coverArmed = false; coverStable.Stop(); coverTimeout.Stop();
            CompositionTarget.Rendering -= RenderOpaqueCover;
        }
        public void StartReveal()
        {
            if (running || stopped) return;
            CompositionTarget.Rendering-=RenderWaiting;waitingElapsed.Stop();lastFrame=-1;
            running = true; picture.Waiting = false;
            elapsed.Restart(); CompositionTarget.Rendering += Render;
            revealTimeout.Start();
        }
        private void OnRevealTimeout(object sender, EventArgs e)
        {
            if (!running || stopped) return;
            Stop();
            LauncherDiagnostics.Write("handoff-latest.log", "Pulse reveal completion deadline reached; releasing the prepared client");
            if (RevealFinished != null) RevealFinished(this, EventArgs.Empty);
        }
        private void Render(object sender, EventArgs e)
        {
            if (!running || stopped) return;
            double time = elapsed.Elapsed.TotalSeconds;
            double frameNumber = Math.Floor(time * 144.0);
            if (frameNumber == lastFrame && time < seconds) return;
            lastFrame = frameNumber; PresentedFrames++;
            picture.Progress = Math.Min(1, time / seconds); picture.InvalidateVisual();
            if (time >= seconds)
            {
                Stop();
                if (RevealFinished != null) RevealFinished(this, EventArgs.Empty);
            }
        }
        private void Stop()
        {
            stopped = true; running = false; elapsed.Stop();
            waitingElapsed.Stop();CompositionTarget.Rendering-=RenderWaiting;
            CompositionTarget.Rendering -= Render; EndCoverWatch();
            revealTimeout.Stop(); revealTimeout.Tick -= OnRevealTimeout;
            coverTimeout.Tick -= OnCoverTimeout; SourceInitialized -= OnSourceInitialized;
        }
    }

    public sealed class PulseSurface : FrameworkElement
    {
        private readonly ImageSource frame;
        private readonly RadialGradientBrush hole;
        private readonly RadialGradientBrush[] rings = new RadialGradientBrush[3];
        private readonly Typeface waitingTypeface = new Typeface("Microsoft YaHei UI");
        private readonly SolidColorBrush waitingBackground = new SolidColorBrush(Color.FromArgb(210, 10, 17, 22));
        private RectangleGeometry fullClip;
        private FormattedText waitingLabel;
        private DrawingGroup waitingCaption;
        private CultureInfo waitingCulture;
        private double waitingDpi, waitingCaptionHeight;
        public double Progress { get; set; }
        public bool Waiting { get; set; }
        public double WaitingSeconds { get; set; }
        private Rect filmBounds;
        private int geometryRevision, opaqueRenderRevision = -1;
        private double opaqueRenderWidth, opaqueRenderHeight;
        public Rect FilmBounds
        {
            get { return filmBounds; }
            set { if (filmBounds == value) return; filmBounds = value; geometryRevision++; InvalidateVisual(); }
        }
        internal int GeometryRevision { get { return geometryRevision; } }
        internal bool HasCurrentOpaqueRender
        {
            get { return frame != null && Progress == 0 && opaqueRenderRevision == geometryRevision &&
                ActualWidth > 0 && ActualHeight > 0 && opaqueRenderWidth == ActualWidth && opaqueRenderHeight == ActualHeight &&
                FilmBounds.Width > 0 && FilmBounds.Height > 0; }
        }
        private readonly bool singleTear;
        public PulseSurface(ImageSource image, bool singleTear = false)
        {
            this.singleTear = singleTear;
            frame = image;
            hole = new RadialGradientBrush { MappingMode = BrushMappingMode.Absolute };
            hole.GradientStops.Add(new GradientStop(Colors.Transparent, 0.90));
            hole.GradientStops.Add(new GradientStop(Colors.Black, 1.0));
            var ring = new RadialGradientBrush { MappingMode = BrushMappingMode.Absolute };
            ring.GradientStops.Add(new GradientStop(Colors.Transparent, 0.83));
            ring.GradientStops.Add(new GradientStop(Color.FromArgb(55, 138, 231, 255), 0.91));
            ring.GradientStops.Add(new GradientStop(Color.FromArgb(215, 240, 253, 255), 0.961));
            ring.GradientStops.Add(new GradientStop(Color.FromArgb(100, 151, 230, 255), 0.987));
            ring.GradientStops.Add(new GradientStop(Colors.Transparent, 1.0));
            for (int i = 0; i < rings.Length; i++) rings[i] = ring.Clone();
            waitingBackground.Freeze();
            IsHitTestVisible = false;
        }
        private RectangleGeometry FullClip(Rect bounds)
        {
            if (fullClip == null || fullClip.Rect != bounds)
            {
                fullClip = new RectangleGeometry(bounds);
                fullClip.Freeze();
            }
            return fullClip;
        }
        private FormattedText WaitingLabel()
        {
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            CultureInfo culture = CultureInfo.CurrentUICulture;
            // The wording and font are stable during the ripple. Keep the
            // shaped text instead of repeating Chinese font fallback/layout
            // on every frame; refresh when either rendering context changes.
            if (waitingLabel == null || waitingDpi != dpi || !Equals(waitingCulture, culture))
            {
                waitingLabel = new FormattedText("正在準備 Codex　點擊右上角關閉動畫", culture,
                    FlowDirection.LeftToRight, waitingTypeface, 14, Brushes.White, dpi);
                waitingCaption = null;
                waitingDpi = dpi; waitingCulture = culture;
            }
            return waitingLabel;
        }
        private DrawingGroup WaitingCaption(double height)
        {
            FormattedText label = WaitingLabel();
            if (waitingCaption == null || waitingCaptionHeight != height)
            {
                // Reuse the retained glyph drawing as well as its layout. The
                // animated rings remain live; only the unchanged caption is
                // frozen, without allocating a bitmap or lowering its quality.
                waitingCaption = new DrawingGroup();
                using (DrawingContext caption = waitingCaption.Open())
                {
                    caption.DrawRoundedRectangle(waitingBackground, null,
                        new Rect(24, height - 68, label.Width + 32, 40), 8, 8);
                    caption.DrawText(label, new Point(40, height - 58));
                }
                waitingCaption.Freeze(); waitingCaptionHeight = height;
            }
            return waitingCaption;
        }
        private static double Ease(double x) { x = Math.Max(0, Math.Min(1, x)); return x * x * (3 - 2 * x); }
        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        { base.OnRenderSizeChanged(sizeInfo); geometryRevision++; }
        protected override void OnRender(DrawingContext dc)
        {
            double width = ActualWidth, height = ActualHeight;
            if (width <= 0 || height <= 0) return;
            var full = new Rect(0, 0, width, height);
            double scale = Math.Min(width / 1280, height / 720);
            var film = new Rect((width - 1280 * scale) / 2, (height - 720 * scale) / 2, 1280 * scale, 720 * scale);
            if (FilmBounds.Width > 0 && FilmBounds.Height > 0) film = FilmBounds;
            // Match the authored droplet. Legacy uses the second drop; the
            // living sequence reveals from its only drop at source frame 1066.
            Point center = new Point(film.X + film.Width * (singleTear ? 0.8953535572861991 : 0.9109514), film.Y + film.Height * (singleTear ? 0.9 : 0.9097222));
            double farX = Math.Max(center.X, width - center.X), farY = Math.Max(center.Y, height - center.Y);
            double maximum = Math.Sqrt(farX * farX + farY * farY) * 1.20;
            double p = Math.Max(0, Math.Min(1, Progress));
            // Impact already has momentum. A zero-speed ease-in looked like a
            // second pause after the movie, even with a prepared replacement.
            // The packaged frame is 10 ms after impact: the existing movie's
            // outer ring is already 14.4 design pixels wide and moving outward.
            double waveTravel = 0.32 * p + 2.36 * p * p - 1.68 * p * p * p;
            double radius = ((singleTear ? 11.9029183 : 14.4) * scale) + maximum * waveTravel;
            double vertical = radius * (0.075 + 0.925 * Ease(p / 0.42));
            hole.Center = hole.GradientOrigin = center; hole.RadiusX = radius; hole.RadiusY = vertical;
            if (p > 0) dc.PushOpacityMask(hole);
            dc.DrawRectangle(Brushes.Black, null, full);
            if (frame != null) dc.DrawImage(frame, film);
            if (p > 0) dc.Pop();
            if (p > 0 && p < 1)
            {
                dc.PushClip(FullClip(full));
                // A bright pulse front reveals the real window in its wake.
                for (int i = 0; i < 3; i++)
                {
                    double r = radius * (1 - i * 0.075);
                    var ring = rings[i];
                    ring.Center = ring.GradientOrigin = center;
                    ring.RadiusX = r; ring.RadiusY = vertical * (1 - i * 0.075);
                    dc.PushOpacity((1 - i * 0.28) * (1 - Ease((p - 0.80) / 0.20)));
                    dc.DrawRectangle(ring, null, full); dc.Pop();
                }
                // One brief pale flash, not repeated whole-screen flickering.
                double flash = 0.10 * Math.Exp(-Math.Pow((p - 0.20) / 0.10, 2));
                dc.PushOpacity(flash); dc.DrawRectangle(Brushes.White, null, full); dc.Pop();
                dc.Pop();
            }
            if (Waiting)
            {
                // Only the small impact area moves during exceptional startup
                // delay; the original character image is never re-drawn.
                dc.PushClip(FullClip(full));
                for(int i=0;i<3;i++){
                    double cycle=(WaitingSeconds/0.85+i/3.0)%1.0;
                    double r=(14.4+115*cycle)*scale;
                    var brush=rings[i];brush.Center=brush.GradientOrigin=center;brush.RadiusX=r;brush.RadiusY=r*0.12;
                    dc.PushOpacity((1-cycle)*0.75);dc.DrawRectangle(brush,null,full);dc.Pop();
                }
                dc.Pop();
                dc.DrawDrawing(WaitingCaption(height));
            }
            if (p == 0 && frame != null)
            {
                opaqueRenderRevision = geometryRevision;
                opaqueRenderWidth = width; opaqueRenderHeight = height;
            }
        }
    }
}

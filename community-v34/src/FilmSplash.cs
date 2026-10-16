using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace DragonLauncher
{
    /// <summary>Plays the packaged film once and then activates the configured application.</summary>
    public sealed class LegacyFilmSplash : Window
    {
        private readonly string filmPath;
        private readonly Settings settings;
        private readonly bool preview;
        private readonly bool quietErrors;
        private readonly MediaElement player;
        private readonly Grid surface;
        private readonly Button cancel;
        private readonly DispatcherTimer watchdog;
        private readonly Stopwatch clock = new Stopwatch();
        private readonly Stopwatch playbackClock = new Stopwatch();
        private bool done, cleaned, opened, playbackReleased;
        private double duration, lastPosition, lastProgress;
        public bool PlaybackSucceeded { get; private set; }

        public LegacyFilmSplash(string path, Settings config, bool onlyPreview, bool quietErrors = false)
        {
            if (config == null) throw new ArgumentNullException("config");
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("未配置启动动画视频路径。", "path");
            filmPath = Path.GetFullPath(path);
            if (!File.Exists(filmPath)) throw new FileNotFoundException("找不到启动动画视频，请保留 output 文件夹。", filmPath);
            settings = config;
            preview = onlyPreview;
            this.quietErrors = quietErrors;
            duration = settings.Duration;

            Title = "GPT 龙娘 · Codex 启动";
            Width = Math.Min(Scene.DesignWidth, SystemParameters.WorkArea.Width * 0.9);
            Height = Width * Scene.DesignHeight / Scene.DesignWidth;
            if (Height > SystemParameters.WorkArea.Height * 0.9)
            {
                Height = SystemParameters.WorkArea.Height * 0.9;
                Width = Height * Scene.DesignWidth / Scene.DesignHeight;
            }
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            // An opaque native window allows WPF's video surface to use hardware composition.
            AllowsTransparency = false;
            Background = Brushes.Black;
            Topmost = true;
            ShowInTaskbar = false;

            player = new MediaElement
            {
                LoadedBehavior = MediaState.Manual,
                UnloadedBehavior = MediaState.Close,
                Stretch = Stretch.Uniform,
                IsMuted = true,
                Volume = 0,
                IsHitTestVisible = false
            };
            surface = new Grid { Background = Brushes.Black };
            surface.Children.Add(player);
            double scale = Width / Scene.DesignWidth;
            cancel = new Button
            {
                Content = "×",
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 24 * scale,
                Foreground = new SolidColorBrush(Color.FromRgb(153, 201, 209)),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Width = 48 * scale,
                Height = 48 * scale,
                Margin = new Thickness(0, 10 * scale, 14 * scale, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                ToolTip = "取消本次启动",
                Focusable = false
            };
            surface.Children.Add(cancel);
            Content = surface;

            watchdog = new DispatcherTimer(DispatcherPriority.Background);
            watchdog.Interval = TimeSpan.FromMilliseconds(250);
            watchdog.Tick += OnWatchdog;
            player.MediaOpened += OnMediaOpened;
            player.MediaEnded += OnMediaEnded;
            player.MediaFailed += OnMediaFailed;
            cancel.Click += OnCancel;
            surface.MouseLeftButtonDown += OnSurfaceMouseDown;
            PreviewKeyDown += OnPreviewKeyDown;
            ContentRendered += OnContentRendered;
            Closed += OnClosed;
        }

        private void OnContentRendered(object sender, EventArgs e)
        {
            ContentRendered -= OnContentRendered;
            if (done) return;
            Activate();
            Focus();
            clock.Start();
            watchdog.Start();
            try
            {
                player.Source = new Uri(filmPath, UriKind.Absolute);
                player.Play();
            }
            catch (Exception error) { Fail("启动动画无法播放。", error); }
        }

        private void OnMediaOpened(object sender, RoutedEventArgs e)
        {
            if (done) return;
            opened = true;
            if (player.NaturalDuration.HasTimeSpan)
                duration = player.NaturalDuration.TimeSpan.TotalSeconds;
            if (double.IsNaN(duration) || double.IsInfinity(duration) || duration <= 0 || duration > 60)
            {
                Fail("启动动画时长无效，视频应在 60 秒以内。", null);
                return;
            }
            lastPosition = player.Position.TotalSeconds;
            lastProgress = clock.Elapsed.TotalSeconds;
            playbackClock.Restart();
        }

        private void OnWatchdog(object sender, EventArgs e)
        {
            if (done) return;
            double elapsed = clock.Elapsed.TotalSeconds;
            if (!opened)
            {
                if (elapsed >= 10) Fail("视频解码器在 10 秒内未能打开启动动画。", null);
                return;
            }
            double position = player.Position.TotalSeconds;
            if (position > lastPosition + 0.005)
            {
                lastPosition = position;
                lastProgress = elapsed;
            }
            if (elapsed - lastProgress > 5)
                Fail("启动动画播放停止超过 5 秒，已停止动画播放。", null);
            else if (elapsed > duration + 10)
                Fail("启动动画超出预期播放时长，已停止动画播放。", null);
        }

        private void OnMediaEnded(object sender, RoutedEventArgs e)
        {
            if (done) return;
            PlaybackSucceeded = true;
            playbackClock.Stop();
            if (preview)
            {
                try
                {
                    string output = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "output");
                    Directory.CreateDirectory(output);
                    File.WriteAllText(Path.Combine(output, "native-preview-validation.txt"),
                        "PASS: native MediaElement playback reached MediaEnded. App activation was not executed.\n" +
                        "NaturalVideoWidth: " + player.NaturalVideoWidth + "\n" +
                        "NaturalVideoHeight: " + player.NaturalVideoHeight + "\n" +
                        "NaturalDurationSeconds: " + duration.ToString("F6", System.Globalization.CultureInfo.InvariantCulture) + "\n" +
                        "PlaybackElapsedSeconds: " + playbackClock.Elapsed.TotalSeconds.ToString("F6", System.Globalization.CultureInfo.InvariantCulture) + "\n" +
                        "Video: " + filmPath);
                }
                catch (Exception error)
                {
                    // Only --preview-test requires a writable validation log.
                    if (quietErrors)
                    {
                        PlaybackSucceeded = false;
                        Fail("启动动画播放完成，但无法保存验证日志。", error);
                        return;
                    }
                }
            }
            LaunchClose();
        }
        private void OnMediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            Fail("启动动画无法解码。请确认 MP4 文件完整，Windows 已启用媒体播放组件。", e.ErrorException);
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape && e.Key != Key.Enter && e.Key != Key.Space) return;
            e.Handled = true;
            LaunchClose();
        }

        private void OnSurfaceMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (done || e.ChangedButton != MouseButton.Left) return;
            Point point = e.GetPosition(surface);
            double scale = ActualWidth / Scene.DesignWidth;
            if (point.Y >= 70 * scale) return;
            e.Handled = true;
            try { DragMove(); }
            catch (InvalidOperationException) { }
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (cleaned) return;
            done = true;
            Close();
        }

        private void LaunchClose()
        {
            if (done) return;
            done = true;
            ReleasePlayback();
            if (preview) { Close(); return; }
            OpenApplicationAndClose();
        }

        private void OpenApplicationAndClose()
        {
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (cleaned) return;
                try { AppActivation.Open(settings); }
                catch (Exception error)
                {
                    Topmost = false;
                    MessageBox.Show(this,
                        "无法打开 Codex。请检查 launcher.config.xml 中的应用标识或路径。\n\n" + error.Message,
                        "GPT 龙娘启动器", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally { Close(); }
            }), DispatcherPriority.Background);
        }

        private void Fail(string message, Exception error)
        {
            if (done) return;
            done = true;
            ReleasePlayback();
            string details = message + (error == null ? "" : "\n\n" + error.Message);
            try
            {
                string output = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "output");
                Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output, "launcher-media-error.txt"),
                    details + "\n\nVideo: " + filmPath + (error == null ? "" : "\n\n" + error));
            }
            catch { }
            // The animation is optional; a playback failure must not prevent
            // a normal launch. Explicit cancellation never enters this path.
            if (!preview) { OpenApplicationAndClose(); return; }
            if (quietErrors) { Close(); return; }
            Topmost = false;
            try
            {
                MessageBox.Show(this, details, "GPT 龙娘启动器", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { Close(); }
        }

        private void ReleasePlayback()
        {
            if (playbackReleased) return;
            playbackReleased = true;
            watchdog.Stop();
            watchdog.Tick -= OnWatchdog;
            clock.Stop();
            playbackClock.Stop();
            player.MediaOpened -= OnMediaOpened;
            player.MediaEnded -= OnMediaEnded;
            player.MediaFailed -= OnMediaFailed;
            // Stop only rewinds media. Close releases decoder resources before
            // activation or an error dialog can keep this window alive.
            try { player.Close(); }
            catch { }
            // Keep this independent so a Close failure cannot skip the reset.
            try { player.Source = null; }
            catch { }
        }

        private void OnClosed(object sender, EventArgs e)
        {
            if (cleaned) return;
            cleaned = true;
            done = true;
            ReleasePlayback();
            cancel.Click -= OnCancel;
            surface.MouseLeftButtonDown -= OnSurfaceMouseDown;
            PreviewKeyDown -= OnPreviewKeyDown;
            ContentRendered -= OnContentRendered;
            Closed -= OnClosed;
        }
    }
}

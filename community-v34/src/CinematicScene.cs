using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace DragonLauncher
{
    public sealed class Scene : FrameworkElement
    {
        public const double DesignWidth = 1280, DesignHeight = 720;
        public const int ExportWidth = 2560, ExportHeight = 1440;
        private readonly Settings settings;
        private readonly bool reducedMotion;
        private readonly bool wallpaper;
        private double wallpaperPhase;
        private const string HeaderMessage = "EXPLORE WITH REASON   RESPOND WITH KINDNESS";
        private const string HeaderTranslation = "以理性探索無限　以溫柔回應世界";
        private const string Greeting = "盡情瘋狂吧　我的架構師";
        private readonly double headerRuleStart;
        private readonly BitmapImage opened, closed;
        private readonly StrokeTrace trace;
        private readonly StrokeTrace mesh;
        private readonly Typeface mono = new Typeface("Consolas");
        private readonly Typeface chinese;
        private readonly Typeface display = new Typeface(new FontFamily("DengXian, Microsoft YaHei UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        private readonly Geometry titleGlyph, greetingGlyph, englishGlyph;
        private readonly Brush displayFill;
        private readonly DrawingGroup portrait;
        private readonly EyeRig eyes;
        private readonly FaceDetailAssets faceDetail;
        private readonly Point[] eyeCenters = new Point[2];
        private readonly Geometry[] irisSymbols = new Geometry[8];
        private readonly HairBand[] hairBands;
        private readonly int leftAnchor, rightAnchor;
        private readonly Geometry[] rainGlyphs = new Geometry[6];
        private readonly Brush[] rainBrushes = new Brush[9];
        private readonly Pen scanlinePen = new Pen(Alpha(0.014, 3, 6, 9), 0.6);
        private readonly LightBloom bloom;
        private readonly Rect bloomArea;
        private BitmapSource bloomBitmap;
        private StrokeTrace.MotionFrame bloomLines, bloomMesh;
        private sealed class LightBloom : FrameworkElement
        {
            public Drawing Drawing;
            public Rect Area;
            protected override void OnRender(DrawingContext dc)
            {
                dc.PushTransform(new ScaleTransform(0.5, 0.5));
                dc.PushTransform(new TranslateTransform(-Area.X, -Area.Y));
                if (Drawing != null) dc.DrawDrawing(Drawing);
                dc.Pop(); dc.Pop();
            }
        }
        private sealed class HairBand
        {
            public int Y, Height;
            public BitmapSource Left, Middle, Right;
        }
        public double Time;
        public bool Launching;

        public Scene(string root, Settings config, bool reduced)
            : this(root, config, reduced, false) { }

        public Scene(string root, Settings config, bool reduced, bool wallpaperMode)
        {
            settings = config; reducedMotion = reduced; wallpaper = wallpaperMode;
            chinese = LoadChineseTypeface(root);
            if (wallpaper) Time = 4.75 * settings.Duration / 8;
            Width = DesignWidth; Height = DesignHeight;
            headerRuleStart = 53 + new FormattedText(HeaderMessage, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
                mono, 13.5, Brushes.White, 1.0).WidthIncludingTrailingWhitespace + 14;
            titleGlyph = LetterGeometry("Codex", display, 132, 64, 249, 0.3);
            greetingGlyph = LetterGeometry(Greeting, chinese, 25, 87, 430, 3.0);
            englishGlyph = LetterGeometry("GO WILD  MY ARCHITECT", display, 16, 88, 468, 1.0);
            var textGradient = new LinearGradientBrush();
            textGradient.StartPoint = new Point(0, 0); textGradient.EndPoint = new Point(0.85, 1);
            textGradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#C4F6EC"), 0));
            textGradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#EFFAFF"), 0.46));
            textGradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#75BEDB"), 1));
            textGradient.Freeze(); displayFill = textGradient;
            opened = LoadImage(root, config.Image); closed = LoadImage(root, config.ClosedImage);
            if (opened.PixelWidth != closed.PixelWidth || opened.PixelHeight != closed.PixelHeight)
                throw new InvalidDataException("睁眼与闭眼素材必须具有相同尺寸。");
            if (opened.PixelWidth < 64 || opened.PixelHeight < 64)
                throw new InvalidDataException("龙娘特写素材尺寸过小。");
            // Schedule the camera-visible face and hair, rather than spending
            // drawing time on the full body's offscreen contours.
            // The reduced-motion image never draws paths, bloom or moving hair.
            // Avoid computing hundreds of unused geometry frames for that image.
            if (!reducedMotion && !wallpaper)
            {
                trace = new StrokeTrace(closed, new Rect(0, opened.PixelHeight * 0.215,
                    opened.PixelWidth * 0.59, opened.PixelHeight * 0.425));
                mesh = BuildCurveMesh(closed);
                bloomArea = new Rect(0, opened.PixelHeight * 0.205, opened.PixelWidth * 0.60, opened.PixelHeight * 0.445);
                bloom = new LightBloom { Area = bloomArea, Width = Math.Ceiling(bloomArea.Width / 2), Height = Math.Ceiling(bloomArea.Height / 2),
                    Effect = new BlurEffect { Radius = 3.6, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Quality } };
                bloom.Measure(new Size(bloom.Width, bloom.Height));
                bloom.Arrange(new Rect(0, 0, bloom.Width, bloom.Height));
            }
            portrait = new DrawingGroup();
            using (var dc = portrait.Open()) dc.DrawImage(closed, new Rect(0, 0, opened.PixelWidth, opened.PixelHeight));
            portrait.Freeze();
            eyeCenters[0] = new Point(opened.PixelWidth * 0.424, opened.PixelHeight * 0.441);
            eyeCenters[1] = new Point(opened.PixelWidth * 0.629, opened.PixelHeight * 0.407);
            string[] symbols = { "0", "1", "<", ">", "/", "{", "}", "_" };
            for (int i = 0; i < symbols.Length; i++)
            {
                irisSymbols[i] = new FormattedText(symbols[i], CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    mono, 5, Brushes.White, 1.0).BuildGeometry(new Point(0, 0));
                irisSymbols[i].Freeze();
            }
            faceDetail = FaceDetailAssets.Load(root);
            // Wallpaper eyes are baked into the loop offline, so the installed
            // player still decodes just one video and holds no pose atlas.
            eyes = new EyeRig(root, opened, reducedMotion, faceDetail);
            leftAnchor = (int)(opened.PixelWidth * 0.32);
            rightAnchor = (int)(opened.PixelWidth * 0.79);
            hairBands = new HairBand[reducedMotion ? 0 : (opened.PixelHeight + 23) / 24];
            for (int i = 0; i < hairBands.Length; i++)
            {
                int y = i * 24, height = Math.Min(24, opened.PixelHeight - y);
                // Bleed source pixels across rows to keep resampling from exposing a dark tile edge.
                int top = Math.Max(0, y - 2), bottom = Math.Min(opened.PixelHeight, y + height + 2);
                var band = new HairBand { Y = top, Height = bottom - top };
                band.Left = new CroppedBitmap(closed, new Int32Rect(0, top, leftAnchor + 2, band.Height));
                band.Middle = new CroppedBitmap(closed, new Int32Rect(leftAnchor - 2, top, rightAnchor - leftAnchor + 4, band.Height));
                band.Right = new CroppedBitmap(closed, new Int32Rect(rightAnchor - 2, top, opened.PixelWidth - rightAnchor + 2, band.Height));
                band.Left.Freeze(); band.Middle.Freeze(); band.Right.Freeze(); hairBands[i] = band;
            }
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
            string[] glyphs = { "0", "1", ":", ".", "+", "/" };
            for (int i = 0; i < glyphs.Length; i++)
            {
                rainGlyphs[i] = new FormattedText(glyphs[i], CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    mono, 9, Brushes.White, 1.0).BuildGeometry(new Point(0, 0));
                rainGlyphs[i].Freeze();
            }
            for (int i = 0; i < rainBrushes.Length; i++)
            {
                rainBrushes[i] = Alpha(0.035 + i * 0.004, 56, 142, 128);
                rainBrushes[i].Freeze();
            }
            scanlinePen.Freeze();
        }
        private static BitmapImage LoadImage(string root, string relative)
        {
            var file = Path.GetFullPath(Path.Combine(root, relative));
            if (!File.Exists(file)) throw new FileNotFoundException("找不到动画素材，请保留 assets 文件夹。", file);
            var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(file); bitmap.EndInit(); bitmap.Freeze(); return bitmap;
        }
        private static Typeface LoadChineseTypeface(string root)
        {
            string folder = Path.GetFullPath(Path.Combine(root, "assets", "fonts")) + Path.DirectorySeparatorChar;
            string file = Path.Combine(folder, "LXGWWenKaiTC-Light.ttf");
            if (!File.Exists(file)) throw new FileNotFoundException("Missing bundled Traditional Chinese font.", file);
            var face = new Typeface(new FontFamily(new Uri(folder), "./#LXGW WenKai TC"),
                FontStyles.Normal, FontWeights.Light, FontStretches.Normal);
            GlyphTypeface glyph;
            if (!face.TryGetGlyphTypeface(out glyph) || !String.Equals(Path.GetFullPath(glyph.FontUri.LocalPath), file, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The bundled Traditional Chinese typeface did not resolve directly.");
            foreach (char c in HeaderTranslation + Greeting + "正在開啟 Codex正在描繪龍娘輪廓正在讓角色顯影龍娘正在甦醒準備出發ESC 跳過並開啟 Codex")
            {
                ushort index;
                if (!glyph.CharacterToGlyphMap.TryGetValue(c, out index) || index == 0)
                    throw new InvalidDataException("The bundled typeface is missing U+" + ((int)c).ToString("X4"));
            }
            return face;
        }
        public void SetTime(double seconds)
        {
            double quantized = Math.Floor(Math.Max(0, seconds) * settings.FrameRate + 0.000001) / settings.FrameRate;
            if (Math.Abs(quantized - Time) < 0.000001) return;
            Time = quantized;
            InvalidateVisual();
        }
        public void SetWallpaperTime(double seconds, double loopSeconds)
        {
            if (!wallpaper) throw new InvalidOperationException("This scene is not a wallpaper export.");
            if (Double.IsNaN(seconds) || Double.IsInfinity(seconds) || Double.IsNaN(loopSeconds) || Double.IsInfinity(loopSeconds) || loopSeconds <= 0)
                throw new ArgumentOutOfRangeException("loopSeconds");
            // Keep every wallpaper channel periodic. In particular, do not
            // replay startup's modulo-scrolling particles backwards: they jump
            // at their internal wrap points even when endpoint pixels match.
            wallpaperPhase = (seconds % loopSeconds + loopSeconds) % loopSeconds / loopSeconds;
            double u = 4.75 + 1.4 * (1 - Math.Cos(wallpaperPhase * Math.PI * 2)) / 2;
            Time = u * settings.Duration / 8;
            InvalidateVisual();
        }
        private static double Clamp(double v) { return Math.Max(0, Math.Min(1, v)); }
        private static double Smooth(double v) { v = Clamp(v); return v * v * (3 - 2 * v); }
        private static double Ease(double v) { return 1 - Math.Pow(1 - Clamp(v), 3); }
        private static Brush ColorBrush(string hex) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        private static Brush Alpha(double alpha, byte r, byte g, byte b) { return new SolidColorBrush(Color.FromArgb((byte)(Clamp(alpha) * 255), r, g, b)); }
        private static Geometry LetterGeometry(string text, Typeface face, double size, double x, double y, double tracking)
        {
            var geometry = new GeometryGroup();
            foreach (char letter in text)
            {
                var formatted = new FormattedText(letter.ToString(), CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
                    face, size, Brushes.White, 2.0);
                geometry.Children.Add(formatted.BuildGeometry(new Point(x, y)));
                x += formatted.WidthIncludingTrailingWhitespace + tracking;
            }
            geometry.Freeze(); return geometry;
        }
        private void Text(DrawingContext dc, string text, double x, double y, double size, string color, bool code)
        {
            dc.DrawText(new FormattedText(text, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
                code ? mono : chinese, size, ColorBrush(color), 1.0), new Point(x, y));
        }
        private static StrokeTrace BuildCurveMesh(BitmapSource source)
        {
            int w = source.PixelWidth, h = source.PixelHeight;
            var bytes = new byte[w * h * 4];
            new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0).CopyPixels(bytes, w * 4, 0);
            Func<Point, bool> visible = p => p.X > 0 && p.Y > h * 0.215 && p.X < w * 0.59 && p.Y < h * 0.64 && bytes[((int)p.Y * w + (int)p.X) * 4 + 3] > 110;
            var result = new List<Point[]>();
                // Surface latitude/longitude curves follow the face; no planar triangle lattice.
                for (double row = -0.9; row <= 0.91; row += 0.085)
                {
                    double latitude = row;
                    PlotCurve(result, t =>
                    {
                        double u = (t * 2 - 1);
                        double spread = Math.Sqrt(1 - latitude * latitude);
                        return new Point(w * 0.53 + u * w * 0.225 * spread,
                            h * 0.465 + latitude * h * 0.16 + (45 + 22 * spread) * u * u * spread);
                    }, visible);
                }
                for (double column = -0.9; column <= 0.91; column += 0.095)
                {
                    double longitude = column;
                    PlotCurve(result, t =>
                    {
                        double v = t * 1.9 - 0.95;
                        return new Point(w * 0.53 + longitude * w * 0.225 * Math.Sqrt(1 - v * v),
                            h * 0.465 + v * h * 0.16 + 15 * longitude * longitude);
                    }, visible);
                }
                // Arched cross-sections add flow to the hair, while the source contours supply its real strands.
                for (double band = 0.20; band < 0.64; band += 0.027)
                {
                    double y = band;
                    PlotCurve(result, t => new Point(w * (0.09 + t * 0.85),
                        h * y + h * 0.045 * Math.Sin(t * Math.PI) + w * 0.015 * Math.Sin(t * 7 + y * 4)), visible);
                }
            return new StrokeTrace(result);
        }
        private static void PlotCurve(List<Point[]> curves, Func<double, Point> curve, Func<Point, bool> visible)
        {
            var points = new List<Point>();
            for (int i = 0; i <= 120; i++)
            {
                Point point = curve(i / 120.0);
                if (!visible(point))
                {
                    if (points.Count > 1) curves.Add(points.ToArray());
                    points.Clear(); continue;
                }
                points.Add(point);
            }
            if (points.Count > 1) curves.Add(points.ToArray());
        }
        private static double EyeOpening(double time)
        {
            double x = Clamp((time - 3.25) / 1.5);
            return Smooth(x);
        }
        private static double Blink(double u, double start, double end)
        {
            if (u <= start || u >= end) return 0;
            double phase = (u - start) / (end - start);
            // Slower opening than closing; both ends have zero velocity.
            return phase < 0.44 ? Smooth(phase / 0.44) : 1 - Smooth((phase - 0.44) / 0.56);
        }
        private static void Cross(DrawingContext dc, double x, double y, double length, double opacity)
        {
            var pen = new Pen(Alpha(opacity, 118, 212, 218), 0.8);
            dc.DrawLine(pen, new Point(x - length, y), new Point(x + length, y));
            dc.DrawLine(pen, new Point(x, y - length), new Point(x, y + length));
        }
        private static void Bokeh(DrawingContext dc, double x, double y, double radius, double opacity)
        {
            var glow = new RadialGradientBrush();
            glow.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(Clamp(opacity) * 255), 39, 178, 207), 0));
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(0, 25, 119, 177), 1));
            dc.DrawEllipse(glow, null, new Point(x, y), radius, radius);
        }
        private void PaintPortrait(DrawingContext dc, Rect rectangle, double awake)
        {
            dc.PushTransform(new TranslateTransform(rectangle.X, rectangle.Y));
            dc.PushTransform(new ScaleTransform(rectangle.Width / opened.PixelWidth, rectangle.Height / opened.PixelHeight));
            if (reducedMotion) dc.DrawDrawing(portrait);
            else
            {
                DrawHair(dc);
            }
            if (faceDetail != null) faceDetail.DrawClosed(dc);
            eyes.Draw(dc, awake, reducedMotion ? Double.NaN : Time);
            // Eye material is authored into the portrait textures. No program
            // glyphs or lens-shaped overlay are painted over the character.
            dc.Pop(); dc.Pop();
        }
        private void PaintTearFinale(DrawingContext dc, Rect character, double u, double awake)
        {
            // Authored into the startup video only. The wallpaper and reduced
            // motion paths never enter this finale or its blinking poses.
            if (wallpaper || reducedMotion || u < 4.95) return;
            double sx = character.Width / opened.PixelWidth, sy = character.Height / opened.PixelHeight;
            double wet = Smooth((u - 4.95) / 1.25);
            dc.PushTransform(new TranslateTransform(character.X, character.Y));
            dc.PushTransform(new ScaleTransform(sx, sy));
            for (int eye = 0; eye < eyeCenters.Length; eye++)
            {
                Point center = eyeCenters[eye];
                var film = new StreamGeometry();
                using (var path = film.Open())
                {
                    // A small lens on the lower iris edge, not an illuminated
                    // underline crossing the white of the eye.
                    path.BeginFigure(new Point(center.X - 7, center.Y + 23), true, true);
                    path.QuadraticBezierTo(new Point(center.X + 9, center.Y + 24), new Point(center.X + 26, center.Y + 14), true, false);
                    path.QuadraticBezierTo(new Point(center.X + 11, center.Y + 28), new Point(center.X - 7, center.Y + 23), true, false);
                }
                double shine = wet * Smooth(awake / 0.8);
                var filmGlass = new LinearGradientBrush();
                filmGlass.StartPoint = new Point(0, 0); filmGlass.EndPoint = new Point(0.2, 1);
                filmGlass.GradientStops.Add(new GradientStop(Color.FromArgb(0, 203, 244, 255), 0));
                filmGlass.GradientStops.Add(new GradientStop(Color.FromArgb(86, 178, 224, 245), 0.55));
                filmGlass.GradientStops.Add(new GradientStop(Color.FromArgb(150, 224, 248, 255), 0.78));
                filmGlass.GradientStops.Add(new GradientStop(Color.FromArgb(12, 101, 184, 228), 1));
                dc.PushOpacity(shine);
                dc.DrawGeometry(filmGlass, null, film);
                dc.DrawEllipse(Alpha(0.53, 239, 251, 255), null, new Point(center.X + 9, center.Y + 24), 3.5, 0.45);
                dc.Pop();
                Bokeh(dc, center.X + 10, center.Y + 23, 4, shine * 0.08);
            }
            dc.Pop(); dc.Pop();

            // The visible eye pools tears first. Two droplets then slide down
            // its cheek, accelerate toward the lower optical plane and break
            // into expanding rings. The paths share their impact positions.
            Point origin = new Point(character.X + (eyeCenters[0].X + 10) * sx,
                character.Y + (eyeCenters[0].Y + 23) * sy);
            for (int tear = 0; tear < 2; tear++)
            {
                double release = 6.35 + tear * 0.24, impact = 7.40 + tear * 0.19;
                double progress = Clamp((u - release) / (impact - release));
                double slide = progress < 0.38 ? 0.20 * Smooth(progress / 0.38) :
                    0.20 + 0.80 * Math.Pow((progress - 0.38) / 0.62, 2);
                Point source = new Point(origin.X + tear * 7, origin.Y - tear * 2);
                Point landing = new Point(source.X + 17 + tear * 13, 648 + tear * 7);
                Point point = new Point(source.X + (landing.X - source.X) * Ease(progress),
                    source.Y + (landing.Y - source.Y) * slide);
                double pool = Smooth((u - (5.45 + tear * 0.38)) / (release - (5.45 + tear * 0.38)));
                if (u < impact && pool > 0)
                {
                    double trailLength = Math.Min(18, Math.Max(0, point.Y - source.Y));
                    if (trailLength > 0.5)
                    {
                        var trail = new StreamGeometry();
                        using (var path = trail.Open())
                        {
                            path.BeginFigure(new Point(point.X - 1.5, point.Y - trailLength), false, false);
                            path.QuadraticBezierTo(new Point(point.X - 0.5, point.Y - trailLength * 0.45),
                                new Point(point.X, point.Y - 4), true, false);
                        }
                        double trailOpacity = Smooth(progress / 0.25) * 0.11;
                        dc.DrawGeometry(null, StrokePen(trailOpacity, 157, 231, 247, 0.7), trail);
                    }
                    double radius = (tear == 0 ? 7.2 : 4.0) * (0.25 + 0.75 * pool);
                    PaintCodeTear(dc, point, radius, radius * (1.35 + progress * 0.55), u, tear, pool);
                }
                double age = u - impact;
                if (age >= 0 && age < 0.65)
                {
                    double fade = 1 - Smooth(age / 0.65);
                    Bokeh(dc, landing.X, landing.Y, 30 + age * 65, fade * 0.32);
                    for (int ring = 0; ring < 3; ring++)
                    {
                        double elapsed = age - ring * 0.065;
                        if (elapsed < 0) continue;
                        double radius = 6 + Math.Sqrt(elapsed) * (tear == 0 ? 112 : 84);
                        dc.DrawEllipse(null, StrokePen(fade * (0.46 - ring * 0.10), 125, 232, 254, 0.65),
                            landing, radius, radius * 0.075);
                    }
                    for (int i = 0; i < 14; i++)
                    {
                        double direction = i * 2.39996 + tear;
                        double speed = 22 + i % 5 * 13;
                        double x = landing.X + Math.Cos(direction) * age * speed;
                        double y = landing.Y - Math.Abs(Math.Sin(direction)) * age * speed * 0.8 + age * age * 24;
                        dc.DrawEllipse(Alpha(fade * (0.24 + i % 3 * 0.1), 174, 241, 255), null,
                            new Point(x, y), i % 4 == 0 ? 1.05 : 0.55, i % 4 == 0 ? 1.05 : 0.55);
                    }
                }
            }
        }
        private void PaintCodeTear(DrawingContext dc, Point point, double rx, double ry, double u, int tear, double visibility)
        {
            dc.PushOpacity(visibility);
            dc.PushTransform(new TranslateTransform(point.X, point.Y));
            var drop = new StreamGeometry();
            using (var path = drop.Open())
            {
                path.BeginFigure(new Point(0, -ry), true, true);
                path.BezierTo(new Point(rx * 0.15, -ry * 0.28), new Point(rx * 1.15, -ry * 0.05), new Point(rx, ry * 0.42), true, false);
                path.BezierTo(new Point(rx * 0.7, ry * 1.08), new Point(-rx * 0.7, ry * 1.08), new Point(-rx, ry * 0.42), true, false);
                path.BezierTo(new Point(-rx * 1.15, -ry * 0.05), new Point(-rx * 0.15, -ry * 0.28), new Point(0, -ry), true, false);
            }
            var glass = new RadialGradientBrush();
            glass.GradientOrigin = new Point(0.32, 0.37); glass.Center = new Point(0.46, 0.56);
            glass.RadiusX = 0.68; glass.RadiusY = 0.68;
            glass.GradientStops.Add(new GradientStop(Color.FromArgb(115, 218, 248, 255), 0));
            glass.GradientStops.Add(new GradientStop(Color.FromArgb(55, 150, 221, 250), 0.34));
            glass.GradientStops.Add(new GradientStop(Color.FromArgb(138, 63, 136, 199), 0.76));
            glass.GradientStops.Add(new GradientStop(Color.FromArgb(172, 200, 242, 255), 1));
            dc.DrawGeometry(glass, StrokePen(0.55, 192, 241, 255, 0.45), drop);
            dc.PushClip(drop);
            for (int column = 0; column < 3; column++)
            {
                double drift = (u * (7.0 + column * 1.3) + column * 1.7 + tear) % 4.7;
                for (int row = -1; row < 6; row++)
                {
                    dc.PushTransform(new TranslateTransform(-rx * 0.65 + column * rx * 0.47, -ry + row * 4.7 + drift));
                    dc.PushTransform(new ScaleTransform(tear == 0 ? 0.82 : 0.48, tear == 0 ? 0.82 : 0.48));
                    dc.DrawGeometry(Alpha(column == 1 ? 0.75 : 0.50, 134, 249, 255), null, irisSymbols[(row + column + tear + 4) % 2]);
                    dc.Pop(); dc.Pop();
                }
            }
            dc.Pop();
            // A short specular arc and a lower caustic describe liquid volume.
            var specular = new StreamGeometry();
            using (var path = specular.Open())
            {
                path.BeginFigure(new Point(-rx * 0.56, -ry * 0.12), false, false);
                path.QuadraticBezierTo(new Point(-rx * 0.76, ry * 0.22), new Point(-rx * 0.49, ry * 0.43), true, false);
            }
            dc.DrawGeometry(null, StrokePen(0.79, 241, 253, 255, 0.75), specular);
            dc.DrawEllipse(Alpha(0.50, 224, 251, 255), null, new Point(rx * 0.20, ry * 0.68), rx * 0.28, ry * 0.055);
            dc.Pop(); dc.Pop();
        }
        private void DrawHair(DrawingContext dc)
        {
            double u = Time * 8 / settings.Duration;
            double hairPhase = wallpaper ? wallpaperPhase * Math.PI * 4 : u * 1.9;
            foreach (var band in hairBands)
            {
                // Source bleed overlaps the fixed face anchors, avoiding vertical filter seams.
                double left = Math.Sin(hairPhase + band.Y * 0.006) * 4.5 * Smooth((band.Y - 400) / 480.0);
                double right = Math.Sin(hairPhase + band.Y * 0.006 + 1.5) * 4.5 * Smooth((band.Y - 790) / 480.0);
                double leftScale = (leftAnchor - left) / leftAnchor;
                double rightScale = (opened.PixelWidth - rightAnchor + right) / (opened.PixelWidth - rightAnchor);
                dc.DrawImage(band.Left, new Rect(left, band.Y, (leftAnchor + 2) * leftScale, band.Height));
                dc.DrawImage(band.Middle, new Rect(leftAnchor - 2, band.Y, rightAnchor - leftAnchor + 4, band.Height));
                dc.DrawImage(band.Right, new Rect(rightAnchor - 2 * rightScale, band.Y, (opened.PixelWidth - rightAnchor + 2) * rightScale, band.Height));
            }
        }
        private static double GreetingGlitch(double u)
        {
            double distance = Math.Min(Math.Abs(u - 5.2), Math.Min(Math.Abs(u - 6.4), Math.Abs(u - 7.2)));
            if (distance >= 0.06) return 0;
            double envelope = Math.Cos(distance / 0.06 * Math.PI / 2);
            return envelope * envelope;
        }
        private void PaintGreetingGlyphs(DrawingContext dc, double u)
        {
            dc.DrawGeometry(null, StrokePen(0.045, 38, 192, 209, 3), greetingGlyph);
            dc.DrawGeometry(null, StrokePen(0.12, 76, 216, 230, 1.1), greetingGlyph);
            dc.DrawGeometry(null, StrokePen(0.55, 147, 234, 241, 0.35), greetingGlyph);
            dc.DrawGeometry(displayFill, null, greetingGlyph);
            dc.PushOpacity(0.68 * Smooth((u - 4.15) / 0.75));
            dc.DrawGeometry(displayFill, null, englishGlyph);
            dc.Pop();
        }
        private void PaintGreetingGlitch(DrawingContext dc, double u, double strength)
        {
            // Only the two small slogan lines participate. Neither the title,
            // header, character nor background enters these small strip clips.
            for (int row = 0; row < 2; row++)
            {
                Rect strip = new Rect(82, row == 0 ? 445 : 480, 390, row == 0 ? 2.2 : 1.8);
                double shift = (row == 0 ? 1.6 : -1.4) * strength;
                dc.PushClip(new RectangleGeometry(strip));
                dc.PushTransform(new TranslateTransform(shift, 0));
                PaintGreetingGlyphs(dc, u);
                dc.PushTransform(new TranslateTransform(1.0 * strength, 0));
                dc.DrawGeometry(null, StrokePen(0.15 * strength, 64, 219, 255, 0.45), row == 0 ? greetingGlyph : englishGlyph);
                dc.Pop(); dc.Pop(); dc.Pop();
            }
            dc.PushClip(new RectangleGeometry(new Rect(80, 425, 390, 73)));
            for (int i = 0; i < 10; i++)
            {
                bool chinese = i % 2 == 0;
                double x = 88 + i * 37 % (chinese ? 326 : 220);
                double y = (chinese ? 445 : 480) + Math.Sin(i * 2.4) * (chinese ? 11 : 6);
                dc.DrawRectangle(Alpha(strength * (0.14 + i % 3 * 0.035), 103, 229, 252), null,
                    new Rect(x, y, i % 3 == 0 ? 1.6 : 0.7, 0.7));
            }
            dc.Pop();
        }
        private static Pen StrokePen(double opacity, byte r, byte g, byte b, double width)
        {
            return new Pen(Alpha(opacity, r, g, b), width) {
                StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        }
        private static void PaintLightPaths(DrawingContext dc, StrokeTrace.MotionFrame frame, double strength, bool surface)
        {
            // The broad bloom is blurred separately. These narrow strokes keep
            // the moving head and its tapered wake crisp inside that light.
            double width = surface ? 0.45 : 0.68;
            dc.DrawGeometry(null, StrokePen(strength * 0.09, 42, 206, 250, 2.0), frame.Lines);
            dc.DrawGeometry(null, StrokePen(strength * 0.76, 113, 231, 248, width), frame.Lines);
            dc.DrawGeometry(null, StrokePen(strength * 0.16, 41, 210, 255, 2.4), frame.Tail);
            dc.DrawGeometry(null, StrokePen(strength * 0.42, 166, 244, 255, width + 0.25), frame.Tail);
            dc.DrawGeometry(null, StrokePen(strength * 0.65, 203, 252, 255, width + 0.32), frame.Light);
            dc.DrawGeometry(null, StrokePen(strength, 239, 255, 255, width + 0.40), frame.Hot);
            foreach (Point head in frame.Heads)
            {
                Bokeh(dc, head.X, head.Y, surface ? 3.2 : 6, strength * 0.65);
                dc.DrawEllipse(Alpha(strength * 0.95, 233, 255, 255), null, head, surface ? 0.43 : 0.72, surface ? 0.43 : 0.72);
            }
        }
        private void PaintBloom(DrawingContext dc, StrokeTrace.MotionFrame lines, StrokeTrace.MotionFrame surface)
        {
            if (bloomBitmap == null || !Object.ReferenceEquals(lines, bloomLines) || !Object.ReferenceEquals(surface, bloomMesh))
            {
                var drawing = new DrawingGroup();
                using (var glow = drawing.Open())
                {
                    glow.DrawGeometry(null, StrokePen(0.26, 23, 161, 251, 2.8), surface.Lines);
                    glow.DrawGeometry(null, StrokePen(0.42, 23, 178, 255, 3.8), lines.Lines);
                    glow.DrawGeometry(null, StrokePen(0.75, 27, 197, 255, 5.8), lines.Tail);
                    glow.DrawGeometry(null, StrokePen(0.50, 75, 233, 255, 4.2), lines.Hot);
                }
                drawing.Freeze(); bloom.Drawing = drawing; bloom.InvalidateVisual(); bloom.UpdateLayout();
                var image = new RenderTargetBitmap((int)bloom.Width, (int)bloom.Height, 96, 96, PixelFormats.Pbgra32);
                image.Render(bloom); image.Freeze(); bloomBitmap = image;
                bloomLines = lines; bloomMesh = surface;
            }
            dc.DrawImage(bloomBitmap, new Rect(bloomArea.X, bloomArea.Y, bloom.Width * 2, bloom.Height * 2));
        }
        private void PaintWireframe(DrawingContext dc, Rect rectangle, double opacity, double progress, double meshProgress)
        {
            if (opacity <= 0) return;
            dc.PushOpacity(opacity);
            dc.PushTransform(new TranslateTransform(rectangle.X, rectangle.Y));
            dc.PushTransform(new ScaleTransform(rectangle.Width / opened.PixelWidth, rectangle.Height / opened.PixelHeight));
            var lines = trace.GetFrame(progress); var surface = mesh.GetFrame(meshProgress);
            // A restrained complete scaffold makes the virtual face readable
            // before the travelling light heads finish tracing its contours.
            // It uses the existing source-aligned curves, with no scan strip
            // across the face and no changes to the character or eye textures.
            double structure = Smooth(progress);
            dc.DrawGeometry(null, StrokePen(0.07 + 0.07 * structure, 64, 199, 244, 0.55), mesh.FullGeometry);
            dc.DrawGeometry(null, StrokePen(0.04 + 0.04 * structure, 91, 218, 247, 0.48), trace.FullGeometry);
            PaintBloom(dc, lines, surface);
            PaintLightPaths(dc, surface, 0.62, true);
            PaintLightPaths(dc, lines, 1.0, false);
            dc.Pop(); dc.Pop(); dc.Pop();
        }
        public string LineSample(double seconds)
        {
            if (reducedMotion || wallpaper) return "static-material scene: no animated light paths";
            var frame = trace.GetFrame(seconds * 8 / settings.Duration / 2.25);
            string heads = "";
            foreach (Point p in frame.Heads)
                heads += String.Format(CultureInfo.InvariantCulture, " ({0:F1},{1:F1})", p.X, p.Y);
            return String.Format(CultureInfo.InvariantCulture, "time={0:F3}s; visibleCurves={1}; revealedLength={2:F1}/{3:F1}; travellingHeads={4}; straightSegments={6}; curvedSegments={7}; sourcePositions:{5}",
                seconds, trace.StrokeCount, frame.RevealedLength, trace.TotalLength, frame.Heads.Length, heads, trace.StraightSegments, trace.CurvedSegments);
        }
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            double u = reducedMotion ? 5.4 : Time * 8 / settings.Duration;
            double loopAngle = wallpaperPhase * Math.PI * 2;
            var screen = new Rect(0, 0, DesignWidth, DesignHeight);
            dc.PushClip(new RectangleGeometry(screen, 10, 10));
            dc.DrawRectangle(ColorBrush("#030A0D"), null, screen);
            var wash = new RadialGradientBrush();
            wash.GradientStops.Add(new GradientStop(Color.FromArgb(60, 5, 57, 68), 0));
            wash.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
            dc.DrawEllipse(wash, null, new Point(927, 320), 620, 560);
            double opacity = reducedMotion || wallpaper ? 1 : Ease(u / 0.20) * Clamp((8 - u) / 0.18);
            dc.PushOpacity(opacity);
            for (int i = 0; i < 48; i++)
            {
                double x = 26 + (i * 173 % 1220);
                double y = 70 + (i * 89 % 443);
                double travel = reducedMotion ? 0 : wallpaper ?
                    29 + 29 * Math.Sin(loopAngle * (1 + i % 2) + i * 0.43) : (u * (7 + i % 4)) % 58;
                dc.DrawLine(new Pen(Alpha(0.013 + i % 3 * 0.005, 79, 162, 164), 1), new Point(x, 40), new Point(x, 690));
                dc.DrawLine(new Pen(Alpha(0.10 + i % 4 * 0.017, 50, 158, 156), 0.9), new Point(x, y + travel), new Point(x, y + travel + 20 + i % 16));
            }
            for (int column = 0; !wallpaper && column < 26; column++)
            {
                double x = 57 + column * 43;
                double scroll = (u * (29 + column % 5 * 5) + column * 47) % 36;
                for (int row = 0; row < 20; row++)
                {
                    double y = 35 + row * 35 + scroll;
                    dc.PushTransform(new TranslateTransform(x, y));
                    dc.DrawGeometry(rainBrushes[(column * 7 + row * 3) % 9], null,
                        rainGlyphs[(column * 3 + row * 7) % rainGlyphs.Length]);
                    dc.Pop();
                }
            }
            for (int i = 0; i < 21; i++)
            {
                double x = 35 + (i * 151 % 1190);
                double y = wallpaper ? 52 + (i * 127 % 605) + 16 * Math.Sin(loopAngle + i * 0.53) :
                    34 + ((i * 127 + u * (3 + i % 6)) % 650);
                double brightness = 0.17 + 0.13 * Math.Sin((wallpaper ? loopAngle : u * 1.1) + i * 0.7);
                Bokeh(dc, x, y, 6 + i % 14, brightness);
            }
            double camera = Smooth((u - 0.1) / 2.7);
            double portraitHeight = 2670 - camera * 120;
            double portraitWidth = portraitHeight * opened.PixelWidth / opened.PixelHeight;
            double floatY = reducedMotion ? 0 : Math.Sin(u * 1.13) * 2.4;
            var characterRect = new Rect(430 - camera * 37 + Math.Sin(u * 0.6) * 1.4,
                -650 - camera * 107 + floatY, portraitWidth, portraitHeight);
            if (wallpaper)
            {
                // She slowly leans into view, showing both eyes, then returns
                // to the original half-face framing. No generated new pose or
                // eyebrow patch changes her identity. Both turnarounds have
                // zero camera velocity and acceleration.
                double reveal = Smooth((1 - Math.Cos(loopAngle)) / 2);
                portraitHeight = 2550 - 90 * reveal;
                portraitWidth = portraitHeight * opened.PixelWidth / opened.PixelHeight;
                characterRect = new Rect(393 - 275 * reveal,
                    -757 + 90 * 0.41 * reveal + 1.8 * Math.Sin(loopAngle * 2), portraitWidth, portraitHeight);
            }
            double awaken = EyeOpening(u);
            if (reducedMotion) awaken = 1;
            else if (wallpaper)
            {
                // Two gentle blinks in a twenty-second loop. Outside these
                // short intervals the original open-eyed expression remains.
                double ambientSeconds = wallpaperPhase * 20;
                awaken = 1 - Math.Max(Blink(ambientSeconds, 3.1, 3.48), Blink(ambientSeconds, 12.4, 12.82));
            }
            else awaken *= 1 - Math.Max(Blink(u, 4.9, 5.4), Blink(u, 5.7, 6.2));
            double material = reducedMotion ? 1 : Smooth((u - 2.30) / 1.10);
            if (material > 0)
            {
                dc.PushOpacity(material);
                PaintPortrait(dc, characterRect, awaken);
                PaintTearFinale(dc, characterRect, u, awaken);
                dc.Pop();
            }
            if (material < 1)
            {
                PaintWireframe(dc, characterRect, (1 - material) * Ease(u / 0.3),
                    Clamp(u / 2.25), Clamp((u - 0.95) / 1.30));
            }
            // Slowly moving light across the foreground adds depth while the face remains anchored.
            if (u > 3.7)
            {
                double breathing = 0.06 + 0.025 * Math.Sin(wallpaper ? loopAngle * 2 : u * 1.6);
                Bokeh(dc, 1125, 367, 165, breathing);
            }
            var darken = new LinearGradientBrush();
            darken.StartPoint = new Point(0, 0); darken.EndPoint = new Point(1, 0);
            darken.GradientStops.Add(new GradientStop(Color.FromArgb(95, 0, 6, 9), 0));
            darken.GradientStops.Add(new GradientStop(Color.FromArgb(25, 0, 6, 9), 0.56));
            darken.GradientStops.Add(new GradientStop(Colors.Transparent, 0.8));
            dc.DrawRectangle(darken, null, screen);
            if (!wallpaper)
            {
            Cross(dc, 35, 33, 5, 0.85);
            Text(dc, HeaderMessage, 53, 24, 13.5, "#D6E7E7", true);
            dc.DrawLine(new Pen(Alpha(0.36, 169, 209, 211), 0.7), new Point(headerRuleStart, 34), new Point(469, 34));
            Cross(dc, 478, 34, 4, 0.65);
            dc.DrawText(new FormattedText(HeaderTranslation, CultureInfo.GetCultureInfo("zh-TW"), FlowDirection.LeftToRight,
                chinese, 11.5, ColorBrush("#768E94"), 1.0), new Point(53, 49));
            Text(dc, "01  WIREFRAME", 35, 115, 9.5, u < 2.25 ? "#A0E7EA" : "#4A646B", true);
            Text(dc, "02  MATERIALIZE", 35, 132, 9.5, u >= 2.25 && u < 3.25 ? "#A0E7EA" : "#4A646B", true);
            Text(dc, "03  AWAKEN", 35, 149, 9.5, u >= 3.25 ? "#A0E7EA" : "#4A646B", true);
            dc.DrawLine(new Pen(ColorBrush("#47C9C5"), 2), new Point(35, 164), new Point(35 + 43 * Clamp(u / 4.5), 164));
            double titleEnter = Smooth((u - 3.0) / 1.0);
            dc.PushOpacity(titleEnter);
            dc.PushClip(new RectangleGeometry(new Rect(60, 240, 520 * titleEnter, 174)));
            dc.DrawGeometry(null, StrokePen(0.045, 38, 192, 209, 8), titleGlyph);
            dc.DrawGeometry(null, StrokePen(0.12, 76, 216, 230, 3.0), titleGlyph);
            dc.DrawGeometry(null, StrokePen(0.78, 147, 234, 241, 0.65), titleGlyph);
            dc.PushOpacity(Smooth((u - 4.0) / 0.75));
            dc.DrawGeometry(displayFill, null, titleGlyph);
            dc.PushClip(titleGlyph);
            double lightX = reducedMotion ? 280 : 70 + (u - 4) * 180;
            var glint = new LinearGradientBrush();
            glint.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
            glint.GradientStops.Add(new GradientStop(Color.FromArgb(80, 239, 255, 255), 0.5));
            glint.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
            dc.DrawRectangle(glint, null, new Rect(lightX, 240, 52, 174));
            dc.Pop(); dc.Pop();
            dc.Pop(); dc.Pop();
            double message = Smooth((u - 4.0) / 0.75);
            dc.PushOpacity(message);
            double glitch = reducedMotion ? 0 : GreetingGlitch(u);
            if (glitch > 0)
            {
                var strips = new GeometryGroup();
                strips.Children.Add(new RectangleGeometry(new Rect(82, 445, 390, 2.2)));
                strips.Children.Add(new RectangleGeometry(new Rect(82, 480, 390, 1.8)));
                dc.PushClip(new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(screen), strips));
            }
            PaintGreetingGlyphs(dc, u);
            if (glitch > 0) { dc.Pop(); PaintGreetingGlitch(dc, u, glitch); }
            var rule = new LinearGradientBrush();
            rule.GradientStops.Add(new GradientStop(Color.FromArgb(165, 106, 229, 216), 0));
            rule.GradientStops.Add(new GradientStop(Color.FromArgb(50, 100, 196, 218), 0.6));
            rule.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
            dc.DrawRectangle(rule, null, new Rect(82, 415, 463, 0.8));
            dc.DrawLine(StrokePen(0.75, 102, 223, 214, 1.6), new Point(82, 413), new Point(112, 413));
            Cross(dc, 82, 447, 2.6, 0.72);
            dc.Pop();
            }
            double orbitT = wallpaper ? loopAngle : u * 0.9;
            var ellipse = new Pen(Alpha(0.13, 158, 217, 219), 0.8);
            dc.PushTransform(new RotateTransform(-14, 315, 365));
            dc.DrawEllipse(null, ellipse, new Point(315, 365), 274, 53);
            var comet = new Point(315 + Math.Cos(orbitT) * 274, 365 + Math.Sin(orbitT) * 53);
            Bokeh(dc, comet.X, comet.Y, 12, 0.48);
            dc.DrawEllipse(ColorBrush("#9EEBEE"), null, comet, 1.9, 1.9); dc.Pop();
            if (!wallpaper)
            {
            Text(dc, Launching ? "正在開啟 Codex" : u < 2.25 ? "正在描繪龍娘輪廓" : u < 3.25 ? "正在讓角色顯影" : u < 4.75 ? "龍娘正在甦醒" : "準備出發", 82, 559, 12, "#77B5B5", false);
            Text(dc, "QHD / " + settings.FrameRate + " FPS", 35, 669, 9, "#708C92", true);
            Text(dc, "ESC 跳過並開啟 Codex", 237, 666, 11, "#6C878D", false);
            double progress = Clamp(Time / settings.Duration);
            dc.DrawLine(new Pen(Alpha(0.17, 104, 186, 190), 1), new Point(35, 697), new Point(562, 697));
            dc.DrawLine(new Pen(Alpha(0.70, 92, 206, 205), 1.5), new Point(35, 697), new Point(35 + 527 * progress, 697));
            Text(dc, ((int)(progress * 100)).ToString("D2") + "%", 577, 689, 9, "#708C92", true);
            Cross(dc, 1250, 677, 4, 0.35);
            }
            for (int y = 0; y < DesignHeight; y += 3)
                dc.DrawLine(scanlinePen, new Point(0, y), new Point(DesignWidth, y));
            if (!wallpaper && !reducedMotion && Math.Abs(u - 7.68) < 0.13)
            {
                // One restrained cyan pulse follows impact; no white strobe.
                double pulse = Math.Cos((u - 7.68) / 0.13 * Math.PI / 2);
                dc.DrawRectangle(Alpha(0.055 * pulse * pulse, 112, 229, 249), null, screen);
            }
            dc.Pop(); dc.Pop();
        }
    }
}

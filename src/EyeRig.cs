using System;
using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

namespace DragonLauncher
{
    // Optional high-resolution material layer. Coordinates remain in the
    // original portrait's 1024 x 1536 space; only texture sampling is denser.
    public sealed class FaceDetailAssets
    {
        public readonly Rect Region;
        public readonly int Scale;
        public readonly string AtlasPath, OpenLeftPath, OpenRightPath;
        private readonly BitmapSource closed;
        private readonly Brush edgeMask;

        private FaceDetailAssets(string root, XElement manifest)
        {
            if ((string)manifest.Attribute("version") != "1") throw new InvalidDataException("Unsupported face detail manifest version.");
            Scale = Int32.Parse(RequiredAttribute(manifest, "scale"), CultureInfo.InvariantCulture);
            if (Scale != 2) throw new InvalidDataException("Face detail texture scale must be 2.");
            var region = manifest.Element("region");
            if (region == null) throw new InvalidDataException("Missing face detail region.");
            Region = new Rect(Parse(region, "x"), Parse(region, "y"), Parse(region, "width"), Parse(region, "height"));
            if (Region != new Rect(256, 448, 512, 768)) throw new InvalidDataException("Unsupported face detail source region.");
            closed = ReadBitmap(Resolve(root, manifest, "closedTexture"), 1024, 1536);
            // The atlas is authoring-only for animated eyes. Fixed-open modes
            // can use their two small patches even if the atlas is unavailable.
            AtlasPath = Resolve(root, manifest, "eyeAtlas", false);
            OpenLeftPath = Resolve(root, manifest, "openLeft");
            OpenRightPath = Resolve(root, manifest, "openRight");
            // A small frozen alpha texture feathers the integration boundary;
            // it is prepared once, not recomputed for every video frame.
            const int width = 128, height = 192;
            var data = new byte[width * height * 4];
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                double edge = Math.Min(Math.Min(x, width - 1 - x), Math.Min(y, height - 1 - y)) / 3.0;
                edge = Math.Max(0, Math.Min(1, edge));
                byte alpha = (byte)Math.Round(edge * edge * (3 - 2 * edge) * 255);
                int index = (y * width + x) * 4;
                data[index] = data[index + 1] = data[index + 2] = data[index + 3] = alpha;
            }
            var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, data, width * 4);
            bitmap.Freeze();
            var brush = new ImageBrush(bitmap) { Stretch = Stretch.Fill };
            brush.Freeze(); edgeMask = brush;
        }
        public static FaceDetailAssets Load(string root)
        {
            string path = Path.Combine(root, "assets", "face-detail.config.xml");
            if (!File.Exists(path)) return null;
            XElement xml = XDocument.Load(path).Root;
            if (xml == null || xml.Name != "faceDetail") throw new InvalidDataException("Invalid face detail manifest.");
            return new FaceDetailAssets(root, xml);
        }
        private static string RequiredAttribute(XElement element, string name)
        {
            string value = (string)element.Attribute(name);
            if (String.IsNullOrWhiteSpace(value)) throw new InvalidDataException("Missing face detail attribute: " + name);
            return value;
        }
        private static int Parse(XElement element, string name)
        {
            return Int32.Parse(RequiredAttribute(element, name), CultureInfo.InvariantCulture);
        }
        private static string Resolve(string root, XElement manifest, string name, bool mustExist = true)
        {
            string relative = (string)manifest.Element(name);
            if (String.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) throw new InvalidDataException("Invalid face detail asset: " + name);
            string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string path = Path.GetFullPath(Path.Combine(prefix, relative));
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Face detail asset leaves the package.");
            if (mustExist && !File.Exists(path)) throw new FileNotFoundException("Missing face detail asset: " + name, path);
            return path;
        }
        internal static BitmapSource ReadBitmap(string path, int width, int height)
        {
            var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(Path.GetFullPath(path)); bitmap.EndInit(); bitmap.Freeze();
            if (bitmap.PixelWidth != width || bitmap.PixelHeight != height) throw new InvalidDataException("Unexpected face detail texture dimensions: " + path);
            return bitmap;
        }
        public void DrawClosed(DrawingContext dc)
        {
            dc.PushOpacityMask(edgeMask);
            dc.DrawImage(closed, Region);
            dc.Pop();
        }
    }

    // Aligned eyelid poses retain the iris and foreground hair at their original size.
    public sealed class EyeRig
    {
        private const int Frames = 121, Columns = 11, BasePatchWidth = 214, BasePatchHeight = 132;
        private readonly int patchWidth, patchHeight, textureScale;
        private readonly BitmapSource atlasPixels;
        private readonly byte[] patchPixels;
        private readonly double[] featherX, featherY;
        private readonly bool fixedOpenPose;
        private readonly BitmapSource[][] cache = { new BitmapSource[Frames], new BitmapSource[Frames] };
        private readonly Rect[] destinations;
        private readonly MaterialFlow[] materialFlow = new MaterialFlow[2];

        public EyeRig(string root, BitmapSource character)
            : this(root, character, false) { }

        public EyeRig(string root, BitmapSource character, bool fixedOpen)
            : this(root, character, fixedOpen, null) { }

        public EyeRig(string root, BitmapSource character, bool fixedOpen, FaceDetailAssets detail)
        {
            fixedOpenPose = fixedOpen;
            textureScale = detail == null ? 1 : detail.Scale;
            patchWidth = BasePatchWidth * textureScale; patchHeight = BasePatchHeight * textureScale;
            featherX = new double[patchWidth]; featherY = new double[patchHeight];
            for (int x = 0; x < patchWidth; x++) featherX[x] = Smooth(Math.Min(x, patchWidth - 1 - x) / (10.0 * textureScale));
            for (int y = 0; y < patchHeight; y++) featherY[y] = Smooth(Math.Min(y, patchHeight - 1 - y) / (10.0 * textureScale));
            double sx = character.PixelWidth / 1024.0, sy = character.PixelHeight / 1536.0;
            if (detail != null && (character.PixelWidth != 1024 || character.PixelHeight != 1536))
                throw new InvalidDataException("Face detail requires the original 1024 x 1536 portrait coordinate space.");
            destinations = new[] {
                new Rect(326 * sx, 606 * sy, BasePatchWidth * sx, BasePatchHeight * sy),
                new Rect(549 * sx, 548 * sy, BasePatchWidth * sx, BasePatchHeight * sy)
            };
            if (fixedOpenPose && detail != null)
            {
                // These two small files avoid decoding the 104.31 MiB authoring
                // atlas in the low-motion launcher and wallpaper exporter.
                cache[0][Frames - 1] = PrepareFixed(FaceDetailAssets.ReadBitmap(detail.OpenLeftPath, patchWidth, patchHeight));
                cache[1][Frames - 1] = PrepareFixed(FaceDetailAssets.ReadBitmap(detail.OpenRightPath, patchWidth, patchHeight));
                return;
            }
            string path = detail == null ? Path.Combine(root, "assets", "dragon-eye-motion.png") : detail.AtlasPath;
            if (!File.Exists(path)) throw new FileNotFoundException("Missing eye motion atlas.", path);
            BitmapSource atlas = FaceDetailAssets.ReadBitmap(path, Columns * patchWidth * 2, Columns * patchHeight);
            atlasPixels = new FormatConvertedBitmap(atlas, PixelFormats.Pbgra32, null, 0); atlasPixels.Freeze();
            patchPixels = new byte[patchWidth * patchHeight * 4];
            if (fixedOpenPose)
            {
                cache[0][Frames - 1] = Extract(0, Frames - 1);
                cache[1][Frames - 1] = Extract(1, Frames - 1);
                // The static scene only needs two small patches. Release the
                // full atlas and scratch storage after preparing those patches.
                atlasPixels = null;
                patchPixels = null;
            }
        }
        private static double Smooth(double x) { x = Math.Max(0, Math.Min(1, x)); return x * x * (3 - 2 * x); }
        public void Draw(DrawingContext dc, double openness)
        {
            Draw(dc, openness, Double.NaN);
        }
        public void Draw(DrawingContext dc, double openness, double materialTime)
        {
            int frame = fixedOpenPose ? Frames - 1 : (int)Math.Round(Math.Max(0, Math.Min(1, openness)) * (Frames - 1));
            for (int eye = 0; eye < 2; eye++)
            {
                if (cache[eye][frame] == null) cache[eye][frame] = Extract(eye, frame);
                BitmapSource image = cache[eye][frame];
                if (textureScale == 2 && frame > 0 && !Double.IsNaN(materialTime))
                {
                    if (materialFlow[eye] == null) materialFlow[eye] = new MaterialFlow(eye, patchWidth, patchHeight);
                    image = materialFlow[eye].Sample(image, materialTime);
                }
                dc.DrawImage(image, destinations[eye]);
            }
        }
        // Only the offline animated scene calls this sampler. The reduced-motion
        // launcher uses the overload above, retaining the original fixed pixels.
        // The optical movement samples the painted iris itself: no generated
        // glyphs, disk overlay, changed pupil or new highlight artwork.
        private sealed class MaterialFlow
        {
            private readonly int width, height, eye;
            private readonly byte[] sourcePixels, resultPixels;
            private readonly double[] mask;
            private readonly WriteableBitmap output;
            private BitmapSource sourceImage;
            public MaterialFlow(int index, int w, int h)
            {
                eye = index; width = w; height = h;
                sourcePixels = new byte[w * h * 4]; resultPixels = new byte[w * h * 4]; mask = new double[w * h];
                output = new WriteableBitmap(w, h, 96, 96, PixelFormats.Pbgra32, null);
            }
            private void SetSource(BitmapSource image)
            {
                sourceImage = image; image.CopyPixels(sourcePixels, width * 4, 0);
                double cx = eye == 0 ? 232 : 182, cy = eye == 0 ? 155 : 155;
                double pupilX = eye == 0 ? 234 : 180, pupilY = eye == 0 ? 140 : 150;
                for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
                {
                    int p = y * width + x, i = p * 4;
                    double dx = (x - cx) / 63, dy = (y - cy) / 52;
                    double radius = Math.Sqrt(dx * dx + dy * dy);
                    double px = (x - pupilX) / 19, py = (y - pupilY) / 25;
                    double pupil = Math.Sqrt(px * px + py * py);
                    // The conservative geometry and material colour jointly
                    // exclude skin/eye white; the current pose removes any
                    // region covered by its upper lid during a blink.
                    double purple = Smooth((sourcePixels[i] - sourcePixels[i + 2] - 3) / 22.0);
                    double lit = Smooth((sourcePixels[i] - 65) / 40.0);
                    double highlight = 1 - Smooth((Math.Min(sourcePixels[i], Math.Min(sourcePixels[i + 1], sourcePixels[i + 2])) - 218) / 22.0);
                    mask[p] = Smooth((1 - radius) / .18) * Smooth((pupil - 1) / .35) * purple * lit * highlight;
                }
            }
            public BitmapSource Sample(BitmapSource image, double seconds)
            {
                if (!Object.ReferenceEquals(image, sourceImage)) SetSource(image);
                Buffer.BlockCopy(sourcePixels, 0, resultPixels, 0, sourcePixels.Length);
                double phase = seconds * Math.PI * 2 / 2.8 + eye * .6;
                for (int y = 1; y < height - 2; y++) for (int x = 1; x < width - 2; x++)
                {
                    int p = y * width + x, index = p * 4; double amount = mask[p];
                    if (amount <= .001) continue;
                    double wave = Math.Sin(phase + x * .045 + y * .018);
                    double sx = Math.Max(0, Math.Min(width - 2, x + 1.1 * amount * Math.Sin(phase * .7 + y * .045)));
                    double sy = Math.Max(0, Math.Min(height - 2, y + 2.8 * amount * wave));
                    int ix = (int)sx, iy = (int)sy; double fx = sx - ix, fy = sy - iy;
                    int a = (iy * width + ix) * 4, b = a + width * 4;
                    double light = 1 + .085 * amount * Math.Sin(phase + y * .075 - x * .025);
                    for (int c = 0; c < 3; c++)
                    {
                        double upper = sourcePixels[a + c] * (1 - fx) + sourcePixels[a + 4 + c] * fx;
                        double lower = sourcePixels[b + c] * (1 - fx) + sourcePixels[b + 4 + c] * fx;
                        double sampled = (upper * (1 - fy) + lower * fy) * light;
                        resultPixels[index + c] = (byte)Math.Round(Math.Max(0, Math.Min(sourcePixels[index + 3], sourcePixels[index + c] * (1 - amount) + sampled * amount)));
                    }
                }
                output.WritePixels(new Int32Rect(0, 0, width, height), resultPixels, width * 4, 0);
                return output;
            }
        }
        private BitmapSource Extract(int eye, int frame)
        {
            int originX = frame % Columns * patchWidth * 2 + eye * patchWidth;
            int originY = frame / Columns * patchHeight;
            // Copy only the requested patch; BitmapSource.Create owns a copy,
            // so the same scratch buffer can be reused without altering caches.
            var data = patchPixels;
            atlasPixels.CopyPixels(new Int32Rect(originX, originY, patchWidth, patchHeight), data, patchWidth * 4, 0);
            return Feather(data);
        }
        private BitmapSource PrepareFixed(BitmapSource source)
        {
            var data = new byte[patchWidth * patchHeight * 4];
            new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0).CopyPixels(data, patchWidth * 4, 0);
            return Feather(data);
        }
        private BitmapSource Feather(byte[] data)
        {
            for (int y = 0; y < patchHeight; y++) for (int x = 0; x < patchWidth; x++)
            {
                double feather = featherX[x] * featherY[y];
                int target = (y * patchWidth + x) * 4;
                for (int c = 0; c < 4; c++) data[target + c] = (byte)Math.Round(data[target + c] * feather);
            }
            var image = BitmapSource.Create(patchWidth, patchHeight, 96, 96, PixelFormats.Pbgra32, null, data, patchWidth * 4);
            image.Freeze(); return image;
        }
    }
}

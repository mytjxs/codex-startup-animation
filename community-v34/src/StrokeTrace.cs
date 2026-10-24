using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DragonLauncher
{
    // Trace real contours, then animate each curve by arc length. Frozen frame
    // geometries keep the export and the realtime fallback on the same paths.
    public sealed class StrokeTrace
    {
        private const int ProgressSteps = 360;
        private MotionFrame[] progressFrames;
        public Geometry FullGeometry { get; private set; }
        public int StrokeCount { get; private set; }
        public double TotalLength { get; private set; }
        public int StraightSegments { get; private set; }
        public int CurvedSegments { get; private set; }

        public sealed class MotionFrame
        {
            public Geometry Lines, Tail, Light, Hot;
            public Point[] Heads;
            public double RevealedLength;
        }

        private sealed class Stroke
        {
            public Point[] Points;
            public double[] Distance;
            public double Length;
            public StreamGeometry Geometry;
            public double Start, Duration;
            public bool Highlight;
        }

        public StrokeTrace(BitmapSource source, Rect visibleArea)
        {
            if (source == null) throw new ArgumentNullException("source");
            int width = source.PixelWidth, height = source.PixelHeight;
            if (width < 8 || height < 8) throw new ArgumentException("The source image is too small.", "source");
            var pixels = new byte[width * height * 4];
            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            converted.CopyPixels(pixels, width * 4, 0);
            var edges = ExtractEdges(pixels, width, height);
            List<Stroke> strokes = Clip(Trace(edges, width, height), visibleArea);
            Join(strokes);
            strokes.RemoveAll(s => s.Length < 38);
            for (int i = 0; i < strokes.Count; i++)
            {
                Point[] points = strokes[i].Points;
                Vector direction = points[points.Length - 1] - points[0];
                // Hair flows from its root down to its tip. Across the face,
                // lashes and short contours travel from left to right.
                if (Math.Abs(direction.Y) > Math.Abs(direction.X) * 0.55 ? direction.Y < 0 : direction.X < 0)
                    Array.Reverse(points);
                strokes[i] = MakeStroke(SmoothCurve(points));
            }
            Initialize(strokes, false);
        }

        public StrokeTrace(IEnumerable<Point[]> curves)
        {
            var strokes = new List<Stroke>();
            foreach (Point[] points in curves)
            {
                if (points.Length < 2) continue;
                Stroke stroke = MakeStroke(points);
                if (stroke.Length >= 25) strokes.Add(stroke);
            }
            Initialize(strokes, true);
        }

        private void Initialize(List<Stroke> strokes, bool surface)
        {
            // The longest silhouette curves establish the shape. Stagger the
            // other strands and the facial details into a second drawing pass.
            if (!surface) strokes.Sort((a, b) => b.Length.CompareTo(a.Length));
            int primary = Math.Min(24, strokes.Count);
            for (int i = 0; i < strokes.Count; i++)
            {
                Stroke stroke = strokes[i];
                if (surface)
                {
                    stroke.Start = 0.08 + 1.30 * i / Math.Max(1, strokes.Count - 1);
                    stroke.Duration = Math.Min(0.80, 0.38 + stroke.Length / 1100);
                    stroke.Highlight = true;
                }
                else if (i < primary)
                {
                    stroke.Start = 0.02 + 0.58 * i / Math.Max(1, primary - 1);
                    stroke.Duration = Math.Min(1.12, 0.40 + stroke.Length / 750);
                    stroke.Highlight = true;
                }
                else
                {
                    double order = (i - primary) / (double)Math.Max(1, strokes.Count - primary - 1);
                    stroke.Start = 0.65 + order * 1.13;
                    stroke.Duration = Math.Min(0.68, 0.23 + stroke.Length / 850);
                    stroke.Highlight = stroke.Length > 90 && i % 3 == 0;
                }
                stroke.Duration = Math.Min(stroke.Duration, 2.22 - stroke.Start);
            }
            StrokeCount = strokes.Count;
            var complete = new StreamGeometry();
            using (var context = complete.Open())
            {
                foreach (Stroke stroke in strokes)
                {
                    TotalLength += stroke.Length;
                    Append(context, stroke.Points, stroke.Length, stroke.Distance);
                }
            }
            complete.Freeze(); FullGeometry = complete;
            progressFrames = new MotionFrame[ProgressSteps + 1];
            for (int step = 0; step <= ProgressSteps; step++)
            {
                double time = 2.25 * step / ProgressSteps;
                var group = new GeometryGroup();
                var partial = new StreamGeometry();
                var tail = new StreamGeometry();
                var light = new StreamGeometry();
                var hot = new StreamGeometry();
                var heads = new List<Point>();
                double revealed = 0;
                using (var bodyContext = partial.Open())
                using (var tailContext = tail.Open())
                using (var lightContext = light.Open())
                using (var hotContext = hot.Open())
                {
                    foreach (Stroke stroke in strokes)
                    {
                        double fraction = Math.Max(0, Math.Min(1, (time - stroke.Start) / stroke.Duration));
                        // Smooth departure/arrival, with continuous travel in
                        // between. There is no whole-line opacity reveal.
                        fraction = fraction * fraction * (3 - 2 * fraction);
                        double end = stroke.Length * fraction;
                        revealed += end;
                        if (fraction >= 1) group.Children.Add(stroke.Geometry);
                        else if (fraction > 0) Append(bodyContext, stroke.Points, end, stroke.Distance);
                        if (!stroke.Highlight || fraction <= 0 || fraction >= 1) continue;
                        double trailLength = Math.Min(80, stroke.Length * 0.38);
                        AppendRange(tailContext, stroke, end - trailLength, end);
                        AppendRange(lightContext, stroke, end - trailLength * 0.52, end);
                        AppendRange(hotContext, stroke, end - trailLength * 0.19, end);
                        // A compact travelling head, not a fixed sparkle.
                        if (heads.Count < (surface ? 6 : 18)) heads.Add(At(stroke, end));
                    }
                }
                partial.Freeze(); group.Children.Add(partial); group.Freeze();
                tail.Freeze(); light.Freeze(); hot.Freeze();
                progressFrames[step] = new MotionFrame { Lines = group, Tail = tail, Light = light, Hot = hot,
                    Heads = heads.ToArray(), RevealedLength = revealed };
            }
        }

        public Geometry GetGeometry(double progress)
        {
            return GetFrame(progress).Lines;
        }

        public MotionFrame GetFrame(double progress)
        {
            if (Double.IsNaN(progress) || progress <= 0) return progressFrames[0];
            if (progress >= 1) return progressFrames[ProgressSteps];
            return progressFrames[(int)Math.Round(progress * ProgressSteps)];
        }

        private static Stroke MakeStroke(Point[] points)
        {
            var distances = new double[points.Length];
            for (int i = 1; i < points.Length; i++) distances[i] = distances[i - 1] + (points[i] - points[i - 1]).Length;
            var stroke = new Stroke { Points = points, Distance = distances, Length = distances[points.Length - 1] };
            var geometry = new StreamGeometry();
            using (var context = geometry.Open()) Append(context, points, stroke.Length, distances);
            geometry.Freeze(); stroke.Geometry = geometry;
            return stroke;
        }

        private static List<Stroke> Clip(List<Stroke> strokes, Rect area)
        {
            var clipped = new List<Stroke>();
            foreach (Stroke stroke in strokes)
            {
                var points = new List<Point>();
                foreach (Point point in stroke.Points)
                {
                    if (area.Contains(point)) points.Add(point);
                    else if (points.Count > 0)
                    {
                        if (points.Count > 1) clipped.Add(MakeStroke(points.ToArray()));
                        points.Clear();
                    }
                }
                if (points.Count > 1) clipped.Add(MakeStroke(points.ToArray()));
            }
            return clipped;
        }

        private static Vector EndDirection(Point[] points, bool start)
        {
            int last = points.Length - 1;
            Vector direction = start ? points[0] - points[Math.Min(last, 3)] : points[last] - points[Math.Max(0, last - 3)];
            if (direction.Length > 0) direction.Normalize();
            return direction;
        }

        private static void Join(List<Stroke> strokes)
        {
            // Edge intersections split strands. Reconnect only nearby ends
            // whose tangents agree; never bridge unrelated hair and face lines.
            for (int pass = 0; pass < 5; pass++)
            {
                bool changed = false;
                for (int i = 0; i < strokes.Count; i++)
                {
                    int best = -1; bool reverseA = false, reverseB = false; double bestScore = 9.0;
                    Point[] a = strokes[i].Points;
                    for (int j = i + 1; j < strokes.Count; j++)
                    {
                        Point[] b = strokes[j].Points;
                        for (int ae = 0; ae < 2; ae++)
                        for (int be = 0; be < 2; be++)
                        {
                            Point ap = ae == 0 ? a[0] : a[a.Length - 1];
                            Point bp = be == 0 ? b[0] : b[b.Length - 1];
                            double gap = (ap - bp).Length;
                            if (gap > 7.5) continue;
                            double agreement = -Vector.Multiply(EndDirection(a, ae == 0), EndDirection(b, be == 0));
                            if (agreement < 0.80) continue;
                            double score = gap + (1 - agreement) * 8;
                            if (score < bestScore) { best = j; bestScore = score; reverseA = ae == 0; reverseB = be == 1; }
                        }
                    }
                    if (best < 0) continue;
                    Point[] second = strokes[best].Points;
                    if (reverseA) Array.Reverse(a);
                    if (reverseB) Array.Reverse(second);
                    var joined = new Point[a.Length + second.Length];
                    Array.Copy(a, joined, a.Length); Array.Copy(second, 0, joined, a.Length, second.Length);
                    strokes[i] = MakeStroke(joined); strokes.RemoveAt(best); changed = true;
                }
                if (!changed) break;
            }
        }

        private Point[] SmoothCurve(Point[] points)
        {
            // Remove small painted-edge kinks at a fixed spatial scale first.
            // Merely interpolating every detected corner reproduces its noise.
            Stroke original = MakeStroke(points);
            var samples = new List<Point>();
            for (double distance = 0; distance < original.Length; distance += 3)
                samples.Add(At(original, distance));
            samples.Add(points[points.Length - 1]);
            var filtered = new List<Point>(samples.Count);
            for (int i = 0; i < samples.Count; i++)
            {
                if (i == 0 || i == samples.Count - 1) { filtered.Add(samples[i]); continue; }
                double x = 0, y = 0, weight = 0;
                for (int k = -4; k <= 4; k++)
                {
                    Point sample = samples[Math.Max(0, Math.Min(samples.Count - 1, i + k))];
                    double w = 5 - Math.Abs(k); x += sample.X * w; y += sample.Y * w; weight += w;
                }
                Vector before = Unit(samples[i] - samples[Math.Max(0, i - 4)]);
                Vector after = Unit(samples[Math.Min(samples.Count - 1, i + 4)] - samples[i]);
                double angle = Math.Acos(Math.Max(-1, Math.Min(1, Vector.Multiply(before, after))));
                // Preserve genuinely angular horn/clothing construction, while
                // hair and facial arcs get continuous, rounded tangents.
                double preserve = Math.Max(0, Math.Min(1, (angle - 0.90) / 0.45));
                double edge = Math.Min(1, Math.Min(i, samples.Count - 1 - i) / 4.0);
                filtered.Add(samples[i] + (new Point(x / weight, y / weight) - samples[i]) * edge * (1 - preserve));
            }
            points = Simplify(filtered, 1.35).ToArray();
            var result = new List<Point>(); result.Add(points[0]);
            for (int i = 0; i < points.Length - 1; i++)
            {
                Point p1 = points[i], p2 = points[i + 1];
                Vector span = p2 - p1, direction = Unit(span);
                Vector incoming = i == 0 ? direction : Unit(p1 - points[i - 1]);
                Vector outgoing = i + 2 == points.Length ? direction : Unit(points[i + 2] - p2);
                Vector first = Vector.Multiply(incoming, direction) < 0.62 ? direction : Unit(incoming + direction);
                Vector last = Vector.Multiply(direction, outgoing) < 0.62 ? direction : Unit(direction + outgoing);
                if (Vector.Multiply(first, direction) > 0.998 && Vector.Multiply(last, direction) > 0.998)
                {
                    // Actual straight segments stay exact lines, with no
                    // spline bowing at their midpoint.
                    result.Add(p2); StraightSegments++; continue;
                }
                double firstHandle = Math.Min(span.Length, i == 0 ? span.Length : (p1 - points[i - 1]).Length) / 3;
                double lastHandle = Math.Min(span.Length, i + 2 == points.Length ? span.Length : (points[i + 2] - p2).Length) / 3;
                Point c1 = p1 + first * firstHandle, c2 = p2 - last * lastHandle;
                int steps = Math.Max(3, (int)Math.Ceiling(span.Length / 1.5));
                CurvedSegments++;
                for (int k = 1; k <= steps; k++)
                {
                    double t = k / (double)steps, s = 1 - t;
                    result.Add(new Point(
                        s * s * s * p1.X + 3 * s * s * t * c1.X + 3 * s * t * t * c2.X + t * t * t * p2.X,
                        s * s * s * p1.Y + 3 * s * s * t * c1.Y + 3 * s * t * t * c2.Y + t * t * t * p2.Y));
                }
            }
            return result.ToArray();
        }

        private static Vector Unit(Vector vector)
        {
            if (vector.Length > 0.000001) vector.Normalize();
            return vector;
        }

        private static Point At(Stroke stroke, double distance)
        {
            if (distance <= 0) return stroke.Points[0];
            int i = FirstAtOrAfter(stroke.Distance, distance);
            if (i == stroke.Points.Length) return stroke.Points[stroke.Points.Length - 1];
            return stroke.Points[i - 1] + (stroke.Points[i] - stroke.Points[i - 1]) *
                ((distance - stroke.Distance[i - 1]) / (stroke.Distance[i] - stroke.Distance[i - 1]));
        }

        private static int FirstAtOrAfter(double[] distances, double distance)
        {
            // Arc lengths are sorted. Find exactly the same first segment as
            // the original linear scan, including repeated/zero-length points.
            int low = 1, high = distances.Length;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (distances[middle] < distance) low = middle + 1;
                else high = middle;
            }
            return low;
        }

        private static void AppendRange(StreamGeometryContext context, Stroke stroke, double start, double end)
        {
            start = Math.Max(0, start); end = Math.Min(stroke.Length, end);
            if (end <= start) return;
            context.BeginFigure(At(stroke, start), false, false);
            for (int i = FirstAtOrAfter(stroke.Distance, start); i < stroke.Points.Length && stroke.Distance[i] < end; i++)
                if (stroke.Distance[i] > start) context.LineTo(stroke.Points[i], true, false);
            context.LineTo(At(stroke, end), true, false);
        }

        public void Draw(DrawingContext context, Pen pen, double progress)
        {
            if (context == null) throw new ArgumentNullException("context");
            context.DrawGeometry(null, pen, GetGeometry(progress));
        }

        private static bool[] ExtractEdges(byte[] pixels, int width, int height)
        {
            int count = width * height;
            var light = new float[count];
            var temporary = new float[count];
            var blurred = new float[count];
            for (int i = 0; i < count; i++)
            {
                int p = i * 4;
                light[i] = (float)((pixels[p] * 0.114 + pixels[p + 1] * 0.587 + pixels[p + 2] * 0.299) * pixels[p + 3] / 255.0);
            }
            // A small Gaussian removes painted texture while retaining the
            // actual hair strands, horns, eyelids and face silhouette.
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int row = y * width;
                temporary[row + x] = (light[row + Math.Max(0, x - 2)] +
                    4 * light[row + Math.Max(0, x - 1)] + 6 * light[row + x] +
                    4 * light[row + Math.Min(width - 1, x + 1)] + light[row + Math.Min(width - 1, x + 2)]) / 16;
            }
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                blurred[y * width + x] = (temporary[Math.Max(0, y - 2) * width + x] +
                    4 * temporary[Math.Max(0, y - 1) * width + x] + 6 * temporary[y * width + x] +
                    4 * temporary[Math.Min(height - 1, y + 1) * width + x] +
                    temporary[Math.Min(height - 1, y + 2) * width + x]) / 16;
            var magnitude = new float[count];
            var direction = new byte[count];
            for (int y = 2; y < height - 2; y++)
            for (int x = 2; x < width - 2; x++)
            {
                int i = y * width + x;
                double gx = -blurred[i - width - 1] + blurred[i - width + 1]
                    - 2 * blurred[i - 1] + 2 * blurred[i + 1]
                    - blurred[i + width - 1] + blurred[i + width + 1];
                double gy = -blurred[i - width - 1] - 2 * blurred[i - width] - blurred[i - width + 1]
                    + blurred[i + width - 1] + 2 * blurred[i + width] + blurred[i + width + 1];
                magnitude[i] = (float)Math.Sqrt(gx * gx + gy * gy);
                double ax = Math.Abs(gx), ay = Math.Abs(gy);
                direction[i] = ax > ay * 2.41421356 ? (byte)0 :
                    ay > ax * 2.41421356 ? (byte)2 : gx * gy >= 0 ? (byte)1 : (byte)3;
            }
            var weak = new bool[count];
            var edges = new bool[count];
            var queue = new Queue<int>();
            for (int y = 3; y < height - 3; y++)
            for (int x = 3; x < width - 3; x++)
            {
                int i = y * width + x;
                float value = magnitude[i];
                if (value < 20 || pixels[i * 4 + 3] < 20) continue;
                int offset = direction[i] == 0 ? 1 : direction[i] == 2 ? width :
                    direction[i] == 1 ? width + 1 : width - 1;
                // Asymmetric comparison avoids the two-pixel rails produced by
                // a flat local maximum, while keeping a single continuous line.
                if (value < magnitude[i - offset] || value <= magnitude[i + offset]) continue;
                weak[i] = true;
                if (value >= 44) { edges[i] = true; queue.Enqueue(i); }
            }
            int[] offsets = { -width - 1, -width, -width + 1, -1, 1, width - 1, width, width + 1 };
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                foreach (int offset in offsets)
                {
                    int next = current + offset;
                    if (!weak[next] || edges[next]) continue;
                    edges[next] = true; queue.Enqueue(next);
                }
            }
            return edges;
        }

        private static List<Stroke> Trace(bool[] edges, int width, int height)
        {
            var visited = new bool[edges.Length];
            var strokes = new List<Stroke>();
            // Endpoints first gives every isolated curve its natural direction.
            // A second pass collects closed loops and the remaining branches.
            for (int pass = 0; pass < 2; pass++)
            for (int y = 3; y < height - 3; y++)
            for (int x = 3; x < width - 3; x++)
            {
                int start = y * width + x;
                if (!edges[start] || visited[start]) continue;
                if (pass == 0 && Degree(edges, start, width) > 1) continue;
                List<Point> points = Follow(edges, visited, start, width);
                if (points.Count < 12) continue;
                points = Simplify(points, 0.72);
                if (points.Count < 2) continue;
                var distances = new double[points.Count];
                for (int i = 1; i < points.Count; i++)
                    distances[i] = distances[i - 1] + (points[i] - points[i - 1]).Length;
                if (distances[points.Count - 1] < 15) continue;
                var stroke = new Stroke { Points = points.ToArray(), Distance = distances, Length = distances[points.Count - 1] };
                var geometry = new StreamGeometry();
                using (var context = geometry.Open()) Append(context, stroke.Points, stroke.Length, distances);
                geometry.Freeze(); stroke.Geometry = geometry;
                strokes.Add(stroke);
            }
            return strokes;
        }

        private static bool Connected(bool[] edges, int current, int dx, int dy, int width)
        {
            if (!edges[current + dy * width + dx]) return false;
            // Remove redundant diagonal links around orthogonal corners. This
            // prevents little triangle branches in an otherwise single line.
            return dx == 0 || dy == 0 || (!edges[current + dx] && !edges[current + dy * width]);
        }

        private static int Degree(bool[] edges, int current, int width)
        {
            int degree = 0;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if ((dx != 0 || dy != 0) && Connected(edges, current, dx, dy, width)) degree++;
            return degree;
        }

        private static List<Point> Follow(bool[] edges, bool[] visited, int start, int width)
        {
            var points = new List<Point>();
            int current = start, previous = -1;
            int oldDx = 0, oldDy = 0;
            while (current >= 0)
            {
                points.Add(new Point(current % width + 0.5, current / width + 0.5));
                visited[current] = true;
                int next = -1, nextDx = 0, nextDy = 0;
                double bestScore = Double.MinValue;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int candidate = current + dy * width + dx;
                    if (visited[candidate] || !Connected(edges, current, dx, dy, width)) continue;
                    double score = previous < 0 ? -dy * 0.10 :
                        (oldDx * dx + oldDy * dy) / Math.Sqrt((oldDx * oldDx + oldDy * oldDy) * (dx * dx + dy * dy));
                    // At a junction continue along the same curve rather than
                    // changing to a perpendicular painted detail.
                    if (score > bestScore) { bestScore = score; next = candidate; nextDx = dx; nextDy = dy; }
                }
                previous = current; current = next; oldDx = nextDx; oldDy = nextDy;
            }
            if (points.Count > 3)
            {
                var filtered = new List<Point>(points.Count);
                filtered.Add(points[0]);
                for (int i = 1; i < points.Count - 1; i++)
                    filtered.Add(new Point((points[i - 1].X + 2 * points[i].X + points[i + 1].X) / 4,
                        (points[i - 1].Y + 2 * points[i].Y + points[i + 1].Y) / 4));
                filtered.Add(points[points.Count - 1]);
                return filtered;
            }
            return points;
        }

        private static List<Point> Simplify(List<Point> points, double tolerance)
        {
            var keep = new bool[points.Count]; keep[0] = true; keep[points.Count - 1] = true;
            var ranges = new Stack<int[]>(); ranges.Push(new[] { 0, points.Count - 1 });
            double squaredTolerance = tolerance * tolerance;
            while (ranges.Count > 0)
            {
                int[] range = ranges.Pop(); int first = range[0], last = range[1];
                Vector span = points[last] - points[first]; double squaredLength = span.LengthSquared;
                double farthest = squaredTolerance; int split = -1;
                for (int i = first + 1; i < last; i++)
                {
                    Vector relative = points[i] - points[first];
                    double t = squaredLength == 0 ? 0 : Math.Max(0, Math.Min(1, Vector.Multiply(relative, span) / squaredLength));
                    Vector delta = relative - span * t; double distance = delta.LengthSquared;
                    if (distance > farthest) { farthest = distance; split = i; }
                }
                if (split >= 0) { keep[split] = true; ranges.Push(new[] { first, split }); ranges.Push(new[] { split, last }); }
            }
            var simplified = new List<Point>();
            for (int i = 0; i < points.Count; i++) if (keep[i]) simplified.Add(points[i]);
            return simplified;
        }

        private static void Append(StreamGeometryContext context, Point[] points, double length, double[] distances)
        {
            if (points.Length < 2 || length <= 0) return;
            context.BeginFigure(points[0], false, false);
            for (int i = 1; i < points.Length; i++)
            {
                if (distances[i] <= length) context.LineTo(points[i], true, false);
                else
                {
                    double fraction = (length - distances[i - 1]) / (distances[i] - distances[i - 1]);
                    Point endpoint = points[i - 1] + (points[i] - points[i - 1]) * Math.Max(0, fraction);
                    context.LineTo(endpoint, true, false); break;
                }
            }
        }
    }
}

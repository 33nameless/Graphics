using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace TriangleExcircles
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    // ═══════════════════════════════════════════
    //  СТРУКТУРЫ
    // ═══════════════════════════════════════════

    public struct LineSegment
    {
        public PointF A, B;
        public LineSegment(PointF a, PointF b) { A = a; B = b; }
    }

    public struct CircleData
    {
        public PointF Center;
        public float Radius;
        public string Label;       // "Ia", "Ib", "Ic"
        public Color Color;
        public CircleData(PointF c, float r, string lbl, Color col)
        {
            Center = c; Radius = r; Label = lbl; Color = col;
        }
    }

    // ═══════════════════════════════════════════
    //  АЛГОРИТМЫ РАСТЕРИЗАЦИИ ОКРУЖНОСТЕЙ
    // ═══════════════════════════════════════════
    public static class CircleAlgorithms
    {
        /// <summary>
        /// 1. По уравнению окружности: x² + y² = R²
        /// Для каждого x от -R до R вычисляем y = ±sqrt(R²-x²)
        /// Затем для каждого y аналогично — чтобы не было разрывов
        /// </summary>
        public static List<Point> ByEquation(int cx, int cy, int r)
        {
            HashSet<Point> set = new HashSet<Point>();
            if (r <= 0) return new List<Point>(set);

            // Проход по X
            for (int x = -r; x <= r; x++)
            {
                double yy = Math.Sqrt((double)r * r - (double)x * x);
                int y1 = (int)Math.Round(yy);
                int y2 = -(int)Math.Round(yy);
                set.Add(new Point(cx + x, cy + y1));
                set.Add(new Point(cx + x, cy + y2));
            }

            // Проход по Y (заполняет пропуски на крутых участках)
            for (int y = -r; y <= r; y++)
            {
                double xx = Math.Sqrt((double)r * r - (double)y * y);
                int x1 = (int)Math.Round(xx);
                int x2 = -(int)Math.Round(xx);
                set.Add(new Point(cx + x1, cy + y));
                set.Add(new Point(cx + x2, cy + y));
            }

            return new List<Point>(set);
        }

        /// <summary>
        /// 2. Параметрическое уравнение: x = cx + R*cos(t), y = cy + R*sin(t)
        /// Шаг по t подбирается чтобы не было пропусков
        /// </summary>
        public static List<Point> Parametric(int cx, int cy, int r)
        {
            HashSet<Point> set = new HashSet<Point>();
            if (r <= 0) return new List<Point>(set);

            // Шаг: чем больше радиус, тем меньше шаг
            int steps = Math.Max(360, (int)(2 * Math.PI * r * 1.5));
            double dt = 2 * Math.PI / steps;

            for (int i = 0; i <= steps; i++)
            {
                double t = i * dt;
                int x = (int)Math.Round(cx + r * Math.Cos(t));
                int y = (int)Math.Round(cy + r * Math.Sin(t));
                set.Add(new Point(x, y));
            }

            return new List<Point>(set);
        }

        /// <summary>
        /// 3. Алгоритм Брезенхема для окружности.
        /// Целочисленный, использует 8-кратную симметрию.
        /// Самый эффективный.
        /// </summary>
        public static List<Point> Bresenham(int cx, int cy, int r)
        {
            HashSet<Point> set = new HashSet<Point>();
            if (r <= 0)
            {
                set.Add(new Point(cx, cy));
                return new List<Point>(set);
            }

            int x = 0;
            int y = r;
            int d = 3 - 2 * r;

            while (x <= y)
            {
                // 8 симметричных точек
                set.Add(new Point(cx + x, cy + y));
                set.Add(new Point(cx - x, cy + y));
                set.Add(new Point(cx + x, cy - y));
                set.Add(new Point(cx - x, cy - y));
                set.Add(new Point(cx + y, cy + x));
                set.Add(new Point(cx - y, cy + x));
                set.Add(new Point(cx + y, cy - x));
                set.Add(new Point(cx - y, cy - x));

                if (d < 0)
                {
                    d += 4 * x + 6;
                }
                else
                {
                    d += 4 * (x - y) + 10;
                    y--;
                }
                x++;
            }

            return new List<Point>(set);
        }

        /// <summary>
        /// Рисует пиксели на Bitmap
        /// </summary>
        public static void DrawPixels(Bitmap bmp, List<Point> points, Color color)
        {
            foreach (Point p in points)
            {
                if (p.X >= 0 && p.X < bmp.Width && p.Y >= 0 && p.Y < bmp.Height)
                    bmp.SetPixel(p.X, p.Y, color);
            }
        }

        /// <summary>
        /// Рисует отрезок Брезенхемом (для сторон треугольника)
        /// </summary>
        public static List<Point> LineBresenham(int x1, int y1, int x2, int y2)
        {
            List<Point> pts = new List<Point>();
            int dx = Math.Abs(x2 - x1), dy = Math.Abs(y2 - y1);
            int sx = x1 < x2 ? 1 : -1, sy = y1 < y2 ? 1 : -1;
            int err = dx - dy, x = x1, y = y1;
            while (true)
            {
                pts.Add(new Point(x, y));
                if (x == x2 && y == y2) break;
                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; x += sx; }
                if (e2 < dx) { err += dx; y += sy; }
            }
            return pts;
        }
    }

    // ═══════════════════════════════════════════
    //  ГЕОМЕТРИЯ: ТРЕУГОЛЬНИК + ВНЕВПИСАННЫЕ ОКРУЖНОСТИ
    // ═══════════════════════════════════════════
    public static class TriangleGeometry
    {
        /// <summary>
        /// По трём длинам сторон вычисляет координаты вершин.
        /// A в начале, B на оси X, C вычисляется.
        /// Затем центрируем и масштабируем.
        /// </summary>
        public static bool ComputeVertices(float a, float b, float c,
            int imgW, int imgH, int margin,
            out PointF A, out PointF B, out PointF C)
        {
            A = B = C = PointF.Empty;

            // Проверка неравенства треугольника
            if (a + b <= c || a + c <= b || b + c <= a)
                return false;
            if (a <= 0 || b <= 0 || c <= 0)
                return false;

            // a = BC, b = AC, c = AB
            // Ставим A в (0,0), B в (c, 0)
            // Находим C через длины b (AC) и a (BC)

            float cosA = (b * b + c * c - a * a) / (2 * b * c);
            float sinA = (float)Math.Sqrt(1 - cosA * cosA);

            PointF rawA = new PointF(0, 0);
            PointF rawB = new PointF(c, 0);
            PointF rawC = new PointF(b * cosA, b * sinA);

            // Центрируем
            float centerX = (rawA.X + rawB.X + rawC.X) / 3f;
            float centerY = (rawA.Y + rawB.Y + rawC.Y) / 3f;

            rawA = new PointF(rawA.X - centerX, rawA.Y - centerY);
            rawB = new PointF(rawB.X - centerX, rawB.Y - centerY);
            rawC = new PointF(rawC.X - centerX, rawC.Y - centerY);

            // Масштабируем чтобы вписать в область рисования с учётом
            // вневписанных окружностей (они могут быть большими)
            float s = SemiPerimeter(a, b, c);
            float maxR = Math.Max(ExcircleRadius(a, b, c),
                         Math.Max(ExcircleRadius(b, a, c),
                                  ExcircleRadius(c, a, b)));

            // Находим bounds всех объектов (вершины + центры вневписанных)
            PointF Ia, Ib, Ic;
            float ra, rb, rc;
            ComputeExcirclesRaw(rawA, rawB, rawC, a, b, c,
                out Ia, out ra, out Ib, out rb, out Ic, out rc);

            float minX = Math.Min(rawA.X, Math.Min(rawB.X, rawC.X));
            float maxXv = Math.Max(rawA.X, Math.Max(rawB.X, rawC.X));
            float minY = Math.Min(rawA.Y, Math.Min(rawB.Y, rawC.Y));
            float maxYv = Math.Max(rawA.Y, Math.Max(rawB.Y, rawC.Y));

            // Учитываем окружности
            minX = Math.Min(minX, Math.Min(Ia.X - ra, Math.Min(Ib.X - rb, Ic.X - rc)));
            maxXv = Math.Max(maxXv, Math.Max(Ia.X + ra, Math.Max(Ib.X + rb, Ic.X + rc)));
            minY = Math.Min(minY, Math.Min(Ia.Y - ra, Math.Min(Ib.Y - rb, Ic.Y - rc)));
            maxYv = Math.Max(maxYv, Math.Max(Ia.Y + ra, Math.Max(Ib.Y + rb, Ic.Y + rc)));

            float rangeX = maxXv - minX;
            float rangeY = maxYv - minY;

            float availW = imgW - 2 * margin;
            float availH = imgH - 2 * margin;

            float scale = Math.Min(availW / rangeX, availH / rangeY);
            scale = Math.Max(0.1f, scale);

            float offX = imgW / 2f - (minX + rangeX / 2f) * scale;
            float offY = imgH / 2f - (minY + rangeY / 2f) * scale;

            A = new PointF(rawA.X * scale + offX, rawA.Y * scale + offY);
            B = new PointF(rawB.X * scale + offX, rawB.Y * scale + offY);
            C = new PointF(rawC.X * scale + offX, rawC.Y * scale + offY);

            return true;
        }

        public static float SemiPerimeter(float a, float b, float c)
        {
            return (a + b + c) / 2f;
        }

        public static float Area(float a, float b, float c)
        {
            float s = SemiPerimeter(a, b, c);
            return (float)Math.Sqrt(s * (s - a) * (s - b) * (s - c));
        }

        /// <summary>
        /// Радиус вневписанной окружности, касающейся стороны a.
        /// ra = S / (s - a)
        /// </summary>
        public static float ExcircleRadius(float a, float b, float c)
        {
            float s = SemiPerimeter(a, b, c);
            float area = Area(a, b, c);
            float denom = s - a;
            if (Math.Abs(denom) < 0.001f) return 0;
            return area / denom;
        }

        /// <summary>
        /// Вычисляет центры вневписанных окружностей по координатам вершин.
        /// 
        /// Вневписанная окружность, касающаяся стороны a (BC):
        ///   Центр Ia = (-a*A + b*B + c*C) / (-a + b + c)
        ///   (отрицательный коэффициент у противоположной вершины)
        /// </summary>
        public static void ComputeExcircles(PointF A, PointF B, PointF C,
            float a, float b, float c,
            out List<CircleData> circles)
        {
            circles = new List<CircleData>();

            float s = SemiPerimeter(a, b, c);
            float area = Area(a, b, c);

            // Вневписанная напротив A (касается стороны a = BC)
            // Ia = (-a*A + b*B + c*C) / (-a + b + c)
            float dA = -a + b + c;
            if (Math.Abs(dA) > 0.01f)
            {
                float ra = area / (s - a);
                PointF Ia = new PointF(
                    (-a * A.X + b * B.X + c * C.X) / dA,
                    (-a * A.Y + b * B.Y + c * C.Y) / dA);
                circles.Add(new CircleData(Ia, ra, "Ia", Color.FromArgb(255, 80, 80)));
            }

            // Вневписанная напротив B (касается стороны b = AC)
            float dB = a - b + c;
            if (Math.Abs(dB) > 0.01f)
            {
                float rb = area / (s - b);
                PointF Ib = new PointF(
                    (a * A.X - b * B.X + c * C.X) / dB,
                    (a * A.Y - b * B.Y + c * C.Y) / dB);
                circles.Add(new CircleData(Ib, rb, "Ib", Color.FromArgb(80, 255, 80)));
            }

            // Вневписанная напротив C (касается стороны c = AB)
            float dC = a + b - c;
            if (Math.Abs(dC) > 0.01f)
            {
                float rc = area / (s - c);
                PointF Ic = new PointF(
                    (a * A.X + b * B.X - c * C.X) / dC,
                    (a * A.Y + b * B.Y - c * C.Y) / dC);
                circles.Add(new CircleData(Ic, rc, "Ic", Color.FromArgb(80, 180, 255)));
            }
        }

        /// <summary>
        /// Версия для «сырых» координат (до масштабирования)
        /// </summary>
        public static void ComputeExcirclesRaw(PointF A, PointF B, PointF C,
            float a, float b, float c,
            out PointF Ia, out float ra,
            out PointF Ib, out float rb,
            out PointF Ic, out float rc)
        {
            float s = SemiPerimeter(a, b, c);
            float area = Area(a, b, c);

            float dA = -a + b + c;
            ra = Math.Abs(s - a) > 0.01f ? area / (s - a) : 0;
            Ia = Math.Abs(dA) > 0.01f
                ? new PointF((-a * A.X + b * B.X + c * C.X) / dA,
                             (-a * A.Y + b * B.Y + c * C.Y) / dA)
                : A;

            float dB = a - b + c;
            rb = Math.Abs(s - b) > 0.01f ? area / (s - b) : 0;
            Ib = Math.Abs(dB) > 0.01f
                ? new PointF((a * A.X - b * B.X + c * C.X) / dB,
                             (a * A.Y - b * B.Y + c * C.Y) / dB)
                : B;

            float dC = a + b - c;
            rc = Math.Abs(s - c) > 0.01f ? area / (s - c) : 0;
            Ic = Math.Abs(dC) > 0.01f
                ? new PointF((a * A.X + b * B.X - c * C.X) / dC,
                             (a * A.Y + b * B.Y - c * C.Y) / dC)
                : C;
        }

        public static float Dist(PointF p1, PointF p2)
        {
            float dx = p1.X - p2.X, dy = p1.Y - p2.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }
    }

    // ═══════════════════════════════════════════
    //  ПАНЕЛЬ С МАСШТАБИРОВАНИЕМ
    // ═══════════════════════════════════════════
    public class ZoomPanel : Panel
    {
        private Bitmap sourceImage;
        private float zoom = 1f;
        private PointF offset = PointF.Empty;
        private bool dragging = false;
        private Point dragStart;
        private PointF offsetStart;
        private Point hoverPixel = new Point(-1, -1);

        public Func<int, int, string> PixelInfoFunc;

        private Label lblZoom;
        private Label lblPixel;

        public ZoomPanel()
        {
            DoubleBuffered = true;
            BackColor = Color.FromArgb(15, 15, 20);

            Panel bar = new Panel();
            bar.Dock = DockStyle.Bottom;
            bar.Height = 28;
            bar.BackColor = Color.FromArgb(30, 30, 40);
            Controls.Add(bar);

            int bx = 3;
            bar.Controls.Add(MakeSmallBtn("+", ref bx, delegate { DoZoom(1.5f); }));
            bar.Controls.Add(MakeSmallBtn("-", ref bx, delegate { DoZoom(1f / 1.5f); }));
            bar.Controls.Add(MakeSmallBtn("1:1", ref bx, delegate { ResetZoom(); }));
            bar.Controls.Add(MakeSmallBtn("Fit", ref bx, delegate { FitZoom(); }));

            lblZoom = new Label();
            lblZoom.Text = "x1.0";
            lblZoom.Location = new Point(bx + 5, 5);
            lblZoom.AutoSize = true;
            lblZoom.ForeColor = Color.FromArgb(150, 200, 255);
            lblZoom.Font = new Font("Consolas", 8);
            bar.Controls.Add(lblZoom);

            lblPixel = new Label();
            lblPixel.Text = "";
            lblPixel.Location = new Point(bx + 60, 5);
            lblPixel.AutoSize = true;
            lblPixel.ForeColor = Color.FromArgb(255, 220, 100);
            lblPixel.Font = new Font("Consolas", 8);
            bar.Controls.Add(lblPixel);

            MouseDown += delegate (object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left)
                {
                    dragging = true; dragStart = e.Location;
                    offsetStart = offset; Cursor = Cursors.SizeAll;
                }
            };
            MouseMove += delegate (object s, MouseEventArgs e)
            {
                if (dragging)
                {
                    offset = new PointF(
                        offsetStart.X + (dragStart.X - e.X) / zoom,
                        offsetStart.Y + (dragStart.Y - e.Y) / zoom);
                    Invalidate();
                }
                else
                {
                    UpdateHover(e.Location);
                }
            };
            MouseUp += delegate { dragging = false; Cursor = Cursors.Default; };
            MouseWheel += delegate (object s, MouseEventArgs e)
            {
                float f = e.Delta > 0 ? 1.3f : 1f / 1.3f;
                ZoomAt(e.Location, f);
            };
            MouseLeave += delegate { hoverPixel = new Point(-1, -1); Invalidate(); };
        }

        Button MakeSmallBtn(string text, ref int x, EventHandler handler)
        {
            Button b = new Button();
            b.Text = text;
            b.Size = new Size(35, 22);
            b.Location = new Point(x, 3);
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = Color.FromArgb(55, 55, 75);
            b.ForeColor = Color.White;
            b.Font = new Font("Consolas", 8, FontStyle.Bold);
            b.Click += handler;
            x += 38;
            return b;
        }

        void UpdateHover(Point screen)
        {
            if (sourceImage == null) return;
            int px = (int)Math.Floor(screen.X / zoom + offset.X);
            int py = (int)Math.Floor(screen.Y / zoom + offset.Y);
            if (px >= 0 && px < sourceImage.Width && py >= 0 && py < sourceImage.Height)
            {
                if (hoverPixel.X != px || hoverPixel.Y != py)
                {
                    hoverPixel = new Point(px, py);
                    Color c = sourceImage.GetPixel(px, py);
                    string info = "(" + px + "," + py + ") RGB(" + c.R + "," + c.G + "," + c.B + ")";
                    if (PixelInfoFunc != null) info += " " + PixelInfoFunc(px, py);
                    lblPixel.Text = info;
                    Invalidate();
                }
            }
            else
            {
                hoverPixel = new Point(-1, -1);
                lblPixel.Text = "";
                Invalidate();
            }
        }

        public void SetImage(Bitmap bmp)
        {
            if (sourceImage != null) sourceImage.Dispose();
            sourceImage = bmp;
            Invalidate();
        }

        public Bitmap GetImage() { return sourceImage; }

        public void ResetZoom() { zoom = 1f; offset = PointF.Empty; lblZoom.Text = "x1.0"; Invalidate(); }

        public void FitZoom()
        {
            if (sourceImage == null) return;
            float zx = (float)Width / sourceImage.Width;
            float zy = (float)(Height - 28) / sourceImage.Height;
            zoom = Math.Min(zx, zy);
            offset = PointF.Empty;
            lblZoom.Text = "x" + zoom.ToString("F1");
            Invalidate();
        }

        void DoZoom(float factor)
        {
            ZoomAt(new Point(Width / 2, (Height - 28) / 2), factor);
        }

        void ZoomAt(Point sp, float factor)
        {
            float ix = sp.X / zoom + offset.X;
            float iy = sp.Y / zoom + offset.Y;
            zoom *= factor;
            zoom = Math.Max(0.2f, Math.Min(50f, zoom));
            offset = new PointF(ix - sp.X / zoom, iy - sp.Y / zoom);
            lblZoom.Text = "x" + zoom.ToString("F1");
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            if (sourceImage == null) return;

            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            int dH = Height - 28;
            g.SetClip(new Rectangle(0, 0, Width, dH));

            float dx = -offset.X * zoom, dy = -offset.Y * zoom;
            float dw = sourceImage.Width * zoom, ddh = sourceImage.Height * zoom;
            g.DrawImage(sourceImage, dx, dy, dw, ddh);

            if (zoom >= 4f)
            {
                using (Pen gp = new Pen(Color.FromArgb(35, 255, 255, 255)))
                {
                    int sx = Math.Max(0, (int)offset.X);
                    int sy = Math.Max(0, (int)offset.Y);
                    int ex = Math.Min(sourceImage.Width, (int)(offset.X + Width / zoom) + 1);
                    int ey = Math.Min(sourceImage.Height, (int)(offset.Y + dH / zoom) + 1);
                    for (int px = sx; px <= ex; px++)
                        g.DrawLine(gp, (px - offset.X) * zoom, 0, (px - offset.X) * zoom, dH);
                    for (int py = sy; py <= ey; py++)
                        g.DrawLine(gp, 0, (py - offset.Y) * zoom, Width, (py - offset.Y) * zoom);
                }
            }

            if (hoverPixel.X >= 0)
            {
                float hx = (hoverPixel.X - offset.X) * zoom;
                float hy = (hoverPixel.Y - offset.Y) * zoom;
                using (Pen hp = new Pen(Color.Yellow, 2))
                    g.DrawRectangle(hp, hx, hy, zoom, zoom);
            }

            g.ResetClip();
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }
    }

    // ═══════════════════════════════════════════
    //  ГЛАВНАЯ ФОРМА
    // ═══════════════════════════════════════════
    public class MainForm : Form
    {
        NumericUpDown nudA, nudB, nudC;
        TabControl tabs;
        ZoomPanel[] panels = new ZoomPanel[5];
        Label lblStatus;
        Bitmap backgroundImage = null;

        int imgW = 700, imgH = 600;

        // Для попиксельного сравнения
        HashSet<Point> lastSetEq, lastSetParam, lastSetBres;

        public MainForm()
        {
            Text = "Треугольник и вневписанные окружности — Растеризация окружностей";
            Size = new Size(1150, 780);
            MinimumSize = new Size(900, 650);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(35, 35, 45);
            ForeColor = Color.White;

            BuildUI();
            UpdateAll();
        }

        void BuildUI()
        {
            Panel left = new Panel();
            left.Dock = DockStyle.Left;
            left.Width = 265;
            left.BackColor = Color.FromArgb(40, 40, 55);
            left.AutoScroll = true;
            Controls.Add(left);

            int y = 10;

            left.Controls.Add(ML("СТОРОНЫ ТРЕУГОЛЬНИКА", 10, y, Color.FromArgb(100, 200, 255), true));
            y += 30;

            left.Controls.Add(ML("a (BC):", 10, y, Color.FromArgb(255, 80, 80), true));
            nudA = MakeNud(left, 80, y, 200); y += 32;

            left.Controls.Add(ML("b (AC):", 10, y, Color.FromArgb(80, 255, 80), true));
            nudB = MakeNud(left, 80, y, 250); y += 32;

            left.Controls.Add(ML("c (AB):", 10, y, Color.FromArgb(80, 180, 255), true));
            nudC = MakeNud(left, 80, y, 300); y += 40;

            left.Controls.Add(MB("Нарисовать", ref y, Color.FromArgb(50, 180, 80),
                delegate { UpdateAll(); }));
            left.Controls.Add(MB("Равносторонний (200)", ref y, Color.FromArgb(100, 150, 50),
                delegate { nudA.Value = 200; nudB.Value = 200; nudC.Value = 200; }));
            left.Controls.Add(MB("Прямоугольный (3-4-5)", ref y, Color.FromArgb(50, 130, 180),
                delegate { nudA.Value = 300; nudB.Value = 400; nudC.Value = 500; }));
            left.Controls.Add(MB("Тупоугольный", ref y, Color.FromArgb(180, 100, 50),
                delegate { nudA.Value = 150; nudB.Value = 200; nudC.Value = 320; }));
            left.Controls.Add(MB("Случайный", ref y, Color.FromArgb(150, 80, 200),
                delegate { RandomSides(); }));

            y += 10;
            left.Controls.Add(ML("ФАЙЛЫ", 10, y, Color.FromArgb(255, 200, 100), true));
            y += 22;

            left.Controls.Add(MB("Загрузить фон", ref y, Color.FromArgb(80, 130, 180),
                delegate { LoadBg(); }));
            left.Controls.Add(MB("Сохранить все", ref y, Color.FromArgb(60, 160, 120),
                delegate { SaveAll(); }));
            left.Controls.Add(MB("Сохранить текущее", ref y, Color.FromArgb(60, 140, 160),
                delegate { SaveCurrent(); }));

            y += 10;
            left.Controls.Add(ML("Колёсико = зум\nЛКМ = панорама\nСетка от 4x", 10, y,
                Color.FromArgb(140, 140, 160), false));
            y += 55;

            left.Controls.Add(ML("ИНФОРМАЦИЯ", 10, y, Color.FromArgb(200, 200, 200), true));
            y += 20;

            lblStatus = new Label();
            lblStatus.Location = new Point(10, y);
            lblStatus.Size = new Size(240, 200);
            lblStatus.ForeColor = Color.LightGray;
            lblStatus.Font = new Font("Consolas", 8);
            left.Controls.Add(lblStatus);

            // Вкладки
            tabs = new TabControl();
            tabs.Dock = DockStyle.Fill;
            tabs.Font = new Font("Segoe UI", 9);
            Controls.Add(tabs);

            string[] names = {
                "1. Уравнение окружности",
                "2. Параметрическое",
                "3. Брезенхем",
                "4. Встроенный DrawEllipse",
                "5. Сравнение"
            };

            for (int i = 0; i < 5; i++)
            {
                TabPage tp = new TabPage(names[i]);
                tp.BackColor = Color.FromArgb(20, 20, 30);
                ZoomPanel zp = new ZoomPanel();
                zp.Dock = DockStyle.Fill;
                int idx = i;
                zp.PixelInfoFunc = delegate (int px, int py) { return PixelInfo(px, py, idx); };
                panels[i] = zp;
                tp.Controls.Add(zp);
                tabs.TabPages.Add(tp);
            }

            Controls.SetChildIndex(tabs, 0);
            Controls.SetChildIndex(left, 1);
        }

        Label ML(string text, int x, int y, Color c, bool bold)
        {
            Label l = new Label();
            l.Text = text; l.Location = new Point(x, y); l.AutoSize = true;
            l.ForeColor = c;
            l.Font = new Font("Segoe UI", 9, bold ? FontStyle.Bold : FontStyle.Regular);
            return l;
        }

        Button MB(string text, ref int y, Color c, EventHandler h)
        {
            Button b = new Button();
            b.Text = text; b.Location = new Point(15, y);
            b.Size = new Size(230, 30); b.FlatStyle = FlatStyle.Flat;
            b.BackColor = c; b.ForeColor = Color.White;
            b.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            b.Click += h;
            y += 35;
            return b;
        }

        NumericUpDown MakeNud(Panel parent, int x, int y, int val)
        {
            NumericUpDown n = new NumericUpDown();
            n.Location = new Point(x, y - 2);
            n.Width = 100; n.Minimum = 10; n.Maximum = 2000;
            n.Value = val; n.DecimalPlaces = 0;
            n.BackColor = Color.FromArgb(55, 55, 70);
            n.ForeColor = Color.White;
            n.ValueChanged += delegate { UpdateAll(); };
            parent.Controls.Add(n);
            return n;
        }

        // ═══════════════════════════════════════════
        //  ОСНОВНАЯ ЛОГИКА
        // ═══════════════════════════════════════════

        void UpdateAll()
        {
            float a = (float)nudA.Value;
            float b = (float)nudB.Value;
            float c = (float)nudC.Value;

            PointF A, B, C;
            if (!TriangleGeometry.ComputeVertices(a, b, c, imgW, imgH, 40, out A, out B, out C))
            {
                lblStatus.Text = "Невозможно построить\nтреугольник!\n\n" +
                    "Проверьте неравенство\nтреугольника:\n" +
                    "a + b > c\na + c > b\nb + c > a";
                for (int i = 0; i < 5; i++)
                {
                    Bitmap err = new Bitmap(imgW, imgH);
                    using (Graphics g = Graphics.FromImage(err))
                    {
                        g.Clear(Color.FromArgb(30, 10, 10));
                        using (Font f = new Font("Segoe UI", 14, FontStyle.Bold))
                            g.DrawString("Невозможно построить треугольник!\nПроверьте неравенство треугольника.",
                                f, Brushes.OrangeRed, 30, imgH / 2 - 30);
                    }
                    panels[i].SetImage(err);
                }
                return;
            }

            // Стороны
            List<LineSegment> sides = new List<LineSegment>();
            sides.Add(new LineSegment(A, B));
            sides.Add(new LineSegment(B, C));
            sides.Add(new LineSegment(C, A));

            // Вневписанные окружности
            List<CircleData> circles;
            TriangleGeometry.ComputeExcircles(A, B, C, a, b, c, out circles);

            // Вкладка 1: Уравнение окружности
            Bitmap b1 = MakeBase();
            DrawTriangle(b1, sides);
            DrawCirclesMethod(b1, circles, CircleAlgorithms.ByEquation);
            Annotate(b1, A, B, C, circles, "Уравнение окружности (x²+y²=R²)",
                Color.FromArgb(0, 150, 255));
            panels[0].SetImage(b1);

            // Вкладка 2: Параметрическое
            Bitmap b2 = MakeBase();
            DrawTriangle(b2, sides);
            DrawCirclesMethod(b2, circles, CircleAlgorithms.Parametric);
            Annotate(b2, A, B, C, circles, "Параметрическое (x=R·cos t, y=R·sin t)",
                Color.FromArgb(255, 100, 50));
            panels[1].SetImage(b2);

            // Вкладка 3: Брезенхем
            Bitmap b3 = MakeBase();
            DrawTriangle(b3, sides);
            DrawCirclesMethod(b3, circles, CircleAlgorithms.Bresenham);
            Annotate(b3, A, B, C, circles, "Алгоритм Брезенхема",
                Color.FromArgb(50, 255, 100));
            panels[2].SetImage(b3);

            // Вкладка 4: Встроенный
            Bitmap b4 = MakeBase();
            DrawTriangle(b4, sides);
            DrawCirclesBuiltIn(b4, circles);
            Annotate(b4, A, B, C, circles, "Встроенный DrawEllipse",
                Color.FromArgb(255, 200, 50));
            panels[3].SetImage(b4);

            // Сбор данных для сравнения
            lastSetEq = CollectCirclePoints(circles, CircleAlgorithms.ByEquation);
            lastSetParam = CollectCirclePoints(circles, CircleAlgorithms.Parametric);
            lastSetBres = CollectCirclePoints(circles, CircleAlgorithms.Bresenham);

            // Вкладка 5: Сравнение
            Bitmap b5 = MakeComparison(sides, circles, A, B, C);
            panels[4].SetImage(b5);

            // Статистика
            float area = TriangleGeometry.Area(a, b, c);
            float sp = TriangleGeometry.SemiPerimeter(a, b, c);

            HashSet<Point> dEP = new HashSet<Point>(lastSetEq); dEP.SymmetricExceptWith(lastSetParam);
            HashSet<Point> dEB = new HashSet<Point>(lastSetEq); dEB.SymmetricExceptWith(lastSetBres);
            HashSet<Point> dPB = new HashSet<Point>(lastSetParam); dPB.SymmetricExceptWith(lastSetBres);

            string info = "a=" + a.ToString("F0") + " b=" + b.ToString("F0") +
                " c=" + c.ToString("F0") + "\n" +
                "S=" + area.ToString("F1") + " p=" + sp.ToString("F1") + "\n";

            foreach (CircleData cd in circles)
                info += cd.Label + ": r=" + cd.Radius.ToString("F1") + "\n";

            info += "---------------\n" +
                "Ур-ие:  " + lastSetEq.Count + " пикс\n" +
                "Парам:  " + lastSetParam.Count + " пикс\n" +
                "Брез:   " + lastSetBres.Count + " пикс\n" +
                "---------------\n" +
                "Разл Ур-Пар: " + dEP.Count + "\n" +
                "Разл Ур-Бр:  " + dEB.Count + "\n" +
                "Разл Пар-Бр: " + dPB.Count;

            lblStatus.Text = info;
        }

        Bitmap MakeBase()
        {
            Bitmap bmp = new Bitmap(imgW, imgH);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                if (backgroundImage != null)
                {
                    g.DrawImage(backgroundImage, 0, 0, imgW, imgH);
                    using (SolidBrush br = new SolidBrush(Color.FromArgb(140, 0, 0, 0)))
                        g.FillRectangle(br, 0, 0, imgW, imgH);
                }
                else
                {
                    g.Clear(Color.FromArgb(15, 15, 25));
                }
            }
            return bmp;
        }

        void DrawTriangle(Bitmap bmp, List<LineSegment> sides)
        {
            foreach (LineSegment ln in sides)
            {
                List<Point> pts = CircleAlgorithms.LineBresenham(
                    (int)Math.Round(ln.A.X), (int)Math.Round(ln.A.Y),
                    (int)Math.Round(ln.B.X), (int)Math.Round(ln.B.Y));
                CircleAlgorithms.DrawPixels(bmp, pts, Color.White);
            }
        }

        void DrawCirclesMethod(Bitmap bmp, List<CircleData> circles,
            Func<int, int, int, List<Point>> algo)
        {
            foreach (CircleData cd in circles)
            {
                int cx = (int)Math.Round(cd.Center.X);
                int cy = (int)Math.Round(cd.Center.Y);
                int r = (int)Math.Round(cd.Radius);
                List<Point> pts = algo(cx, cy, r);
                CircleAlgorithms.DrawPixels(bmp, pts, cd.Color);
            }
        }

        void DrawCirclesBuiltIn(Bitmap bmp, List<CircleData> circles)
        {
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.None;
                foreach (CircleData cd in circles)
                {
                    float x = cd.Center.X - cd.Radius;
                    float y = cd.Center.Y - cd.Radius;
                    float d = cd.Radius * 2;
                    using (Pen pen = new Pen(cd.Color, 1.5f))
                        g.DrawEllipse(pen, x, y, d, d);
                }
            }
        }

        void Annotate(Bitmap bmp, PointF A, PointF B, PointF C,
            List<CircleData> circles, string title, Color titleColor)
        {
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;

                using (Font f = new Font("Segoe UI", 10, FontStyle.Bold))
                using (SolidBrush br = new SolidBrush(titleColor))
                    g.DrawString(title, f, br, 8, 6);

                Font fv = new Font("Segoe UI", 9, FontStyle.Bold);

                // Вершины
                DrawDot(g, A, "A", Color.White, fv);
                DrawDot(g, B, "B", Color.White, fv);
                DrawDot(g, C, "C", Color.White, fv);

                // Центры окружностей
                foreach (CircleData cd in circles)
                {
                    DrawDot(g, cd.Center, cd.Label + " r=" + cd.Radius.ToString("F0"),
                        cd.Color, fv);
                }

                // Подписи сторон
                using (Font fs = new Font("Consolas", 8))
                {
                    PointF mAB = Mid(A, B);
                    PointF mBC = Mid(B, C);
                    PointF mCA = Mid(C, A);
                    g.DrawString("c=" + TriangleGeometry.Dist(A, B).ToString("F0"), fs,
                        Brushes.LightGray, mAB.X + 5, mAB.Y);
                    g.DrawString("a=" + TriangleGeometry.Dist(B, C).ToString("F0"), fs,
                        Brushes.LightGray, mBC.X + 5, mBC.Y);
                    g.DrawString("b=" + TriangleGeometry.Dist(C, A).ToString("F0"), fs,
                        Brushes.LightGray, mCA.X + 5, mCA.Y);
                }

                // Легенда
                int ly = imgH - 60;
                using (Font fl = new Font("Consolas", 8))
                {
                    g.DrawString("-- Стороны треугольника", fl, Brushes.White, 8, ly);
                    ly += 14;
                    foreach (CircleData cd in circles)
                    {
                        using (SolidBrush cb = new SolidBrush(cd.Color))
                            g.DrawString("○ " + cd.Label + " — вневписанная", fl, cb, 8, ly);
                        ly += 14;
                    }
                }

                fv.Dispose();
            }
        }

        void DrawDot(Graphics g, PointF pt, string lbl, Color c, Font f)
        {
            using (SolidBrush br = new SolidBrush(c))
            {
                g.FillEllipse(br, pt.X - 4, pt.Y - 4, 8, 8);
                g.DrawString(lbl, f, br, pt.X + 6, pt.Y - 14);
            }
        }

        PointF Mid(PointF a, PointF b)
        {
            return new PointF((a.X + b.X) / 2, (a.Y + b.Y) / 2);
        }

        // ─── Сравнение ───

        Bitmap MakeComparison(List<LineSegment> sides, List<CircleData> circles,
            PointF A, PointF B, PointF C)
        {
            Bitmap bmp = new Bitmap(imgW, imgH);
            using (Graphics g = Graphics.FromImage(bmp))
                g.Clear(Color.FromArgb(10, 10, 15));

            // Треугольник
            DrawTriangle(bmp, sides);

            // Наложение окружностей
            HashSet<Point> all = new HashSet<Point>(lastSetEq);
            all.UnionWith(lastSetParam);
            all.UnionWith(lastSetBres);

            int diff = 0;
            foreach (Point p in all)
            {
                if (p.X < 0 || p.X >= imgW || p.Y < 0 || p.Y >= imgH) continue;

                bool e = lastSetEq.Contains(p);
                bool pr = lastSetParam.Contains(p);
                bool br = lastSetBres.Contains(p);

                Color col;
                if (e && pr && br)
                {
                    col = Color.FromArgb(120, 120, 120);
                }
                else
                {
                    diff++;
                    int cr = 0, cg = 0, cb = 0;
                    if (e) { cb = 255; }
                    if (pr) { cr = 255; cg += 50; }
                    if (br) { cg = 255; }
                    col = Color.FromArgb(Math.Min(255, cr), Math.Min(255, cg), Math.Min(255, cb));
                }
                bmp.SetPixel(p.X, p.Y, col);
            }

            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Font fb = new Font("Segoe UI", 10, FontStyle.Bold);
                Font fs = new Font("Consolas", 9);

                g.DrawString("СРАВНЕНИЕ — приблизьте колёсиком!", fb, Brushes.White, 8, 6);
                int yy = 28;
                g.DrawString("Серый = все совпали", fs, Brushes.Gray, 8, yy); yy += 16;
                g.DrawString("Синий = только Уравнение", fs,
                    new SolidBrush(Color.FromArgb(80, 80, 255)), 8, yy); yy += 16;
                g.DrawString("Красный = только Параметрич.", fs,
                    new SolidBrush(Color.FromArgb(255, 50, 50)), 8, yy); yy += 16;
                g.DrawString("Зелёный = только Брезенхем", fs,
                    new SolidBrush(Color.FromArgb(80, 255, 80)), 8, yy);

                string dt = diff > 0
                    ? "Различий: " + diff + " пикс — приблизьте!"
                    : "Все алгоритмы совпали!";
                Color dc = diff > 0 ? Color.OrangeRed : Color.LightGreen;
                g.DrawString(dt, fb, new SolidBrush(dc), 8, imgH - 28);

                DrawDot(g, A, "A", Color.White, fs);
                DrawDot(g, B, "B", Color.White, fs);
                DrawDot(g, C, "C", Color.White, fs);

                foreach (CircleData cd in circles)
                    DrawDot(g, cd.Center, cd.Label, cd.Color, fs);

                fb.Dispose();
                fs.Dispose();
            }

            return bmp;
        }

        HashSet<Point> CollectCirclePoints(List<CircleData> circles,
            Func<int, int, int, List<Point>> algo)
        {
            HashSet<Point> set = new HashSet<Point>();
            foreach (CircleData cd in circles)
            {
                int cx = (int)Math.Round(cd.Center.X);
                int cy = (int)Math.Round(cd.Center.Y);
                int r = (int)Math.Round(cd.Radius);
                foreach (Point p in algo(cx, cy, r))
                    set.Add(p);
            }
            return set;
        }

        string PixelInfo(int px, int py, int tab)
        {
            if (lastSetEq == null) return "";
            Point p = new Point(px, py);
            bool e = lastSetEq.Contains(p);
            bool pr = lastSetParam.Contains(p);
            bool br = lastSetBres.Contains(p);
            if (!e && !pr && !br) return "";

            string s = "[";
            if (e) s += "Ур ";
            if (pr) s += "Пар ";
            if (br) s += "Бр ";
            s += "]";
            if (e && pr && br) s += " ВСЕ";
            else s += " РАЗНИЦА!";
            return s;
        }

        // ─── Кнопки ───

        void RandomSides()
        {
            Random rnd = new Random();
            int s1 = rnd.Next(100, 400);
            int s2 = rnd.Next(100, 400);
            int s3 = rnd.Next(Math.Abs(s1 - s2) + 30, s1 + s2 - 10);
            nudA.Value = Math.Min(s1, (int)nudA.Maximum);
            nudB.Value = Math.Min(s2, (int)nudB.Maximum);
            nudC.Value = Math.Min(Math.Max(s3, (int)nudC.Minimum), (int)nudC.Maximum);
        }

        void LoadBg()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Filter = "Изображения|*.png;*.jpg;*.jpeg;*.bmp|Все|*.*";
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        if (backgroundImage != null) backgroundImage.Dispose();
                        using (Bitmap tmp = new Bitmap(dlg.FileName))
                            backgroundImage = new Bitmap(tmp);
                        imgW = Math.Max(400, Math.Min(1200, backgroundImage.Width));
                        imgH = Math.Max(300, Math.Min(900, backgroundImage.Height));
                        UpdateAll();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Ошибка: " + ex.Message);
                    }
                }
            }
        }

        void SaveAll()
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    string f = dlg.SelectedPath;
                    string t = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string[] n = { "1_Equation", "2_Parametric", "3_Bresenham",
                                   "4_BuiltIn", "5_Comparison" };
                    int cnt = 0;
                    for (int i = 0; i < 5; i++)
                    {
                        Bitmap img = panels[i].GetImage();
                        if (img != null)
                        {
                            img.Save(Path.Combine(f, n[i] + "_" + t + ".png"), ImageFormat.Png);
                            cnt++;
                        }
                    }
                    MessageBox.Show("Сохранено " + cnt + " файлов в:\n" + f);
                }
            }
        }

        void SaveCurrent()
        {
            int idx = tabs.SelectedIndex;
            Bitmap img = panels[idx].GetImage();
            if (img == null) { MessageBox.Show("Нет изображения."); return; }

            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Filter = "PNG|*.png|JPEG|*.jpg|BMP|*.bmp";
                dlg.FileName = "excircles_" + (idx + 1);
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    ImageFormat fmt = ImageFormat.Png;
                    string ext = Path.GetExtension(dlg.FileName).ToLower();
                    if (ext == ".jpg") fmt = ImageFormat.Jpeg;
                    else if (ext == ".bmp") fmt = ImageFormat.Bmp;
                    img.Save(dlg.FileName, fmt);
                    MessageBox.Show("Сохранено: " + dlg.FileName);
                }
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            if (backgroundImage != null) backgroundImage.Dispose();
        }
    }
}
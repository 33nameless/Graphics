using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace TriangleBisectors
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

    //  ОТРЕЗОК
    public struct LineSegment
    {
        public PointF A;
        public PointF B;
        public LineSegment(PointF a, PointF b) { A = a; B = b; }
    }

    //  АЛГОРИТМЫ РАСТЕРИЗАЦИИ
    public static class RasterAlgorithms
    {
        public static List<Point> DDA(int x1, int y1, int x2, int y2)
        {
            List<Point> pts = new List<Point>();
            int dx = x2 - x1, dy = y2 - y1;
            int steps = Math.Max(Math.Abs(dx), Math.Abs(dy));
            if (steps == 0) { pts.Add(new Point(x1, y1)); return pts; }
            float xInc = (float)dx / steps, yInc = (float)dy / steps;
            float x = x1, y = y1;
            for (int i = 0; i <= steps; i++)
            {
                pts.Add(new Point((int)Math.Round(x), (int)Math.Round(y)));
                x += xInc; y += yInc;
            }
            return pts;
        }

        public static List<Point> Bresenham(int x1, int y1, int x2, int y2)
        {
            List<Point> pts = new List<Point>();
            int dx = Math.Abs(x2 - x1), dy = Math.Abs(y2 - y1);
            int sx = x1 < x2 ? 1 : -1, sy = y1 < y2 ? 1 : -1;
            bool steep = dy > dx;
            if (steep) { int t = dx; dx = dy; dy = t; }
            float error = 0f, dErr = dx != 0 ? (float)dy / dx : 0f;
            int x = x1, y = y1;
            for (int i = 0; i <= dx; i++)
            {
                pts.Add(new Point(x, y));
                error += dErr;
                if (error >= 0.5f)
                {
                    if (steep) x += sx; else y += sy;
                    error -= 1f;
                }
                if (steep) y += sy; else x += sx;
            }
            return pts;
        }

        public static List<Point> BresenhamInteger(int x1, int y1, int x2, int y2)
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

        public static void DrawOnBitmap(Bitmap bmp, List<LineSegment> lines,
            Color color, Func<int, int, int, int, List<Point>> algo)
        {
            foreach (LineSegment ln in lines)
            {
                List<Point> pts = algo(
                    (int)Math.Round(ln.A.X), (int)Math.Round(ln.A.Y),
                    (int)Math.Round(ln.B.X), (int)Math.Round(ln.B.Y));
                foreach (Point p in pts)
                    if (p.X >= 0 && p.X < bmp.Width && p.Y >= 0 && p.Y < bmp.Height)
                        bmp.SetPixel(p.X, p.Y, color);
            }
        }

        public static void DrawBuiltIn(Bitmap bmp, List<LineSegment> lines,
            Color color, float width)
        {
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.None;
                using (Pen pen = new Pen(color, width))
                    foreach (LineSegment ln in lines)
                        g.DrawLine(pen, ln.A, ln.B);
            }
        }
    }

    //  ГЕОМЕТРИЯ
    public static class TriangleGeometry
    {
        public static float Dist(PointF a, PointF b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        public static PointF BisectorFoot(PointF A, PointF B, PointF C)
        {
            float AB = Dist(A, B), AC = Dist(A, C);
            if (AB + AC < 0.001f) return B;
            float t = AB / (AB + AC);
            return new PointF(B.X + t * (C.X - B.X), B.Y + t * (C.Y - B.Y));
        }

        public static void GetAll(PointF A, PointF B, PointF C,
            out List<LineSegment> sides, out List<LineSegment> bisectors)
        {
            sides = new List<LineSegment>();
            sides.Add(new LineSegment(A, B));
            sides.Add(new LineSegment(B, C));
            sides.Add(new LineSegment(C, A));

            bisectors = new List<LineSegment>();
            bisectors.Add(new LineSegment(A, BisectorFoot(A, B, C)));
            bisectors.Add(new LineSegment(B, BisectorFoot(B, A, C)));
            bisectors.Add(new LineSegment(C, BisectorFoot(C, A, B)));
        }

        public static PointF LineIntersect(PointF p1, PointF p2, PointF p3, PointF p4)
        {
            float d = (p1.X - p2.X) * (p3.Y - p4.Y) - (p1.Y - p2.Y) * (p3.X - p4.X);
            if (Math.Abs(d) < 0.0001f) return new PointF(float.NaN, float.NaN);
            float t = ((p1.X - p3.X) * (p3.Y - p4.Y) - (p1.Y - p3.Y) * (p3.X - p4.X)) / d;
            return new PointF(p1.X + t * (p2.X - p1.X), p1.Y + t * (p2.Y - p1.Y));
        }
    }

    //  ПАНЕЛЬ С МАСШТАБИРОВАНИЕМ И ПАНОРАМОЙ
    public class ZoomPanel : Panel
    {
        private Bitmap sourceImage;
        private float zoom = 1f;
        private PointF offset = PointF.Empty;   // смещение в пикселях изображения
        private bool dragging = false;
        private Point dragStart;
        private PointF offsetStart;

        // Координаты пикселя под курсором
        private Point hoverPixel = new Point(-1, -1);

        // Информация о пикселе (для сравнения)
        public Func<int, int, string> PixelInfoCallback;

        // Подпись зума
        private Label lblZoom;
        private Label lblPixelInfo;

        public ZoomPanel()
        {
            DoubleBuffered = true;
            BackColor = Color.FromArgb(15, 15, 20);

            // Панель кнопок зума
            Panel zoomBar = new Panel();
            zoomBar.Dock = DockStyle.Bottom;
            zoomBar.Height = 30;
            zoomBar.BackColor = Color.FromArgb(30, 30, 40);
            Controls.Add(zoomBar);

            Button btnZoomIn = new Button();
            btnZoomIn.Text = "+";
            btnZoomIn.Size = new Size(35, 24);
            btnZoomIn.Location = new Point(3, 3);
            btnZoomIn.FlatStyle = FlatStyle.Flat;
            btnZoomIn.BackColor = Color.FromArgb(60, 60, 80);
            btnZoomIn.ForeColor = Color.White;
            btnZoomIn.Font = new Font("Consolas", 12, FontStyle.Bold);
            btnZoomIn.Click += delegate { ZoomIn(); };
            zoomBar.Controls.Add(btnZoomIn);

            Button btnZoomOut = new Button();
            btnZoomOut.Text = "-";
            btnZoomOut.Size = new Size(35, 24);
            btnZoomOut.Location = new Point(42, 3);
            btnZoomOut.FlatStyle = FlatStyle.Flat;
            btnZoomOut.BackColor = Color.FromArgb(60, 60, 80);
            btnZoomOut.ForeColor = Color.White;
            btnZoomOut.Font = new Font("Consolas", 12, FontStyle.Bold);
            btnZoomOut.Click += delegate { ZoomOut(); };
            zoomBar.Controls.Add(btnZoomOut);

            Button btnReset = new Button();
            btnReset.Text = "1:1";
            btnReset.Size = new Size(45, 24);
            btnReset.Location = new Point(81, 3);
            btnReset.FlatStyle = FlatStyle.Flat;
            btnReset.BackColor = Color.FromArgb(60, 60, 80);
            btnReset.ForeColor = Color.White;
            btnReset.Font = new Font("Consolas", 8);
            btnReset.Click += delegate { ResetZoom(); };
            zoomBar.Controls.Add(btnReset);

            Button btnFit = new Button();
            btnFit.Text = "Вписать";
            btnFit.Size = new Size(60, 24);
            btnFit.Location = new Point(130, 3);
            btnFit.FlatStyle = FlatStyle.Flat;
            btnFit.BackColor = Color.FromArgb(60, 60, 80);
            btnFit.ForeColor = Color.White;
            btnFit.Font = new Font("Consolas", 8);
            btnFit.Click += delegate { FitZoom(); };
            zoomBar.Controls.Add(btnFit);

            lblZoom = new Label();
            lblZoom.Text = "x1.0";
            lblZoom.Location = new Point(200, 6);
            lblZoom.AutoSize = true;
            lblZoom.ForeColor = Color.FromArgb(150, 200, 255);
            lblZoom.Font = new Font("Consolas", 9, FontStyle.Bold);
            zoomBar.Controls.Add(lblZoom);

            lblPixelInfo = new Label();
            lblPixelInfo.Text = "";
            lblPixelInfo.Location = new Point(280, 6);
            lblPixelInfo.AutoSize = true;
            lblPixelInfo.ForeColor = Color.FromArgb(255, 220, 100);
            lblPixelInfo.Font = new Font("Consolas", 8);
            zoomBar.Controls.Add(lblPixelInfo);

            // Обработчики мыши
            MouseDown += OnMouseDownHandler;
            MouseMove += OnMouseMoveHandler;
            MouseUp += OnMouseUpHandler;
            MouseWheel += OnMouseWheelHandler;
            MouseLeave += delegate { hoverPixel = new Point(-1, -1); Invalidate(); };
        }

        public void SetImage(Bitmap bmp)
        {
            if (sourceImage != null) sourceImage.Dispose();
            sourceImage = bmp;
            Invalidate();
        }

        public Bitmap GetImage()
        {
            return sourceImage;
        }

        public void ResetZoom()
        {
            zoom = 1f;
            offset = PointF.Empty;
            UpdateZoomLabel();
            Invalidate();
        }

        public void FitZoom()
        {
            if (sourceImage == null) return;
            int drawH = Height - 30; // минус панель кнопок
            float zx = (float)Width / sourceImage.Width;
            float zy = (float)drawH / sourceImage.Height;
            zoom = Math.Min(zx, zy);
            zoom = Math.Max(0.1f, zoom);
            offset = PointF.Empty;
            UpdateZoomLabel();
            Invalidate();
        }

        private void ZoomIn()
        {
            Point center = new Point(Width / 2, (Height - 30) / 2);
            ApplyZoom(center, 1.5f);
        }

        private void ZoomOut()
        {
            Point center = new Point(Width / 2, (Height - 30) / 2);
            ApplyZoom(center, 1f / 1.5f);
        }

        private void ApplyZoom(Point screenPt, float factor)
        {
            // Точка на изображении под курсором до зума
            float imgX = (screenPt.X / zoom) + offset.X;
            float imgY = (screenPt.Y / zoom) + offset.Y;

            float oldZoom = zoom;
            zoom *= factor;
            zoom = Math.Max(0.2f, Math.Min(50f, zoom));

            // Корректируем offset чтобы точка осталась на месте
            offset = new PointF(
                imgX - screenPt.X / zoom,
                imgY - screenPt.Y / zoom);

            UpdateZoomLabel();
            Invalidate();
        }

        private void UpdateZoomLabel()
        {
            if (lblZoom != null)
                lblZoom.Text = "x" + zoom.ToString("F1");
        }

        // Обработчики мыши

        private void OnMouseWheelHandler(object sender, MouseEventArgs e)
        {
            float factor = e.Delta > 0 ? 1.25f : 1f / 1.25f;
            ApplyZoom(e.Location, factor);
        }

        private void OnMouseDownHandler(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                dragging = true;
                dragStart = e.Location;
                offsetStart = offset;
                Cursor = Cursors.SizeAll;
            }
        }

        private void OnMouseMoveHandler(object sender, MouseEventArgs e)
        {
            if (dragging)
            {
                float dx = (dragStart.X - e.X) / zoom;
                float dy = (dragStart.Y - e.Y) / zoom;
                offset = new PointF(offsetStart.X + dx, offsetStart.Y + dy);
                Invalidate();
            }
            else
            {
                // Определяем пиксель под курсором
                int px = (int)Math.Floor(e.X / zoom + offset.X);
                int py = (int)Math.Floor(e.Y / zoom + offset.Y);

                if (sourceImage != null &&
                    px >= 0 && px < sourceImage.Width &&
                    py >= 0 && py < sourceImage.Height)
                {
                    if (hoverPixel.X != px || hoverPixel.Y != py)
                    {
                        hoverPixel = new Point(px, py);

                        Color c = sourceImage.GetPixel(px, py);
                        string info = "(" + px + "," + py + ") RGB(" +
                            c.R + "," + c.G + "," + c.B + ")";

                        if (PixelInfoCallback != null)
                            info += " " + PixelInfoCallback(px, py);

                        if (lblPixelInfo != null)
                            lblPixelInfo.Text = info;

                        Invalidate();
                    }
                }
                else
                {
                    hoverPixel = new Point(-1, -1);
                    if (lblPixelInfo != null)
                        lblPixelInfo.Text = "";
                    Invalidate();
                }
            }
        }

        private void OnMouseUpHandler(object sender, MouseEventArgs e)
        {
            dragging = false;
            Cursor = Cursors.Default;
        }

        // Рисование

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.Clear(BackColor);

            if (sourceImage == null) return;

            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            // Область рисования (без нижней панели)
            int drawH = Height - 30;

            // Вычисляем прямоугольник отрисовки
            float drawX = -offset.X * zoom;
            float drawY = -offset.Y * zoom;
            float drawW = sourceImage.Width * zoom;
            float drawDH = sourceImage.Height * zoom;

            // Ограничиваем область рисования
            g.SetClip(new Rectangle(0, 0, Width, drawH));

            g.DrawImage(sourceImage, drawX, drawY, drawW, drawDH);

            // СЕТКА при увеличении >= 4x
            if (zoom >= 4f)
            {
                using (Pen gridPen = new Pen(Color.FromArgb(40, 255, 255, 255)))
                {
                    // Вычисляем видимый диапазон пикселей
                    int startPx = Math.Max(0, (int)offset.X);
                    int startPy = Math.Max(0, (int)offset.Y);
                    int endPx = Math.Min(sourceImage.Width,
                        (int)(offset.X + Width / zoom) + 1);
                    int endPy = Math.Min(sourceImage.Height,
                        (int)(offset.Y + drawH / zoom) + 1);

                    // Вертикальные линии
                    for (int px = startPx; px <= endPx; px++)
                    {
                        float sx = (px - offset.X) * zoom;
                        g.DrawLine(gridPen, sx, 0, sx, drawH);
                    }

                    // Горизонтальные линии
                    for (int py = startPy; py <= endPy; py++)
                    {
                        float sy = (py - offset.Y) * zoom;
                        g.DrawLine(gridPen, 0, sy, Width, sy);
                    }
                }
            }

            // Подсветка пикселя под курсором
            if (hoverPixel.X >= 0 && hoverPixel.Y >= 0)
            {
                float hx = (hoverPixel.X - offset.X) * zoom;
                float hy = (hoverPixel.Y - offset.Y) * zoom;

                using (Pen hlPen = new Pen(Color.FromArgb(200, 255, 255, 0), 2))
                {
                    g.DrawRectangle(hlPen, hx, hy, zoom, zoom);
                }

                // Координаты рядом с курсором при большом зуме
                if (zoom >= 6f)
                {
                    string coordText = hoverPixel.X + "," + hoverPixel.Y;
                    using (Font f = new Font("Consolas", 7))
                    {
                        SizeF sz = g.MeasureString(coordText, f);
                        float tx = hx + zoom + 2;
                        float ty = hy;
                        if (tx + sz.Width > Width) tx = hx - sz.Width - 2;
                        if (ty + sz.Height > drawH) ty = hy - sz.Height;

                        using (SolidBrush bg = new SolidBrush(Color.FromArgb(180, 0, 0, 0)))
                            g.FillRectangle(bg, tx - 1, ty - 1, sz.Width + 2, sz.Height + 2);
                        g.DrawString(coordText, f, Brushes.Yellow, tx, ty);
                    }
                }
            }

            g.ResetClip();

            // Подсказка
            if (zoom <= 1.1f)
            {
                using (Font f = new Font("Consolas", 8))
                    g.DrawString("Колёсико = зум, ЛКМ = перемещение", f,
                        new SolidBrush(Color.FromArgb(100, 255, 255, 255)),
                        5, drawH - 16);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Invalidate();
        }
    }

    //  ГЛАВНАЯ ФОРМА
    public class MainForm : Form
    {
        PointF vertA = new PointF(300, 50);
        PointF vertB = new PointF(100, 400);
        PointF vertC = new PointF(500, 350);

        NumericUpDown nudAX, nudAY, nudBX, nudBY, nudCX, nudCY;
        TabControl tabControl;
        ZoomPanel[] zoomPanels = new ZoomPanel[5];
        Label lblStatus;

        int imgW = 600, imgH = 500;
        Bitmap backgroundImage = null;

        // Данные для попиксельного сравнения
        HashSet<Point> lastSetDDA, lastSetBres, lastSetBresI;

        public MainForm()
        {
            Text = "Треугольник и биссектрисы — Растеризация (зум колёсиком, перетаскивание ЛКМ)";
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
            // ЛЕВАЯ ПАНЕЛЬ
            Panel left = new Panel();
            left.Dock = DockStyle.Left;
            left.Width = 260;
            left.BackColor = Color.FromArgb(40, 40, 55);
            left.Padding = new Padding(10);
            left.AutoScroll = true;
            Controls.Add(left);

            int y = 10;

            left.Controls.Add(MakeLabel("ВЕРШИНЫ ТРЕУГОЛЬНИКА", 10, y,
                Color.FromArgb(100, 200, 255), true));
            y += 35;

            y = AddVertex(left, "Вершина A:", ref nudAX, ref nudAY,
                (int)vertA.X, (int)vertA.Y, y, Color.FromArgb(255, 80, 80));
            y = AddVertex(left, "Вершина B:", ref nudBX, ref nudBY,
                (int)vertB.X, (int)vertB.Y, y, Color.FromArgb(80, 255, 80));
            y = AddVertex(left, "Вершина C:", ref nudCX, ref nudCY,
                (int)vertC.X, (int)vertC.Y, y, Color.FromArgb(80, 180, 255));

            y += 10;

            left.Controls.Add(MakeBtn("Нарисовать", y, Color.FromArgb(50, 180, 80),
                delegate { UpdateAll(); }));
            y += 40;
            left.Controls.Add(MakeBtn("Сбросить", y, Color.FromArgb(180, 150, 50),
                delegate { ResetVerts(); }));
            y += 40;
            left.Controls.Add(MakeBtn("Случайный", y, Color.FromArgb(150, 80, 200),
                delegate { RandomVerts(); }));
            y += 50;

            left.Controls.Add(MakeLabel("ФАЙЛЫ", 10, y,
                Color.FromArgb(255, 200, 100), true));
            y += 25;

            left.Controls.Add(MakeBtn("Загрузить фон", y,
                Color.FromArgb(80, 130, 180), delegate { LoadBg(); }));
            y += 40;
            left.Controls.Add(MakeBtn("Сохранить все (5 шт)", y,
                Color.FromArgb(60, 160, 120), delegate { SaveAll(); }));
            y += 40;
            left.Controls.Add(MakeBtn("Сохранить текущее", y,
                Color.FromArgb(60, 140, 160), delegate { SaveCurrent(); }));
            y += 50;

            left.Controls.Add(MakeLabel("УПРАВЛЕНИЕ", 10, y,
                Color.FromArgb(180, 220, 255), true));
            y += 25;
            left.Controls.Add(MakeLabel(
                "Колёсико мыши = зум\n" +
                "ЛКМ + тянуть = панорама\n" +
                "Навести = инфо о пикселе\n" +
                "Сетка от 4x зума",
                10, y, Color.FromArgb(160, 160, 180), false));
            y += 70;

            left.Controls.Add(MakeLabel("СТАТИСТИКА", 10, y,
                Color.FromArgb(200, 200, 200), true));
            y += 22;

            lblStatus = new Label();
            lblStatus.Location = new Point(10, y);
            lblStatus.Size = new Size(230, 160);
            lblStatus.ForeColor = Color.LightGray;
            lblStatus.Font = new Font("Consolas", 8);
            lblStatus.Text = "Готово";
            left.Controls.Add(lblStatus);

            // ВКЛАДКИ
            tabControl = new TabControl();
            tabControl.Dock = DockStyle.Fill;
            tabControl.Font = new Font("Segoe UI", 9);
            Controls.Add(tabControl);

            string[] names = {
                "1. ЦДА (DDA)",
                "2. Брезенхем",
                "3. Целочисл. Брезенхем",
                "4. Встроенный DrawLine",
                "5. Сравнение (наложение)"
            };

            for (int i = 0; i < 5; i++)
            {
                TabPage tp = new TabPage(names[i]);
                tp.BackColor = Color.FromArgb(20, 20, 30);

                ZoomPanel zp = new ZoomPanel();
                zp.Dock = DockStyle.Fill;

                // Для вкладки сравнения — callback информации о пикселе
                int tabIdx = i;
                zp.PixelInfoCallback = delegate (int px, int py)
                {
                    return GetPixelAlgoInfo(px, py, tabIdx);
                };

                zoomPanels[i] = zp;
                tp.Controls.Add(zp);
                tabControl.TabPages.Add(tp);
            }

            Controls.SetChildIndex(tabControl, 0);
            Controls.SetChildIndex(left, 1);
        }

        // Хелперы UI 

        Label MakeLabel(string text, int x, int y, Color color, bool bold)
        {
            Label lbl = new Label();
            lbl.Text = text;
            lbl.Location = new Point(x, y);
            lbl.AutoSize = true;
            lbl.ForeColor = color;
            lbl.Font = new Font("Segoe UI", 9, bold ? FontStyle.Bold : FontStyle.Regular);
            return lbl;
        }

        Button MakeBtn(string text, int y, Color color, EventHandler handler)
        {
            Button btn = new Button();
            btn.Text = text;
            btn.Location = new Point(15, y);
            btn.Size = new Size(220, 32);
            btn.FlatStyle = FlatStyle.Flat;
            btn.BackColor = color;
            btn.ForeColor = Color.White;
            btn.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            btn.Cursor = Cursors.Hand;
            btn.Click += handler;
            return btn;
        }

        int AddVertex(Panel parent, string label,
            ref NumericUpDown nx, ref NumericUpDown ny,
            int vx, int vy, int yy, Color color)
        {
            parent.Controls.Add(MakeLabel(label, 10, yy, color, true));
            yy += 22;

            parent.Controls.Add(MakeLabel("X:", 15, yy + 3, Color.White, false));
            nx = new NumericUpDown();
            nx.Location = new Point(35, yy);
            nx.Width = 80;
            nx.Minimum = 0;
            nx.Maximum = 2000;
            nx.Value = vx;
            nx.BackColor = Color.FromArgb(55, 55, 70);
            nx.ForeColor = Color.White;
            nx.ValueChanged += delegate { UpdateAll(); };
            parent.Controls.Add(nx);

            parent.Controls.Add(MakeLabel("Y:", 125, yy + 3, Color.White, false));
            ny = new NumericUpDown();
            ny.Location = new Point(145, yy);
            ny.Width = 80;
            ny.Minimum = 0;
            ny.Maximum = 2000;
            ny.Value = vy;
            ny.BackColor = Color.FromArgb(55, 55, 70);
            ny.ForeColor = Color.White;
            ny.ValueChanged += delegate { UpdateAll(); };
            parent.Controls.Add(ny);

            return yy + 35;
        }
        //  ЛОГИКА

        void ReadVerts()
        {
            vertA = new PointF((float)nudAX.Value, (float)nudAY.Value);
            vertB = new PointF((float)nudBX.Value, (float)nudBY.Value);
            vertC = new PointF((float)nudCX.Value, (float)nudCY.Value);
        }

        void UpdateAll()
        {
            ReadVerts();

            List<LineSegment> sides, bisectors;
            TriangleGeometry.GetAll(vertA, vertB, vertC, out sides, out bisectors);

            Color cS = Color.White, cB = Color.Yellow;

            // ЦДА
            Bitmap b1 = MakeBase();
            RasterAlgorithms.DrawOnBitmap(b1, sides, cS, RasterAlgorithms.DDA);
            RasterAlgorithms.DrawOnBitmap(b1, bisectors, cB, RasterAlgorithms.DDA);
            Annotate(b1, "ЦДА (DDA)", Color.FromArgb(0, 150, 255));
            zoomPanels[0].SetImage(b1);

            // Брезенхем
            Bitmap b2 = MakeBase();
            RasterAlgorithms.DrawOnBitmap(b2, sides, cS, RasterAlgorithms.Bresenham);
            RasterAlgorithms.DrawOnBitmap(b2, bisectors, cB, RasterAlgorithms.Bresenham);
            Annotate(b2, "Брезенхем (вещественный)", Color.FromArgb(255, 100, 50));
            zoomPanels[1].SetImage(b2);

            // Целочисленный
            Bitmap b3 = MakeBase();
            RasterAlgorithms.DrawOnBitmap(b3, sides, cS, RasterAlgorithms.BresenhamInteger);
            RasterAlgorithms.DrawOnBitmap(b3, bisectors, cB, RasterAlgorithms.BresenhamInteger);
            Annotate(b3, "Целочисленный Брезенхем", Color.FromArgb(50, 255, 100));
            zoomPanels[2].SetImage(b3);

            // Встроенный
            Bitmap b4 = MakeBase();
            RasterAlgorithms.DrawBuiltIn(b4, sides, cS, 2f);
            RasterAlgorithms.DrawBuiltIn(b4, bisectors, cB, 1.5f);
            Annotate(b4, "Встроенный DrawLine", Color.FromArgb(255, 200, 50));
            zoomPanels[3].SetImage(b4);

            // Собираем точки для сравнения
            lastSetDDA = Collect(sides, bisectors, RasterAlgorithms.DDA);
            lastSetBres = Collect(sides, bisectors, RasterAlgorithms.Bresenham);
            lastSetBresI = Collect(sides, bisectors, RasterAlgorithms.BresenhamInteger);

            // Сравнение
            Bitmap b5 = MakeComparison(sides, bisectors);
            zoomPanels[4].SetImage(b5);

            // Статистика
            HashSet<Point> dDB = new HashSet<Point>(lastSetDDA);
            dDB.SymmetricExceptWith(lastSetBres);
            HashSet<Point> dDI = new HashSet<Point>(lastSetDDA);
            dDI.SymmetricExceptWith(lastSetBresI);
            HashSet<Point> dBI = new HashSet<Point>(lastSetBres);
            dBI.SymmetricExceptWith(lastSetBresI);

            float ab = TriangleGeometry.Dist(vertA, vertB);
            float bc = TriangleGeometry.Dist(vertB, vertC);
            float ca = TriangleGeometry.Dist(vertC, vertA);

            lblStatus.Text =
                "A(" + vertA.X.ToString("F0") + "," + vertA.Y.ToString("F0") + ")\n" +
                "B(" + vertB.X.ToString("F0") + "," + vertB.Y.ToString("F0") + ")\n" +
                "C(" + vertC.X.ToString("F0") + "," + vertC.Y.ToString("F0") + ")\n" +
                "AB=" + ab.ToString("F1") + " BC=" + bc.ToString("F1") +
                " CA=" + ca.ToString("F1") + "\n" +
                "---------------\n" +
                "ЦДА:   " + lastSetDDA.Count + " пикс\n" +
                "Брез:  " + lastSetBres.Count + " пикс\n" +
                "Целоч: " + lastSetBresI.Count + " пикс\n" +
                "---------------\n" +
                "Разл ЦДА-Брез: " + dDB.Count + "\n" +
                "Разл ЦДА-Цел:  " + dDI.Count + "\n" +
                "Разл Брез-Цел: " + dBI.Count + "\n" +
                (dDB.Count == 0 && dDI.Count == 0
                    ? "Все совпадают!" : "ЕСТЬ РАЗНИЦА!");
        }

        Bitmap MakeBase()
        {
            Bitmap bmp = new Bitmap(imgW, imgH);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                if (backgroundImage != null)
                {
                    g.DrawImage(backgroundImage, 0, 0, imgW, imgH);
                    using (SolidBrush br = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
                        g.FillRectangle(br, 0, 0, imgW, imgH);
                }
                else
                {
                    g.Clear(Color.FromArgb(15, 15, 25));
                }
            }
            return bmp;
        }

        void Annotate(Bitmap bmp, string title, Color color)
        {
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (Font f = new Font("Segoe UI", 11, FontStyle.Bold))
                using (SolidBrush br = new SolidBrush(color))
                    g.DrawString(title, f, br, 10, 8);

                using (Font f = new Font("Consolas", 9))
                {
                    g.DrawString("-- Стороны", f, Brushes.White, 10, imgH - 45);
                    g.DrawString("-- Биссектрисы", f, Brushes.Yellow, 10, imgH - 25);
                }

                Font fv = new Font("Segoe UI", 9, FontStyle.Bold);
                DrawDot(g, vertA, "A", Color.FromArgb(255, 80, 80), fv);
                DrawDot(g, vertB, "B", Color.FromArgb(80, 255, 80), fv);
                DrawDot(g, vertC, "C", Color.FromArgb(80, 180, 255), fv);

                // Инцентр
                PointF dA = TriangleGeometry.BisectorFoot(vertA, vertB, vertC);
                PointF dB = TriangleGeometry.BisectorFoot(vertB, vertA, vertC);
                PointF ic = TriangleGeometry.LineIntersect(vertA, dA, vertB, dB);
                if (!float.IsNaN(ic.X))
                {
                    using (SolidBrush br = new SolidBrush(Color.FromArgb(255, 150, 50)))
                    {
                        g.FillEllipse(br, ic.X - 4, ic.Y - 4, 8, 8);
                        g.DrawString("I", fv, br, ic.X + 6, ic.Y - 8);
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
                g.DrawString(lbl + "(" + pt.X.ToString("F0") + "," +
                    pt.Y.ToString("F0") + ")", f, br, pt.X + 6, pt.Y - 14);
            }
        }

        Bitmap MakeComparison(List<LineSegment> sides, List<LineSegment> bisectors)
        {
            Bitmap bmp = new Bitmap(imgW, imgH);
            using (Graphics g = Graphics.FromImage(bmp))
                g.Clear(Color.FromArgb(10, 10, 15));

            HashSet<Point> all = new HashSet<Point>(lastSetDDA);
            all.UnionWith(lastSetBres);
            all.UnionWith(lastSetBresI);

            int diff = 0;
            foreach (Point p in all)
            {
                if (p.X < 0 || p.X >= imgW || p.Y < 0 || p.Y >= imgH) continue;

                bool d = lastSetDDA.Contains(p);
                bool b = lastSetBres.Contains(p);
                bool bi = lastSetBresI.Contains(p);

                Color c;
                if (d && b && bi)
                {
                    c = Color.FromArgb(100, 100, 100);
                }
                else
                {
                    diff++;
                    int cr = 0, cg = 0, cb = 0;
                    if (d) { cb = 255; }
                    if (b) { cr = 255; }
                    if (bi) { cg = 255; }
                    c = Color.FromArgb(Math.Min(255, cr), Math.Min(255, cg), Math.Min(255, cb));
                }
                bmp.SetPixel(p.X, p.Y, c);
            }

            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Font fb = new Font("Segoe UI", 11, FontStyle.Bold);
                Font fs = new Font("Consolas", 9);

                g.DrawString("СРАВНЕНИЕ — приблизьте колёсиком!", fb, Brushes.White, 10, 8);

                int yy = 32;
                g.DrawString("Серый = все совпадают", fs, Brushes.Gray, 10, yy); yy += 16;
                g.DrawString("Синий = только ЦДА", fs,
                    new SolidBrush(Color.FromArgb(80, 80, 255)), 10, yy); yy += 16;
                g.DrawString("Красный = только Брезенхем", fs,
                    new SolidBrush(Color.FromArgb(255, 80, 80)), 10, yy); yy += 16;
                g.DrawString("Зелёный = только Целочисл.", fs,
                    new SolidBrush(Color.FromArgb(80, 255, 80)), 10, yy);

                string dt = diff > 0
                    ? "Различий: " + diff + " пикселей — приблизьте чтобы увидеть!"
                    : "Все алгоритмы совпали!";
                Color dc = diff > 0 ? Color.OrangeRed : Color.LightGreen;
                g.DrawString(dt, fb, new SolidBrush(dc), 10, imgH - 30);

                DrawDot(g, vertA, "A", Color.FromArgb(255, 80, 80), fs);
                DrawDot(g, vertB, "B", Color.FromArgb(80, 255, 80), fs);
                DrawDot(g, vertC, "C", Color.FromArgb(80, 180, 255), fs);

                fb.Dispose();
                fs.Dispose();
            }

            return bmp;
        }
        /// Информация о пикселе — какие алгоритмы его содержат.
        /// Показывается при наведении мыши.
        string GetPixelAlgoInfo(int px, int py, int tabIdx)
        {
            if (lastSetDDA == null) return "";

            Point p = new Point(px, py);
            bool d = lastSetDDA.Contains(p);
            bool b = lastSetBres.Contains(p);
            bool bi = lastSetBresI.Contains(p);

            if (!d && !b && !bi) return "[фон]";

            string s = "[";
            if (d) s += "ЦДА ";
            if (b) s += "Брез ";
            if (bi) s += "Цел ";
            s += "]";

            if (d && b && bi) s += " = ВСЕ";
            else s += " РАЗНИЦА!";

            return s;
        }

        HashSet<Point> Collect(List<LineSegment> sides, List<LineSegment> bisectors,
            Func<int, int, int, int, List<Point>> algo)
        {
            HashSet<Point> set = new HashSet<Point>();
            List<LineSegment> all = new List<LineSegment>();
            all.AddRange(sides);
            all.AddRange(bisectors);
            foreach (LineSegment ln in all)
            {
                List<Point> pts = algo(
                    (int)Math.Round(ln.A.X), (int)Math.Round(ln.A.Y),
                    (int)Math.Round(ln.B.X), (int)Math.Round(ln.B.Y));
                foreach (Point p in pts) set.Add(p);
            }
            return set;
        }

        //Кнопки 

        void ResetVerts()
        {
            nudAX.Value = 300; nudAY.Value = 50;
            nudBX.Value = 100; nudBY.Value = 400;
            nudCX.Value = 500; nudCY.Value = 350;
        }

        void RandomVerts()
        {
            Random rnd = new Random();
            int m = 50;
            nudAX.Value = rnd.Next(m, imgW - m);
            nudAY.Value = rnd.Next(m, imgH - m);
            nudBX.Value = rnd.Next(m, imgW - m);
            nudBY.Value = rnd.Next(m, imgH - m);
            nudCX.Value = rnd.Next(m, imgW - m);
            nudCY.Value = rnd.Next(m, imgH - m);
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
                        nudAX.Maximum = nudBX.Maximum = nudCX.Maximum = imgW;
                        nudAY.Maximum = nudBY.Maximum = nudCY.Maximum = imgH;
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
                    string[] n = { "1_DDA", "2_Bres", "3_BresInt", "4_BuiltIn", "5_Compare" };
                    int c = 0;
                    for (int i = 0; i < 5; i++)
                    {
                        Bitmap img = zoomPanels[i].GetImage();
                        if (img != null)
                        {
                            img.Save(Path.Combine(f, n[i] + "_" + t + ".png"), ImageFormat.Png);
                            c++;
                        }
                    }
                    MessageBox.Show("Сохранено " + c + " файлов в:\n" + f);
                }
            }
        }

        void SaveCurrent()
        {
            int idx = tabControl.SelectedIndex;
            Bitmap img = zoomPanels[idx].GetImage();
            if (img == null) { MessageBox.Show("Нет изображения."); return; }

            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Filter = "PNG|*.png|JPEG|*.jpg|BMP|*.bmp";
                dlg.FileName = "triangle_" + (idx + 1);
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
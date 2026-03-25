using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AffineTransforms
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

    //  АЛГОРИТМЫ ПРЕОБРАЗОВАНИЙ
    public static class TransformAlgorithms
    {
        //  1. ПЕРЕНОС (Translation)
        //  Матрица:                     Обратная:
        //  | 1  0  tx |                 | 1  0  -tx |
        //  | 0  1  ty |                 | 0  1  -ty |
        //  | 0  0  1  |                 | 0  0   1  |
        //  x' = x + tx
        //  y' = y + ty
        public static Bitmap Translate(Bitmap src, int tx, int ty, Color bgColor)
        {
            int w = src.Width;
            int h = src.Height;
            Bitmap dst = new Bitmap(w, h, PixelFormat.Format32bppArgb);

            byte[] srcPx = GetPixels(src);
            byte[] dstPx = new byte[srcPx.Length];

            // Заполняем фоном
            FillBackground(dstPx, w, h, bgColor);

            int stride = w * 4;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    // Прямое преобразование: (x,y) → (x+tx, y+ty)
                    int nx = x + tx;
                    int ny = y + ty;

                    if (nx >= 0 && nx < w && ny >= 0 && ny < h)
                    {
                        int srcIdx = (y * stride) + (x * 4);
                        int dstIdx = (ny * stride) + (nx * 4);

                        dstPx[dstIdx] = srcPx[srcIdx];         // B
                        dstPx[dstIdx + 1] = srcPx[srcIdx + 1]; // G
                        dstPx[dstIdx + 2] = srcPx[srcIdx + 2]; // R
                        dstPx[dstIdx + 3] = srcPx[srcIdx + 3]; // A
                    }
                }
            }

            SetPixels(dst, dstPx);
            return dst;
        }

        /// Обратный перенос: (tx, ty) → (-tx, -ty)
        public static Bitmap TranslateInverse(Bitmap src, int tx, int ty, Color bgColor)
        {
            return Translate(src, -tx, -ty, bgColor);
        }

        //  2. ОТРАЖЕНИЕ (Reflection)
        //  По горизонтали (относительно вертикальной оси):
        //  | -1  0  w-1 |     Обратная = та же самая
        //  |  0  1   0  |     (отражение = самообратное)
        //  |  0  0   1  |
        //  x' = (w-1) - x,  y' = y
        //  По вертикали (относительно горизонтальной оси):
        //  | 1   0   0  |
        //  | 0  -1  h-1 |
        //  | 0   0   1  |
        //  x' = x,  y' = (h-1) - y
        //  Оба:
        //  x' = (w-1) - x,  y' = (h-1) - y
        public static Bitmap Reflect(Bitmap src, bool horizontal, bool vertical)
        {
            int w = src.Width;
            int h = src.Height;
            Bitmap dst = new Bitmap(w, h, PixelFormat.Format32bppArgb);

            byte[] srcPx = GetPixels(src);
            byte[] dstPx = new byte[srcPx.Length];

            int stride = w * 4;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int nx = horizontal ? (w - 1 - x) : x;
                    int ny = vertical ? (h - 1 - y) : y;

                    int srcIdx = (y * stride) + (x * 4);
                    int dstIdx = (ny * stride) + (nx * 4);

                    dstPx[dstIdx] = srcPx[srcIdx];
                    dstPx[dstIdx + 1] = srcPx[srcIdx + 1];
                    dstPx[dstIdx + 2] = srcPx[srcIdx + 2];
                    dstPx[dstIdx + 3] = srcPx[srcIdx + 3];
                }
            }

            SetPixels(dst, dstPx);
            return dst;
        }

        /// Обратное отражение = то же самое отражение (самообратное преобразование)
        public static Bitmap ReflectInverse(Bitmap src, bool horizontal, bool vertical)
        {
            return Reflect(src, horizontal, vertical);
        }

        //  3. КОМБИНАЦИЯ: Перенос + Отражение
        //  Сначала применяем перенос, затем отражение.
        //  Обратное: сначала обратное отражение, затем обратный перенос.
        public static Bitmap TranslateAndReflect(Bitmap src, int tx, int ty,
            bool horizontal, bool vertical, Color bgColor)
        {
            Bitmap translated = Translate(src, tx, ty, bgColor);
            Bitmap result = Reflect(translated, horizontal, vertical);
            translated.Dispose();
            return result;
        }

        public static Bitmap TranslateAndReflectInverse(Bitmap src, int tx, int ty,
            bool horizontal, bool vertical, Color bgColor)
        {
            // Обратный порядок: сначала обратное отражение, потом обратный перенос
            Bitmap reflected = ReflectInverse(src, horizontal, vertical);
            Bitmap result = TranslateInverse(reflected, tx, ty, bgColor);
            reflected.Dispose();
            return result;
        }

        //  4. НЕЛИНЕЙНОЕ ПРЕОБРАЗОВАНИЕ
        //
        //  Функция: i = x² / (1 + x),  j = y
        //
        //  Где x, y — нормализованные координаты [0..1]
        //     (x = pixel_x / width, y = pixel_y / height)
        //
        //  Для обратного преобразования нужно решить:
        //     i = x²/(1+x) относительно x
        //     x²/(1+x) = i  ->  x² = i(1+x)  ->  x² - ix - i = 0
        //     x = (i + sqrt(i² + 4i)) / 2   (берём положительный корень)

        /// Прямое нелинейное преобразование:
        ///   i = x² / (1 + x),  j = y
        /// Используем обратное отображение (для каждого пикселя результата находим соответствующий пиксель источника).
        public static Bitmap NonlinearTransform(Bitmap src, Color bgColor)
        {
            int w = src.Width;
            int h = src.Height;
            Bitmap dst = new Bitmap(w, h, PixelFormat.Format32bppArgb);

            byte[] srcPx = GetPixels(src);
            byte[] dstPx = new byte[srcPx.Length];
            FillBackground(dstPx, w, h, bgColor);

            int stride = w * 4;

            // Для каждого пикселя (di, dj) результата находим исходный (sx, sy)
            // i = x²/(1+x), j = y
            // Обратное: x = (i + sqrt(i²+4i)) / 2, y = j
            for (int dj = 0; dj < h; dj++)
            {
                for (int di = 0; di < w; di++)
                {
                    // Нормализованные координаты результата
                    double ni = (double)di / (w - 1);  // [0..1]
                    double nj = (double)dj / (h - 1);

                    // Обратное преобразование: из (i,j) находим (x,y)
                    // x²/(1+x) = i -> x² - ix - i = 0
                    // x = (i + sqrt(i² + 4i)) / 2
                    double disc = ni * ni + 4.0 * ni;
                    if (disc < 0) continue;

                    double nx = (ni + Math.Sqrt(disc)) / 2.0;
                    double ny = nj;  // j = y, поэтому y = j

                    // Пиксельные координаты источника
                    int sx = (int)Math.Round(nx * (w - 1));
                    int sy = (int)Math.Round(ny * (h - 1));

                    if (sx >= 0 && sx < w && sy >= 0 && sy < h)
                    {
                        int srcIdx = (sy * stride) + (sx * 4);
                        int dstIdx = (dj * stride) + (di * 4);

                        dstPx[dstIdx] = srcPx[srcIdx];
                        dstPx[dstIdx + 1] = srcPx[srcIdx + 1];
                        dstPx[dstIdx + 2] = srcPx[srcIdx + 2];
                        dstPx[dstIdx + 3] = srcPx[srcIdx + 3];
                    }
                }
            }

            SetPixels(dst, dstPx);
            return dst;
        }

        /// Обратное нелинейное преобразование:
        /// Прямое: i = x²/(1+x), j = y
        /// Обратное: для каждого (dx,dy) результата вычисляем,
        ///   откуда он пришёл в преобразованном изображении.
        ///   Т.е. применяем прямую формулу: i = x²/(1+x) к нормализованному x.
        public static Bitmap NonlinearInverse(Bitmap src, Color bgColor)
        {
            int w = src.Width;
            int h = src.Height;
            Bitmap dst = new Bitmap(w, h, PixelFormat.Format32bppArgb);

            byte[] srcPx = GetPixels(src);
            byte[] dstPx = new byte[srcPx.Length];
            FillBackground(dstPx, w, h, bgColor);

            int stride = w * 4;

            for (int dy = 0; dy < h; dy++)
            {
                for (int dx = 0; dx < w; dx++)
                {
                    // Нормализованные координаты (это x,y исходного изображения)
                    double nx = (double)dx / (w - 1);
                    double ny = (double)dy / (h - 1);

                    // Прямая формула: i = x²/(1+x), j = y
                    double ni = (nx * nx) / (1.0 + nx);
                    double nj = ny;

                    // Пиксельные координаты в преобразованном изображении
                    int si = (int)Math.Round(ni * (w - 1));
                    int sj = (int)Math.Round(nj * (h - 1));

                    if (si >= 0 && si < w && sj >= 0 && sj < h)
                    {
                        int srcIdx = (sj * stride) + (si * 4);
                        int dstIdx = (dy * stride) + (dx * 4);

                        dstPx[dstIdx] = srcPx[srcIdx];
                        dstPx[dstIdx + 1] = srcPx[srcIdx + 1];
                        dstPx[dstIdx + 2] = srcPx[srcIdx + 2];
                        dstPx[dstIdx + 3] = srcPx[srcIdx + 3];
                    }
                }
            }

            SetPixels(dst, dstPx);
            return dst;
        }

        /// Нелинейное с билинейной интерполяцией (для лучшего качества)
        public static Bitmap NonlinearTransformBilinear(Bitmap src, Color bgColor)
        {
            int w = src.Width;
            int h = src.Height;
            Bitmap dst = new Bitmap(w, h, PixelFormat.Format32bppArgb);

            byte[] srcPx = GetPixels(src);
            byte[] dstPx = new byte[srcPx.Length];
            FillBackground(dstPx, w, h, bgColor);

            int stride = w * 4;

            for (int dj = 0; dj < h; dj++)
            {
                for (int di = 0; di < w; di++)
                {
                    double ni = (double)di / (w - 1);
                    double nj = (double)dj / (h - 1);

                    double disc = ni * ni + 4.0 * ni;
                    if (disc < 0) continue;

                    double nx = (ni + Math.Sqrt(disc)) / 2.0;
                    double ny = nj;

                    // Билинейная интерполяция
                    double srcXf = nx * (w - 1);
                    double srcYf = ny * (h - 1);

                    int x0 = (int)Math.Floor(srcXf);
                    int y0 = (int)Math.Floor(srcYf);
                    int x1 = Math.Min(x0 + 1, w - 1);
                    int y1 = Math.Min(y0 + 1, h - 1);

                    if (x0 < 0 || x0 >= w || y0 < 0 || y0 >= h) continue;

                    double fx = srcXf - x0;
                    double fy = srcYf - y0;

                    int dstIdx = (dj * stride) + (di * 4);

                    for (int ch = 0; ch < 4; ch++)
                    {
                        double v00 = srcPx[y0 * stride + x0 * 4 + ch];
                        double v10 = srcPx[y0 * stride + x1 * 4 + ch];
                        double v01 = srcPx[y1 * stride + x0 * 4 + ch];
                        double v11 = srcPx[y1 * stride + x1 * 4 + ch];

                        double val = v00 * (1 - fx) * (1 - fy) +
                                     v10 * fx * (1 - fy) +
                                     v01 * (1 - fx) * fy +
                                     v11 * fx * fy;

                        dstPx[dstIdx + ch] = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(val)));
                    }
                }
            }

            SetPixels(dst, dstPx);
            return dst;
        }

        // Утилиты для работы с пикселями

        public static byte[] GetPixels(Bitmap bmp)
        {
            BitmapData data = bmp.LockBits(
                new Rectangle(0, 0, bmp.Width, bmp.Height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            int bytes = Math.Abs(data.Stride) * bmp.Height;
            byte[] pixels = new byte[bytes];
            Marshal.Copy(data.Scan0, pixels, 0, bytes);
            bmp.UnlockBits(data);
            return pixels;
        }

        public static void SetPixels(Bitmap bmp, byte[] pixels)
        {
            BitmapData data = bmp.LockBits(
                new Rectangle(0, 0, bmp.Width, bmp.Height),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
            bmp.UnlockBits(data);
        }

        public static void FillBackground(byte[] pixels, int w, int h, Color c)
        {
            int stride = w * 4;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int idx = y * stride + x * 4;
                    pixels[idx] = c.B;
                    pixels[idx + 1] = c.G;
                    pixels[idx + 2] = c.R;
                    pixels[idx + 3] = c.A;
                }
            }
        }
    }

    //  ГЛАВНАЯ ФОРМА
    public class MainForm : Form
    {
        Bitmap imgOriginal;
        TabControl tabs;
        PictureBox[] picBoxes = new PictureBox[8];
        Label lblStatus;

        // Параметры
        NumericUpDown nudTX, nudTY;
        CheckBox chkHoriz, chkVert;
        CheckBox chkBilinear;

        Color bgColor = Color.FromArgb(20, 20, 30);

        string[] tabNames = {
            "1. Оригинал",
            "2. Перенос",
            "3. Отражение",
            "4. Перенос+Отражение",
            "5. Обратное (восстановление)",
            "6. Нелинейное i=x²/(1+x)",
            "7. Обратное нелинейное",
            "8. График функции"
        };

        public MainForm()
        {
            Text = "Аффинные и нелинейные преобразования изображений";
            Size = new Size(1200, 800);
            MinimumSize = new Size(1000, 650);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(35, 35, 45);
            ForeColor = Color.White;

            BuildUI();
            CreateSampleImage();
        }

        void BuildUI()
        {
            // ЛЕВАЯ ПАНЕЛЬ
            Panel left = new Panel();
            left.Dock = DockStyle.Left;
            left.Width = 270;
            left.BackColor = Color.FromArgb(40, 40, 55);
            left.AutoScroll = true;
            Controls.Add(left);

            int y = 10;

            left.Controls.Add(ML("ЗАГРУЗКА", 10, y, Color.FromArgb(100, 200, 255), true));
            y += 25;
            left.Controls.Add(MB("Загрузить изображение", ref y,
                Color.FromArgb(60, 150, 220), delegate { LoadImage(); }));
            left.Controls.Add(MB("Тестовое изображение", ref y,
                Color.FromArgb(150, 100, 200), delegate { CreateSampleImage(); }));

            y += 5;
            left.Controls.Add(ML("ПЕРЕНОС (Translation)", 10, y,
                Color.FromArgb(255, 200, 100), true));
            y += 25;

            left.Controls.Add(ML("Сдвиг X (tx):", 10, y, Color.White, false));
            nudTX = MakeNud(left, 120, y, 50, -1000, 1000);
            y += 30;

            left.Controls.Add(ML("Сдвиг Y (ty):", 10, y, Color.White, false));
            nudTY = MakeNud(left, 120, y, 30, -1000, 1000);
            y += 35;

            left.Controls.Add(ML("ОТРАЖЕНИЕ (Reflection)", 10, y,
                Color.FromArgb(100, 255, 150), true));
            y += 25;

            chkHoriz = new CheckBox();
            chkHoriz.Text = "По горизонтали (x' = W-x)";
            chkHoriz.Location = new Point(15, y);
            chkHoriz.AutoSize = true;
            chkHoriz.ForeColor = Color.White;
            chkHoriz.Checked = true;
            chkHoriz.CheckedChanged += delegate { ApplyAll(); };
            left.Controls.Add(chkHoriz);
            y += 25;

            chkVert = new CheckBox();
            chkVert.Text = "По вертикали (y' = H-y)";
            chkVert.Location = new Point(15, y);
            chkVert.AutoSize = true;
            chkVert.ForeColor = Color.White;
            chkVert.Checked = false;
            chkVert.CheckedChanged += delegate { ApplyAll(); };
            left.Controls.Add(chkVert);
            y += 35;

            left.Controls.Add(ML("НЕЛИНЕЙНОЕ", 10, y,
                Color.FromArgb(255, 100, 100), true));
            y += 22;
            left.Controls.Add(ML("i = x² / (1 + x)\nj = y", 10, y,
                Color.FromArgb(255, 220, 150), false));
            y += 35;

            chkBilinear = new CheckBox();
            chkBilinear.Text = "Билинейная интерполяция";
            chkBilinear.Location = new Point(15, y);
            chkBilinear.AutoSize = true;
            chkBilinear.ForeColor = Color.White;
            chkBilinear.Checked = true;
            chkBilinear.CheckedChanged += delegate { ApplyAll(); };
            left.Controls.Add(chkBilinear);
            y += 35;

            left.Controls.Add(MB("Применить все", ref y,
                Color.FromArgb(50, 180, 80), delegate { ApplyAll(); }));

            y += 5;
            left.Controls.Add(ML("СОХРАНЕНИЕ", 10, y,
                Color.FromArgb(200, 200, 200), true));
            y += 22;
            left.Controls.Add(MB("Сохранить все", ref y,
                Color.FromArgb(60, 160, 120), delegate { SaveAll(); }));
            left.Controls.Add(MB("Сохранить текущее", ref y,
                Color.FromArgb(60, 140, 160), delegate { SaveCurrent(); }));

            y += 5;
            left.Controls.Add(ML("ТЕОРИЯ", 10, y,
                Color.FromArgb(180, 180, 200), true));
            y += 20;
            left.Controls.Add(ML(
                "Аффинные:\n" +
                " Перенос: x'=x+tx, y'=y+ty\n" +
                " Отраж:  x'=W-1-x (гориз)\n" +
                "         y'=H-1-y (верт)\n\n" +
                "Нелинейное:\n" +
                " i = x²/(1+x), j = y\n" +
                " Обратное:\n" +
                " x = (i+√(i²+4i))/2\n" +
                " y = j",
                10, y, Color.FromArgb(150, 150, 170), false));
            y += 160;

            lblStatus = new Label();
            lblStatus.Location = new Point(10, y);
            lblStatus.Size = new Size(245, 60);
            lblStatus.ForeColor = Color.LightGray;
            lblStatus.Font = new Font("Consolas", 8);
            lblStatus.Text = "";
            left.Controls.Add(lblStatus);

            // ВКЛАДКИ
            tabs = new TabControl();
            tabs.Dock = DockStyle.Fill;
            tabs.Font = new Font("Segoe UI", 9);
            Controls.Add(tabs);

            for (int i = 0; i < 8; i++)
            {
                TabPage tp = new TabPage(tabNames[i]);
                tp.BackColor = Color.FromArgb(20, 20, 30);

                PictureBox pb = new PictureBox();
                pb.Dock = DockStyle.Fill;
                pb.SizeMode = PictureBoxSizeMode.Zoom;
                pb.BackColor = Color.FromArgb(20, 20, 30);
                picBoxes[i] = pb;
                tp.Controls.Add(pb);
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
            b.Size = new Size(235, 28); b.FlatStyle = FlatStyle.Flat;
            b.BackColor = c; b.ForeColor = Color.White;
            b.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            b.Click += h; y += 33;
            return b;
        }

        NumericUpDown MakeNud(Panel parent, int x, int y, int val, int min, int max)
        {
            NumericUpDown n = new NumericUpDown();
            n.Location = new Point(x, y - 3);
            n.Width = 90; n.Minimum = min; n.Maximum = max; n.Value = val;
            n.BackColor = Color.FromArgb(55, 55, 70);
            n.ForeColor = Color.White;
            n.ValueChanged += delegate { ApplyAll(); };
            parent.Controls.Add(n);
            return n;
        }

        //  ТЕСТОВОЕ ИЗОБРАЖЕНИЕ
        void CreateSampleImage()
        {
            int w = 500, h = 400;
            Bitmap bmp = new Bitmap(w, h);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;

                // Градиентный фон
                using (LinearGradientBrush lgb = new LinearGradientBrush(
                    new Point(0, 0), new Point(w, h),
                    Color.FromArgb(30, 60, 120), Color.FromArgb(120, 60, 30)))
                    g.FillRectangle(lgb, 0, 0, w, h);

                // Сетка
                using (Pen gridPen = new Pen(Color.FromArgb(60, 255, 255, 255)))
                {
                    for (int x = 0; x < w; x += 50)
                        g.DrawLine(gridPen, x, 0, x, h);
                    for (int yy = 0; yy < h; yy += 50)
                        g.DrawLine(gridPen, 0, yy, w, yy);
                }

                // Фигуры
                g.FillEllipse(Brushes.Red, 50, 50, 100, 100);
                g.FillRectangle(Brushes.Blue, 200, 80, 120, 80);
                g.FillEllipse(Brushes.Green, 350, 50, 80, 120);

                // Треугольник
                Point[] tri = { new Point(100, 300), new Point(200, 200), new Point(250, 350) };
                g.FillPolygon(Brushes.Yellow, tri);

                // Текст
                using (Font f = new Font("Segoe UI", 16, FontStyle.Bold))
                    g.DrawString("ТЕСТ", f, Brushes.White, 320, 280);

                // Стрелка (для видимости отражения)
                using (Pen arrowPen = new Pen(Color.Cyan, 3))
                {
                    arrowPen.EndCap = LineCap.ArrowAnchor;
                    g.DrawLine(arrowPen, 50, 350, 200, 350);
                    g.DrawLine(arrowPen, 50, 350, 50, 250);
                }

                // Буква L (чтобы видеть отражение)
                using (Font f = new Font("Segoe UI", 40, FontStyle.Bold))
                    g.DrawString("L", f, Brushes.Magenta, 380, 200);
            }

            SetOriginal(bmp);
        }

        void SetOriginal(Bitmap bmp)
        {
            if (imgOriginal != null) imgOriginal.Dispose();
            imgOriginal = bmp;

            SetPic(0, new Bitmap(bmp));
            ApplyAll();
        }

        //  ПРИМЕНЕНИЕ ПРЕОБРАЗОВАНИЙ
        void ApplyAll()
        {
            if (imgOriginal == null) return;

            int tx = (int)nudTX.Value;
            int ty = (int)nudTY.Value;
            bool hRef = chkHoriz.Checked;
            bool vRef = chkVert.Checked;

            // 2. Перенос
            Bitmap translated = TransformAlgorithms.Translate(imgOriginal, tx, ty, bgColor);
            SetPic(1, translated);

            // 3. Отражение
            Bitmap reflected = TransformAlgorithms.Reflect(imgOriginal, hRef, vRef);
            SetPic(2, reflected);

            // 4. Перенос + Отражение
            Bitmap combined = TransformAlgorithms.TranslateAndReflect(
                imgOriginal, tx, ty, hRef, vRef, bgColor);
            SetPic(3, combined);

            // 5. Обратное (восстановление из комбинированного)
            Bitmap restored = TransformAlgorithms.TranslateAndReflectInverse(
                combined, tx, ty, hRef, vRef, bgColor);
            SetPic(4, restored);

            // 6. Нелинейное
            Bitmap nonlinear;
            if (chkBilinear.Checked)
                nonlinear = TransformAlgorithms.NonlinearTransformBilinear(imgOriginal, bgColor);
            else
                nonlinear = TransformAlgorithms.NonlinearTransform(imgOriginal, bgColor);
            SetPic(5, nonlinear);

            // 7. Обратное нелинейное
            Bitmap nlInverse = TransformAlgorithms.NonlinearInverse(nonlinear, bgColor);
            SetPic(6, nlInverse);

            // 8. График функции
            Bitmap graph = DrawFunctionGraph();
            SetPic(7, graph);

            // Статус
            lblStatus.Text =
                "Размер: " + imgOriginal.Width + "x" + imgOriginal.Height + "\n" +
                "tx=" + tx + " ty=" + ty + "\n" +
                "Отраж: " + (hRef ? "H" : "") + (vRef ? "V" : "") +
                (!hRef && !vRef ? "нет" : "");
        }

        /// Рисует график функции i = x²/(1+x)
        Bitmap DrawFunctionGraph()
        {
            int w = 600, h = 500;
            Bitmap bmp = new Bitmap(w, h);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.FromArgb(15, 15, 25));

                int margin = 60;
                int plotW = w - margin * 2;
                int plotH = h - margin * 2 - 30;

                // Заголовок
                using (Font f = new Font("Segoe UI", 12, FontStyle.Bold))
                    g.DrawString("Функция преобразования: i = x² / (1 + x),  j = y",
                        f, Brushes.White, margin, 10);

                // Оси
                using (Pen axisPen = new Pen(Color.Gray, 1))
                {
                    // X
                    g.DrawLine(axisPen,
                        margin, margin + 30 + plotH,
                        margin + plotW, margin + 30 + plotH);
                    // Y
                    g.DrawLine(axisPen,
                        margin, margin + 30,
                        margin, margin + 30 + plotH);
                }

                // Подписи осей
                using (Font f = new Font("Consolas", 9))
                {
                    g.DrawString("x", f, Brushes.LightGray,
                        margin + plotW + 5, margin + 30 + plotH - 10);
                    g.DrawString("i = f(x)", f, Brushes.LightGray,
                        margin + 5, margin + 15);
                    g.DrawString("0", f, Brushes.Gray,
                        margin - 15, margin + 30 + plotH + 2);
                    g.DrawString("1", f, Brushes.Gray,
                        margin + plotW - 5, margin + 30 + plotH + 2);
                    g.DrawString("1", f, Brushes.Gray,
                        margin - 15, margin + 28);
                }

                // Диагональ y = x (для сравнения)
                using (Pen diagPen = new Pen(Color.FromArgb(60, 255, 255, 255), 1))
                {
                    diagPen.DashStyle = DashStyle.Dash;
                    g.DrawLine(diagPen,
                        margin, margin + 30 + plotH,
                        margin + plotW, margin + 30);
                }

                // Функция: i = x²/(1+x)
                using (Pen funcPen = new Pen(Color.FromArgb(255, 100, 100), 2.5f))
                {
                    PointF prev = PointF.Empty;
                    bool first = true;

                    for (int px = 0; px <= plotW; px++)
                    {
                        double x = (double)px / plotW;  // [0..1]
                        double i = (x * x) / (1.0 + x);

                        float screenX = margin + px;
                        float screenY = margin + 30 + plotH - (float)(i * plotH);

                        if (!first)
                            g.DrawLine(funcPen, prev, new PointF(screenX, screenY));

                        prev = new PointF(screenX, screenY);
                        first = false;
                    }
                }

                // Обратная функция: x = (i + sqrt(i²+4i))/2
                using (Pen invPen = new Pen(Color.FromArgb(100, 200, 100), 2))
                {
                    invPen.DashStyle = DashStyle.Dot;
                    PointF prev = PointF.Empty;
                    bool first = true;

                    for (int px = 0; px <= plotW; px++)
                    {
                        double i = (double)px / plotW;
                        double disc = i * i + 4 * i;
                        if (disc < 0) continue;
                        double x = (i + Math.Sqrt(disc)) / 2.0;
                        if (x > 1.5) continue;

                        float screenX = margin + px;
                        float screenY = margin + 30 + plotH - (float)(x / 1.0 * plotH);
                        screenY = Math.Max(margin + 30, Math.Min(margin + 30 + plotH, screenY));

                        if (!first && Math.Abs(prev.Y - screenY) < plotH * 0.5f)
                            g.DrawLine(invPen, prev, new PointF(screenX, screenY));

                        prev = new PointF(screenX, screenY);
                        first = false;
                    }
                }

                // Легенда
                int ly = h - 25;
                using (Font f = new Font("Consolas", 9))
                {
                    g.DrawString("── i = x²/(1+x)", f,
                        new SolidBrush(Color.FromArgb(255, 100, 100)), margin, ly);
                    g.DrawString("·· x = (i+√(i²+4i))/2  (обратная)", f,
                        new SolidBrush(Color.FromArgb(100, 200, 100)), margin + 180, ly);
                    g.DrawString("-- y = x  (линейная)", f,
                        new SolidBrush(Color.FromArgb(80, 80, 80)), margin + 440, ly);
                }

                // Ключевые точки
                using (Font f = new Font("Consolas", 8))
                {
                    // f(0) = 0
                    g.FillEllipse(Brushes.White, margin - 3, margin + 30 + plotH - 3, 6, 6);

                    // f(0.5) = 0.25/1.5 = 0.167
                    double fHalf = 0.25 / 1.5;
                    float sx = margin + plotW * 0.5f;
                    float sy = margin + 30 + plotH - (float)(fHalf * plotH);
                    g.FillEllipse(Brushes.Yellow, sx - 3, sy - 3, 6, 6);
                    g.DrawString("(0.5, " + fHalf.ToString("F3") + ")", f,
                        Brushes.Yellow, sx + 5, sy - 5);

                    // f(1) = 1/2 = 0.5
                    sx = margin + plotW;
                    sy = margin + 30 + plotH - plotH * 0.5f;
                    g.FillEllipse(Brushes.Cyan, sx - 3, sy - 3, 6, 6);
                    g.DrawString("(1, 0.5)", f, Brushes.Cyan, sx - 60, sy - 15);
                }
            }
            return bmp;
        }

        void SetPic(int idx, Bitmap bmp)
        {
            if (picBoxes[idx].Image != null)
                picBoxes[idx].Image.Dispose();
            picBoxes[idx].Image = bmp;
        }

        //  ЗАГРУЗКА / СОХРАНЕНИЕ

        void LoadImage()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Filter = "Изображения|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tiff|Все|*.*";
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        Bitmap loaded;
                        using (Bitmap tmp = new Bitmap(dlg.FileName))
                            loaded = new Bitmap(tmp);
                        SetOriginal(loaded);
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
                    string ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string[] names = {
                        "1_original", "2_translate", "3_reflect",
                        "4_translate_reflect", "5_restored",
                        "6_nonlinear", "7_nonlinear_inverse", "8_graph"
                    };
                    int cnt = 0;
                    for (int i = 0; i < 8; i++)
                    {
                        if (picBoxes[i].Image != null)
                        {
                            picBoxes[i].Image.Save(
                                Path.Combine(f, names[i] + "_" + ts + ".png"),
                                ImageFormat.Png);
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
            if (idx < 0 || idx >= 8 || picBoxes[idx].Image == null)
            { MessageBox.Show("Нет изображения."); return; }

            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Filter = "PNG|*.png|JPEG|*.jpg|BMP|*.bmp";
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    ImageFormat fmt = ImageFormat.Png;
                    string ext = Path.GetExtension(dlg.FileName).ToLower();
                    if (ext == ".jpg") fmt = ImageFormat.Jpeg;
                    else if (ext == ".bmp") fmt = ImageFormat.Bmp;
                    picBoxes[idx].Image.Save(dlg.FileName, fmt);
                    MessageBox.Show("Сохранено: " + dlg.FileName);
                }
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            if (imgOriginal != null) imgOriginal.Dispose();
        }
    }
}
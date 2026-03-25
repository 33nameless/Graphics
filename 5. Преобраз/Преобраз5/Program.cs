using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ImageProcessing
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

    // ═══════════════════════════════════════════════
    //  АЛГОРИТМЫ ОБРАБОТКИ ИЗОБРАЖЕНИЙ
    // ═══════════════════════════════════════════════
    public static class ImageAlgorithms
    {
        /// <summary>
        /// Логарифмическое преобразование яркости.
        /// 
        /// Формула: s = c * log(1 + r)
        /// где r — исходная яркость [0..255],
        ///     c — коэффициент масштабирования = 255 / log(1 + maxVal),
        ///     s — результирующая яркость.
        /// 
        /// Цветовая модель: работаем в HSL (или HSV).
        /// Конвертируем RGB → HSL, логарифмируем компоненту L (яркость),
        /// конвертируем обратно HSL → RGB.
        /// Это позволяет изменить яркость, сохранив оттенок и насыщенность.
        /// 
        /// Эффект: расширяет тёмные тона, сжимает светлые.
        /// Полезно для изображений с большим динамическим диапазоном.
        /// </summary>
        public static Bitmap LogTransform(Bitmap source, float coefficient)
        {
            int w = source.Width;
            int h = source.Height;
            Bitmap result = new Bitmap(w, h, PixelFormat.Format32bppArgb);

            // Блокируем биты для быстрого доступа
            BitmapData srcData = source.LockBits(
                new Rectangle(0, 0, w, h),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            BitmapData dstData = result.LockBits(
                new Rectangle(0, 0, w, h),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

            int bytes = Math.Abs(srcData.Stride) * h;
            byte[] srcPixels = new byte[bytes];
            byte[] dstPixels = new byte[bytes];

            Marshal.Copy(srcData.Scan0, srcPixels, 0, bytes);

            // Коэффициент c: нормализация к диапазону [0..255]
            // c = 255 / log(1 + 255) — для стандартного случая
            // Пользователь может менять coefficient для усиления/ослабления
            double c = 255.0 / Math.Log(1.0 + 255.0 * coefficient);

            for (int i = 0; i < bytes; i += 4)
            {
                // Формат BGRA
                byte bVal = srcPixels[i];
                byte gVal = srcPixels[i + 1];
                byte rVal = srcPixels[i + 2];
                byte aVal = srcPixels[i + 3];

                // RGB → HSL
                double hue, sat, lum;
                RgbToHsl(rVal, gVal, bVal, out hue, out sat, out lum);

                // Логарифмирование яркости (L)
                // lum в диапазоне [0..1], переводим в [0..255] для формулы
                double lumScaled = lum * 255.0;
                double newLumScaled = c * Math.Log(1.0 + lumScaled);
                newLumScaled = Clamp(newLumScaled, 0, 255);
                double newLum = newLumScaled / 255.0;

                // HSL → RGB
                byte newR, newG, newB;
                HslToRgb(hue, sat, newLum, out newR, out newG, out newB);

                dstPixels[i] = newB;
                dstPixels[i + 1] = newG;
                dstPixels[i + 2] = newR;
                dstPixels[i + 3] = aVal;
            }

            Marshal.Copy(dstPixels, 0, dstData.Scan0, bytes);

            source.UnlockBits(srcData);
            result.UnlockBits(dstData);

            return result;
        }

        /// <summary>
        /// Наложение изображений в режиме "Перекрытие" (Overlay).
        /// 
        /// Формула для каждого канала:
        ///   если base <= 0.5:
        ///     result = 2 * base * blend
        ///   иначе:
        ///     result = 1 - 2*(1-base)*(1-blend)
        /// 
        /// где base — нижнее (базовое) изображение,
        ///     blend — верхнее (накладываемое) изображение.
        /// 
        /// Эффект: тёмные участки base становятся темнее (Multiply),
        ///         светлые — светлее (Screen).
        ///         Усиливает контраст.
        /// 
        /// Параметр opacity управляет степенью наложения [0..1].
        /// </summary>
        public static Bitmap OverlayBlend(Bitmap baseImg, Bitmap blendImg, float opacity)
        {
            int w = baseImg.Width;
            int h = baseImg.Height;
            Bitmap result = new Bitmap(w, h, PixelFormat.Format32bppArgb);

            // Масштабируем blend до размеров base
            Bitmap scaledBlend = new Bitmap(blendImg, w, h);

            BitmapData baseData = baseImg.LockBits(
                new Rectangle(0, 0, w, h),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData blendData = scaledBlend.LockBits(
                new Rectangle(0, 0, w, h),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData dstData = result.LockBits(
                new Rectangle(0, 0, w, h),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

            int bytes = Math.Abs(baseData.Stride) * h;
            byte[] basePx = new byte[bytes];
            byte[] blendPx = new byte[bytes];
            byte[] dstPx = new byte[bytes];

            Marshal.Copy(baseData.Scan0, basePx, 0, bytes);
            Marshal.Copy(blendData.Scan0, blendPx, 0, bytes);

            for (int i = 0; i < bytes; i += 4)
            {
                // Для каждого канала B, G, R
                for (int ch = 0; ch < 3; ch++)
                {
                    double b = basePx[i + ch] / 255.0;   // base [0..1]
                    double l = blendPx[i + ch] / 255.0;   // blend [0..1]

                    // Формула Overlay
                    double ov;
                    if (b <= 0.5)
                        ov = 2.0 * b * l;
                    else
                        ov = 1.0 - 2.0 * (1.0 - b) * (1.0 - l);

                    // Смешиваем с учётом прозрачности (opacity)
                    double final_val = b * (1.0 - opacity) + ov * opacity;
                    dstPx[i + ch] = (byte)Clamp(final_val * 255.0, 0, 255);
                }

                // Альфа-канал
                dstPx[i + 3] = basePx[i + 3];
            }

            Marshal.Copy(dstPx, 0, dstData.Scan0, bytes);

            baseImg.UnlockBits(baseData);
            scaledBlend.UnlockBits(blendData);
            result.UnlockBits(dstData);

            scaledBlend.Dispose();

            return result;
        }

        // ═══════════════════════════════════════════
        //  ЦВЕТОВЫЕ ПРЕОБРАЗОВАНИЯ RGB ↔ HSL
        // ═══════════════════════════════════════════

        /// <summary>
        /// RGB [0..255] → HSL [H:0..360, S:0..1, L:0..1]
        /// </summary>
        public static void RgbToHsl(byte r, byte g, byte b,
            out double h, out double s, out double l)
        {
            double rd = r / 255.0;
            double gd = g / 255.0;
            double bd = b / 255.0;

            double max = Math.Max(rd, Math.Max(gd, bd));
            double min = Math.Min(rd, Math.Min(gd, bd));
            double delta = max - min;

            // Яркость
            l = (max + min) / 2.0;

            if (delta < 0.00001)
            {
                h = 0;
                s = 0;
                return;
            }

            // Насыщенность
            if (l < 0.5)
                s = delta / (max + min);
            else
                s = delta / (2.0 - max - min);

            // Оттенок
            if (Math.Abs(max - rd) < 0.00001)
                h = (gd - bd) / delta + (gd < bd ? 6 : 0);
            else if (Math.Abs(max - gd) < 0.00001)
                h = (bd - rd) / delta + 2;
            else
                h = (rd - gd) / delta + 4;

            h *= 60;
        }

        /// <summary>
        /// HSL [H:0..360, S:0..1, L:0..1] → RGB [0..255]
        /// </summary>
        public static void HslToRgb(double h, double s, double l,
            out byte r, out byte g, out byte b)
        {
            if (s < 0.00001)
            {
                byte v = (byte)Clamp(l * 255, 0, 255);
                r = g = b = v;
                return;
            }

            double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            double p = 2 * l - q;
            double hNorm = h / 360.0;

            r = (byte)Clamp(HueToRgb(p, q, hNorm + 1.0 / 3.0) * 255, 0, 255);
            g = (byte)Clamp(HueToRgb(p, q, hNorm) * 255, 0, 255);
            b = (byte)Clamp(HueToRgb(p, q, hNorm - 1.0 / 3.0) * 255, 0, 255);
        }

        private static double HueToRgb(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6.0) return p + (q - p) * 6 * t;
            if (t < 1.0 / 2.0) return q;
            if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6;
            return p;
        }

        /// <summary>
        /// Ограничение значения в диапазоне
        /// </summary>
        public static double Clamp(double val, double min, double max)
        {
            if (val < min) return min;
            if (val > max) return max;
            return val;
        }

        /// <summary>
        /// Построение гистограммы яркости
        /// </summary>
        public static int[] BuildHistogram(Bitmap bmp)
        {
            int[] hist = new int[256];

            BitmapData data = bmp.LockBits(
                new Rectangle(0, 0, bmp.Width, bmp.Height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            int bytes = Math.Abs(data.Stride) * bmp.Height;
            byte[] pixels = new byte[bytes];
            Marshal.Copy(data.Scan0, pixels, 0, bytes);

            for (int i = 0; i < bytes; i += 4)
            {
                // Яркость по формуле: Y = 0.299R + 0.587G + 0.114B
                byte bVal = pixels[i];
                byte gVal = pixels[i + 1];
                byte rVal = pixels[i + 2];
                int y = (int)(0.299 * rVal + 0.587 * gVal + 0.114 * bVal);
                y = Math.Max(0, Math.Min(255, y));
                hist[y]++;
            }

            bmp.UnlockBits(data);
            return hist;
        }

        /// <summary>
        /// Рисование гистограммы
        /// </summary>
        public static Bitmap DrawHistogram(int[] hist, int width, int height,
            Color barColor, Color bgColor, string title)
        {
            Bitmap bmp = new Bitmap(width, height);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(bgColor);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                int margin = 40;
                int plotW = width - margin * 2;
                int plotH = height - margin * 2 - 20;

                // Максимальное значение для нормализации
                int maxVal = 0;
                for (int i = 0; i < 256; i++)
                    if (hist[i] > maxVal) maxVal = hist[i];

                if (maxVal == 0) maxVal = 1;

                // Столбцы
                float barW = (float)plotW / 256;
                using (SolidBrush brush = new SolidBrush(barColor))
                {
                    for (int i = 0; i < 256; i++)
                    {
                        float barH = (float)hist[i] / maxVal * plotH;
                        float x = margin + i * barW;
                        float y = margin + 20 + plotH - barH;
                        g.FillRectangle(brush, x, y, Math.Max(barW - 0.5f, 0.5f), barH);
                    }
                }

                // Оси
                using (Pen axisPen = new Pen(Color.Gray))
                {
                    g.DrawLine(axisPen, margin, margin + 20 + plotH,
                        margin + plotW, margin + 20 + plotH);
                    g.DrawLine(axisPen, margin, margin + 20,
                        margin, margin + 20 + plotH);
                }

                // Подписи
                using (Font f = new Font("Consolas", 8))
                {
                    g.DrawString("0", f, Brushes.Gray, margin - 5, margin + 20 + plotH + 2);
                    g.DrawString("255", f, Brushes.Gray,
                        margin + plotW - 15, margin + 20 + plotH + 2);
                }

                using (Font f = new Font("Segoe UI", 10, FontStyle.Bold))
                    g.DrawString(title, f, new SolidBrush(barColor), margin, 5);
            }
            return bmp;
        }
    }

    // ═══════════════════════════════════════════════
    //  ГЛАВНАЯ ФОРМА
    // ═══════════════════════════════════════════════
    public class MainForm : Form
    {
        // Изображения
        Bitmap imgOriginal = null;
        Bitmap imgTransformed = null;
        Bitmap imgBlend = null;
        Bitmap imgOverlay = null;

        // Элементы управления
        TabControl tabs;
        PictureBox pbOriginal, pbTransformed, pbBlend, pbOverlay;
        PictureBox pbHistOriginal, pbHistTransformed;
        TrackBar sliderCoeff, sliderOpacity;
        Label lblCoeff, lblOpacity, lblStatus;

        float logCoefficient = 1.0f;
        float overlayOpacity = 0.7f;

        public MainForm()
        {
            Text = "Логарифмирование яркости + Наложение перекрытием (Overlay)";
            Size = new Size(1200, 800);
            MinimumSize = new Size(1000, 650);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(35, 35, 45);
            ForeColor = Color.White;

            BuildUI();
            CreateSampleImages();
        }

        void BuildUI()
        {
            // ═══ ЛЕВАЯ ПАНЕЛЬ ═══
            Panel left = new Panel();
            left.Dock = DockStyle.Left;
            left.Width = 280;
            left.BackColor = Color.FromArgb(40, 40, 55);
            left.AutoScroll = true;
            Controls.Add(left);

            int y = 10;

            left.Controls.Add(ML("ЗАГРУЗКА ИЗОБРАЖЕНИЙ", 10, y,
                Color.FromArgb(100, 200, 255), true));
            y += 28;

            left.Controls.Add(MB("Загрузить основное", ref y,
                Color.FromArgb(60, 150, 220), delegate { LoadMain(); }));
            left.Controls.Add(MB("Загрузить для наложения", ref y,
                Color.FromArgb(60, 180, 150), delegate { LoadBlend(); }));
            left.Controls.Add(MB("Тестовые изображения", ref y,
                Color.FromArgb(150, 100, 200), delegate { CreateSampleImages(); }));

            y += 10;
            left.Controls.Add(ML("ЛОГАРИФМИРОВАНИЕ", 10, y,
                Color.FromArgb(255, 200, 100), true));
            y += 25;

            left.Controls.Add(ML("s = c * log(1 + r)", 10, y,
                Color.FromArgb(200, 200, 200), false));
            y += 20;

            left.Controls.Add(ML("Коэффициент (c):", 10, y,
                Color.White, false));
            y += 20;

            sliderCoeff = new TrackBar();
            sliderCoeff.Location = new Point(10, y);
            sliderCoeff.Width = 250;
            sliderCoeff.Minimum = 1;
            sliderCoeff.Maximum = 300;
            sliderCoeff.Value = 100;
            sliderCoeff.TickFrequency = 25;
            sliderCoeff.ValueChanged += delegate
            {
                logCoefficient = sliderCoeff.Value / 100f;
                lblCoeff.Text = "c = " + logCoefficient.ToString("F2");
                ApplyTransform();
            };
            left.Controls.Add(sliderCoeff);
            y += 50;

            lblCoeff = new Label();
            lblCoeff.Text = "c = 1.00";
            lblCoeff.Location = new Point(10, y);
            lblCoeff.AutoSize = true;
            lblCoeff.ForeColor = Color.FromArgb(255, 220, 100);
            lblCoeff.Font = new Font("Consolas", 10, FontStyle.Bold);
            left.Controls.Add(lblCoeff);
            y += 30;

            // Описание
            left.Controls.Add(ML(
                "c < 1: слабый эффект\n" +
                "c = 1: стандартный\n" +
                "c > 1: сильный эффект\n\n" +
                "Расширяет тёмные тона,\n" +
                "сжимает светлые.",
                10, y, Color.FromArgb(160, 160, 180), false));
            y += 95;

            left.Controls.Add(ML("НАЛОЖЕНИЕ (OVERLAY)", 10, y,
                Color.FromArgb(100, 255, 150), true));
            y += 25;

            left.Controls.Add(ML(
                "base≤0.5: 2·base·blend\n" +
                "base>0.5: 1-2(1-b)(1-l)",
                10, y, Color.FromArgb(200, 200, 200), false));
            y += 35;

            left.Controls.Add(ML("Непрозрачность:", 10, y,
                Color.White, false));
            y += 20;

            sliderOpacity = new TrackBar();
            sliderOpacity.Location = new Point(10, y);
            sliderOpacity.Width = 250;
            sliderOpacity.Minimum = 0;
            sliderOpacity.Maximum = 100;
            sliderOpacity.Value = 70;
            sliderOpacity.TickFrequency = 10;
            sliderOpacity.ValueChanged += delegate
            {
                overlayOpacity = sliderOpacity.Value / 100f;
                lblOpacity.Text = "Opacity = " + (overlayOpacity * 100).ToString("F0") + "%";
                ApplyOverlay();
            };
            left.Controls.Add(sliderOpacity);
            y += 50;

            lblOpacity = new Label();
            lblOpacity.Text = "Opacity = 70%";
            lblOpacity.Location = new Point(10, y);
            lblOpacity.AutoSize = true;
            lblOpacity.ForeColor = Color.FromArgb(100, 255, 150);
            lblOpacity.Font = new Font("Consolas", 10, FontStyle.Bold);
            left.Controls.Add(lblOpacity);
            y += 35;

            left.Controls.Add(ML("СОХРАНЕНИЕ", 10, y,
                Color.FromArgb(200, 200, 200), true));
            y += 22;

            left.Controls.Add(MB("Сохранить все", ref y,
                Color.FromArgb(60, 160, 120), delegate { SaveAll(); }));
            left.Controls.Add(MB("Сохранить текущее", ref y,
                Color.FromArgb(60, 140, 160), delegate { SaveCurrent(); }));

            y += 10;
            lblStatus = new Label();
            lblStatus.Location = new Point(10, y);
            lblStatus.Size = new Size(250, 60);
            lblStatus.ForeColor = Color.LightGray;
            lblStatus.Font = new Font("Consolas", 8);
            lblStatus.Text = "Готово";
            left.Controls.Add(lblStatus);

            // ═══ ВКЛАДКИ ═══
            tabs = new TabControl();
            tabs.Dock = DockStyle.Fill;
            tabs.Font = new Font("Segoe UI", 9);
            Controls.Add(tabs);

            // Вкладка 1: Оригинал
            TabPage tp1 = new TabPage("1. Оригинал");
            tp1.BackColor = Color.FromArgb(20, 20, 30);
            Panel p1 = new Panel();
            p1.Dock = DockStyle.Fill;

            pbOriginal = new PictureBox();
            pbOriginal.Dock = DockStyle.Fill;
            pbOriginal.SizeMode = PictureBoxSizeMode.Zoom;
            pbOriginal.BackColor = Color.FromArgb(20, 20, 30);
            p1.Controls.Add(pbOriginal);

            tp1.Controls.Add(p1);
            tabs.TabPages.Add(tp1);

            // Вкладка 2: Логарифмирование
            TabPage tp2 = new TabPage("2. Логарифмирование яркости");
            tp2.BackColor = Color.FromArgb(20, 20, 30);
            pbTransformed = new PictureBox();
            pbTransformed.Dock = DockStyle.Fill;
            pbTransformed.SizeMode = PictureBoxSizeMode.Zoom;
            pbTransformed.BackColor = Color.FromArgb(20, 20, 30);
            tp2.Controls.Add(pbTransformed);
            tabs.TabPages.Add(tp2);

            // Вкладка 3: Гистограммы
            TabPage tp3 = new TabPage("3. Гистограммы (до / после)");
            tp3.BackColor = Color.FromArgb(20, 20, 30);

            SplitContainer splitHist = new SplitContainer();
            splitHist.Dock = DockStyle.Fill;
            splitHist.Orientation = Orientation.Horizontal;
            splitHist.SplitterDistance = 250;
            splitHist.BackColor = Color.FromArgb(20, 20, 30);

            pbHistOriginal = new PictureBox();
            pbHistOriginal.Dock = DockStyle.Fill;
            pbHistOriginal.SizeMode = PictureBoxSizeMode.Zoom;
            pbHistOriginal.BackColor = Color.FromArgb(20, 20, 30);
            splitHist.Panel1.Controls.Add(pbHistOriginal);

            pbHistTransformed = new PictureBox();
            pbHistTransformed.Dock = DockStyle.Fill;
            pbHistTransformed.SizeMode = PictureBoxSizeMode.Zoom;
            pbHistTransformed.BackColor = Color.FromArgb(20, 20, 30);
            splitHist.Panel2.Controls.Add(pbHistTransformed);

            tp3.Controls.Add(splitHist);
            tabs.TabPages.Add(tp3);

            // Вкладка 4: Изображение для наложения
            TabPage tp4 = new TabPage("4. Изображение для наложения");
            tp4.BackColor = Color.FromArgb(20, 20, 30);
            pbBlend = new PictureBox();
            pbBlend.Dock = DockStyle.Fill;
            pbBlend.SizeMode = PictureBoxSizeMode.Zoom;
            pbBlend.BackColor = Color.FromArgb(20, 20, 30);
            tp4.Controls.Add(pbBlend);
            tabs.TabPages.Add(tp4);

            // Вкладка 5: Результат наложения
            TabPage tp5 = new TabPage("5. Результат наложения (Overlay)");
            tp5.BackColor = Color.FromArgb(20, 20, 30);
            pbOverlay = new PictureBox();
            pbOverlay.Dock = DockStyle.Fill;
            pbOverlay.SizeMode = PictureBoxSizeMode.Zoom;
            pbOverlay.BackColor = Color.FromArgb(20, 20, 30);
            tp5.Controls.Add(pbOverlay);
            tabs.TabPages.Add(tp5);

            Controls.SetChildIndex(tabs, 0);
            Controls.SetChildIndex(left, 1);
        }

        Label ML(string text, int x, int y, Color c, bool bold)
        {
            Label l = new Label();
            l.Text = text;
            l.Location = new Point(x, y);
            l.AutoSize = true;
            l.ForeColor = c;
            l.Font = new Font("Segoe UI", 9, bold ? FontStyle.Bold : FontStyle.Regular);
            return l;
        }

        Button MB(string text, ref int y, Color c, EventHandler h)
        {
            Button b = new Button();
            b.Text = text;
            b.Location = new Point(15, y);
            b.Size = new Size(245, 30);
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = c;
            b.ForeColor = Color.White;
            b.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            b.Click += h;
            y += 35;
            return b;
        }

        // ═══════════════════════════════════════════
        //  СОЗДАНИЕ ТЕСТОВЫХ ИЗОБРАЖЕНИЙ
        // ═══════════════════════════════════════════

        /// <summary>
        /// Генерирует тестовые изображения если пользователь
        /// не загрузил свои файлы.
        /// </summary>
        void CreateSampleImages()
        {
            int w = 600, h = 450;

            // Основное: градиент с цветными кругами (имитация тёмной сцены)
            Bitmap main = new Bitmap(w, h);
            using (Graphics g = Graphics.FromImage(main))
            {
                // Тёмный градиент
                using (System.Drawing.Drawing2D.LinearGradientBrush lgb =
                    new System.Drawing.Drawing2D.LinearGradientBrush(
                        new Point(0, 0), new Point(w, h),
                        Color.FromArgb(5, 5, 15),
                        Color.FromArgb(60, 50, 40)))
                {
                    g.FillRectangle(lgb, 0, 0, w, h);
                }

                // Цветные круги с разной яркостью
                Random rnd = new Random(42);
                for (int i = 0; i < 20; i++)
                {
                    int x = rnd.Next(50, w - 50);
                    int yy = rnd.Next(50, h - 50);
                    int r = rnd.Next(20, 80);
                    int bright = rnd.Next(10, 120); // Преимущественно тёмные
                    Color col = Color.FromArgb(
                        bright + rnd.Next(0, 50),
                        bright + rnd.Next(0, 30),
                        bright);

                    using (System.Drawing.Drawing2D.PathGradientBrush pgb =
                        CreateRadialBrush(x, yy, r, col))
                    {
                        g.FillEllipse(pgb, x - r, yy - r, r * 2, r * 2);
                    }
                }

                // Текст
                using (Font f = new Font("Segoe UI", 14, FontStyle.Bold))
                    g.DrawString("Тёмное изображение (для логарифмирования)",
                        f, new SolidBrush(Color.FromArgb(40, 40, 50)), 10, 10);
            }

            // Второе изображение для наложения: яркий паттерн
            Bitmap blend = new Bitmap(w, h);
            using (Graphics g = Graphics.FromImage(blend))
            {
                g.Clear(Color.FromArgb(30, 30, 50));

                // Полосы
                for (int i = 0; i < w; i += 40)
                {
                    Color col = (i / 40) % 2 == 0
                        ? Color.FromArgb(200, 100, 50)
                        : Color.FromArgb(50, 100, 200);
                    using (SolidBrush br = new SolidBrush(col))
                        g.FillRectangle(br, i, 0, 20, h);
                }

                // Круги
                for (int i = 0; i < 8; i++)
                {
                    int x = 60 + i * 70;
                    int yy = 100 + (i % 3) * 120;
                    using (SolidBrush br = new SolidBrush(
                        Color.FromArgb(255, 200 - i * 20, 100 + i * 15, 50 + i * 25)))
                        g.FillEllipse(br, x - 30, yy - 30, 60, 60);
                }

                using (Font f = new Font("Segoe UI", 14, FontStyle.Bold))
                    g.DrawString("Изображение для наложения (Overlay)",
                        f, Brushes.White, 10, 10);
            }

            SetOriginal(main);
            SetBlend(blend);
        }

        System.Drawing.Drawing2D.PathGradientBrush CreateRadialBrush(
            int cx, int cy, int r, Color centerColor)
        {
            System.Drawing.Drawing2D.GraphicsPath path =
                new System.Drawing.Drawing2D.GraphicsPath();
            path.AddEllipse(cx - r, cy - r, r * 2, r * 2);

            System.Drawing.Drawing2D.PathGradientBrush pgb =
                new System.Drawing.Drawing2D.PathGradientBrush(path);
            pgb.CenterColor = centerColor;
            pgb.SurroundColors = new Color[] { Color.Transparent };
            return pgb;
        }

        // ═══════════════════════════════════════════
        //  УСТАНОВКА ИЗОБРАЖЕНИЙ
        // ═══════════════════════════════════════════

        void SetOriginal(Bitmap bmp)
        {
            if (imgOriginal != null) imgOriginal.Dispose();
            imgOriginal = bmp;

            if (pbOriginal.Image != null) pbOriginal.Image.Dispose();
            pbOriginal.Image = new Bitmap(bmp);

            ApplyTransform();
        }

        void SetBlend(Bitmap bmp)
        {
            if (imgBlend != null) imgBlend.Dispose();
            imgBlend = bmp;

            if (pbBlend.Image != null) pbBlend.Image.Dispose();
            pbBlend.Image = new Bitmap(bmp);

            ApplyOverlay();
        }

        /// <summary>
        /// Применяет логарифмическое преобразование
        /// </summary>
        void ApplyTransform()
        {
            if (imgOriginal == null) return;

            if (imgTransformed != null) imgTransformed.Dispose();
            imgTransformed = ImageAlgorithms.LogTransform(imgOriginal, logCoefficient);

            if (pbTransformed.Image != null) pbTransformed.Image.Dispose();
            pbTransformed.Image = new Bitmap(imgTransformed);

            // Гистограммы
            UpdateHistograms();

            // Обновляем наложение (используем преобразованное как базу)
            ApplyOverlay();

            UpdateStatus();
        }

        /// <summary>
        /// Применяет наложение Overlay
        /// </summary>
        void ApplyOverlay()
        {
            Bitmap baseForOverlay = imgTransformed != null ? imgTransformed : imgOriginal;
            if (baseForOverlay == null || imgBlend == null) return;

            if (imgOverlay != null) imgOverlay.Dispose();
            imgOverlay = ImageAlgorithms.OverlayBlend(baseForOverlay, imgBlend, overlayOpacity);

            if (pbOverlay.Image != null) pbOverlay.Image.Dispose();
            pbOverlay.Image = new Bitmap(imgOverlay);

            UpdateStatus();
        }

        void UpdateHistograms()
        {
            if (imgOriginal == null) return;

            int[] histOrig = ImageAlgorithms.BuildHistogram(imgOriginal);
            int[] histTrans = imgTransformed != null
                ? ImageAlgorithms.BuildHistogram(imgTransformed)
                : histOrig;

            Bitmap hOrig = ImageAlgorithms.DrawHistogram(histOrig, 700, 250,
                Color.FromArgb(100, 180, 255), Color.FromArgb(20, 20, 30),
                "Гистограмма ОРИГИНАЛА");

            Bitmap hTrans = ImageAlgorithms.DrawHistogram(histTrans, 700, 250,
                Color.FromArgb(255, 180, 80), Color.FromArgb(20, 20, 30),
                "Гистограмма ПОСЛЕ логарифмирования (c=" +
                logCoefficient.ToString("F2") + ")");

            if (pbHistOriginal.Image != null) pbHistOriginal.Image.Dispose();
            pbHistOriginal.Image = hOrig;

            if (pbHistTransformed.Image != null) pbHistTransformed.Image.Dispose();
            pbHistTransformed.Image = hTrans;
        }

        void UpdateStatus()
        {
            string s = "";
            if (imgOriginal != null)
                s += "Основное: " + imgOriginal.Width + "x" + imgOriginal.Height + "\n";
            if (imgBlend != null)
                s += "Наложение: " + imgBlend.Width + "x" + imgBlend.Height + "\n";
            s += "Коэфф: " + logCoefficient.ToString("F2") + "\n";
            s += "Opacity: " + (overlayOpacity * 100).ToString("F0") + "%";
            lblStatus.Text = s;
        }

        // ═══════════════════════════════════════════
        //  ЗАГРУЗКА / СОХРАНЕНИЕ
        // ═══════════════════════════════════════════

        void LoadMain()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "Загрузить основное изображение";
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
                        MessageBox.Show("Ошибка загрузки:\n" + ex.Message,
                            "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        void LoadBlend()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "Загрузить изображение для наложения";
                dlg.Filter = "Изображения|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tiff|Все|*.*";
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        Bitmap loaded;
                        using (Bitmap tmp = new Bitmap(dlg.FileName))
                            loaded = new Bitmap(tmp);
                        SetBlend(loaded);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Ошибка загрузки:\n" + ex.Message,
                            "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        void SaveAll()
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Выберите папку для сохранения";
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    string folder = dlg.SelectedPath;
                    string ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    int cnt = 0;

                    if (imgOriginal != null)
                    {
                        imgOriginal.Save(
                            Path.Combine(folder, "1_original_" + ts + ".png"),
                            ImageFormat.Png);
                        cnt++;
                    }
                    if (imgTransformed != null)
                    {
                        imgTransformed.Save(
                            Path.Combine(folder, "2_log_transformed_" + ts + ".png"),
                            ImageFormat.Png);
                        cnt++;
                    }
                    if (pbHistOriginal.Image != null)
                    {
                        pbHistOriginal.Image.Save(
                            Path.Combine(folder, "3_histogram_original_" + ts + ".png"),
                            ImageFormat.Png);
                        cnt++;
                    }
                    if (pbHistTransformed.Image != null)
                    {
                        pbHistTransformed.Image.Save(
                            Path.Combine(folder, "4_histogram_transformed_" + ts + ".png"),
                            ImageFormat.Png);
                        cnt++;
                    }
                    if (imgBlend != null)
                    {
                        imgBlend.Save(
                            Path.Combine(folder, "5_blend_layer_" + ts + ".png"),
                            ImageFormat.Png);
                        cnt++;
                    }
                    if (imgOverlay != null)
                    {
                        imgOverlay.Save(
                            Path.Combine(folder, "6_overlay_result_" + ts + ".png"),
                            ImageFormat.Png);
                        cnt++;
                    }

                    MessageBox.Show("Сохранено " + cnt + " файлов в:\n" + folder,
                        "Готово", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        void SaveCurrent()
        {
            int idx = tabs.SelectedIndex;
            Image img = null;

            switch (idx)
            {
                case 0: img = imgOriginal; break;
                case 1: img = imgTransformed; break;
                case 2: img = pbHistOriginal.Image; break;
                case 3: img = imgBlend; break;
                case 4: img = imgOverlay; break;
            }

            if (img == null)
            {
                MessageBox.Show("Нет изображения для сохранения.");
                return;
            }

            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Filter = "PNG|*.png|JPEG|*.jpg|BMP|*.bmp";
                string[] names = { "original", "log_transform", "histograms",
                                   "blend_layer", "overlay_result" };
                dlg.FileName = names[Math.Min(idx, names.Length - 1)];

                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    ImageFormat fmt = ImageFormat.Png;
                    string ext = Path.GetExtension(dlg.FileName).ToLower();
                    if (ext == ".jpg" || ext == ".jpeg") fmt = ImageFormat.Jpeg;
                    else if (ext == ".bmp") fmt = ImageFormat.Bmp;

                    img.Save(dlg.FileName, fmt);
                    MessageBox.Show("Сохранено: " + dlg.FileName);
                }
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            if (imgOriginal != null) imgOriginal.Dispose();
            if (imgTransformed != null) imgTransformed.Dispose();
            if (imgBlend != null) imgBlend.Dispose();
            if (imgOverlay != null) imgOverlay.Dispose();
        }
    }
}
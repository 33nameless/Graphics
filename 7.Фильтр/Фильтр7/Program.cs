using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ImageFilters
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
    //  ЦВЕТОВЫЕ ПРЕОБРАЗОВАНИЯ RGB ↔ HSL
    // ═══════════════════════════════════════════════
    public static class ColorConvert
    {
        public static void RgbToHsl(byte r, byte g, byte b,
            out double h, out double s, out double l)
        {
            double rd = r / 255.0, gd = g / 255.0, bd = b / 255.0;
            double max = Math.Max(rd, Math.Max(gd, bd));
            double min = Math.Min(rd, Math.Min(gd, bd));
            double delta = max - min;

            l = (max + min) / 2.0;

            if (delta < 1e-6)
            {
                h = 0; s = 0; return;
            }

            s = l < 0.5 ? delta / (max + min) : delta / (2.0 - max - min);

            if (Math.Abs(max - rd) < 1e-6)
                h = (gd - bd) / delta + (gd < bd ? 6 : 0);
            else if (Math.Abs(max - gd) < 1e-6)
                h = (bd - rd) / delta + 2;
            else
                h = (rd - gd) / delta + 4;
            h *= 60;
        }

        public static void HslToRgb(double h, double s, double l,
            out byte r, out byte g, out byte b)
        {
            if (s < 1e-6)
            {
                byte v = Clamp(l * 255);
                r = g = b = v;
                return;
            }
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            double p = 2 * l - q;
            double hn = h / 360.0;
            r = Clamp(HueToRgb(p, q, hn + 1.0 / 3.0) * 255);
            g = Clamp(HueToRgb(p, q, hn) * 255);
            b = Clamp(HueToRgb(p, q, hn - 1.0 / 3.0) * 255);
        }

        static double HueToRgb(double p, double q, double t)
        {
            if (t < 0) t += 1; if (t > 1) t -= 1;
            if (t < 1.0 / 6.0) return p + (q - p) * 6 * t;
            if (t < 0.5) return q;
            if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6;
            return p;
        }

        public static byte Clamp(double v)
        {
            if (v < 0) return 0;
            if (v > 255) return 255;
            return (byte)Math.Round(v);
        }

        public static double ClampD(double v, double min, double max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }
    }

    // ═══════════════════════════════════════════════
    //  ФИЛЬТРЫ
    // ═══════════════════════════════════════════════
    public static class Filters
    {
        // ───────────────────────────────────────────
        //  ЯДРО ГАУССИАНА
        //
        //  G(x,y) = (1/(2πσ²)) * exp(-(x²+y²)/(2σ²))
        //
        //  Фильтр низких частот: пропускает плавные
        //  изменения, подавляет резкие (размытие).
        // ───────────────────────────────────────────
        public static double[,] GaussianKernel(int radius, double sigma)
        {
            int size = 2 * radius + 1;
            double[,] kernel = new double[size, size];
            double sum = 0;
            double s2 = 2 * sigma * sigma;

            for (int y = -radius; y <= radius; y++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    double val = Math.Exp(-(x * x + y * y) / s2) / (Math.PI * s2);
                    kernel[y + radius, x + radius] = val;
                    sum += val;
                }
            }

            // Нормализация
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    kernel[y, x] /= sum;

            return kernel;
        }

        // ───────────────────────────────────────────
        //  ЯДРО ЛАПЛАСИАНА ГАУССИАНА (LoG)
        //
        //  LoG(x,y) = -(1/(πσ⁴)) * (1 - (x²+y²)/(2σ²)) 
        //             * exp(-(x²+y²)/(2σ²))
        //
        //  Фильтр высоких частот: подчёркивает границы,
        //  резкие переходы. Сочетает сглаживание Гауссиана
        //  с детекцией краёв Лапласиана.
        // ───────────────────────────────────────────
        public static double[,] LoGKernel(int radius, double sigma)
        {
            int size = 2 * radius + 1;
            double[,] kernel = new double[size, size];
            double s2 = sigma * sigma;
            double s4 = s2 * s2;
            double sum = 0;

            for (int y = -radius; y <= radius; y++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    double r2 = x * x + y * y;
                    double val = -(1.0 / (Math.PI * s4))
                        * (1.0 - r2 / (2.0 * s2))
                        * Math.Exp(-r2 / (2.0 * s2));
                    kernel[y + radius, x + radius] = val;
                    sum += val;
                }
            }

            // Центрирование (сумма ядра → 0)
            double avg = sum / (size * size);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    kernel[y, x] -= avg;

            return kernel;
        }

        // ───────────────────────────────────────────
        //  ФНЧ: ГАУССОВО РАЗМЫТИЕ ЗА ПРЕДЕЛАМИ ПРЯМОУГОЛЬНИКА
        //
        //  Внутри прямоугольника axb — оригинал.
        //  Снаружи — размытие Гауссианом.
        //  На границе — плавный переход (feather).
        // ───────────────────────────────────────────
        public static Bitmap GaussianBlurOutsideRect(Bitmap src,
            Rectangle rect, int radius, double sigma, int feather)
        {
            int w = src.Width, h = src.Height;

            // Полностью размытое изображение
            Bitmap blurred = ApplyConvolution(src, GaussianKernel(radius, sigma));

            // Результат: смешиваем оригинал и размытое по маске
            Bitmap result = new Bitmap(w, h, PixelFormat.Format32bppArgb);

            byte[] srcPx = GetPixels(src);
            byte[] blurPx = GetPixels(blurred);
            byte[] dstPx = new byte[srcPx.Length];

            int stride = w * 4;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int idx = y * stride + x * 4;

                    // Расстояние до прямоугольника
                    double dist = DistanceToRect(x, y, rect);

                    // Коэффициент смешивания:
                    // dist <= 0 → внутри → alpha = 0 (оригинал)
                    // dist >= feather → снаружи → alpha = 1 (размытое)
                    // между — плавный переход
                    double alpha;
                    if (dist <= 0)
                        alpha = 0;
                    else if (feather <= 0 || dist >= feather)
                        alpha = 1;
                    else
                        alpha = dist / feather;

                    // Плавность перехода (сглаживание)
                    alpha = alpha * alpha * (3 - 2 * alpha); // smoothstep

                    for (int ch = 0; ch < 4; ch++)
                    {
                        dstPx[idx + ch] = (byte)(
                            srcPx[idx + ch] * (1 - alpha) +
                            blurPx[idx + ch] * alpha);
                    }
                }
            }

            SetPixels(result, dstPx);
            blurred.Dispose();
            return result;
        }

        /// <summary>
        /// Расстояние от точки до прямоугольника.
        /// Отрицательное = внутри.
        /// </summary>
        static double DistanceToRect(int px, int py, Rectangle rect)
        {
            int dx = 0, dy = 0;

            if (px < rect.Left) dx = rect.Left - px;
            else if (px > rect.Right) dx = px - rect.Right;

            if (py < rect.Top) dy = rect.Top - py;
            else if (py > rect.Bottom) dy = py - rect.Bottom;

            if (dx == 0 && dy == 0)
            {
                // Внутри — возвращаем отрицательное расстояние до границы
                int distLeft = px - rect.Left;
                int distRight = rect.Right - px;
                int distTop = py - rect.Top;
                int distBottom = rect.Bottom - py;
                return -Math.Min(Math.Min(distLeft, distRight),
                                 Math.Min(distTop, distBottom));
            }

            return Math.Sqrt(dx * dx + dy * dy);
        }

        // ───────────────────────────────────────────
        //  ФВЧ: ЛАПЛАСИАН ГАУССИАНА В HSL
        //
        //  Левая половина:  LoG по H (тону) и L (яркости)
        //  Правая половина: LoG по S (насыщенности) и L (яркости)
        // ───────────────────────────────────────────
        public static Bitmap LoGInHSL(Bitmap src, int radius, double sigma,
            double strength)
        {
            int w = src.Width, h = src.Height;
            Bitmap result = new Bitmap(w, h, PixelFormat.Format32bppArgb);

            // Конвертируем всё изображение в HSL массивы
            byte[] srcPx = GetPixels(src);
            int stride = w * 4;

            double[,] hArr = new double[h, w];
            double[,] sArr = new double[h, w];
            double[,] lArr = new double[h, w];
            byte[,] aArr = new byte[h, w];

            // RGB → HSL
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int idx = y * stride + x * 4;
                    byte bv = srcPx[idx], gv = srcPx[idx + 1],
                         rv = srcPx[idx + 2], av = srcPx[idx + 3];

                    double hh, ss, ll;
                    ColorConvert.RgbToHsl(rv, gv, bv, out hh, out ss, out ll);

                    hArr[y, x] = hh;
                    sArr[y, x] = ss;
                    lArr[y, x] = ll;
                    aArr[y, x] = av;
                }
            }

            // Ядро LoG
            double[,] kernel = LoGKernel(radius, sigma);
            int kSize = 2 * radius + 1;

            // Свёртка каждого канала HSL
            double[,] logH = ConvolveChannel(hArr, kernel, radius, w, h, true);
            double[,] logS = ConvolveChannel(sArr, kernel, radius, w, h, false);
            double[,] logL = ConvolveChannel(lArr, kernel, radius, w, h, false);

            // Собираем результат
            byte[] dstPx = new byte[srcPx.Length];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int idx = y * stride + x * 4;
                    bool isLeft = x < w / 2;

                    double newH, newS, newL;

                    if (isLeft)
                    {
                        // Левая: LoG по H и L, S оригинал
                        newH = hArr[y, x] + logH[y, x] * strength * 360;
                        newL = lArr[y, x] + logL[y, x] * strength;
                        newS = sArr[y, x];  // без изменений
                    }
                    else
                    {
                        // Правая: LoG по S и L, H оригинал
                        newS = sArr[y, x] + logS[y, x] * strength;
                        newL = lArr[y, x] + logL[y, x] * strength;
                        newH = hArr[y, x];  // без изменений
                    }

                    // Нормализация
                    newH = ((newH % 360) + 360) % 360;
                    newS = ColorConvert.ClampD(newS, 0, 1);
                    newL = ColorConvert.ClampD(newL, 0, 1);

                    // HSL → RGB
                    byte rr, gg, bb;
                    ColorConvert.HslToRgb(newH, newS, newL, out rr, out gg, out bb);

                    dstPx[idx] = bb;
                    dstPx[idx + 1] = gg;
                    dstPx[idx + 2] = rr;
                    dstPx[idx + 3] = aArr[y, x];
                }
            }

            SetPixels(result, dstPx);
            return result;
        }

        /// <summary>
        /// Свёртка одного канала ядром
        /// </summary>
        static double[,] ConvolveChannel(double[,] channel, double[,] kernel,
            int radius, int w, int h, bool isAngle)
        {
            double[,] result = new double[h, w];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    double sum = 0;

                    for (int ky = -radius; ky <= radius; ky++)
                    {
                        for (int kx = -radius; kx <= radius; kx++)
                        {
                            int sx = Math.Max(0, Math.Min(w - 1, x + kx));
                            int sy = Math.Max(0, Math.Min(h - 1, y + ky));

                            double val = channel[sy, sx];

                            if (isAngle)
                            {
                                // Для Hue: учитываем цикличность
                                double diff = val - channel[y, x];
                                if (diff > 180) diff -= 360;
                                if (diff < -180) diff += 360;
                                sum += diff * kernel[ky + radius, kx + radius];
                            }
                            else
                            {
                                sum += val * kernel[ky + radius, kx + radius];
                            }
                        }
                    }

                    result[y, x] = sum;
                }
            }

            return result;
        }

        // ───────────────────────────────────────────
        //  СВЁРТКА (общая)
        // ───────────────────────────────────────────
        public static Bitmap ApplyConvolution(Bitmap src, double[,] kernel)
        {
            int w = src.Width, h = src.Height;
            int kSize = kernel.GetLength(0);
            int radius = kSize / 2;

            Bitmap dst = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            byte[] srcPx = GetPixels(src);
            byte[] dstPx = new byte[srcPx.Length];
            int stride = w * 4;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    double sumB = 0, sumG = 0, sumR = 0;
                    int idx = y * stride + x * 4;

                    for (int ky = -radius; ky <= radius; ky++)
                    {
                        for (int kx = -radius; kx <= radius; kx++)
                        {
                            int sx = Math.Max(0, Math.Min(w - 1, x + kx));
                            int sy = Math.Max(0, Math.Min(h - 1, y + ky));
                            int sIdx = sy * stride + sx * 4;
                            double kVal = kernel[ky + radius, kx + radius];

                            sumB += srcPx[sIdx] * kVal;
                            sumG += srcPx[sIdx + 1] * kVal;
                            sumR += srcPx[sIdx + 2] * kVal;
                        }
                    }

                    dstPx[idx] = ColorConvert.Clamp(sumB);
                    dstPx[idx + 1] = ColorConvert.Clamp(sumG);
                    dstPx[idx + 2] = ColorConvert.Clamp(sumR);
                    dstPx[idx + 3] = srcPx[idx + 3];
                }
            }

            SetPixels(dst, dstPx);
            return dst;
        }

        // ═══ Утилиты пикселей ═══
        public static byte[] GetPixels(Bitmap bmp)
        {
            BitmapData d = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            byte[] px = new byte[Math.Abs(d.Stride) * bmp.Height];
            Marshal.Copy(d.Scan0, px, 0, px.Length);
            bmp.UnlockBits(d);
            return px;
        }

        public static void SetPixels(Bitmap bmp, byte[] px)
        {
            BitmapData d = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(px, 0, d.Scan0, px.Length);
            bmp.UnlockBits(d);
        }
    }

    // ═══════════════════════════════════════════════
    //  ГЛАВНАЯ ФОРМА
    // ═══════════════════════════════════════════════
    public class MainForm : Form
    {
        Bitmap imgOriginal;
        TabControl tabs;
        PictureBox[] pics = new PictureBox[5];
        Label lblStatus;

        // ФНЧ параметры
        NumericUpDown nudRectX, nudRectY, nudRectW, nudRectH;
        NumericUpDown nudGaussRadius, nudFeather;
        TrackBar sliderGaussSigma;
        Label lblGaussSigma;

        // ФВЧ параметры
        NumericUpDown nudLogRadius;
        TrackBar sliderLogSigma, sliderLogStrength;
        Label lblLogSigma, lblLogStrength;

        public MainForm()
        {
            Text = "Фильтры НЧ (Гауссиан) и ВЧ (Лапласиан Гауссиана в HSL)";
            Size = new Size(1200, 800);
            MinimumSize = new Size(1000, 650);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(35, 35, 45);
            ForeColor = Color.White;
            BuildUI();
            CreateSample();
        }

        void BuildUI()
        {
            Panel left = new Panel();
            left.Dock = DockStyle.Left;
            left.Width = 285;
            left.BackColor = Color.FromArgb(40, 40, 55);
            left.AutoScroll = true;
            Controls.Add(left);

            int y = 8;

            // Загрузка
            left.Controls.Add(ML("ЗАГРУЗКА", 10, y, Color.FromArgb(100, 200, 255), true));
            y += 22;
            left.Controls.Add(MB("Загрузить изображение", ref y,
                Color.FromArgb(60, 150, 220), delegate { LoadImg(); }));
            left.Controls.Add(MB("Тестовое изображение", ref y,
                Color.FromArgb(150, 100, 200), delegate { CreateSample(); }));

            // ─── ФНЧ ───
            y += 5;
            left.Controls.Add(ML("ФНЧ: ГАУССОВО РАЗМЫТИЕ", 10, y,
                Color.FromArgb(255, 200, 100), true));
            y += 20;
            left.Controls.Add(ML("Область за прямоуг. axb\nразмывается гауссианом", 10, y,
                Color.FromArgb(180, 180, 200), false));
            y += 35;

            left.Controls.Add(ML("Прямоуг. X:", 10, y, Color.White, false));
            nudRectX = MN(left, 100, y, 100, 0, 2000); y += 26;
            left.Controls.Add(ML("Прямоуг. Y:", 10, y, Color.White, false));
            nudRectY = MN(left, 100, y, 80, 0, 2000); y += 26;
            left.Controls.Add(ML("Ширина a:", 10, y, Color.White, false));
            nudRectW = MN(left, 100, y, 250, 10, 2000); y += 26;
            left.Controls.Add(ML("Высота b:", 10, y, Color.White, false));
            nudRectH = MN(left, 100, y, 200, 10, 2000); y += 28;

            left.Controls.Add(ML("Радиус ядра:", 10, y, Color.White, false));
            nudGaussRadius = MN(left, 120, y, 5, 1, 30); y += 26;

            left.Controls.Add(ML("Sigma:", 10, y, Color.White, false));
            y += 18;
            sliderGaussSigma = new TrackBar();
            sliderGaussSigma.Location = new Point(10, y);
            sliderGaussSigma.Width = 255;
            sliderGaussSigma.Minimum = 5;
            sliderGaussSigma.Maximum = 100;
            sliderGaussSigma.Value = 30;
            sliderGaussSigma.TickFrequency = 10;
            sliderGaussSigma.ValueChanged += delegate
            {
                lblGaussSigma.Text = "σ = " + (sliderGaussSigma.Value / 10.0).ToString("F1");
            };
            left.Controls.Add(sliderGaussSigma);
            y += 42;
            lblGaussSigma = ML("σ = 3.0", 10, y, Color.FromArgb(255, 220, 100), false);
            left.Controls.Add(lblGaussSigma);
            y += 20;

            left.Controls.Add(ML("Feather:", 10, y, Color.White, false));
            nudFeather = MN(left, 100, y, 20, 0, 200); y += 30;

            // ─── ФВЧ ───
            left.Controls.Add(ML("ФВЧ: ЛАПЛАСИАН ГАУССИАНА", 10, y,
                Color.FromArgb(100, 255, 150), true));
            y += 20;
            left.Controls.Add(ML("Лево: LoG по H,L\nПраво: LoG по S,L", 10, y,
                Color.FromArgb(180, 180, 200), false));
            y += 35;

            left.Controls.Add(ML("Радиус:", 10, y, Color.White, false));
            nudLogRadius = MN(left, 100, y, 3, 1, 20); y += 26;

            left.Controls.Add(ML("Sigma:", 10, y, Color.White, false));
            y += 18;
            sliderLogSigma = new TrackBar();
            sliderLogSigma.Location = new Point(10, y);
            sliderLogSigma.Width = 255;
            sliderLogSigma.Minimum = 5;
            sliderLogSigma.Maximum = 50;
            sliderLogSigma.Value = 14;
            sliderLogSigma.TickFrequency = 5;
            sliderLogSigma.ValueChanged += delegate
            {
                lblLogSigma.Text = "σ = " + (sliderLogSigma.Value / 10.0).ToString("F1");
            };
            left.Controls.Add(sliderLogSigma);
            y += 42;
            lblLogSigma = ML("σ = 1.4", 10, y, Color.FromArgb(100, 255, 150), false);
            left.Controls.Add(lblLogSigma);
            y += 22;

            left.Controls.Add(ML("Сила:", 10, y, Color.White, false));
            y += 18;
            sliderLogStrength = new TrackBar();
            sliderLogStrength.Location = new Point(10, y);
            sliderLogStrength.Width = 255;
            sliderLogStrength.Minimum = 1;
            sliderLogStrength.Maximum = 100;
            sliderLogStrength.Value = 30;
            sliderLogStrength.TickFrequency = 10;
            sliderLogStrength.ValueChanged += delegate
            {
                lblLogStrength.Text = "Сила = " +
                    (sliderLogStrength.Value / 10.0).ToString("F1");
            };
            left.Controls.Add(sliderLogStrength);
            y += 42;
            lblLogStrength = ML("Сила = 3.0", 10, y,
                Color.FromArgb(100, 255, 150), false);
            left.Controls.Add(lblLogStrength);
            y += 28;

            // Кнопки
            left.Controls.Add(MB("Применить все фильтры", ref y,
                Color.FromArgb(50, 180, 80), delegate { ApplyAll(); }));
            left.Controls.Add(MB("Сохранить все", ref y,
                Color.FromArgb(60, 160, 120), delegate { SaveAll(); }));
            left.Controls.Add(MB("Сохранить текущее", ref y,
                Color.FromArgb(60, 140, 160), delegate { SaveCurrent(); }));

            y += 5;
            lblStatus = new Label();
            lblStatus.Location = new Point(10, y);
            lblStatus.Size = new Size(260, 50);
            lblStatus.ForeColor = Color.LightGray;
            lblStatus.Font = new Font("Consolas", 8);
            left.Controls.Add(lblStatus);

            // ═══ ВКЛАДКИ ═══
            tabs = new TabControl();
            tabs.Dock = DockStyle.Fill;
            tabs.Font = new Font("Segoe UI", 9);
            Controls.Add(tabs);

            string[] names = {
                "1. Оригинал",
                "2. ФНЧ: Гауссиан (размытие)",
                "3. ФВЧ: LoG (H,L | S,L)",
                "4. ФНЧ + ФВЧ",
                "5. Ядра фильтров"
            };

            for (int i = 0; i < 5; i++)
            {
                TabPage tp = new TabPage(names[i]);
                tp.BackColor = Color.FromArgb(20, 20, 30);
                PictureBox pb = new PictureBox();
                pb.Dock = DockStyle.Fill;
                pb.SizeMode = PictureBoxSizeMode.Zoom;
                pb.BackColor = Color.FromArgb(20, 20, 30);
                pics[i] = pb;
                tp.Controls.Add(pb);
                tabs.TabPages.Add(tp);
            }

            Controls.SetChildIndex(tabs, 0);
            Controls.SetChildIndex(left, 1);
        }

        Label ML(string t, int x, int y, Color c, bool b)
        {
            Label l = new Label();
            l.Text = t; l.Location = new Point(x, y); l.AutoSize = true;
            l.ForeColor = c;
            l.Font = new Font("Segoe UI", 9, b ? FontStyle.Bold : FontStyle.Regular);
            return l;
        }

        Button MB(string t, ref int y, Color c, EventHandler h)
        {
            Button b = new Button();
            b.Text = t; b.Location = new Point(15, y);
            b.Size = new Size(250, 28); b.FlatStyle = FlatStyle.Flat;
            b.BackColor = c; b.ForeColor = Color.White;
            b.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            b.Click += h; y += 32;
            return b;
        }

        NumericUpDown MN(Panel p, int x, int y, int v, int min, int max)
        {
            NumericUpDown n = new NumericUpDown();
            n.Location = new Point(x, y - 2); n.Width = 80;
            n.Minimum = min; n.Maximum = max; n.Value = v;
            n.BackColor = Color.FromArgb(55, 55, 70);
            n.ForeColor = Color.White;
            p.Controls.Add(n);
            return n;
        }

        // ═══ ТЕСТОВОЕ ИЗОБРАЖЕНИЕ ═══
        void CreateSample()
        {
            int w = 500, h = 400;
            Bitmap bmp = new Bitmap(w, h);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                // Фон
                using (var lgb = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new Point(0, 0), new Point(w, h),
                    Color.FromArgb(20, 40, 80), Color.FromArgb(100, 60, 30)))
                    g.FillRectangle(lgb, 0, 0, w, h);

                // Сетка
                using (Pen gp = new Pen(Color.FromArgb(40, 255, 255, 255)))
                    for (int i = 0; i < Math.Max(w, h); i += 40)
                    {
                        if (i < w) g.DrawLine(gp, i, 0, i, h);
                        if (i < h) g.DrawLine(gp, 0, i, w, i);
                    }

                // Фигуры разных цветов
                g.FillEllipse(Brushes.Red, 30, 30, 100, 100);
                g.FillRectangle(Brushes.Yellow, 180, 50, 100, 80);
                g.FillEllipse(Brushes.Cyan, 330, 40, 120, 90);
                g.FillEllipse(Brushes.Lime, 50, 200, 80, 120);
                g.FillRectangle(Brushes.Magenta, 200, 220, 90, 90);
                g.FillEllipse(Brushes.Orange, 350, 250, 110, 80);

                // Текст
                using (Font f = new Font("Segoe UI", 20, FontStyle.Bold))
                {
                    g.DrawString("Лево: H,L", f, Brushes.White, 20, 340);
                    g.DrawString("Право: S,L", f, Brushes.White, 280, 340);
                }

                // Разделитель
                using (Pen dp = new Pen(Color.FromArgb(100, 255, 255, 0), 2))
                {
                    dp.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                    g.DrawLine(dp, w / 2, 0, w / 2, h);
                }
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

        // ═══ ПРИМЕНЕНИЕ ФИЛЬТРОВ ═══
        void ApplyAll()
        {
            if (imgOriginal == null) return;

            Cursor = Cursors.WaitCursor;
            lblStatus.Text = "Обработка...";
            Application.DoEvents();

            try
            {
                int w = imgOriginal.Width, h = imgOriginal.Height;

                // Параметры ФНЧ
                Rectangle rect = new Rectangle(
                    (int)nudRectX.Value, (int)nudRectY.Value,
                    (int)nudRectW.Value, (int)nudRectH.Value);
                int gRadius = (int)nudGaussRadius.Value;
                double gSigma = sliderGaussSigma.Value / 10.0;
                int feather = (int)nudFeather.Value;

                // Параметры ФВЧ
                int logRadius = (int)nudLogRadius.Value;
                double logSigma = sliderLogSigma.Value / 10.0;
                double logStrength = sliderLogStrength.Value / 10.0;

                // 2. ФНЧ: Гауссово размытие за пределами прямоугольника
                Bitmap blurred = Filters.GaussianBlurOutsideRect(
                    imgOriginal, rect, gRadius, gSigma, feather);

                // Рисуем рамку прямоугольника для наглядности
                using (Graphics g = Graphics.FromImage(blurred))
                {
                    using (Pen p = new Pen(Color.FromArgb(150, 255, 255, 0), 2))
                    {
                        p.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                        g.DrawRectangle(p, rect);
                    }
                    using (Font f = new Font("Consolas", 9))
                        g.DrawString("Внутри: оригинал | Снаружи: Гауссиан σ=" +
                            gSigma.ToString("F1"), f,
                            new SolidBrush(Color.FromArgb(200, 255, 255, 100)), 5, 5);
                }
                SetPic(1, blurred);

                // 3. ФВЧ: Лапласиан Гауссиана в HSL
                Bitmap logged = Filters.LoGInHSL(imgOriginal, logRadius, logSigma,
                    logStrength);

                // Рисуем разделитель
                using (Graphics g = Graphics.FromImage(logged))
                {
                    using (Pen dp = new Pen(Color.FromArgb(120, 255, 255, 0), 2))
                    {
                        dp.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                        g.DrawLine(dp, w / 2, 0, w / 2, h);
                    }
                    using (Font f = new Font("Consolas", 9))
                    {
                        g.DrawString("LoG: H + L", f,
                            new SolidBrush(Color.FromArgb(200, 255, 200, 100)), 5, 5);
                        g.DrawString("LoG: S + L", f,
                            new SolidBrush(Color.FromArgb(200, 100, 255, 200)),
                            w / 2 + 5, 5);
                    }
                }
                SetPic(2, logged);

                // 4. Оба фильтра: сначала ФНЧ, потом ФВЧ
                Bitmap both = Filters.LoGInHSL(blurred, logRadius, logSigma, logStrength);
                using (Graphics g = Graphics.FromImage(both))
                {
                    using (Pen p = new Pen(Color.FromArgb(100, 255, 255, 0), 1))
                    {
                        p.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                        g.DrawRectangle(p, rect);
                        g.DrawLine(p, w / 2, 0, w / 2, h);
                    }
                    using (Font f = new Font("Consolas", 9))
                        g.DrawString("ФНЧ (Гауссиан) + ФВЧ (LoG в HSL)", f,
                            new SolidBrush(Color.FromArgb(200, 255, 255, 255)), 5, 5);
                }
                SetPic(3, both);

                // 5. Визуализация ядер
                Bitmap kernelVis = DrawKernels(gRadius, gSigma, logRadius, logSigma);
                SetPic(4, kernelVis);

                lblStatus.Text = w + "x" + h +
                    "\nГаусс: r=" + gRadius + " σ=" + gSigma.ToString("F1") +
                    "\nLoG: r=" + logRadius + " σ=" + logSigma.ToString("F1") +
                    " сила=" + logStrength.ToString("F1");
            }
            catch (Exception ex)
            {
                lblStatus.Text = "Ошибка: " + ex.Message;
            }

            Cursor = Cursors.Default;
        }

        /// <summary>
        /// Визуализация ядер фильтров
        /// </summary>
        Bitmap DrawKernels(int gR, double gS, int lR, double lS)
        {
            int w = 700, h = 500;
            Bitmap bmp = new Bitmap(w, h);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.FromArgb(15, 15, 25));

                Font fTitle = new Font("Segoe UI", 11, FontStyle.Bold);
                Font fSmall = new Font("Consolas", 8);

                // Ядро Гауссиана
                double[,] gKernel = Filters.GaussianKernel(gR, gS);
                DrawKernelGrid(g, gKernel, 20, 40, 300, 200,
                    "Ядро Гауссиана (ФНЧ) σ=" + gS.ToString("F1"),
                    Color.FromArgb(255, 200, 100), fTitle, fSmall);

                // Ядро LoG
                double[,] lKernel = Filters.LoGKernel(lR, lS);
                DrawKernelGrid(g, lKernel, 370, 40, 300, 200,
                    "Ядро LoG (ФВЧ) σ=" + lS.ToString("F1"),
                    Color.FromArgb(100, 255, 150), fTitle, fSmall);

                // Описание
                int ty = 280;
                g.DrawString("ФИЛЬТР НИЗКИХ ЧАСТОТ (ФНЧ) — Гауссиан:", fTitle,
                    new SolidBrush(Color.FromArgb(255, 200, 100)), 20, ty);
                ty += 22;
                g.DrawString(
                    "G(x,y) = (1/(2πσ²)) · exp(-(x²+y²)/(2σ²))\n" +
                    "Пропускает плавные изменения, подавляет резкие.\n" +
                    "Применяется к области ЗА ПРЕДЕЛАМИ прямоугольника axb.\n" +
                    "Внутри прямоугольника — оригинал. Feather = плавный переход.",
                    fSmall, Brushes.LightGray, 20, ty);

                ty += 70;
                g.DrawString("ФИЛЬТР ВЫСОКИХ ЧАСТОТ (ФВЧ) — Лапласиан Гауссиана:", fTitle,
                    new SolidBrush(Color.FromArgb(100, 255, 150)), 20, ty);
                ty += 22;
                g.DrawString(
                    "LoG(x,y) = -(1/(πσ⁴))·(1-(x²+y²)/(2σ²))·exp(-(x²+y²)/(2σ²))\n" +
                    "Подчёркивает границы и резкие переходы.\n" +
                    "Работает в цветовой модели HSL:\n" +
                    "  Левая половина:  LoG по Hue (тон) + Lightness (яркость)\n" +
                    "  Правая половина: LoG по Saturation (насыщ.) + Lightness (яркость)",
                    fSmall, Brushes.LightGray, 20, ty);

                fTitle.Dispose();
                fSmall.Dispose();
            }
            return bmp;
        }

        void DrawKernelGrid(Graphics g, double[,] kernel,
            int ox, int oy, int width, int height,
            string title, Color titleColor, Font fTitle, Font fSmall)
        {
            int size = kernel.GetLength(0);
            g.DrawString(title, fTitle, new SolidBrush(titleColor), ox, oy - 20);

            if (size > 11)
            {
                g.DrawString("Ядро " + size + "x" + size + " (слишком большое для сетки)",
                    fSmall, Brushes.Gray, ox, oy + 10);
                return;
            }

            float cellW = (float)width / size;
            float cellH = (float)height / size;

            // Находим min/max
            double min = double.MaxValue, max = double.MinValue;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    if (kernel[y, x] < min) min = kernel[y, x];
                    if (kernel[y, x] > max) max = kernel[y, x];
                }

            double range = max - min;
            if (range < 1e-10) range = 1;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    double val = kernel[y, x];
                    double norm = (val - min) / range;
                    int bright = (int)(norm * 255);

                    Color c;
                    if (val >= 0)
                        c = Color.FromArgb(bright / 2, bright, bright / 2);
                    else
                        c = Color.FromArgb(bright, bright / 3, bright / 3);

                    float cx = ox + x * cellW;
                    float cy = oy + y * cellH;

                    using (SolidBrush br = new SolidBrush(c))
                        g.FillRectangle(br, cx, cy, cellW - 1, cellH - 1);

                    // Значение
                    if (size <= 7)
                    {
                        string valStr = val.ToString("F3");
                        using (Font tiny = new Font("Consolas", 6))
                            g.DrawString(valStr, tiny, Brushes.White,
                                cx + 1, cy + cellH / 2 - 5);
                    }
                }
            }
        }

        void SetPic(int idx, Bitmap bmp)
        {
            if (pics[idx].Image != null) pics[idx].Image.Dispose();
            pics[idx].Image = bmp;
        }

        // ═══ ЗАГРУЗКА/СОХРАНЕНИЕ ═══
        void LoadImg()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Filter = "Изображения|*.png;*.jpg;*.jpeg;*.bmp|Все|*.*";
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        Bitmap loaded;
                        using (Bitmap tmp = new Bitmap(dlg.FileName))
                            loaded = new Bitmap(tmp);
                        SetOriginal(loaded);
                    }
                    catch (Exception ex) { MessageBox.Show("Ошибка: " + ex.Message); }
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
                    string[] n = { "1_original", "2_gaussian_lpf", "3_log_hpf",
                                   "4_both", "5_kernels" };
                    int c = 0;
                    for (int i = 0; i < 5; i++)
                        if (pics[i].Image != null)
                        {
                            pics[i].Image.Save(Path.Combine(f, n[i] + "_" + t + ".png"),
                                ImageFormat.Png);
                            c++;
                        }
                    MessageBox.Show("Сохранено " + c + " файлов в:\n" + f);
                }
            }
        }

        void SaveCurrent()
        {
            int idx = tabs.SelectedIndex;
            if (pics[idx].Image == null) { MessageBox.Show("Нет изображения."); return; }
            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Filter = "PNG|*.png|JPEG|*.jpg|BMP|*.bmp";
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    ImageFormat fmt = ImageFormat.Png;
                    string ext = Path.GetExtension(dlg.FileName).ToLower();
                    if (ext == ".jpg") fmt = ImageFormat.Jpeg;
                    else if (ext == ".bmp") fmt = ImageFormat.Bmp;
                    pics[idx].Image.Save(dlg.FileName, fmt);
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
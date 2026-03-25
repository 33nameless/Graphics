using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using System.Collections.Generic;

namespace GraphicsTask
{
    public class Program
    {
        [STAThread]
        static void Main()
        {
            // Запрашиваем размер нового изображения
            string widthInput = Microsoft.VisualBasic.Interaction.InputBox(
                "Введите ширину нового изображения (минимум 600):",
                "Размер изображения",
                "1000");

            if (!int.TryParse(widthInput, out int newWidth) || newWidth < 600)
            {
                MessageBox.Show("Некорректная ширина. Будет использовано 1000.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                newWidth = 1000;
            }

            string heightInput = Microsoft.VisualBasic.Interaction.InputBox(
                "Введите высоту нового изображения (минимум 600):",
                "Размер изображения",
                "1000");

            if (!int.TryParse(heightInput, out int newHeight) || newHeight < 600)
            {
                MessageBox.Show("Некорректная высота. Будет использовано 1000.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                newHeight = 1000;
            }

            // Загружаем исходное изображение
            OpenFileDialog openDialog = new OpenFileDialog
            {
                Title = "Выбери исходное изображение",
                Filter = "Изображения|*.png;*.jpg;*.jpeg;*.bmp;*.gif"
            };

            if (openDialog.ShowDialog() != DialogResult.OK) return;

            Bitmap sourceImage = new Bitmap(openDialog.FileName);

            // Генерируем результат
            Bitmap resultImage = GenerateResultImage(sourceImage, newWidth, newHeight);

            // === ПРЕДПРОСМОТР РЕЗУЛЬТАТА ===
            Form previewForm = new Form
            {
                Text = $"Предпросмотр результата — {newWidth}×{newHeight}",
                Size = new Size(900, 900),
                StartPosition = FormStartPosition.CenterScreen,
                FormBorderStyle = FormBorderStyle.Sizable
            };

            PictureBox pictureBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = resultImage
            };

            Button saveButton = new Button
            {
                Text = "Сохранить изображение",
                Dock = DockStyle.Bottom,
                Height = 50,
                Font = new Font("Arial", 14, FontStyle.Bold),
                BackColor = Color.DarkGreen,
                ForeColor = Color.White
            };

            saveButton.Click += (s, e) =>
            {
                SaveFileDialog saveDialog = new SaveFileDialog
                {
                    FileName = $"результат_{newWidth}x{newHeight}.png",
                    Filter = "PNG изображение|*.png"
                };

                if (saveDialog.ShowDialog() == DialogResult.OK)
                {
                    resultImage.Save(saveDialog.FileName, ImageFormat.Png);
                    MessageBox.Show($"Изображение сохранено!\n{saveDialog.FileName}", "Готово!", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            };

            previewForm.Controls.Add(pictureBox);
            previewForm.Controls.Add(saveButton);

            Application.Run(previewForm);

            sourceImage.Dispose();
            resultImage.Dispose();
        }

        // === ГЕНЕРАЦИЯ РЕЗУЛЬТАТА (всё масштабируется) ===
        static Bitmap GenerateResultImage(Bitmap source, int width, int height)
        {
            Bitmap result = new Bitmap(width, height);
            using (Graphics g = Graphics.FromImage(result))
                g.Clear(Color.White);

            float scale = Math.Min(width, height) / 1080f;
            if (scale < 0.3f) scale = 0.3f;

            int triangleSide = (int)(450 * scale);
            int triangleHeight = (int)(triangleSide * 0.8660254f);

            int srcCenterX = source.Width / 2;
            int srcCenterY = source.Height / 2;

            Point srcTop = new Point(srcCenterX, srcCenterY);
            Point srcLeft = new Point(srcCenterX - triangleSide / 2, srcCenterY + triangleHeight);
            Point srcRight = new Point(srcCenterX + triangleSide / 2, srcCenterY + triangleHeight);

            var pixels = GetTrianglePixels(srcTop, srcLeft, srcRight, source.Width, source.Height);

            int destTopY = height - triangleHeight;
            int destCenterX = triangleSide / 2;

            foreach (var p in pixels)
            {
                int nx = destCenterX + (p.X - srcTop.X);
                int ny = destTopY + (p.Y - srcTop.Y);
                if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                    result.SetPixel(nx, ny, source.GetPixel(p.X, p.Y));
            }

            // Зелёная обводка вырезанно треугольника
            using (Graphics g = Graphics.FromImage(result))
            {
                Point destTop = new Point(destCenterX, destTopY);
                Point destLeft = new Point(destCenterX - triangleSide / 2, destTopY + triangleHeight);
                Point destRight = new Point(destCenterX + triangleSide / 2, destTopY + triangleHeight);

                Pen pen = new Pen(Color.LimeGreen, Math.Max(3, (int)(5 * scale)));
                pen.StartCap = pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                g.DrawLine(pen, destTop, destLeft);
                g.DrawLine(pen, destLeft, destRight);
                g.DrawLine(pen, destRight, destTop);
            }

            // Оси графика
            using (Graphics g = Graphics.FromImage(result))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                int originX = width / 2;
                int originY = height;

                float axisWidth = width - 300;
                float axisHeight = height - 180;

                Pen axisPen = new Pen(Color.Black, Math.Max(3, (int)(4 * scale)));
                g.DrawLine(axisPen, originX - axisWidth / 2, originY, originX + axisWidth / 2, originY);
                g.DrawLine(axisPen, originX, originY, originX, originY - axisHeight);

                // Деления
                Pen tickPen = new Pen(Color.FromArgb(100, 0, 0, 0), 1);
                int tickEvery = 10;
                int tickLength = (int)(9 * scale);

                for (int d = 0; d <= axisWidth / 2; d += tickEvery)
                {
                    if (d == 0) continue;
                    int pxR = originX + d;
                    int pxL = originX - d;
                    g.DrawLine(tickPen, pxR, originY - tickLength / 2, pxR, originY + tickLength / 2);
                    g.DrawLine(tickPen, pxL, originY - tickLength / 2, pxL, originY + tickLength / 2);
                }

                for (int d = 0; d <= axisHeight; d += tickEvery)
                {
                    if (d == 0) continue;
                    int py = originY - d;
                    g.DrawLine(tickPen, originX - tickLength / 2, py, originX + tickLength / 2, py);
                }

                // Подписи
                int fontSize = (int)(42 * scale);
                if (fontSize < 20) fontSize = 20;

                g.DrawString("X", new Font("Arial", fontSize / 1.5f, FontStyle.Bold), Brushes.Black, originX + axisWidth / 2 - 45, originY + 35);
                g.DrawString("Y", new Font("Arial", fontSize / 1.5f, FontStyle.Bold), Brushes.Black, originX - 70, originY - axisHeight - 15);
                g.DrawString("y = (1/x)²", new Font("Arial", fontSize, FontStyle.Bold), Brushes.Navy,
                    originX - 230, originY - axisHeight - 90);

                // График функции
                Pen graphPen = new Pen(Color.Navy, Math.Max(3, (int)(4.5f * scale)));
                graphPen.StartCap = graphPen.EndCap = System.Drawing.Drawing2D.LineCap.Round;

                List<Point> left = new List<Point>();
                List<Point> right = new List<Point>();

                float unitsX = (axisWidth / 2) / 10f;
                float unitsY = axisHeight / 65f;

                for (float x = -10f; x < -0.12f; x += 0.008f)
                {
                    float y = 1f / (x * x);
                    int px = originX + (int)(x * unitsX);
                    int py = originY - (int)(y * unitsY);
                    if (py < 50) py = 50;
                    left.Add(new Point(px, py));
                }

                for (float x = 0.12f; x <= 10f; x += 0.008f)
                {
                    float y = 1f / (x * x);
                    int px = originX + (int)(x * unitsX);
                    int py = originY - (int)(y * unitsY);
                    if (py < 50) py = 50;
                    right.Add(new Point(px, py));
                }

                if (left.Count > 1) g.DrawLines(graphPen, left.ToArray());
                if (right.Count > 1) g.DrawLines(graphPen, right.ToArray());
            }

            return result;
        }

        // Методы треугольника 
        static List<Point> GetTrianglePixels(Point v1, Point v2, Point v3, int w, int h)
        {
            List<Point> pixels = new List<Point>();
            int minX = Math.Max(0, Math.Min(v1.X, Math.Min(v2.X, v3.X)));
            int maxX = Math.Min(w - 1, Math.Max(v1.X, Math.Max(v2.X, v3.X)));
            int minY = Math.Max(0, Math.Min(v1.Y, Math.Min(v2.Y, v3.Y)));
            int maxY = Math.Min(h - 1, Math.Max(v1.Y, Math.Max(v2.Y, v3.Y)));

            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                    if (IsPointInTriangle(x, y, v1, v2, v3))
                        pixels.Add(new Point(x, y));

            return pixels;
        }

        static bool IsPointInTriangle(int px, int py, Point v1, Point v2, Point v3)
        {
            float d = (v2.Y - v3.Y) * (v1.X - v3.X) + (v3.X - v2.X) * (v1.Y - v3.Y);
            if (Math.Abs(d) < 0.001f) return false;
            float a = ((v2.Y - v3.Y) * (px - v3.X) + (v3.X - v2.X) * (py - v3.Y)) / d;
            float b = ((v3.Y - v1.Y) * (px - v3.X) + (v1.X - v3.X) * (py - v3.Y)) / d;
            float c = 1 - a - b;
            return a >= 0 && b >= 0 && c >= 0;
        }
    }
}
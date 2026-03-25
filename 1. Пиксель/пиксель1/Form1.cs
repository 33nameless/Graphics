using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace пиксель1
{
    public partial class Form1 : Form
    {
        private Bitmap _image; // Битмап для хранения изображения
        public Form1()
        {
            InitializeComponent();
        }
        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            // Освобождение ресурсов при закрытии формы
            _image?.Dispose();
        }
        private void button1_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.gif";
                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        LoadImage(openFileDialog.FileName);                    
                    }
                    catch (Exception ex)
                    { 
                        MessageBox.Show("Ошибка при загрузке изображения:" + ex.Message);                        
                    }
                }

            }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            // Изменение пикселей
            if (_image != null)
            {
                try
                {
                    ChangePixelColors();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Ошибка при изменении пикселей: " + ex.Message);
                }
            }
            else
            {
                MessageBox.Show("Загрузите изображение перед изменением пикселей.");
            }
        }

        private void ChangePixelColors()
        {
            if (_image == null) throw new InvalidOperationException("Изображение не загружено.");

            // Ширина и Высота изображения
            int width = _image.Width;
            int height = _image.Height;

            // Верхний левый угол
            _image.SetPixel(0, 0, ApplyContrast(Color.FromArgb(255, 64, 64, 127)));

            // Верхний правый угол
            _image.SetPixel(width - 1, 0, ApplyContrast(Color.FromArgb(255, 127, 64, 127)));

            // Нижний правый угол
            _image.SetPixel(width - 1, height - 1, ApplyContrast(Color.FromArgb(255, 127, 127, 64)));

            pictureBox1.Invalidate(); // Перерисовываем PictureBox
        }
        // Применяем контрастность только к одному пикселю
        private Color ApplyContrast(Color originalColor)
        {
            // Увеличиваем контрастность с коэффициентом 1.5
            float contrastFactor = 1.5f;

            // Применяем контрастность к каждому каналу (RGB)
            int r = ApplyContrastToChannel(originalColor.R, contrastFactor);
            int g = ApplyContrastToChannel(originalColor.G, contrastFactor);
            int b = ApplyContrastToChannel(originalColor.B, contrastFactor);

            // Возвращаем изменённый цвет с сохранением альфа-канала
            return Color.FromArgb(originalColor.A, r, g, b);
        }
        // Применение контрастности к одному каналу
        private int ApplyContrastToChannel(int value, float contrastFactor)
        {
            float contrast = (259 * (contrastFactor + 255)) / (255 * (259 - contrastFactor));
            int newValue = (int)(contrast * (value - 128) + 128);

            // Ограничиваем значение в диапазоне [0, 255]
            return Math.Max(0, Math.Min(255, newValue));
        }
        private void button3_Click(object sender, EventArgs e)
        {
            // Сохранение изображения
            using (SaveFileDialog saveFileDialog = new SaveFileDialog())
            {
                saveFileDialog.Filter = "PNG Image|*.png|JPEG Image|*.jpg";
                if (saveFileDialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        SaveImage(saveFileDialog.FileName);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Ошибка при сохранении изображения: " + ex.Message);
                    }
                }
            }
        }

        private void SaveImage(string fileName)
        {
            try
            {
                _image.Save(fileName, ImageFormat.Png); // Сохраняем изображение в файл
            }
            catch (Exception ex)
            {
                throw new Exception("Не удалось сохранить изображение: " + ex.Message);
            }
        }
        private void LoadImage(string fileName) // Загружаем изображение в программу
        {
            try
            {
                _image = new Bitmap(fileName);
                pictureBox1.Image = _image;

                panel1.AutoScroll = true;
                panel1.AutoScrollMinSize = new Size(_image.Width, _image.Height);

                pictureBox1.Size = _image.Size;
            }
            catch (Exception ex)
            {
                throw new Exception("Не удалось загрузить изображение:" + ex.Message);
            }
        }
        private void pictureBox1_Click(object sender, EventArgs e)
        {

        }

        private void panel1_Paint(object sender, PaintEventArgs e)
        {

        }
    }
}

using dither_ex_machina.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;


namespace dither_ex_machina.Controls
{
    public partial class ColorPickerWindow : Window
    {
        private double _hue;        // 0..360
        private double _saturation; // 0..1
        private double _value;      // 0..1

        private bool _isDraggingSV;
        private bool _isDraggingHue;

        public Color SelectedColor => ColorSpaceHelper.HsvToRgb(_hue, _saturation, _value);

        public ColorPickerWindow(Color initialColor)
        {
            InitializeComponent();

            var (h, s, v) = ColorSpaceHelper.RgbToHsv(initialColor.R, initialColor.G, initialColor.B);
            _hue = h;
            _saturation = s;
            _value = v;

            CurrentColorSwatch.Background = new SolidColorBrush(initialColor);

            Loaded += (s2, e2) => UpdateAll();
        }

        public static bool ShowDialog(Window owner, Color initialColor, out Color result)
        {
            var picker = new ColorPickerWindow(initialColor) { Owner = owner };
            bool? dialogResult = picker.ShowDialog();

            if (dialogResult == true)
            {
                result = picker.SelectedColor;
                return true;
            }

            result = initialColor;
            return false;
        }

        //общий пересчёт всего UI из текущих _hue/_saturation/_value
        private void UpdateAll()
        {
            Color hueColor = ColorSpaceHelper.HsvToRgb(_hue, 1.0, 1.0);
            HueBackgroundRect.Fill = new SolidColorBrush(hueColor);

            double svWidth = SVSquareContainer.ActualWidth;
            double svHeight = SVSquareContainer.ActualHeight;

            double markerX = _saturation * svWidth;
            double markerY = (1 - _value) * svHeight;

            Canvas.SetLeft(SVMarkerOuter, markerX - SVMarkerOuter.Width / 2);
            Canvas.SetTop(SVMarkerOuter, markerY - SVMarkerOuter.Height / 2);
            Canvas.SetLeft(SVMarkerInner, markerX - SVMarkerInner.Width / 2);
            Canvas.SetTop(SVMarkerInner, markerY - SVMarkerInner.Height / 2);

            double hueBarHeight = HueBarContainer.ActualHeight;
            double hueMarkerY = (_hue / 360.0) * hueBarHeight;

            Canvas.SetTop(HueMarkerLeft, hueMarkerY - 5);
            Canvas.SetLeft(HueMarkerLeft, 0);

            Canvas.SetTop(HueMarkerRight, hueMarkerY - 5);
            Canvas.SetLeft(HueMarkerRight, HueBarContainer.ActualWidth - 9);

            Color current = SelectedColor;
            NewColorSwatch.Background = new SolidColorBrush(current);

            HTextBox.Text = Math.Round(_hue).ToString(CultureInfo.InvariantCulture);
            STextBox.Text = Math.Round(_saturation * 100).ToString(CultureInfo.InvariantCulture);
            VTextBox.Text = Math.Round(_value * 100).ToString(CultureInfo.InvariantCulture);

            RTextBox.Text = current.R.ToString(CultureInfo.InvariantCulture);
            GTextBox.Text = current.G.ToString(CultureInfo.InvariantCulture);
            BTextBox.Text = current.B.ToString(CultureInfo.InvariantCulture);

            HexTextBox.Text = $"{current.R:X2}{current.G:X2}{current.B:X2}";
        }

        //перетаскивание маркера в SV-квадрате
        private void SVSquare_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDraggingSV = true;
            SVMarkerCanvas.CaptureMouse();
            UpdateSVFromMouse(e.GetPosition(SVMarkerCanvas));
        }

        private void SVSquare_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDraggingSV || e.LeftButton != MouseButtonState.Pressed) return;
            UpdateSVFromMouse(e.GetPosition(SVMarkerCanvas));
        }

        private void SVSquare_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDraggingSV = false;
            SVMarkerCanvas.ReleaseMouseCapture();
        }

        private void UpdateSVFromMouse(Point p)
        {
            double width = SVSquareContainer.ActualWidth;
            double height = SVSquareContainer.ActualHeight;
            if (width <= 0 || height <= 0) return;

            double x = Math.Clamp(p.X, 0, width);
            double y = Math.Clamp(p.Y, 0, height);

            _saturation = x / width;
            _value = 1.0 - (y / height);

            UpdateAll();
        }

        //перетаскивание маркера на шкале оттенка
        private void HueBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDraggingHue = true;
            HueMarkerCanvas.CaptureMouse();
            UpdateHueFromMouse(e.GetPosition(HueMarkerCanvas));
        }

        private void HueBar_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDraggingHue || e.LeftButton != MouseButtonState.Pressed) return;
            UpdateHueFromMouse(e.GetPosition(HueMarkerCanvas));
        }

        private void HueBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDraggingHue = false;
            HueMarkerCanvas.ReleaseMouseCapture();
        }

        private void UpdateHueFromMouse(Point p)
        {
            double height = HueBarContainer.ActualHeight;
            if (height <= 0) return;

            double y = Math.Clamp(p.Y, 0, height);
            _hue = (y / height) * 360.0;

            UpdateAll();
        }

        //ручной ввод HSV
        private void HsvTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ApplyHsvFromTextBoxes();
                Keyboard.ClearFocus();
            }
        }

        private void HsvTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            ApplyHsvFromTextBoxes();
        }

        private void ApplyHsvFromTextBoxes()
        {
            if (!double.TryParse(HTextBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double h)) h = _hue;
            if (!double.TryParse(STextBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double s)) s = _saturation * 100;
            if (!double.TryParse(VTextBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double v)) v = _value * 100;

            _hue = Math.Clamp(h, 0, 360);
            _saturation = Math.Clamp(s, 0, 100) / 100.0;
            _value = Math.Clamp(v, 0, 100) / 100.0;

            UpdateAll();
        }

        //ручной ввод RGB
        private void RgbTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ApplyRgbFromTextBoxes();
                Keyboard.ClearFocus();
            }
        }

        private void RgbTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            ApplyRgbFromTextBoxes();
        }

        private void ApplyRgbFromTextBoxes()
        {
            Color current = SelectedColor;

            if (!byte.TryParse(RTextBox.Text, out byte r)) r = current.R;
            if (!byte.TryParse(GTextBox.Text, out byte g)) g = current.G;
            if (!byte.TryParse(BTextBox.Text, out byte b)) b = current.B;

            var (h, s, v) = ColorSpaceHelper.RgbToHsv(r, g, b);
            _hue = h;
            _saturation = s;
            _value = v;

            UpdateAll();
        }

        //ручной ввод HEX
        private void HexTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ApplyHexFromTextBox();
                Keyboard.ClearFocus();
            }
        }

        private void HexTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            ApplyHexFromTextBox();
        }

        private void ApplyHexFromTextBox()
        {
            string hex = HexTextBox.Text.Trim().TrimStart('#');

            if (hex.Length == 6 &&
                byte.TryParse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r) &&
                byte.TryParse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g) &&
                byte.TryParse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
            {
                var (h, s, v) = ColorSpaceHelper.RgbToHsv(r, g, b);
                _hue = h;
                _saturation = s;
                _value = v;
            }

            UpdateAll(); 
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}

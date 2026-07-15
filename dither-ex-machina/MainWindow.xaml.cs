using dither_ex_machina.Controls;
using dither_ex_machina.Effects;
using dither_ex_machina.Models;
using dither_ex_machina.Rendering;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace dither_ex_machina
{
    public partial class MainWindow : Window
    {
        private byte[] _grayscale;
        private int _width;
        private int _height;

        private WriteableBitmap _outputBitmap;
        private CancellationTokenSource _cts;
        private DispatcherTimer _timer;

        private readonly List<IDitherEffect> _effects = new()
        {
            new BayerDitherEffect(),
            new ErrorDiffusionEffect(),
            new ContourLinesEffect(),
            new HalftoneEffect(),
            new CrosshatchEffect(),
        };

        private readonly Dictionary<string, double> _currentEffectParams = new();

        public MainWindow()
        {
            InitializeComponent();

            GradientEditor.SetStops(new[]
            {
                new GradientStopModel { Position = 0.0, Color = Colors.Black },
                new GradientStopModel { Position = 1.0, Color = Colors.White },
            });

            ModeComboBox.ItemsSource = _effects;
            ModeComboBox.SelectedIndex = 0;
        }

        //загрузка изображения
        private void OpenButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Изображения|*.png;*.jpg;*.jpeg;*.bmp"
            };
            if (dlg.ShowDialog() != true) return;

            LoadImage(dlg.FileName);
        }

        private void LoadImage(string path)
        {
            BitmapImage bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();

            FormatConvertedBitmap converted = new FormatConvertedBitmap();
            converted.BeginInit();
            converted.Source = bmp;
            converted.DestinationFormat = PixelFormats.Bgra32;
            converted.EndInit();

            _width = converted.PixelWidth;
            _height = converted.PixelHeight;

            int stride = _width * 4;
            byte[] pixels = new byte[stride * _height];
            converted.CopyPixels(pixels, stride, 0);

            _grayscale = new byte[_width * _height];
            for (int i = 0, p = 0; p < pixels.Length; p += 4, i++)
            {
                byte b = pixels[p];
                byte g = pixels[p + 1];
                byte r = pixels[p + 2];
                _grayscale[i] = (byte)(0.299 * r + 0.587 * g + 0.114 * b);
            }

            _outputBitmap = new WriteableBitmap(_width, _height, 96, 96, PixelFormats.Bgra32, null);
            PreviewImage.Source = _outputBitmap;

            RequestRender();
        }

        //переключение режима эффекта
        private void ModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ModeComboBox.SelectedItem is IDitherEffect effect)
            {
                ApplyDefaultSettingsToUI(effect.DefaultSettings);
                RebuildParameterPanel(effect);
                RequestRender();
            }
        }

        private void ApplyDefaultSettingsToUI(RenderSettings defaults)
        {
            MedianRadiusSlider.ValueChanged -= Param_Changed;
            BlurRadiusSlider.ValueChanged -= Param_Changed;
            CutoffSlider.ValueChanged -= Param_Changed;
            HighlightCutoffSlider.ValueChanged -= Param_Changed;
            ContrastCenterSlider.ValueChanged -= Param_Changed;
            ContrastSteepnessSlider.ValueChanged -= Param_Changed;
            PostContrastSmoothSlider.ValueChanged -= Param_Changed;
            SupersampleSlider.ValueChanged -= Param_Changed;

            MedianRadiusSlider.Value = defaults.MedianRadius;
            BlurRadiusSlider.Value = defaults.BlurRadius;
            CutoffSlider.Value = defaults.ShadowCutoff;
            HighlightCutoffSlider.Value = defaults.HighlightCutoff;
            ContrastCenterSlider.Value = defaults.ContrastCenter;
            ContrastSteepnessSlider.Value = defaults.ContrastSteepness;
            PostContrastSmoothSlider.Value = defaults.PostContrastSmoothRadius;
            SupersampleSlider.Value = defaults.Supersample;
            InvertCheckBox.IsChecked = defaults.Invert;

            MedianRadiusSlider.ValueChanged += Param_Changed;
            BlurRadiusSlider.ValueChanged += Param_Changed;
            CutoffSlider.ValueChanged += Param_Changed;
            HighlightCutoffSlider.ValueChanged += Param_Changed;
            ContrastCenterSlider.ValueChanged += Param_Changed;
            ContrastSteepnessSlider.ValueChanged += Param_Changed;
            PostContrastSmoothSlider.ValueChanged += Param_Changed;
            SupersampleSlider.ValueChanged += Param_Changed;
        }

        private void RebuildParameterPanel(IDitherEffect effect)
        {
            EffectParametersPanel.Children.Clear();
            _currentEffectParams.Clear();

            foreach (var def in effect.Parameters)
            {
                var label = new TextBlock
                {
                    Text = def.Label,
                    Margin = new Thickness(0, 10, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                };

                var slider = new Slider
                {
                    Minimum = def.Min,
                    Maximum = def.Max,
                    Value = def.Default,
                    TickFrequency = def.TickFrequency,
                    IsSnapToTickEnabled = def.TickFrequency > 0
                };

                var valueText = new TextBlock();
                var binding = new Binding("Value")
                {
                    Source = slider,
                    StringFormat = def.Format
                };
                valueText.SetBinding(TextBlock.TextProperty, binding);

                string key = def.Key;
                _currentEffectParams[key] = def.Default;

                slider.ValueChanged += (s, e) =>
                {
                    _currentEffectParams[key] = slider.Value;
                    RequestRender();
                };

                EffectParametersPanel.Children.Add(label);
                EffectParametersPanel.Children.Add(slider);
                EffectParametersPanel.Children.Add(valueText);
            }
        }

        //gradient map

        private void GradientMapEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            bool enabled = GradientMapEnabledCheckBox.IsChecked == true;
            GradientEditor.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            RequestRender();
        }

        private void GradientEditor_GradientChanged(object sender, EventArgs e)
        {
            RequestRender();
        }


        private void Param_Changed(object sender, RoutedEventArgs e)
        {
            RequestRender();
        }

        private void RequestRender()
        {
            if (_grayscale == null) return;

            if (_timer == null)
            {
                _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
                _timer.Tick += (s, e) =>
                {
                    _timer.Stop();
                    RenderAsync();
                };
            }
            _timer.Stop();
            _timer.Start();
        }

        private RenderSettings GetCurrentSettings()
        {
            return new RenderSettings
            {
                MedianRadius = (int)MedianRadiusSlider.Value,
                BlurRadius = (int)BlurRadiusSlider.Value,
                ContrastCenter = ContrastCenterSlider.Value,
                ContrastSteepness = ContrastSteepnessSlider.Value,
                PostContrastSmoothRadius = (int)PostContrastSmoothSlider.Value,
                ShadowCutoff = CutoffSlider.Value,
                HighlightCutoff = HighlightCutoffSlider.Value,
                Supersample = (int)SupersampleSlider.Value,
                Invert = InvertCheckBox.IsChecked == true
            };
        }

        private async void RenderAsync()
        {
            if (ModeComboBox.SelectedItem is not IDitherEffect effect) return;

            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            var settings = GetCurrentSettings();
            var effectParams = new Dictionary<string, double>(_currentEffectParams);

            (byte[] r, byte[] g, byte[] b)? gradientLut = null;
            if (GradientMapEnabledCheckBox.IsChecked == true)
            {
                var stops = GradientEditor.GetStops();
                if (stops.Count >= 2)
                {
                    gradientLut = new ColorGradient(stops).BuildLut();
                }
            }

            int width = _width, height = _height;
            byte[] gray = _grayscale;

            try
            {
                byte[] result = await Task.Run(() =>
                    DitherRenderer.Render(gray, width, height, settings, effect, effectParams, gradientLut, token),
                    token);

                if (token.IsCancellationRequested) return;

                _outputBitmap.WritePixels(
                    new Int32Rect(0, 0, width, height),
                    result, width * 4, 0);
            }
            catch (OperationCanceledException)
            {
            }
        }

        //сохранение

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (_outputBitmap == null) return;

            var dlg = new SaveFileDialog
            {
                Filter = "PNG|*.png|JPEG|*.jpg"
            };
            if (dlg.ShowDialog() != true) return;

            BitmapEncoder encoder = dlg.FileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                ? new JpegBitmapEncoder()
                : (BitmapEncoder)new PngBitmapEncoder();

            encoder.Frames.Add(BitmapFrame.Create(_outputBitmap));

            using (var fs = new FileStream(dlg.FileName, FileMode.Create))
            {
                encoder.Save(fs);
            }
        }
    }
}
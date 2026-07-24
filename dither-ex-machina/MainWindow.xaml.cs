using dither_ex_machina.Controls;
using dither_ex_machina.Effects;
using dither_ex_machina.Models;
using dither_ex_machina.Rendering;
using Microsoft.Win32;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
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

        private WriteableBitmap _fullResBitmap;
        private WriteableBitmap _displayBitmap;
        private double _previewResolutionScale = 1.0;

        private bool _isPanning;
        private Point _panStart;

        private CancellationTokenSource _cts;
        private DispatcherTimer _timer;

        private readonly List<IDitherEffect> _effects = new()
        {
            new BayerDitherEffect(),
            new ErrorDiffusionEffect(),
            new IsolinesModulation(),
            new ContourLinesEffect(),
            new HalftoneEffect(),
            new CrosshatchEffect(),
            new OscilloscopeEffect(),
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

        private void PreviewBorder_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_displayBitmap == null) return;

            Point posInImage = e.GetPosition(PreviewImage);

            double oldScale = PreviewScale.ScaleX;
            double zoomFactor = e.Delta > 0 ? 1.1 : 1.0 / 1.1;
            double newScale = Math.Max(0.1, Math.Min(20, oldScale * zoomFactor));

            Point worldPoint = new Point(
                (posInImage.X - PreviewTranslate.X) / oldScale,
                (posInImage.Y - PreviewTranslate.Y) / oldScale);

            PreviewScale.ScaleX = newScale;
            PreviewScale.ScaleY = newScale;
            PreviewTranslate.X = posInImage.X - worldPoint.X * newScale;
            PreviewTranslate.Y = posInImage.Y - worldPoint.Y * newScale;

            e.Handled = true;
        }

        private void PreviewBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_displayBitmap == null) return;
            _isPanning = true;
            _panStart = e.GetPosition(this);
            ((Border)sender).CaptureMouse();
            e.Handled = true;
        }

        private void PreviewBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isPanning)
            {
                _isPanning = false;
                Mouse.Capture(null);
            }
        }

        private void PreviewBorder_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isPanning) return;

            Point currentPos = e.GetPosition(this);
            Vector delta = currentPos - _panStart;
            _panStart = currentPos;

            PreviewTranslate.X += delta.X;
            PreviewTranslate.Y += delta.Y;
        }

        private void ResetZoomButton_Click(object sender, RoutedEventArgs e)
        {
            PreviewScale.ScaleX = 1;
            PreviewScale.ScaleY = 1;
            PreviewTranslate.X = 0;
            PreviewTranslate.Y = 0;
        }

        private void PreviewResolutionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_grayscale == null) return;

            _previewResolutionScale = e.NewValue;
            RequestRender();
        }

        //загрузка изображения
        private void OpenButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Изображения|*.png;*.jpg;*.jpeg;*.jpe;*.bmp;*.webp"
            };
            if (dlg.ShowDialog() != true) return;

            LoadImage(dlg.FileName);
        }

        private void LoadImage(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            BitmapSource source;

            if (ext == ".webp")
            {
                using var bitmap = SKBitmap.Decode(path);
                int w = bitmap.Width;
                int h = bitmap.Height;
                byte[] imgPixels = new byte[w * h * 4];
                Marshal.Copy(bitmap.GetPixels(), imgPixels, 0, imgPixels.Length);

                source = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, imgPixels, w * 4);
            }
            else
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

                source = converted;
            }

            _width = source.PixelWidth;
            _height = source.PixelHeight;

            int stride = _width * 4;
            byte[] pixels = new byte[stride * _height];
            source.CopyPixels(pixels, stride, 0);

            _grayscale = new byte[_width * _height];
            for (int i = 0, p = 0; p < pixels.Length; p += 4, i++)
            {
                byte b = pixels[p];
                byte g = pixels[p + 1];
                byte r = pixels[p + 2];
                _grayscale[i] = (byte)(0.299 * r + 0.587 * g + 0.114 * b);
            }

            _fullResBitmap = new WriteableBitmap(_width, _height, 96, 96, PixelFormats.Bgra32, null);
            _displayBitmap = _fullResBitmap;
            PreviewImage.Source = _displayBitmap;

            PreviewScale.ScaleX = 1;
            PreviewScale.ScaleY = 1;
            PreviewTranslate.X = 0;
            PreviewTranslate.Y = 0;

            _previewResolutionScale = 1.0;
            PreviewResolutionSlider.Value = 1.0;

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

            GlowControlsPanel.Visibility = GlowEnabledCheckBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

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

        private void GlowEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            bool enabled = GlowEnabledCheckBox.IsChecked == true;
            GlowControlsPanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
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
                Invert = InvertCheckBox.IsChecked == true,
                GlowEnabled = GlowEnabledCheckBox.IsChecked == true,
                GlowRadius = (int)GlowRadiusSlider.Value,
                GlowThreshold = GlowThresholdSlider.Value,
                GlowIntensity = GlowIntensitySlider.Value
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

            if (settings.GlowEnabled)
            {
                int refSize = Math.Max(width, height);
                double scale = Math.Max(1.0, refSize / 1000.0);
                settings.GlowRadius = Math.Max(1, (int)(settings.GlowRadius * scale));
            }

            if (_previewResolutionScale < 1.0)
            {
                width = Math.Max(1, (int)(_width * _previewResolutionScale));
                height = Math.Max(1, (int)(_height * _previewResolutionScale));
                gray = ImageOps.DownsampleBilinear(_grayscale, _width, _height, width, height);
            }

            try
            {
                byte[] result = await Task.Run(() =>
                    DitherRenderer.Render(gray, width, height, settings, effect, effectParams, gradientLut, token),
                    token);

                if (token.IsCancellationRequested) return;

                if (_previewResolutionScale < 1.0)
                {
                    if (_displayBitmap == null || _displayBitmap.PixelWidth != width || _displayBitmap.PixelHeight != height)
                    {
                        _displayBitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
                        PreviewImage.Source = _displayBitmap;
                    }

                    _displayBitmap.WritePixels(
                        new Int32Rect(0, 0, width, height),
                        result, width * 4, 0);
                }
                else
                {
                    if (_fullResBitmap == null || _fullResBitmap.PixelWidth != _width || _fullResBitmap.PixelHeight != _height)
                    {
                        _fullResBitmap = new WriteableBitmap(_width, _height, 96, 96, PixelFormats.Bgra32, null);
                    }

                    _fullResBitmap.WritePixels(
                        new Int32Rect(0, 0, _width, _height),
                        result, _width * 4, 0);

                    _displayBitmap = _fullResBitmap;
                    PreviewImage.Source = _displayBitmap;
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        //сохранение

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (_fullResBitmap == null && _displayBitmap == null) return;

            var dlg = new SaveFileDialog
            {
                Filter = "PNG|*.png|JPEG|*.jpg;*.jpeg;*.jpe|BMP|*.bmp|WebP|*.webp"
            };
            if (dlg.ShowDialog() != true) return;

            byte[] gradientLutR = null, gradientLutG = null, gradientLutB = null;
            if (GradientMapEnabledCheckBox.IsChecked == true)
            {
                var stops = GradientEditor.GetStops();
                if (stops.Count >= 2)
                {
                    var lut = new ColorGradient(stops).BuildLut();
                    gradientLutR = lut.r;
                    gradientLutG = lut.g;
                    gradientLutB = lut.b;
                }
            }

            if (_previewResolutionScale < 1.0)
            {
                var settings = GetCurrentSettings();
                var effectParams = new Dictionary<string, double>(_currentEffectParams);
                var effect = ModeComboBox.SelectedItem as IDitherEffect;

                (byte[] r, byte[] g, byte[] b)? gradientLut = null;
                if (gradientLutR != null)
                {
                    gradientLut = (gradientLutR, gradientLutG, gradientLutB);
                }

                byte[] result = await Task.Run(() =>
                    DitherRenderer.Render(_grayscale, _width, _height, settings, effect, effectParams, gradientLut, CancellationToken.None));

                if (_fullResBitmap == null || _fullResBitmap.PixelWidth != _width || _fullResBitmap.PixelHeight != _height)
                {
                    _fullResBitmap = new WriteableBitmap(_width, _height, 96, 96, PixelFormats.Bgra32, null);
                }

                _fullResBitmap.WritePixels(
                    new Int32Rect(0, 0, _width, _height),
                    result, _width * 4, 0);
            }

            WriteableBitmap bitmapToSave = _fullResBitmap;

            string ext = Path.GetExtension(dlg.FileName).ToLowerInvariant();
            BitmapEncoder encoder = ext switch
            {
                ".jpg" or ".jpeg" or ".jpe" => new JpegBitmapEncoder(),
                ".bmp" => new BmpBitmapEncoder(),
                ".webp" => null,
                _ => new PngBitmapEncoder()
            };

            if (ext == ".webp")
            {
                int width = bitmapToSave.PixelWidth;
                int height = bitmapToSave.PixelHeight;
                int stride = width * 4;
                byte[] pixels = new byte[stride * height];
                bitmapToSave.CopyPixels(pixels, stride, 0);

                using var stream = new FileStream(dlg.FileName, FileMode.Create);
                var handle = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
                try
                {
                    using var image = SKImage.FromPixels(new SKImageInfo(width, height, SKColorType.Bgra8888), handle.AddrOfPinnedObject());
                    using var data = image.Encode(SKEncodedImageFormat.Webp, 80);
                    data.SaveTo(stream);
                }
                finally
                {
                    handle.Free();
                }
            }
            else
            {
                encoder.Frames.Add(BitmapFrame.Create(bitmapToSave));

                using (var fs = new FileStream(dlg.FileName, FileMode.Create))
                {
                    encoder.Save(fs);
                }
            }
        }
    }
}
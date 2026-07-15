using dither_ex_machina.Models;
using System;
using System.Collections.Generic;
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
    public partial class GradientBarControl : UserControl
    {
        private const double MarkerWidth = 16;
        private const double MarkerHeight = 14;

        private readonly List<GradientStopModel> _stops = new();
        private GradientStopModel _selectedStop;

        private bool _isDragging;
        private Polygon _draggedMarker;

        public event EventHandler GradientChanged;

        public GradientBarControl()
        {
            InitializeComponent();
            MarkersCanvas.SizeChanged += (s, e) => RepositionMarkers();
        }

        public void SetStops(IEnumerable<GradientStopModel> stops)
        {
            _stops.Clear();
            _stops.AddRange(stops);
            _selectedStop = null;

            RebuildMarkers();
            UpdateGradientPreview();
            UpdateSelectedStopPanel();
        }

        public List<GradientStopModel> GetStops() => _stops.OrderBy(s => s.Position).ToList();

        private void GradientPreviewBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            double width = GradientPreviewBorder.ActualWidth;
            if (width <= 0) return;

            Point pos = e.GetPosition(GradientPreviewBorder);
            double position = Math.Clamp(pos.X / width, 0.0, 1.0);

            Color currentColor = SampleColorAt(position);

            var newStop = new GradientStopModel { Position = position, Color = currentColor };
            _stops.Add(newStop);

            RebuildMarkers();
            UpdateGradientPreview();
            SelectStop(newStop);

            GradientChanged?.Invoke(this, EventArgs.Empty);
        }

        private Color SampleColorAt(double t)
        {
            var sorted = _stops.OrderBy(s => s.Position).ToList();
            if (sorted.Count == 0) return Colors.Gray;
            if (sorted.Count == 1) return sorted[0].Color;

            if (t <= sorted[0].Position) return sorted[0].Color;
            if (t >= sorted[^1].Position) return sorted[^1].Color;

            for (int i = 0; i < sorted.Count - 1; i++)
            {
                var a = sorted[i];
                var b = sorted[i + 1];
                if (t >= a.Position && t <= b.Position)
                {
                    double range = b.Position - a.Position;
                    double local = range < 1e-9 ? 0 : (t - a.Position) / range;
                    byte r = (byte)(a.Color.R + (b.Color.R - a.Color.R) * local);
                    byte g = (byte)(a.Color.G + (b.Color.G - a.Color.G) * local);
                    byte bl = (byte)(a.Color.B + (b.Color.B - a.Color.B) * local);
                    return Color.FromRgb(r, g, bl);
                }
            }
            return sorted[^1].Color;
        }

        private void RebuildMarkers()
        {
            MarkersCanvas.Children.Clear();

            foreach (var stop in _stops)
            {
                MarkersCanvas.Children.Add(CreateMarkerPolygon(stop));
            }

            RepositionMarkers();
        }

        private Polygon CreateMarkerPolygon(GradientStopModel stop)
        {
            bool isSelected = ReferenceEquals(stop, _selectedStop);

            var polygon = new Polygon
            {
                Points = new PointCollection(new[]
                {
                    new Point(0, 0),
                    new Point(MarkerWidth, 0),
                    new Point(MarkerWidth / 2.0, MarkerHeight)
                }),
                Fill = new SolidColorBrush(stop.Color),
                Stroke = isSelected ? Brushes.DeepSkyBlue : Brushes.Black,
                StrokeThickness = isSelected ? 2 : 1,
                Width = MarkerWidth,
                Height = MarkerHeight,
                Tag = stop,
                Cursor = Cursors.Hand
            };

            polygon.MouseLeftButtonDown += Marker_MouseLeftButtonDown;
            polygon.MouseMove += Marker_MouseMove;
            polygon.MouseLeftButtonUp += Marker_MouseLeftButtonUp;

            return polygon;
        }

        private void RepositionMarkers()
        {
            double width = MarkersCanvas.ActualWidth;
            if (width <= 0) return;

            foreach (UIElement child in MarkersCanvas.Children)
            {
                if (child is not Polygon marker) continue;
                var stop = (GradientStopModel)marker.Tag;

                double x = stop.Position * width - MarkerWidth / 2.0;
                Canvas.SetLeft(marker, x);
                Canvas.SetTop(marker, 0);
            }
        }

        private void UpdateMarkerFill(GradientStopModel stop)
        {
            foreach (UIElement child in MarkersCanvas.Children)
            {
                if (child is Polygon marker && ReferenceEquals(marker.Tag, stop))
                {
                    marker.Fill = new SolidColorBrush(stop.Color);
                    break;
                }
            }
        }

        private void UpdateMarkerHighlights()
        {
            foreach (UIElement child in MarkersCanvas.Children)
            {
                if (child is not Polygon marker) continue;
                var stop = (GradientStopModel)marker.Tag;
                bool isSelected = ReferenceEquals(stop, _selectedStop);
                marker.Stroke = isSelected ? Brushes.DeepSkyBlue : Brushes.Black;
                marker.StrokeThickness = isSelected ? 2 : 1;
            }
        }

        private void Marker_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var marker = (Polygon)sender;
            var stop = (GradientStopModel)marker.Tag;

            SelectStop(stop);

            if (e.ClickCount == 2)
            {
                OpenColorPickerForSelectedStop();
                e.Handled = true;
                return;
            }

            _isDragging = true;
            _draggedMarker = marker;
            marker.CaptureMouse();
            e.Handled = true;
        }

        private void Marker_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging || !ReferenceEquals(sender, _draggedMarker)) return;
            if (e.LeftButton != MouseButtonState.Pressed) return;

            double width = MarkersCanvas.ActualWidth;
            if (width <= 0) return;

            Point pos = e.GetPosition(MarkersCanvas);
            double newPosition = Math.Clamp(pos.X / width, 0.0, 1.0);

            var stop = (GradientStopModel)_draggedMarker.Tag;
            stop.Position = newPosition;

            RepositionMarkers();
            UpdateGradientPreview();
            UpdateSelectedStopPanel();
        }

        private void Marker_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging) return;

            _isDragging = false;
            _draggedMarker?.ReleaseMouseCapture();
            _draggedMarker = null;

            GradientChanged?.Invoke(this, EventArgs.Empty);
        }

        private void SelectStop(GradientStopModel stop)
        {
            _selectedStop = stop;
            UpdateMarkerHighlights();
            UpdateSelectedStopPanel();
        }

        private void UpdateSelectedStopPanel()
        {
            if (_selectedStop == null)
            {
                SelectedStopPanel.Visibility = Visibility.Collapsed;
                return;
            }

            SelectedStopPanel.Visibility = Visibility.Visible;
            SelectedColorSwatch.Background = new SolidColorBrush(_selectedStop.Color);
            PositionTextBox.Text = Math.Round(_selectedStop.Position * 100).ToString();
        }

        private void PositionTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            ApplyPositionFromTextBox();
        }

        private void PositionTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ApplyPositionFromTextBox();
                Keyboard.ClearFocus();
            }
        }

        private void ApplyPositionFromTextBox()
        {
            if (_selectedStop == null) return;

            if (double.TryParse(PositionTextBox.Text, out double percent))
            {
                _selectedStop.Position = Math.Clamp(percent / 100.0, 0, 1);
                RepositionMarkers();
                UpdateGradientPreview();
                GradientChanged?.Invoke(this, EventArgs.Empty);
            }

            PositionTextBox.Text = Math.Round(_selectedStop.Position * 100).ToString();
        }

        private void DeleteStopButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedStop == null || _stops.Count <= 2) return;

            _stops.Remove(_selectedStop);
            _selectedStop = null;

            RebuildMarkers();
            UpdateGradientPreview();
            UpdateSelectedStopPanel();

            GradientChanged?.Invoke(this, EventArgs.Empty);
        }

        private void SelectedColorSwatch_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            OpenColorPickerForSelectedStop();
        }

        private void OpenColorPickerForSelectedStop()
        {
            if (_selectedStop == null) return;

            var owner = Window.GetWindow(this);

            if (ColorPickerWindow.ShowDialog(owner, _selectedStop.Color, out Color newColor))
            {
                _selectedStop.Color = newColor;

                UpdateMarkerFill(_selectedStop);
                UpdateGradientPreview();
                UpdateSelectedStopPanel();

                GradientChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void UpdateGradientPreview()
        {
            if (_stops.Count == 0)
            {
                GradientPreviewBorder.Background = Brushes.Black;
                return;
            }

            var sorted = _stops.OrderBy(s => s.Position).ToList();

            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0.5),
                EndPoint = new Point(1, 0.5)
            };

            foreach (var s in sorted)
            {
                brush.GradientStops.Add(new System.Windows.Media.GradientStop(s.Color, s.Position));
            }

            GradientPreviewBorder.Background = brush;
        }
    }
}

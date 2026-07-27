using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Shell;
using System.Windows.Threading;

namespace dither_ex_machina.Controls
{
    public class CustomChromeController
    {
        //параметры трекинга зрачка
        private const double EyePupilMaxOffsetX = 15.0;
        private const double EyePupilMaxOffsetY = 4.0;
        private const double EyePupilSensitivity = 70.0;

        private const double EyePupilSmoothingHalfLife = 0.06;
        private DispatcherTimer _blinkTimer;
        private readonly Random _blinkRandom = new Random();
        private bool _isBlinking;

        private bool _eyeOpened;
        private Stopwatch _eyeStopwatch;

        private readonly Window _owner;
        private readonly Grid _eyeLogo;
        private readonly Path _eyelidTop;
        private readonly Path _eyelidBottom;
        private readonly Canvas _pupilCanvas;
        private readonly Path _eyePupil;
        private readonly TranslateTransform _eyePupilTransform;
        private readonly Border _cardBorder;
        private readonly Grid _previewClipGrid;

        private BezierSegment _topLeft;
        private BezierSegment _topRight;
        private BezierSegment _botLeft;
        private BezierSegment _botRight;

        //геометрия век
        private const double EyeX0 = 2;
        private const double EyeX1 = 16;   //горизонтальный выход из старта
        private const double EyeX2 = 26;   //начало подъёма
        private const double EyeXM = 40;   //середина (пик)
        private const double EyeX3 = 54;   //удержание пика
        private const double EyeX4 = 64;   //возврат к горизонтали
        private const double EyeX5 = 78;

        private const double EyeYMid = 16;
        private const double EyeYTop = 1;   //пик верхнего века
        private const double EyeYBot = 30;  //пик нижнего века

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out Win32Point lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        private struct Win32Point { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public Win32Point ptReserved;
            public Win32Point ptMaxSize;
            public Win32Point ptMaxPosition;
            public Win32Point ptMinTrackSize;
            public Win32Point ptMaxTrackSize;
        }

        private const int WM_GETMINMAXINFO = 0x0024;
        private const int MONITOR_DEFAULTTONEAREST = 0x00000002;

        [DllImport("user32.dll")]
        private static extern nint MonitorFromWindow(nint handle, int flags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);

        public CustomChromeController(
            Window owner,
            Grid eyeLogo,
            Path eyelidTop,
            Path eyelidBottom,
            Canvas pupilCanvas,
            Path eyePupil,
            TranslateTransform eyePupilTransform,
            Border cardBorder,
            Grid previewClipGrid)
        {
            _owner = owner;
            _eyeLogo = eyeLogo;
            _eyelidTop = eyelidTop;
            _eyelidBottom = eyelidBottom;
            _pupilCanvas = pupilCanvas;
            _eyePupil = eyePupil;
            _eyePupilTransform = eyePupilTransform;
            _cardBorder = cardBorder;
            _previewClipGrid = previewClipGrid;
        }

        public bool IsEyeOpened => _eyeOpened;

        public void Initialize()
        {
            UpdateCardClip();
            InitEyelids();

            _eyeStopwatch = Stopwatch.StartNew();
            CompositionTarget.Rendering += CompositionTarget_Rendering;

            StartBlinkTimer();
        }

        public void Cleanup()
        {
            CompositionTarget.Rendering -= CompositionTarget_Rendering;
            _blinkTimer?.Stop();
        }

        public void AttachHwndHook()
        {
            if (PresentationSource.FromVisual(_owner) is HwndSource source)
                source.AddHook(WndProc);
        }

        private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
        {
            if (msg == WM_GETMINMAXINFO)
            {
                var mmi = (MINMAXINFO)Marshal.PtrToStructure(lParam, typeof(MINMAXINFO));

                DpiScale dpi = VisualTreeHelper.GetDpi(_owner);

                if (_owner.MinWidth > 0)
                    mmi.ptMinTrackSize.X = (int)Math.Ceiling(_owner.MinWidth * dpi.DpiScaleX);
                if (_owner.MinHeight > 0)
                    mmi.ptMinTrackSize.Y = (int)Math.Ceiling(_owner.MinHeight * dpi.DpiScaleY);

                if (!double.IsInfinity(_owner.MaxWidth) && !double.IsNaN(_owner.MaxWidth))
                    mmi.ptMaxTrackSize.X = (int)Math.Floor(_owner.MaxWidth * dpi.DpiScaleX);
                if (!double.IsInfinity(_owner.MaxHeight) && !double.IsNaN(_owner.MaxHeight))
                    mmi.ptMaxTrackSize.Y = (int)Math.Floor(_owner.MaxHeight * dpi.DpiScaleY);

                nint monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
                if (monitor != nint.Zero)
                {
                    var mi = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
                    GetMonitorInfo(monitor, ref mi);

                    RECT work = mi.rcWork;
                    RECT bounds = mi.rcMonitor;

                    mmi.ptMaxPosition.X = Math.Abs(work.Left - bounds.Left);
                    mmi.ptMaxPosition.Y = Math.Abs(work.Top - bounds.Top);
                    mmi.ptMaxSize.X = Math.Abs(work.Right - work.Left);
                    mmi.ptMaxSize.Y = Math.Abs(work.Bottom - work.Top);
                }

                Marshal.StructureToPtr(mmi, lParam, true);
                handled = true;
            }
            return nint.Zero;
        }

        //строит PathGeometry из двух BezierSegment для одного века.
        private BezierSegment[] BuildEyelidGeometry(
            Path path,
            double peakY)
        {
            var leftSeg = new BezierSegment(
                new Point(EyeX1, EyeYMid),
                new Point(EyeX2, peakY),
                new Point(EyeXM, peakY),
                isStroked: true);

            var rightSeg = new BezierSegment(
                new Point(EyeX3, peakY),
                new Point(EyeX4, EyeYMid),
                new Point(EyeX5, EyeYMid),
                isStroked: true);

            var figure = new PathFigure
            {
                StartPoint = new Point(EyeX0, EyeYMid),
                IsClosed = false
            };
            figure.Segments.Add(leftSeg);
            figure.Segments.Add(rightSeg);

            var geo = new PathGeometry();
            geo.Figures.Add(figure);
            path.Data = geo;

            return new[] { leftSeg, rightSeg };
        }

        //строит замкнутую PathGeometry глаза
        private PathGeometry BuildEyeClipGeometry(double topPeakY, double botPeakY)
        {
            var figure = new PathFigure
            {
                StartPoint = new Point(EyeX0, EyeYMid),
                IsClosed = true
            };

            //верхнее веко (слева направо)
            figure.Segments.Add(new BezierSegment(
                new Point(EyeX1, EyeYMid),
                new Point(EyeX2, topPeakY),
                new Point(EyeXM, topPeakY),
                isStroked: false));

            figure.Segments.Add(new BezierSegment(
                new Point(EyeX3, topPeakY),
                new Point(EyeX4, EyeYMid),
                new Point(EyeX5, EyeYMid),
                isStroked: false));

            //нижнее веко (справа налево)
            figure.Segments.Add(new BezierSegment(
                new Point(EyeX4, EyeYMid),
                new Point(EyeX3, botPeakY),
                new Point(EyeXM, botPeakY),
                isStroked: false));

            figure.Segments.Add(new BezierSegment(
                new Point(EyeX2, botPeakY),
                new Point(EyeX1, EyeYMid),
                new Point(EyeX0, EyeYMid),
                isStroked: false));

            var geo = new PathGeometry();
            geo.Figures.Add(figure);
            return geo;
        }

        private void InitEyelids()
        {
            var top = BuildEyelidGeometry(_eyelidTop, EyeYMid);
            var bot = BuildEyelidGeometry(_eyelidBottom, EyeYMid);

            _topLeft = top[0]; _topRight = top[1];
            _botLeft = bot[0]; _botRight = bot[1];

            if (_pupilCanvas != null)
                _pupilCanvas.Clip = BuildEyeClipGeometry(EyeYMid, EyeYMid);
        }

        //анимация открытия
        public void AnimateEyeOpen()
        {
            if (_topLeft == null || _eyePupil == null) return;

            var duration = new Duration(TimeSpan.FromMilliseconds(450));
            var beginTime = TimeSpan.FromMilliseconds(100);
            var easing = new BackEase { Amplitude = 0.2, EasingMode = EasingMode.EaseOut };

            //верхнее веко
            AnimateBezierPoint(_topLeft,
                BezierSegment.Point1Property,
                new Point(EyeX1, EyeYMid), new Point(EyeX1, EyeYMid),
                duration, beginTime, easing);

            AnimateBezierPoint(_topLeft,
                BezierSegment.Point2Property,
                new Point(EyeX2, EyeYMid), new Point(EyeX2, EyeYTop),
                duration, beginTime, easing);

            AnimateBezierPoint(_topLeft,
                BezierSegment.Point3Property,
                new Point(EyeXM, EyeYMid), new Point(EyeXM, EyeYTop),
                duration, beginTime, easing);

            AnimateBezierPoint(_topRight,
                BezierSegment.Point1Property,
                new Point(EyeX3, EyeYMid), new Point(EyeX3, EyeYTop),
                duration, beginTime, easing);

            AnimateBezierPoint(_topRight,
                BezierSegment.Point2Property,
                new Point(EyeX4, EyeYMid), new Point(EyeX4, EyeYMid),
                duration, beginTime, easing);

            AnimateBezierPoint(_topRight,
                BezierSegment.Point3Property,
                new Point(EyeX5, EyeYMid), new Point(EyeX5, EyeYMid),
                duration, beginTime, easing);

            //нижнее веко
            AnimateBezierPoint(_botLeft,
                BezierSegment.Point1Property,
                new Point(EyeX1, EyeYMid), new Point(EyeX1, EyeYMid),
                duration, beginTime, easing);

            AnimateBezierPoint(_botLeft,
                BezierSegment.Point2Property,
                new Point(EyeX2, EyeYMid), new Point(EyeX2, EyeYBot),
                duration, beginTime, easing);

            AnimateBezierPoint(_botLeft,
                BezierSegment.Point3Property,
                new Point(EyeXM, EyeYMid), new Point(EyeXM, EyeYBot),
                duration, beginTime, easing);

            AnimateBezierPoint(_botRight,
                BezierSegment.Point1Property,
                new Point(EyeX3, EyeYMid), new Point(EyeX3, EyeYBot),
                duration, beginTime, easing);

            AnimateBezierPoint(_botRight,
                BezierSegment.Point2Property,
                new Point(EyeX4, EyeYMid), new Point(EyeX4, EyeYMid),
                duration, beginTime, easing);

            AnimateBezierPoint(_botRight,
                BezierSegment.Point3Property,
                new Point(EyeX5, EyeYMid), new Point(EyeX5, EyeYMid),
                duration, beginTime, easing);
            AnimatePupilClip(beginTime, duration);

            //зрачок
            var pupilFade = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(200))
            {
                BeginTime = TimeSpan.FromMilliseconds(360)
            };
            _eyePupil.BeginAnimation(UIElement.OpacityProperty, pupilFade);

            _eyeOpened = true;
        }

        private void AnimatePupilClip(TimeSpan beginTime, Duration duration)
        {
            if (_pupilCanvas == null) return;

            int steps = 40;
            double totalMs = duration.TimeSpan.TotalMilliseconds;
            double delayMs = beginTime.TotalMilliseconds;

            var easing = new BackEase { Amplitude = 0.2, EasingMode = EasingMode.EaseOut };

            int step = 0;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delayMs) };

            timer.Tick += (s, e) =>
            {
                timer.Interval = TimeSpan.FromMilliseconds(totalMs / steps);

                double t = Math.Min((double)step / steps, 1.0);

                double et = EaseBackOut(t, 0.2);

                double topPeakY = EyeYMid + (EyeYTop - EyeYMid) * et;
                double botPeakY = EyeYMid + (EyeYBot - EyeYMid) * et;

                _pupilCanvas.Clip = BuildEyeClipGeometry(topPeakY, botPeakY);

                step++;
                if (step > steps) timer.Stop();
            };

            timer.Start();
        }

        //моргание
        private void StartBlinkTimer()
        {
            _blinkTimer = new DispatcherTimer();
            _blinkTimer.Tick += BlinkTimer_Tick;
            ScheduleNextBlink();
            _blinkTimer.Start();
        }

        private void ScheduleNextBlink()
        {
            //случайный интервал между морганиями: 2.5–7 секунд
            double seconds = 12.5 + _blinkRandom.NextDouble() * 14.5;
            _blinkTimer.Interval = TimeSpan.FromSeconds(seconds);
        }

        private void BlinkTimer_Tick(object sender, EventArgs e)
        {
            if (_eyeOpened && !_isBlinking)
                AnimateBlink();

            ScheduleNextBlink();
        }

        private void AnimateBlink()
        {
            if (_topLeft == null || _eyePupil == null) return;

            _isBlinking = true;

            var closeDuration = TimeSpan.FromMilliseconds(90);
            var openDuration = TimeSpan.FromMilliseconds(120);

            var closeEase = new QuadraticEase { EasingMode = EasingMode.EaseIn };
            var openEase = new QuadraticEase { EasingMode = EasingMode.EaseOut };

            AnimateBezierBlink(_topLeft, BezierSegment.Point2Property, EyeYTop, closeDuration, openDuration, closeEase, openEase);
            AnimateBezierBlink(_topLeft, BezierSegment.Point3Property, EyeYTop, closeDuration, openDuration, closeEase, openEase);
            AnimateBezierBlink(_topRight, BezierSegment.Point1Property, EyeYTop, closeDuration, openDuration, closeEase, openEase);

            AnimateBezierBlink(_botLeft, BezierSegment.Point2Property, EyeYBot, closeDuration, openDuration, closeEase, openEase);
            AnimateBezierBlink(_botLeft, BezierSegment.Point3Property, EyeYBot, closeDuration, openDuration, closeEase, openEase);
            AnimateBezierBlink(_botRight, BezierSegment.Point1Property, EyeYBot, closeDuration, openDuration, closeEase, openEase);

            AnimatePupilClipBlink(closeDuration, openDuration);

            double totalMs = closeDuration.TotalMilliseconds + openDuration.TotalMilliseconds;
            var resetTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(totalMs + 20) };
            resetTimer.Tick += (s, e) =>
            {
                resetTimer.Stop();
                _isBlinking = false;
            };
            resetTimer.Start();
        }

        private static void AnimateBezierBlink(
            BezierSegment segment,
            DependencyProperty property,
            double openY,
            TimeSpan closeDuration,
            TimeSpan openDuration,
            IEasingFunction closeEase,
            IEasingFunction openEase)
        {
            Point current = (Point)segment.GetValue(property);
            double x = current.X;

            var animation = new PointAnimationUsingKeyFrames { FillBehavior = FillBehavior.HoldEnd };

            animation.KeyFrames.Add(new EasingPointKeyFrame(
                new Point(x, EyeYMid),
                KeyTime.FromTimeSpan(closeDuration),
                closeEase));

            animation.KeyFrames.Add(new EasingPointKeyFrame(
                new Point(x, openY),
                KeyTime.FromTimeSpan(closeDuration + openDuration),
                openEase));

            segment.BeginAnimation(property, animation);
        }

        private void AnimatePupilClipBlink(TimeSpan closeDuration, TimeSpan openDuration)
        {
            if (_pupilCanvas == null) return;

            const int closeSteps = 6;
            const int openSteps = 8;

            int step = 0;
            int totalSteps = closeSteps + openSteps;

            var timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(closeDuration.TotalMilliseconds / closeSteps)
            };

            timer.Tick += (s, e) =>
            {
                step++;

                double topPeakY, botPeakY;

                if (step <= closeSteps)
                {
                    double t = (double)step / closeSteps;
                    topPeakY = EyeYTop + (EyeYMid - EyeYTop) * t;
                    botPeakY = EyeYBot + (EyeYMid - EyeYBot) * t;
                }
                else
                {
                    if (step == closeSteps + 1)
                        timer.Interval = TimeSpan.FromMilliseconds(openDuration.TotalMilliseconds / openSteps);

                    int openStep = step - closeSteps;
                    double t = (double)openStep / openSteps;
                    topPeakY = EyeYMid + (EyeYTop - EyeYMid) * t;
                    botPeakY = EyeYMid + (EyeYBot - EyeYMid) * t;
                }

                _pupilCanvas.Clip = BuildEyeClipGeometry(topPeakY, botPeakY);

                if (step >= totalSteps) timer.Stop();
            };

            timer.Start();
        }

        private static double EaseBackOut(double t, double amplitude)
        {
            t = 1.0 - t;
            double result = t * t * ((amplitude + 1) * t - amplitude);
            return 1.0 - result;
        }

        private static void AnimateBezierPoint(
            BezierSegment segment,
            DependencyProperty property,
            Point from, Point to,
            Duration duration, TimeSpan beginTime,
            IEasingFunction easing)
        {
            var anim = new PointAnimation(from, to, duration)
            {
                BeginTime = beginTime,
                EasingFunction = easing,
                FillBehavior = FillBehavior.HoldEnd
            };
            segment.BeginAnimation(property, anim);
        }

        //трекинг зрачка
        private void CompositionTarget_Rendering(object sender, EventArgs e)
        {
            if (_eyeLogo == null || _eyePupilTransform == null) return;
            if (_eyeLogo.ActualWidth <= 0 || _eyeLogo.ActualHeight <= 0) return;
            if (!GetCursorPos(out Win32Point p)) return;

            double dt = _eyeStopwatch.Elapsed.TotalSeconds;
            _eyeStopwatch.Restart();
            dt = Math.Min(dt, 0.1);

            Point cursorScreen = new Point(p.X, p.Y);

            Point localPoint;
            try { localPoint = _eyeLogo.PointFromScreen(cursorScreen); }
            catch { return; }

            Point center = new Point(_eyeLogo.ActualWidth / 2.0, _eyeLogo.ActualHeight / 2.0);
            Vector direction = localPoint - center;

            Point targetOffset;
            double length = direction.Length;

            if (length < 0.01)
            {
                targetOffset = new Point(0, 0);
            }
            else
            {
                double factor = Math.Min(length / EyePupilSensitivity, 1.0);
                targetOffset = new Point(
                    direction.X / length * EyePupilMaxOffsetX * factor,
                    direction.Y / length * EyePupilMaxOffsetY * factor);
            }

            double smoothing = 1.0 - Math.Pow(0.5, dt / EyePupilSmoothingHalfLife);

            _eyePupilTransform.X += (targetOffset.X - _eyePupilTransform.X) * smoothing;
            _eyePupilTransform.Y += (targetOffset.Y - _eyePupilTransform.Y) * smoothing;
        }

        private void UpdateCardClip()
        {
            if (_cardBorder == null) return;
            double w = Math.Max(0, _cardBorder.ActualWidth);
            double h = Math.Max(0, _cardBorder.ActualHeight);
            double radius = _cardBorder.CornerRadius.TopLeft;
            _cardBorder.Clip = new RectangleGeometry(new Rect(0, 0, w, h), radius, radius);
        }

        public void OnPreviewClipGridSizeChanged(SizeChangedEventArgs e)
        {
            double w = Math.Max(0, e.NewSize.Width);
            double h = Math.Max(0, e.NewSize.Height);
            _previewClipGrid.Clip = new RectangleGeometry(new Rect(0, 0, w, h), 9, 9);
        }

        public void OnCardBorderSizeChanged(SizeChangedEventArgs e) => UpdateCardClip();

        public void OnWindowStateChanged()
        {
            _cardBorder.CornerRadius = _owner.WindowState == WindowState.Maximized
                ? new CornerRadius(0)
                : new CornerRadius(10);
            UpdateCardClip();
        }

        public void Minimize() => SystemCommands.MinimizeWindow(_owner);

        public void MaximizeRestore()
        {
            _owner.WindowState = _owner.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        public void Close() => SystemCommands.CloseWindow(_owner);
    }
}

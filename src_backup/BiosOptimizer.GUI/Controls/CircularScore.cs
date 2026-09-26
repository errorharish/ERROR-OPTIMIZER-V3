using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace BiosOptimizer.GUI.Controls
{
    public class CircularScore : UserControl
    {
        // ── Dependency Properties ──────────────────────────────────────

        public static readonly DependencyProperty ScoreProperty =
            DependencyProperty.Register(nameof(Score), typeof(double), typeof(CircularScore),
                new PropertyMetadata(0.0, OnScoreChanged));

        public static readonly DependencyProperty MaxScoreProperty =
            DependencyProperty.Register(nameof(MaxScore), typeof(double), typeof(CircularScore),
                new PropertyMetadata(100.0, OnScoreChanged));

        public static readonly DependencyProperty ScoreLabelProperty =
            DependencyProperty.Register(nameof(ScoreLabel), typeof(string), typeof(CircularScore),
                new PropertyMetadata("Calculating...", OnScoreLabelChanged));



        public static readonly DependencyProperty IsLoadingProperty =
            DependencyProperty.Register(nameof(IsLoading), typeof(bool), typeof(CircularScore),
                new PropertyMetadata(false, OnIsLoadingChanged));

        public double Score
        {
            get => (double)GetValue(ScoreProperty);
            set => SetValue(ScoreProperty, value);
        }

        public double MaxScore
        {
            get => (double)GetValue(MaxScoreProperty);
            set => SetValue(MaxScoreProperty, value);
        }

        public string ScoreLabel
        {
            get => (string)GetValue(ScoreLabelProperty);
            set => SetValue(ScoreLabelProperty, value);
        }



        public bool IsLoading
        {
            get => (bool)GetValue(IsLoadingProperty);
            set => SetValue(IsLoadingProperty, value);
        }

        // ── Explicit State Machine ──────────────────────────────────────
        private enum CircularScoreState
        {
            Uninitialized,
            Loading,
            Ready
        }

        private CircularScoreState _state = CircularScoreState.Uninitialized;

        // ── Visual Tree ─────────────────────────────────────────────
        private Grid _rootGrid;
        private Canvas _pathCanvas;
        private Path _trackPath;
        private Path _activeArcPath;
        private Path _loadingArcPath;
        private StackPanel _centerPanel;
        
        private TextBlock _scoreText;
        private TextBlock _maxText;
        private TextBlock _labelText;

        private RotateTransform _loaderTransform;

        // ── Geometry System ─────────────────────────────────────────
        private struct GaugeGeometry
        {
            public Point Center;
            public double Radius;
            public bool IsValid;
        }

        private GaugeGeometry _geometry;

        // ── Animation State ─────────────────────────────────────────
        private bool _isRendering = false;
        private double _loadingAngle = 0;
        private double _currentScoreAngle = 0;
        private double _targetScoreAngle = 0;
        private double _startScoreAngle = 0;
        private DateTime _animationStartTime;
        private const double AnimationDurationMs = 900.0;

        // ── Lifecycle ─────────────────────────────────────────────────
        public CircularScore()
        {
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            BuildVisualTree();
        }

        private void BuildVisualTree()
        {
            _rootGrid = new Grid
            {
                ClipToBounds = true
            };

            // Canvas forces absolute coordinate rendering without bounding-box shifts
            _pathCanvas = new Canvas
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            _rootGrid.Children.Add(_pathCanvas);

            // Track Ring (Subtle dark track, no halo)
            _trackPath = CreateArcPath(new SolidColorBrush(Color.FromRgb(26, 29, 38)), 14);
            _trackPath.Visibility = Visibility.Collapsed;
            _pathCanvas.Children.Add(_trackPath);

            // Active Score Arc
            _activeArcPath = CreateArcPath(new SolidColorBrush(Color.FromRgb(229, 57, 53)), 14);
            _activeArcPath.Visibility = Visibility.Collapsed;
            _pathCanvas.Children.Add(_activeArcPath);

            // Loading Arc
            _loadingArcPath = CreateArcPath(new SolidColorBrush(Color.FromRgb(229, 57, 53)), 14);
            _loadingArcPath.Visibility = Visibility.Collapsed;
            
            _loaderTransform = new RotateTransform(0);
            _loadingArcPath.RenderTransform = _loaderTransform;
            _pathCanvas.Children.Add(_loadingArcPath);

            // Center Content
            _centerPanel = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Orientation = Orientation.Vertical,
                Visibility = Visibility.Collapsed
            };

            _scoreText = new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                FontSize = 48,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(240, 241, 243)),
                Text = "0"
            };

            _maxText = new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromArgb(120, 157, 163, 176)),
                Text = "/100",
                Margin = new Thickness(0, -4, 0, 0)
            };

            _labelText = new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(229, 57, 53)),
                Text = ScoreLabel,
                Margin = new Thickness(0, 6, 0, 0)
            };

            _centerPanel.Children.Add(_scoreText);
            _centerPanel.Children.Add(_maxText);
            _centerPanel.Children.Add(_labelText);
            _rootGrid.Children.Add(_centerPanel);

            Content = _rootGrid;
        }

        private Path CreateArcPath(Brush stroke, double thickness)
        {
            return new Path
            {
                Stroke = stroke,
                StrokeThickness = thickness,
                Fill = Brushes.Transparent,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Stretch = Stretch.None
            };
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            EvaluateState();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            StopAnimation();
            _state = CircularScoreState.Uninitialized;
        }

        // ── Geometry Calculation ──────────────────────────────────────
        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            UpdateGeometry();
        }

        private void UpdateGeometry()
        {
            if (ActualWidth <= 0 || ActualHeight <= 0)
            {
                _geometry = new GaugeGeometry { IsValid = false };
                return;
            }

            double r = (Math.Min(ActualWidth, ActualHeight) / 2.0) - 20.0;
            if (r < 1) r = 1;

            _geometry = new GaugeGeometry
            {
                Center = new Point(ActualWidth / 2.0, ActualHeight / 2.0),
                Radius = r,
                IsValid = true
            };

            // 1. Update Track geometry
            _trackPath.Data = BuildArcPathGeometry(_geometry, 359.999);

            // 2. Update Loading geometry (static 90 degrees)
            _loadingArcPath.Data = BuildArcPathGeometry(_geometry, 90);
            
            // 3. Sync rotation center for the loading arc
            _loaderTransform.CenterX = _geometry.Center.X;
            _loaderTransform.CenterY = _geometry.Center.Y;

            // 4. Force active arc redraw if ready
            if (_state == CircularScoreState.Ready)
            {
                _activeArcPath.Data = BuildArcPathGeometry(_geometry, Math.Max(0.001, Math.Min(_currentScoreAngle, 359.999)));
            }

            // Now that geometry is valid, we can safely apply state visibility and animation
            EvaluateState();
        }

        private Geometry BuildArcPathGeometry(GaugeGeometry geo, double sweepAngle)
        {
            if (!geo.IsValid) return Geometry.Empty;

            double startAngle = -90; // Top
            double endAngle = startAngle + sweepAngle;

            double startRad = startAngle * Math.PI / 180;
            double endRad = endAngle * Math.PI / 180;

            double x1 = geo.Center.X + geo.Radius * Math.Cos(startRad);
            double y1 = geo.Center.Y + geo.Radius * Math.Sin(startRad);
            double x2 = geo.Center.X + geo.Radius * Math.Cos(endRad);
            double y2 = geo.Center.Y + geo.Radius * Math.Sin(endRad);

            bool isLarge = sweepAngle > 180;

            var geom = new StreamGeometry();
            using (var ctx = geom.Open())
            {
                ctx.BeginFigure(new Point(x1, y1), false, false);
                if (sweepAngle >= 359.9)
                {
                    double midRad = (startAngle + 180) * Math.PI / 180;
                    double midX = geo.Center.X + geo.Radius * Math.Cos(midRad);
                    double midY = geo.Center.Y + geo.Radius * Math.Sin(midRad);
                    ctx.ArcTo(new Point(midX, midY), new Size(geo.Radius, geo.Radius), 0, false, SweepDirection.Clockwise, true, false);
                    ctx.ArcTo(new Point(x2, y2), new Size(geo.Radius, geo.Radius), 0, false, SweepDirection.Clockwise, true, false);
                }
                else
                {
                    ctx.ArcTo(new Point(x2, y2), new Size(geo.Radius, geo.Radius), 0, isLarge, SweepDirection.Clockwise, true, false);
                }
            }
            geom.Freeze();
            return geom;
        }

        // ── State Machine ─────────────────────────────────────────────
        private static void OnIsLoadingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CircularScore cs)
                cs.EvaluateState();
        }

        private static void OnScoreChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CircularScore cs && cs._state == CircularScoreState.Ready)
                cs.AnimateToScore(cs.Score);
        }

        private static void OnScoreLabelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CircularScore cs && cs._labelText != null)
                cs._labelText.Text = (string)e.NewValue;
        }

        private void EvaluateState()
        {
            if (!IsLoaded) return;
            
            // CRITICAL: Order of initialization -> Geometry FIRST, Visibility SECOND
            if (!_geometry.IsValid) return;

            CircularScoreState targetState = IsLoading ? CircularScoreState.Loading : CircularScoreState.Ready;
            
            if (_state != targetState)
            {
                SetState(targetState);
            }
        }

        private void SetState(CircularScoreState newState)
        {
            _state = newState;
            StopAnimation();

            // Reset all visibility atomically
            _trackPath.Visibility = Visibility.Visible;
            
            if (_state == CircularScoreState.Loading)
            {
                _activeArcPath.Visibility = Visibility.Collapsed;
                _centerPanel.Visibility = Visibility.Collapsed;
                
                // Set loading arc visibility AFTER geometry is valid (which is guaranteed here)
                _loadingArcPath.Visibility = Visibility.Visible;
                
                _loadingAngle = 0;
                _loaderTransform.Angle = 0;
                
                StartAnimation();
            }
            else if (_state == CircularScoreState.Ready)
            {
                _loadingArcPath.Visibility = Visibility.Collapsed;
                
                _activeArcPath.Visibility = Visibility.Visible;
                _centerPanel.Visibility = Visibility.Visible;
                
                _currentScoreAngle = 0;
                _startScoreAngle = 0;
                
                if (_scoreText != null) _scoreText.Text = "0";
                if (_labelText != null) _labelText.Text = ScoreLabel;

                AnimateToScore(Score);
            }
        }

        // ── Rendering Loop ────────────────────────────────────────────
        private void StartAnimation()
        {
            if (!_isRendering)
            {
                CompositionTarget.Rendering += OnRenderFrame;
                _isRendering = true;
                _animationStartTime = DateTime.Now;
            }
        }

        private void StopAnimation()
        {
            if (_isRendering)
            {
                CompositionTarget.Rendering -= OnRenderFrame;
                _isRendering = false;
            }
        }

        private void AnimateToScore(double targetScore)
        {
            if (_state != CircularScoreState.Ready || !_geometry.IsValid) return;

            _targetScoreAngle = (targetScore / Math.Max(MaxScore, 1.0)) * 360.0;
            if (_targetScoreAngle < 0.001) _targetScoreAngle = 0.001;

            _startScoreAngle = _currentScoreAngle;
            
            if (_labelText != null) _labelText.Text = ScoreLabel;

            _animationStartTime = DateTime.Now;
            StartAnimation();
        }

        private void OnRenderFrame(object sender, EventArgs e)
        {
            if (!_geometry.IsValid) return;

            if (_state == CircularScoreState.Loading)
            {
                // Smooth rotation using RotateTransform (NO GEOMETRY REBUILD)
                double elapsedSec = (DateTime.Now - _animationStartTime).TotalSeconds;
                _animationStartTime = DateTime.Now;
                
                _loadingAngle = (_loadingAngle + (elapsedSec * 300.0)) % 360;
                _loaderTransform.Angle = _loadingAngle;
            }
            else if (_state == CircularScoreState.Ready)
            {
                // Score animation requires rebuilding path geometry as it grows
                double elapsedMs = (DateTime.Now - _animationStartTime).TotalMilliseconds;
                double progress = Math.Min(elapsedMs / AnimationDurationMs, 1.0);
                
                double t = 1 - Math.Pow(1 - progress, 3); // Cubic ease-out

                _currentScoreAngle = _startScoreAngle + (_targetScoreAngle - _startScoreAngle) * t;
                
                double targetScore = (_targetScoreAngle / 360.0) * MaxScore;
                double startScore = (_startScoreAngle / 360.0) * MaxScore;
                double currentScore = startScore + (targetScore - startScore) * t;

                _activeArcPath.Data = BuildArcPathGeometry(_geometry, Math.Max(0.001, Math.Min(_currentScoreAngle, 359.999)));

                if (_scoreText != null)
                    _scoreText.Text = ((int)currentScore).ToString();

                if (progress >= 1.0)
                {
                    StopAnimation();
                    if (_scoreText != null) _scoreText.Text = ((int)targetScore).ToString();
                    _currentScoreAngle = _targetScoreAngle;
                }
            }
            else
            {
                StopAnimation();
            }
        }
    }
}

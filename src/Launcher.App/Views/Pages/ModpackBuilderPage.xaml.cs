using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Launcher.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace Launcher.App.Views.Pages;

public partial class ModpackBuilderPage : INavigableView<ModpackBuilderViewModel>
{
    // The dot-field: a static grid of dots that brighten near the cursor (a WPF take on reactbits'
    // dot background). We keep the ellipses and their centres side-by-side for cheap distance checks.
    private readonly List<Ellipse> _dots = [];
    private readonly List<Point> _dotCenters = [];
    private static readonly Color DotColor = Color.FromRgb(0x7C, 0x6A, 0xB0);
    private const double DotBaseOpacity = 0.16;
    private const double DotBaseSize = 3.0;

    // The orbit: four nodes revolve around the hub. A timer advances the angle; hovering the cluster
    // pauses it so a node is easy to click.
    private readonly DispatcherTimer _orbitTimer;
    private double _orbitAngle;
    private bool _autoRotate = true;
    private const double OrbitRadius = 150;

    public ModpackBuilderViewModel ViewModel { get; }

    public ModpackBuilderPage(ModpackBuilderViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();

        _orbitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _orbitTimer.Tick += (_, _) =>
        {
            if (_autoRotate)
            {
                _orbitAngle = (_orbitAngle + 0.25) % 360;
                UpdateOrbit();
            }
        };

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += (_, _) => BuildDotField();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Give the orbital canvas the whole width — hide the docked account/friends/news column while
        // this page is up (restored on the way out).
        App.GetService<MainWindowViewModel>().IsRightPanelVisible = false;
        BuildDotField();
        UpdateOrbit();
        _orbitTimer.Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _orbitTimer.Stop();
        App.GetService<MainWindowViewModel>().IsRightPanelVisible = true;
    }

    private void OnOrbitMouseEnter(object sender, MouseEventArgs e) => _autoRotate = false;

    private void OnOrbitMouseLeave(object sender, MouseEventArgs e) => _autoRotate = true;

    /// <summary>Places each node on the circle at its base angle + the current rotation, and fades/orders
    /// them front-to-back so the ring reads as a real orbit.</summary>
    private void UpdateOrbit()
    {
        // Create top, Моды right, Играть bottom, Конфиги left.
        PlaceNode(NodeCreate, TCreate, -90);
        PlaceNode(NodeMods, TMods, 0);
        PlaceNode(NodePlay, TPlay, 90);
        PlaceNode(NodeConfigs, TConfigs, 180);
    }

    private void PlaceNode(UIElement node, TranslateTransform transform, double baseAngle)
    {
        var rad = (baseAngle + _orbitAngle) * Math.PI / 180.0;
        transform.X = OrbitRadius * Math.Cos(rad);
        transform.Y = OrbitRadius * Math.Sin(rad);

        // Front of the orbit (bottom, sin > 0) is brighter and drawn on top.
        var sin = Math.Sin(rad);
        node.Opacity = 0.55 + 0.45 * ((1 + sin) / 2);
        Panel.SetZIndex(node, 20 + (int)Math.Round(10 * sin));
    }

    private void BuildDotField()
    {
        if (DotCanvas is null)
        {
            return;
        }

        DotCanvas.Children.Clear();
        _dots.Clear();
        _dotCenters.Clear();

        double w = ActualWidth, h = ActualHeight;
        if (w < 1 || h < 1)
        {
            return;
        }

        const double spacing = 34;
        var brush = new SolidColorBrush(DotColor);
        for (var y = spacing / 2; y < h; y += spacing)
        {
            for (var x = spacing / 2; x < w; x += spacing)
            {
                var dot = new Ellipse
                {
                    Width = DotBaseSize,
                    Height = DotBaseSize,
                    Fill = brush,
                    Opacity = DotBaseOpacity,
                };
                Canvas.SetLeft(dot, x - DotBaseSize / 2);
                Canvas.SetTop(dot, y - DotBaseSize / 2);
                DotCanvas.Children.Add(dot);
                _dots.Add(dot);
                _dotCenters.Add(new Point(x, y));
            }
        }
    }

    private void OnRootMouseMove(object sender, MouseEventArgs e)
    {
        if (_dots.Count == 0)
        {
            return;
        }

        var p = e.GetPosition(DotCanvas);
        const double radius = 140;
        const double radius2 = radius * radius;

        for (var i = 0; i < _dots.Count; i++)
        {
            var c = _dotCenters[i];
            double dx = c.X - p.X, dy = c.Y - p.Y;
            var d2 = dx * dx + dy * dy;
            var dot = _dots[i];

            if (d2 < radius2)
            {
                var t = 1 - Math.Sqrt(d2) / radius; // 1 at the cursor, 0 at the edge of the glow
                dot.Opacity = DotBaseOpacity + t * 0.8;
                var size = DotBaseSize + t * 3.2;
                dot.Width = dot.Height = size;
                Canvas.SetLeft(dot, c.X - size / 2);
                Canvas.SetTop(dot, c.Y - size / 2);
            }
            else if (dot.Opacity > DotBaseOpacity)
            {
                dot.Opacity = DotBaseOpacity;
                dot.Width = dot.Height = DotBaseSize;
                Canvas.SetLeft(dot, c.X - DotBaseSize / 2);
                Canvas.SetTop(dot, c.Y - DotBaseSize / 2);
            }
        }
    }
}

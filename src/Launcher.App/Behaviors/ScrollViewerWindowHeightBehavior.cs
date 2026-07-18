using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Launcher.App.Behaviors;

/// <summary>
/// Constrains an element's Height to the hosting NavigationView's ActualHeight — apply to a page's root
/// panel (ScrollViewer, or a Grid that itself contains the real scroll region further down).
///
/// Why this is needed: pages are hosted inside NavigationView's internal Frame, and Frame-navigated
/// content sits outside the normal visual/logical ancestry that RelativeSource/ElementName bindings walk
/// — a binding like "{RelativeSource AncestorType=ui:NavigationView}" or "{ElementName=RootNavigation}"
/// written inside a Page's XAML silently fails to resolve. Without a bounded Height, a root ScrollViewer
/// (or a Grid with a "*" row wrapping one) measures with infinite available height and just grows to fit
/// everything instead of clipping/scrolling — the page never gets a scrollbar and mouse-wheel input has
/// nothing to act on.
///
/// Window.GetWindow() and FrameworkElement.FindName() both work here because they don't rely on
/// visual-tree ancestry — GetWindow follows the PresentationSource, and FindName looks the element up in
/// the Window's NameScope directly.
/// </summary>
public static class ScrollViewerWindowHeightBehavior
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(ScrollViewerWindowHeightBehavior), new PropertyMetadata(false, OnEnabledChanged));

    /// <summary>
    /// Pixels to subtract from the NavigationView height — use it for the element's own vertical
    /// margins (e.g. a root Grid with Margin="40,32,40,32" needs Offset="64") so the bottom edge
    /// doesn't run past the window.
    /// </summary>
    public static readonly DependencyProperty OffsetProperty = DependencyProperty.RegisterAttached(
        "Offset", typeof(double), typeof(ScrollViewerWindowHeightBehavior), new PropertyMetadata(0d));

    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);

    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);

    public static void SetOffset(DependencyObject element, double value) => element.SetValue(OffsetProperty, value);

    public static double GetOffset(DependencyObject element) => (double)element.GetValue(OffsetProperty);

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element || e.NewValue is not true)
        {
            return;
        }

        element.Loaded += ElementOnLoaded;
    }

    private static void ElementOnLoaded(object sender, RoutedEventArgs e)
    {
        var element = (FrameworkElement)sender;

        var window = Window.GetWindow(element);
        if (window?.FindName("RootNavigation") is not FrameworkElement navigationView)
        {
            return;
        }

        var binding = new Binding(nameof(FrameworkElement.ActualHeight))
        {
            Source = navigationView,
            Mode = BindingMode.OneWay,
            Converter = SubtractConverter.Instance,
            ConverterParameter = GetOffset(element),
        };
        element.SetBinding(FrameworkElement.HeightProperty, binding);
    }

    private sealed class SubtractConverter : IValueConverter
    {
        public static readonly SubtractConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var height = value is double d ? d : 0d;
            var offset = parameter is double o ? o : 0d;
            return Math.Max(0d, height - offset);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}

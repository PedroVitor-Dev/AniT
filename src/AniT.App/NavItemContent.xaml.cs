using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AniT.App;

public partial class NavItemContent : UserControl
{
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label),
        typeof(string),
        typeof(NavItemContent),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IconDataProperty = DependencyProperty.Register(
        nameof(IconData),
        typeof(Geometry),
        typeof(NavItemContent),
        new PropertyMetadata(Geometry.Empty));

    public NavItemContent()
    {
        InitializeComponent();
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public Geometry IconData
    {
        get => (Geometry)GetValue(IconDataProperty);
        set => SetValue(IconDataProperty, value);
    }
}

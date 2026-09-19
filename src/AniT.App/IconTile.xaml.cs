using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AniT.App;

public partial class IconTile : UserControl
{
    public static readonly DependencyProperty IconDataProperty = DependencyProperty.Register(
        nameof(IconData), typeof(Geometry), typeof(IconTile), new PropertyMetadata(Geometry.Empty));

    public static readonly DependencyProperty TileBackgroundProperty = DependencyProperty.Register(
        nameof(TileBackground), typeof(Brush), typeof(IconTile), new PropertyMetadata(new SolidColorBrush(Color.FromArgb(90, 22, 60, 103))));

    public static readonly DependencyProperty TileBorderBrushProperty = DependencyProperty.Register(
        nameof(TileBorderBrush), typeof(Brush), typeof(IconTile), new PropertyMetadata(new SolidColorBrush(Color.FromArgb(160, 42, 103, 157))));

    public IconTile()
    {
        InitializeComponent();
    }

    public Geometry IconData
    {
        get => (Geometry)GetValue(IconDataProperty);
        set => SetValue(IconDataProperty, value);
    }

    public Brush TileBackground
    {
        get => (Brush)GetValue(TileBackgroundProperty);
        set => SetValue(TileBackgroundProperty, value);
    }

    public Brush TileBorderBrush
    {
        get => (Brush)GetValue(TileBorderBrushProperty);
        set => SetValue(TileBorderBrushProperty, value);
    }
}

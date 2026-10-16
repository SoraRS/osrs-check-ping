using System.Windows;
using System.Windows.Controls;

namespace OsrsWorldPing.Desktop;

public partial class WorldTile : UserControl
{
    public static readonly DependencyProperty EditableProperty = DependencyProperty.Register(nameof(Editable), typeof(bool), typeof(WorldTile), new PropertyMetadata(false));
    public bool Editable { get => (bool)GetValue(EditableProperty); set => SetValue(EditableProperty, value); }
    public WorldTile() => InitializeComponent();
}

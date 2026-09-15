using Avalonia;
using Avalonia.Controls;

namespace LumeFetch.Presentation.Controls;

/// <summary>
/// Retains the desktop grid and reflows its children, in source order, on narrow screens.
/// No controls are replaced, so selection, focus and commands survive rotation/resizing.
/// </summary>
public sealed class AdaptiveGrid : Grid
{
    public static readonly StyledProperty<double> BreakpointProperty =
        AvaloniaProperty.Register<AdaptiveGrid, double>(nameof(Breakpoint), 560);
    public static readonly StyledProperty<int> CompactColumnCountProperty =
        AvaloniaProperty.Register<AdaptiveGrid, int>(nameof(CompactColumnCount), 1,
            validate: value => value > 0);
    public static readonly AttachedProperty<int> CompactColumnSpanProperty =
        AvaloniaProperty.RegisterAttached<AdaptiveGrid, Control, int>("CompactColumnSpan", 1,
            validate: value => value > 0);

    private ColumnDefinitions? _wideColumns;
    private RowDefinitions? _wideRows;
    private readonly Dictionary<Control, (int Row, int Column, int RowSpan, int ColumnSpan)> _widePositions = [];
    private bool _compact;
    private int _compactColumns;
    private int _compactChildren;
    private double _wideRowSpacing;

    static AdaptiveGrid() => AffectsMeasure<AdaptiveGrid>(BreakpointProperty, CompactColumnCountProperty);

    public double Breakpoint { get => GetValue(BreakpointProperty); set => SetValue(BreakpointProperty, value); }
    public int CompactColumnCount { get => GetValue(CompactColumnCountProperty); set => SetValue(CompactColumnCountProperty, value); }
    public static int GetCompactColumnSpan(Control control) => control.GetValue(CompactColumnSpanProperty);
    public static void SetCompactColumnSpan(Control control, int value) => control.SetValue(CompactColumnSpanProperty, value);

    protected override Size MeasureOverride(Size constraint)
    {
        var compact = constraint.Width < Breakpoint;
        if (compact && (!_compact || _compactColumns != CompactColumnCount || _compactChildren != Children.Count))
        {
            if (!_compact)
            {
                _wideColumns = ColumnDefinitions;
                _wideRows = RowDefinitions;
                _wideRowSpacing = RowSpacing;
                _widePositions.Clear();
            }
            foreach (var child in Children)
                _widePositions.TryAdd(child, (GetRow(child), GetColumn(child), GetRowSpan(child), GetColumnSpan(child)));
            ColumnDefinitions = new ColumnDefinitions(string.Join(",", Enumerable.Repeat("*", CompactColumnCount)));
            var row = 0;
            var column = 0;
            foreach (var child in Children)
            {
                var span = Math.Min(GetCompactColumnSpan(child), CompactColumnCount);
                if (column + span > CompactColumnCount) { row++; column = 0; }
                SetRow(child, row);
                SetColumn(child, column);
                SetRowSpan(child, 1);
                SetColumnSpan(child, span);
                column += span;
                if (column == CompactColumnCount) { row++; column = 0; }
            }
            RowDefinitions = new RowDefinitions(string.Join(",", Enumerable.Repeat("Auto", row + (column > 0 ? 1 : 0))));
            _compactColumns = CompactColumnCount;
            _compactChildren = Children.Count;
            RowSpacing = 10;
        }
        else if (!compact && _compact)
        {
            ColumnDefinitions = _wideColumns!;
            RowDefinitions = _wideRows!;
            RowSpacing = _wideRowSpacing;
            foreach (var (child, position) in _widePositions)
            {
                SetRow(child, position.Row);
                SetColumn(child, position.Column);
                SetRowSpan(child, position.RowSpan);
                SetColumnSpan(child, position.ColumnSpan);
            }
        }
        _compact = compact;
        return base.MeasureOverride(constraint);
    }
}

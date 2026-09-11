using System.ComponentModel;
using System.Windows.Media;
using MSS60_DataLogger.Diagnostics;

namespace MSS60_DataLogger;

/// <summary>
/// 左側の数値リスト 1 行分。値だけが頻繁に更新されるので、そこだけ変更通知する。
/// </summary>
public sealed class MeasurementRow(MeasurementDefinition definition, Brush seriesBrush) : INotifyPropertyChanged
{
    private double? _value;

    public event PropertyChangedEventHandler? PropertyChanged;

    public MeasurementDefinition Definition { get; } = definition;

    public string Name => Definition.DisplayName;

    public string Unit => Definition.Unit;

    public Brush SeriesBrush { get; } = seriesBrush;

    public string ToolTipText => Definition.TechnicalName;

    public double? Value
    {
        get => _value;
        set
        {
            if (_value.Equals(value))
            {
                return;
            }

            _value = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FormattedValue)));
        }
    }

    public string FormattedValue => _value is { } value ? MeasurementFormat.Format(value) : "—";
}

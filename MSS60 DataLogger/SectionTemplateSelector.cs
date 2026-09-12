using System.Windows;
using System.Windows.Controls;

namespace MSS60_DataLogger;

/// <summary>
/// カテゴリ内の区画(<see cref="MeasurementSection"/>)を、名前付きならプルダウン(入れ子の Expander)、
/// 無名ならそのままの項目一覧として描き分ける。
/// </summary>
public sealed class SectionTemplateSelector : DataTemplateSelector
{
    public DataTemplate? NamedTemplate { get; set; }

    public DataTemplate? UnlabeledTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object? item, DependencyObject container) =>
        item is MeasurementSection { HasName: true } ? NamedTemplate : UnlabeledTemplate;
}

using System.Collections.ObjectModel;
using System.ComponentModel;
using MSS60_DataLogger.Diagnostics;

namespace MSS60_DataLogger;

/// <summary>「項目」タブの 1 行。チェックが入ったものだけがログ対象になる。</summary>
public sealed class SelectableMeasurement(MeasurementDefinition definition, Action<SelectableMeasurement> onToggled)
    : INotifyPropertyChanged
{
    private bool _isSelected;

    public event PropertyChangedEventHandler? PropertyChanged;

    public MeasurementDefinition Definition { get; } = definition;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            onToggled(this);
        }
    }

    /// <summary>チェックの取り消しを画面に反映させずに戻したいときに使う。</summary>
    public void SetSelectedSilently(bool value)
    {
        _isSelected = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
    }
}

/// <summary>「項目」タブのカテゴリ 1 つ分。</summary>
public sealed class MeasurementGroup : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _isVisible = true;

    public MeasurementGroup(string name, IReadOnlyList<SelectableMeasurement> items)
    {
        Name = name;
        AllItems = items;
        VisibleItems = [.. items];
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; }

    public IReadOnlyList<SelectableMeasurement> AllItems { get; }

    public ObservableCollection<SelectableMeasurement> VisibleItems { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
            {
                return;
            }

            _isExpanded = value;
            Notify(nameof(IsExpanded));
        }
    }

    public bool IsVisible
    {
        get => _isVisible;
        private set
        {
            if (_isVisible == value)
            {
                return;
            }

            _isVisible = value;
            Notify(nameof(IsVisible));
        }
    }

    /// <summary>「基本 (2/14)」のような見出し。</summary>
    public string Header
    {
        get
        {
            int selected = AllItems.Count(i => i.IsSelected);
            return selected > 0
                ? $"{Name}  ({selected}/{AllItems.Count})"
                : $"{Name}  ({AllItems.Count})";
        }
    }

    public void RefreshHeader() => Notify(nameof(Header));

    /// <summary>検索語で表示項目を絞り込む。語が空ならすべて表示する。</summary>
    public void ApplyFilter(string query)
    {
        VisibleItems.Clear();

        if (query.Length == 0)
        {
            foreach (SelectableMeasurement item in AllItems)
            {
                VisibleItems.Add(item);
            }

            IsVisible = true;
            return;
        }

        foreach (SelectableMeasurement item in AllItems.Where(i => i.Definition.SearchKey.Contains(query)))
        {
            VisibleItems.Add(item);
        }

        IsVisible = VisibleItems.Count > 0;
        if (IsVisible)
        {
            IsExpanded = true; // 一致があるカテゴリは自動で開く
        }
    }

    private void Notify(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

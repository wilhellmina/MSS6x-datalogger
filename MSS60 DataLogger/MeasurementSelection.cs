using System.Collections.ObjectModel;
using System.ComponentModel;
using MSS60_DataLogger.Diagnostics;

namespace MSS60_DataLogger;

/// <summary>「項目」タブの 1 行。チェックが入ったものだけがログ対象になる。</summary>
public sealed class SelectableMeasurement(
    MeasurementDefinition definition,
    Action<SelectableMeasurement> onToggled,
    Action<SelectableMeasurement> onFavoriteToggled)
    : INotifyPropertyChanged
{
    private bool _isSelected;
    private bool _isFavorite;

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

    /// <summary>お気に入り登録の有無。記録用の選択(<see cref="IsSelected"/>)とは独立していて、
    /// お気に入りに入っていても未選択、選択中でもお気に入りに入っていない、のどちらもあり得る。</summary>
    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (_isFavorite == value)
            {
                return;
            }

            _isFavorite = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFavorite)));
            onFavoriteToggled(this);
        }
    }

    /// <summary>チェックの取り消しを画面に反映させずに戻したいときに使う。</summary>
    public void SetSelectedSilently(bool value)
    {
        _isSelected = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
    }
}

/// <summary>カテゴリ内でさらに小分けした区画 1 つ分。<see cref="Name"/> が空文字なら見出しを出さずそのまま並べる。
/// 名前付きの区画は画面上で開閉できるプルダウン(入れ子の Expander)として表示する。</summary>
public sealed class MeasurementSection : INotifyPropertyChanged
{
    private bool _isExpanded;

    public MeasurementSection(string name, IReadOnlyList<SelectableMeasurement> items)
    {
        Name = name;
        AllItems = items;
        VisibleItems = [.. items];
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; }

    public bool HasName => Name.Length > 0;

    public IReadOnlyList<SelectableMeasurement> AllItems { get; }

    public ObservableCollection<SelectableMeasurement> VisibleItems { get; }

    public bool IsVisible => VisibleItems.Count > 0;

    /// <summary>名前付き区画(プルダウン)の開閉状態。無名の区画では使わない。</summary>
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
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }

    /// <summary>検索語で表示項目を絞り込む。語が空ならすべて表示する。</summary>
    public void ApplyFilter(string query)
    {
        VisibleItems.Clear();

        IEnumerable<SelectableMeasurement> matches = query.Length == 0
            ? AllItems
            : AllItems.Where(i => i.Definition.SearchKey.Contains(query));

        foreach (SelectableMeasurement item in matches)
        {
            VisibleItems.Add(item);
        }

        if (VisibleItems.Count > 0 && query.Length > 0)
        {
            IsExpanded = true; // 一致があるプルダウンは自動で開く
        }
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
        Sections = BuildSections(items);
        VisibleSections = [.. Sections];
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; }

    public IReadOnlyList<SelectableMeasurement> AllItems { get; }

    /// <summary>見出しなしの区画(先頭)と、名前付きの小分け区画からなる一覧。</summary>
    public IReadOnlyList<MeasurementSection> Sections { get; }

    public ObservableCollection<MeasurementSection> VisibleSections { get; }

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

    /// <summary>1 件でも選択済みの項目があるか。全解除ボタンを操作できるかの判定に使う。</summary>
    public bool HasAnySelected => AllItems.Any(i => i.IsSelected);

    /// <summary>見出しと、全解除ボタンの有効状態をまとめて更新する。</summary>
    public void RefreshHeader()
    {
        Notify(nameof(Header));
        Notify(nameof(HasAnySelected));
    }

    /// <summary>検索語で表示項目を絞り込む。語が空ならすべて表示する。</summary>
    public void ApplyFilter(string query)
    {
        VisibleSections.Clear();

        foreach (MeasurementSection section in Sections)
        {
            section.ApplyFilter(query);
            if (section.IsVisible)
            {
                VisibleSections.Add(section);
            }
        }

        IsVisible = VisibleSections.Count > 0;
        if (IsVisible && query.Length > 0)
        {
            IsExpanded = true; // 一致があるカテゴリは自動で開く
        }
    }

    /// <summary>小分け先(SubCategory)が無い項目をまとめた無見出し区画を先頭に、
    /// 名前付きの小分けはその後ろに続ける。</summary>
    private static IReadOnlyList<MeasurementSection> BuildSections(IReadOnlyList<SelectableMeasurement> items)
    {
        List<MeasurementSection> sections = [];

        List<SelectableMeasurement> unlabeled = [.. items.Where(i => i.Definition.SubCategory.Length == 0)];
        if (unlabeled.Count > 0)
        {
            sections.Add(new MeasurementSection(string.Empty, unlabeled));
        }

        foreach (IGrouping<string, SelectableMeasurement> named in items
            .Where(i => i.Definition.SubCategory.Length > 0)
            .GroupBy(i => i.Definition.SubCategory))
        {
            sections.Add(new MeasurementSection(named.Key, [.. named]));
        }

        return sections;
    }

    private void Notify(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

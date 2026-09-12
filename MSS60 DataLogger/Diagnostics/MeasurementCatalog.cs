using System.IO;
using System.Reflection;

namespace MSS60_DataLogger.Diagnostics;

/// <summary>
/// 埋め込みリソースの CSV から測定値の一覧を読み込む。
/// </summary>
public static class MeasurementCatalog
{
    private const string ResourceName = "MSS60_DataLogger.Resources.Messwerte.csv";

    /// <summary>ジョブ 1 回で要求できる測定値の最大数(SGBD の仕様による)。</summary>
    public const int MaxSelectableCount = 42;

    /// <summary>初期選択される項目(短縮名)。</summary>
    public static readonly string[] DefaultSelection =
    [
        "N", "GANG", "LAM_IST_MW_B1", "LAM_IST_MW_B2", "OZ_ANZEIGE_KOMBI",
        "TUMG", "PUMG", "TOEL", "PWG", "TMOT",
    ];

    private static IReadOnlyList<MeasurementDefinition>? _cache;

    /// <summary>全測定値定義。結果名が重複するエイリアスは最初の 1 件だけを残す。</summary>
    public static IReadOnlyList<MeasurementDefinition> All => _cache ??= Load();

    public static MeasurementDefinition? Find(string arg) =>
        All.FirstOrDefault(d => string.Equals(d.Arg, arg, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<MeasurementDefinition> Load()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"埋め込みリソース '{ResourceName}' が見つかりません。");
        using var reader = new StreamReader(stream);

        List<MeasurementDefinition> definitions = [];
        HashSet<string> seenResultNames = [];

        _ = reader.ReadLine(); // ヘッダー行
        while (reader.ReadLine() is { } line)
        {
            string[] fields = SplitCsvLine(line);
            if (fields.Length < 11)
            {
                continue;
            }

            string arg = fields[0].Trim();
            string resultName = fields[2].Trim();
            if (arg is "" or "-" || resultName.Length == 0)
            {
                continue; // 未定義のプレースホルダー行
            }

            if (!seenResultNames.Add(resultName))
            {
                continue; // 旧ツール向けの別名。同じ値を二重に要求しても意味がないので除外する。
            }

            string unit = fields[5].Trim();
            unit = unit == "-" ? string.Empty : unit;
            string category = MeasurementCategories.Classify(arg, resultName);
            definitions.Add(new MeasurementDefinition(
                Arg: arg,
                Id: fields[1].Trim(),
                ResultName: resultName,
                Unit: unit,
                Description: fields[10].Trim(),
                Category: category,
                SubCategory: MeasurementCategories.ClassifySub(category, resultName, unit)));
        }

        return definitions
            .OrderBy(d => d.Description, NaturalStringComparer.Instance)
            .ToList();
    }

    /// <summary>ダブルクォート囲みに対応した簡易 CSV 分割。</summary>
    private static string[] SplitCsvLine(string line)
    {
        List<string> fields = [];
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return [.. fields];
    }
}

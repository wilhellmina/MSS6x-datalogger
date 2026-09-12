namespace MSS60_DataLogger.Diagnostics;

/// <summary>
/// MSS60 の測定値 1 項目の定義。SGBD の MesswerteTab テーブルから取得した内容に対応する。
/// </summary>
/// <param name="Arg">ジョブ引数に渡す短縮名(例: "N")。</param>
/// <param name="Id">SGBD 上の識別子(例: "0x5800")。</param>
/// <param name="ResultName">返ってくる結果名(例: "STAT_MOTORDREHZAHL_WERT")。</param>
/// <param name="Unit">単位(例: "U/min")。単位なしの項目は空文字。</param>
/// <param name="Description">日本語の説明。</param>
/// <param name="Category">画面上のグループ分け。</param>
/// <param name="SubCategory">カテゴリ内でさらに小分けする見出し。無ければ空文字。</param>
public sealed record MeasurementDefinition(
    string Arg,
    string Id,
    string ResultName,
    string Unit,
    string Description,
    string Category,
    string SubCategory = "")
{
    /// <summary>一覧表示や CSV のヘッダーに使う名前。</summary>
    public string DisplayName => string.IsNullOrEmpty(Description) ? Arg : Description;

    /// <summary>日本語名の裏にある、実際にジョブへ渡す短縮名と結果名。ツールチップ表示用。</summary>
    public string TechnicalName => $"{Arg} / {ResultName}";

    /// <summary>検索用に小文字化して連結した文字列。</summary>
    public string SearchKey { get; } = $"{Arg} {ResultName} {Description}".ToLowerInvariant();
}

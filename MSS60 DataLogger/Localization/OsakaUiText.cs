namespace MSS60_DataLogger.Localization;

/// <summary>お遊びの大阪弁バージョン。タブ名やカラム見出しなど、単なる名詞ラベルは
/// 標準語のままにして(XAML側で固定)、ボタン・ツールチップ・ステータスメッセージのような
/// 「アプリが喋る」部分だけを大阪弁にしている。</summary>
public sealed class OsakaUiText : IUiText
{
    public static readonly OsakaUiText Instance = new();

    private OsakaUiText()
    {
    }

    public string RefreshButton => "更新するで";

    public string ConnectButtonConnect => "つなぐで";

    public string ConnectButtonDisconnect => "切るで";

    public string StatusInitial => "まだ繋がってへんで";

    public string SearchBoxTooltip => "項目名とか短縮名で絞り込めるで";

    public string CategoryClearAllButton => "ぜんぶ消すで";

    public string CategoryClearAllTooltip => "このカテゴリで選んでるやつ、まとめて消すで";

    public string FavoriteToggleTooltip => "お気に入りやで";

    public string FavoritesClearButton => "選んでるん、ぜんぶ消すで";

    public string FavoritesEmptyText =>
        "お気に入り、まだ無いで。「項目」タブで☆をポチっとしたら、ここに出てくるようになるで。";

    public string RecordButtonStart => "● 記録すんで";

    public string RecordButtonStop => "■ もうやめとくわ";

    public string SelectComPortFirst => "COM ポート、選んでや。";

    public string SelectMeasurementFirst => "記録する項目、選んでや。";

    public string SgbdFolderNotFound(string path) => $"SGBD フォルダーが見つからへんで: {path}";

    public string ApplyingSelectionChange => "項目の変更、反映中やで…";

    public string DisconnectedNoSelection => "項目選んでへんかったから、切ったで。";

    public string CannotChangeWhileRecording => "記録中は項目変えられへんで。いっぺん記録止めてや。";

    public string MaxSelectableExceeded(int max) => $"いっぺんに記録できるんは {max} 項目までやで。";

    public string LogFileCreateFailed(string exceptionMessage) => $"ログファイル作られへんかったわ: {exceptionMessage}";

    public string Disconnected => "切ったで。";

    public string ConnectingTo(string comPort) => $"{comPort} に繋いでる最中やで…";

    public string UnsupportedMeasurementsExcluded(string names) => $"このECUでは対応してへんかったから外しといたで: {names}";

    public string UnsupportedAllMeasurements => "選んだ項目、全部このECUでは対応してへんかったわ。";

    public string CommunicationFailed(string exceptionMessage) => $"通信でコケたで: {exceptionMessage}";

    public string Retrying(string exceptionMessage) => $"もっかい試してるで…({exceptionMessage})";

    public string JobRepeatedlyFailed(string jobStatus) => $"ジョブが何回もコケとるわ (JOB_STATUS={jobStatus})。";

    public string ConnectionFailed(string exceptionMessage) => $"繋がらへんかったわ: {exceptionMessage}";

    public string Connected => "繋がったで。";

    public string SavedTo(string path) => $"保存しといたで: {path}";

    public string RowsUnit => "行";
}

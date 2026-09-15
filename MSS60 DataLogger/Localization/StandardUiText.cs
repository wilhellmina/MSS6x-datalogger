namespace MSS60_DataLogger.Localization;

/// <summary>既定の(標準語の)文字列。今まで各所に直書きしていたものをそのまま移した。</summary>
public sealed class StandardUiText : IUiText
{
    public static readonly StandardUiText Instance = new();

    private StandardUiText()
    {
    }

    public string RefreshButton => "更新";

    public string ConnectButtonConnect => "接続";

    public string ConnectButtonDisconnect => "切断";

    public string StatusInitial => "未接続";

    public string SearchBoxTooltip => "項目名・短縮名で絞り込みます";

    public string CategoryClearAllButton => "全解除";

    public string CategoryClearAllTooltip => "カテゴリ内で選択中の項目を一斉解除します";

    public string FavoriteToggleTooltip => "お気に入り";

    public string FavoritesClearButton => "選択中をすべて解除";

    public string FavoritesEmptyText =>
        "お気に入りはまだありません。「項目」タブで各行の☆をクリックすると、ここに追加されます。";

    public string RecordButtonStart => "● 記録開始";

    public string RecordButtonStop => "■ 記録停止";

    public string SelectComPortFirst => "COM ポートを選択してください。";

    public string SelectMeasurementFirst => "記録する項目を選択してください。";

    public string SgbdFolderNotFound(string path) => $"SGBD フォルダーが見つかりません: {path}";

    public string ApplyingSelectionChange => "項目の変更を反映しています…";

    public string DisconnectedNoSelection => "項目が選択されていないため切断しました。";

    public string CannotChangeWhileRecording => "記録中は項目を変更できません。いったん記録を停止してください。";

    public string MaxSelectableExceeded(int max) => $"同時に記録できるのは {max} 項目までです。";

    public string LogFileCreateFailed(string exceptionMessage) => $"ログファイルを作成できませんでした: {exceptionMessage}";

    public string Disconnected => "切断しました。";

    public string ConnectingTo(string comPort) => $"{comPort} に接続しています…";

    public string UnsupportedMeasurementsExcluded(string names) => $"この ECU では未対応のため除外しました: {names}";

    public string UnsupportedAllMeasurements => "選択した測定値がすべてこの ECU では未対応でした。";

    public string CommunicationFailed(string exceptionMessage) => $"通信に失敗しました: {exceptionMessage}";

    public string Retrying(string exceptionMessage) => $"再試行しています… ({exceptionMessage})";

    public string JobRepeatedlyFailed(string jobStatus) => $"ジョブが繰り返し失敗しました (JOB_STATUS={jobStatus})。";

    public string ConnectionFailed(string exceptionMessage) => $"接続に失敗しました: {exceptionMessage}";

    public string Connected => "接続しました。";

    public string SavedTo(string path) => $"保存しました: {path}";

    public string RowsUnit => "行";
}

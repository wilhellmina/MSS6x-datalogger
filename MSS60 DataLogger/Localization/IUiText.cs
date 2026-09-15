namespace MSS60_DataLogger.Localization;

/// <summary>
/// アプリの「声」に相当する文字列(ボタン・ツールチップ・ステータスメッセージなど)。
/// 測定項目名やカテゴリ名などのデータ由来の文字列(埋め込みCSV由来)はここでは扱わず、
/// 常に日本語のまま(このアプリの対応言語は UI の骨格のみが対象)。
/// </summary>
public interface IUiText
{
    string RefreshButton { get; }
    string ConnectButtonConnect { get; }
    string ConnectButtonDisconnect { get; }
    string StatusInitial { get; }
    string SearchBoxTooltip { get; }
    string CategoryClearAllButton { get; }
    string CategoryClearAllTooltip { get; }
    string FavoriteToggleTooltip { get; }
    string FavoritesClearButton { get; }
    string FavoritesEmptyText { get; }
    string RecordButtonStart { get; }
    string RecordButtonStop { get; }

    string SelectComPortFirst { get; }
    string SelectMeasurementFirst { get; }
    string SgbdFolderNotFound(string path);
    string ApplyingSelectionChange { get; }
    string DisconnectedNoSelection { get; }
    string CannotChangeWhileRecording { get; }
    string MaxSelectableExceeded(int max);
    string LogFileCreateFailed(string exceptionMessage);
    string Disconnected { get; }

    string ConnectingTo(string comPort);
    string UnsupportedMeasurementsExcluded(string names);
    string UnsupportedAllMeasurements { get; }
    string CommunicationFailed(string exceptionMessage);
    string Retrying(string exceptionMessage);
    string JobRepeatedlyFailed(string jobStatus);
    string ConnectionFailed(string exceptionMessage);
    string Connected { get; }

    string SavedTo(string path);

    string RowsUnit { get; }
}

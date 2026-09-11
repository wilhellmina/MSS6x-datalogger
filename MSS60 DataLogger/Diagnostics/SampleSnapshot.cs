namespace MSS60_DataLogger.Diagnostics;

/// <summary>
/// ECU から 1 回読み出した測定値のまとまり。
/// <see cref="Values"/> は <see cref="EcuSampler"/> 開始時に渡した項目リストと同じ並び。
/// 値が取得できなかった項目は null。
/// </summary>
/// <param name="Timestamp">読み出しが完了した時刻。</param>
/// <param name="Values">項目ごとの値。</param>
public sealed record SampleSnapshot(DateTime Timestamp, double?[] Values);

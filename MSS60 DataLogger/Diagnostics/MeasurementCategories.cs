namespace MSS60_DataLogger.Diagnostics;

/// <summary>
/// 測定値をカテゴリに振り分ける。SGBD 側にカテゴリ情報はないため、
/// 結果名(ドイツ語)に含まれる語で判定する。上から順に最初に一致したものを採用する。
/// </summary>
public static class MeasurementCategories
{
    public const string Basic = "基本";
    public const string Ignition = "点火・ノック";
    public const string Fuel = "燃調・ラムダ";
    public const string Vanos = "VANOS・カムシャフト";
    public const string Intake = "吸気・スロットル";
    public const string Exhaust = "排気・触媒";
    public const string FuelSupply = "燃料供給・タンク";
    public const string Electrical = "電気・電源";
    public const string Driveline = "トルク・駆動系";
    public const string Temperature = "温度";
    public const string Pressure = "圧力";
    public const string Diagnostics = "診断・状態";
    public const string Other = "その他";

    /// <summary>サブカテゴリ名: 物理量に換算する前の生データ(ROHWERT・AD_WANDLER)。カテゴリを問わず使う。</summary>
    private const string RawSub = "生の値";

    /// <summary>画面に出す順序。</summary>
    public static readonly string[] DisplayOrder =
    [
        Basic, Ignition, Fuel, Intake, Vanos, Exhaust,
        FuelSupply, Driveline, Temperature, Pressure, Electrical, Diagnostics, Other,
    ];

    /// <summary>走行中によく見る項目。カテゴリ判定より優先する。</summary>
    private static readonly HashSet<string> BasicArgs =
    [
        "N", "TMOT", "TOEL", "UB", "RF_M", "ML_GES", "GANG", "PWG",
        "V_VORNE", "V_HINTEN", "TAN_B1", "TAN_B2", "TIME_ML", "ZUSTAND_MOTOR",
    ];

    private static readonly (string Keyword, string Category)[] Rules =
    [
        // 点火・ノック(失火検知のイオン電流系もここに含める)
        ("ZUEND", Ignition),
        ("KLOPF", Ignition),
        ("FUNKEN", Ignition),
        ("IONEN", Ignition),
        ("AUSSETZER", Ignition),
        ("ZUENDKERZ", Ignition),

        // 燃調・ラムダ
        ("LAMBDA", Fuel),
        ("SONDE", Fuel),
        ("TRIMM", Fuel),
        ("EINSPRITZ", Fuel),
        ("WANDFILM", Fuel),
        ("GEMISCH", Fuel),
        ("INTEGRATOR", Fuel), // ラムダ制御の積分器(旧ツール向けの別名)

        // VANOS・カムシャフト
        ("VANOS", Vanos),
        ("NOCKENWELLE", Vanos),

        // 吸気・スロットル
        ("DROSSELKLAPPE", Intake),
        ("LEERLAUF", Intake),
        ("LUFTMASSE", Intake),
        ("SAUGROHR", Intake),
        ("FUELLUNG", Intake),
        ("LLS_", Intake),

        // 排気・触媒(二次空気もここ)
        ("ABGAS", Exhaust),
        ("KAT", Exhaust),
        ("SEKUNDAERLUFT", Exhaust),

        // 燃料供給・タンク
        ("KRAFTSTOFF", FuelSupply),
        ("TANK", FuelSupply),
        ("DMTL", FuelSupply),
        ("FUELLSTAND", FuelSupply),
        ("EKP", FuelSupply),

        // 電気・電源
        ("SPANNUNG", Electrical),
        ("STROM", Electrical),
        ("BATTERIE", Electrical),
        ("GENERATOR", Electrical),
        ("IBS", Electrical),
        ("RUHESTROM", Electrical),
        ("ELEKTROLUEFTER", Electrical),
        ("UBATT", Electrical),

        // トルク・駆動系
        ("MOMENT", Driveline),
        ("GETRIEBE", Driveline),
        ("KUPPLUNG", Driveline),
        ("SMG", Driveline),
        ("FAHRERWUNSCH", Driveline),
        ("PEDALWERTGEBER", Driveline),
        ("PWG", Driveline),
        ("GANG", Driveline),
        ("GESCHWINDIGKEIT", Driveline),
        ("BESCHLEUNIGUNG", Driveline),
        ("KLIMA", Driveline), // エアコンコンプレッサーの負荷トルク

        // 温度・圧力(上のどれにも当てはまらなかったもの)
        ("TEMPERATUR", Temperature),
        ("DRUCK", Pressure),

        // 診断・状態
        ("FEHLER", Diagnostics),
        ("DIAGNOSE", Diagnostics),
        ("ZUSTAND", Diagnostics),
        ("UEBERWACHUNG", Diagnostics),
        ("SICHERHEIT", Diagnostics),
        ("CODIER", Diagnostics),
        ("ADAPTION", Diagnostics),
        ("OBD", Diagnostics),
        ("STATUS", Diagnostics),
        ("EWS", Diagnostics),
        ("SCHALTER", Diagnostics),
    ];

    /// <summary>カテゴリ内でさらに小分けする見出し。該当なしは空文字(見出しなしでそのまま並べる)。</summary>
    private static readonly (string Category, string Keyword, string SubCategory)[] SubRules =
    [
        (Vanos, "VENTIL-STROM", "生の値"),
    ];

    /// <summary>カテゴリを問わず「生の値」に小分けするキーワード。物理量への換算前の生データ。</summary>
    private static readonly string[] RawKeywords = ["ROHWERT", "AD_WANDLER"];

    /// <summary>単位がこれに一致する項目も、キーワードに関わらず「生の値」に小分けする。
    /// バッテリー電圧(Volt)のようにそれ自体が意味を持つ単位は含めない。</summary>
    private static readonly string[] RawUnits = ["mV", "Ohm", "Hz"];

    /// <summary>結果名に ROHWERT 等が含まれていても、通常の項目としてそのまま並べたいものの例外リスト。
    /// 例: 外気圧(PUMG_ROH)は名前に ROHWERT を含むが mbar 単位でそのまま使える値なので、
    /// 「生の値」には小分けしない。</summary>
    private static readonly string[] RawExceptions = ["STAT_UMGEBUNGSDRUCK_ROHWERT_SENSOR_WERT"];

    public static string Classify(string arg, string resultName)
    {
        if (BasicArgs.Contains(arg))
        {
            return Basic;
        }

        foreach ((string keyword, string category) in Rules)
        {
            if (resultName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return category;
            }
        }

        return Other;
    }

    public static string ClassifySub(string category, string resultName, string unit)
    {
        if (RawExceptions.Contains(resultName, StringComparer.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        foreach ((string ruleCategory, string keyword, string subCategory) in SubRules)
        {
            if (ruleCategory == category && resultName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return subCategory;
            }
        }

        foreach (string keyword in RawKeywords)
        {
            if (resultName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return RawSub;
            }
        }

        if (RawUnits.Contains(unit, StringComparer.OrdinalIgnoreCase))
        {
            return RawSub;
        }

        return string.Empty;
    }
}

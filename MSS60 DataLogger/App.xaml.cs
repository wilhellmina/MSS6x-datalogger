using System.Runtime.Loader;
using System.Text;
using System.Windows;

namespace MSS60_DataLogger;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // EdiabasLib.dll は System.IO.Ports をこのマシンの実物と異なるアセンブリバージョン
        // (このビルドでは 10.0.0.8) で参照している。そのままだと EdInterfaceObd の静的コンストラクター内で
        // SerialPort の初期化に失敗し PlatformNotSupportedException になるため、
        // バージョン不一致のリクエストを実際に読み込み済みの System.IO.Ports にフォールバックさせる。
        AssemblyLoadContext.Default.Resolving += (_, name) =>
            name.Name == "System.IO.Ports" ? typeof(System.IO.Ports.SerialPort).Assembly : null;

        // EdiabasNet は内部で Windows のコードページ(cp1252 等)を使うため、
        // コードページプロバイダーを登録しておかないとインスタンス化で例外になる。
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        base.OnStartup(e);
    }
}

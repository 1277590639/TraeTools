using Avalonia;
using System;
using System.Threading;

namespace TraeTools;

sealed class Program
{
    /// <summary>单实例互斥名（与项目名一致，避免多实例各自签到/切换冲突）。</summary>
    private const string MutexName = @"Local\TraeTools.SingleInstance";

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // 无界面自检：给 CI 冒烟用。macOS 上跑不到登录流程，就验证不了凭据加密，
        // 而这个环节恰恰是 DPAPI → AES-GCM 换机制后最容易静默坏掉的地方。
        if (Array.Exists(args, a => a == "--crypto-selftest"))
        {
            Environment.Exit(RunCryptoSelftest());
            return;
        }

        // CI 冒烟需要知道启动到底走到哪一步：Rosetta 转译下 /usr/bin/sample 的栈回溯是坏的，
        // 只能靠程序自己打点。stderr 会并进 app.log。
        Boot("main-entered");
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Console.Error.WriteLine($"[boot] UNHANDLED {e.ExceptionObject}");

        try
        {
            Run(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[boot] FATAL {ex}");
            throw;
        }
    }

    private static void Run(string[] args)
    {
        using var mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            // 已有实例在运行，直接退出本实例（TraeTools 为单实例工具）
            Boot("another-instance-exits");
            return;
        }
        Boot("mutex-acquired");
        // 旧版 TraeCheckin / TraeSwitch 数据迁移到统一根 %APPDATA%\TraeTools（幂等，保留旧目录）
        TraeTools.Services.DataPaths.Migrate();
        Boot("paths-migrated");
        var app = BuildAvaloniaApp();
        Boot("builder-configured");
        app.StartWithClassicDesktopLifetime(args);
        Boot("lifetime-returned");
    }

    private static void Boot(string stage) => Console.Error.WriteLine($"[boot] {stage}");

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();

    /// <summary>
    /// 凭据加密往返自检，返回进程退出码。只探平台加密层，不碰 AppConfig 的迁移逻辑。
    /// </summary>
    private static int RunCryptoSelftest()
    {
        const string sample = "traetools-crypto-selftest-2026";
        bool ok = true;

        Console.WriteLine($"mechanism: {TraeTools.Services.Security.SecretProtector.MechanismName}");

        string? enc;
        try
        {
            enc = TraeTools.Services.Security.SecretProtector.Protect(sample);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL: Protect 抛异常 {ex.GetType().Name}: {ex.Message}");
            return 1;
        }

        if (string.IsNullOrEmpty(enc)) { Console.WriteLine("FAIL: Protect 返回空"); return 1; }
        if (enc == sample) { Console.WriteLine("FAIL: 密文与明文相同，未真正加密"); ok = false; }

        var back = TraeTools.Services.Security.SecretProtector.Unprotect(enc);
        if (!string.Equals(back, sample, StringComparison.Ordinal))
        {
            Console.WriteLine("FAIL: 解密未能还原原文");
            ok = false;
        }

        // 坏密文必须收敛成 null（触发重新登录），不能把异常抛到启动路径上。
        if (TraeTools.Services.Security.SecretProtector.Unprotect("not-base64-at-all") != null)
        {
            Console.WriteLine("FAIL: 非法密文未返回 null");
            ok = false;
        }

        Console.WriteLine(ok ? "CRYPTO SELFTEST: PASS" : "CRYPTO SELFTEST: FAIL");
        return ok ? 0 : 1;
    }
}

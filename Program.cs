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
        if (Array.Exists(args, a => a == "--autostart-selftest"))
        {
            Environment.Exit(RunAutoStartSelftest());
            return;
        }
        if (Array.Exists(args, a => a == "--switch-path-selftest"))
        {
            Environment.Exit(RunSwitchPathSelftest());
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

    /// <summary>
    /// 开机自启往返自检：真实写一次、读回校验、再复原。
    /// macOS 上这条探的是 LaunchAgent plist 能否被系统认账（plutil 校验），
    /// 光看文件存在证明不了 plist 格式合法。
    /// </summary>
    private static int RunAutoStartSelftest()
    {
        Console.Error.WriteLine($"[autostart] mechanism: {TraeCheckin.AutoStartManager.MechanismName}");
        var before = TraeCheckin.AutoStartManager.IsEnabled();

        var err = TraeCheckin.AutoStartManager.SetEnabled(!before);
        if (err != null)
        {
            Console.Error.WriteLine($"FAIL: SetEnabled({!before}) 返回错误：{err}");
            return 1;
        }
        var after = TraeCheckin.AutoStartManager.IsEnabled();

        // 开关处于打开态时把落点内容原样打出来，让 CI 能用 plutil 校验 plist 合法性——
        // 文件存在不等于格式合法，格式错了 launchd 会静默忽略，用户端就是「勾了没反应」。
        var loc = TraeCheckin.AutoStartManager.ConfigLocation;
        Console.Error.WriteLine($"[autostart] location: {loc}");
        if (after && File.Exists(loc))
        {
            Console.Error.WriteLine("[autostart] begin-content");
            Console.Error.Write(File.ReadAllText(loc));
            Console.Error.WriteLine("[autostart] end-content");
        }

        // 复原，别把 CI 机器的自启状态改掉。
        TraeCheckin.AutoStartManager.SetEnabled(before);
        var restored = TraeCheckin.AutoStartManager.IsEnabled();

        bool ok = true;
        if (after != !before) { Console.Error.WriteLine($"FAIL: 开关未生效（期望 {!before}，实际 {after}）"); ok = false; }
        if (restored != before) { Console.Error.WriteLine($"FAIL: 复原失败（期望 {before}，实际 {restored}）"); ok = false; }

        Console.WriteLine(ok ? "AUTOSTART SELFTEST: PASS" : "AUTOSTART SELFTEST: FAIL");
        return ok ? 0 : 1;
    }

    /// <summary>
    /// 切换模块的相对路径解析自检。
    /// 白名单以反斜杠作比较键（已固化进 vault，改不得），但反斜杠在 macOS 上是合法
    /// 文件名而不是分隔符 —— 解析错了不会报错，只会「切换报成功但一个文件都没动」，
    /// 或者把不存在的边车当成有效备份。这是唯一能在无客户端环境下自动验证的一环。
    /// </summary>
    private static int RunSwitchPathSelftest()
    {
        var root = Path.Combine(Path.GetTempPath(), "trae_switch_selftest_" + Guid.NewGuid().ToString("N"));
        var whitelist = TraeSwitch.Services.CarrierDefaults.FingerprintWhitelist;
        try
        {
            Directory.CreateDirectory(root);
            string[] created =
            [
                @"User\globalStorage\state.vscdb",
                @"User\globalStorage\storage.json",
                @"Local Storage\leveldb\CURRENT",
                @"aha\device.json",
                @"machineid",
            ];
            foreach (var rel in created)
            {
                var full = TraeSwitch.Services.CarrierDefaults.ResolvePath(root, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, "x");
            }

            bool ok = true;
            foreach (var rel in created)
            {
                if (!TraeSwitch.Services.CarrierDefaults.Exists(root, rel))
                {
                    Console.Error.WriteLine($"FAIL: 文件已造出但判定不存在：{rel}");
                    ok = false;
                }
            }

            // 没造的必须判不存在：判成存在会把空快照当成有效备份，恢复时用垃圾覆盖真登录态
            if (TraeSwitch.Services.CarrierDefaults.Exists(root, @"User\globalStorage\state.vscdb-wal"))
            {
                Console.Error.WriteLine("FAIL: 未创建的边车被判为存在");
                ok = false;
            }

            if (!OperatingSystem.IsWindows())
            {
                var probe = TraeSwitch.Services.CarrierDefaults.ResolvePath(root, @"User\globalStorage\state.vscdb");
                if (probe.Contains('\\'))
                {
                    Console.Error.WriteLine($"FAIL: 解析结果含字面反斜杠，必然找不到文件：{probe}");
                    ok = false;
                }
            }

            // 白名单本身不该有重复的比较键（重复会让快照/恢复按同一项做两次）
            var dup = whitelist.Select(TraeSwitch.Services.CarrierDefaults.NormalizeRel)
                               .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
                               .Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
            if (dup.Length > 0)
            {
                Console.Error.WriteLine("FAIL: 白名单存在重复项：" + string.Join(", ", dup));
                ok = false;
            }

            Console.WriteLine($"（白名单 {whitelist.Length} 项）");
            Console.WriteLine(ok ? "SWITCH PATH SELFTEST: PASS" : "SWITCH PATH SELFTEST: FAIL");
            return ok ? 0 : 1;
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* 临时目录残留无害 */ }
        }
    }
}

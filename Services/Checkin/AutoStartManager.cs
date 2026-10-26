using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace TraeCheckin;

/// <summary>
/// 开机自启动。Windows 写注册表 HKCU\Software\Microsoft\Windows\CurrentVersion\Run；
/// macOS 写 ~/Library/LaunchAgents 下的 plist。
/// </summary>
internal static class AutoStartManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TraeTools";
    private const string LaunchAgentLabel = "com.traetools.desktop";

    /// <summary>当前机制名称（仅用于界面文案）。</summary>
    public static string MechanismName => OperatingSystem.IsWindows() ? "注册表 Run 项" : "LaunchAgent";

    /// <summary>是否已注册开机自启动。</summary>
    public static bool IsEnabled()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
                return key?.GetValue(ValueName) != null;
            }
            return File.Exists(LaunchAgentPath);
        }
        catch { return false; }
    }

    /// <summary>
    /// 开启或关闭开机自启动。返回 null 表示成功，否则为可直接展示给用户的原因。
    /// 刻意不吞异常：静默失败会让界面上的开关变成「显示已开启但实际没写进去」的假开关。
    /// </summary>
    public static string? SetEnabled(bool enabled)
    {
        try
        {
            return OperatingSystem.IsWindows() ? SetWindows(enabled) : SetMac(enabled);
        }
        catch (Exception ex)
        {
            return $"写入自启动配置失败：{ex.Message}";
        }
    }

    // ---------- Windows ----------

    private static string ExecutablePath => Environment.ProcessPath ?? "";

    private static string? SetWindows(bool enabled)
    {
        if (enabled)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            key.SetValue(ValueName, $@"""{ExecutablePath}""");
        }
        else
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        return null;
    }

    // ---------- macOS ----------

    private static string LaunchAgentPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library", "LaunchAgents", LaunchAgentLabel + ".plist");

    private static string? SetMac(bool enabled)
    {
        var path = LaunchAgentPath;
        if (!enabled)
        {
            RunLaunchctl("bootout", $"gui/{GetUid()}/{LaunchAgentLabel}");
            if (File.Exists(path)) File.Delete(path);
            return null;
        }

        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(path, BuildLaunchAgentPlist(), new UTF8Encoding(false));

        // bootstrap 失败不算错：plist 已落盘，下次登录必然生效；
        // 失败通常只是本次会话已加载过，不值得把整个开关判成失败。
        RunLaunchctl("bootstrap", $"gui/{GetUid()}", path);
        return null;
    }

    /// <summary>
    /// 走 /usr/bin/open -a 而不是直接指向包内二进制：直接 exec Contents/MacOS/ 下的
    /// 可执行文件会绕过 LaunchServices，应用拿不到 bundle 身份，Dock 图标和菜单栏
    /// 应用名都会退化成通用值（实测过）。
    /// </summary>
    private static string BuildLaunchAgentPlist()
    {
        var exec = Environment.ProcessPath ?? "";
        var args = new List<string>();
        var marker = "/Contents/MacOS/";
        var cut = exec.IndexOf(marker, StringComparison.Ordinal);
        if (cut > 0)
        {
            args.Add("/usr/bin/open");
            args.Add("-a");
            args.Add(exec[..cut]);
        }
        else
        {
            args.Add(exec);
        }

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">");
        sb.AppendLine("<plist version=\"1.0\">");
        sb.AppendLine("<dict>");
        sb.AppendLine($"  <key>Label</key><string>{Xml(LaunchAgentLabel)}</string>");
        sb.AppendLine("  <key>ProgramArguments</key><array>");
        foreach (var a in args)
            sb.AppendLine($"    <string>{Xml(a)}</string>");
        sb.AppendLine("  </array>");
        sb.AppendLine("  <key>RunAtLoad</key><true/>");
        sb.AppendLine("  <key>KeepAlive</key><false/>");
        sb.AppendLine("  <key>ProcessType</key><string>Interactive</string>");
        sb.AppendLine("</dict>");
        sb.AppendLine("</plist>");
        return sb.ToString();
    }

    private static string Xml(string raw) => raw
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    private static void RunLaunchctl(string verb, string target)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("/bin/launchctl")
            {
                ArgumentList = { verb, target },
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            p?.WaitForExit(5000);
        }
        catch { /* launchctl 不可用不影响 plist 已写入的事实 */ }
    }

    [DllImport("/usr/lib/libSystem.dylib")]
    private static extern uint geteuid();

    private static uint GetUid() => geteuid();
}

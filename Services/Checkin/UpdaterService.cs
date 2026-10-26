using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TraeTools.Services;

/// <summary>
/// 应用内自更新：从 GitHub Releases 查询最新正式版，下载更新包到本地，
/// 并通过一个独立 PowerShell 脚本在旧进程退出后替换**整套程序文件**并重启新版。
///
/// 更新包形态（P2-4，配合 NativeAOT 上线）：
///   · 首选 <c>TraeTools-win-x64.zip</c>：NativeAOT 产物**不是单文件**，
///     libSkiaSharp / av_libglesv2 / e_sqlite3 / libHarfBuzzSharp / libsodium 必须与 exe 同目录。
///     只替换 exe 会得到"缺 DLL 打不开"的残废程序，因此改为一整套文件的解包替换。
///   · 回退 <c>TraeTools.exe</c>：兼容只发了单文件 exe 的历史版本（老客户端首次升级路径）。
/// 校验：GitHub Release API 的 asset <c>digest</c> 字段（sha256:...）可用时逐字节校验；
/// 不可用时退化为"解包结构校验"（必须是合法 zip 且含 TraeTools.exe）。
/// </summary>
public static class UpdaterService
{
    // 二开仓库：应用内更新从此处（fork）的 Releases 拉取，不再指向原作者仓库
    public const string Repo = "YYY2579/TraeTools";

    /// <summary>首选更新包资源名（AOT 目录版整包）。</summary>
    public const string PackageAssetName = "TraeTools-win-x64.zip";

    /// <summary>历史单文件 exe 资源名（兼容老客户端升级）。</summary>
    public const string LegacyExeAssetName = "TraeTools.exe";

    /// <summary>主程序文件名（更新后的启动目标）。</summary>
    public const string AppExeName = "TraeTools.exe";

    /// <summary>macOS 更新包资源名（按机器架构，不是按进程架构 —— Rosetta 下跑的 x64 进程应该升到 arm64）。</summary>
    public static string MacPackageAssetName =>
        RuntimeInformation.OSArchitecture == Architecture.Arm64
            ? "TraeTools-macos-arm64.dmg"
            : "TraeTools-macos-x64.dmg";

    /// <summary>当前平台应当下载的整包资源名。</summary>
    public static string CurrentPackageAssetName =>
        OperatingSystem.IsWindows() ? PackageAssetName : MacPackageAssetName;

    /// <summary>
    /// 从 macOS 主程序路径取出外层 .app bundle 路径；不是 bundle 内布局则返回 null。
    /// </summary>
    public static string? BundleRootOf(string exePath)
    {
        if (string.IsNullOrEmpty(exePath)) return null;
        var full = exePath.Replace('\\', '/');
        // 取最后一次：祖先目录也可能叫 xxx.app/，Index 会切错 bundle 根
        var cut = full.LastIndexOf(".app/", StringComparison.OrdinalIgnoreCase);
        if (cut <= 0)
        {
            // 结尾正好是 .app 的情况（理论上不会，主程序在 Contents/MacOS 下）
            return full.EndsWith(".app", StringComparison.OrdinalIgnoreCase) ? full : null;
        }
        return full[..(cut + 4)];
    }

    /// <summary>
    /// 更新要替换的目标目录：Windows 是程序所在目录；macOS 是 .app bundle 的**父目录**
    /// （替换的是整个 bundle，不是 Contents/MacOS 里的单个文件）。
    /// </summary>
    public static string? InstallTargetDir()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return null;
        if (OperatingSystem.IsWindows()) return Path.GetDirectoryName(exe);
        var bundle = BundleRootOf(exe);
        return bundle is null ? Path.GetDirectoryName(exe) : Path.GetDirectoryName(bundle);
    }

    // 经全局代理（NetProxy 缓存共享连接池；设置变更后 ResetCache 自动生效）
    private static HttpClient Http => TraeTools.Services.NetProxy.GetClient(TraeTools.Services.NetProxy.Global);

    // UpdateGuard 下载/查询走 NetProxy 缓存客户端（UA 由 NetProxy 统一设置）

    /// <summary>运行中程序的版本号。统一从 AppVersion（version.json 优先，InformationalVersion 兜底）读取，
    /// 与"关于"页展示保持同源，避免出现"显示 v1.0.2.2 / 却判 v1.0.2.0 为旧"的错位。</summary>
    public static Version CurrentVersion
    {
        get
        {
            var v = AppVersion.Version?.Trim() ?? "";
            // GitHub tag 常见形态："v1.0.2.2" / "1.0.2.2" / "1.0.2.2-beta"。先把前导 v 去掉。
            if (v.Length > 0 && (v[0] == 'v' || v[0] == 'V')) v = v[1..];
            // 仅取主版本号段（-xxx 后缀截断）
            var dash = v.IndexOfAny(new[] { '-', '+' });
            if (dash >= 0) v = v[..dash];
            return Version.TryParse(v, out var ver) ? ver : new Version(1, 0, 0);
        }
    }

    /// <param name="AssetUrl">下载地址。</param>
    /// <param name="AssetName">资源文件名（决定是 zip 整包还是单个 exe）。</param>
    /// <param name="IsZip">true = 目录版整包（解包后替换整套文件）；false = 单文件 exe 替换。</param>
    /// <param name="Sha256Hex">GitHub asset digest 提供的期望 SHA-256（可能为空）。</param>
    public sealed record ReleaseInfo(
        string Tag, Version Version, string AssetUrl, string AssetName, long Size, bool IsZip, string? Sha256Hex);

    // GitHub 匿名 API 每小时最多 60 次；自动检查若紧跟频繁启动会很快耗尽配额（返回 403）。
    // 用 1 小时磁盘缓存：自动（静默）检查在窗口内直接沿用上次结论，不反复打 API。
    private static readonly TimeSpan CacheWindow = TimeSpan.FromHours(1);
    private static string CacheFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TraeTools", "update_check_cache.txt");

    /// <summary>远程 release 中发现比本地更新的版本时返回其信息；否则返回 null。</summary>
    public static async Task<ReleaseInfo?> GetNewerAsync(CancellationToken ct = default, bool useCache = true)
    {
        // 自动检查 + 1 小时内刚查过 → 不联网，沿用"已是最新"结论
        if (useCache && IsCacheFresh()) return null;
        try
        {
            return await FetchLatestAsync(ct);
        }
        finally
        {
            TouchCache();   // 无论成败都记录本次检查，避免高频启动反复打 API
        }
    }

    private static bool IsCacheFresh()
    {
        try
        {
            if (!File.Exists(CacheFile)) return false;
            var ts = long.Parse(File.ReadAllText(CacheFile).Trim());
            if (ts <= 0) return false;
            var last = new DateTime(ts, DateTimeKind.Utc);
            return DateTime.UtcNow - last < CacheWindow;
        }
        catch { return false; }
    }

    private static void TouchCache()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CacheFile)!);
            File.WriteAllText(CacheFile, DateTime.UtcNow.Ticks.ToString());
        }
        catch { /* 缓存写失败不影响更新功能 */ }
    }

    private static async Task<ReleaseInfo?> FetchLatestAsync(CancellationToken ct)
    {
        using var resp = await Http.GetAsync(
            $"https://api.github.com/repos/{Repo}/releases/latest",
            HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
        {
            if ((int)resp.StatusCode == 403 &&
                resp.Headers.TryGetValues("X-RateLimit-Reset", out var resetVals) &&
                long.TryParse(resetVals.FirstOrDefault(), out var resetUnix))
            {
                var wait = DateTimeOffset.FromUnixTimeSeconds(resetUnix).ToLocalTime();
                throw new HttpRequestException(
                    $"GitHub 更新限流（每小时最多 60 次），请到 {wait:HH:mm} 后再检查更新。");
            }
            throw new HttpRequestException($"更新检查失败：HTTP {(int)resp.StatusCode}");
        }
        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("tag_name", out var tag) ||
            !TryParseTag(tag.GetString() ?? "", out var ver))
            return null;
        if (ver <= CurrentVersion) return null;   // 本地已是最新（>= 远程）

        var picked = PickAsset(root);
        if (picked == null)
        {
            // 确实有新版、但这次 Release 里没有本平台的包。不能退化成"已是最新版本"——
            // 那会让人以为更新器坏了，而真实情况是发布缺件。
            throw new InvalidOperationException(
                $"发现新版本 {tag.GetString()}，但该 Release 里没有 {CurrentPackageAssetName}，无法应用内更新。"
                + "请到 Releases 页手动下载。");
        }
        return new ReleaseInfo(tag.GetString()!, ver, picked.Value.Url, picked.Value.Name,
            picked.Value.Size, picked.Value.IsZip, picked.Value.Sha256Hex);
    }

    /// <summary>从 release 的 assets 里挑更新资源：优先整包 zip，回退单文件 exe。internal 以便独立环境验证。</summary>
    internal static (string Url, string Name, long Size, bool IsZip, string? Sha256Hex)? PickAsset(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;

        var want = CurrentPackageAssetName;
        (string Url, string Name, long Size, bool IsZip, string? Sha256Hex)? fallback = null;
        foreach (var a in assets.EnumerateArray())
        {
            var name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var url = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
            if (string.IsNullOrEmpty(url)) continue;
            long size = a.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
            var sha = ParseDigest(a);

            if (string.Equals(name, want, StringComparison.OrdinalIgnoreCase))
                return (url!, name, size, true, sha);
            // 单文件 exe 回退只可能对 Windows 有意义：给 macOS 挑一个 .exe 会装出一个跑不了的东西
            if (fallback == null && OperatingSystem.IsWindows()
                && string.Equals(name, LegacyExeAssetName, StringComparison.OrdinalIgnoreCase))
                fallback = (url!, name, size, false, sha);
        }
        return fallback;
    }

    /// <summary>解析 asset 的 digest 字段（形如 "sha256:ab12..."）；非 sha256 或缺失返回 null。internal 以便独立环境验证。</summary>
    internal static string? ParseDigest(JsonElement asset)
    {
        if (!asset.TryGetProperty("digest", out var d) || d.ValueKind != JsonValueKind.String) return null;
        var v = d.GetString() ?? "";
        const string prefix = "sha256:";
        return v.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? v[prefix.Length..].Trim() : null;
    }

    /// <summary>把 "v1.2.3" / "1.2.3" / "1.2.3-beta" 解析为 Version；失败返回 false。</summary>
    public static bool TryParseTag(string tag, out Version ver)
    {
        ver = new Version();
        var s = tag.Trim();
        if (s.Length > 0 && (s[0] == 'v' || s[0] == 'V')) s = s[1..];
        var parts = s.Split('.');
        if (parts.Length < 2) return false;
        var items = new int[4];
        for (var i = 0; i < 4; i++)
        {
            var part = i < parts.Length ? parts[i].Split('-', '+')[0] : "0";
            if (!int.TryParse(part, out items[i])) return false;
        }
        ver = new Version(items[0], items[1], items[2], items[3]);
        return true;
    }

    /// <summary>下载更新资源到 destDir\fileName 并返回完整路径；sink 用于汇报进度。</summary>
    public static async Task<string> DownloadAsync(string url, string destDir, string fileName,
        IProgress<int>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(destDir);
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? 0;
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        var dest = Path.Combine(destDir, fileName);
        await using var fs = File.Create(dest);
        var buf = new byte[81920];
        long done = 0; int read;
        while ((read = await src.ReadAsync(buf.AsMemory(0, buf.Length), ct)) > 0)
        {
            await fs.WriteAsync(buf.AsMemory(0, read), ct);
            done += read;
            if (total > 0) progress?.Report((int)(done * 100 / total));
        }
        return dest;
    }

    /// <summary>SHA-256 逐字节校验；<paramref name="expectedHex"/> 为空时视为"无校验值"返回 true。</summary>
    public static bool VerifySha256(string filePath, string? expectedHex)
    {
        if (string.IsNullOrWhiteSpace(expectedHex)) return true;
        try
        {
            using var sha = SHA256.Create();
            using var fs = File.OpenRead(filePath);
            var actual = Convert.ToHexString(sha.ComputeHash(fs));
            return string.Equals(actual, expectedHex.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>
    /// 把下载好的整包解到 targetDir 内的暂存目录（同卷 → 后续替换可走原子 rename），返回暂存目录。
    /// 解包即做结构校验：必须是合法 zip 且根目录含 <see cref="AppExeName"/>。
    /// </summary>
    public static string PreparePackage(string packagePath, string targetDir)
    {
        if (!OperatingSystem.IsWindows()) return PrepareMacPackage(packagePath);

        var staging = Path.Combine(targetDir, ".~update");
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        Directory.CreateDirectory(staging);
        ZipFile.ExtractToDirectory(packagePath, staging, overwriteFiles: true);
        if (!File.Exists(Path.Combine(staging, AppExeName)))
            throw new InvalidDataException($"更新包结构不合法：根目录缺少 {AppExeName}");
        return staging;
    }

    /// <summary>
    /// macOS：挂载 dmg，用 ditto 把整个 .app 复制到临时暂存目录。
    /// 暂存放临时目录而不是安装目录内 —— /Applications 未必可写，提前在里面建目录
    /// 会把一次本可提示的权限问题变成难以解释的 IO 异常。
    /// ditto 而非 cp -R：它是 macOS 复制 bundle 的标准工具，保留符号链接与包内元数据。
    /// </summary>
    private static string PrepareMacPackage(string dmgPath)
    {
        var staging = Path.Combine(Path.GetTempPath(), "TraeTools_update_stage_" + Guid.NewGuid().ToString("N"));
        var mount = Path.Combine(Path.GetTempPath(), "TraeTools_mnt_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        Directory.CreateDirectory(mount);
        try
        {
            RunOrFail("hdiutil", $"attach -nobrowse -quiet -mountpoint \"{mount}\" \"{dmgPath}\"",
                "挂载更新 dmg 失败");

            var srcBundle = Path.Combine(mount, "TraeTools.app");
            if (!Directory.Exists(srcBundle))
                throw new InvalidDataException("更新包结构不合法：dmg 根目录缺少 TraeTools.app");

            // 缺登录宿主的包不能装：装完自动登录会静默退化成手动粘 Cookie
            var host = Path.Combine(srcBundle, "Contents", "MacOS", "TraeTools.LoginHost");
            if (!File.Exists(host))
                throw new InvalidDataException("更新包结构不合法：bundle 内缺少登录宿主 TraeTools.LoginHost");

            RunOrFail("ditto", $"\"{srcBundle}\" \"{Path.Combine(staging, "TraeTools.app")}\"",
                "复制新版 bundle 到暂存目录失败");
            return staging;
        }
        finally
        {
            RunQuiet("hdiutil", $"detach -quiet -force \"{mount}\"");
            try { Directory.Delete(mount, recursive: true); } catch { /* 临时目录残留无害 */ }
        }
    }

    private static void RunOrFail(string file, string args, string whatFailed)
    {
        var psi = new ProcessStartInfo(file)
        {
            Arguments = args,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException(whatFailed + "：无法启动 " + file);
        p.WaitForExit(120_000);
        if (p.ExitCode != 0)
        {
            var err = Safe(p.StandardError.ReadToEnd());
            throw new InvalidOperationException($"{whatFailed}（exit {p.ExitCode}）{err}");
        }
    }

    private static void RunQuiet(string file, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(file)
            {
                Arguments = args,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            });
            p?.WaitForExit(30_000);
        }
        catch { /* 清理尽力而为 */ }
    }

    private static string Safe(string s)
    {
        try { return string.IsNullOrWhiteSpace(s) ? "" : "：" + s.Trim(); }
        catch { return ""; }
    }

    /// <summary>
    /// 用独立 PowerShell 进程异步应用更新：反复重试直到旧进程退出（文件解锁）后，
    /// 把暂存目录里的**整套文件**逐个覆盖到目标目录，清掉暂存，再启动新版。
    /// 任一文件失败即整体重试（<c>-ErrorAction Stop</c> 保证复制失败会真正抛错被 catch 住），
    /// 避免"半新半旧"。
    ///
    /// 注意：脚本以 Windows PowerShell 5.1（.NET Framework）执行，两处坑已实测确认：
    ///   · <c>[System.IO.File]::Move(src,dst,true)</c> 三参重载在 5.1 **不存在**（MethodNotFound）；
    ///   · 脚本体里 <c>$</c> 不能写成 C# 式的反引号转义（`` `$ ``），5.1 会解析报错。
    /// 故统一用 <c>Copy-Item -Force</c> + 纯 <c>$</c> 变量。
    /// </summary>
    public static void ApplyInBackground(string stagingDir, string targetDir)
    {
        if (!OperatingSystem.IsWindows())
        {
            ApplyMacInBackground(stagingDir, targetDir);
            return;
        }

        var ps1 = Path.Combine(Path.GetTempPath(), $"apply_update_{Guid.NewGuid():N}.ps1");
        var body = $@"
$stage = ""{stagingDir}""
$dst   = ""{targetDir}""
$exe   = ""{AppExeName}""
$tries = 120
for ($i = 0; $i -lt $tries; $i++) {{
  $ok = $true
  try {{
    foreach ($f in Get-ChildItem -LiteralPath $stage -Recurse -File) {{
      $rel = $f.FullName.Substring($stage.Length).TrimStart('\')
      $target = Join-Path $dst $rel
      $dir = Split-Path $target -Parent
      if (-not (Test-Path -LiteralPath $dir)) {{ New-Item -ItemType Directory -Path $dir -Force | Out-Null }}
      Copy-Item -LiteralPath $f.FullName -Destination $target -Force -ErrorAction Stop
    }}
  }} catch {{ $ok = $false }}
  if ($ok) {{ break }}
  Start-Sleep -Milliseconds 250
}}
# 只有整套文件都替换成功才清暂存并重启；否则保留 .~update 供手动/下次恢复，
# 避免半新半旧的安装目录被启动。
if ($ok) {{
  Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
  $newExe = Join-Path $dst $exe
  if (Test-Path -LiteralPath $newExe) {{ Start-Process -FilePath $newExe }}
}}
Remove-Item -LiteralPath $MyInvocation.MyCommand.Path -Force -ErrorAction SilentlyContinue
";
        File.WriteAllText(ps1, body, new UTF8Encoding(true));

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{ps1}\"",
            WindowStyle = ProcessWindowStyle.Hidden,
            CreateNoWindow = true,
            UseShellExecute = false
        };
        Process.Start(psi);
    }

    /// <summary>
    /// macOS：起一个分离的 bash 进程，等旧进程退出后整个换掉 .app bundle 并重新拉起。
    ///
    /// 三个和 Windows 不同的地方，都是实测层面的取舍：
    ///   · 等的是**进程消失**（pgrep）而不是"文件能不能写"：macOS 上正在运行的 bundle
    ///     依然可以被 rename，用可写性判断会误以为已经退出、在旧进程还活着时就动手。
    ///   · 换法是「旧版改名挪走 → 新版放进去 → 成功才删旧版」，任一步失败就把旧版改回来
    ///     并重新拉起，绝不允许出现「两个都没有」的砖。
    ///   · 重启走 open 而不是直接 exec 包内二进制，否则新进程拿不到 bundle 身份。
    /// </summary>
    public static void ApplyMacInBackground(string stagingDir, string targetDir)
    {
        var sh = Path.Combine(Path.GetTempPath(), $"apply_update_{Guid.NewGuid():N}.sh");
        File.WriteAllText(sh, BuildMacSwapScript(dryRun: false), new UTF8Encoding(false));

        // 路径与 PID 一律走命令行参数，不拼进脚本体：拼进去的话路径里出现 $ 或反引号
        // 会被 bash 二次展开，轻则脚本语法崩掉，重则执行意外命令。
        // 也不用 UseShellExecute：脚本要能在父进程退出后继续活着。
        Process.Start(new ProcessStartInfo
        {
            FileName = "/bin/bash",
            ArgumentList = { sh, stagingDir, targetDir, Environment.ProcessId.ToString(), "60" },
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        // 脚本自己负责收尾（删暂存目录与自身）；这里不删，否则会被提前回收。
    }

    /// <summary>
    /// 生成 macOS 换包脚本，用法：bash script.sh &lt;stagingDir&gt; &lt;targetDir&gt; &lt;waitPid&gt; [waitSecs]。
    /// 抽成独立方法是为了让 --update-swap-selftest 跑**同一份**脚本 —— 更新器是唯一
    /// 能把用户现有安装直接搞坏的模块，脚本逻辑不能只靠读代码验收。
    /// dryRun 只跳过 open（拉起假 bundle），文件动作与生产完全一致。
    ///
    /// 关键取舍：先把新版复制到**目标目录所在卷**上的临时名，之后全是同卷 rename。
    /// 跨卷 mv 会退化成「复制+删除」，中途失败会留下半个 TARGET 目录；而把旧版
    /// rename 进一个已存在的目录会变成「旧版嵌在新版里面」而不是恢复。
    /// 全程任何一步失败都不删旧版，直到新版确认就位才删。
    /// </summary>
    internal static string BuildMacSwapScript(bool dryRun)
    {
        return $$"""
            #!/bin/bash
            set -u
            STAGE="$1"
            DST="$2"
            WAIT_PID="$3"
            WAIT_SECS="${4:-60}"
            APP="TraeTools.app"
            TARGET="$DST/$APP"
            NEW="$DST/.TraeTools.app.new"
            OLD="$DST/.TraeTools.app.old"
            DRY={{(dryRun ? 1 : 0)}}

            relaunch() { [ "$DRY" = "1" ] || open "$1" 2>/dev/null; }

            # 等**发起更新的那个进程**退出，而不是"没有任何叫 TraeTools 的进程"。
            # 按名字等有两个错：本脚本若由 TraeTools 自己拉起的自检调用，会永远等不到；
            # 而用户又开了一个副本时，会白等到超时。判据也不能是"文件能不能写"——
            # macOS 上正在运行的 bundle 依然可被 rename，用可写性会误判成已退出。
            gone=0
            ticks=$(( WAIT_SECS * 4 ))
            for i in $(seq 1 "$ticks"); do
              kill -0 "$WAIT_PID" 2>/dev/null || { gone=1; break; }
              sleep 0.25
            done
            if [ "$gone" != "1" ]; then
              # 旧进程还活着就动包，等于在运行中的 bundle 上改文件。宁可放弃更新。
              echo "swap: 进程 $WAIT_PID 在 ${WAIT_SECS}s 内未退出，放弃换包（未改动任何文件）" >&2
              exit 1
            fi

            # 1) 先把新版复制到目标卷。此步完全不碰 TARGET，失败零代价。
            rm -rf "$NEW"
            if [ ! -d "$STAGE/$APP" ]; then
              # 和「目录不可写」分开报：混成一条会把人往权限方向引，实际是包没解出来。
              echo "swap: 暂存目录里没有 $APP（解包未完成），未改动现有安装" >&2
              relaunch "$TARGET"
              exit 1
            fi
            if ! cp -a "$STAGE/$APP" "$NEW" 2>/dev/null; then
              echo "swap: 无法写入 $DST（权限或空间不足），未改动现有安装" >&2
              relaunch "$TARGET"
              exit 1
            fi

            # 2) 旧版挪开（也可能本来就没有：上一次换包失败的残局）
            moved_old=0
            if [ -d "$TARGET" ]; then
              rm -rf "$OLD"
              if mv "$TARGET" "$OLD" 2>/dev/null; then moved_old=1
              else
                echo "swap: 旧版挪动失败，未改动现有安装" >&2
                rm -rf "$NEW"; relaunch "$TARGET"; exit 1
              fi
            fi

            # 3) 新版就位（同卷 rename，原子）
            if mv "$NEW" "$TARGET" 2>/dev/null; then
              rm -rf "$OLD" "$STAGE"
              echo "swap: ok" >&2
              rm -f "$0"
              relaunch "$TARGET"
              exit 0
            fi

            # 4) 新版没放进去 → 尽力回滚旧版
            echo "swap: 新版放入失败，回滚旧版" >&2
            rm -rf "$NEW"
            if [ "$moved_old" = "1" ] && mv "$OLD" "$TARGET" 2>/dev/null; then
              relaunch "$TARGET"
            elif [ "$moved_old" = "1" ]; then
              # 连回滚都失败：绝不删旧版，把残局留在原地供人工恢复
              echo "swap: 回滚也失败！旧版仍在 $OLD，请手动改回 $TARGET" >&2
            fi
            rm -f "$0"
            exit 1
            """;
    }
}

using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;

namespace TraeTools.Services;

/// <summary>
/// 网络代理统一入口。
///
/// 代理地址格式（ProxyServer 字段）：
///   127.0.0.1:7897              → 视为 http 代理（Clash/V2Ray 混合端口最常见）
///   http://127.0.0.1:7897       → 显式 http 代理
///   socks5://127.0.0.1:1080     → socks5 代理（.NET 6+ SocketsHttpHandler 原生支持）
///   socks5://user:pass@h:p      → 带认证
///
/// HttpClient 按「代理地址」缓存复用（"" = 直连），同地址共享连接池；
/// 设置变更后调用 ResetCache() 丢弃旧客户端。
/// </summary>
public static class NetProxy
{
    private static readonly ConcurrentDictionary<string, HttpClient> Clients = new(StringComparer.Ordinal);

    /// <summary>当前全局代理地址（null = 直连）。启动与设置保存时由应用层注入。</summary>
    public static string? Global { get; private set; }

    /// <summary>注入全局代理地址并丢弃既有客户端缓存。</summary>
    public static void SetGlobal(string? address)
    {
        var next = string.IsNullOrWhiteSpace(address) ? null : Normalize(address);
        if (Global == next) return;
        Global = next;
        ResetCache();
    }

    /// <summary>解析生效代理：账号自有代理优先，其次全局；返回 null = 直连。</summary>
    public static string? Resolve(string? accountProxy)
        => string.IsNullOrWhiteSpace(accountProxy) ? Global : Normalize(accountProxy);

    /// <summary>把 ProxyEnabled + ProxyServer 组合为代理地址字符串（未启用/为空 → null）。</summary>
    public static string? ProxyAddress(bool enabled, string? server)
    {
        if (!enabled || string.IsNullOrWhiteSpace(server)) return null;
        return Normalize(server);
    }

    /// <summary>规整代理地址：无 scheme 时补 http://，去尾部空白。</summary>
    public static string Normalize(string server)
    {
        var s = (server ?? "").Trim();
        if (s.Length == 0) return s;
        if (!s.Contains("://", StringComparison.Ordinal)) s = "http://" + s;
        return s;
    }

    /// <summary>地址格式是否可用（host:port 或 scheme://[user:pass@]host:port）。</summary>
    public static bool IsValidServer(string? server)
    {
        var s = (server ?? "").Trim();
        if (s.Length == 0) return false;
        if (!s.Contains("://", StringComparison.Ordinal)) s = "http://" + s;
        if (!Uri.TryCreate(s, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme is not ("http" or "https" or "socks5" or "socks5h")) return false;
        return !string.IsNullOrWhiteSpace(uri.Host) && uri.Port > 0;
    }

    /// <summary>取（或创建）指定代理的共享 HttpClient；proxy 为 null/空 = 直连。</summary>
    public static HttpClient GetClient(string? proxy)
    {
        var key = string.IsNullOrWhiteSpace(proxy) ? "" : Normalize(proxy);
        return Clients.GetOrAdd(key, k => CreateClient(k));
    }

    /// <summary>设置变更后调用：丢弃全部缓存客户端（下一次调用按新代理重建）。</summary>
    public static void ResetCache()
    {
        var old = Clients.Keys.ToList();
        Clients.Clear();
        foreach (var k in old)
        {
            // 旧实例不再复用；交给 GC（不主动 Dispose，避免打断在途请求）
        }
    }

    /// <summary>连通性测试：经指定代理请求 trae.cn 与 api.github.com，返回摘要。</summary>
    public static async Task<(bool Ok, string Message)> TestAsync(string? proxy)
    {
        if (!string.IsNullOrWhiteSpace(proxy) && !IsValidServer(proxy))
            return (false, "代理地址格式无效（示例：127.0.0.1:7897 或 socks5://127.0.0.1:1080）");

        try
        {
            using var http = CreateClient(Normalize(proxy ?? ""));
            http.Timeout = TimeSpan.FromSeconds(12);

            // 出口 IP（ipip.net，国内可达）
            string ip = "";
            try
            {
                var s = await http.GetStringAsync("https://myip.ipip.net");
                ip = s.Trim();
            }
            catch { ip = "(出口 IP 获取失败)"; }

            // Trae API 可达性
            string trae;
            try
            {
                using var resp = await http.GetAsync("https://api.trae.cn/", HttpCompletionOption.ResponseHeadersRead);
                trae = "trae.cn HTTP " + (int)resp.StatusCode;
            }
            catch (Exception ex) { trae = "trae.cn 不可达：" + ex.Message; }

            // GitHub 可达性（部署/更新检查依赖）
            string gh;
            try
            {
                using var resp = await http.GetAsync("https://api.github.com/zen", HttpCompletionOption.ResponseHeadersRead);
                gh = "github HTTP " + (int)resp.StatusCode;
            }
            catch (Exception ex) { gh = "github 不可达：" + ex.Message; }

            return (true, $"{ip}\n{trae}；{gh}");
        }
        catch (Exception ex)
        {
            return (false, "代理不可用：" + ex.Message);
        }
    }

    private static HttpClient CreateClient(string normalizedProxy)
    {
        var handler = new HttpClientHandler();
        if (normalizedProxy.Length > 0)
        {
            var uri = new Uri(normalizedProxy);
            var webProxy = new WebProxy(uri)
            {
                BypassProxyOnLocal = false,
            };
            // user:pass@ 认证
            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                var idx = uri.UserInfo.IndexOf(':');
                if (idx > 0)
                {
                    webProxy.Credentials = new NetworkCredential(
                        Uri.UnescapeDataString(uri.UserInfo[..idx]),
                        Uri.UnescapeDataString(uri.UserInfo[(idx + 1)..]));
                }
            }
            handler.Proxy = webProxy;
            handler.UseProxy = true;
        }
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TraeCheckin/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }
}

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace TraeTools.Services.Security;

/// <summary>
/// 凭据落盘加密。Windows 沿用 DPAPI，其余平台用 AES-GCM + 本机密钥文件。
///
/// 保留 DPAPI 是硬要求：线上用户的 config.json 里全是 DPAPI 密文，换机制等于让他们全部重新登录。
/// 因此两种格式靠 <c>aesg:</c> 前缀区分，可以共存于同一份配置文件，读取时按前缀分派。
///
/// macOS 刻意不用 Keychain：本项目的包是 ad-hoc 签名（无开发者证书、未公证），
/// 而 Keychain 条目的访问 ACL 绑定在代码签名上——ad-hoc 签名每次重新构建都会变，
/// 会导致每次自动更新后都要重新授权、甚至直接取不到密钥。
/// 密钥文件与配置同目录、权限 600，威胁模型与 DPAPI 的「当前用户可读」同级。
/// </summary>
public static class SecretProtector
{
    private const string AesPrefix = "aesg:";
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;

    private static byte[]? _cachedKey;
    private static readonly object KeyGate = new();

    /// <summary>当前平台的加密机制名称，仅用于报告文案。</summary>
    public static string MechanismName =>
        OperatingSystem.IsWindows() ? "Windows DPAPI" : "AES-256-GCM + 本机密钥文件";

    /// <summary>加密。失败抛异常，由调用方兜底（保留旧文件，不能把坏密文写进配置）。</summary>
    public static string Protect(string plain)
    {
        if (OperatingSystem.IsWindows())
        {
            var dpapi = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(dpapi);
        }

        var key = MachineKey();
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var pt = Encoding.UTF8.GetBytes(plain);
        var ct = new byte[pt.Length];
        var tag = new byte[TagSize];
        using (var aes = new AesGcm(key, TagSize))
            aes.Encrypt(nonce, pt, ct, tag);

        var blob = new byte[NonceSize + TagSize + ct.Length];
        Buffer.BlockCopy(nonce, 0, blob, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, blob, NonceSize, TagSize);
        Buffer.BlockCopy(ct, 0, blob, NonceSize + TagSize, ct.Length);
        return AesPrefix + Convert.ToBase64String(blob);
    }

    /// <summary>解密；失败（换机、换用户、密钥文件丢失）返回 null，由调用方触发重新登录。</summary>
    public static string? Unprotect(string? enc)
    {
        if (string.IsNullOrEmpty(enc)) return enc;
        try
        {
            if (!enc.StartsWith(AesPrefix, StringComparison.Ordinal))
            {
                // 历史 DPAPI 密文。非 Windows 上必然解不开，异常由下面的 catch 收敛成 null。
                var dpapi = ProtectedData.Unprotect(Convert.FromBase64String(enc), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(dpapi);
            }

            var blob = Convert.FromBase64String(enc[AesPrefix.Length..]);
            if (blob.Length < NonceSize + TagSize) return null;

            var nonce = new byte[NonceSize];
            var tag = new byte[TagSize];
            var ct = new byte[blob.Length - NonceSize - TagSize];
            Buffer.BlockCopy(blob, 0, nonce, 0, NonceSize);
            Buffer.BlockCopy(blob, NonceSize, tag, 0, TagSize);
            Buffer.BlockCopy(blob, NonceSize + TagSize, ct, 0, ct.Length);

            var pt = new byte[ct.Length];
            using (var aes = new AesGcm(MachineKey(), TagSize))
                aes.Decrypt(nonce, ct, tag, pt);
            return Encoding.UTF8.GetString(pt);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 本机密钥：首次使用时随机生成并落盘，之后常驻内存。
    /// 只在本机自洽，因此配置目录整体拷到另一台机器会解不开（与 DPAPI 行为一致）。
    /// </summary>
    private static byte[] MachineKey()
    {
        lock (KeyGate)
        {
            if (_cachedKey != null) return _cachedKey;

            var path = Path.Combine(DataPaths.Root, "machine.key");
            Directory.CreateDirectory(DataPaths.Root);

            byte[] key;
            if (File.Exists(path))
            {
                key = File.ReadAllBytes(path);
                if (key.Length != KeySize)
                    throw new CryptographicException($"{path} 长度异常（{key.Length}），拒绝使用。");
            }
            else
            {
                key = RandomNumberGenerator.GetBytes(KeySize);
                File.WriteAllBytes(path, key);
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            _cachedKey = key;
            return key;
        }
    }
}

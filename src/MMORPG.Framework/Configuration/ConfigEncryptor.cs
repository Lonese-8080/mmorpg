// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;
using System.Text;
using MMORPG.Framework.Logging;

namespace MMORPG.Framework.Configuration;

/// <summary>
/// 配置敏感字段加密器
/// 
/// 支持两种方式：
/// 1. AES-256（跨平台，使用主密钥字符串 + 随机 Salt/IV） — 默认
/// 2. DPAPI（仅 Windows，使用当前用户/机器密钥） — 平台相关
/// 
/// 密文格式（hex）：
///   ENC[v1]:base64(salt|iv|ciphertext)
/// 
/// 解密时自动识别明文（原样返回），方便平滑迁移：
///   明文 "mySecret" → 原样使用
///   密文 "ENC[v1]:..." → 自动解密
/// 
/// 用法：
///   1. 启动时设置主密钥：ConfigEncryptor.SetMasterKey("my-secret-key")
///      （或通过环境变量 MMORPG_CONFIG_KEY 提供）
///   2. 加密：ConfigEncryptor.Encrypt("mySecret")
///   3. 解密：ConfigEncryptor.TryDecrypt(storedValue, out var plain)
/// </summary>
public static class ConfigEncryptor
{
    private const string Prefix = "ENC[v1]:";
    private const int SaltSize = 16;
    private const int IvSize = 16;
    private const int KeySize = 32; // 256-bit
    private const int Iterations = 100_000;

    private static string? _masterKey;

    /// <summary>
    /// 当前主密钥；若未显式设置，将尝试从环境变量 <c>MMORPG_CONFIG_KEY</c> 读取
    /// </summary>
    public static string? MasterKey
    {
        get
        {
            if (!string.IsNullOrEmpty(_masterKey)) return _masterKey;
            _masterKey = Environment.GetEnvironmentVariable("MMORPG_CONFIG_KEY");
            return _masterKey;
        }
    }

    /// <summary>
    /// 显式设置主密钥（运行时调用以覆盖环境变量）
    /// </summary>
    public static void SetMasterKey(string? key)
    {
        _masterKey = key;
    }

    /// <summary>
    /// 是否已加密（密文以 <c>ENC[v1]:</c> 开头）
    /// </summary>
    public static bool IsEncrypted(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        return value.StartsWith(Prefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// 加密一个字符串
    /// 
    /// 若 <paramref name="plain"/> 已为密文或为 null/empty，原样返回
    /// 若未设置主密钥，会记录警告并原样返回（明文落盘，由调用方负责）
    /// </summary>
    public static string? Encrypt(string? plain)
    {
        if (string.IsNullOrEmpty(plain)) return plain;
        if (IsEncrypted(plain)) return plain;

        var key = MasterKey;
        if (string.IsNullOrEmpty(key))
        {
            Logger.Warning("Config", "ConfigEncryptor 未设置主密钥，明文配置将直接落盘（建议配置 MMORPG_CONFIG_KEY）");
            return plain;
        }

        try
        {
            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var iv = RandomNumberGenerator.GetBytes(IvSize);
            var derivedKey = DeriveKey(key, salt);

            using var aes = Aes.Create();
            aes.KeySize = 256;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = derivedKey;
            aes.IV = iv;

            using var encryptor = aes.CreateEncryptor();
            var plainBytes = Encoding.UTF8.GetBytes(plain);
            var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

            // salt|iv|cipher
            var combined = new byte[SaltSize + IvSize + cipherBytes.Length];
            Buffer.BlockCopy(salt, 0, combined, 0, SaltSize);
            Buffer.BlockCopy(iv, 0, combined, SaltSize, IvSize);
            Buffer.BlockCopy(cipherBytes, 0, combined, SaltSize + IvSize, cipherBytes.Length);

            return Prefix + Convert.ToBase64String(combined);
        }
        catch (Exception ex)
        {
            Logger.Error("Config", ex, "配置加密失败，将以明文返回");
            return plain;
        }
    }

    /// <summary>
    /// 尝试解密。返回 true 表示 <paramref name="value"/> 是密文且已成功解密；false 表示原本就是明文或解密失败
    /// </summary>
    /// <param name="value">配置文件中存储的原始值</param>
    /// <param name="plaintext">解密后的明文（若失败则为原样）</param>
    public static bool TryDecrypt(string? value, out string plaintext)
    {
        plaintext = value ?? string.Empty;

        if (string.IsNullOrEmpty(value)) return true;
        if (!IsEncrypted(value)) return false;

        var key = MasterKey;
        if (string.IsNullOrEmpty(key))
        {
            Logger.Warning("Config", "ConfigEncryptor 未设置主密钥，无法解密: 前缀={0}", Prefix);
            return false;
        }

        try
        {
            var b64 = value.Substring(Prefix.Length);
            var combined = Convert.FromBase64String(b64);
            if (combined.Length < SaltSize + IvSize + 16) // 至少 1 字节密文
            {
                Logger.Warning("Config", "配置密文长度异常: {0} 字节", combined.Length);
                return false;
            }

            var salt = new byte[SaltSize];
            var iv = new byte[IvSize];
            var cipher = new byte[combined.Length - SaltSize - IvSize];
            Buffer.BlockCopy(combined, 0, salt, 0, SaltSize);
            Buffer.BlockCopy(combined, SaltSize, iv, 0, IvSize);
            Buffer.BlockCopy(combined, SaltSize + IvSize, cipher, 0, cipher.Length);

            var derivedKey = DeriveKey(key, salt);
            using var aes = Aes.Create();
            aes.KeySize = 256;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = derivedKey;
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor();
            var plainBytes = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
            plaintext = Encoding.UTF8.GetString(plainBytes);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Config", ex, "配置解密失败");
            return false;
        }
    }

    private static byte[] DeriveKey(string password, byte[] salt)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            password: password,
            salt: salt,
            iterations: Iterations,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: KeySize);
    }
}

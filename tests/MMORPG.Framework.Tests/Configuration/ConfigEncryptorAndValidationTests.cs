using System.ComponentModel.DataAnnotations;
using MMORPG.Framework.Configuration;
using Xunit;

namespace MMORPG.Framework.Tests.Configuration;

/// <summary>
/// ConfigEncryptor 单元测试
///
/// 该类使用全局静态状态（ConfigEncryptor.MasterKey），必须加入
/// <see cref="GlobalStateCollection"/> 以保证串行执行。
/// </summary>
[Collection(GlobalStateCollection.Name)]
public class ConfigEncryptorTests : IDisposable
{
    private readonly string? _originalKey;

    public ConfigEncryptorTests()
    {
        _originalKey = ConfigEncryptor.MasterKey;
    }

    public void Dispose()
    {
        ConfigEncryptor.SetMasterKey(_originalKey);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Encrypt_NullOrEmpty_ReturnsAsIs()
    {
        Assert.Null(ConfigEncryptor.Encrypt(null));
        Assert.Equal(string.Empty, ConfigEncryptor.Encrypt(string.Empty));
    }

    [Fact]
    public void Encrypt_Decrypt_Roundtrip_WithMasterKey()
    {
        ConfigEncryptor.SetMasterKey("test-master-key-123");
        var plain = "my-secret-password";
        var encrypted = ConfigEncryptor.Encrypt(plain);

        Assert.NotNull(encrypted);
        Assert.NotEqual(plain, encrypted);
        Assert.True(ConfigEncryptor.IsEncrypted(encrypted));

        Assert.True(ConfigEncryptor.TryDecrypt(encrypted, out var decrypted));
        Assert.Equal(plain, decrypted);
    }

    [Fact]
    public void Encrypt_DifferentOutput_ForSameInput()
    {
        // 随机 Salt/IV 决定每次加密结果不同
        ConfigEncryptor.SetMasterKey("test-key");
        var plain = "the-same-input";
        var e1 = ConfigEncryptor.Encrypt(plain);
        var e2 = ConfigEncryptor.Encrypt(plain);
        Assert.NotEqual(e1, e2);

        Assert.True(ConfigEncryptor.TryDecrypt(e1, out var d1));
        Assert.True(ConfigEncryptor.TryDecrypt(e2, out var d2));
        Assert.Equal(plain, d1);
        Assert.Equal(plain, d2);
    }

    [Fact]
    public void TryDecrypt_Plaintext_ReturnsFalse_AndKeepsAsIs()
    {
        ConfigEncryptor.SetMasterKey("test-key");
        var plain = "not-encrypted";
        Assert.False(ConfigEncryptor.IsEncrypted(plain));
        Assert.False(ConfigEncryptor.TryDecrypt(plain, out var result));
        Assert.Equal(plain, result);
    }

    [Fact]
    public void TryDecrypt_WrongKey_Fails()
    {
        ConfigEncryptor.SetMasterKey("key-1");
        var encrypted = ConfigEncryptor.Encrypt("secret");

        ConfigEncryptor.SetMasterKey("key-2");
        Assert.False(ConfigEncryptor.TryDecrypt(encrypted, out _));
    }

    [Fact]
    public void IsEncrypted_PrefixCheck()
    {
        Assert.False(ConfigEncryptor.IsEncrypted("plain"));
        Assert.False(ConfigEncryptor.IsEncrypted(""));
        Assert.False(ConfigEncryptor.IsEncrypted(null));
        Assert.True(ConfigEncryptor.IsEncrypted("ENC[v1]:abc"));
    }
}

/// <summary>
/// ServerConfig 校验单元测试
/// </summary>
public class ServerConfigValidationTests
{
    [Fact]
    public void Default_Config_Passes_Validation()
    {
        var cfg = new ServerConfig();
        var ctx = new ValidationContext(cfg);
        var results = cfg.Validate(ctx).ToList();
        Assert.Empty(results);
    }

    [Fact]
    public void Invalid_Port_Fails()
    {
        var cfg = new ServerConfig { Network = new NetworkSettings { Port = 0 } };
        var ctx = new ValidationContext(cfg);
        var results = cfg.Validate(ctx).ToList();
        Assert.Contains(results, r => r.MemberNames.Contains("Network"));
    }

    [Fact]
    public void Invalid_SchemaVersion_Fails()
    {
        var cfg = new ServerConfig { SchemaVersion = "not-semver" };
        var ctx = new ValidationContext(cfg);
        var results = cfg.Validate(ctx).ToList();
        Assert.Contains(results, r => r.MemberNames.Contains("SchemaVersion"));
    }

    [Fact]
    public void Invalid_SamplingRate_Fails()
    {
        var cfg = new ServerConfig { Logging = new LoggingSettings { SamplingRate = 2.0 } };
        var ctx = new ValidationContext(cfg);
        var results = cfg.Validate(ctx).ToList();
        Assert.Contains(results, r => r.MemberNames.Contains("Logging"));
    }

    [Fact]
    public void SessionPool_MaxLessThanMin_Fails()
    {
        var cfg = new ServerConfig
        {
            Network = new NetworkSettings
            {
                SessionPool = new SessionPoolSettings { MinPoolSize = 100, MaxPoolSize = 50 }
            }
        };
        var ctx = new ValidationContext(cfg);
        var results = cfg.Validate(ctx).ToList();
        Assert.Contains(results, r => r.MemberNames.Contains("Network"));
    }

    [Fact]
    public void TlsEnabled_WithoutCert_Fails()
    {
        var cfg = new ServerConfig
        {
            Network = new NetworkSettings { EnableTls = true, CertificatePath = null }
        };
        var ctx = new ValidationContext(cfg);
        var results = cfg.Validate(ctx).ToList();
        Assert.Contains(results, r => r.MemberNames.Contains("Network"));
    }

    [Fact]
    public void IsSchemaCompatibleWith_MajorMatch_True()
    {
        var a = new ServerConfig { SchemaVersion = "1.2.3" };
        var b = new ServerConfig { SchemaVersion = "1.5.7" };
        Assert.True(a.IsSchemaCompatibleWith(b));
    }

    [Fact]
    public void IsSchemaCompatibleWith_MajorMismatch_False()
    {
        var a = new ServerConfig { SchemaVersion = "1.0.0" };
        var b = new ServerConfig { SchemaVersion = "2.0.0" };
        Assert.False(a.IsSchemaCompatibleWith(b));
    }
}

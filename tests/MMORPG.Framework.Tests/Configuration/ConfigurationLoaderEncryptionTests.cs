using System.IO;
using MMORPG.Framework.Configuration;
using Xunit;

namespace MMORPG.Framework.Tests.Configuration;

/// <summary>
/// ConfigurationLoader + ConfigEncryptor 集成测试
///
/// 该类使用全局静态状态（ConfigEncryptor.MasterKey, ConfigurationLoader._cache），
/// 必须加入 <see cref="GlobalStateCollection"/> 以保证串行执行。
/// </summary>
[Collection(GlobalStateCollection.Name)]
public class ConfigurationLoaderEncryptionTests : IDisposable
{
    private readonly string _tempFile;
    private readonly string? _originalKey;

    public ConfigurationLoaderEncryptionTests()
    {
        _originalKey = ConfigEncryptor.MasterKey;
        _tempFile = Path.Combine(Path.GetTempPath(), $"mmorpg_cfg_{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        ConfigEncryptor.SetMasterKey(_originalKey);
        if (File.Exists(_tempFile)) File.Delete(_tempFile);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Load_With_EncryptedPasswordField_Decrypts_Automatically()
    {
        ConfigEncryptor.SetMasterKey("test-master");
        var encryptedPassword = ConfigEncryptor.Encrypt("plain-password");
        Assert.NotNull(encryptedPassword);
        Assert.StartsWith("ENC[v1]:", encryptedPassword!);

        // 注意：Database.Type / Server.Type 是 enum，使用数字（0=Game/MySql）
        var json = $@"{{
  ""Server"": {{ ""ServerId"": 42, ""ServerName"": ""X"", ""Type"": 0, ""DebugMode"": false }},
  ""Database"": {{
    ""Type"": 0,
    ""ConnectionString"": ""{encryptedPassword}"",
    ""MaxPoolSize"": 10, ""MinPoolSize"": 1, ""ConnectionTimeout"": 30, ""CommandTimeout"": 30
  }}
}}";
        File.WriteAllText(_tempFile, json);

        var loaded = ConfigurationLoader.Load<ServerConfig>(_tempFile);
        Assert.NotNull(loaded.Database);
        Assert.Equal("plain-password", loaded.Database.ConnectionString);
    }

    [Fact]
    public void Load_With_PlainPasswordField_KeepsAsIs()
    {
        var json = $@"{{
  ""Server"": {{ ""ServerId"": 1, ""ServerName"": ""X"", ""Type"": 0, ""DebugMode"": false }},
  ""Database"": {{
    ""Type"": 0,
    ""ConnectionString"": ""plain-connection-string"",
    ""MaxPoolSize"": 10, ""MinPoolSize"": 1, ""ConnectionTimeout"": 30, ""CommandTimeout"": 30
  }}
}}";
        File.WriteAllText(_tempFile, json);

        var loaded = ConfigurationLoader.Load<ServerConfig>(_tempFile);
        Assert.Equal("plain-connection-string", loaded.Database.ConnectionString);
    }
}

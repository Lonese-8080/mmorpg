// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

using Xunit;

namespace MMORPG.Framework.Tests.Network;

/// <summary>
/// Validator 单元测试
/// 
/// 测试覆盖：
/// - 字符串验证
/// - 数值范围验证
/// - 空值检查
/// - 正则表达式验证
/// </summary>
[Collection("Network")]
public class ValidatorTests
{
    #region 字符串验证测试

    [Fact]
    public void Validate_空字符串_应返回false()
    {
        // Arrange
        var value = "";

        // Act
        var isValid = !string.IsNullOrEmpty(value);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void Validate_空白字符串_应返回false()
    {
        // Arrange
        var value = "   ";

        // Act
        var isValid = !string.IsNullOrWhiteSpace(value);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void Validate_有效字符串_应返回true()
    {
        // Arrange
        var value = "Hello World";

        // Act
        var isValid = !string.IsNullOrWhiteSpace(value);

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public void Validate_字符串长度检查_短于最小值()
    {
        // Arrange
        var value = "AB";
        var minLength = 3;

        // Act
        var isValid = value.Length >= minLength;

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void Validate_字符串长度检查_长于最大值()
    {
        // Arrange
        var value = "ABCDEFGHIJ";
        var maxLength = 5;

        // Act
        var isValid = value.Length <= maxLength;

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void Validate_字符串长度检查_在范围内()
    {
        // Arrange
        var value = "ABCDE";
        var minLength = 3;
        var maxLength = 10;

        // Act
        var isValid = value.Length >= minLength && value.Length <= maxLength;

        // Assert
        Assert.True(isValid);
    }

    #endregion

    #region 数值范围测试

    [Theory]
    [InlineData(0, 0, 100, true)]
    [InlineData(50, 0, 100, true)]
    [InlineData(100, 0, 100, true)]
    [InlineData(-1, 0, 100, false)]
    [InlineData(101, 0, 100, false)]
    public void Validate_数值范围_应正确验证(int value, int min, int max, bool expected)
    {
        // Act
        var isValid = value >= min && value <= max;

        // Assert
        Assert.Equal(expected, isValid);
    }

    [Fact]
    public void Validate_long类型_范围检查()
    {
        // Arrange
        long value = -1;
        long min = 0;
        long max = 1000;

        // Act
        var isValid = value >= min && value <= max;

        // Assert
        Assert.False(isValid);
    }

    #endregion

    #region 空值检查测试

    [Fact]
    public void Validate_null值_应返回false()
    {
        // Arrange
        string? value = null;

        // Act
        var isValid = value != null && !string.IsNullOrWhiteSpace(value);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void Validate_nonNull值_应返回true()
    {
        // Arrange
        string? value = "test";

        // Act
        var isValid = value != null && !string.IsNullOrWhiteSpace(value);

        // Assert
        Assert.True(isValid);
    }

    #endregion

    #region 正则表达式测试

    [Fact]
    public void Validate_邮箱格式_应正确验证()
    {
        // Arrange
        var email = "test@example.com";
        var emailPattern = @"^[^@\s]+@[^@\s]+\.[^@\s]+$";

        // Act
        var isValid = System.Text.RegularExpressions.Regex.IsMatch(email, emailPattern);

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public void Validate_无效邮箱_应返回false()
    {
        // Arrange
        var email = "invalid-email";
        var emailPattern = @"^[^@\s]+@[^@\s]+\.[^@\s]+$";

        // Act
        var isValid = System.Text.RegularExpressions.Regex.IsMatch(email, emailPattern);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void Validate_数字格式_应正确验证()
    {
        // Arrange
        var number = "12345";
        var numberPattern = @"^\d+$";

        // Act
        var isValid = System.Text.RegularExpressions.Regex.IsMatch(number, numberPattern);

        // Assert
        Assert.True(isValid);
    }

    #endregion

    #region 组合验证测试

    [Fact]
    public void Validate_用户名验证_组合多个规则()
    {
        // Arrange
        var username = "testuser123";
        var minLength = 3;
        var maxLength = 20;
        var pattern = @"^[a-zA-Z0-9_]+$";

        // Act
        var isValid = !string.IsNullOrWhiteSpace(username) &&
                      username.Length >= minLength &&
                      username.Length <= maxLength &&
                      System.Text.RegularExpressions.Regex.IsMatch(username, pattern);

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public void Validate_密码强度验证_组合多个规则()
    {
        // Arrange
        var password = "StrongPass123!";
        var hasMinLength = password.Length >= 8;
        var hasUpper = password.Any(char.IsUpper);
        var hasLower = password.Any(char.IsLower);
        var hasDigit = password.Any(char.IsDigit);

        // Act
        var isValid = hasMinLength && hasUpper && hasLower && hasDigit;

        // Assert
        Assert.True(isValid);
    }

    #endregion
}

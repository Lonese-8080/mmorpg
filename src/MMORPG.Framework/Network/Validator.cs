// Copyright (c) 2024-2026 MMORPG Framework Contributors
// SPDX-License-Identifier: MIT

using System.Reflection;
using MMORPG.Framework.Logging;

namespace MMORPG.Framework.Network;

/// <summary>
/// 数据校验结果
/// </summary>
public class ValidationResult
{
    public bool IsValid { get; }
    public string? ErrorMessage { get; }
    public string FieldName { get; }

    private ValidationResult(bool isValid, string? errorMessage, string fieldName)
    {
        IsValid = isValid;
        ErrorMessage = errorMessage;
        FieldName = fieldName;
    }

    public static ValidationResult Success(string fieldName) =>
        new(true, null, fieldName);

    public static ValidationResult Fail(string fieldName, string errorMessage) =>
        new(false, errorMessage, fieldName);
}

/// <summary>
/// 数据校验属性基类
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public abstract class ValidationAttribute : Attribute
{
    public abstract ValidationResult Validate(object? value, string fieldName);
}

/// <summary>
/// 必填校验
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class RequiredAttribute : ValidationAttribute
{
    public override ValidationResult Validate(object? value, string fieldName)
    {
        if (value == null)
            return ValidationResult.Fail(fieldName, "必填字段");

        if (value is string str && string.IsNullOrWhiteSpace(str))
            return ValidationResult.Fail(fieldName, "必填字段不能为空");

        return ValidationResult.Success(fieldName);
    }
}

/// <summary>
/// 字符串长度校验
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class StringLengthAttribute : ValidationAttribute
{
    public int MinLength { get; }
    public int MaxLength { get; }

    public StringLengthAttribute(int maxLength) : this(0, maxLength) { }

    public StringLengthAttribute(int minLength, int maxLength)
    {
        MinLength = minLength;
        MaxLength = maxLength;
    }

    public override ValidationResult Validate(object? value, string fieldName)
    {
        if (value == null)
            return ValidationResult.Success(fieldName);

        if (value is string str)
        {
            if (str.Length < MinLength)
                return ValidationResult.Fail(fieldName, $"长度不能小于 {MinLength}");

            if (str.Length > MaxLength)
                return ValidationResult.Fail(fieldName, $"长度不能大于 {MaxLength}");
        }

        return ValidationResult.Success(fieldName);
    }
}

/// <summary>
/// 数值范围校验
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class RangeAttribute : ValidationAttribute
{
    public long Min { get; }
    public long Max { get; }

    public RangeAttribute(long min, long max)
    {
        Min = min;
        Max = max;
    }

    public override ValidationResult Validate(object? value, string fieldName)
    {
        if (value == null)
            return ValidationResult.Success(fieldName);

        long numericValue;
        try
        {
            numericValue = Convert.ToInt64(value);
        }
        catch
        {
            return ValidationResult.Fail(fieldName, "无效的数值类型");
        }

        if (numericValue < Min)
            return ValidationResult.Fail(fieldName, $"值不能小于 {Min}");

        if (numericValue > Max)
            return ValidationResult.Fail(fieldName, $"值不能大于 {Max}");

        return ValidationResult.Success(fieldName);
    }
}

/// <summary>
/// 数据校验器
/// 
/// 使用反射扫描对象的校验属性，执行所有校验规则
/// </summary>
public static class Validator
{
    private static readonly Dictionary<Type, List<ValidationRule>> _ruleCache = new();

    private class ValidationRule
    {
        public string FieldName { get; set; } = string.Empty;
        public PropertyInfo? Property { get; set; }
        public FieldInfo? Field { get; set; }
        public List<ValidationAttribute> Attributes { get; set; } = new();
    }

    /// <summary>
    /// 校验对象的所有属性/字段
    /// </summary>
    /// <param name="obj">要校验的对象</param>
    /// <returns>校验结果列表</returns>
    public static List<ValidationResult> Validate(object obj)
    {
        if (obj == null)
            return [ValidationResult.Fail("null", "对象不能为空")];

        var type = obj.GetType();
        var rules = GetValidationRules(type);
        var results = new List<ValidationResult>();

        foreach (var rule in rules)
        {
            object? value = rule.Property?.GetValue(obj) ?? rule.Field?.GetValue(obj);

            foreach (var attr in rule.Attributes)
            {
                var result = attr.Validate(value, rule.FieldName);
                if (!result.IsValid)
                    results.Add(result);
            }
        }

        return results;
    }

    /// <summary>
    /// 校验对象并返回是否通过
    /// </summary>
    /// <param name="obj">要校验的对象</param>
    /// <param name="errorMessage">失败时的错误消息（如果校验通过则为 null）</param>
    /// <returns>是否校验通过</returns>
    public static bool TryValidate(object obj, out string? errorMessage)
    {
        var results = Validate(obj);

        if (results.Count == 0)
        {
            errorMessage = null;
            return true;
        }

        errorMessage = string.Join("; ", results.Select(r => $"{r.FieldName}: {r.ErrorMessage}"));
        return false;
    }

    /// <summary>
    /// 获取类型的校验规则（带缓存）
    /// </summary>
    private static List<ValidationRule> GetValidationRules(Type type)
    {
        if (_ruleCache.TryGetValue(type, out var cached))
            return cached;

        var rules = new List<ValidationRule>();

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var attrs = property.GetCustomAttributes<ValidationAttribute>(true);
            if (attrs.Any())
            {
                rules.Add(new ValidationRule
                {
                    FieldName = property.Name,
                    Property = property,
                    Attributes = attrs.ToList()
                });
            }
        }

        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            var attrs = field.GetCustomAttributes<ValidationAttribute>(true);
            if (attrs.Any())
            {
                rules.Add(new ValidationRule
                {
                    FieldName = field.Name,
                    Field = field,
                    Attributes = attrs.ToList()
                });
            }
        }

        _ruleCache[type] = rules;
        return rules;
    }
}
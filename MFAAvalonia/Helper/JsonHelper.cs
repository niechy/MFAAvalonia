using Avalonia.Threading;
using MFAAvalonia.Extensions;
using Newtonsoft.Json;
using System;
using System.IO;

namespace MFAAvalonia.Helper;

public static class JsonHelper
{
    // 加载JSON配置（自动处理线程问题）
    public static T LoadJson<T>(string filePath, T defaultValue = default, params JsonConverter[] converters)
    {
        return LoadJson(filePath, defaultValue, null, converters);
    }

    public static T LoadJson<T>(string filePath, T defaultValue = default, Action? errorHandle = null, params JsonConverter[] converters)
    {
        try
        {
            EnsureDirectory(filePath);
            if (!File.Exists(filePath)) return defaultValue;

            var json = File.ReadAllText(filePath);
            // 先尝试正常反序列化
            return TryDeserialize<T>(json, converters) ?? defaultValue;
        }
        catch (Exception ex) when (IsThreadAccessException(ex))
        {
            // 线程错误：切换到UI线程重试
            return Dispatcher.UIThread.Invoke(() => LoadJson(filePath, defaultValue, errorHandle, converters));
        }
        catch (Exception ex)
        {
            errorHandle?.Invoke();
            LoggerHelper.Error($"配置加载失败：{Path.GetFileName(filePath)}", ex);
            return defaultValue;
        }
    }

    // 保存JSON配置（自动处理线程问题）
    public static void SaveJson<T>(string filePath, T config, params JsonConverter[] converters)
    {
        try
        {
            EnsureDirectory(filePath);
            // 先尝试正常序列化
            var json = TrySerialize(config, converters);
            File.WriteAllText(filePath, json);
        }
        catch (Exception ex) when (IsThreadAccessException(ex))
        {
            // 线程错误：切换到UI线程重试
            Dispatcher.UIThread.Invoke(() => SaveJson(filePath, config, converters));
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"配置保存失败：{Path.GetFileName(filePath)}", ex);
        }
    }

    // 加载配置（基于LoadJson）
    public static T LoadConfig<T>(string configName, T defaultValue = default, params JsonConverter[] converters)
    {
        var filePath = GetConfigPath(configName);
        return LoadJson(filePath, defaultValue, converters);
    }

    // 保存配置（基于SaveJson）
    public static void SaveConfig<T>(string configName, T config, params JsonConverter[] converters)
    {
        var filePath = GetConfigPath(configName);
        SaveJson(filePath, config, converters);
    }

    // 辅助方法：确保目录存在
    private static void EnsureDirectory(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
            LoggerHelper.Info($"自动创建配置目录：{directory}");
        }
    }

    // 辅助方法：获取配置文件路径
    private static string GetConfigPath(string configName)
    {
        var exeDir = Path.GetDirectoryName(AppContext.BaseDirectory);
        var configDir = Path.Combine(exeDir, "config");
        if (!Directory.Exists(configDir))
        {
            Directory.CreateDirectory(configDir);
        }
        return Path.Combine(configDir, $"{configName}.json");
    }

    // 辅助方法：尝试序列化（带线程错误检测）
    private static string TrySerialize<T>(T value, JsonConverter[] converters)
    {
        var settings = GetSerializerSettings(converters);
        return JsonConvert.SerializeObject(value, settings);
    }

    // 辅助方法：尝试反序列化（带线程错误检测）
    private static T? TryDeserialize<T>(string json, JsonConverter[] converters)
    {
        var settings = GetDeserializerSettings(converters);
        return JsonConvert.DeserializeObject<T>(json, settings);
    }

    // 辅助方法：创建JSON序列化设置
    private static JsonSerializerSettings GetSerializerSettings(JsonConverter[] converters)
    {
        var settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Ignore
        };
        if (converters is { Length: > 0 })
        {
            settings.Converters.AddRange(converters);
        }
        return settings;
    }
    
    private static JsonSerializerSettings GetDeserializerSettings(JsonConverter[] converters)
    {
        var settings = new JsonSerializerSettings();
        if (converters is { Length: > 0 })
        {
            settings.Converters.AddRange(converters);
        }
        return settings;
    }
    
    // 辅助方法：判断是否为线程访问错误
    private static bool IsThreadAccessException(Exception ex)
    {
        // 检查是否为Avalonia线程访问异常
        return ex is InvalidOperationException 
            && ex.Message.Contains("Call from invalid thread") 
            || (ex.InnerException != null && IsThreadAccessException(ex.InnerException));
    }
}
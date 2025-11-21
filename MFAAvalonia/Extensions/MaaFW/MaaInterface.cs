using MFAAvalonia.Helper.Converters;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace MFAAvalonia.Extensions.MaaFW;

public partial class MaaInterface
{
    public class MaaInterfaceOptionCase
    {
        [JsonProperty("name")]
        public string? Name { get; set; }

        [JsonProperty("pipeline_override")]
        public Dictionary<string, JToken>? PipelineOverride { get; set; }

        public override string? ToString()
        {
            var settings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.Ignore
            };
            return JsonConvert.SerializeObject(PipelineOverride, settings);
        }
    }

    public class MaaInterfaceOption
    {
        [JsonIgnore]
        public string? Name { get; set; } = string.Empty;
        [JsonProperty("cases")]
        public List<MaaInterfaceOptionCase>? Cases { get; set; }
        [JsonProperty("default_case")]
        public string? DefaultCase { get; set; }

        [JsonProperty("doc")]
        [JsonConverter(typeof(GenericSingleOrListConverter<string>))]
        public List<string>? Document { get; set; }
    }

    public class MaaInterfaceSelectAdvanced
    {
        [JsonProperty("name")]
        public string? Name { get; set; }

        [JsonProperty("data")] public Dictionary<string, string?> Data = new();

        [JsonIgnore] public string PipelineOverride = "{}";

        public override string? ToString()
        {
            return Name ?? string.Empty;
        }
    }

    public class MaaInterfaceSelectOption
    {
        [JsonProperty("name")]
        public string? Name { get; set; }

        [JsonProperty("index")]
        public int? Index { get; set; }

        public override string? ToString()
        {
            return Name ?? string.Empty;
        }
    }

    public class MaaInterfaceTask
    {
        [JsonProperty("name")] public string? Name;
        [JsonProperty("entry")] public string? Entry;
        [JsonConverter(typeof(GenericSingleOrListConverter<string>))] [JsonProperty("doc")]
        public List<string>? Document;
        [JsonProperty("check",
            NullValueHandling = NullValueHandling.Include,
            DefaultValueHandling = DefaultValueHandling.Include)]
        public bool? Check = false;
        [JsonProperty("repeatable")] public bool? Repeatable;
        [JsonProperty("repeat_count")] public int? RepeatCount;
        [JsonProperty("advanced")] public List<MaaInterfaceSelectAdvanced>? Advanced;
        [JsonProperty("option")] public List<MaaInterfaceSelectOption>? Option;

        [JsonProperty("pipeline_override")] public Dictionary<string, JToken>? PipelineOverride;

        public override string ToString()
        {
            var settings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.Ignore
            };

            return JsonConvert.SerializeObject(this, settings);
        }

        /// <summary>
        /// Creates a deep copy of the current <see cref="MaaInterfaceTask"/> instance.
        /// </summary>
        /// <returns>A new <see cref="MaaInterfaceTask"/> instance that is a deep copy of the current instance.</returns>
        public MaaInterfaceTask Clone()
        {
            return JsonConvert.DeserializeObject<MaaInterfaceTask>(ToString()) ?? new MaaInterfaceTask();
        }
    }

    public class MaaInterfaceResource
    {
        [JsonProperty("name")]
        public string? Name { get; set; }

        [JsonConverter(typeof(GenericSingleOrListConverter<string>))]
        [JsonProperty("path")]
        public List<string>? Path { get; set; }
    }

    public class MaaResourceVersion
    {
        [JsonProperty("name")]
        public string? Name { get; set; }
        [JsonProperty("version")]
        public string? Version { get; set; }
        [JsonProperty("url")]
        public string? Url { get; set; }


        public override string? ToString()
        {
            return Version ?? string.Empty;
        }
    }

    public class MaaResourceControllerAdb
    {
        [JsonProperty("input")]
        public long? Input { get; set; }
        [JsonProperty("screencap")]
        public long? ScreenCap { get; set; }
        [JsonProperty("config")]
        public object? Adb { get; set; }
    }

    public class MaaResourceControllerWin32
    {
        [JsonProperty("class_regex")]
        public string? ClassRegex { get; set; }
        [JsonProperty("window_regex")]
        public string? WindowRegex { get; set; }
        [JsonProperty("input")]
        public long? Input { get; set; }
        [JsonProperty("mouse")]
        public long? Mouse { get; set; }
        [JsonProperty("keyboard ")]
        public long? Keyboard { get; set; }
        [JsonProperty("screencap")]
        public long? ScreenCap { get; set; }
    }

    public class MaaInterfaceAgent
    {
        [JsonProperty("child_exec")]
        public string? ChildExec { get; set; }
        [JsonProperty("child_args")]
        [JsonConverter(typeof(GenericSingleOrListConverter<string>))]
        public List<string>? ChildArgs { get; set; }
        [JsonProperty("identifier")]
        public string? Identifier { get; set; }
    }

    public class MaaResourceController
    {
        [JsonProperty("name")]
        public string? Name { get; set; }
        [JsonProperty("type")]
        public string? Type { get; set; }
        [JsonProperty("adb")]
        public MaaResourceControllerAdb? Adb { get; set; }
        [JsonProperty("win32")]
        public MaaResourceControllerWin32? Win32 { get; set; }
    }


    [JsonProperty("interface_version")]
    public int? InterfaceVersion { get; set; }

    [JsonProperty("mirrorchyan_rid")]
    public string? RID { get; set; }

    [JsonProperty("mirrorchyan_multiplatform")]
    public bool? Multiplatform { get; set; }

    [JsonProperty("name")]
    public string? Name { get; set; }

    [JsonProperty("version")]
    public string? Version { get; set; }

    [JsonProperty("mfa_max_version")]
    public string? MFAMaxVersion { get; set; }

    [JsonProperty("mfa_min_version")]
    public string? MFAMinVersion { get; set; }

    [JsonProperty("message")]
    public string? Message { get; set; }

    [JsonProperty("url")]
    public string? Url { get; set; }

    [JsonProperty("custom_title")]
    public string? CustomTitle { get; set; }

    [JsonProperty("default_controller")]
    public string? DefaultController { get; set; }

    [JsonProperty("lock_controller")]
    public bool LockController { get; set; }

    [JsonProperty("controller")]
    public List<MaaResourceController>? Controller { get; set; }
    [JsonProperty("resource")]
    public List<MaaInterfaceResource>? Resource { get; set; }
    [JsonProperty("task")]
    public List<MaaInterfaceTask>? Task { get; set; }

    [JsonProperty("agent")]
    public MaaInterfaceAgent? Agent { get; set; }

    [JsonProperty("advanced")]
    public Dictionary<string, MaaInterfaceAdvancedOption>? Advanced { get; set; }

    [JsonProperty("option")]
    public Dictionary<string, MaaInterfaceOption>? Option { get; set; }

    [JsonExtensionData]
    public Dictionary<string, object> AdditionalData { get; set; } = new();


    [JsonIgnore]
    public Dictionary<string, MaaInterfaceResource> Resources { get; } = new();


    /// <summary>
    /// 替换单个字符串中的 {PROJECT_DIR} 占位符，并标准化为当前系统的路径格式
    /// </summary>
    /// <param name="input">待处理的字符串（可能包含路径片段）</param>
    /// <param name="replacement">占位符替换值（项目目录路径）</param>
    /// <returns>替换后并标准化路径格式的字符串</returns>
    public static string? ReplacePlaceholder(string? input, string? replacement)
    {
        // 处理输入为空的情况，保持原有行为
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        // 处理替换值为 null 的情况（避免 Replace 抛出空引用异常）
        string safeReplacement = replacement ?? string.Empty;

        // 步骤1：替换占位符
        string replaced = input.Replace("{PROJECT_DIR}", safeReplacement);

        // 步骤2：标准化路径分隔符（适配当前操作系统，不检查文件是否存在）
        string normalizedPath = NormalizePathSeparators(replaced);

        return normalizedPath;
    }

    /// <summary>
    /// 替换字符串列表中所有元素的 {PROJECT_DIR} 占位符，并标准化路径格式
    /// </summary>
    /// <param name="inputs">待处理的字符串列表（可能包含路径片段）</param>
    /// <param name="replacement">占位符替换值（项目目录路径）</param>
    /// <returns>处理后的字符串列表</returns>
    public static List<string> ReplacePlaceholder(IEnumerable<string>? inputs, string? replacement)
    {
        if (inputs == null)
            return new List<string>();

        // 复用单个字符串的处理逻辑（自动包含占位符替换和路径标准化）
        return inputs.ToList().ConvertAll(input => ReplacePlaceholder(input, replacement)!);
    }

    /// <summary>
    /// 辅助方法：标准化路径分隔符（核心逻辑）
    /// - 将所有 / 和 \ 统一替换为当前系统的路径分隔符
    /// - 移除连续的重复分隔符（避免 "a//b" 这类无效格式）
    /// - 不改变路径结构、不补全路径、不检查文件存在性
    /// </summary>
    private static string NormalizePathSeparators(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        // 1. 获取当前系统的路径分隔符（Windows 是 \，Linux/macOS 是 /）
        char targetSeparator = Path.DirectorySeparatorChar;

        // 2. 将所有 / 和 \ 统一替换为目标分隔符
        string normalized = path.Replace('/', targetSeparator).Replace('\\', targetSeparator);

        // 3. 移除连续的重复分隔符（例如 "a//b\\\c" → "a/b/c" 或 "a\b\c"）
        StringBuilder sb = new StringBuilder(normalized.Length);
        char lastChar = '\0';
        foreach (char c in normalized)
        {
            // 跳过与上一个字符相同的分隔符
            if (c == targetSeparator && lastChar == targetSeparator)
                continue;

            sb.Append(c);
            lastChar = c;
        }

        return sb.ToString();
    }


    public override string? ToString()
    {
        var settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Ignore
        };

        return JsonConvert.SerializeObject(this, settings);
    }
}

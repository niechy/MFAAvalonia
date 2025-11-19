using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions;
using MFAAvalonia.Helper;
using MFAAvalonia.Helper.ValueType;
using MFAAvalonia.Views.Windows;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace MFAAvalonia.ViewModels.Windows;

// 公告项数据结构
public class AnnouncementItem
{
    public string Title { get; set; }
    public string Content { get; set; }
    public string FilePath { get; set; }
    public DateTime LastModified { get; set; }
}

public partial class AnnouncementViewModel : ViewModelBase
{
    public static readonly string AnnouncementFolder = "Announcement";

    [ObservableProperty] private AvaloniaList<AnnouncementItem> _announcementItems = new();

    [ObservableProperty] private AnnouncementItem _selectedAnnouncement;

    [ObservableProperty] private bool _doNotRemindThisAnnouncementAgain = Convert.ToBoolean(
        GlobalConfiguration.GetValue(ConfigurationKeys.DoNotShowAnnouncementAgain, bool.FalseString));

    partial void OnDoNotRemindThisAnnouncementAgainChanged(bool value)
    {
        GlobalConfiguration.SetValue(ConfigurationKeys.DoNotShowAnnouncementAgain, value.ToString());
    }

    // 加载公告文件夹中的所有md文件
    public AnnouncementViewModel()
    {
        LoadAnnouncements();
    }
    /// <summary>
    /// 从文本内容中提取第一行和剩余内容（兼容多种换行符）
    /// </summary>
    /// <param name="content">完整文本内容</param>
    /// <param name="firstLine">输出第一行内容</param>
    /// <param name="remainingContent">输出第一行之后的内容（包含换行符本身）</param>
    public static void SplitFirstLine(string content, out string firstLine, out string remainingContent)
    {
        if (string.IsNullOrEmpty(content))
        {
            firstLine = "";
            remainingContent = "";
            return;
        }

        // 可能的换行符（按优先级排序，优先匹配最长的 \r\n）
        var newLineCandidates = new[]
        {
            "\r\n",
            "\n",
            "\r"
        };
        int firstNewLineIndex = int.MaxValue;
        string matchedNewLine = null;

        // 找到第一个出现的换行符
        foreach (var nl in newLineCandidates)
        {
            int index = content.IndexOf(nl);
            if (index != -1 && index < firstNewLineIndex)
            {
                firstNewLineIndex = index;
                matchedNewLine = nl;
            }
        }

        // 如果没有找到换行符，整个内容就是第一行
        if (matchedNewLine == null)
        {
            firstLine = content;
            remainingContent = "";
        }
        else
        {
            // 第一行：从开头到换行符之前
            firstLine = content.Substring(0, firstNewLineIndex);
            // 剩余内容：从换行符之后到结尾（包含换行符本身后面的内容）
            remainingContent = content.Substring(firstNewLineIndex + matchedNewLine.Length);
        }
    }

    private void LoadAnnouncements()
    {
        try
        {
            var resourcePath = Path.Combine(AppContext.BaseDirectory, "resource");
            var announcementDir = Path.Combine(resourcePath, AnnouncementFolder);

            if (!Directory.Exists(announcementDir))
            {
                LoggerHelper.Warning($"公告文件夹不存在: {announcementDir}");
                return;
            }

            // 获取所有md文件，按最后修改时间排序（最新的在前）
            var mdFiles = Directory.GetFiles(announcementDir, "*.md")
                .OrderBy(f => Path.GetFileName(f)[0]) // 按文件名的首字母升序排列
                .ToList();
            foreach (var mdFile in mdFiles)
            {
                try
                {
                    var content = File.ReadAllText(mdFile);
                    SplitFirstLine(content, out string firstLine, out string remainingContent);
                    if (!string.IsNullOrWhiteSpace(firstLine))
                    {
                        // 第一行为标题，其余为内容
                        var title = firstLine.TrimStart('#', ' ').Trim();
                        var item = new AnnouncementItem
                        {
                            Title = title,
                            Content = remainingContent,
                            FilePath = mdFile,
                            LastModified = File.GetLastWriteTime(mdFile)
                        };
                        AnnouncementItems.Add(item);
                    }
                }
                catch (Exception ex)
                {
                    LoggerHelper.Error($"读取公告文件失败: {mdFile}, 错误: {ex.Message}");
                }
            }

            // 默认选中第一个公告
            if (AnnouncementItems.Any())
            {
                SelectedAnnouncement = AnnouncementItems[0];
            }
            LoggerHelper.Info("公告数量：" + AnnouncementItems.Count);
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"加载公告文件夹失败: {ex.Message}");
        }
    }

    public static void CheckAnnouncement(bool forceShow = false)
    {
        var viewModel = new AnnouncementViewModel();
        if (forceShow)
        {
            if (!viewModel.AnnouncementItems.Any())
                ToastHelper.Warn(LangKeys.Warning.ToLocalization(), LangKeys.AnnouncementEmpty.ToLocalization());
        }
        else if (viewModel.DoNotRemindThisAnnouncementAgain || !viewModel.AnnouncementItems.Any())
            return;

        try
        {
            var announcementView = new AnnouncementView
            {
                DataContext = viewModel
            };
            announcementView.Show();
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"显示公告窗口失败: {ex.Message}");
        }
    }
}

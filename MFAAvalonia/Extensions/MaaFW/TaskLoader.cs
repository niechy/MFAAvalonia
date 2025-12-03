using MFAAvalonia.Configuration;
using MFAAvalonia.Helper;
using MFAAvalonia.Helper.ValueType;
using MFAAvalonia.ViewModels.Other;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace MFAAvalonia.Extensions.MaaFW;

/// <summary>
/// 任务加载器
/// </summary>
public class TaskLoader(MaaInterface? maaInterface)
{
    public const string NEW_SEPARATOR = "<|||>";
    public const string OLD_SEPARATOR = ":";


    /// <summary>
    /// 加载任务列表
    /// </summary>
    public void LoadTasks(
        List<MaaInterface.MaaInterfaceTask> tasks,
        ObservableCollection<DragItemViewModel> tasksSource,
        ref bool firstTask,
        IList<DragItemViewModel>? oldDrags = null)
    {
        var currentTasks = ConfigurationManager.Current.GetValue(ConfigurationKeys.CurrentTasks, new List<string>());
        if (currentTasks.Count <= 0 || currentTasks.Any(t => t.Contains(OLD_SEPARATOR) && !t.Contains(NEW_SEPARATOR)))
        {
            currentTasks.Clear();
            currentTasks.AddRange(tasks.Select(t => $"{t.Name}{NEW_SEPARATOR}{t.Entry}").Distinct().ToList());
            ConfigurationManager.Current.SetValue(ConfigurationKeys.CurrentTasks, currentTasks);
        }

        var items = ConfigurationManager.Current.GetValue(ConfigurationKeys.TaskItems, new List<MaaInterface.MaaInterfaceTask>()) ?? new List<MaaInterface.MaaInterfaceTask>();
        var drags = (oldDrags?.ToList() ?? new List<DragItemViewModel>()).Union(items.Select(interfaceItem => new DragItemViewModel(interfaceItem))).ToList();

        if (firstTask)
        {
            InitializeResources();
            firstTask = false;
        }

        var (updateList, removeList) = SynchronizeTaskItems(ref currentTasks, drags, tasks);
        ConfigurationManager.Current.SetValue(ConfigurationKeys.CurrentTasks, currentTasks);
        updateList.RemoveAll(d => removeList.Contains(d));

        UpdateViewModels(updateList, tasks, tasksSource);
    }

    private void InitializeResources()
    {
        var allResources = maaInterface?.Resources.Values.Count > 0
            ? maaInterface.Resources.Values.ToList()
            :
            [
                new()
                {
                    Name = "Default",
                    Path = [MaaProcessor.ResourceBase]
                }
            ];

        // 获取当前控制器的名称
        var currentControllerName = GetCurrentControllerName();
        
        // 根据控制器过滤资源
        var filteredResources = FilterResourcesByController(allResources, currentControllerName);

        foreach (var resource in filteredResources)
        {
            resource.InitializeDisplayName();
        }
        Instances.TaskQueueViewModel.CurrentResources = new ObservableCollection<MaaInterface.MaaInterfaceResource>(filteredResources);Instances.TaskQueueViewModel.CurrentResource = ConfigurationManager.Current.GetValue(ConfigurationKeys.Resource, string.Empty);
        if (Instances.TaskQueueViewModel.CurrentResources.Count > 0 && Instances.TaskQueueViewModel.CurrentResources.All(r => r.Name != Instances.TaskQueueViewModel.CurrentResource))
            Instances.TaskQueueViewModel.CurrentResource = Instances.TaskQueueViewModel.CurrentResources[0].Name ?? "Default";
    }

    /// <summary>
    /// 获取当前控制器的名称
    /// </summary>
    private string? GetCurrentControllerName()
    {
        var currentControllerType = Instances.TaskQueueViewModel.CurrentController;
        var controllerTypeKey = currentControllerType.ToJsonKey();
        
        // 从 interface 的 controller 配置中查找匹配的控制器
        var controller = maaInterface?.Controller?.Find(c => 
            c.Type != null && c.Type.Equals(controllerTypeKey, StringComparison.OrdinalIgnoreCase));
        
        return controller?.Name;
    }

    /// <summary>
    /// 根据控制器过滤资源
    /// </summary>
    /// <param name="resources">所有资源列表</param>
    /// <param name="controllerName">当前控制器名称</param>
    /// <returns>过滤后的资源列表</returns>
    public static List<MaaInterface.MaaInterfaceResource> FilterResourcesByController(
        List<MaaInterface.MaaInterfaceResource> resources, 
        string? controllerName)
    {
        return resources.Where(r =>{
            // 如果资源没有指定 controller，则支持所有控制器
            if (r.Controller == null || r.Controller.Count == 0)
                return true;
            
            // 如果当前控制器名称为空，则显示所有资源
            if (string.IsNullOrWhiteSpace(controllerName))
                return true;
            
            // 检查资源是否支持当前控制器
            return r.Controller.Any(c => 
                c.Equals(controllerName, StringComparison.OrdinalIgnoreCase));
        }).ToList();
    }

    private (List<DragItemViewModel> updateList, List<DragItemViewModel> removeList) SynchronizeTaskItems(
        ref List<string> currentTasks,
        IList<DragItemViewModel> drags,
        List<MaaInterface.MaaInterfaceTask> tasks)
    {
        var currentTaskSet = new HashSet<(string Name, string Entry)>(
            currentTasks.Select(t => t.Split(NEW_SEPARATOR, 2)).Where(parts => parts.Length == 2).Select(parts => (parts[0], parts[1])));

        var newDict = tasks.GroupBy(t => (t.Name, t.Entry)).ToDictionary(group => group.Key, group => group.Last());
        var removeList = new List<DragItemViewModel>();
        var updateList = new List<DragItemViewModel>();

        if (drags.Count == 0) return (updateList, removeList);

        foreach (var oldItem in drags)
        {
            if (newDict.TryGetValue((oldItem.InterfaceItem?.Name, oldItem.InterfaceItem?.Entry), out var newItem))
            {
                UpdateExistingItem(oldItem, newItem);
                updateList.Add(oldItem);
            }
            else
            {
                var sameNameTasks = tasks.Where(t => t.Entry == oldItem.InterfaceItem?.Entry).ToList();
                if (sameNameTasks.Any())
                {
                    UpdateExistingItem(oldItem, sameNameTasks.First(), tasks.Any(t => t.Name == sameNameTasks.First().Name));
                    updateList.Add(oldItem);
                }
                else removeList.Add(oldItem);
            }
        }

        foreach (var task in tasks)
        {
            if (!currentTaskSet.Contains((task.Name, task.Entry)))
            {
                updateList.Add(new DragItemViewModel(task));
                currentTasks.Add($"{task.Name}:{task.Entry}");
            }
        }
        return (updateList, removeList);
    }

    private void UpdateExistingItem(DragItemViewModel oldItem, MaaInterface.MaaInterfaceTask newItem, bool updateName = false)
    {
        if (oldItem.InterfaceItem == null) return;
        if (updateName) oldItem.InterfaceItem.Name = newItem.Name;
        else if (oldItem.InterfaceItem.Name != newItem.Name) return;

        oldItem.InterfaceItem.Entry = newItem.Entry;
        oldItem.InterfaceItem.Label = newItem.Label;
        oldItem.InterfaceItem.PipelineOverride = newItem.PipelineOverride;
        oldItem.InterfaceItem.Description = newItem.Description;
        oldItem.InterfaceItem.Document = newItem.Document;
        oldItem.InterfaceItem.Repeatable = newItem.Repeatable;

        UpdateAdvancedOptions(oldItem, newItem);
        UpdateOptions(oldItem, newItem);
    }

    private void UpdateAdvancedOptions(DragItemViewModel oldItem, MaaInterface.MaaInterfaceTask newItem)
    {
        if (newItem.Advanced != null)
        {
            var tempDict = oldItem.InterfaceItem?.Advanced?.ToDictionary(t => t.Name) ?? new Dictionary<string, MaaInterface.MaaInterfaceSelectAdvanced>();
            var advanceds = JsonConvert.DeserializeObject<List<MaaInterface.MaaInterfaceSelectAdvanced>>(JsonConvert.SerializeObject(newItem.Advanced));
            oldItem.InterfaceItem!.Advanced = advanceds?.Select(opt =>
            {
                if (tempDict.TryGetValue(opt.Name ?? string.Empty, out var existing)) opt.Data = existing.Data;
                return opt;
            }).ToList();
        }
        else oldItem.InterfaceItem!.Advanced = null;
    }

    private void UpdateOptions(DragItemViewModel oldItem, MaaInterface.MaaInterfaceTask newItem)
    {
        if (newItem.Option != null)
        {
            var tempDict = oldItem.InterfaceItem?.Option?.ToDictionary(t => t.Name) ?? new Dictionary<string, MaaInterface.MaaInterfaceSelectOption>();
            var options = JsonConvert.DeserializeObject<List<MaaInterface.MaaInterfaceSelectOption>>(JsonConvert.SerializeObject(newItem.Option));
            oldItem.InterfaceItem!.Option = options?.Select(opt =>
            {
                if (tempDict.TryGetValue(opt.Name ?? string.Empty, out var existing))
                {
                    if ((maaInterface?.Option?.TryGetValue(opt.Name ?? string.Empty, out var io) ?? false) && io.Cases is { Count: > 0 }) opt.Index = Math.Min(existing.Index ?? 0, io.Cases.Count - 1);
                    if (existing.Data?.Count > 0) opt.Data = existing.Data;
                    if (existing.SubOptions?.Count > 0) opt.SubOptions = MergeSubOptions(existing.SubOptions);
                }
                else SetDefaultOptionValue(maaInterface, opt);
                return opt;
            }).ToList();
        }
        else oldItem.InterfaceItem!.Option = null;
    }

    private List<MaaInterface.MaaInterfaceSelectOption> MergeSubOptions(List<MaaInterface.MaaInterfaceSelectOption> existingSubOptions)
    {
        return existingSubOptions.Select(subOpt =>
        {
            var newSubOpt = new MaaInterface.MaaInterfaceSelectOption
            {
                Name = subOpt.Name,
                Index = subOpt.Index,
                Data = subOpt.Data?.Count > 0 ? new Dictionary<string, string?>(subOpt.Data) : null
            };
            if ((maaInterface?.Option?.TryGetValue(subOpt.Name ?? string.Empty, out var sio) ?? false) && sio.Cases is { Count: > 0 })
                newSubOpt.Index = Math.Min(subOpt.Index ?? 0, sio.Cases.Count - 1);
            if (subOpt.SubOptions?.Count > 0) newSubOpt.SubOptions = MergeSubOptions(subOpt.SubOptions);
            return newSubOpt;
        }).ToList();
    }

    public static void SetDefaultOptionValue(MaaInterface? @interface, MaaInterface.MaaInterfaceSelectOption option)
    {
        if (!(@interface?.Option?.TryGetValue(option.Name ?? string.Empty, out var io) ?? false)) return;
        var defaultIndex = io.Cases?.FindIndex(c => c.Name == io.DefaultCase) ?? -1;
        if (defaultIndex != -1) option.Index = defaultIndex;
        if (io.IsInput && io.Inputs != null)
        {
            option.Data ??= new Dictionary<string, string?>();
            foreach (var input in io.Inputs)
                if (!string.IsNullOrEmpty(input.Name) && !option.Data.ContainsKey(input.Name))
                    option.Data[input.Name] = input.Default ?? string.Empty;
        }}

    private void UpdateViewModels(IList<DragItemViewModel> drags, List<MaaInterface.MaaInterfaceTask> tasks, ObservableCollection<DragItemViewModel> tasksSource)
    {
        var newItems = tasks.Select(t => new DragItemViewModel(t)).ToList();
        foreach (var item in newItems)
        {
            if (item.InterfaceItem?.Option != null && !drags.Any())item.InterfaceItem.Option.ForEach(option => SetDefaultOptionValue(maaInterface, option));
        }

        tasksSource.Clear();
        foreach (var item in newItems) tasksSource.Add(item);

        if (!Instances.TaskQueueViewModel.TaskItemViewModels.Any())
            Instances.TaskQueueViewModel.TaskItemViewModels = new ObservableCollection<DragItemViewModel>(drags.Any() ? drags : newItems);
    }
}

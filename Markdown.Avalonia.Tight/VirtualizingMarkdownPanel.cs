using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using ColorDocument.Avalonia;
using ColorDocument.Avalonia.DocumentElements;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Markdown.Avalonia
{
    /// <summary>
    /// 虚拟化 Markdown 面板，只渲染可见区域的内容
    /// </summary>
    public class VirtualizingMarkdownPanel : Panel, ILogicalScrollable
    {
        private readonly List<DocumentElement> _allElements = new();
        private readonly Dictionary<int, Control> _realizedElements = new();
        private readonly List<double> _elementHeights = new();
        private readonly List<double> _elementOffsets = new();
        
        private double _estimatedItemHeight = 50; // 估计的元素高度
        private Size _extent;
        private Vector _offset;
        private Size _viewport;
        private bool _canHorizontallyScroll;
        private bool _canVerticallyScroll = true;
        private ScrollViewer? _scrollOwner;
        
        /// <summary>
        /// 是否启用虚拟化
        /// </summary>
        public bool IsVirtualizing { get; set; } = true;
        
        /// <summary>
        /// 预加载的额外元素数量（上下各多少个）
        /// </summary>
        public int OverScanCount { get; set; } = 5;

        public VirtualizingMarkdownPanel()
        {
            // 初始化
        }

        /// <summary>
        /// 设置文档元素
        /// </summary>
        public void SetElements(IEnumerable<DocumentElement> elements)
        {
            // 清理旧元素
            ClearRealizedElements();
            _allElements.Clear();
            _elementHeights.Clear();
            _elementOffsets.Clear();
            
            // 添加新元素
            _allElements.AddRange(elements);
            
            // 初始化高度估计
            double offset = 0;
            foreach (var element in _allElements)
            {
                _elementHeights.Add(_estimatedItemHeight);
                _elementOffsets.Add(offset);
                offset += _estimatedItemHeight;
            }
            
            // 更新范围
            UpdateExtent();
            InvalidateMeasure();
        }

        /// <summary>
        /// 追加文档元素
        /// </summary>
        public void AppendElements(IEnumerable<DocumentElement> elements)
        {
            double offset = _elementOffsets.Count > 0 
                ? _elementOffsets[^1] + _elementHeights[^1] 
                : 0;
            
            foreach (var element in elements)
            {
                _allElements.Add(element);
                _elementHeights.Add(_estimatedItemHeight);
                _elementOffsets.Add(offset);
                offset += _estimatedItemHeight;
            }
            
            UpdateExtent();
            InvalidateMeasure();
        }

        /// <summary>
        /// 清除所有已实现的元素
        /// </summary>
        private void ClearRealizedElements()
        {
            foreach (var kvp in _realizedElements)
            {
                Children.Remove(kvp.Value);
            }
            _realizedElements.Clear();
        }

        /// <summary>
        /// 更新范围大小
        /// </summary>
        private void UpdateExtent()
        {
            double totalHeight = 0;
            if (_elementOffsets.Count > 0 && _elementHeights.Count > 0)
            {
                totalHeight = _elementOffsets[^1] + _elementHeights[^1];
            }
            
            _extent = new Size(Bounds.Width, totalHeight);
            InvalidateScrollable();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            if (!IsVirtualizing || _allElements.Count == 0)
            {
                // 非虚拟化模式：测量所有元素
                return MeasureAllElements(availableSize);
            }
            
            // 虚拟化模式：只测量可见元素
            return MeasureVisibleElements(availableSize);
        }

        private Size MeasureAllElements(Size availableSize)
        {
            double totalHeight = 0;
            double maxWidth = 0;
            
            // 确保所有元素都已实现
            for (int i = 0; i < _allElements.Count; i++)
            {
                var control = RealizeElement(i);
                if (control != null)
                {
                    control.Measure(availableSize);
                    var desiredSize = control.DesiredSize;
                    
                    // 更新实际高度
                    if (i < _elementHeights.Count)
                    {
                        _elementHeights[i] = desiredSize.Height;
                        _elementOffsets[i] = totalHeight;
                    }
                    
                    totalHeight += desiredSize.Height;
                    maxWidth = Math.Max(maxWidth, desiredSize.Width);
                }
            }
            
            _extent = new Size(maxWidth, totalHeight);
            InvalidateScrollable();
            
            return new Size(
                double.IsInfinity(availableSize.Width) ? maxWidth : availableSize.Width,
                double.IsInfinity(availableSize.Height) ? totalHeight : availableSize.Height
            );
        }

        private Size MeasureVisibleElements(Size availableSize)
        {
            // 计算可见范围
            double viewportTop = _offset.Y;
            double viewportBottom = viewportTop + availableSize.Height;
            
            // 找到可见元素的索引范围
            int startIndex = FindElementIndexAtOffset(viewportTop);
            int endIndex = FindElementIndexAtOffset(viewportBottom);
            
            // 添加预加载
            startIndex = Math.Max(0, startIndex - OverScanCount);
            endIndex = Math.Min(_allElements.Count - 1, endIndex + OverScanCount);
            
            // 回收不可见的元素
            var indicesToRemove = _realizedElements.Keys
                .Where(i => i < startIndex || i > endIndex)
                .ToList();
            
            foreach (var index in indicesToRemove)
            {
                VirtualizeElement(index);
            }
            
            // 实现可见元素
            double maxWidth = 0;
            for (int i = startIndex; i <= endIndex && i < _allElements.Count; i++)
            {
                var control = RealizeElement(i);
                if (control != null)
                {
                    control.Measure(new Size(availableSize.Width, double.PositiveInfinity));
                    var desiredSize = control.DesiredSize;
                    
                    // 更新实际高度（如果与估计值不同）
                    if (i < _elementHeights.Count && Math.Abs(_elementHeights[i] - desiredSize.Height) > 1)
                    {
                        double heightDiff = desiredSize.Height - _elementHeights[i];
                        _elementHeights[i] = desiredSize.Height;
                        
                        // 更新后续元素的偏移
                        for (int j = i + 1; j < _elementOffsets.Count; j++)
                        {
                            _elementOffsets[j] += heightDiff;
                        }
                        
                        UpdateExtent();
                    }
                    
                    maxWidth = Math.Max(maxWidth, desiredSize.Width);
                }
            }
            
            return new Size(
                double.IsInfinity(availableSize.Width) ? maxWidth : availableSize.Width,
                double.IsInfinity(availableSize.Height) ? _extent.Height : availableSize.Height
            );
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            _viewport = finalSize;
            
            foreach (var kvp in _realizedElements)
            {
                int index = kvp.Key;
                var control = kvp.Value;
                
                if (index < _elementOffsets.Count && index < _elementHeights.Count)
                {
                    double y = _elementOffsets[index] - _offset.Y;
                    control.Arrange(new Rect(0, y, finalSize.Width, _elementHeights[index]));
                }
            }
            
            return finalSize;
        }

        /// <summary>
        /// 实现（创建）指定索引的元素
        /// </summary>
        private Control? RealizeElement(int index)
        {
            if (index < 0 || index >= _allElements.Count)
                return null;
            
            if (_realizedElements.TryGetValue(index, out var existing))
                return existing;
            
            var element = _allElements[index];
            var control = element.Control;
            
            if (!Children.Contains(control))
            {
                Children.Add(control);}
            
            _realizedElements[index] = control;
            return control;
        }

        /// <summary>
        /// 虚拟化（移除）指定索引的元素
        /// </summary>
        private void VirtualizeElement(int index)
        {
            if (_realizedElements.TryGetValue(index, out var control))
            {
                Children.Remove(control);
                _realizedElements.Remove(index);
            }
        }

        /// <summary>
        /// 查找指定偏移位置的元素索引
        /// </summary>
        private int FindElementIndexAtOffset(double offset)
        {
            if (_elementOffsets.Count == 0)
                return 0;
            
            // 二分查找
            int left = 0;
            int right = _elementOffsets.Count - 1;
            
            while (left < right)
            {
                int mid = (left + right + 1) / 2;
                if (_elementOffsets[mid] <= offset)
                {
                    left = mid;
                }
                else
                {
                    right = mid - 1;
                }
            }
            
            return left;
        }

        #region ILogicalScrollable 实现

        public bool CanHorizontallyScroll
        {
            get => _canHorizontallyScroll;
            set => _canHorizontallyScroll = value;
        }

        public bool CanVerticallyScroll
        {
            get => _canVerticallyScroll;
            set => _canVerticallyScroll = value;
        }

        public bool IsLogicalScrollEnabled => IsVirtualizing;

        public Size ScrollSize => new Size(16, 16);

        public Size PageScrollSize => new Size(_viewport.Width, _viewport.Height);

        public Size Extent => _extent;

        public Vector Offset
        {
            get => _offset;
            set
            {
                if (_offset != value)
                {
                    _offset = value;
                    InvalidateMeasure();
                    InvalidateArrange();
                }
            }
        }

        public Size Viewport => _viewport;

        public event EventHandler? ScrollInvalidated;

        public bool BringIntoView(Control target, Rect targetRect)
        {
            // 查找目标元素的索引
            for (int i = 0; i < _allElements.Count; i++)
            {
                if (ReferenceEquals(_allElements[i].Control, target))
                {
                    double elementTop = _elementOffsets[i];
                    double elementBottom = elementTop + _elementHeights[i];
                    
                    if (elementTop < _offset.Y)
                    {
                        Offset = new Vector(_offset.X, elementTop);}
                    else if (elementBottom > _offset.Y + _viewport.Height)
                    {
                        Offset = new Vector(_offset.X, elementBottom - _viewport.Height);
                    }
                    
                    return true;
                }
            }
            
            return false;
        }

        public Control? GetControlInDirection(NavigationDirection direction, Control? from)
        {
            return null; // 简化实现
        }

        public void RaiseScrollInvalidated(EventArgs e)
        {
            ScrollInvalidated?.Invoke(this, e);
        }

        private void InvalidateScrollable()
        {
            ScrollInvalidated?.Invoke(this, EventArgs.Empty);
        }

        #endregion
    }
}
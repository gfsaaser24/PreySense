using Microsoft.Win32;
using PreySense.Frontend.Controls;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace PreySense.Frontend.Services;

public static class LocalizationService
{
    private const string SettingsKey = @"SOFTWARE\PreySense";
    private const string LanguageValue = "Language";
    private static readonly ConditionalWeakTable<DependencyObject, Dictionary<DependencyProperty, string>> Originals = new();

    private static readonly IReadOnlyDictionary<string, string> Chinese = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [" • Games"] = " • 游戏", [" • Lighting"] = " • 灯光", [" • Metrics"] = " • 监控", [" • Mode"] = " • 模式", [" • Settings"] = " • 设置",
        ["PreySense • Games"] = "PreySense • 游戏", ["PreySense • Lighting"] = "PreySense • 灯光", ["PreySense • Metrics"] = "PreySense • 监控",
        ["PreySense • Mode"] = "PreySense • 模式", ["PreySense • Platform Settings"] = "PreySense • 平台设置",
        ["Language"] = "语言", ["English"] = "英语", ["Simplified Chinese"] = "简体中文",
        ["Games"] = "游戏", ["Game Profiles"] = "游戏配置", ["Add"] = "添加", ["Remove"] = "移除", ["Apply When Game Launches"] = "游戏启动时应用",
        ["Lighting"] = "灯光", ["Keyboard"] = "键盘", ["Logo"] = "徽标", ["Logo Backlight"] = "徽标背光", ["Sync With Keyboard"] = "与键盘同步",
        ["Lighting Mode"] = "灯光模式", ["Effect Mode"] = "效果模式", ["Animation Effect"] = "动画效果", ["Animation Speed"] = "动画速度",
        ["Static"] = "静态", ["Dynamic"] = "动态", ["Brightness"] = "亮度", ["Backlight Timeout"] = "背光超时", ["Sync All Zones"] = "同步所有区域",
        ["Preset Themes"] = "预设主题", ["None"] = "无", ["Zone 1"] = "区域 1", ["Zone 2"] = "区域 2", ["Zone 3"] = "区域 3", ["Zone 4"] = "区域 4",
        ["Breathing"] = "呼吸", ["Neon"] = "霓虹", ["Neon Wave"] = "霓虹波浪", ["Color Wave"] = "彩色波浪", ["Ripple"] = "涟漪", ["Zoom"] = "缩放", ["Snake"] = "贪吃蛇", ["Disco"] = "迪斯科",
        ["Rainbow Cycle"] = "彩虹循环", ["CPU Heatmap"] = "CPU 热力图", ["Battery Fuel Gauge"] = "电量指示", ["Police Strobe"] = "警灯闪烁",
        ["Fire Ember"] = "余烬", ["Matrix Cascade"] = "矩阵瀑布", ["Cylon Scanner"] = "扫描灯", ["Aurora Drift"] = "极光漂移",
        ["Ocean Tide"] = "海洋潮汐", ["Heartbeat"] = "心跳", ["Lightning Storm"] = "雷暴", ["Starlight"] = "星光", ["Ice Crystal"] = "冰晶",
        ["Predator"] = "Predator", ["Cyberpunk"] = "赛博朋克", ["Fire & Ice"] = "冰与火", ["Matrix"] = "矩阵", ["Stealth"] = "隐秘",
        ["Sunset"] = "日落", ["Ocean"] = "海洋", ["Ember"] = "余烬", ["Vaporwave"] = "蒸汽波", ["Royal"] = "皇家", ["Arctic"] = "极地",
        ["Sakura"] = "樱花", ["Rainbow"] = "彩虹", ["Toxic"] = "荧光绿", ["Lava"] = "熔岩", ["Midnight"] = "午夜", ["Dracula"] = "德古拉",
        ["Mode"] = "模式", ["Performance Mode"] = "性能模式", ["Quiet"] = "安静", ["Eco"] = "节能", ["Balanced"] = "均衡", ["Performance"] = "性能", ["Turbo"] = "极速",
        ["Fans"] = "风扇", ["CPU Fan"] = "CPU 风扇", ["GPU Fan"] = "GPU 风扇", ["Custom"] = "自定义", ["Auto"] = "自动",
        ["GPU Mode"] = "GPU 模式", ["Hybrid"] = "混合模式", ["Advanced Optimus"] = "高级 Optimus", ["dGPU Only"] = "仅独立显卡",
        ["CPU Power Limits"] = "CPU 功耗限制", ["GPU Power Limits"] = "GPU 功耗限制", ["GPU Power Limit"] = "GPU 功耗限制",
        ["Sustained Long Power [PL1]"] = "持续功耗 [PL1]", ["Short Burst Power [PL2]"] = "短时突发功耗 [PL2]", ["Burst Peak Power [PL2]"] = "峰值突发功耗 [PL2]",
        ["CPU Boost"] = "CPU 睿频", ["Burst Window [Tau]"] = "突发时间窗口 [Tau]", ["Thermal Trip Point [WMG3]"] = "温度保护点 [WMG3]", ["GPU Thermal Trip Point [WMG3]"] = "GPU 温度保护点 [WMG3]",
        ["Reset"] = "重置", ["Apply"] = "应用", ["Save"] = "保存", ["Cancel"] = "取消", ["Install"] = "安装", ["Uninstall"] = "卸载", ["Quit"] = "退出",
        ["Advanced"] = "高级", ["Run on Windows Startup"] = "随 Windows 启动", ["Theme"] = "主题", ["Light"] = "浅色", ["Dark"] = "深色",
        ["Battery Health Limit"] = "电池健康限制", ["Acer Services"] = "Acer 服务", ["Enabled"] = "已启用", ["Disabled"] = "已禁用",
        ["Acer BlueLight Shield"] = "Acer 蓝光护盾", ["LCD Overdrive"] = "LCD 响应加速", ["USB Power-Off Charging"] = "关机 USB 充电",
        ["ACPI Override"] = "ACPI 覆盖", ["Enable ACPI Override"] = "启用 ACPI 覆盖",
        ["OpenCore Active"] = "OpenCore 已激活", ["OpenCore Not Loaded"] = "OpenCore 未加载",
        ["OpenCore is not loaded. Boot via OpenCore to unlock ACPI control."] = "OpenCore 未加载。请通过 OpenCore 启动以解锁 ACPI 控制。",
        ["ACPI Hardware Override is disabled."] = "ACPI 硬件覆盖已禁用。",
        ["Install to USB Drive"] = "安装到 USB 驱动器", ["Install to Boot Drive"] = "安装到启动驱动器",
        ["Factory Profile Calibration"] = "出厂配置校准", ["Calibrate"] = "校准", ["Not calibrated"] = "尚未校准",
        ["Enable Metrics Overlay"] = "启用监控浮窗", ["Enabled in Games Only"] = "仅在游戏中启用", ["Show FPS"] = "显示 FPS",
        ["Update Interval"] = "更新间隔", ["Background Opacity"] = "背景不透明度", ["HUD Scale"] = "HUD 缩放", ["Horizontal Position"] = "水平位置",
        ["Vertical Position"] = "垂直位置", ["Width"] = "宽度", ["Height"] = "高度", ["Overlay"] = "浮窗", ["Refresh Rate"] = "刷新率",
        ["Profile Mode"] = "配置模式", ["Max"] = "最大",
        ["Predator Cyan"] = "Predator 青色", ["Arctic White"] = "极地白", ["Cyber Neon"] = "赛博霓虹", ["Matrix Green"] = "矩阵绿",
        ["Hellfire Red"] = "地狱火红", ["Lava Orange"] = "熔岩橙", ["Ocean Blue"] = "海洋蓝", ["Sakura Pink"] = "樱花粉",
        ["Sunset Orange"] = "日落橙", ["Tactical Amber"] = "战术琥珀", ["Toxic Lime"] = "荧光青柠", ["Vaporwave Pink"] = "蒸汽波粉",
        ["Deep Violet"] = "深紫", ["Ice Blue"] = "冰蓝", ["Midnight Indigo"] = "午夜靛蓝"
    };

    public static string CurrentLanguage { get; private set; } = "en-US";
    public static bool IsChinese => CurrentLanguage.Equals("zh-CN", StringComparison.OrdinalIgnoreCase);

    public static void Initialize()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(SettingsKey);
        string saved = key?.GetValue(LanguageValue) as string ?? CultureInfo.CurrentUICulture.Name;
        CurrentLanguage = saved.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh-CN" : "en-US";
        ApplyCulture();
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => LocalizeWindow((Window)sender)));
    }

    public static void SetLanguage(string language)
    {
        string normalized = language.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh-CN" : "en-US";
        if (normalized == CurrentLanguage)
            return;
        CurrentLanguage = normalized;
        ApplyCulture();
        using RegistryKey? key = Registry.CurrentUser.CreateSubKey(SettingsKey);
        key?.SetValue(LanguageValue, CurrentLanguage, RegistryValueKind.String);
        foreach (Window window in Application.Current.Windows)
        {
            LocalizeWindow(window);
            if (window.DataContext is ViewModels.MainViewModel vm)
                vm.RefreshLocalizedText();
        }
    }

    public static string Translate(string text) => IsChinese && Chinese.TryGetValue(text, out string? translated) ? translated : text;

    public static void LocalizeWindow(Window window)
    {
        window.Language = System.Windows.Markup.XmlLanguage.GetLanguage(CurrentLanguage);
        Apply(window, Window.TitleProperty);
        LocalizeTree(window.Content as DependencyObject);
    }

    private static void ApplyCulture()
    {
        var culture = CultureInfo.GetCultureInfo(CurrentLanguage);
        CultureInfo.CurrentUICulture = culture;
    }

    private static void LocalizeTree(DependencyObject? element)
    {
        if (element == null) return;
        Apply(element, ToolTipService.ToolTipProperty);

        switch (element)
        {
            case SettingSwitchRow row:
                Apply(row, SettingSwitchRow.TitleProperty); Apply(row, SettingSwitchRow.DescriptionProperty);
                Apply(row, SettingSwitchRow.OnTextProperty); Apply(row, SettingSwitchRow.OffTextProperty); return;
            case SettingDropdownRow row:
                Apply(row, SettingDropdownRow.TitleProperty); Apply(row, SettingDropdownRow.DescriptionProperty); break;
            case SettingActionRow row:
                Apply(row, SettingActionRow.TitleProperty); Apply(row, SettingActionRow.DescriptionProperty); Apply(row, SettingActionRow.ActionTextProperty); return;
            case SettingValueRow row:
                Apply(row, SettingValueRow.TitleProperty); return;
            case LabeledTabSwitcher tabs:
                Apply(tabs, LabeledTabSwitcher.LeftTextProperty); Apply(tabs, LabeledTabSwitcher.RightTextProperty); return;
            case TextBlock text when !BindingOperations.IsDataBound(text, TextBlock.TextProperty): Apply(text, TextBlock.TextProperty); break;
            case HeaderedContentControl header when !BindingOperations.IsDataBound(header, HeaderedContentControl.HeaderProperty): Apply(header, HeaderedContentControl.HeaderProperty); break;
            case ContentControl content when !BindingOperations.IsDataBound(content, ContentControl.ContentProperty): Apply(content, ContentControl.ContentProperty); break;
        }

        int children = VisualTreeHelper.GetChildrenCount(element);
        for (int i = 0; i < children; i++)
            LocalizeTree(VisualTreeHelper.GetChild(element, i));
    }

    private static void Apply(DependencyObject target, DependencyProperty property)
    {
        if (BindingOperations.IsDataBound(target, property) || target.GetValue(property) is not string current || string.IsNullOrWhiteSpace(current))
            return;
        var properties = Originals.GetOrCreateValue(target);
        if (!properties.TryGetValue(property, out string? original))
        {
            original = current;
            properties[property] = original;
        }
        target.SetCurrentValue(property, IsChinese ? Translate(original) : original);
    }
}

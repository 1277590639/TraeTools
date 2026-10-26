using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using TraeTools.Services;
using TraeTools.ViewModels;
using TraeTools.Views;

namespace TraeTools;

public partial class App : Application
{
    /// <summary>系统托盘图标全局引用（TrayIcon 无源头引用会被 GC 回收，故存于此）。</summary>
    public static TrayIcon? TrayRef;

    public App()
    {
        // Avalonia 的 Application 构造函数把 Name 硬编码为 "Avalonia Application"，
        // 而 macOS 顶部那条加粗的应用菜单名取的就是这个属性（不是 Info.plist 的
        // CFBundleName —— 补了 NSPrincipalClass 也无效，实测过）。不覆盖就会一直显示 Avalonia。
        Name = "TraeTools";
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Boot("app-before-mainvm");
            var vm = new MainViewModel();
            Boot("app-after-mainvm");

            // 主题必须在窗口创建前应用：否则会先按默认变体绘制一帧再切过去（可见闪一下）。
            // MainViewModel 的构造已完成 AppConfig.Load()，此处读到的就是用户设置。
            ThemeService.Apply(MainViewModel.AppConfig?.ThemeMode);
            Boot("app-theme-applied");

            desktop.MainWindow = new MainWindow
            {
                DataContext = vm,
            };
            Boot("app-mainwindow-assigned");
        }
        else
        {
            Boot("app-no-desktop-lifetime");
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void Boot(string stage) => Console.Error.WriteLine($"[boot] {stage}");
}
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TraeTools.Models;

namespace TraeTools.ViewModels;

/// <summary>
/// <see cref="MainViewModel"/> 的增量部分：顶栏的「当前账号 + 下拉切换」。
///
/// 下拉列出的是**应用账号**（AppConfig.Accounts，即签到/用量使用的账号），
/// 选中即把该账号设为「当前生效」（影响仪表盘/签到/用量/云端）。
/// 与「账号切换」页的 vault 冷切换是两回事：后者备份/恢复 Trae 客户端登录态，
/// 仍只在切换页操作，不在此处混用，避免"检测不到登录账号"的错觉。
/// </summary>
public partial class MainViewModel
{
    /// <summary>顶栏下拉的应用账号列表。</summary>
    public ObservableCollection<AccountInfo> TopAccounts { get; } = new();

    private bool _suppressTopSelection;

    /// <summary>顶栏下拉当前选中（= 当前生效账号）。写入即应用激活账号。</summary>
    public AccountInfo? TopSelectedAccount
    {
        get => TopAccounts.FirstOrDefault(a => a.IsCurrent);
        set
        {
            if (value == null || _suppressTopSelection) return;
            var cfg = AppConfig;
            if (cfg == null) return;
            var acc = cfg.Accounts.FirstOrDefault(a => a.Id == value.Id);
            if (acc == null) return;
            if (cfg.ActiveAccountId == acc.Id) return;
            cfg.ActiveAccountId = acc.Id;
            try { cfg.Save(); } catch { /* 保存失败不影响切换 */ }
            RefreshTopBar();
            NotifyActiveAccountChanged();
        }
    }

    /// <summary>当前真正生效（激活）的账号文案，用于顶栏右侧提示；无账号时给出明确说明。</summary>
    public string TopActiveAccountText
    {
        get
        {
            var cfg = AppConfig;
            if (cfg == null || cfg.Accounts.Count == 0) return "尚未添加账号";
            var acc = cfg.Accounts.FirstOrDefault(a => a.Id == cfg.ActiveAccountId) ?? cfg.Accounts[0];
            var name = string.IsNullOrEmpty(acc.Name) ? $"账号@{acc.Id[..Math.Min(6, acc.Id.Length)]}" : acc.Name;
            return $"当前生效：{name}";
        }
    }

    /// <summary>切换是否进行中（兼容既有绑定；账号激活为即时操作，恒为 false）。</summary>
    public bool TopIsSwitching => false;

    /// <summary>
    /// 刷新顶栏展示。账号增删、激活账号变化后调用（调用方负责在 UI 线程上调用）。
    /// </summary>
    public void RefreshTopBar()
    {
        try
        {
            var cfg = AppConfig;
            var activeId = cfg?.ActiveAccountId;
            var accounts = cfg?.Accounts ?? new List<TraeCheckin.TraeAccount>();
            if (accounts.Count == 0 && TopAccounts.Count == 0) return;

            _suppressTopSelection = true;
            TopAccounts.Clear();
            var colors = new[] { "#3B82F6", "#10B981", "#F59E0B", "#8B5CF6", "#EC4899" };
            int idx = 0;
            foreach (var acc in accounts)
            {
                idx++;
                var name = string.IsNullOrEmpty(acc.Name) ? $"账号@{acc.Id[..Math.Min(6, acc.Id.Length)]}" : acc.Name;
                bool isActive = cfg != null && (acc.Id == activeId || (string.IsNullOrEmpty(activeId) && idx == 1));
                TopAccounts.Add(new AccountInfo
                {
                    Id = acc.Id,
                    Name = name,
                    Initial = name.Length > 0 ? name[0].ToString() : "?",
                    Color = colors[(idx - 1) % colors.Length],
                    Status = isActive ? "当前生效" : "",
                    StatusType = isActive ? "ok" : "info",
                    IsCurrent = isActive,
                });
            }
            _suppressTopSelection = false;

            OnPropertyChanged(nameof(TopAccounts));
            OnPropertyChanged(nameof(TopSelectedAccount));
            OnPropertyChanged(nameof(TopActiveAccountText));
            OnPropertyChanged(nameof(TopIsSwitching));
        }
        catch { /* 顶栏刷新失败不影响功能 */ }
    }

    /// <summary>
    /// 顶栏「切换」按钮：选中即已生效；这里跳到账号切换页（vault 冷切换在那里操作）。
    /// </summary>
    [RelayCommand]
    private Task SwitchToSelectedAccountAsync()
    {
        Navigate("switch");
        return Task.CompletedTask;
    }
}

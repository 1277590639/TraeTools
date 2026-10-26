# 🛠️ TraeTools

> ✨ 本项目基于 [star620/TraeTools](https://github.com/star620/TraeTools) 二次开发，持续修复 Bug 并新增功能 🎁
> 🎁 修复了一批影响使用的 Bug，并新增云端定时自定义等能力；如果你喜欢这个项目，**顺手点个 Star ⭐✨ 就是最大的鼓励啦～**
>
> 📦 提供三类构建：**Windows 完整版** ／ **macOS 完整版**（Intel 与 Apple Silicon）／ **macOS 菜单栏轻量版 TraeBar**

---

## 🧭 三个包分别是什么（先选对这个）

macOS 上有**两个不同的程序**，别装错：

| | 🪟 **TraeTools** Windows 完整版 | 🍎 **TraeTools** macOS 完整版 | 🍎 **TraeBar** 菜单栏轻量版 |
|---|---|---|---|
| 形态 | 完整窗口应用 | 完整窗口应用 | 菜单栏常驻，**没有主窗口** |
| 技术 | C# / Avalonia | C# / Avalonia（与 Windows 同一套代码） | Swift / AppKit 独立实现 |
| 体积 | 63 MB（zip） | 54 MB（dmg） | 0.2 MB（dmg） |
| 适合谁 | 要全部功能 | 要全部功能 | 只想后台自动签到、不想装大程序 |

**怎么选**：想要和 Windows 一样的界面和功能，装 **TraeTools macOS 版**；只关心"每天自动把签到做掉"、平时不需要打开软件，装 **TraeBar**。两者可以同时安装，互不干扰。

> 💡 双击 TraeBar 后"什么都没发生"是**正常的**——它设计上就没有窗口，去屏幕右上角菜单栏找 ⚡ 图标。


## 🌟 二开版本带来了什么（v1.4.1）

| 类别 | 内容 |
|---|---|
| 🔐 重新登录修复 | 修复「重新登录窗口一闪而过」；新增登录身份校验，杜绝把别的账号的凭据存进当前账号（跨账号污染） |
| 🔑 Session 保护 | 登录时自动补读会话 Cookie（约 14 天续命关键），绝不覆盖已有的有效 Session |
| ✅ 签到成功率 | 修复多页面并发探测/换新触发的接口限流风暴；命中 9074 风控自动换设备号重试（对齐 checkin.py 成熟策略） |
| ⏰ 云端时间自定义 | 云端签到运行时间随便改（北京时间 HH:mm），点「应用到云端」即生效，无需重新部署 |
| 🌐 部署指向本仓库 | fork / 部署 / 应用内更新检查全部指向本仓库，二开链路自洽 |
| 🧹 其他 | 修复每次启动误报「迁移 N 条记录」；快签 10 秒冷却；错误日志不再张冠李戴 |

> 📖 完整的问题分析、取证过程与验证方法见 [修复报告](修复报告-2026-10-25.md)。

---

## 🚀 Trae 一体化管理工具

> Trae 多账号每日签到 · 云端自动签到 · 账号快速切换 · 用量统计
>
> 一个工具覆盖 Trae 账号的「登录 → 签到 → 切换 → 用量查看 → 云端托管」全流程 💪

## ✨ 功能特性

- 📅 **多账号每日签到**：一页管理多个 Trae 账号，一键签到全部启用账号；自动签到可设定时间，到点自动执行
- ☁️ **云端签到（永不掉线）**：一键把签到任务部署到你自己的 GitHub fork（GitHub Actions 定时运行），无需电脑开机；**运行时间可自定义**
- 🔄 **账号切换**：为每个账号备份登录态（vault），一键冷切换客户端账号，失败自动回滚（仅 Windows，macOS 见[功能差异](#-macos-完整版与-windows-的功能差异)）
- 📊 **用量统计**：按会话展示模型 / 输入输出 Token / 缓存命中率 / 积分消耗，并本地持久化；另含「积分快过期提醒」
- 👤 **账号资料**：展示每个账号的昵称 / 脱敏手机号 / 头像 / 学生认证徽章
- 🤖 **飞书推送**：签到结果可实时推送到飞书机器人
- 🪜 **网络代理**：一键启用全局代理（签到 / 用量 / GitHub 接口与登录浏览器统一出口），支持按账号覆盖，多账号可分网降低风控关联
- ⬆️ **自动更新**：内置检查更新与静默替换安装（Windows 换整套文件，macOS 换 .app bundle，失败自动回滚旧版）

## 🖥️ 界面

| 导航 | 说明 |
|---|---|
| 🏠 仪表盘 | 剩余积分、今日签到状态、积分趋势图、账号概览 |
| 📊 用量统计 | 逐会话用量 + 模型分布 + 汇聚统计，全部本地留档 |
| 📅 每日签到 | 本月日历、连签天数、签到记录、一键/自动签到 |
| 🔄 账号切换 | 建档（备份登录态）、一键切换、校验（仅 Windows） |
| ☁️ 云端签到 | GitHub 授权、一键部署、部署状态与日志、签到时间自定义 |
| ⚙️ 设置 | 自动签到配置、Token 信息、飞书推送、账号管理、关于 |

## 📦 下载

到 [Releases](https://github.com/YYY2579/TraeTools/releases) 下载对应平台的包 🎁

| 包 | 平台 | 说明 |
|---|---|---|
| `TraeTools-win-x64.zip` | 🪟 Windows 10/11 x64 | 解压运行，无需安装 .NET 运行时 |
| `TraeTools-macos-arm64.dmg` | 🍎 **macOS 完整版** · Apple Silicon（M1/M2/M3/M4…） | 打开 dmg，把 **TraeTools** 拖入 Applications |
| `TraeTools-macos-x64.dmg` | 🍎 **macOS 完整版** · Intel | 同上 |
| `TraeBar-macos-arm64.dmg` | 🍎 **macOS 菜单栏轻量版** · Apple Silicon | 打开 dmg，把 **TraeBar** 拖入 Applications |
| `TraeBar-macos-x64.dmg` | 🍎 **macOS 菜单栏轻量版** · Intel | 同上 |

> 🍎 macOS 包未做 Apple 公证（公证需要付费开发者账号），所以**首次启动会被系统拦截，这是 macOS 对所有网络下载软件的行为，不是包损坏**。放行方式（二选一）：
> 1. 系统设置 → 隐私与安全性 → 拉到底点「仍要打开」；
> 2. 终端执行（按你装的是哪个改名字）：
>    ```bash
>    xattr -cr /Applications/TraeTools.app   # macOS 完整版
>    xattr -cr /Applications/TraeBar.app     # 菜单栏轻量版
>    ```
>    然后正常双击。

> ⚠️ 别用 `xattr -cr` 去"修"一个真的坏了的包：它只是清掉系统的下载隔离标记，不修任何实际问题。如果清完还是打不开，那是真故障，请开 Issue 附上报错。

## 🍎 macOS 完整版与 Windows 的功能差异

macOS 完整版和 Windows 版是**同一套界面代码**，日常功能一致。三处不同如实列出：

| 功能 | Windows | macOS 完整版 |
|---|---|---|
| 添加账号 / 每日签到 / 用量统计 / 云端签到 / 飞书推送 / 设置 | ✅ | ✅ |
| 应用内自动更新 | ✅ | ✅ |
| 开机自启 | 注册表 | LaunchAgent |
| 凭据本地加密 | DPAPI | AES-256-GCM + 本机密钥文件（权限 600） |
| 🪜 **网络代理** | 全局 + 每账号，**含登录内嵌浏览器** | 签到 / 用量 / GitHub / 飞书**照常生效**；⚠️ **登录浏览器不支持** |
| 🔄 **账号切换** | ✅ | ❌ **尚未开放**，见下 |

**关于代理**：macOS 的 WKWebView 没有可用的连接级代理入口（实测 `ProxySettings` 两种写法都不生效）。为避免"你以为这个账号走了独立代理、实际和其它账号同一个出口 IP"这种更危险的情况，**登录时会直接报错而不是静默直连**。需要分网的话，请先用不带代理的网络完成一次登录，之后签到/用量的代理不受影响。

**关于账号切换**：该功能会改写 Trae 客户端自身的登录态文件，macOS 版尚未在真机上验证完成，因此**当前版本不开放**。已确认客户端数据在 mac 上的位置与 Windows 同构（`~/Library/Application Support/Trae CN`），实现正在进行，上线前会先经只读的建档验证。

> 所有 macOS 构建在发布前都会跑一组自动化验收（编译、真机启动出窗口、登录凭据捕获链路、加密往返、开机自启、更新换包与回滚、TLS 连通性），未通过的包不会出现在 Releases 里。

## 🚀 快速开始

### 方式一：直接使用
在 [Releases](https://github.com/YYY2579/TraeTools/releases) 下载对应平台的包（Windows 需 .NET 9/10 桌面运行时已随包内置）。

### 方式二：源码编译

Windows 与 macOS 完整版共用同一份 C# 代码，需要 **.NET 10 SDK**：

```bash
git clone https://github.com/YYY2579/TraeTools.git
cd TraeTools
dotnet build -c Release
```

macOS 完整版还需要 Xcode Command Line Tools（登录内嵌浏览器是一个 Swift/AppKit 写的独立小宿主，见 `macos-loginhost/`）：

```bash
dotnet publish TraeTools.csproj -c Release -r osx-arm64 --self-contained true \
  -p:PublishSingleFile=false -p:PublishTrimmed=false -o out-macos
bash .github/build_login_host.sh arm64 out-macos
bash .github/pkg_dotnet_dmg.sh arm64 1.5.6 out-macos
```

> 日常不需要本地编译：CI 会在每次推送和每次发版时构建并跑完验收，Releases 里直接下载即可。

菜单栏轻量版 TraeBar 是另一套 Swift 实现，在 [`macos`](https://github.com/YYY2579/TraeTools/tree/macos) 分支独立维护。

## 📖 使用指南

### 1. 添加 / 登录账号
`设置 → 账号管理 → + 添加`：自动打开登录窗口（内嵌浏览器），手机号+验证码登录后自动读取凭证并保存；登录态中的 JWT 仅约 **8 小时**有效，但会同时保存约 **14 天**有效的 `X-Cloudide-Session` 会话 Cookie，过期后用它一键静默续期。**若登录时未能保存 Session，8 小时后就必须重新登录**——登录窗口底部会明确提示是否成功抓到 Session。🔐

### 2. 每日签到
签到页点「一键签到」或开启「每日自动签到」；也可在仪表盘点「立即签到」。签到结果写入本地历史，日历与连签天数随之更新。命中 9074 风控时会自动换设备号重试 🛡️

### 3. 用量统计
进入「用量统计」页面自动同步近 7 天会话；每次会话展示模型、入/出 Token、缓存命中字节与积分消耗，数据按账号本地保留，可追溯历史趋势。

### 4. 云端签到（部署到 GitHub Actions）
1. `云端签到` 页点击「授权 GitHub」（设备码授权，无需密码，仅需 repo 权限）
2. 点击「一键部署到云端」：程序自动 fork 本仓库 → 写入每个账号的 `TRAE_SESSION` / `TRAE_DEVICE_ID` secret → 启用并触发验证运行
3. 在「签到时间」卡片里设置想要的运行时刻（北京时间），点「应用到云端」即生效 ⏰
4. 验证成功即生效：**每天到你设定的时间**自动签到，即使本机关机也不中断 ☁️

部署原理：fork 仓库中的 `.github/workflows/checkin.yml` 定时运行 `checkin.py`，脚本用 `X-Cloudide-Session` 换取新 JWT 后调用签到接口（仅依赖 Python 标准库）。

> 从旧版 TRAE-Checkin 迁移：已部署过旧 fork 的用户，在新版「重新部署」时会提示自动删除旧 fork 并重建，无需手动去网页操作。旧 fork 仓库可自行删除。

### 5. 账号切换（仅 Windows）
`账号切换` 页：为当前登录的客户端「建档」（备份登录态载体）→ 选择目标账号 → 「切换至此」一键冷切换；切换失败会自动回滚到原账号并重启客户端。账号删空后可直接「建档」输入新名称重新建立。

> 🍎 macOS 上此功能尚未开放，原因见[功能差异](#-macos-完整版与-windows-的功能差异)。

## ❓ 常见问题

| 现象 | 说明 / 处理 |
|---|---|
| 签到提示「当前参与用户太多」 | Trae 风控：同设备/网络频繁请求触发。新版已自动换设备号重试；仍失败建议错开高峰、减少账号数、不同账号用独立设备号 |
| 云端部署 HTTP 401 | GitHub 授权失效或权限不足：点「重新授权」；仍失败则自动删除旧 fork 重建 |
| 云端提示「未找到 workflow」 | 旧 fork 早于本仓库添加 checkin.yml：按提示一键「删除并重建 fork」即可自愈 |
| 签到 0 积分 | 当日已签过（重复签到不加分），或非会员当日基础 150 分已到账 |
| Token 失效 | 程序会用 `X-Cloudide-Session` 静默换新；彻底失效时提示重新登录 |
| 重新登录窗口一闪而过？ | v1.4.1 已修复 ✅ 若仍遇到，说明网页里登录了别的账号，窗口会保持打开并红字提示，请退出后改用当前账号手机号登录 |
| 🍎 双击 TraeBar 后什么都没出现？ | **正常**。TraeBar 是菜单栏应用，没有窗口也不显示 Dock 图标，去屏幕右上角菜单栏点 ⚡ 才出菜单。要完整界面请装 TraeTools macOS 版 |
| 🍎 提示"已损坏 / 无法验证开发者"？ | macOS 对所有未公证的网络下载软件都这样，不是包坏了。按[上面](#-macos-完整版与-windows-的功能差异)的放行步骤处理即可 |

> 云端签到凭证 `TRAE_SESSION` 有效期约 14 天，过期后请在软件内重新登录并重新部署。

## 👨‍💻 开发者

- 技术栈：Avalonia 12 / .NET 10 / CommunityToolkit.Mvvm / Sodium.Core；登录内嵌浏览器按平台各一个独立宿主进程 —— Windows 用 WebView2（`TraeTools.LoginHost/`，C#），macOS 用 WKWebView（`macos-loginhost/`，Swift）。两者只靠「命令行参数 + `--out` 回写 JSON」与主程序通信，主程序不关心宿主用什么语言写。菜单栏轻量版 TraeBar 为 Swift/AppKit，在 `macos` 分支
- 目录：`ViewModels/` 页面逻辑、`Services/` 网络与领域服务、`Views/` 界面、`Models/` 数据模型
- CI：`release.yml` 出三平台包；`macos-probe.yml` + `.github/verify_macos_artifacts.sh` 跑 macOS 验收（编译、真机启动出窗口、登录凭据捕获链路、加密往返、开机自启、更新换包与回滚、TLS 连通性）。断言做过变异测试，确认没有"永远绿"的空检查
- 行为默认不对外请求官方非公开接口之外的能力；用量/积分接口为逆向网页接口，字段随官方可能变化，代码均做容错解析

## ⚠️ 免责声明

- 本项目为个人开源工具，与 ByteDance / Trae 官方无任何关系
- 使用自动签到、账号切换等功能请遵守 Trae 服务条款，风险自负
- 请勿出售、外借或用于非法用途；账号凭证仅保存在你自己的电脑/GitHub secret 中

## 🙏 致谢

- [star620/TraeTools](https://github.com/star620/TraeTools)：本项目的前身（TRAE-Checkin + Trea-Switch 的整合重构版），本仓库在其基础上二次开发
- [TRAE-Checkin](https://github.com/star620/TRAE-Checkin)：云端签到体系（checkin.py + GitHub Actions 流程）源自该仓库
- [Trea-Switch](https://github.com/star620/Trea-Switch)：账号切换（vault 建档 / 冷切换 / 守护回滚）能力源自该仓库

## 📄 许可证

[GPL v3](LICENSE) —— 二开与分发请遵循同许可证。

---

<div align="center">

**觉得好用就点个 Star ⭐ 吧～ 感谢每一位支持的朋友 🎉✨**

</div>

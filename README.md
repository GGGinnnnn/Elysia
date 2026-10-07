# 爱莉希雅桌宠 · ElysiaPet

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![WPF](https://img.shields.io/badge/UI-WPF-blueviolet.svg)](https://learn.microsoft.com/dotnet/desktop/wpf/)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%2F11-0078D4.svg)]()
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

> “大好的时光，有爱莉希雅陪着你，每分每秒都很特别哦~ ♪”

集 **智能对话、桌面陪伴、日程管家、生产力快捷工具** 于一体的 Windows 桌面看板娘。

本仓库的前身是一份 **Python / PyQt6** 实现，现已用 **C# / .NET 10 / WPF** 完整重写：
分层架构、`async/await` 取代裸线程、配置原子写入、全量日志，
并打包成**零依赖的单文件 exe**——目标电脑不需要安装 Python 或任何运行时。

旧的 Python 版本已归档到 [`legacy-python/`](legacy-python/)，可以继续参考或运行。

---

## 为什么换语言

原来的 Python 版本功能是齐的，但坑集中在三类地方，靠打补丁很难根治：

| 旧版问题 | 新版怎么解决 |
| --- | --- |
| 单个 `main.py` **1602 行**，界面、业务、系统调用全混在一起 | 拆成 `Models` / `Services` / `Views` 三层，桌宠窗口按职责分成「生命周期 / 布局 / 气泡 / 输入 / 调度 / 交互」几个区域 |
| 每个网络请求都新建一个 `QThread`，无取消、无并发保护 | `HttpClient` + `async/await` + `CancellationTokenSource`，连发消息时旧请求会被主动取消 |
| `except Exception:` 裸捕获、`except: pass`，出错静默 | 精确捕获异常类型 + 全量日志 + 面向用户的中文提示 |
| `int("五分钟")` 直接抛异常，设置悄悄不保存 | `TextParsers.ExtractInt/ExtractDouble` 抽数字 + `Math.Clamp` 钳制范围 |
| `json.dump` 直接覆盖配置，写一半崩溃就全丢 | 先写 `.tmp` 再 `File.Replace` 原子替换；损坏的配置自动备份成 `config.json.broken-时间戳` |
| `requests.post(..., verify=False)` 关掉了证书校验 | 使用 .NET 默认的 TLS 校验，不再弱化安全性 |
| `change_state("idle")` 指向一个不存在的素材 | `StateToAsset()` 统一映射，未知状态回退 `waiting` |
| 整点报时要求 `minute==0 and second==0`，定时器抖一下当天就不报时 | 放宽为「整点后 30 秒内只报一次」，用 `lastChimedHour` 去重 |
| 窗口层级靠比较中文字符串 | `WindowLevel` 枚举 |
| 管理台页码索引手工顺延，插一页就错位 | `DashboardPage` 枚举 + 字典分页，新增页面不会影响其它页 |
| 打包后 `print()` 没有任何输出，出问题无从查起 | 运行目录下的 `elysia.log`（自动轮转）+ 全局异常兜底 |
| 目标机器要装 Python + PyQt6 + requests + pywin32 | 单文件 exe，**零依赖**，双击即用 |

---

## 快速开始

### 下载即用（推荐）

到 **[Releases](https://github.com/GGGinnnnn/Elysia/releases)** 页面下载
`ElysiaPet-v2.0.0-win-x64.zip`，解压后**双击 `ElysiaPet.exe` 即可运行**：

- 单文件自包含，目标电脑**不需要安装 .NET 运行时**；
- 8 个 GIF 表情与图标都已内嵌，**单独这一个文件就能跑**；
- 首次启动会在同目录生成 `config.json`，在管理台「系统设置」里填入 API Key 即可对话。

### 从源码构建（开发）

需要 [.NET 10 SDK](https://dotnet.microsoft.com/download)。

```powershell
git clone https://github.com/GGGinnnnn/Elysia.git
cd Elysia

.\build.ps1              # 编译 Debug
.\build.ps1 -Test        # 编译并运行 68 项自检
.\build.ps1 -Publish     # 发布单文件绿色版到 dist\
dotnet run --project src\ElysiaPet\ElysiaPet.csproj
```

> 如果提示「在此系统上禁止运行脚本」，改用：
> `powershell -ExecutionPolicy Bypass -File .\build.ps1 -Test`，
> 或先执行一次 `set-executionpolicy -scope currentuser RemoteSigned` 放行本地脚本。
> 当然也可以直接用 `dotnet build` / `dotnet run`，不依赖脚本。

### 生成绿色版单文件 exe

```powershell
.\build.ps1 -Publish
```

产物在 `dist\ElysiaPet.exe`（自包含，目标机器不需要安装 .NET）。
**所有素材（8 个 GIF + 图标）都已内嵌进 exe，所以单独复制这一个文件就能运行**，
旁边的 `Assets` 目录只是方便你想换角色时替换同名文件，删掉也不影响使用。

首次发布需要下载 win-x64 运行时包，会慢几分钟，之后就快了。

### 自检模式

```powershell
.\build.ps1 -Test
# 或者
src\ElysiaPet\bin\Debug\net10.0-windows\ElysiaPet.exe --selftest
```

自检会依次验证：心情标签解析、声控日程解析、输入容错、配置钳制与读写、8 个素材可用性
（外部文件与内嵌资源两条路径）、**8 个 GIF 是否真的是可播放的多帧动画**（逐帧解码并校验帧延时）、`GifAnimator` 是否真的在换帧、
桌宠窗口创建、管理台 8 个分页逐个打开、气泡与表情切换、斜杠指令容错、日程写入链路，
以及**用假 HTTP 处理器跑通完整对话链路**
（鉴权头、模型名、人设、历史上下文顺序、401 与非法 JSON 的错误处理）。
还有几项交互回归：输入框 10 秒自动收起、右键单击弹菜单 / 右键拖拽不弹菜单、
**对话结束后回到待机表情**、待机时动画仍在播放。
结果全部写进 `elysia.log`，最后一行会给出失败项数量。当前共 68 项，全部通过。

---

## 目录结构

```
Elysia/
├─ build.ps1                     一键构建 / 自检 / 发布脚本
├─ README.md
├─ PROGRESS.md                   重构过程记录（含旧版问题清单与四轮反馈修复）
├─ LICENSE                       MIT
├─ config.example.json           配置文件字段模板（不含 API Key）
├─ .gitignore
├─ legacy-python/                旧的 Python / PyQt6 实现（已归档）
├─ tools/                        诊断脚本（拖拽复现 / GIF 抓帧 / 右键菜单验证）
└─ src/ElysiaPet/
   ├─ ElysiaPet.csproj           net10.0-windows，UseWPF + UseWindowsForms
   ├─ App.xaml(.cs)              入口、全局异常兜底、服务装配
   ├─ SelfTest.cs                --selftest 自检模式（68 项）
   ├─ GifTest.cs / UiShot.cs / GifFrames.cs   诊断模式
   ├─ Assets/                    图标 + 8 个 GIF 表情素材
   ├─ Models/
   │  ├─ AppConfig.cs            完整配置模型 + Normalize() 范围钳制
   │  └─ PersonaDefaults.cs      爱莉希雅人设、24 小时报时语录、待机语录、斜杠指令表
   ├─ Services/
   │  ├─ ConfigService.cs        配置原子读写 + AppPaths 路径解析
   │  ├─ AiClient.cs             大模型 HTTP 客户端（OpenAI 兼容格式）
   │  ├─ GifAnimator.cs          自研逐帧 GIF 播放器（不依赖 WPF 自动播放）
   │  ├─ AppLog.cs               队列化日志 + 1MB 轮转
   │  ├─ AutostartService.cs     注册表 / 启动文件夹两种自启动
   │  ├─ ProcessLauncher.cs      外部程序与系统工具启动
   │  ├─ WindowLevelService.cs   窗口层级与工具窗口样式（P/Invoke）
   │  ├─ TrayService.cs          系统托盘图标与菜单
   │  ├─ TextParsers.cs          心情标签 / 日程 / 数字解析（纯函数）
   │  └─ IPetHost.cs 等          桌宠与管理台之间的接口约定
   ├─ Theme/Theme.xaml           配色与控件样式
   └─ Views/
      ├─ PetWindow.xaml(.cs)     桌宠本体
      ├─ DashboardWindow.xaml(.cs) 管理台外壳
      └─ Pages/                  8 个分页 + PageBase
```

---

## 功能对照

| 功能 | 说明 |
| --- | --- |
| AI 对话 | 接入任意 OpenAI 兼容接口，默认 `deepseek-flash`；可配置携带的历史轮数 |
| 情绪表情 | 模型回复句尾的心情标签自动转成 8 种 GIF 表情：`waiting`/`cry`/`question`/`wink`/`like`/`speechless`/`hurry`/`sleep` |
| 打字机气泡 | 逐字显示，点一下立刻出全文，再点一下提前消失；气泡出现/消失时桌宠不会跳动 |
| 拖动与缩放 | 左键拖动、右键左右拖动等比缩放、滚轮微调；位置和大小自动记忆 |
| 自动透明 | 默认 30 秒无操作后半透明（0.3），**鼠标移到桌宠身上会立刻恢复全亮**；时长与透明度都可调 |
| 待机表情 | 说完话、气泡淡出后自动回到待机动画；深夜 22:00~08:00 会趴着睡觉 |
| 整点报时 | 24 个小时可分别配置多条语录，随机抽取；可在设置页试听 |
| 日程提醒 | 直接说「12:00提醒我吃饭」即可录入；到点冒泡 + 屏幕中央系统对话框双重提醒 |
| 斜杠指令 | `/终端`、`/任务管理器`、`/计算器` 等 18 个系统工具快捷打开 |
| 快捷启动 | 在管理台绑定任意程序，实时出现在桌宠右键菜单顶部，支持排序与立即测试 |
| 托盘图标 | 常用入口、重置位置与置顶、退出应用 |
| 开机自启 | 注册表方式或启动文件夹快捷方式，二选一，可在设置页查看真实状态 |
| 管理台 | 历史对话 / 自动冒泡 / 系统设置 / 提醒事项 / 报时设置 / 快捷启动 / 功能说明 / 关于 |

---

## 表情动画是怎么实现的

**没有依赖 WPF 的自动 GIF 播放**，因为把 `BitmapImage` 绑到 `Image.Source` 时它并不保证推进动画
（本项目实测只显示其中一帧，桌宠就会「一动不动」）。播放由 `Services/GifAnimator.cs` 自己驱动：

1. 用 `BitmapDecoder` 解码出全部帧；
2. 若存在尺寸小于整张画布的帧（局部更新型 GIF），用 `RenderTargetBitmap` 把当前帧叠到上一帧上合成完整画面；
3. 统一转成 `Pbgra32`，保证索引调色板的透明色被正确展开；
4. 读 `/grctlext/Delay` 得到每帧时长（0 或异常值按 100ms 处理，避免卡住）；
5. 用 30ms 轮询的 `DispatcherTimer` 按累计时间推进帧并循环播放。

素材加载顺序仍是「exe 同目录的外部文件 → 内嵌资源」，所以换角色只需替换同名 GIF。

自检里有三道相关防线：验证素材是多帧动画、验证动画器送出的帧非空、
以及**端到端把窗口真跑 14 轮并比对 `PetImage.Source` 的像素指纹**，
要求至少出现 4 种不同画面 —— 这一项能在程序内部直接证明「桌宠在动」。

---

## 诊断模式

正常使用不会触发，排查问题时很有用：

```powershell
# 自检：68 项检查，结果写进 elysia.log
ElysiaPet.exe --selftest

# 把管理台各分页与桌宠渲染成 PNG，存到 exe 同目录的 shots\ 里
ElysiaPet.exe --shot

# 只放一张 GIF 的极简窗口，用于判断动画渲染是否正常
ElysiaPet.exe --giftest

# 把某个 GIF 的每一帧分别导出成 PNG，确认帧本身有没有问题
ElysiaPet.exe --gifframes
```

`tools\` 目录下还有几个 PowerShell 诊断脚本：

| 脚本 | 用途 |
| --- | --- |
| `tools\diag-drag.ps1` | 启动桌宠后用真实鼠标事件模拟拖拽，逐步打印「窗口位置 / 期望位置 / 偏差」 |
| `tools\diag-gif.ps1` | 抓取窗口像素判断 GIF 是否真的在动 |
| `tools\capture-pet.ps1` | 把桌宠窗口画面存成 PNG |

> 注意：抓动画一定要用屏幕 BitBlt，不能用 `PrintWindow` ——
> 后者对做了透明处理的窗口只会返回一张缓存的静态画面，会误判成「动画不动」。


---

## 配置文件

首次运行会在 **exe 同目录**生成 `config.json`（开发时在 `bin\Debug\net10.0-windows\` 下）。

- 所有字段都有默认值，老配置缺字段也能安全读入。
- 手改配置写坏了不会导致程序崩溃：会备份成 `config.json.broken-<时间戳>` 并重建。
- `window_level` 可选 `AlwaysOnTop` / `AboveNormal` / `DesktopOnly`。
- `api_key` 也可以直接在管理台「系统设置」里填写，保存即生效。

出问题时先看同目录的 **`elysia.log`**，里面记录了启动信息、配置读写、AI 请求状态与全部异常。

---

## 已知说明

- 仅支持 Windows（WPF + 少量 Win32 API），已在 Windows 10/11 + .NET 10 上构建验证。
- 「只显示在桌面」层级走的是 `SetWindowPos(HWND_BOTTOM)`，与旧版行为一致。
  如果桌宠被其它窗口挡住找不到，右键托盘图标选「重置位置与强制置顶」即可召回。
- 支持多显示器：窗口位置与「气泡撑高后的回落」都基于真实显示器枚举，
  拔掉副屏或改分辨率后，桌宠不会跑到屏幕外，也不会把异常坐标写回配置。
- GIF 素材沿用原来那一套（`src/ElysiaPet/Assets/gifs/`）。
  加载顺序是「exe 同目录的外部文件 → 内嵌资源」，所以想换角色，
  替换同目录下的同名 GIF 即可，不需要重新编译；删掉外部文件则自动用内嵌版本。
- 由于是重写版本，**不复用**旧版的 `pet_settings.json`，
  需要在新的 `config.json` 里填 API Key（或在管理台界面里填）。
  字段模板见 [`config.example.json`](config.example.json)。

---

## 致谢与声明

- **GIF 表情素材**来自 B站 up主 **_BLZ_**。如有侵权，请联系作者删除。
- 角色「爱莉希雅」版权归 **miHoYo** 所有，本项目仅供学习交流使用。
- 旧版 Python 实现（`legacy-python/`）为本项目的历史版本，一并保留在仓库中。

本项目基于 [MIT License](LICENSE) 开源。

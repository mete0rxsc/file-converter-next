# 原版功能迁移与验收

目标：.NET 10 / C# / Avalonia，保持原项目的右键菜单入口、窗口结构和操作。功能基线为 Tichau/FileConverter 提交 5dce6c0980635e3a6ae8d8acff235bcc9b1441a4。没有独立转换工作台：不添加文件选择、拖入文件、手动开始转换、主题切换等新交互。Shell 的两种实现和工作进程属于替换故障组件的内部实现。

截至 2026-11-02，本实现尚未满足“全部真实环境验收完成”。已具备源码、发布构建和下面列明的验证证据，禁止把未验收项算作通过。

## 窗口和使用流程

| 上游行为 | 迁移实现 | 验证结果 |
|---|---|---|
| 无参数启动显示右键使用说明及 GIF 演示 | HelpWindow、UsageAnimation；不使用 WpfAnimatedGif | Headless 窗口路由、资源加载及渲染通过，交互动画长时间播放待人工验收 |
| 右键选预设立即转换 | 两种 Shell → 唯一请求文件 → 主进程 → 工作进程 | 原生宿主逐项验证七种图片输出的菜单请求 ID；UI 集成测试实际提交中文 PNG→GIF/WebP/JPG/PNG/ICO/AVIF/PDF 请求，验证输出扩展名和文件存在 |
| 转换主窗口只显示任务、进度、取消、日志和设置入口 | 950×500 MainWindow；原版图标和状态色 | Headless 渲染通过；确认没有转换工作台页签、文件选择器和开始按钮 |
| 单独设置窗口，转换预设 / 应用 / 关于三个页签 | 1050×700 SettingsWindow | 三个页签渲染通过 |
| 修改预设，切换选择保留编辑；保存关闭，直接关闭撤销 | 编辑事务、SettingsSaved、EndSettingsEdit | UI 测试通过 |
| 原版编码模式、声道、角度单选，质量/缩放/码率滑块，高级参数 | 重新实现 Avalonia 控件；原版数值表、标签和模板规格 | 值绑定和预设编辑测试通过；逐控件鼠标操作待人工验收 |
| 预设树创建、复制、删除、文件夹、拖动排序、导入导出 | PresetTreeNode、MoveTreeItem、安全 XML | XML 往返、保存/撤销、移动进文件夹通过；实际鼠标拖动待人工验收 |
| 使用原版翻译和图标 | 资源原样复用，resx 转 JSON | 资源存在及渲染通过；全部语言逐窗人工校对待验收 |
| 原版帮助、日志、关于及升级窗口 | 独立窗口，不放入新增管理页面 | 窗口渲染通过；未配置更新源时明确显示状态 |

## 转换和数据安全

| 功能 | 替代实现 | 验证结果与范围 |
|---|---|---|
| 17 种输出 | FFmpeg 8.1 / Magick.NET 15.0.0 / Ghostscript 10.08.0 | Aac、Avi、Avif、Flac、Gif、Ico、Jpg、Mkv、Mp3、Mp4、Ogg、Ogv、Pdf、Png、Wav、Webm、Webp 均以生成样本转换并回读通过 |
| 原版输入映射及 76 预设 | 嵌入原版 XML，格式映射独立建模 | 数目和 XML 往返通过；73 个非 CD 预设各选一份兼容代表样本通过，未覆盖每种扩展名/编码组合 |
| 音频编码、声道、WAV 位深 | FFmpeg 参数列表 | WAV 8/16/24/32 位回读通过；其余已测媒体预设通过。8 位改为标准 pcm_u8，修正旧错误编码名 |
| 视频质量、速度、缩放、旋转、帧率、自定义 FFmpeg | FfmpegArguments，原版参数语义 | 默认预设代表样本通过；全部边界参数的独立元数据断言仍待补全 |
| PDF、图片及多页输出 | Magick.NET 图片；独立 Ghostscript 渲染/验证 PDF | 两页 PDF 全页输出及全部图片目标格式通过 |
| DVD / VOB | 新版 FFmpeg | 合成 VOB 转换通过；加密/受损 DVD 不属于该样本的验证结论 |
| Office | 独立 STA 工作进程、Windows COM，替换 NetOffice | 本机 Word/docx、Excel/xlsx、PowerPoint/pptx 分别转 PDF 与 PNG 通过；旧 doc/xls/ppt 和 ODF 各格式、多文档并发未全部实测 |
| Office 实例清理 | 根据 COM 实例窗口识别进程；仅结束确认属于任务的新实例 | 常规转换通过；COM 启动或无窗口阶段卡住时的实例清理仍需故障注入验收。默认系统打印机未改动 |
| 硬件加速和回退 | 新版 FFmpeg NVENC / AMF；失败重试软件编码 | RTX 5070 Laptop 实际 NVENC 通过；不支持的 AMF 失败后软件回退通过；AMD 实体硬件编码未验收 |
| CD 抓轨 | Windows 原生设备接口、SafeFileHandle | 已实现；本机无光驱和音频 CD，3 个 CD 预设未实测 |
| 批量与并发 | 主进程有界调度，每任务独立工作进程 | 并发输出目标预留及转换请求通过；大量文件和长时间压力测试未完成 |
| 不覆盖源文件/已有输出 | 任务专属临时目录、CreateNew 预留、提交失败回滚本任务文件 | 同名、12 路并发、取消、损坏输入、多输出提交中途失败等回归通过，源文件保留 |
| 成功后归档/删除 | 全部输出提交后才执行原版后处理 | 核心提交/归档/删除测试通过；真实磁盘写满和断电场景未完成 |
| 旧 XML 迁移、备份、导入导出 | 禁用 DTD/外部实体；版本化 JSON 原子写入 | CoreTests 迁移、损坏配置保护、XML 往返与 XXE 拒绝通过 |
| 路径模板语法 | OutputTemplate | 文件名、扩展名大小写、页号、页数、日期、目录等单测通过；全部原版模板组合待人工比对 |
| 剪贴板与退出倒计时 | Avalonia 文件剪贴板和可取消计时器 | 实现已连接；真实 Windows 剪贴板与倒计时交互未完成验收 |
| 原版命令参数 | --settings、--conversion-preset、--input-files 等 | 解析/窗口路由测试通过；全部命令组合的进程级验收未完成 |

## Shell、安装、更新

| 项目 | 实现与证据 | 状态 |
|---|---|---|
| 故障库替换 | 不引用 SharpShell、旧 FileConverterExtension、NetOffice、Ripper 或 yeti.mmedia；源码重新实现互操作 | 已消除已知完整宽度指针截断写法；不能据此宣称已证明 Kitopia 历史崩溃根因 |
| NativeAOT DLL | 原生导出、COM vtable、宿主生命周期固定驻留、DllCanUnloadNow=S_FALSE | 原生宿主测试通过 |
| C++ DLL | 标准 COM 引用计数及卸载 | 原生宿主测试通过 |
| 共用菜单协议和测试 | 数字 verb（含 Unicode 标志及空 lpVerbW）、64 位 ANSI/Unicode 字符串 verb、请求预设 ID 一致性、编号边界、混合选择、损坏快照、重复菜单、引用计数/卸载、唯一请求文件 | 修复前两种实现均可复现 WebP 请求错发；修复后通过原生宿主回归。宿主使用独立临时快照，不替换用户菜单；发布脚本在打包前执行该检查 |
| Explorer / Kitopia | 只加载新 DLL；不注册旧缺陷扩展 | 用户确认菜单可见但选择不同格式均得到 GIF。本轮查明安装和驻留 DLL 仍为 3.0.0，快照却指向另一份 3.0.1 主程序。3.0.2 使用独立 Shell 路径和新的内部 COM 标识；真实菜单复验及 Kitopia 验收仍待完成 |
| 安装时不关闭 Explorer | 恢复上游 Installer/Product.wxs 的 MSIRESTARTMANAGERCONTROL=Disable；不结束宿主；注册后 SHChangeNotify | 3.0.0→3.0.2 实际升级返回 0，Explorer PID 16964 和启动时间保持不变；新主程序和双 DLL 哈希与发布目录一致 |
| MSI、ZIP | per-user MSI、完整免安装目录与 ZIP，启动条件检查旧版，默认 NativeAOT | 构建、安装及本机 3.0.0→3.0.2 升级通过；用户设置及旧 XML 哈希不变；完整卸载、升级失败回滚及其他账户场景未验收。旧版阻止仅测启动条件属性样例，未注册旧扩展 |
| 更新 | 空仓库不联网；配置 owner/repo 后检查、版本比较、说明、后台下载、取消、SHA-256 校验；依原版在退出时启动 MSI | HTTP 模拟、下载校验及窗口关闭后等待退出的 UI 回归通过；实际 GitHub 测试源发布到安装的完整升级流程未验收 |
| 依赖 | 维护中的版本与 SHA-256 锁定；NuGet 漏洞扫描 | artifacts/dependency-audit.json 未报已知 NuGet 漏洞；原生 delegate 不能由该扫描完全覆盖 |
| 发布许可/签名 | GPLv3、署名、第三方说明随包 | 开发构建未签名；对外发布者仍须提供准确对应的 GPL/AGPL 源码和构建资料 |

## 验证记录

- 核心/媒体/预设/GPU：tests/FileConverter.Tests/TestResults/core-media-final.trx，49 项通过。
- 3.0.2 版本/升级回归：tests/FileConverter.Tests/TestResults/core-shell-fix.trx，22 项通过（包含在上述核心测试范围中，不重复累计）。
- Office：office-safe-identity.trx 的 Word/PowerPoint 两项及 office-excel-stage.trx 的 Excel 一项通过。后者包括两个真实输出，约 108 秒（含样本文档创建）；本机默认网络打印机使 Office 启动变慢。
- 原版窗口/保存撤销/七种图片输出自动转换/退出时更新/不完整注册保护及注册失败回滚：tests/FileConverter.UiTests/TestResults/original-workflow.trx，12 项通过。
- 修改前恢复点及两名子代理的代码审查结果：docs/REVIEW-20261102.md。
- 本机实际升级证据：artifacts/upgrade-3.0.2-result.json、artifacts/msi-upgrade-3.0.2.log；Explorer 未被关闭，安装子进程 Session=1。是否已在该 Explorer 中点击并使用修正版，仍须单独核实。
- 最新窗口截图：artifacts/screenshots/conversion-success.png、settings-0.png、settings-1.png、settings-2.png、help.png、diagnostics.png、upgrade.png、upgrade-ready.png。历史 page-*.png 为已撤回工作台的旧证据，不代表当前界面。
- 安装上下文诊断：artifacts/registry-probe/bin/Release/net10.0-windows/msi.txt 与 normal.txt。独立 MSI 探针在安装服务的用户子进程中可读已有 NativeAOT 处理器；普通工具子进程同 SID、同完整注册表路径却读取不到。探针自身测试键已清理，不据此推断 Explorer 结果或历史崩溃根因。
- 预设逐项代表样本报告：artifacts/preset-acceptance.json。
- Headless 渲染和原生宿主测试不替代 Explorer、Kitopia、真实鼠标拖动、剪贴板与安装交互验收。

## 76 个默认预设逐项记录

每个“通过”仅指兼容代表样本转换完成、引擎回读有效，并非所有输入映射、编码和参数组合均已验收。

| # | 原版名称 | 输出 | 验证 |
|---|---|---|---|
| 1 | To Mkv | Mkv | 代表样本转换及引擎回读通过 |
| 2 | To Mp4 | Mp4 | 代表样本转换及引擎回读通过 |
| 3 | To Mp4 (low quality) | Mp4 | 代表样本转换及引擎回读通过 |
| 4 | To Webm | Webm | 代表样本转换及引擎回读通过 |
| 5 | To Ogv | Ogv | 代表样本转换及引擎回读通过 |
| 6 | To Avi | Avi | 代表样本转换及引擎回读通过 |
| 7 | To Gif | Gif | 代表样本转换及引擎回读通过 |
| 8 | To Gif (low quality) | Gif | 代表样本转换及引擎回读通过 |
| 9 | To Ogg | Ogg | 代表样本转换及引擎回读通过 |
| 10 | To Flac | Flac | 代表样本转换及引擎回读通过 |
| 11 | To Wav | Wav | 代表样本转换及引擎回读通过 |
| 12 | To Mp3 | Mp3 | 代表样本转换及引擎回读通过 |
| 13 | To Aac | Aac | 代表样本转换及引擎回读通过 |
| 14 | Extract DVD to Mp4 | Mp4 | 代表样本转换及引擎回读通过 |
| 15 | Extract DVD to Mkv | Mkv | 代表样本转换及引擎回读通过 |
| 16 | Extract DVD to Flac | Flac | 代表样本转换及引擎回读通过 |
| 17 | Extract DVD to Ogg | Ogg | 代表样本转换及引擎回读通过 |
| 18 | Extract DVD to Mp3 | Mp3 | 代表样本转换及引擎回读通过 |
| 19 | Extract CDA to Flac | Flac | 未验收：缺实体音频 CD |
| 20 | Extract CDA to Ogg | Ogg | 未验收：缺实体音频 CD |
| 21 | Extract CDA to Mp3 | Mp3 | 未验收：缺实体音频 CD |
| 22 | To Png | Png | 代表样本转换及引擎回读通过 |
| 23 | To Png (paged) | Png | 代表样本转换及引擎回读通过 |
| 24 | To Webp | Webp | 代表样本转换及引擎回读通过 |
| 25 | To Avif | Avif | 代表样本转换及引擎回读通过 |
| 26 | To Jpg | Jpg | 代表样本转换及引擎回读通过 |
| 27 | To Ico | Ico | 代表样本转换及引擎回读通过 |
| 28 | To Pdf | Pdf | 代表样本转换及引擎回读通过 |
| 29 | Scale 75%/To Mkv | Mkv | 代表样本转换及引擎回读通过 |
| 30 | Scale 75%/To Mp4 | Mp4 | 代表样本转换及引擎回读通过 |
| 31 | Scale 75%/To Webm | Webm | 代表样本转换及引擎回读通过 |
| 32 | Scale 75%/To Ogv | Ogv | 代表样本转换及引擎回读通过 |
| 33 | Scale 75%/To Avi | Avi | 代表样本转换及引擎回读通过 |
| 34 | Scale 75%/To Png | Png | 代表样本转换及引擎回读通过 |
| 35 | Scale 75%/To Webp | Webp | 代表样本转换及引擎回读通过 |
| 36 | Scale 75%/To Jpg | Jpg | 代表样本转换及引擎回读通过 |
| 37 | Scale 25%/To Mkv | Mkv | 代表样本转换及引擎回读通过 |
| 38 | Scale 25%/To Mp4 | Mp4 | 代表样本转换及引擎回读通过 |
| 39 | Scale 25%/To Webm | Webm | 代表样本转换及引擎回读通过 |
| 40 | Scale 25%/To Ogv | Ogv | 代表样本转换及引擎回读通过 |
| 41 | Scale 25%/To Avi | Avi | 代表样本转换及引擎回读通过 |
| 42 | Scale 25%/To Png | Png | 代表样本转换及引擎回读通过 |
| 43 | Scale 25%/To Webp | Webp | 代表样本转换及引擎回读通过 |
| 44 | Scale 25%/To Jpg | Jpg | 代表样本转换及引擎回读通过 |
| 45 | Scale 720p/To Mkv | Mkv | 代表样本转换及引擎回读通过 |
| 46 | Scale 720p/To Mp4 | Mp4 | 代表样本转换及引擎回读通过 |
| 47 | Scale 720p/To Webm | Webm | 代表样本转换及引擎回读通过 |
| 48 | Scale 720p/To Ogv | Ogv | 代表样本转换及引擎回读通过 |
| 49 | Scale 720p/To Avi | Avi | 代表样本转换及引擎回读通过 |
| 50 | Scale 1080p/To Mkv | Mkv | 代表样本转换及引擎回读通过 |
| 51 | Scale 1080p/To Mp4 | Mp4 | 代表样本转换及引擎回读通过 |
| 52 | Scale 1080p/To Webm | Webm | 代表样本转换及引擎回读通过 |
| 53 | Scale 1080p/To Ogv | Ogv | 代表样本转换及引擎回读通过 |
| 54 | Scale 1080p/To Avi | Avi | 代表样本转换及引擎回读通过 |
| 55 | Rotate left/To Mkv | Mkv | 代表样本转换及引擎回读通过 |
| 56 | Rotate left/To Mp4 | Mp4 | 代表样本转换及引擎回读通过 |
| 57 | Rotate left/To Mp4 (low quality) | Mp4 | 代表样本转换及引擎回读通过 |
| 58 | Rotate left/To Webm | Webm | 代表样本转换及引擎回读通过 |
| 59 | Rotate left/To Ogv | Ogv | 代表样本转换及引擎回读通过 |
| 60 | Rotate left/To Avi | Avi | 代表样本转换及引擎回读通过 |
| 61 | Rotate left/To Png | Png | 代表样本转换及引擎回读通过 |
| 62 | Rotate left/To Webp | Webp | 代表样本转换及引擎回读通过 |
| 63 | Rotate left/To Jpg | Jpg | 代表样本转换及引擎回读通过 |
| 64 | Rotate right/To Mkv | Mkv | 代表样本转换及引擎回读通过 |
| 65 | Rotate right/To Mp4 | Mp4 | 代表样本转换及引擎回读通过 |
| 66 | Rotate right/To Mp4 (low quality) | Mp4 | 代表样本转换及引擎回读通过 |
| 67 | Rotate right/To Webm | Webm | 代表样本转换及引擎回读通过 |
| 68 | Rotate right/To Ogv | Ogv | 代表样本转换及引擎回读通过 |
| 69 | Rotate right/To Avi | Avi | 代表样本转换及引擎回读通过 |
| 70 | Rotate right/To Png | Png | 代表样本转换及引擎回读通过 |
| 71 | Rotate right/To Webp | Webp | 代表样本转换及引擎回读通过 |
| 72 | Rotate right/To Jpg | Jpg | 代表样本转换及引擎回读通过 |
| 73 | Tempo - Pitch/To Mp3 (25% slower) | Mp3 | 代表样本转换及引擎回读通过 |
| 74 | Tempo - Pitch/To Mp3 (25% faster) | Mp3 | 代表样本转换及引擎回读通过 |
| 75 | Tempo - Pitch/To Mp3 (pitched -1st) | Mp3 | 代表样本转换及引擎回读通过 |
| 76 | Tempo - Pitch/To Mp3 (pitched +1st) | Mp3 | 代表样本转换及引擎回读通过 |


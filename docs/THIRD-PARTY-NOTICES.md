# 许可、来源与依赖

本项目采用 GNU GPL version 3。见根目录 LICENSE.md。

## 上游数据复用

来源：[Tichau/FileConverter](https://github.com/Tichau/FileConverter)，提交 5dce6c0980635e3a6ae8d8acff235bcc9b1441a4，作者 Adrien Allard 及上游贡献者，GPLv3。

复用材料：Settings.default.xml 的 76 个预设和参数、原版所有 .resx 字符串（转成 JSON）、ApplicationIcon / PresetIcon / FolderIcon / SettingsIcon、CancelIcon / SuccessIcon / FailIcon / LogIcon / DownloadIcon、FileConverterUsage.gif。按原版 MainWindow / SettingsWindow / HelpWindow / DiagnosticsWindow / UpgradeWindow 的布局和交互规格迁移到 Avalonia。版权和翻译贡献属于原贡献者。原项目 README 的 Localization 部分列有译者。

复用审查与证据：预设和格式数据由 CoreTests 对数目、映射、迁移及 XML 往返验证，PresetAcceptanceTests 实测其中 73 个非 CD 预设；翻译、图标、演示动画及窗口布局由 WindowTests 的启动路由、设置保存/撤销、右键自动转换和渲染验证。音频码率和缩放滑块刻度取自原版模板及 EncodingQualitySliderControl 的纯数值表，控件实现重新编写；不复用其 WPF 二进制或旧媒体库。

审查：数据没有可执行代码；XML 使用禁用 DTD 和外部实体的读取器。默认自定义 FFmpeg 参数检查为转码滤镜参数；以独立参数列表传递。旧 Shell、转换生命周期、COM 互操作、路径正则、注册、线程和进程控制代码均未复制。纯数据和行为规格不构成“已证明所有旧代码无 Bug”。

## 当前依赖

| 依赖 | 版本 | 许可证/来源 |
|---|---|---|
| .NET | 10 | MIT，Microsoft |
| Avalonia | 12.1.4 | MIT，AvaloniaUI |
| CommunityToolkit.Mvvm | 8.4.2 | MIT，.NET Community Toolkit |
| Magick.NET-Q16-AnyCPU | 15.0.0 | Apache-2.0，dlemstra / ImageMagick；原生 delegate 许可见包内声明 |
| FFmpeg Windows GPL build | 8.1，SHA-256 见锁文件 | GPL，BtbN/FFmpeg-Builds；源码及构建脚本由发布者提供 |
| Ghostscript | 10.08.0 | AGPLv3，ArtifexSoftware；独立进程执行 |
| WiX | 6.0.2 | MS-RL，构建工具 |
| xUnit、Microsoft.NET.Test.Sdk、Avalonia.Headless | 工程锁定版本 | 测试用途，不进入发行包 |

禁止依赖：SharpShell、旧 FileConverterExtension、NetOffice、Ripper、yeti.mmedia、Markdown.Xaml、WpfAnimatedGif。其中 SharpShell 有已确认的 64 位指针处理缺陷；其余旧组件被新实现替换，不据此宣称所有这些库均存在同一缺陷。

分发必须随附本项目源码、许可证、第三方声明及相应 GPL/AGPL 源码获取信息。正式发布者应归档与二进制准确对应的 FFmpeg/Ghostscript 源码和构建配置，而不能仅依赖可能变化的 latest 下载链接。

审计记录在 artifacts/dependency-audit.json；NuGet 漏洞源检查不覆盖全部原生 delegate，不能当作零漏洞保证。

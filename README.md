# File Converter Next

[项目仓库](https://github.com/mete0rxsc/file-converter-next) · **V2.0 重构：Mete0r** · Windows x64 · C# / .NET 10 / Avalonia · GPLv3

File Converter Next 是 Windows 文件右键菜单转换工具。选中文件，在右键菜单中选择转换预设，即可开始转换并查看进度；设置窗口用于管理预设和应用选项。直接运行主程序会显示使用说明。

本项目以 [Tichau/FileConverter](https://github.com/Tichau/FileConverter) 的提交 `5dce6c0980635e3a6ae8d8acff235bcc9b1441a4` 为功能基线，保留原版窗口和操作，迁移到 .NET 10 / Avalonia 并替换旧组件。原作者为 Adrien Allard（Tichau）及上游贡献者，重构与后续维护者为 Mete0r。

## 安装与使用

1. 安装 Release 中的 `FileConverterNext-3.0.5-win-x64.msi`。安装向导会显示许可和安装目录页面，默认按当前用户安装并注册菜单。
2. 在资源管理器中选中文件，右键选择 **File Converter Next → 转换预设**。Windows 11 通常需要先点击“显示更多选项”。
3. 转换自动开始。右键菜单中的设置入口可创建、复制、调整、导入和导出预设。

安装沿用原版的 Restart Manager 设置，不要求关闭或重启 Explorer。检测到原版 FileConverter / SharpShell 扩展时，安装会提示先卸载原版；不会自行注销或重新启用旧缺陷扩展。

免安装 ZIP 需完整解压。直接打开 `FileConverterNext.exe` 查看介绍；需要菜单时显式运行：

```powershell
.\FileConverterNext.exe --register-shell-extension
.\FileConverterNext.exe --settings
# 移除免安装版的菜单
.\FileConverterNext.exe --unregister-shell-extension
```

## 格式与兼容性

源码基线包含 **17 种输出格式、76 个默认预设**：AAC、AVI、AVIF、FLAC、GIF、ICO、JPG、MKV、MP3、MP4、OGG、OGV、PDF、PNG、WAV、WEBM、WEBP。输入映射覆盖原版音视频、图片、动画、PDF、Office 文档、DVD/VOB 和音频 CD。

完整发行包包含 FFmpeg、FFprobe、Ghostscript 和 LibreOffice。Office / ODF 文档转换已切换到 LibreOffice 无界面导出，不调用 Microsoft Office COM；但当前 DOC 导出集成测试仍超时且没有生成文件，无打印机和不可达网络打印机环境也未完成验证，因此尚不能称打印机等待问题已修复。CD 抓轨需要实体光驱与音频 CD，硬件编码取决于本机设备和驱动。菜单默认使用 C# NativeAOT DLL，也包含行为一致的原生 C++ 实现。

设置位于 `%LOCALAPPDATA%\FileConverterNext\settings.json`。首次运行会迁移先前重构测试版的设置，保留来源备份和预设 ID；旧 XML 设置仍可读取和导入。原名称仅保留在兼容迁移、旧安装检测、来源署名与历史记录中。

## 修复摘要

| 问题 | 修正 |
|---|---|
| 选择 WebP/JPG 等格式却总调用 GIF 预设 | 修正带 Unicode 标志的数字 verb 解析，正确处理空 `lpVerbW` 与菜单命令编号 |
| 旧菜单 DLL 驻留、快照指向不同版本 EXE | 使用独立 DLL 路径与 COM 标识，菜单快照优先使用已注册的实际主程序 |
| 旧扩展中的完整宽度指针与命令处理缺陷 | 用新 NativeAOT / C++ COM 实现替换 SharpShell，按完整指针宽度区分数字和字符串 verb |
| 安装要求关闭 Explorer | 恢复原版 `MSIRESTARTMANAGERCONTROL=Disable`，不强行结束资源管理器 |
| Office 转换弹出选择/等待打印机 | 已改用随包 LibreOffice 无界面导出，避免调用 Office COM；当前导出测试仍失败，打印机等待问题尚未验收 |
| 失败、取消及多输出提交可能影响源文件 | 使用任务专属临时目录；全部输出成功提交后才执行用户选择的归档或删除 |
| 注册失败留下部分菜单配置 | 注册前预检，写入失败恢复菜单快照及本次涉及的注册信息 |
| 中文 ANSI 帮助文本和部分有效 SID 处理不正确 | 按 Windows ANSI 代码页编码，使用 Windows SID 解析与规范化 |

这些条目包含前序重构版本的修复。**已消除已知指针缺陷，不等于已证明 Kitopia 历史崩溃的根因。** 详细版本记录见 [CHANGELOG.md](CHANGELOG.md)。

## 构建

需要 Windows x64、.NET SDK 10、Visual Studio 2022 Build Tools（C++ x64 工具与 Windows SDK 26100 或更新版本）以及 7-Zip。构建脚本会检查必需工具，依赖版本与 SHA-256 由锁文件约束。

```powershell
.\scripts\Build.ps1
dotnet test '.\tests\FileConverterNext.Tests\FileConverterNext.Tests.csproj' -c Release
dotnet test '.\tests\FileConverterNext.UiTests\FileConverterNext.UiTests.csproj' -c Release
```

主解决方案为 `File Converter Next.sln`，桌面项目为 `File Converter Next/File Converter Next.csproj`。构建生成的可发布资产集中在：

```text
artifacts/release/
  FileConverterNext-3.0.5-win-x64.msi
  FileConverterNext-3.0.5-win-x64.zip
  SHA256SUMS.txt
```

完整免安装文件目录为 `artifacts/publish-next/`，包含用于文档转换的 LibreOffice。发布到 GitHub Release 的步骤、签名与源码分发要求见 [发布说明](docs/RELEASING.md)。介绍页的 **V2.0** 是重构署名，安装包内部版本 **3.0.5** 用于已有 3.x 测试版的升级。

## 更新与验收状态

新配置默认更新仓库为 `mete0rxsc/file-converter-next`；已有配置中的更新源保持用户原值。可在 `settings.json` 的 `Updates.Repository` 查看或修改，设为空可禁用更新源。程序不使用原版项目的更新地址。

历史验收记录包含核心/媒体测试和代表样本的 73 个非 CD 预设；其中 Office 结果来自旧 COM 路径，不代表当前 LibreOffice 实现通过。实体音频 CD、Kitopia 宿主、部分压力场景和完整 GitHub Release 更新流程仍需真实环境验收。不能把源码中存在某项功能视为所有环境已经验证；当前范围及证据见 [功能验收矩阵](docs/ACCEPTANCE.md)。

- [使用、配置与命令行](docs/CONFIGURATION.md)
- [版本记录与 Bug 修复](CHANGELOG.md)
- [发布步骤](docs/RELEASING.md)
- [来源、复用审查及第三方许可](docs/THIRD-PARTY-NOTICES.md)
- [GNU GPLv3](LICENSE.md)

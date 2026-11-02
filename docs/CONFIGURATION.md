# File Converter Next 配置与使用

设置文件：`%LOCALAPPDATA%\FileConverterNext\settings.json`。菜单快照、请求文件和日志均位于同一个数据目录。

首次启动时，若新目录尚无设置，会读取先前重构测试版 `%LOCALAPPDATA%\FileConverter\settings.json`，复制配置并保留来源备份及预设 ID，原文件不修改。旧 `Settings.user.xml` 仍支持读取、迁移与导入导出；迁移保留备份。没有旧设置时载入 76 个默认预设。损坏或版本不兼容的配置不会被默认值覆盖。

## 更新仓库

新配置默认使用本项目仓库，在设置文件的 `Updates.Repository` 查看或修改：

~~~json
{
  "Updates": {
    "Repository": "mete0rxsc/file-converter-next",
    "CheckAtStartup": true
  }
}
~~~

已有配置中的更新源保持用户原值；迁移不会替用户改成新仓库。`Repository` 的格式为 `owner/repo`，设为空则明确显示未配置并停止检查该源；`CheckAtStartup` 控制启动时自动检查。所有软件产品 GitHub、项目和帮助入口均指向 [File Converter Next](https://github.com/mete0rxsc/file-converter-next)，原仓库仅作为来源保留。

更新检查读取 GitHub 最新 Release 的版本和说明，下载过程可取消。上传的 MSI 名称应包含 `x64`，GitHub API 资产必须提供有效的 `sha256:` digest。按原版点击“当我退出 File Converter Next 时安装”后在后台下载；校验成功后在应用正常退出时启动安装程序。若最后一个窗口关闭时仍在下载，会显示进度窗口，关闭该窗口取消更新。程序不访问原版更新源。

发布步骤及签名说明见 [RELEASING.md](RELEASING.md)。当前生成的 MSI 未做代码签名，项目不包含签名证书。

## 两种右键菜单

~~~json
{
  "Shell": {
    "Mode": "NativeAot",
    "DisplayPresetIcons": false
  }
}
~~~

Mode 可为 NativeAot 或 NativeCpp。这是故障库替换的内部实现选项。保存配置后执行：

~~~powershell
& '.\FileConverterNext.exe' --apply-shell-mode
& '.\FileConverterNext.exe' --unregister-shell-extension
~~~

只注册一个处理器。AOT DLL 固定驻留宿主生命周期，不能卸载；C++ 支持 COM 标准卸载。当前载荷目录为 `shell\3.0.5`，DLL 为 `FileConverterNext.Shell.NativeAot.dll` 和 `FileConverterNext.Shell.NativeCpp.dll`。独立路径及 COM 标识避免误用宿主中仍驻留的旧实现，注册完成会发送 Windows 菜单关联变更通知，旧 DLL 的内存由宿主自然退出时释放。

安装工程恢复上游 Installer/Product.wxs 中的 MSIRESTARTMANAGERCONTROL=Disable，不要求关闭或重启 Explorer。Windows 11 的菜单位置与原版一致，位于传统菜单（通常经「显示更多选项」进入）。不会注册默认打开关联。

新版注册前检测原版 FileConverter 和 SharpShell CLSID；检测到原版时需先自行卸载。原名称在此仅用于兼容检测。先前 3.x 重构测试版使用同一 MSI 升级关系，由新安装包处理升级。程序不恢复旧缺陷扩展，不结束 Explorer 或 Kitopia。

## 工具与转换

完整发行包附带 FFmpeg、FFprobe 和 Ghostscript。内部工具路径可通过配置文件 Tools.FFmpeg、Tools.FFprobe、Tools.Ghostscript 指定绝对路径。Office 格式仍需要 Microsoft Office；CD 抓轨需要可读音频 CD 与实体光驱。硬件编码失败会回退软件编码。

输出在任务专属目录转换并验证后提交；已有文件通过数字后缀避让，不覆盖输入。多页文件逐页输出。仅全部结果成功写入后执行该预设选择的源文件归档或删除；默认保留源文件。归档目录必须是输入目录内的子目录。

自定义 FFmpeg 字段是参数，不经过命令解释器；保留原版格式，不包含 ffmpeg.exe、输入路径和最终输出路径。-y 被拒绝以避免覆盖文件。高级参数仍由使用者负责，额外输出应指向独立位置。

## 兼容命令行

常规使用与原版一致：选中文件 → 右键 → File Converter Next → 选择预设。转换自动开始。菜单内的设置入口打开“转换预设 / 应用 / 关于”窗口。直接双击 EXE 显示原版的使用说明和演示。

~~~powershell
& '.\FileConverterNext.exe' --settings
& '.\FileConverterNext.exe' --conversion-preset 'To Mp3' 'C:\Music\sample.wav'
& '.\FileConverterNext.exe' --conversion-preset 'To Png' --input-files 'C:\Temp\files.txt'
& '.\FileConverterNext.exe' --register-shell-extension
& '.\FileConverterNext.exe' --version
~~~

支持 --verbose、--post-install-init 和新版 --request。--input-files 是每行一个路径的 UTF-8 文本；菜单调用使用唯一 JSON 请求文件，避免长命令行和临时文件竞争。

## 构建

需要 .NET SDK 10、Visual Studio 2022 Build Tools 的 C++ x64 工具和 Windows SDK 26100 或更新版本，以及 7-Zip（仅用于提取官方 Ghostscript 发布包）。解决方案为 `File Converter Next.sln`，桌面项目为 `File Converter Next/File Converter Next.csproj`。

~~~powershell
.\scripts\Build.ps1
dotnet test '.\tests\FileConverterNext.Tests\FileConverterNext.Tests.csproj'
dotnet test '.\tests\FileConverterNext.UiTests\FileConverterNext.UiTests.csproj' -c Release
~~~

需要验证旧测试版 AOT 已驻留时的兼容性，可运行 `.\scripts\Test-Shell.ps1 -PreviousAot '旧DLL的完整路径'`。加载只发生在隔离的原生测试宿主内，使用临时菜单快照，不修改用户菜单注册。

`artifacts\publish-next\FileConverterNext.exe` 为完整免安装目录入口。面向 Release 的文件集中在 `artifacts\release\`：`FileConverterNext-3.0.5-win-x64.msi`、`FileConverterNext-3.0.5-win-x64.zip` 和 `SHA256SUMS.txt`。介绍页的 V2.0 表示 Mete0r 的重构版本署名；3.0.5 为安装及更新检查使用的内部版本号，避免对先前 3.x 测试版形成降级。

依赖版本及 SHA-256 锁定在 scripts\dependencies.lock.json。FFmpeg 发布者的 latest 资产可能变更；脚本会拒绝与锁定哈希不符的文件，须经审查后更新锁文件，不自动信任新文件。

运行测试前请先构建完整发行包，使转换测试可以找到 Worker、FFmpeg、FFprobe 和 Ghostscript。硬件、Office、光驱及真实 Shell 宿主的验收需要对应环境，见 [验收矩阵](ACCEPTANCE.md)。历史验收文档中的旧项目路径是当时证据，不是当前构建命令。

# 配置与使用

设置文件：%LOCALAPPDATA%\FileConverter\settings.json。首次启动从旧 Settings.user.xml 迁移并保留 .bak；不存在旧设置则载入 76 个默认预设。损坏的设置不会自动覆盖。

## 以后填写更新仓库的位置

创建自己的 GitHub 仓库后，在此配置文件的 Updates.Repository 填写「你的用户名/仓库名」：

~~~json
{
  "Updates": {
    "Repository": "你的用户名/仓库名",
    "CheckAtStartup": true
  }
}
~~~

当前 Repository 留空。程序不会访问原版的更新地址。发布 GitHub Release，上传名称包含 x64 的 .msi 资产；GitHub 资产必须提供 sha256 digest。检查读取最新版本及更新说明，下载过程可取消。按原版点击“当我退出 File Converter 时安装”后在后台下载；SHA-256 校验成功后，在应用退出时启动安装程序。若最后一个窗口关闭时仍在下载，会显示升级进度窗口；关闭该窗口取消更新。正式发布前自行签名 MSI；仓库中没有签名证书。

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
& '.\FileConverter.exe' --apply-shell-mode
& '.\FileConverter.exe' --unregister-shell-extension
~~~

只注册一个处理器。AOT DLL 固定驻留宿主生命周期，不能卸载；C++ 支持 COM 标准卸载。3.0.2 使用 shell\3.0.2 独立目录及新的内部 COM 标识，避免继续使用之前测试版已驻留的错误实现。注册完成会发送 Windows 菜单关联变更通知。旧 DLL 的内存由宿主自然退出时释放。

安装工程恢复上游 Installer/Product.wxs 中的 MSIRESTARTMANAGERCONTROL=Disable，不要求关闭或重启 Explorer。Windows 11 的菜单位置与原版一致，位于传统菜单（通常经「显示更多选项」进入）。不会注册默认打开关联。

新版注册前检测旧 FileConverter 和 SharpShell CLSID；存在旧版时需要先自行卸载旧版。程序不自动卸载旧版、不恢复旧缺陷扩展、不结束 Explorer 或 Kitopia。

## 工具与转换

完整发行包附带 FFmpeg、FFprobe 和 Ghostscript。内部工具路径可通过配置文件 Tools.FFmpeg、Tools.FFprobe、Tools.Ghostscript 指定绝对路径。Office 格式仍需要 Microsoft Office；CD 抓轨需要可读音频 CD 与实体光驱。硬件编码失败会回退软件编码。

输出在任务专属目录转换并验证后提交；已有文件通过数字后缀避让，不覆盖输入。多页文件逐页输出。仅全部结果成功写入后执行该预设选择的源文件归档或删除；默认保留源文件。归档目录必须是输入目录内的子目录。

自定义 FFmpeg 字段是参数，不经过命令解释器；保留原版格式，不包含 ffmpeg.exe、输入路径和最终输出路径。-y 被拒绝以避免覆盖文件。高级参数仍由使用者负责，额外输出应指向独立位置。

## 兼容命令行

常规使用与原版一致：选中文件 → 右键 → File Converter → 选择预设。转换自动开始。菜单内的设置入口打开“转换预设 / 应用 / 关于”窗口。直接双击 EXE 显示原版的使用说明和演示。

~~~powershell
& '.\FileConverter.exe' --settings
& '.\FileConverter.exe' --conversion-preset 'To Mp3' 'C:\Music\sample.wav'
& '.\FileConverter.exe' --conversion-preset 'To Png' --input-files 'C:\Temp\files.txt'
& '.\FileConverter.exe' --register-shell-extension
& '.\FileConverter.exe' --version
~~~

支持 --verbose、--post-install-init 和新版 --request。--input-files 是每行一个路径的 UTF-8 文本；菜单调用使用唯一 JSON 请求文件，避免长命令行和临时文件竞争。

## 构建

需要 .NET SDK 10、Visual Studio 2022 Build Tools 的 C++ x64 工具和 Windows SDK 26100 或更新版本，以及 7-Zip（仅用于提取官方 Ghostscript 发布包）。

~~~powershell
.\scripts\Build.ps1
dotnet test '.\tests\FileConverter.Tests\FileConverter.Tests.csproj'
dotnet test '.\tests\FileConverter.UiTests\FileConverter.UiTests.csproj' -c Release
~~~

需要验证旧测试版 AOT 已驻留时的兼容性，可运行 `.\scripts\Test-Shell.ps1 -PreviousAot '旧DLL的完整路径'`。加载只发生在隔离的原生测试宿主内，使用临时菜单快照，不修改用户菜单注册。

artifacts\publish\FileConverter.exe 为完整免安装目录的入口，artifacts\FileConverter-3.0.2-win-x64.zip 为压缩包，installer\bin\x64\Release\*.msi 为安装包。

依赖版本及 SHA-256 锁定在 scripts\dependencies.lock.json。FFmpeg 发布者的 latest 资产可能变更；脚本会拒绝与锁定哈希不符的文件，须经审查后更新锁文件，不自动信任新文件。

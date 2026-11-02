# File Converter Next 发布说明

项目仓库：[mete0rxsc/file-converter-next](https://github.com/mete0rxsc/file-converter-next)。GitHub Actions 在推送与源码版本一致的 `v*` 标签时构建并发布 MSI、ZIP 和 SHA-256 清单。核心与 UI 测试失败会阻止发布；Office 集成测试单独记录实际结果。当前流程固定发布为 prerelease，不设为 latest，因为实体设备、打印机环境及安装交互验收尚未完成。

## 版本与文件

本次内部版本为 **3.0.5**，建议使用 tag **`v3.0.5`**。介绍页的 **V2.0 重构 · Mete0r** 是重构署名，不替代 MSI 的数值版本。

构建后，发布文件位于仓库根目录的 `artifacts/release/`：

| 文件 | 用途 |
|---|---|
| `FileConverterNext-3.0.5-win-x64.msi` | Windows x64 按用户安装包，默认注册右键菜单 |
| `FileConverterNext-3.0.5-win-x64.zip` | 完整免安装目录；用户通过显式命令注册菜单 |
| `SHA256SUMS.txt` | 对应上述发布资产的 SHA-256 校验清单 |

中间 MSI 位于 `installer/bin/x64/Release/`，完整免安装目录位于 `artifacts/publish-next/`。向 Release 上传时使用 `artifacts/release/` 中的最终文件。不要将 PDB、obj、日志、测试截图、用户配置或签名私钥作为应用资产上传。

## 构建与核对

环境要求：Windows x64、.NET SDK 10、Visual Studio 2022 Build Tools 的 C++ x64 工具、Windows SDK 26100 或更新版本，以及 7-Zip。依赖由 NuGet `packages.lock.json` 和 `scripts/dependencies.lock.json` 锁定；构建时使用 lessmsi 按原目录结构解包官方 LibreOffice MSI，转换工具随发行包提供。

```powershell
.\scripts\Build.ps1
dotnet test '.\tests\FileConverterNext.Tests\FileConverterNext.Tests.csproj' -c Release
dotnet test '.\tests\FileConverterNext.UiTests\FileConverterNext.UiTests.csproj' -c Release
Get-FileHash '.\artifacts\release\FileConverterNext-3.0.5-win-x64.msi' -Algorithm SHA256
Get-FileHash '.\artifacts\release\FileConverterNext-3.0.5-win-x64.zip' -Algorithm SHA256
```

校验结果应与 `SHA256SUMS.txt` 相同。FFmpeg 的上游 `latest` 资产可能变更；哈希不一致时构建必须停止，由维护者重新审查并更新锁定信息，不可跳过校验。

实际发布前检查以下项目，并将结果记录在验收文档中：

- 新安装、旧重构版本升级、卸载、旧原版阻止条件，以及用户设置与预设迁移。
- 在安装向导中更改安装目录，确认最终文件、快捷方式、Shell 注册与卸载都使用所选目录。
- LibreOffice headless 实测 DOC/DOCX/XLS/XLSX/PPT/PPTX/ODT/ODS/ODP 导出 PDF 和图片；在无打印机及不可达网络打印机环境确认没有连接提示。
- NativeAOT / C++ 隔离宿主测试，资源管理器中的菜单、预设选择及实际输出。
- 中文、空格、长路径、多输出、同名文件、取消和失败后源文件保持。
- 需要宣传为已验证的 Office、硬件编码、实体音频 CD、Kitopia 宿主及更新流程，在具备依赖的环境中实际验收。

已有历史测试不能替代本版本的安装和宿主验收。未具备环境的项目应明确标为未完成，不以“全功能已通过”发布。详细范围见 [ACCEPTANCE.md](ACCEPTANCE.md)。

## 签名与校验清单

当前构建不包含代码签名证书，生成的 MSI 为**未签名**文件。维护者如需正式签名，应使用自己的证书和可信时间戳，在所有构建操作完成后对最终 MSI 签名。证书和私钥应存放于仓库外，`.gitignore` 已忽略常见证书格式。

签名会改变文件内容；签名后必须重新计算 `SHA256SUMS.txt`，并用签名后的 MSI 替换所有待上传副本。不要对文件签名或修改后继续使用旧哈希。

可在发布目录生成校验清单：

```powershell
$releaseDirectory = Join-Path (Get-Location) 'artifacts\release'
$assetNames = @('FileConverterNext-3.0.5-win-x64.msi', 'FileConverterNext-3.0.5-win-x64.zip')
$checksumLines = foreach ($assetName in $assetNames) {
    $assetPath = Join-Path $releaseDirectory $assetName
    '{0}  {1}' -f (Get-FileHash -LiteralPath $assetPath -Algorithm SHA256).Hash.ToLowerInvariant(), $assetName
}
[IO.File]::WriteAllLines((Join-Path $releaseDirectory 'SHA256SUMS.txt'), $checksumLines, [Text.UTF8Encoding]::new($false))
```

## 自动发布到 GitHub Release

1. 将完成审查的源码提交并推送到本项目仓库。
2. 在对应提交创建并推送数值版本标签，例如 `v3.0.5`；这会启动 `.github/workflows/release.yml`。
3. Actions 在 Windows runner 上构建 MSI/ZIP、运行核心和 UI 测试，并执行 Office 格式集成测试。
4. 核心或 UI 测试失败时不会创建 Release。Office 测试结果会写入发布说明；当前只创建 prerelease，即使其自动测试通过，也不代表打印机及真实安装环境验收完成。NVIDIA 实体硬件测试在托管 runner 中排除。
5. 下载工作流发布的 MSI、ZIP 和 `SHA256SUMS.txt` 并核对哈希。MSI 的 GitHub API `digest` 也必须是最终文件的 `sha256:…`，因为应用更新器会校验该字段。
6. 独立验收更新说明、下载、取消、哈希验证和退出后安装，再根据证据更新验收文档。

手动运行 Actions 时也必须在 Run workflow 的分支/标签选择器中选择对应版本标签。上传先写入草稿，核对所有资产的 GitHub API SHA-256 digest 后再公开；上传失败可重跑来恢复草稿。已公开版本不会被覆盖，应使用新的源码版本与标签发布后续修正。

应用检查 `repos/mete0rxsc/file-converter-next/releases/latest`；tag 应为可解析的数值版本（如 `v3.0.5`）。预发布版本不会按普通 latest Release 分发，新版本必须大于 `UpdateService.CurrentVersion`，否则不会提示更新。

## 更新源与兼容设置

新设置的 `Updates.Repository` 默认为 `mete0rxsc/file-converter-next`。已有配置保持用户原值；若先前留空，需在 `%LOCALAPPDATA%\FileConverterNext\settings.json` 主动填写以参与更新。

```json
{
  "Updates": {
    "Repository": "mete0rxsc/file-converter-next",
    "CheckAtStartup": true
  }
}
```

这是一段字段示例；修改现有配置时应保留其他设置和预设，不要用示例覆盖整个文件。设为空仓库时程序显示未配置并不检查该源。

## 许可证、来源与对应源码

File Converter Next 保留原项目 GPLv3 许可、Adrien Allard 及上游贡献者署名，并标明 Mete0r 的重构工作。原参考仓库为 [Tichau/FileConverter](https://github.com/Tichau/FileConverter)，基线提交 `5dce6c0980635e3a6ae8d8acff235bcc9b1441a4`。

发行包必须保留 `LICENSE.md`、`THIRD-PARTY-NOTICES.md` 和附带工具的许可文件。分发者还需按 GPL/AGPL 要求提供与发行二进制准确对应的源码及必要构建信息，包括所分发的 FFmpeg、Ghostscript 版本、修改和构建配置。项目 tag 的自动源码包只覆盖本仓库，不能代替独立分发工具的对应源码。

正式发布时应一并归档或提供稳定可获取的对应依赖源码，记录 SHA-256、来源和构建参数；不能只依赖可能变化的 `latest` 下载链接。第三方依赖来源和许可证地址应保留真实上游，不替换为本项目仓库。

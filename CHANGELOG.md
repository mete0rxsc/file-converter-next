# File Converter Next 更新记录

本文件区分当前发布改动、前序重构版本修复与仍需验证的场景。项目仓库：[mete0rxsc/file-converter-next](https://github.com/mete0rxsc/file-converter-next)。

## 3.0.5 — File Converter Next

- 软件界面、右键菜单、程序集、命名空间、工程、主程序和发行包统一使用 **File Converter Next / FileConverterNext**。
- 软件中的项目、帮助及 GitHub 入口统一指向新仓库；原项目地址继续保留在来源与致谢中。
- 介绍界面保留 **V2.0 重构 · Mete0r** 署名、原作者署名、GPLv3 说明及简短 Bug 修复摘要。
- 使用新的设置目录，并保留先前重构测试版设置的迁移、来源备份和预设 ID。
- Office 与 ODF 文档改由随包提供的 LibreOffice 无界面导出，不再调用 Microsoft Office COM。当前 DOC 导出集成测试仍超时且无输出，无打印机及不可达网络打印机环境尚未验收；不能将打印机等待问题记为已修复。
- MSI 恢复 GPLv3 安装许可页、可编辑安装目录和完整安装向导；默认仍在当前用户目录安装。
- 新配置的更新仓库默认为 `mete0rxsc/file-converter-next`；不修改已有配置中用户指定的更新源。
- 补充 README、发布说明及 `.gitignore`，将 MSI、免安装 ZIP 和 SHA-256 清单集中到 `artifacts/release/`。

V2.0 是重构版本署名，3.0.5 是 Windows Installer 与更新检查使用的内部版本号。

## 前序重构版本已修复的问题

### 预设选择与菜单调用

- 修正带 `CMIC_MASK_UNICODE` 的数字 verb 调用。先前重构测试版会将空 `lpVerbW` 当作命令 0，导致选 WebP/JPG 等格式时调用第一个 GIF 预设。
- 不再通过 `IntPtr.ToInt32()` 判断数字或字符串 verb；采用完整宽度指针、正确结构尺寸和 ANSI / Unicode 分派。
- 用独立 DLL 路径与 COM 标识替换宿主中已驻留的先前实现，避免安装文件已更新但 Explorer 仍使用旧菜单代码。
- 菜单快照优先记录已注册的实际 EXE；开发目录启动不再自动覆盖全局菜单入口。
- 中文 ANSI 帮助文本使用 Windows `CP_ACP`，避免 ASCII / UTF-8 与宿主 ANSI 约定不符。

旧 SharpShell 及旧 FileConverterExtension 没有进入新发行包。新实现消除了已确认的指针处理缺陷；**没有据此认定或证明 Kitopia 历史崩溃根因**。

### 安装与注册

- 恢复原版 `MSIRESTARTMANAGERCONTROL=Disable`，不在安装时要求关闭或重启 Explorer。
- 注册前检查主程序与选定 DLL 是否齐全，避免不完整发行包破坏现有菜单快照。
- 注册中途失败时恢复菜单快照及本次涉及的 COM 类、菜单处理器和 EXE 路径；恢复失败会明确报告。
- 使用 Windows `SecurityIdentifier` 解析有效 SID，替换只识别部分本地账户形式的正则。
- UI 版本与 Shell 载荷目录版本分别处理，避免只更新介绍界面后注册程序查找错误目录。

### 转换任务与源文件保护

- 每个任务使用独立临时目录和唯一请求文件，避免临时文件命名竞争及命令行长度限制。
- 转换在独立工作进程中执行，传递版本化请求、进度、结果与取消消息。
- 输出先生成并验证，再提交目标文件；避免覆盖输入及已有输出。
- 全部输出成功提交后才执行预设指定的源文件归档或删除，失败或取消只清理本任务产物。
- Office COM、PDF 渲染、CD 设备资源及转换生命周期采用新实现，替换旧依赖调用层。

## 验证边界

历史测试证据见 [ACCEPTANCE.md](docs/ACCEPTANCE.md) 和两份 [修复审查](docs/REVIEW-20261102.md)、[介绍窗口审查](docs/REVIEW-WELCOME-20261102.md)。其中测试数量属于各自记录的版本，不自动视为本次发布的重复验收结果。

实体音频 CD、Kitopia 场景、全部输入组合和压力场景、完整 GitHub 更新发布流程仍需验收。特定 SID 的代码修正不等于已在实体 Entra 账户完成测试。MSI 构建成功也不等于安装、升级、卸载及所有宿主场景全部通过。

## 原项目

参考：[Tichau/FileConverter](https://github.com/Tichau/FileConverter)，基线提交 `5dce6c0980635e3a6ae8d8acff235bcc9b1441a4`。保留 Adrien Allard 及上游贡献者署名和 GPLv3 许可。详细复用范围见 [THIRD-PARTY-NOTICES.md](docs/THIRD-PARTY-NOTICES.md)。

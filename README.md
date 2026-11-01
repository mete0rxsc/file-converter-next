# FileConverter

C# / .NET 10 / Avalonia 的 Windows x64 右键菜单转换扩展。使用方式与原版一致：在资源管理器右键选择预设，直接执行转换并显示进度窗口；菜单中的设置入口管理原版预设和应用选项。直接运行 EXE 显示原版右键使用说明。

以 Tichau/FileConverter 提交 5dce6c0980635e3a6ae8d8acff235bcc9b1441a4 为功能基线，包含 17 种输出格式和 76 个默认预设。界面按原版窗口和操作迁移到 Avalonia；不提供额外的转换工作台、文件选择器或手动开始转换流程。

支持 C# NativeAOT 与原生 C++ 两种动态 Shell 菜单，默认 NativeAOT。禁止引入旧 SharpShell 扩展。

- [构建、使用与配置](docs/CONFIGURATION.md)
- [功能验收与当前限制](docs/ACCEPTANCE.md)
- [复用审查及第三方许可](docs/THIRD-PARTY-NOTICES.md)
- [GPLv3](LICENSE.md)

**未来更新源位置：** %LOCALAPPDATA%\FileConverter\settings.json → Updates.Repository，填写 owner/repo。

发布和全部真实环境验收状态以验收文档为准；未完成的设备/宿主测试不等于功能已验证。

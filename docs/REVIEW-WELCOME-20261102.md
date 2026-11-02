# 介绍窗口调整（2026-11-02）

修改前恢复点：`e76e147`。仅调整安装后/无参数启动时已有的介绍窗口，保留原右键转换、设置及任务流程。

- 加入原项目仓库链接 `https://github.com/Tichau/FileConverter`，复用已有的打开仓库命令。
- 显示 `V2.0 重构 · Mete0r`，保留 Tichau、贡献者署名及 GPLv3 许可说明。
- 简述已修正的预设错选、菜单命令/指针处理及安装行为。未宣称已证明 Kitopia 历史崩溃根因。
- 使用独立卡片、统一间距、可滚动内容及固定底部关闭按钮；窗口可缩放，保留原版使用动画。中文文案与英文回退均已检查。

安装包内部版本升至 3.0.3，使现有 3.0.2 可正常升级；V2.0 为用户指定的重构署名。转换代码和 Shell 协议未改，本次发布保留经过验证的 3.0.2 双 Shell DLL，不覆盖宿主中已驻留的不同实现。

首次实际升级发现注册路径原先绑定主程序版本，导致查找不存在的 shell/3.0.3 并回滚。已将 Shell 载荷目录版本与 UI 版本分开，保持与构建、Shell 测试脚本的 shell/3.0.2 一致；补充注册预检回归，确保 3.0.3 主程序能找到保留的 DLL，且不改写菜单快照。

验证：原有 12 项 UI/回归测试通过（含七种图片输出）；截图检查默认中文、英文、640×520 小窗口及滚动底部，证据在 `artifacts/screenshots/help*.png`、`tests/FileConverter.UiTests/TestResults/welcome-ui.trx`。子代理只读审查三个 UI 文件及截图，未发现阻塞缺陷或功能越界。未进行屏幕阅读器或 Windows 高对比度模式实际验收。

最终实际安装返回 0，已安装 3.0.3 主程序及相关程序集与发布文件哈希一致；两个 Shell DLL 与原 3.0.2 发布文件相同。Explorer PID 17632 和启动时间未变，settings.json、Settings.user.xml 哈希未变。证据：`artifacts/welcome-msi-upgrade-reviewed.log`、`artifacts/welcome-upgrade-result.json`。子代理对版本号及注册路径修正均已补审通过。

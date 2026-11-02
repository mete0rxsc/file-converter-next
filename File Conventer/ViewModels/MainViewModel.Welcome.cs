namespace File_Conventer.ViewModels;

public partial class MainViewModel
{
    public string WelcomeSubtitle => L.Translate("WelcomeSubtitle", "在文件上右键，即可转换格式。", "Convert files directly from the context menu.");
    public string WelcomeAttribution => L.Translate("WelcomeAttribution", "V2.0 重构 · Mete0r", "V2.0 reconstruction · Mete0r");
    public string WelcomeUpstream => L.Translate("WelcomeUpstream", "原项目：File Converter · Tichau 及贡献者", "Original project: File Converter · Tichau and contributors");
    public string WelcomeRepositoryHint => L.Translate("WelcomeRepositoryHint", "在浏览器中打开原项目仓库", "Open the original project repository in your browser");
    public string WelcomeLicense => L.Translate("WelcomeLicense", "基于原项目重构，保留 GPLv3 许可与原作者署名。", "Based on the original project, preserving its GPLv3 license and author credits.");
    public string WelcomeUsageTitle => L.Translate("WelcomeUsageTitle", "如何使用", "How to use");
    public string WelcomeCloseLabel => L.Translate("WelcomeCloseLabel", "知道了", "OK");
    public string WelcomeFixesTitle => L.Translate("WelcomeFixesTitle", "本次修复", "Fixes in this reconstruction");
    public string WelcomeFormatFix => L.Translate("WelcomeFormatFix", "• 修复右键预设错选，避免选择其他格式却转换为 GIF。", "• Corrected context menu preset selection that could produce GIF instead of the selected format.");
    public string WelcomeShellFix => L.Translate("WelcomeShellFix", "• 替换旧菜单扩展，修正命令与指针处理中的已知缺陷。", "• Replaced the legacy menu extension and corrected known command and pointer handling defects.");
    public string WelcomeInstallerFix => L.Translate("WelcomeInstallerFix", "• 恢复原版安装行为，安装时不再要求关闭资源管理器。", "• Restored the original installer behavior so installation no longer asks to close Explorer.");
}

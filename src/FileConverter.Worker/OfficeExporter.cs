using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace FileConverter.Worker;

[SupportedOSPlatform("windows")]
public static partial class OfficeExporter
{
    public static void SelectLocalWordPrinter(object application)
    {
        using var devices = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\Devices");
        if (devices?.GetValue("Microsoft Print to PDF") is not string) return;
        object? wordBasic = null;
        try
        {
            wordBasic = ((dynamic)application).WordBasic;
            // ActivePrinter would change the user's system default printer. This
            // WordBasic API changes only this automation instance's printer.
            ((dynamic)wordBasic).FilePrintSetup(Printer: "Microsoft Print to PDF", DoNotSetAsSysDefault: 1);
        }
        catch (COMException) { }
        finally { if (wordBasic is not null) Marshal.FinalReleaseComObject(wordBasic); }
    }
    public static void SelectLocalExcelPrinter(object application)
    {
        using var devices = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\Devices");
        if (devices?.GetValue("Microsoft Print to PDF") is not string descriptor) return;
        var comma = descriptor.IndexOf(',');
        if (comma < 0) return;
        var port = descriptor[(comma + 1)..];
        foreach (var separator in new[] { " on ", " 在 ", " auf ", " sur ", " en ", " su " })
        {
            try { ((dynamic)application).ActivePrinter = "Microsoft Print to PDF" + separator + port; return; }
            catch (COMException) { }
        }
    }
    public static Task ExportAsync(string input, string output, CancellationToken token, Action<string>? status = null)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            object? application = null, document = null, documents = null;
            string kind = "";
            Exception? failure = null;
            Process? ownedProcess = null;
            CancellationTokenRegistration cancellation = default;
            var existing = Process.GetProcesses().Select(p => { try { return p.Id; } finally { p.Dispose(); } }).ToHashSet();
            try
            {
                token.ThrowIfCancellationRequested();
                var ext = Path.GetExtension(input).ToLowerInvariant();
                kind = ext is ".doc" or ".docx" or ".odt" ? "Word" : ext is ".xls" or ".xlsx" or ".ods" ? "Excel" : "PowerPoint";
                var type = Type.GetTypeFromProgID(kind + ".Application") ?? throw new InvalidOperationException($"未安装 {kind}。Office 转换需要本机 Microsoft Office。");
                status?.Invoke("启动 " + kind);
                application = Activator.CreateInstance(type) ?? throw new InvalidOperationException("无法启动 Office。");
                dynamic app = application;
                if (kind != "PowerPoint") app.Visible = false;
                if (kind == "Word") { app.DisplayAlerts = 0; app.AutomationSecurity = 3; }
                if (kind == "Excel") { app.DisplayAlerts = false; app.AutomationSecurity = 3; }
                if (kind == "PowerPoint") { app.DisplayAlerts = 1; app.AutomationSecurity = 3; }
                void IdentifyInstance(object windowOwner, string property)
                {
                    try
                    {
                        var raw = windowOwner.GetType().InvokeMember(property, System.Reflection.BindingFlags.GetProperty, null, windowOwner, null);
                        var window = (nint)Convert.ToInt64(raw);
                        GetWindowThreadProcessId(window, out var applicationPid);
                        if (applicationPid != 0 && !existing.Contains((int)applicationPid))
                            ownedProcess = Process.GetProcessById((int)applicationPid);
                    }
                    catch (Exception error) when (error is COMException or System.Reflection.TargetInvocationException or MissingMemberException) { }
                }
                if (kind == "Excel") IdentifyInstance(application, "Hwnd");
                cancellation = token.Register(() => { try { if (ownedProcess is { HasExited: false }) ownedProcess.Kill(true); } catch (InvalidOperationException) { } });
                token.ThrowIfCancellationRequested();
                if (kind == "Word")
                {
                    status?.Invoke("Word：选择实例打印机");
                    SelectLocalWordPrinter(application);
                    documents = app.Documents;
                    status?.Invoke("Word：打开文档");
                    document = ((dynamic)documents).Open(input, ReadOnly: true, AddToRecentFiles: false, Visible: false);
                    object window = ((dynamic)document).ActiveWindow;
                    try { IdentifyInstance(window, "Hwnd"); }
                    finally { Marshal.FinalReleaseComObject(window); }
                    status?.Invoke("Word：导出 PDF");
                    ((dynamic)document).ExportAsFixedFormat(output, 17);
                }
                else if (kind == "Excel")
                {
                    status?.Invoke("Excel：选择实例打印机");
                    SelectLocalExcelPrinter(application);
                    documents = app.Workbooks;
                    status?.Invoke("Excel：打开工作簿");
                    document = ((dynamic)documents).Open(input, UpdateLinks: 0, ReadOnly: true, IgnoreReadOnlyRecommended: true);
                    status?.Invoke("Excel：导出 PDF");
                    ((dynamic)document).ExportAsFixedFormat(0, output);
                }
                else
                {
                    status?.Invoke("PowerPoint：打开演示文稿");
                    documents = app.Presentations;
                    document = ((dynamic)documents).Open(input, ReadOnly: -1, Untitled: 0, WithWindow: 0);
                    IdentifyInstance(application, "HWND");
                    // PowerPoint's ExportAsFixedFormat has a non-optional COM PrintRange
                    // argument which IDispatch binders handle inconsistently. PDF SaveAs
                    // uses the same Office fixed-format exporter without that ABI trap.
                    ((dynamic)document).SaveAs(output, 32, 0); // ppSaveAsPDF
                }
                token.ThrowIfCancellationRequested();
                if (!File.Exists(output)) throw new IOException("Office 未生成 PDF 文件。");
            }
            catch (Exception error) { failure = error; }
            finally
            {
                status?.Invoke(kind + "：关闭文档和释放实例");
                if (document is not null)
                {
                    try { if (kind == "Word") ((dynamic)document).Close(0); else if (kind == "Excel") ((dynamic)document).Close(false); else ((dynamic)document).Close(); } catch (Exception) { }
                    Marshal.FinalReleaseComObject(document);
                }
                if (documents is not null) Marshal.FinalReleaseComObject(documents);
                if (application is not null) { try { if (ownedProcess is not null) ((dynamic)application).Quit(); } catch (Exception) { } Marshal.FinalReleaseComObject(application); }
                ownedProcess?.Dispose();
                cancellation.Dispose();
                if (token.IsCancellationRequested) completion.TrySetCanceled(token);
                else if (failure is not null) completion.TrySetException(failure);
                else completion.TrySetResult();
            }
        }) { IsBackground = true, Name = "Office STA conversion" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(token);
    }
    [LibraryImport("user32.dll")] private static partial uint GetWindowThreadProcessId(nint hwnd, out uint processId);
}

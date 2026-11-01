using System.Runtime.InteropServices;
using FileConverter.Core;
using FileConverter.Worker;
using Xunit;

namespace FileConverter.Tests;

public sealed class OfficeTests
{
    [Theory] [InlineData("Word", "docx")] [InlineData("Excel", "xlsx")] [InlineData("PowerPoint", "pptx")]
    public async Task InstalledOfficeExportsRealDocumentToPdfAndPng(string kind, string extension)
    {
        var root = Path.Combine(Path.GetTempPath(), "FileConverter-office-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var input = Path.Combine(root, "中文 document." + extension);
        try
        {
            await CreateOfficeDocument(kind, input).WaitAsync(TimeSpan.FromSeconds(60));
            foreach (var output in new[] { OutputType.Pdf, OutputType.Png })
            {
                var preset = new Preset { Name = "Office", OutputType = output, InputTypes = [extension], Settings = PresetDefaults.For(output) };
                var worker = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/FileConverter.Worker/bin/Debug/net10.0-windows/FileConverter.Worker.exe"));
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                var messages = new List<string>();
                IReadOnlyList<string> files;
                try { files = await new ConversionClient(worker).ConvertAsync(input, preset, new() { Tools = ConversionTests.Tools() }, new Progress<WorkerMessage>(m => { lock (messages) messages.Add(m.Message ?? m.Kind); }), deadline.Token); }
                catch (Exception error) { throw new InvalidOperationException(kind + " → " + output + ": " + string.Join(" / ", messages), error); }
                Assert.NotEmpty(files); Assert.All(files, file => Assert.True(new FileInfo(file).Length > 0));
                Assert.True(File.Exists(input));
            }
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } }
    }
    private static Task CreateOfficeDocument(string kind, string path)
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var objects = new List<object>(); object? application = null, document = null; Exception? failure = null;
            try
            {
                var type = Type.GetTypeFromProgID(kind + ".Application") ?? throw new InvalidOperationException(kind + " is not installed.");
                application = Activator.CreateInstance(type)!; dynamic app = application;
                if (kind == "Word")
                {
                    app.Visible = false; app.DisplayAlerts = 0;
                    OfficeExporter.SelectLocalWordPrinter(application);
                    object docs = app.Documents; objects.Add(docs); document = ((dynamic)docs).Add();
                    object content = ((dynamic)document).Content; objects.Add(content); ((dynamic)content).Text = "FileConverter conversion acceptance test.\r\n中文测试文档。";
                    ((dynamic)document).SaveAs2(path, 16);
                }
                else if (kind == "Excel")
                {
                    app.Visible = false; app.DisplayAlerts = false;
                    OfficeExporter.SelectLocalExcelPrinter(application);
                    object books = app.Workbooks; objects.Add(books); document = ((dynamic)books).Add();
                    object sheets = ((dynamic)document).Worksheets; objects.Add(sheets);
                    object sheet = ((dynamic)sheets)[1]; objects.Add(sheet);
                    object cell = ((dynamic)sheet).Range["A1"]; objects.Add(cell); ((dynamic)cell).Value2 = "FileConverter 中文测试";
                    ((dynamic)document).SaveAs(path, 51);
                }
                else
                {
                    object presentations = app.Presentations; objects.Add(presentations); document = ((dynamic)presentations).Add(0);
                    object slides = ((dynamic)document).Slides; objects.Add(slides);
                    object slide = ((dynamic)slides).Add(1, 12); objects.Add(slide);
                    object shapes = ((dynamic)slide).Shapes; objects.Add(shapes);
                    object shape = ((dynamic)shapes).AddTextbox(1, 20, 20, 600, 100); objects.Add(shape);
                    object frame = ((dynamic)shape).TextFrame; objects.Add(frame);
                    object range = ((dynamic)frame).TextRange; objects.Add(range); ((dynamic)range).Text = "FileConverter 中文测试";
                    ((dynamic)document).SaveAs(path, 24);
                }
            }
            catch (Exception error) { failure = error; }
            finally
            {
                foreach (var obj in objects.AsEnumerable().Reverse()) Marshal.FinalReleaseComObject(obj);
                if (document is not null) { try { if (kind == "PowerPoint") ((dynamic)document).Close(); else ((dynamic)document).Close(0); } catch { } Marshal.FinalReleaseComObject(document); }
                if (application is not null) { try { ((dynamic)application).Quit(); } catch { } Marshal.FinalReleaseComObject(application); }
                if (failure is null) source.TrySetResult(); else source.TrySetException(failure);
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return source.Task;
    }
}

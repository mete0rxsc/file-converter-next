using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using SkiaSharp;

namespace File_Conventer.Controls;

// Replaces the old WpfAnimatedGif dependency for the original usage illustration.
public sealed class UsageAnimation : Image
{
    private SKCodec? codec;
    private SKBitmap? pixels;
    private WriteableBitmap? bitmap;
    private byte[]? buffer;
    private SKCodecFrameInfo[] frames = [];
    private readonly DispatcherTimer timer = new();
    private int frame;
    public UsageAnimation() => timer.Tick += (_, _) => DrawNextFrame();
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        using var stream = AssetLoader.Open(new Uri("avares://FileConverter/Assets/FileConverterUsage.gif"));
        using var memory = new MemoryStream(); stream.CopyTo(memory);
        codec = SKCodec.Create(new SKMemoryStream(memory.ToArray()));
        if (codec is null) return;
        frames = codec.FrameInfo;
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        pixels = new SKBitmap(info);
        bitmap = new WriteableBitmap(new PixelSize(info.Width, info.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        buffer = new byte[pixels.ByteCount];
        Source = bitmap; frame = 0; DrawNextFrame(); timer.Start();
    }
    private void DrawNextFrame()
    {
        if (codec is null || pixels is null || bitmap is null || buffer is null) return;
        var result = codec.GetPixels(pixels.Info, pixels.GetPixels(), new SKCodecOptions(frame, frame == 0 ? -1 : frame - 1));
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput)) { timer.Stop(); return; }
        Marshal.Copy(pixels.GetPixels(), buffer, 0, buffer.Length);
        using (var target = bitmap.Lock())
            for (var row = 0; row < pixels.Height; row++)
                Marshal.Copy(buffer, row * pixels.RowBytes, target.Address + row * target.RowBytes, pixels.Width * 4);
        InvalidateVisual();
        timer.Interval = TimeSpan.FromMilliseconds(frames.Length > frame ? Math.Max(20, frames[frame].Duration) : 100);
        frame = frames.Length == 0 ? 0 : (frame + 1) % frames.Length;
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        timer.Stop(); Source = null; bitmap?.Dispose(); pixels?.Dispose(); codec?.Dispose();
        bitmap = null; pixels = null; codec = null; buffer = null;
        base.OnDetachedFromVisualTree(e);
    }
}

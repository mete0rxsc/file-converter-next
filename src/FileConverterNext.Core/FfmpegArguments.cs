using System.Globalization;
using System.Runtime.InteropServices;

namespace FileConverterNext.Core;

public static partial class FfmpegArguments
{
    private static readonly int[] Mp3Rates = [245, 225, 190, 175, 165, 130, 115, 100, 85, 65];
    private static readonly int[] OggRates = [32, 48, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 500];
    private static readonly Dictionary<int, string> AacQuality = new()
    { [460]="3.9", [340]="3", [256]="2.2", [224]="1.9", [192]="1.6", [155]="1.3", [128]="1", [112]="0.9", [96]="0.75", [80]="0.6", [64]="0.45", [48]="0.3", [32]="0.2", [16]="0.1" };
    private static string N(double value) => value.ToString("0.########", CultureInfo.InvariantCulture);
    private static int Nearest(int[] values, double value) => Enumerable.Range(0, values.Length).MinBy(i => Math.Abs(values[i] - value));
    public static List<string> Build(Preset p, string input, string output, HardwareMode hardware = HardwareMode.Off)
    {
        var args = new List<string> { "-nostdin", "-hide_banner", "-loglevel", "error", "-n", "-progress", "pipe:1", "-i", input };
        void Add(params string[] values) => args.AddRange(values);
        var rate = p.Number("AudioBitrate", 128);
        var quality = p.Number("VideoQuality", 28);
        var channels = p.Number("AudioChannelCount", 0);
        if (p.Flag("EnableFFMPEGCustomCommand"))
        {
            var custom = SplitCommand(p.Get("FFMPEGCustomCommand"));
            if (custom.Contains("-y")) throw new ArgumentException("自定义参数不能使用 -y 覆盖现有文件；输出由任务事务管理。");
            Add(custom);
            Add(output);
            return args;
        }
        if (!Formats.IsVideoOutput(p.OutputType) && p.OutputType != OutputType.Gif) Add("-vn");
        switch (p.OutputType)
        {
            case OutputType.Aac: Add("-c:a", "aac", "-q:a", AacQuality.GetValueOrDefault((int)rate, N(rate / 128)), "-write_apetag", "1"); break;
            case OutputType.Flac: Add("-c:a", "flac", "-compression_level", "12"); break;
            case OutputType.Mp3:
                Add("-c:a", "libmp3lame");
                if (p.Get("AudioEncodingMode", "Mp3VBR") == "Mp3CBR") Add("-b:a", N(rate) + "k");
                else Add("-q:a", Nearest(Mp3Rates, rate).ToString(CultureInfo.InvariantCulture));
                Add("-id3v2_version", "3", "-write_id3v1", "1"); break;
            case OutputType.Ogg: Add("-c:a", "libvorbis", "-q:a", N(Nearest(OggRates, rate) - 2)); break;
            case OutputType.Wav:
                var codec = p.Get("AudioEncodingMode", "Wav16") switch { "Wav8" => "pcm_u8", "Wav24" => "pcm_s24le", "Wav32" => "pcm_s32le", _ => "pcm_s16le" };
                Add("-c:a", codec); break;
            case OutputType.Mkv:
            case OutputType.Mp4:
                var speed = p.Get("VideoEncodingSpeed", "Medium").ToLowerInvariant();
                var crf = N(Math.Clamp(51 - quality, 0, 51));
                switch (hardware)
                {
                    case HardwareMode.CUDA: Add("-c:v", "h264_nvenc", "-preset", "p4", "-rc", "constqp", "-qp", crf); break;
                    case HardwareMode.AMF: Add("-c:v", "h264_amf", "-usage", "transcoding", "-quality", speed.Contains("slow") ? "quality" : speed == "medium" ? "balanced" : "speed", "-qp_i", crf, "-qp_p", crf); break;
                    default: Add("-c:v", "libx264", "-preset", speed, "-crf", crf); break;
                }
                break;
            case OutputType.Webm: Add("-c:v", "libvpx-vp9", "-crf", N(Math.Clamp(63 - quality, 0, 63)), "-b:v", "0"); break;
            case OutputType.Ogv: Add("-c:v", "libtheora", "-q:v", N(Math.Clamp(quality, 0, 10))); break;
            case OutputType.Avi: Add("-c:v", "mpeg4", "-vtag", "xvid", "-q:v", N(Math.Clamp(31 - quality, 1, 31))); break;
            case OutputType.Gif:
                var filter = Transform(p, false);
                var fps = N(Math.Clamp(p.Number("VideoFramesPerSecond", 15), 1, 240));
                Add("-filter_complex", $"{(filter.Length > 0 ? filter + "," : "")}fps={fps},split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse", "-loop", "0");
                break;
            default: throw new ArgumentException("该格式不使用 FFmpeg 引擎。");
        }
        if (channels > 0) Add("-ac", N(channels));
        if (Formats.IsVideoOutput(p.OutputType))
        {
            if (!p.Flag("EnableAudio", true)) Add("-an");
            else if (p.OutputType == OutputType.Avi) Add("-c:a", "libmp3lame", "-q:a", N(Nearest(Mp3Rates, rate)));
            else if (p.OutputType is OutputType.Ogv or OutputType.Webm) Add("-c:a", "libvorbis", "-q:a", N(Nearest(OggRates, rate) - 2));
            else Add("-c:a", "aac", "-q:a", AacQuality.GetValueOrDefault((int)rate, N(rate / 128)));
            var filter = Transform(p, p.OutputType is OutputType.Mp4 or OutputType.Mkv);
            if (filter.Length > 0) Add("-vf", filter);
            var fps = p.Number("VideoFramesPerSecond", 0);
            if (fps > 0) Add("-r", N(fps));
            if (!p.Flag("EnableVideo", true)) Add("-vn");
        }
        Add(output);
        return args;
    }
    public static string Transform(Preset p, bool even)
    {
        var scale = p.Number("VideoScale", 1);
        if (scale <= 0 || scale > 100) throw new ArgumentException("缩放比例必须大于 0 且不超过 100。");
        var filters = new List<string>();
        if (even) filters.Add($"scale=max(2\\,trunc(iw*{N(scale)}/2)*2):max(2\\,trunc(ih*{N(scale)}/2)*2)");
        else if (Math.Abs(scale - 1) > 0.0001) filters.Add($"scale=iw*{N(scale)}:ih*{N(scale)}");
        var rotation = ((p.Number("VideoRotation", 0) % 360) + 360) % 360;
        if (rotation == 90) filters.Add("transpose=2");
        else if (rotation == 180) filters.Add("vflip,hflip");
        else if (rotation == 270) filters.Add("transpose=1");
        else if (rotation != 0) throw new ArgumentException("视频旋转仅支持 0、90、180、270 度。");
        if (even) filters.Add("format=yuv420p");
        return string.Join(',', filters);
    }
    // Windows argument grammar, never executed through a shell.
    public static string[] SplitCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return [];
        var argv = CommandLineToArgvW("ffmpeg " + command, out var count);
        if (argv == IntPtr.Zero) throw new ArgumentException("自定义 FFmpeg 参数无效。");
        try { return Enumerable.Range(1, count - 1).Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size))!).ToArray(); }
        finally { LocalFree(argv); }
    }
    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial IntPtr CommandLineToArgvW(string command, out int count);
    [LibraryImport("kernel32.dll")] private static partial IntPtr LocalFree(IntPtr pointer);
}

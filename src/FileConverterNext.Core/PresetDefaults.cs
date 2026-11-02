namespace FileConverterNext.Core;
public static class PresetDefaults
{
    public static Dictionary<string, string> For(OutputType output)
    {
        // Preset data are the upstream source of truth, including applicable settings.
        var sample = SettingsStore.LoadDefaults().Presets.FirstOrDefault(p => p.OutputType == output);
        var result = sample is null ? new Dictionary<string, string>() : new Dictionary<string, string>(sample.Settings);
        if (output == OutputType.Wav) result["AudioEncodingMode"] = "Wav16";
        if (output == OutputType.Mp3) result["AudioEncodingMode"] = "Mp3VBR";
        return result;
    }
}

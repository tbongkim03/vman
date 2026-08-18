using System.Text.Json;
using System.Text.Json.Serialization;

namespace VMan.Core;

/// <summary>%LOCALAPPDATA%\vman\settings.json 에 저장되는 사용자 설정.</summary>
public sealed class Settings
{
    /// <summary>"apple" 또는 "oneui".</summary>
    public string Theme { get; set; } = "apple";

    /// <summary>"system", "light", "dark".</summary>
    public string Appearance { get; set; } = "system";

    [JsonIgnore]
    public static string FilePath => Path.Combine(Layout.Root, "settings.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch (Exception)
        {
            // 설정 파일이 깨졌으면 기본값으로 돌아간다
        }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Layout.Root);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
        }
        catch (Exception)
        {
            // 저장 실패가 앱을 죽이지는 않게 한다
        }
    }
}

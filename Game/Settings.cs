using System.Text.Json.Serialization;

namespace GameProject {
    public class Settings {
        public string HostIp { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 6121;
    }

    [JsonSourceGenerationOptionsAttribute(
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        WriteIndented = true)]
    [JsonSerializable(typeof(Settings))]
    internal partial class SettingsContext : JsonSerializerContext { }
}

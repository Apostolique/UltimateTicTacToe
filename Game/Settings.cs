using System.Text.Json.Serialization;

namespace GameProject {
    public class Settings {
        /// <summary>
        /// The relay that introduces the two players and forwards their moves, deployed from
        /// <c>Server/</c>.
        /// </summary>
        /// <remarks>
        /// Desktop reads this out of <c>Settings.json</c> next to the executable, so it can be
        /// pointed somewhere else without a rebuild. The browser has nowhere to keep a file, so
        /// it uses whatever is compiled in here. To test against a relay running locally, use
        /// <c>ws://127.0.0.1:8090/ws</c> and not <c>localhost</c>: the name resolves to IPv6
        /// first and wrangler only listens on IPv4, which costs about two seconds per connect.
        /// </remarks>
        public string RelayUrl { get; set; } = "wss://ultimatetictactoe-relay.apostolique.workers.dev/ws";
    }

    [JsonSourceGenerationOptionsAttribute(
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        WriteIndented = true)]
    [JsonSerializable(typeof(Settings))]
    internal partial class SettingsContext : JsonSerializerContext { }
}

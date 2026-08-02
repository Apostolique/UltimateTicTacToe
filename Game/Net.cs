#nullable enable
using System.Globalization;

namespace GameProject {
    /// <summary>
    /// The one seam the game talks to for online play. Everything goes through a relay, so a
    /// desktop player and a browser player are in the same pool and neither needs a reachable
    /// address. The relay never reads a game message, it only forwards it to the other seat.
    /// </summary>
    public static class Net {
        public enum Mode {
            /// Two players on one machine.
            Offline,
            /// Dialing the relay.
            Connecting,
            /// Holding a code, no opponent yet.
            Waiting,
            /// In the matchmaking queue.
            Searching,
            /// Opponent present.
            Playing,
        }

        static readonly RelayClient _client = new();
        static (int Macro, int Micro)? _sentHover;

        public static Mode Status { get; private set; } = Mode.Offline;
        public static string Code { get; private set; } = "";
        /// <summary>The host plays X and the joiner plays O.</summary>
        public static bool IsHost { get; private set; }
        public static string? Error { get; private set; }

        public static bool IsOnline => Status != Mode.Offline;
        public static bool HasPeer => Status == Mode.Playing;

        /// <summary>Where the opponent is hovering, as a cell rather than a position so it
        /// lands in the right place whatever size their window is.</summary>
        public static (int Macro, int Micro)? RemoteHover { get; private set; }

        public static void HostGame() {
            Start();
            _client.Send("host");
        }

        public static void JoinGame(string code) {
            Start();
            _client.Send($"join {code}");
        }

        public static void FindMatch() {
            Start();
            _client.Send("queue");
            Status = Mode.Searching;
        }

        public static void Disconnect() {
            _client.Close();
            Status = Mode.Offline;
            Code = "";
            IsHost = false;
            Error = null;
            RemoteHover = null;
            _sentHover = null;
            GameRoot.Reset();
        }

        static void Start() {
            _client.Connect(GameRoot.Settings.RelayUrl);
            Status = Mode.Connecting;
            Code = "";
            IsHost = false;
            Error = null;
            RemoteHover = null;
            _sentHover = null;
            GameRoot.Reset();
        }

        public static void PollEvents() {
            if (Status == Mode.Offline) return;

            if (_client.State == RelayClient.Status.Failed) {
                Error = _client.Error;
                _client.Close();
                Status = Mode.Offline;
                Code = "";
                return;
            }

            while (_client.TryRead(out string line)) Handle(line);
        }

        static void Handle(string line) {
            (string verb, string rest) = SplitFirst(line);

            switch (verb) {
                case "hosted":
                    Code = rest;
                    IsHost = true;
                    Status = Mode.Waiting;
                    break;

                case "joined":
                    // The seat is ours, but the other one may still be empty: the relay keeps
                    // a code alive after its host drops, so joining doesn't imply an opponent.
                    Code = rest;
                    IsHost = false;
                    Status = Mode.Waiting;
                    break;

                case "matched": {
                    (string code, string role) = SplitFirst(rest);
                    Code = code;
                    IsHost = role == "host";
                    // Matchmaking puts both seats in at once, so there's no peer line coming.
                    Status = Mode.Playing;
                    GameRoot.Reset();
                    break;
                }

                case "searching":
                    Status = Mode.Searching;
                    break;

                case "peer":
                    if (rest == "in") {
                        Status = Mode.Playing;
                        GameRoot.Reset();
                    } else {
                        Status = Mode.Waiting;
                        RemoteHover = null;
                    }
                    break;

                case "msg":
                    Receive(rest);
                    break;

                case "error":
                    Error = rest;
                    _client.Close();
                    Status = Mode.Offline;
                    Code = "";
                    break;
            }
        }

        static void Receive(string payload) {
            (string kind, string rest) = SplitFirst(payload);

            switch (kind) {
                case "play": {
                    (string a, string b) = SplitFirst(rest);
                    if (int.TryParse(a, NumberStyles.Integer, CultureInfo.InvariantCulture, out int macro) &&
                        int.TryParse(b, NumberStyles.Integer, CultureInfo.InvariantCulture, out int micro)) {
                        GameRoot.MakePlay(macro, micro);
                        RemoteHover = null;
                    }
                    break;
                }
                case "reset":
                    GameRoot.Reset();
                    RemoteHover = null;
                    break;

                case "hover": {
                    (string a, string b) = SplitFirst(rest);
                    RemoteHover =
                        int.TryParse(a, NumberStyles.Integer, CultureInfo.InvariantCulture, out int macro) &&
                        int.TryParse(b, NumberStyles.Integer, CultureInfo.InvariantCulture, out int micro) &&
                        macro >= 0
                            ? (macro, micro)
                            : null;
                    break;
                }
            }
        }

        public static void SendPlay(int macro, int micro) {
            if (HasPeer) _client.Send($"msg play {macro} {micro}");
            _sentHover = null;
        }

        public static void SendReset() {
            if (HasPeer) _client.Send("msg reset");
        }

        /// <summary>Only goes out when the hovered cell actually changes.</summary>
        public static void SendHover((int Macro, int Micro)? cell) {
            if (!HasPeer || cell == _sentHover) return;
            _sentHover = cell;
            _client.Send(cell == null ? "msg hover -1 -1" : $"msg hover {cell.Value.Macro} {cell.Value.Micro}");
        }

        /// <summary>
        /// Whether this machine gets to play the mark that's up. One machine plays both when
        /// offline; online, nobody plays until an opponent is actually sitting there.
        /// </summary>
        public static bool IsLocalTurn(bool isPlayer1) => Status switch {
            Mode.Offline => true,
            Mode.Playing => isPlayer1 == IsHost,
            _ => false,
        };

        static (string Head, string Tail) SplitFirst(string s) {
            int space = s.IndexOf(' ');
            return space < 0 ? (s, "") : (s[..space], s[(space + 1)..]);
        }
    }
}

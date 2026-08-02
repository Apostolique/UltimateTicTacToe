using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Apos.Input;
using Apos.Shapes;
using Apos.Tweens;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace GameProject {
    public class GameRoot : Game {
        public static Settings Settings;

        static readonly Board _board = new Board();

        /// The network code drives the game through these, so a play that arrived over the
        /// wire goes through the same path as one made here.
        public static void MakePlay(int macro, int micro) => _board.TryPlay(macro, micro);
        public static void Reset() => _board.Reset();

        /// True while it's X's turn. The host plays X and the joiner plays O.
        internal static bool _isPlayer1 => _board.Turn == Mark.X;

        public GameRoot() {
            _graphics = new GraphicsDeviceManager(this);
#if KNI
            _graphics.GraphicsProfile = GraphicsProfile.FL10_0;
#else
            _graphics.GraphicsProfile = GraphicsProfile.HiDef;
#endif
            IsMouseVisible = true;
            Content.RootDirectory = "Content";

#if BLAZORGL
            // No writable app directory in the browser, so the built in relay address stands.
            Settings = new Settings();
#else
            Settings = EnsureJson<Settings>("Settings.json", SettingsContext.Default.Settings);
#endif
        }

        protected override void Initialize() {
            Window.AllowUserResizing = true;

            _graphics.PreferredBackBufferWidth = 800;
            _graphics.PreferredBackBufferHeight = 860;
            _graphics.ApplyChanges();

            base.Initialize();
        }

        protected override void LoadContent() {
            _sb = new ShapeBatch(GraphicsDevice);

            InputHelper.Setup(this);

            using (var ttf = TitleContainer.OpenStream($"{Content.RootDirectory}/source-code-pro-medium.ttf")) {
                _font = new ShapeFont(ttf);
            }
        }

        protected override void Update(GameTime gameTime) {
            InputHelper.UpdateSetup();
            TweenHelper.UpdateSetup(gameTime);

            Net.PollEvents();

            float width = GraphicsDevice.Viewport.Width;
            float height = GraphicsDevice.Viewport.Height;
            var layout = BoardLayout.Fit(width, height);

            Vector2 mouse = InputHelper.NewMouse.Position.ToVector2();
            bool clicked = _playerClick.Pressed();

            _onlineButton = OnlineButtonBounds(layout);
            _onlineHovered = !_lobby.IsOpen && _onlineButton.Contains(mouse);

            _lobby.Update(width, height, mouse, clicked);

            (int Macro, int Micro)? hovered = null;
            if (!_lobby.IsOpen) {
                if (_toggleLobby.Pressed()) {
                    _lobby.Toggle();
                } else if (clicked && _onlineHovered) {
                    _lobby.Open();
                } else {
                    hovered = PlayTurn(layout, mouse, clicked);
                }

                if (_reset.Pressed()) {
                    Net.SendReset();
                    Reset();
                }
            }

            _view.SetCursor(hovered, _board.Turn, layout);
            _view.Sync(_board);

            InputHelper.UpdateCleanup();
            base.Update(gameTime);
        }

        /// <summary>Returns the cell to preview, which is the opponent's when it's their go.</summary>
        (int Macro, int Micro)? PlayTurn(BoardLayout layout, Vector2 mouse, bool clicked) {
            if (!Net.IsLocalTurn(_isPlayer1)) {
                // Their pointer arrives as a cell, so it lands in the right square whatever
                // size their window is. Re-check it: a stale one can outlive its own move.
                var remote = Net.RemoteHover;
                return remote != null && _board.IsPlayable(remote.Value.Macro, remote.Value.Micro) ? remote : null;
            }

            var cell = layout.Pick(mouse);
            if (cell == null || !_board.IsPlayable(cell.Value.Macro, cell.Value.Micro)) {
                Net.SendHover(null);
                return null;
            }

            Net.SendHover(cell);
            if (!clicked) return cell;

            Net.SendPlay(cell.Value.Macro, cell.Value.Micro);
            MakePlay(cell.Value.Macro, cell.Value.Micro);
            // That square is taken now, so nothing is previewed until the mouse moves.
            return null;
        }

        protected override void Draw(GameTime gameTime) {
            GraphicsDevice.Clear(BoardView.Background);

            float width = GraphicsDevice.Viewport.Width;
            float height = GraphicsDevice.Viewport.Height;
            var layout = BoardLayout.Fit(width, height);

            _sb.Begin();
            _view.Draw(_sb, _font, _board, layout);
            DrawHud(layout);
            _lobby.Draw(_sb, _font, width, height);
            _sb.End();

            base.Draw(gameTime);
        }

        Lobby.Bounds OnlineButtonBounds(BoardLayout layout) {
            string label = OnlineLabel();
            float size = 15f;
            float w = _font.MeasureString(label, size).X + 24f;
            return new Lobby.Bounds(
                new Vector2(layout.Origin.X + layout.Size - w, (BoardLayout.HudHeight - 28f) / 2f),
                new Vector2(w, 28f));
        }

        string OnlineLabel() => Net.Status switch {
            Net.Mode.Connecting => "connecting...",
            Net.Mode.Waiting => Net.IsHost ? $"code {Net.Code}" : $"waiting {Net.Code}",
            Net.Mode.Searching => "searching...",
            Net.Mode.Playing => Net.IsHost ? $"{Net.Code} - you are X" : $"{Net.Code} - you are O",
            _ => "play online",
        };

        void DrawHud(BoardLayout layout) {
            const float size = 19f;
            const float swatch = 26f;
            float y = (BoardLayout.HudHeight - swatch) / 2f;
            var origin = new Vector2(layout.Origin.X, y);

            Color c = _board.Turn == Mark.X ? TWColor.Red500 : TWColor.Blue500;
            string status = _board.Winner switch {
                Mark.X => "X wins",
                Mark.O => "O wins",
                _ => _board.IsDraw ? "Draw" : _board.Turn == Mark.X ? "X to play" : "O to play",
            };
            if (Net.HasPeer && !_board.IsOver) {
                status += Net.IsLocalTurn(_isPlayer1) ? " - your turn" : " - their turn";
            }

            if (!_board.IsOver) {
                _sb.DrawRectangle(origin, new Vector2(swatch), c, TWColor.Gray200, 2f, 6f);
            }

            // DrawString takes the top left of the line, so center the label against the swatch
            // by its own line height rather than guessing at an offset.
            float textY = y + (swatch - _font.LineHeight * size) / 2f;
            _sb.DrawString(_font, status,
                new Vector2(origin.X + (_board.IsOver ? 0f : swatch + 12f), textY), size, TWColor.Gray100);

            string label = OnlineLabel();
            const float labelSize = 15f;
            Color fill = Net.HasPeer ? TWColor.Emerald900 : Net.IsOnline ? TWColor.Blue900 : TWColor.Gray800;
            Color border = Net.HasPeer ? TWColor.Emerald600 : Net.IsOnline ? TWColor.Blue600 : TWColor.Gray600;
            if (_onlineHovered) fill = TWColor.Gray700;

            _sb.FillRectangle(_onlineButton.XY, _onlineButton.Size, fill, 8f);
            _sb.BorderRectangle(_onlineButton.XY, _onlineButton.Size, border, 1.5f, 8f);
            _sb.DrawString(_font, label,
                new Vector2(_onlineButton.XY.X + 12f,
                            _onlineButton.XY.Y + (_onlineButton.Size.Y - _font.LineHeight * labelSize) / 2f),
                labelSize, TWColor.Gray200);
        }

        public static string GetPath(string name) => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name);
        public static T LoadJson<T>(string name, JsonTypeInfo<T> typeInfo) where T : new() {
            T json;
            string jsonPath = GetPath(name);

            if (File.Exists(jsonPath)) {
                json = JsonSerializer.Deserialize<T>(File.ReadAllText(jsonPath), typeInfo)!;
            } else {
                json = new T();
            }

            return json;
        }
        public static T EnsureJson<T>(string name, JsonTypeInfo<T> typeInfo) where T : new() {
            T json;
            string jsonPath = GetPath(name);

            if (File.Exists(jsonPath)) {
                json = JsonSerializer.Deserialize<T>(File.ReadAllText(jsonPath), typeInfo)!;
            } else {
                json = new T();
                string jsonString = JsonSerializer.Serialize(json, typeInfo);
                File.WriteAllText(jsonPath, jsonString);
            }

            return json;
        }
        public static void SaveJson<T>(string name, T json, JsonTypeInfo<T> typeInfo) {
            string jsonPath = GetPath(name);
            string jsonString = JsonSerializer.Serialize(json, typeInfo);
            File.WriteAllText(jsonPath, jsonString);
        }

        GraphicsDeviceManager _graphics;
        ShapeBatch _sb;
        ShapeFont _font;

        readonly BoardView _view = new BoardView();
        readonly Lobby _lobby = new Lobby();

        Lobby.Bounds _onlineButton;
        bool _onlineHovered;

        ICondition _playerClick = new MouseCondition(MouseButton.LeftButton);
        ICondition _reset = new KeyboardCondition(Keys.R);
        ICondition _toggleLobby = new KeyboardCondition(Keys.Tab);
    }
}

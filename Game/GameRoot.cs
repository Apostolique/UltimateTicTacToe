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

        /// <summary>Back buffer pixels per game unit. The browser host sets it; everything else
        /// leaves it at 1.</summary>
        /// A phone's screen has about three real pixels per CSS pixel, and the browser build
        /// renders at the real count so the board doesn't come out soft. That would shrink the
        /// hud to a third of its size, since <see cref="BoardLayout.HudHeight"/> and the label
        /// sizes are absolute, so the game keeps working in CSS pixels and hands the scale to
        /// the view matrix instead. Apos.Shapes draws analytically, so the shapes stay sharp
        /// under it rather than being magnified.
        public static float UiScale = 1f;

        /// <summary>Back buffer the browser host wants, in device pixels. Null everywhere else.</summary>
        /// KNI resizes the canvas back to the CSS size whenever the window changes, so the back
        /// buffer has to be put back alongside it or the two disagree and the game draws into a
        /// corner of its own canvas.
        public static Point? BackBuffer;

        /// <summary>How much of the viewport a soft keyboard hasn't covered, in game units.
        /// Null everywhere the host has no way to know.</summary>
        /// A phone keyboard takes the bottom of the page without shrinking it, so the canvas
        /// keeps its full height and the lobby panel would sit centered half behind it.
        public static float? VisibleHeight;

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

#if !BLAZORGL
            // The canvas is already the size the page made it, and forcing a back buffer over
            // that just draws the game into a corner of it.
            _graphics.PreferredBackBufferWidth = 800;
            _graphics.PreferredBackBufferHeight = 860;
            _graphics.ApplyChanges();
#endif

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
            InputHelper.UpdateSetup(gameTime);
            TweenHelper.UpdateSetup(gameTime);

            Net.PollEvents();

            if (BackBuffer is Point target && target.X > 0 && target.Y > 0 &&
                (_graphics.PreferredBackBufferWidth != target.X || _graphics.PreferredBackBufferHeight != target.Y)) {
                _graphics.PreferredBackBufferWidth = target.X;
                _graphics.PreferredBackBufferHeight = target.Y;
                _graphics.ApplyChanges();
            }

            float width = GraphicsDevice.Viewport.Width / UiScale;
            float height = GraphicsDevice.Viewport.Height / UiScale;
            var layout = BoardLayout.Fit(width, height);

            // No division here. The canvas is denser than its own css size, and KNI reports both
            // the mouse and touch against that css size, so a pointer already arrives in the
            // units the layout works in.
            Vector2 mouse = Pointer.Position;
            bool clicked = _playerClick.Pressed();

            _onlineButton = OnlineButtonBounds(layout);
            _onlineHovered = !_lobby.IsOpen && _onlineButton.Contains(mouse);

            _restartButton = RestartButtonBounds(layout, height);
            _restartHovered = !_lobby.IsOpen && _board.IsOver && _restartButton.Contains(mouse);

            // The panel closes inside its own update, so by the line after it the board is
            // already reachable again and the press that hit Close is still in hand. Spend it
            // here: the alternative is a move played on whatever square the button was over.
            bool panelWasOpen = _lobby.IsOpen;
            _lobby.Update(_font, width, MathF.Min(VisibleHeight ?? height, height), mouse, clicked);
            if (panelWasOpen) clicked = false;

            (int Macro, int Micro)? hovered = null;
            if (!_lobby.IsOpen) {
                if (_toggleLobby.Pressed()) {
                    _lobby.Toggle();
                } else if (clicked && _onlineHovered) {
                    _lobby.Open();
                } else if (clicked && _restartHovered) {
                    Net.SendReset();
                    Reset();
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

            float width = GraphicsDevice.Viewport.Width / UiScale;
            float height = GraphicsDevice.Viewport.Height / UiScale;
            var layout = BoardLayout.Fit(width, height);

            _sb.Begin(view: Matrix.CreateScale(UiScale, UiScale, 1f));
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

        string StatusLabel() {
            string status = _board.Winner switch {
                Mark.X => "X wins",
                Mark.O => "O wins",
                _ => _board.IsDraw ? "Draw" : _board.Turn == Mark.X ? "X to play" : "O to play",
            };
            if (Net.HasPeer && !_board.IsOver) {
                status += Net.IsLocalTurn(_isPlayer1) ? " - your turn" : " - their turn";
            }
            return status;
        }

        /// <summary>Centered in the room under the board, and only once the game is over.</summary>
        /// A phone has no keyboard to press R with, so the reset needs something to tap. It goes
        /// under the board rather than in the hud because the hud strip is only the board's
        /// width, and on a phone the status and the online button have already spent it.
        Lobby.Bounds RestartButtonBounds(BoardLayout layout, float viewportHeight) {
            var size = new Vector2(_font.MeasureString(RestartLabel, 15f).X + 24f, 28f);
            float bottom = layout.Origin.Y + layout.Size;
            return new Lobby.Bounds(
                new Vector2(layout.Center.X - size.X / 2f,
                            bottom + (viewportHeight - bottom - size.Y) / 2f),
                size);
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
            string status = StatusLabel();

            if (!_board.IsOver) {
                _sb.DrawRectangle(origin, new Vector2(swatch), c, TWColor.Gray200, 2f, 6f);
            }

            // DrawString takes the top left of the line, so center the label against the swatch
            // by its own line height rather than guessing at an offset.
            float textY = y + (swatch - _font.LineHeight * size) / 2f;
            _sb.DrawString(_font, status,
                new Vector2(origin.X + (_board.IsOver ? 0f : swatch + 12f), textY), size, TWColor.Gray100);

            if (_board.IsOver) {
                Color restartFill = _restartHovered && Pointer.Source == PointerSource.Mouse
                    ? TWColor.Gray700 : TWColor.Gray800;
                _sb.FillRectangle(_restartButton.XY, _restartButton.Size, restartFill, 8f);
                _sb.BorderRectangle(_restartButton.XY, _restartButton.Size, TWColor.Gray600, 1.5f, 8f);
                _sb.DrawString(_font, RestartLabel,
                    new Vector2(_restartButton.XY.X + 12f,
                                _restartButton.XY.Y + (_restartButton.Size.Y - _font.LineHeight * 15f) / 2f),
                    15f, TWColor.Gray200);
            }

            string label = OnlineLabel();
            const float labelSize = 15f;
            Color fill = Net.HasPeer ? TWColor.Emerald900 : Net.IsOnline ? TWColor.Blue900 : TWColor.Gray800;
            Color border = Net.HasPeer ? TWColor.Emerald600 : Net.IsOnline ? TWColor.Blue600 : TWColor.Gray600;
            // Touch has no hover, and the pointer stays where the last tap landed, so the button
            // would sit lit up with nothing on the screen.
            if (_onlineHovered && Pointer.Source == PointerSource.Mouse) fill = TWColor.Gray700;

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

        const string RestartLabel = "play again";

        Lobby.Bounds _onlineButton;
        bool _onlineHovered;

        Lobby.Bounds _restartButton;
        bool _restartHovered;

        ICondition _playerClick =
            new AnyCondition(
                new MouseCondition(MouseButton.LeftButton),
                new TouchCondition()
            );
        ICondition _reset = new KeyboardCondition(Keys.R);
        ICondition _toggleLobby = new KeyboardCondition(Keys.Tab);
    }
}

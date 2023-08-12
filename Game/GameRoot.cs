using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Apos.Input;
using Apos.Shapes;
using Apos.Tweens;
using LiteNetLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace GameProject {
    public class GameRoot : Game {
        public static float DeltaTime { get; private set; }
        public static Vector2 MousePosition;

        public GameRoot() {
            _graphics = new GraphicsDeviceManager(this);
            _graphics.GraphicsProfile = GraphicsProfile.HiDef;
            IsMouseVisible = true;
            Content.RootDirectory = "Content";

            Settings = EnsureJson<Settings>("Settings.json", SettingsContext.Default.Settings);
        }

        protected override void Initialize() {
            Window.AllowUserResizing = true;

            _graphics.PreferredBackBufferWidth = 700;
            _graphics.PreferredBackBufferHeight = 700;
            _graphics.ApplyChanges();

            base.Initialize();
        }

        protected override void LoadContent() {
            _s = new SpriteBatch(GraphicsDevice);
            _sb = new ShapeBatch(GraphicsDevice, Content);

            InputHelper.Setup(this);

            NetServer.Host();
        }

        protected override void Update(GameTime gameTime) {
            InputHelper.UpdateSetup();
            TweenHelper.UpdateSetup(gameTime);

            if (_quit.Pressed())
                Exit();

            if (KeyboardCondition.Pressed(Keys.Enter) && !NetClient.IsRunning) {
                NetServer.Stop();

                Settings = EnsureJson<Settings>("Settings.json", SettingsContext.Default.Settings);

                NetClient.Join(Settings.HostIp);
            }

            DeltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;

            NetServer.PollEvents();
            NetClient.PollEvents();

            if (_isPlayer1 && NetServer.IsRunning || !_isPlayer1 && NetClient.IsRunning || !NetServer.HasPeer && !NetClient.HasPeer) {
                MousePosition = InputHelper.NewMouse.Position.ToVector2();
                if (_playerClick.Pressed()) {
                    var v = WorldToMicroBoard(MousePosition);
                    if (v != null && (ForcedMacro == null || ForcedMacro.Value == v.Value.X) && _board.IsAvailable(v.Value.X, v.Value.Y)) {
                        if (NetServer.IsRunning) {
                            var w = NetServer.CreatePacket(NetServer.Packets.MakePlay);
                            w.Put(0, 8, v.Value.X);
                            w.Put(0, 8, v.Value.Y);
                            NetServer.SendToAll(w, 0, DeliveryMethod.ReliableOrdered);
                        } else if (NetClient.IsRunning) {
                            var w = NetClient.CreatePacket(NetClient.Packets.MakePlay);
                            w.Put(0, 8, v.Value.X);
                            w.Put(0, 8, v.Value.Y);
                            NetClient.Send(w, 0, DeliveryMethod.ReliableOrdered);
                        }
                        MakePlay(v.Value.X, v.Value.Y);
                    }
                }
            }

            if (_reset.Pressed()) {
                if (NetServer.IsRunning) {
                    var w = NetServer.CreatePacket(NetServer.Packets.ResetGame);
                    NetServer.SendToAll(w, 0, DeliveryMethod.ReliableOrdered);
                } else if (NetClient.IsRunning) {
                    var w = NetClient.CreatePacket(NetClient.Packets.ResetGame);
                    NetClient.Send(w, 0, DeliveryMethod.ReliableOrdered);
                }
                Reset();
            }

            InputHelper.UpdateCleanup();
            base.Update(gameTime);
        }

        protected override void Draw(GameTime gameTime) {
            GraphicsDevice.Clear(Color.Black);

            _sb.Begin();
            DrawPlayerIndicator();

            _board.Draw(_sb);

            Color c = _isPlayer1 ? TWColor.Red300 : TWColor.Blue300;
            var v = WorldToMicroBoard(MousePosition);
            if (v != null && (ForcedMacro == null || ForcedMacro.Value == v.Value.X) && _board.IsAvailable(v.Value.X, v.Value.Y)) {
                bool isCreated = _cursor == null;
                Vector2? oldCursor = _cursor;
                _cursor = CoordinateToWorld(v.Value.X, v.Value.Y);
                _cursorPlayer = _isPlayer1;

                if (isCreated) {
                    if (_cursorKillXY != null) {
                        _cursorCreate = new FloatTween(_cursorKill.Value, 1f, 800, Easing.SineInOut);
                        _cursorMotion = new Vector2Tween(_cursorKillXY.Value, _cursor.Value, 800, Easing.BounceOut);
                        _cursorKillXY = null;
                    } else {
                        _cursorCreate = new FloatTween(0f, 1f, 800, Easing.ElasticOut);
                        _cursorMotion = new WaitTween<Vector2>(_cursor.Value, 0);
                    }
                }

                if (_cursor != oldCursor) {
                    _cursorMotion = new Vector2Tween(_cursorMotion.Value, _cursor.Value, 800, Easing.BounceOut);
                }

                _sb.DrawCircle(_cursorMotion.Value, 10f * _cursorCreate.Value, c, TWColor.Black, 2f);
            } else if (_cursor != null) {
                if (_cursorPlayer == _isPlayer1) {
                    _cursorKillXY = _cursor;
                    _cursorKill = new FloatTween(1f, 0f, 400, Easing.BackIn);
                    _cursorKillColor = c;
                }
                _cursor = null;
            }
            if (_cursorKillXY != null) {
                _sb.DrawCircle(_cursorKillXY.Value, 10f * _cursorKill.Value, _cursorKillColor, TWColor.Black, 2f);

                if (_cursorKill.Value == 0f) {
                    _cursorKillXY = null;
                }
            }
            _sb.End();

            base.Draw(gameTime);
        }

        public static void Reset() {
            _board = new MacroBoard();
            ForcedMacro = null;
            _isPlayer1 = true;
        }

        public static void DrawX(ShapeBatch sb, Vector2 xy, Vector2 size, float scale) {
            sb.DrawLine(xy - (size / 2f) * scale, xy + (size / 2f) * scale, 8f * scale, TWColor.White, TWColor.Red500, 4f * scale);
            sb.DrawLine(xy + new Vector2(-size.X / 2f, size.Y / 2f) * scale, xy + new Vector2(size.X / 2f, -size.Y / 2f) * scale, 8f * scale, TWColor.White, TWColor.Red500, 4f * scale);
            sb.FillLine(xy - (size / 2f) * scale, xy + (size / 2f) * scale, 4f * scale, TWColor.White);
        }
        public static void DrawO(ShapeBatch sb, Vector2 xy, float size, float scale) {
            sb.BorderCircle(xy, (size / 2f) * scale, TWColor.Blue500, 4f * scale);
            sb.BorderCircle(xy, (size / 2f - 4f) * scale, TWColor.White, 8f * scale);
            sb.BorderCircle(xy, (size / 2f - 12f) * scale, TWColor.Blue500, 4f * scale);
        }
        private void DrawPlayerIndicator() {
            Color c = _isPlayer1 ? TWColor.Red500 : TWColor.Blue500;
            _sb.DrawRectangle(new Vector2(10, 10), new Vector2(30, 30), c, TWColor.White, 2f);
        }
        public static void DrawBoard(ShapeBatch sb, Vector2 offset, float spacing, Color c) {
            for (int i = 1; i <= 2; i++) {
                sb.FillLine(new Vector2(offset.X + i * spacing, offset.Y), new Vector2(offset.X + i * spacing, offset.Y + spacing * 3f), 4f, c);
                sb.FillLine(new Vector2(offset.X, offset.Y + i * spacing), new Vector2(offset.X + spacing * 3f, offset.Y + i * spacing), 4f, c);
            }
        }

        private int? WorldToMacroBoard(Vector2 xy) {
            if (
                xy.X <= MacroOffset.X ||
                xy.X >= MacroOffset.X + MacroSize * 3f ||
                xy.Y <= MacroOffset.Y ||
                xy.Y >= MacroOffset.Y + MacroSize * 3f) {
                return null;
            }

            var macroX = (int)MathF.Floor((xy.X - MacroOffset.X) / MacroSize);
            var macroY = (int)MathF.Floor((xy.Y - MacroOffset.Y) / MacroSize);

            return macroY * 3 + macroX;
        }
        public static(int X, int Y) ? WorldToMicroBoard(Vector2 xy) {
            if (
                xy.X <= MacroOffset.X ||
                xy.X >= MacroOffset.X + MacroSize * 3f ||
                xy.Y <= MacroOffset.Y ||
                xy.Y >= MacroOffset.Y + MacroSize * 3f) {
                return null;
            }

            var macroX = (int)MathF.Floor((xy.X - MacroOffset.X) / MacroSize);
            var macroY = (int)MathF.Floor((xy.Y - MacroOffset.Y) / MacroSize);

            var microX = (int)MathHelper.Clamp(MathF.Floor((xy.X - FullOffset.X - macroX * MacroSize) / MicroSize), 0, 2);
            var microY = (int)MathHelper.Clamp(MathF.Floor((xy.Y - FullOffset.Y - macroY * MacroSize) / MicroSize), 0, 2);

            return (macroY * 3 + macroX, microY * 3 + microX);
        }
        private Vector2 CoordinateToWorld(int x) {
            int macroX = x % 3;
            int macroY = x / 3;

            return new Vector2(MacroOffset.X + macroX * MacroSize + MacroSize / 2f, MacroOffset.Y + macroY * MacroSize + MacroSize / 2f);
        }
        private Vector2 CoordinateToWorld(int x, int y) {
            int macroX = x % 3;
            int macroY = x / 3;

            int microX = y % 3;
            int microY = y / 3;

            return new Vector2(FullOffset.X + macroX * MacroSize + microX * MicroSize + MicroSize / 2f, FullOffset.Y + macroY * MacroSize + microY * MicroSize + MicroSize / 2f);
        }

        public static Mark Validate(ITile[] tiles) {
            // Horizontal line
            for (int i = 0; i < 3; i++) {
                if (IsSame(tiles[i * 3], tiles[i * 3 + 1], tiles[i * 3 + 2])) {
                    return tiles[i * 3].Owner;
                }
            }

            // Vertical line
            for (int i = 0; i < 3; i++) {
                if (IsSame(tiles[i], tiles[i + 3], tiles[i + 6])) {
                    return tiles[i].Owner;
                }
            }

            // Diagonal lines
            if (IsSame(tiles[0], tiles[4], tiles[8])) {
                return tiles[0].Owner;
            } else if (IsSame(tiles[2], tiles[4], tiles[6])) {
                return tiles[2].Owner;
            }

            return Mark.None;
        }
        public static bool IsSame(ITile a, ITile b, ITile c) {
            return a.Owner != Mark.None && a.Owner == b.Owner && b.Owner == c.Owner;
        }

        public static void MakePlay(int x, int y) {
            Mark m = _isPlayer1 ? Mark.X : Mark.O;
            _board.Capture(x, y, m);
            _isPlayer1 = !_isPlayer1;

            if (_board.IsAvailable(y)) {
                ForcedMacro = y;
            } else {
                ForcedMacro = null;
            }
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

        private class MacroBoard : ITile {
            public Mark Owner { get; set; } = Mark.None;
            public ITween<float> Scale { get; set; } = new FloatTween(0f, 1f, 1000, Easing.ElasticOut);
            public Color OldColor { get; set; } = TWColor.Black;
            public Color OppositeColor { get; set; } = TWColor.Black;
            public Color NewColor { get; set; } = TWColor.Black;
            public FloatTween ColorTween { get; set; }

            public void Capture(int x, int y, Mark player) {
                _tiles[x].Capture(y, player);
                Owner = Validate(_tiles);

                if (Owner != Mark.None) {
                    Scale = new WaitTween<float>(0f, 400).To(1f, 1000, Easing.ElasticOut);
                }
            }

            public bool IsAvailable(int x) {
                return Owner == Mark.None && _tiles[x].Owner == Mark.None;
            }

            public bool IsAvailable(int x, int y) {
                return Owner == Mark.None && _tiles[x].Owner == Mark.None && _tiles[x].IsAvailable(y);
            }

            public void Draw(ShapeBatch sb) {
                bool isActive = Owner == Mark.None;
                OppositeColor = !isActive ? TWColor.White : TWColor.Gray600;
                NewColor = isActive ? TWColor.White : TWColor.Gray600;

                if (NewColor != OldColor) {
                    OldColor = NewColor;
                    ColorTween = new FloatTween(0f, 1f, 200, Easing.CircInOut);
                }

                DrawBoard(sb, MacroOffset, MacroSize, Color.Lerp(OppositeColor, NewColor, ColorTween.Value));

                for (int i = 0; i < _tiles.Length; i++) {
                    int macroX = i % 3;
                    int macroY = i / 3;

                    _tiles[i].Draw(sb, i, macroX, macroY, IsAvailable(i));
                }

                DrawTiles(sb);
            }

            private void DrawTiles(ShapeBatch sb) {
                for (int i = 0; i < _tiles.Length; i++) {
                    int macroX = i % 3;
                    int macroY = i / 3;

                    _tiles[i].DrawTiles(sb, macroX, macroY);

                    Vector2 center = new Vector2(MacroOffset.X + macroX * MacroSize + MacroSize / 2f, MacroOffset.Y + macroY * MacroSize + MacroSize / 2f);

                    if (_tiles[i].Owner == Mark.X) {
                        DrawX(sb, center, new Vector2(MacroSize - 32f, MacroSize - 32f), _tiles[i].Scale.Value);
                    } else if (_tiles[i].Owner == Mark.O) {
                        DrawO(sb, center, MacroSize - 16f, _tiles[i].Scale.Value);
                    }
                }

                Vector2 boardCenter = new Vector2(MacroOffset.X + MacroSize * 3f / 2f, MacroOffset.Y + MacroSize * 3f / 2f);
                if (Owner == Mark.X) {
                    DrawX(sb, boardCenter, new Vector2(MacroSize * 3f - 32, MacroSize * 3f - 32), Scale.Value);
                } else if (Owner == Mark.O) {
                    DrawO(sb, boardCenter, MacroSize * 3f - 16, Scale.Value);
                }
            }

            MicroBoard[] _tiles = new MicroBoard[9] {
                new MicroBoard(), new MicroBoard(), new MicroBoard(),
                new MicroBoard(), new MicroBoard(), new MicroBoard(),
                new MicroBoard(), new MicroBoard(), new MicroBoard(),
            };
        }

        private class MicroBoard : ITile {
            public Mark Owner { get; set; } = Mark.None;
            public ITween<float> Scale { get; set; }
            public Color OldColor { get; set; } = TWColor.Black;
            public Color OppositeColor { get; set; } = TWColor.Black;
            public Color NewColor { get; set; } = TWColor.Black;
            public FloatTween ColorTween { get; set; }

            public void Capture(int index, Mark player) {
                _tiles[index].Owner = player;
                _tiles[index].Scale = new FloatTween(0f, 1f, 1000, Easing.ElasticOut);
                Owner = Validate(_tiles);

                if (Owner != Mark.None) {
                    Scale = new WaitTween<float>(0f, 200).To(1f, 1000, Easing.ElasticOut);
                }
            }

            public bool IsAvailable(int index) {
                return _tiles[index].Owner == Mark.None;
            }

            public void Draw(ShapeBatch sb, int index, int macroX, int macroY, bool isAvailable) {
                bool isActive = (ForcedMacro == null || ForcedMacro.Value == index) && isAvailable;
                OppositeColor = !isActive ? TWColor.White : TWColor.Gray600;
                NewColor = isActive ? TWColor.White : TWColor.Gray600;

                if (NewColor != OldColor) {
                    OldColor = NewColor;
                    ColorTween = new FloatTween(0f, 1f, 200, Easing.CircInOut);
                }

                DrawBoard(sb, new Vector2(FullOffset.X + macroX * MacroSize, FullOffset.Y + macroY * MacroSize), MicroSize, Color.Lerp(OppositeColor, NewColor, ColorTween.Value));
            }

            public void DrawTiles(ShapeBatch sb, int macroX, int macroY) {
                for (int i = 0; i < _tiles.Length; i++) {
                    int x = i % 3;
                    int y = i / 3;

                    Vector2 center = new Vector2(FullOffset.X + macroX * MacroSize + x * MicroSize + MicroSize / 2f, FullOffset.Y + macroY * MacroSize + y * MicroSize + MicroSize / 2f);

                    if (_tiles[i].Owner == Mark.X) {
                        DrawX(sb, center, new Vector2(MicroSize - 32f, MicroSize - 32f), _tiles[i].Scale.Value);
                    } else if (_tiles[i].Owner == Mark.O) {
                        DrawO(sb, center, MicroSize - 16f, _tiles[i].Scale.Value);
                    }
                }
            }

            Tile[] _tiles = new Tile[9] {
                new Tile(), new Tile(), new Tile(),
                new Tile(), new Tile(), new Tile(),
                new Tile(), new Tile(), new Tile()
            };
        }

        private class Tile : ITile {
            public Mark Owner { get; set; } = Mark.None;
            public ITween<float> Scale { get; set; } = new FloatTween(0f, 1f, 1000, Easing.ElasticOut);
        }

        public interface ITile {
            Mark Owner { get; set; }
            ITween<float> Scale { get; set; }
        }

        public enum Mark {
            None,
            X,
            O
        }

        GraphicsDeviceManager _graphics;
        SpriteBatch _s;
        ShapeBatch _sb;

        ICondition _quit =
            new AnyCondition(
                new KeyboardCondition(Keys.Escape),
                new GamePadCondition(GamePadButton.Back, 0)
            );
        ICondition _playerClick = new MouseCondition(MouseButton.LeftButton);
        ICondition _reset = new KeyboardCondition(Keys.R);

        Vector2? _cursor = null;
        bool _cursorPlayer;
        FloatTween _cursorCreate;
        ITween<Vector2> _cursorMotion;

        Vector2? _cursorKillXY = null;
        FloatTween _cursorKill;
        Color _cursorKillColor;

        public static Settings Settings;

        static MacroBoard _board = new MacroBoard();
        public static int? ForcedMacro = null;

        internal static bool _isPlayer1 = true;
        public static float MacroSize = 200f;
        public static float MicroSize = 200f / 4f;
        public static Vector2 MacroOffset = new Vector2(50, 50);
        public static Vector2 MicroOffset = new Vector2(25, 25);
        public static Vector2 FullOffset = new Vector2(75, 75);
    }
}

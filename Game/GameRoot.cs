﻿using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Apos.Input;
using Apos.Shapes;

namespace GameProject {
    public class GameRoot : Game {
        public GameRoot() {
            _graphics = new GraphicsDeviceManager(this);
            IsMouseVisible = true;
            Content.RootDirectory = "Content";
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
        }

        protected override void Update(GameTime gameTime) {
            InputHelper.UpdateSetup();

            if (_quit.Pressed())
                Exit();

            if (_playerClick.Pressed()) {
                var v = WorldToMicroBoard(InputHelper.NewMouse.Position.ToVector2());
                if (v != null && (_forcedMacro == null || _forcedMacro.Value == v.Value.X) && _board.IsAvailable(v.Value.X, v.Value.Y)) {
                    Mark m = _isPlayer1 ? Mark.X : Mark.O;
                    _board.Capture(v.Value.X, v.Value.Y, m);
                    _isPlayer1 = !_isPlayer1;

                    if (_board.IsAvailable(v.Value.Y)) {
                        _forcedMacro = v.Value.Y;
                    } else {
                        _forcedMacro = null;
                    }
                }
            }

            InputHelper.UpdateCleanup();
            base.Update(gameTime);
        }

        protected override void Draw(GameTime gameTime) {
            GraphicsDevice.Clear(Color.Black);

            _sb.Begin();
            DrawPlayerIndicator();
            DrawBoards();

            _board.DrawTiles(_sb);

            var v = WorldToMicroBoard(InputHelper.NewMouse.Position.ToVector2());
            if (v != null && (_forcedMacro == null || _forcedMacro.Value == v.Value.X) && _board.IsAvailable(v.Value.X, v.Value.Y)) {
                Color c = _isPlayer1 ? TWColor.Red300 : TWColor.Blue300;
                _sb.DrawCircle(CoordinateToWorld(v.Value.X, v.Value.Y), 10f, c, TWColor.Black, 2f);
            }
            _sb.End();

            base.Draw(gameTime);
        }

        public static void DrawX(ShapeBatch sb, Vector2 xy, Vector2 size) {
            sb.DrawLine(xy - size / 2f, xy + size / 2f, 8f, TWColor.White, TWColor.Red500, 4f);
            sb.DrawLine(xy + new Vector2(-size.X / 2f, size.Y / 2f), xy + new Vector2(size.X / 2f, -size.Y / 2f), 8f, TWColor.White, TWColor.Red500, 4f);
            sb.FillLine(xy - size / 2f, xy + size / 2f, 4f, TWColor.White);
        }
        public static void DrawO(ShapeBatch sb, Vector2 xy, float size) {
            sb.BorderCircle(xy, size / 2f, TWColor.Blue500, 4f);
            sb.BorderCircle(xy, size / 2f - 4f, TWColor.White, 8f);
            sb.BorderCircle(xy, size / 2f - 12f, TWColor.Blue500, 4f);
        }
        private void DrawPlayerIndicator() {
            Color c = _isPlayer1 ? TWColor.Red500 : TWColor.Blue500;
            _sb.DrawRectangle(new Vector2(10, 10), new Vector2(30, 30), c, TWColor.White, 2f);
        }
        private void DrawBoards() {
            DrawBoard(MacroOffset, MacroSize, TWColor.White);

            for (int i = 0; i < 3; i++) {
                for (int j = 0; j < 3; j++) {
                    int index = j * 3 + i;
                    Color c = (_forcedMacro == null || _forcedMacro.Value == index) && _board.IsAvailable(index) ? TWColor.White : TWColor.Gray600;
                    DrawBoard(new Vector2(FullOffset.X + i * MacroSize, FullOffset.Y + j * MacroSize), MicroSize, c);
                }
            }
        }
        private void DrawBoard(Vector2 offset, float spacing, Color c) {
            for (int i = 1; i <= 2; i++) {
                _sb.FillLine(new Vector2(offset.X + i * spacing, offset.Y), new Vector2(offset.X + i * spacing, offset.Y + spacing * 3f), 4f, c);
                _sb.FillLine(new Vector2(offset.X,  offset.Y + i * spacing), new Vector2(offset.X + spacing * 3f, offset.Y + i * spacing), 4f, c);
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
        private (int X, int Y)? WorldToMicroBoard(Vector2 xy) {
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

        private class MacroBoard : ITile {
            public Mark Owner { get; set; } = Mark.None;

            public void Capture(int x, int y, Mark player) {
                _tiles[x].Capture(y, player);
                Owner = Validate(_tiles);
            }

            public bool IsAvailable(int x) {
                return Owner == Mark.None && _tiles[x].Owner == Mark.None;
            }

            public bool IsAvailable(int x, int y) {
                return Owner == Mark.None && _tiles[x].Owner == Mark.None && _tiles[x].IsAvailable(y);
            }

            public void DrawTiles(ShapeBatch sb) {
                for (int i = 0; i < _tiles.Length; i++) {
                    int macroX = i % 3;
                    int macroY = i / 3;

                    _tiles[i].DrawTiles(sb, macroX, macroY);

                    Vector2 center = new Vector2(MacroOffset.X + macroX * MacroSize + MacroSize / 2f, MacroOffset.Y + macroY * MacroSize + MacroSize / 2f);

                    if (_tiles[i].Owner == Mark.X) {
                        DrawX(sb, center, new Vector2(MacroSize - 32, MacroSize - 32));
                    } else if(_tiles[i].Owner == Mark.O) {
                        DrawO(sb, center, MacroSize - 16);
                    }
                }

                Vector2 boardCenter = new Vector2(MacroOffset.X + MacroSize * 3f / 2f, MacroOffset.Y + MacroSize * 3f / 2f);
                if (Owner == Mark.X) {
                    DrawX(sb, boardCenter, new Vector2(MacroSize * 3f - 32, MacroSize * 3f - 32));
                } else if(Owner == Mark.O) {
                    DrawO(sb, boardCenter, MacroSize * 3f - 16);
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

            public void Capture(int index, Mark player) {
                _tiles[index].Owner = player;
                Owner = Validate(_tiles);
            }

            public bool IsAvailable(int index) {
                return _tiles[index].Owner == Mark.None;
            }

            public void DrawTiles(ShapeBatch sb, int macroX, int macroY) {
                for (int i = 0; i < _tiles.Length; i++) {
                    int x = i % 3;
                    int y = i / 3;

                    Vector2 center = new Vector2(FullOffset.X + macroX * MacroSize + x * MicroSize + MicroSize / 2f, FullOffset.Y + macroY * MacroSize + y * MicroSize + MicroSize / 2f);

                    if (_tiles[i].Owner == Mark.X) {
                        DrawX(sb, center, new Vector2(MicroSize - 32, MicroSize - 32));
                    } else if(_tiles[i].Owner == Mark.O) {
                        DrawO(sb, center, MicroSize - 16);
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
        }

        public interface ITile {
            Mark Owner { get; set; }
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

        MacroBoard _board = new MacroBoard();
        int? _forcedMacro = null;

        bool _isPlayer1 = true;
        public static float MacroSize = 200f;
        public static float MicroSize = 200f / 4f;
        public static Vector2 MacroOffset = new Vector2(50, 50);
        public static Vector2 MicroOffset = new Vector2(25, 25);
        public static Vector2 FullOffset = new Vector2(75, 75);
    }
}
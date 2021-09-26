using System;
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
                _isPlayer1 = !_isPlayer1;
            }

            InputHelper.UpdateCleanup();
            base.Update(gameTime);
        }

        protected override void Draw(GameTime gameTime) {
            GraphicsDevice.Clear(Color.Black);

            _sb.Begin();
            DrawPlayerIndicator();
            DrawBoards();
            DrawX(new Vector2(100, 100), new Vector2(15, 15));
            DrawO(new Vector2(200, 200), 30);

            var v = MouseToMicroBoard(InputHelper.NewMouse.Position.ToVector2());
            if (v != null) _sb.DrawCircle(v.Value, 10f, TWColor.Purple300, TWColor.Black, 2f);
            _sb.End();

            base.Draw(gameTime);
        }

        private void DrawX(Vector2 xy, Vector2 size) {
            _sb.DrawLine(xy - size / 2f, xy + size / 2f, 8f, TWColor.White, TWColor.Red500, 4f);
            _sb.DrawLine(xy + new Vector2(-size.X / 2f, size.Y / 2f), xy + new Vector2(size.X / 2f, -size.Y / 2f), 8f, TWColor.White, TWColor.Red500, 4f);
            _sb.FillLine(xy - size / 2f, xy + size / 2f, 4f, TWColor.White);
        }
        private void DrawO(Vector2 xy, float size) {
            _sb.DrawCircle(xy, size / 2f, TWColor.White, TWColor.Blue500, 4f);
        }
        private void DrawPlayerIndicator() {
            Color c = TWColor.Red500;
            if (!_isPlayer1) {
                c = TWColor.Blue500;
            }

            _sb.DrawRectangle(new Vector2(10, 10), new Vector2(30, 30), c, TWColor.White, 2f);
        }
        private void DrawBoards() {
            DrawBoard(_macroOffset, _macroSize);

            for (int i = 0; i < 3; i++) {
                for (int j = 0; j < 3; j++) {
                    DrawBoard(new Vector2(_fullOffset.X + i * _macroSize, _fullOffset.Y + j * _macroSize), _microSize);
                }
            }
        }
        private void DrawBoard(Vector2 offset, float spacing) {
            for (int i = 1; i <= 2; i++) {
                _sb.FillLine(new Vector2(offset.X + i * spacing, offset.Y), new Vector2(offset.X + i * spacing, offset.Y + spacing * 3f), 4f, TWColor.White);
                _sb.FillLine(new Vector2(offset.X,  offset.Y + i * spacing), new Vector2(offset.X + spacing * 3f, offset.Y + i * spacing), 4f, TWColor.White);
            }
        }

        private Vector2? MouseToMacroBoard(Vector2 xy) {
            if (
                xy.X <= _macroOffset.X ||
                xy.X >= _macroOffset.X + _macroSize * 3f ||
                xy.Y <= _macroOffset.Y ||
                xy.Y >= _macroOffset.Y + _macroSize * 3f) {
                return null;
            }

            var x = MathF.Floor((xy.X - _macroOffset.X) / _macroSize);
            var y = MathF.Floor((xy.Y - _macroOffset.Y) / _macroSize);

            return new Vector2(_macroOffset.X + x * _macroSize + _macroSize / 2f, _macroOffset.Y + y * _macroSize + _macroSize / 2f);
        }
        private Vector2? MouseToMicroBoard(Vector2 xy) {
            if (
                xy.X <= _macroOffset.X ||
                xy.X >= _macroOffset.X + _macroSize * 3f ||
                xy.Y <= _macroOffset.Y ||
                xy.Y >= _macroOffset.Y + _macroSize * 3f) {
                return null;
            }

            var macroX = MathF.Floor((xy.X - _macroOffset.X) / _macroSize);
            var macroY = MathF.Floor((xy.Y - _macroOffset.Y) / _macroSize);

            var x = MathHelper.Clamp(MathF.Floor((xy.X - _fullOffset.X - macroX * _macroSize) / _microSize), 0, 2);
            var y = MathHelper.Clamp(MathF.Floor((xy.Y - _fullOffset.Y - macroY * _macroSize) / _microSize), 0, 2);

            return new Vector2(_fullOffset.X + macroX * _macroSize + x * _microSize + _microSize / 2f, _fullOffset.Y + macroY * _macroSize + y * _microSize + _microSize / 2f);
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

        bool _isPlayer1 = true;
        float _macroSize = 200f;
        float _microSize = 200f / 4f;
        Vector2 _macroOffset = new Vector2(50, 50);
        Vector2 _microOffset = new Vector2(25, 25);
        Vector2 _fullOffset = new Vector2(75, 75);
    }
}

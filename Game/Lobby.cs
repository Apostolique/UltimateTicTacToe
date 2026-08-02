using System;
using System.Text;
using Apos.Input;
using Apos.Shapes;
using Apos.Tweens;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace GameProject {
    /// <summary>
    /// The panel for starting an online game. Hit testing happens in <see cref="Update"/> and
    /// stores what it worked out, so <see cref="Draw"/> only reads: the two have to agree on
    /// the layout, and computing it twice is how a button ends up half a pixel off what you
    /// can actually click.
    /// </summary>
    public class Lobby {
        const int CodeLength = 5;
        /// Matches the relay's alphabet. Anything outside it can't be a real code.
        const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

        /// A clickable rectangle. Shared with the HUD so its button hit tests the same way.
        public readonly struct Bounds(Vector2 xy, Vector2 size) {
            public readonly Vector2 XY = xy;
            public readonly Vector2 Size = size;
            public bool Contains(Vector2 p) =>
                p.X >= XY.X && p.Y >= XY.Y && p.X < XY.X + Size.X && p.Y < XY.Y + Size.Y;
        }

        readonly StringBuilder _typed = new();

        Bounds _panel, _primary, _secondary, _codeBox, _join, _close;
        bool _hasPrimary, _hasSecondary, _hasCode, _hasJoin;
        bool _wasPlaying;
        int _hovered = -1;

        public bool IsOpen { get; private set; }

        public void Toggle() {
            if (IsOpen) Close(); else Open();
        }
        public void Open() {
            // Reopening starts on an empty field rather than resuming a half typed code.
            _typed.Clear();
            IsOpen = true;
        }
        public void Close() => IsOpen = false;

        public void Update(float viewportWidth, float viewportHeight, Vector2 mouse, bool clicked) {
            // An opponent turning up is the point of the panel, so it gets out of the way. On
            // the transition only: closing it whenever the game is on would mean it could
            // never be reopened, and Disconnect lives in here.
            if (Net.Status == Net.Mode.Playing && _wasPlaying == false) IsOpen = false;
            _wasPlaying = Net.Status == Net.Mode.Playing;

            if (!IsOpen) return;

            if (KeyboardCondition.Pressed(Keys.Escape)) {
                IsOpen = false;
                return;
            }

            Layout(viewportWidth, viewportHeight);
            ReadTyping();

            _hovered = -1;
            if (_hasPrimary && _primary.Contains(mouse)) _hovered = 0;
            else if (_hasSecondary && _secondary.Contains(mouse)) _hovered = 1;
            else if (_hasJoin && CanJoin && _join.Contains(mouse)) _hovered = 2;
            else if (_close.Contains(mouse)) _hovered = 3;

            bool submit = KeyboardCondition.Pressed(Keys.Enter);
            if (submit && CanJoin) {
                Net.JoinGame(_typed.ToString());
                _typed.Clear();
                return;
            }

            if (!clicked) return;

            switch (_hovered) {
                case 0: OnPrimary(); break;
                case 1: Net.FindMatch(); break;
                case 2: Net.JoinGame(_typed.ToString()); _typed.Clear(); break;
                case 3: IsOpen = false; break;
            }
        }

        bool CanJoin => Net.Status == Net.Mode.Offline && _typed.Length == CodeLength;

        void OnPrimary() {
            switch (Net.Status) {
                case Net.Mode.Offline: Net.HostGame(); break;
                default: Net.Disconnect(); break;   // waiting, searching, connecting
            }
        }

        void ReadTyping() {
            if (Net.Status != Net.Mode.Offline) return;

            foreach (TextInputEventArgs e in InputHelper.TextEvents) {
                if (e.Key == Keys.Back) {
                    if (_typed.Length > 0) _typed.Length--;
                    continue;
                }
                char c = char.ToUpperInvariant(e.Character);
                if (_typed.Length < CodeLength && Alphabet.IndexOf(c) >= 0) _typed.Append(c);
            }
        }

        void Layout(float viewportWidth, float viewportHeight) {
            float width = MathF.Min(400f, viewportWidth - 32f);
            const float pad = 24f;
            const float row = 46f;
            const float gap = 12f;

            _hasPrimary = true;
            _hasSecondary = Net.Status == Net.Mode.Offline;
            _hasCode = Net.Status == Net.Mode.Offline;
            _hasJoin = _hasCode;

            // Title, then whatever rows this state needs, then Close.
            float height = pad + 30f + gap;
            if (_hasSecondary) height += 24f + gap;                 // status or code line
            else height += 58f;                                     // the code, shown big
            height += row + gap;                                    // primary
            if (_hasSecondary) height += row + gap;                 // find a match
            if (_hasCode) height += 18f + gap + row + gap;          // "or join with a code" + field
            if (Net.Error != null) height += 22f + gap;
            height += row + pad;                                    // close

            var origin = new Vector2((viewportWidth - width) / 2f, MathF.Max((viewportHeight - height) / 2f, 16f));
            _panel = new Bounds(origin, new Vector2(width, height));

            float y = origin.Y + pad + 30f + gap;
            y += _hasSecondary ? 24f + gap : 58f;
            if (Net.Error != null) y += 22f + gap;

            float inner = width - pad * 2f;
            _primary = new Bounds(new Vector2(origin.X + pad, y), new Vector2(inner, row));
            y += row + gap;

            if (_hasSecondary) {
                _secondary = new Bounds(new Vector2(origin.X + pad, y), new Vector2(inner, row));
                y += row + gap;
            }

            if (_hasCode) {
                y += 18f + gap;
                float joinWidth = 96f;
                _codeBox = new Bounds(new Vector2(origin.X + pad, y), new Vector2(inner - joinWidth - gap, row));
                _join = new Bounds(new Vector2(origin.X + pad + inner - joinWidth, y), new Vector2(joinWidth, row));
                y += row + gap;
            }

            _close = new Bounds(new Vector2(origin.X + pad, y), new Vector2(inner, row));
        }

        public void Draw(ShapeBatch sb, ShapeFont font, float viewportWidth, float viewportHeight) {
            if (!IsOpen) return;

            // Dim the board so the panel is clearly the thing being talked to.
            sb.FillRectangle(Vector2.Zero, new Vector2(viewportWidth, viewportHeight), TWColor.Black * 0.55f);

            sb.FillRectangleBlurred(_panel.XY + new Vector2(0f, 8f), _panel.Size, TWColor.Black * 0.6f, 28f, 18f);
            sb.FillRectangle(_panel.XY, _panel.Size, TWColor.Gray900, 18f);
            sb.BorderRectangle(_panel.XY, _panel.Size, TWColor.Gray700, 2f, 18f);

            const float pad = 24f;
            const float gap = 12f;
            float y = _panel.XY.Y + pad;

            Text(sb, font, "Play online", new Vector2(_panel.XY.X + pad, y), 22f, TWColor.Gray100);
            y += 30f + gap;

            switch (Net.Status) {
                case Net.Mode.Offline:
                    Text(sb, font, "Anyone on desktop or in a browser can join.",
                        new Vector2(_panel.XY.X + pad, y), 14f, TWColor.Gray500);
                    break;
                case Net.Mode.Connecting:
                    Text(sb, font, "Connecting to the relay...", new Vector2(_panel.XY.X + pad, y), 16f, TWColor.Gray400);
                    break;
                case Net.Mode.Searching:
                    Text(sb, font, "Looking for an opponent" + Dots(), new Vector2(_panel.XY.X + pad, y), 16f, TWColor.Gray400);
                    break;
                case Net.Mode.Waiting:
                    Text(sb, font, "Share this code", new Vector2(_panel.XY.X + pad, y), 14f, TWColor.Gray500);
                    Text(sb, font, Net.Code, new Vector2(_panel.XY.X + pad, y + 18f), 34f, TWColor.Blue300);
                    break;
                case Net.Mode.Playing:
                    Text(sb, font, $"Playing {Net.Code}, you are {(Net.IsHost ? "X" : "O")}",
                        new Vector2(_panel.XY.X + pad, y), 16f, TWColor.Emerald300);
                    Text(sb, font, "Both of you can press R to start over.",
                        new Vector2(_panel.XY.X + pad, y + 24f), 14f, TWColor.Gray500);
                    break;
            }
            y += Net.Status == Net.Mode.Offline || Net.Status == Net.Mode.Connecting || Net.Status == Net.Mode.Searching
                ? 24f + gap
                : 58f;

            if (Net.Error != null) {
                Text(sb, font, Net.Error, new Vector2(_panel.XY.X + pad, y), 14f, TWColor.Red400);
                y += 22f + gap;
            }

            string primaryLabel = Net.Status switch {
                Net.Mode.Offline => "Host a game",
                Net.Mode.Searching => "Stop searching",
                _ => "Disconnect",
            };
            Button(sb, font, _primary, primaryLabel, _hovered == 0, Net.Status == Net.Mode.Offline, true);

            if (_hasSecondary) Button(sb, font, _secondary, "Find a match", _hovered == 1, false, true);

            if (_hasCode) {
                y = _codeBox.XY.Y - 18f - gap;
                Text(sb, font, "or join with a code", new Vector2(_panel.XY.X + pad, y), 14f, TWColor.Gray500);

                sb.FillRectangle(_codeBox.XY, _codeBox.Size, TWColor.Gray800, 10f);
                sb.BorderRectangle(_codeBox.XY, _codeBox.Size, TWColor.Gray600, 2f, 10f);

                // A caret parked after the last character, so an empty field still reads as
                // something you can type into.
                string shown = _typed.ToString();
                float codeSize = 26f;
                Vector2 measured = font.MeasureString(shown, codeSize);
                var textAt = new Vector2(_codeBox.XY.X + 14f, _codeBox.XY.Y + (_codeBox.Size.Y - font.LineHeight * codeSize) / 2f);
                if (shown.Length > 0) sb.DrawString(font, shown, textAt, codeSize, TWColor.Gray100);
                if (TweenHelper.TotalMS % 1000 < 550) {
                    sb.FillRectangle(new Vector2(textAt.X + measured.X + 2f, textAt.Y + 3f),
                        new Vector2(2f, font.LineHeight * codeSize - 6f), TWColor.Gray400);
                }

                Button(sb, font, _join, "Join", _hovered == 2, true, CanJoin);
            }

            Button(sb, font, _close, IsHotseatLabel() ? "Play on one screen" : "Close", _hovered == 3, false, true);
        }

        bool IsHotseatLabel() => Net.Status == Net.Mode.Offline;

        static string Dots() {
            long step = TweenHelper.TotalMS / 400 % 4;
            return new string('.', (int)step);
        }

        static void Button(ShapeBatch sb, ShapeFont font, Bounds box, string label, bool hovered, bool primary, bool enabled) {
            Color fill = !enabled ? TWColor.Gray800
                : primary ? (hovered ? TWColor.Blue500 : TWColor.Blue600)
                : hovered ? TWColor.Gray700 : TWColor.Gray800;
            Color border = !enabled ? TWColor.Gray700 : primary ? TWColor.Blue400 : TWColor.Gray600;
            Color text = enabled ? TWColor.Gray100 : TWColor.Gray600;

            sb.FillRectangle(box.XY, box.Size, fill, 10f);
            sb.BorderRectangle(box.XY, box.Size, border, 2f, 10f);

            const float size = 17f;
            Vector2 measured = font.MeasureString(label, size);
            sb.DrawString(font, label,
                new Vector2(box.XY.X + (box.Size.X - measured.X) / 2f,
                            box.XY.Y + (box.Size.Y - font.LineHeight * size) / 2f),
                size, text);
        }

        static void Text(ShapeBatch sb, ShapeFont font, string text, Vector2 xy, float size, Color color) =>
            sb.DrawString(font, text, xy, size, color);
    }
}

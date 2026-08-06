using System;
using System.Collections.Generic;
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

        /// <summary>One wrapped line of the panel's body text.</summary>
        readonly struct Line(string text, float size, Color color, float offset) {
            public readonly string Text = text;
            public readonly float Size = size;
            public readonly Color Color = color;
            /// How far below the top of the status block it sits.
            public readonly float Offset = offset;
        }

        readonly StringBuilder _typed = new();
        readonly List<Line> _status = [];

        Bounds _panel, _primary, _secondary, _tertiary, _codeBox, _join, _close;
        Vector2 _statusAt;
        bool _hasPrimary, _hasSecondary, _hasTertiary, _hasCode, _hasJoin;
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
        public void Close() {
            IsOpen = false;
            TextEntry.Hide();
        }

        /// <param name="visibleHeight">The part of the viewport a soft keyboard hasn't covered.
        /// The panel lays out inside it so the code field stays somewhere you can see it.</param>
        public void Update(ShapeFont font, float viewportWidth, float visibleHeight, Vector2 mouse, bool clicked) {
            // An opponent turning up is the point of the panel, so it gets out of the way. On
            // the transition only: closing it whenever the game is on would mean it could
            // never be reopened, and Disconnect lives in here.
            if (Net.Status == Net.Mode.Playing && _wasPlaying == false) Close();
            _wasPlaying = Net.Status == Net.Mode.Playing;

            if (!IsOpen) return;

            if (KeyboardCondition.Pressed(Keys.Escape)) {
                Close();
                return;
            }

            Layout(font, viewportWidth, visibleHeight);

            // Read, then decide, then push back. The other order writes last frame's string
            // over anything typed since.
            TextEntry.Poll();
            ReadTyping();
            if (_hasCode) TextEntry.Show(_codeBox.XY, _codeBox.Size, _typed.ToString(), CodeLength);
            else TextEntry.Hide();

            _hovered = -1;
            if (_hasPrimary && _primary.Contains(mouse)) _hovered = 0;
            else if (_hasSecondary && _secondary.Contains(mouse)) _hovered = 1;
            else if (_hasTertiary && _tertiary.Contains(mouse)) _hovered = 2;
            else if (_hasJoin && CanJoin && _join.Contains(mouse)) _hovered = 3;
            else if (_close.Contains(mouse)) _hovered = 4;

            bool submit = KeyboardCondition.Pressed(Keys.Enter) || TextEntry.Submitted;
            if (submit && CanJoin) {
                Net.JoinGame(_typed.ToString());
                _typed.Clear();
                return;
            }

            if (!clicked) return;

            switch (_hovered) {
                case 0: OnPrimary(); break;
                case 1: OnSecondary(); break;
                case 2: Net.SendSwap(); break;
                case 3: Net.JoinGame(_typed.ToString()); _typed.Clear(); break;
                case 4: Close(); break;
            }
        }

        bool CanJoin => Net.Status == Net.Mode.Offline && _typed.Length == CodeLength;

        void OnPrimary() {
            switch (Net.Status) {
                case Net.Mode.Offline: Net.HostGame(); break;
                default: Net.Disconnect(); break;   // waiting, searching, connecting
            }
        }

        /// The one row that means two different things, since the states that use it never
        /// overlap: a game to start over, or no game and somewhere to find one.
        static string SecondaryLabel => Net.Status == Net.Mode.Playing ? "Start over" : "Find a match";

        void OnSecondary() {
            if (Net.Status == Net.Mode.Playing) {
                Net.SendReset();
                GameRoot.Reset();
            } else {
                Net.FindMatch();
            }
        }

        void ReadTyping() {
            if (Net.Status != Net.Mode.Offline) return;

            // A focused field is the only thing that says what's in it. The key events are
            // still coming, since KNI listens on the window and so sees everything the input
            // does, and taking both would type every character twice.
            if (TextEntry.Focused) {
                _typed.Clear();
                foreach (char c in TextEntry.Text) Append(c);
                return;
            }

            foreach (TextInputEventArgs e in InputHelper.TextEvents) {
                if (e.Key == Keys.Back) {
                    if (_typed.Length > 0) _typed.Length--;
                    continue;
                }
                Append(e.Character);
            }
        }

        /// Anything outside the alphabet is dropped, so a full field and a key that can't be in
        /// a code both do nothing.
        void Append(char raw) {
            char c = char.ToUpperInvariant(raw);
            if (_typed.Length < CodeLength && Alphabet.IndexOf(c) >= 0) _typed.Append(c);
        }

        void Layout(ShapeFont font, float viewportWidth, float visibleHeight) {
            float width = MathF.Min(400f, viewportWidth - 32f);
            const float pad = 24f;
            const float row = 46f;
            const float gap = 12f;
            float inner = width - pad * 2f;

            _hasPrimary = true;
            _hasSecondary = Net.Status == Net.Mode.Offline || Net.Status == Net.Mode.Playing;
            _hasTertiary = Net.Status == Net.Mode.Playing;
            _hasCode = Net.Status == Net.Mode.Offline;
            _hasJoin = _hasCode;

            float status = LayoutStatus(font, inner) + gap;

            // Title, then whatever rows this state needs, then Close.
            float height = pad + 30f + gap;
            height += status;                                       // what the state has to say
            height += row + gap;                                    // primary
            if (_hasSecondary) height += row + gap;                 // find a match, or start over
            if (_hasTertiary) height += row + gap;                  // swap sides
            if (_hasCode) height += 18f + gap + row + gap;          // "or join with a code" + field
            height += row + pad;                                    // close

            var origin = new Vector2((viewportWidth - width) / 2f, MathF.Max((visibleHeight - height) / 2f, 16f));
            _panel = new Bounds(origin, new Vector2(width, height));

            _statusAt = new Vector2(origin.X + pad, origin.Y + pad + 30f + gap);

            float y = _statusAt.Y + status;
            _primary = new Bounds(new Vector2(origin.X + pad, y), new Vector2(inner, row));
            y += row + gap;

            if (_hasSecondary) {
                _secondary = new Bounds(new Vector2(origin.X + pad, y), new Vector2(inner, row));
                y += row + gap;
            }

            if (_hasTertiary) {
                _tertiary = new Bounds(new Vector2(origin.X + pad, y), new Vector2(inner, row));
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

        /// <summary>Wraps whatever the current state has to say to the panel's width and
        /// returns how tall that came out.</summary>
        float LayoutStatus(ShapeFont font, float inner) {
            _status.Clear();
            float at = 0f;

            switch (Net.Status) {
                case Net.Mode.Offline:
                    at = AddStatus(font, "Anyone can join, desktop or browser.", 14f, inner, TWColor.Gray500, at);
                    break;
                case Net.Mode.Connecting:
                    at = AddStatus(font, "Connecting to the relay...", 16f, inner, TWColor.Gray400, at);
                    break;
                case Net.Mode.Searching:
                    at = AddStatus(font, "Looking for an opponent" + Dots(), 16f, inner, TWColor.Gray400, at);
                    break;
                case Net.Mode.Waiting:
                    at = AddStatus(font, "Share this code", 14f, inner, TWColor.Gray500, at);
                    at = AddStatus(font, Net.Code, 34f, inner, TWColor.Blue300, at);
                    break;
                case Net.Mode.Playing:
                    at = AddStatus(font, $"Playing {Net.Code}, you are {(Net.IsHost ? "X" : "O")}",
                        16f, inner, TWColor.Emerald300, at);
                    at = AddStatus(font, "Either of you can use these.", 14f, inner, TWColor.Gray500, at);
                    break;
            }

            // The relay writes these, so there's no length to design around.
            if (Net.Error != null) at = AddStatus(font, Net.Error, 14f, inner, TWColor.Red400, at);

            return at;
        }

        /// <summary>Appends one string's worth of lines and returns where the next one starts.</summary>
        float AddStatus(ShapeFont font, string text, float size, float inner, Color color, float at) {
            foreach (string line in WrapLines(font, text, size, inner)) {
                _status.Add(new Line(line, size, color, at));
                at += font.LineHeight * size + 4f;
            }
            return at;
        }

        /// <summary>Greedy word wrap. A word wider than the panel on its own still runs over,
        /// which only a relay error could manage and breaking it would read worse.</summary>
        static IEnumerable<string> WrapLines(ShapeFont font, string text, float size, float width) {
            var line = new StringBuilder();

            foreach (string word in text.Split(' ')) {
                if (line.Length == 0) {
                    line.Append(word);
                    continue;
                }

                if (font.MeasureString($"{line} {word}", size).X > width) {
                    yield return line.ToString();
                    line.Clear();
                } else {
                    line.Append(' ');
                }
                line.Append(word);
            }

            if (line.Length > 0) yield return line.ToString();
        }

        public void Draw(ShapeBatch sb, ShapeFont font, float viewportWidth, float viewportHeight) {
            // Opening happens after the update that would have placed the panel, so the first
            // frame of it has nowhere to draw yet and everything would land in the corner.
            if (!IsOpen || _panel.Size.X <= 0f) return;

            // Dim the board so the panel is clearly the thing being talked to.
            sb.FillRectangle(Vector2.Zero, new Vector2(viewportWidth, viewportHeight), TWColor.Black * 0.55f);

            sb.FillRectangleBlurred(_panel.XY + new Vector2(0f, 8f), _panel.Size, TWColor.Black * 0.6f, 28f, 18f);
            sb.FillRectangle(_panel.XY, _panel.Size, TWColor.Gray900, 18f);
            sb.BorderRectangle(_panel.XY, _panel.Size, TWColor.Gray700, 2f, 18f);

            const float pad = 24f;
            const float gap = 12f;

            Text(sb, font, "Play online", new Vector2(_panel.XY.X + pad, _panel.XY.Y + pad), 22f, TWColor.Gray100);

            foreach (Line line in _status) {
                Text(sb, font, line.Text, _statusAt + new Vector2(0f, line.Offset), line.Size, line.Color);
            }

            string primaryLabel = Net.Status switch {
                Net.Mode.Offline => "Host a game",
                Net.Mode.Searching => "Stop searching",
                _ => "Disconnect",
            };
            Button(sb, font, _primary, primaryLabel, _hovered == 0, Net.Status == Net.Mode.Offline, true);

            if (_hasSecondary) Button(sb, font, _secondary, SecondaryLabel, _hovered == 1, false, true);
            if (_hasTertiary) Button(sb, font, _tertiary, "Swap sides", _hovered == 2, false, true);

            if (_hasCode) {
                Text(sb, font, "or join with a code",
                    new Vector2(_panel.XY.X + pad, _codeBox.XY.Y - 18f - gap), 14f, TWColor.Gray500);

                // Whether typing lands here. A mouse doesn't have to focus the field first,
                // since the key events reach the game either way, so this is only ever false on
                // a touch screen that hasn't been given a keyboard yet.
                bool live = TextEntry.Focused || Pointer.Source == PointerSource.Mouse;

                sb.FillRectangle(_codeBox.XY, _codeBox.Size, TWColor.Gray800, 10f);
                sb.BorderRectangle(_codeBox.XY, _codeBox.Size, live ? TWColor.Blue400 : TWColor.Gray600, 2f, 10f);

                string shown = _typed.ToString();
                float codeSize = 26f;
                Vector2 measured = font.MeasureString(shown, codeSize);
                var textAt = new Vector2(_codeBox.XY.X + 14f, _codeBox.XY.Y + (_codeBox.Size.Y - font.LineHeight * codeSize) / 2f);
                if (shown.Length > 0) sb.DrawString(font, shown, textAt, codeSize, TWColor.Gray100);

                // A caret parked after the last character, so an empty field still reads as
                // something you can type into. A caret over a box with no keyboard behind it
                // would be a lie, so that one says how to get one instead.
                if (live) {
                    if (TweenHelper.TotalMS % 1000 < 550) {
                        sb.FillRectangle(new Vector2(textAt.X + measured.X + 2f, textAt.Y + 3f),
                            new Vector2(2f, font.LineHeight * codeSize - 6f), TWColor.Gray400);
                    }
                } else if (shown.Length == 0) {
                    const float hintSize = 16f;
                    Text(sb, font, "tap to type",
                        new Vector2(textAt.X, _codeBox.XY.Y + (_codeBox.Size.Y - font.LineHeight * hintSize) / 2f),
                        hintSize, TWColor.Gray600);
                }

                Button(sb, font, _join, "Join", _hovered == 3, true, CanJoin);
            }

            Button(sb, font, _close, IsHotseatLabel() ? "Play on one screen" : "Close", _hovered == 4, false, true);
        }

        bool IsHotseatLabel() => Net.Status == Net.Mode.Offline;

        /// Padded out to its longest form. The font is monospaced, so the line keeps one width
        /// and the wrap can't reflow it four times a second.
        static string Dots() {
            long step = TweenHelper.TotalMS / 400 % 4;
            return new string('.', (int)step).PadRight(3);
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

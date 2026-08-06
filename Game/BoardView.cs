using System;
using Apos.Shapes;
using Apos.Tweens;
using Microsoft.Xna.Framework;

namespace GameProject {
    /// <summary>
    /// Draws a <see cref="Board"/> and owns every animation over it. The board holds no view
    /// state at all: <see cref="Sync"/> compares what it reads against what it drew last and
    /// starts a tween for whatever moved. That's the only place a tween is ever created, so a
    /// play made locally, one that arrived over the network, and a reset all animate the same.
    /// </summary>
    public class BoardView {
        /// What to clear the screen to before drawing.
        public static Color Background => TWColor.Gray900;

        // Three steps of hierarchy: the macro grid reads first, then a playable sub-grid, then
        // one that's out of reach. Live sits well below the macro grid on purpose, or a fresh
        // board where everything is playable turns into 81 equally loud cells.
        static readonly Color GridLive = TWColor.Gray400;
        static readonly Color GridDim = TWColor.Gray700;
        static readonly Color MacroGrid = TWColor.Gray100;

        /// Mark colors, dark end first, as a gradient runs top to bottom.
        static (Color Top, Color Bottom, Color Glow) Palette(Mark mark) => mark == Mark.X
            ? (TWColor.Red400, TWColor.Red600, TWColor.Red500)
            : (TWColor.Blue400, TWColor.Blue600, TWColor.Blue500);

        // What the view believes is on the board. A difference against the real one is the
        // signal to start an animation, and it's checked once per frame in Sync.
        readonly Mark[] _seenCells = new Mark[81];
        readonly Mark[] _seenMacro = new Mark[9];
        readonly bool[] _seenPlayable = new bool[9];
        Mark _seenWinner = Mark.None;
        bool _seenOver;
        /// The cell the last play landed on, as macro * 9 + micro, or -1 on a fresh board.
        int _seenLast = -1;

        readonly ITween<float>[] _cellPop = new ITween<float>[81];
        readonly ITween<float>[] _macroPop = new ITween<float>[9];
        readonly ITween<float>[] _macroGlow = new ITween<float>[9];
        ITween<float> _winnerPop = Fixed(0f);
        ITween<float> _bannerPop = Fixed(0f);
        ITween<float> _lastPop = Fixed(0f);

        (int Macro, int Micro)? _cursorCell;
        Vector2 _cursorTarget;
        ITween<Vector2> _cursorXY = new WaitTween<Vector2>(Vector2.Zero, 0);
        ITween<float> _cursorPop = Fixed(0f);
        Mark _cursorMark = Mark.X;

        public BoardView() {
            for (int i = 0; i < _cellPop.Length; i++) _cellPop[i] = Fixed(0f);
            for (int i = 0; i < 9; i++) {
                _macroPop[i] = Fixed(0f);
                _macroGlow[i] = Fixed(1f);
                _seenPlayable[i] = true;
            }
        }

        static ITween<float> Fixed(float value) => new FloatTween(value, value, 1, Easing.Linear);

        /// <summary>A mark landing, big enough to read as a thump. Delayed so a macro win
        /// lands after the mark that caused it.</summary>
        static ITween<float> PopIn(long delay, long duration) =>
            new WaitTween<float>(0f, delay).To(1f, duration, Easing.ElasticOut);
        static ITween<float> FadeOut(float from) =>
            new FloatTween(from, 0f, 180, Easing.CubeIn);

        public void Sync(Board board) {
            for (int macro = 0; macro < 9; macro++) {
                for (int micro = 0; micro < 9; micro++) {
                    int k = macro * 9 + micro;
                    Mark now = board.Cell(macro, micro);
                    if (now == _seenCells[k]) continue;

                    _seenCells[k] = now;
                    _cellPop[k] = now == Mark.None ? FadeOut(_cellPop[k].Value) : PopIn(0, 620);
                }

                Mark macroNow = board.MacroOwner(macro);
                if (macroNow != _seenMacro[macro]) {
                    _seenMacro[macro] = macroNow;
                    _macroPop[macro] = macroNow == Mark.None ? FadeOut(_macroPop[macro].Value) : PopIn(200, 780);
                }

                // Tweening from the live value rather than a fixed 0 or 1 means a board that
                // flips back before the fade finished picks up where it is instead of jumping.
                bool playable = board.IsPlayable(macro);
                if (playable != _seenPlayable[macro]) {
                    _seenPlayable[macro] = playable;
                    _macroGlow[macro] = new FloatTween(_macroGlow[macro].Value, playable ? 1f : 0f, 260, Easing.CircInOut);
                }
            }

            int last = board.LastMacro < 0 ? -1 : board.LastMacro * 9 + board.LastMicro;
            if (last != _seenLast) {
                _seenLast = last;
                // Behind the mark it belongs to rather than with it, so the thump lands first
                // and the ring settles around it. Moving on is a cut, the way a chess board
                // moves its highlight, since two lit cells would both look like the last move.
                _lastPop = last < 0
                    ? FadeOut(_lastPop.Value)
                    : new WaitTween<float>(0f, 160).To(1f, 320, Easing.CubeOut);
            }

            if (board.Winner != _seenWinner) {
                _seenWinner = board.Winner;
                _winnerPop = board.Winner == Mark.None ? FadeOut(_winnerPop.Value) : PopIn(420, 900);
            }

            if (board.IsOver != _seenOver) {
                _seenOver = board.IsOver;
                // A win waits for the big mark to land before the banner covers it up. This
                // one drives an alpha, so it eases instead of overshooting like the marks do.
                _bannerPop = board.IsOver
                    ? new WaitTween<float>(0f, board.Winner == Mark.None ? 260 : 1000).To(1f, 420, Easing.CubeOut)
                    : FadeOut(_bannerPop.Value);
            }
        }

        /// <summary>Point the hover preview at a cell, or pass null to send it away.</summary>
        public void SetCursor((int Macro, int Micro)? cell, Mark mark, BoardLayout layout) {
            if (cell == null) {
                if (_cursorCell != null) {
                    _cursorCell = null;
                    _cursorPop = new FloatTween(_cursorPop.Value, 0f, 240, Easing.BackIn);
                }
                return;
            }

            _cursorMark = mark;
            Vector2 target = layout.MicroCenter(cell.Value.Macro, cell.Value.Micro);

            if (_cursorCell == null) {
                // Coming back before the fade finished slides over instead of teleporting.
                _cursorXY = _cursorPop.Value > 0.01f
                    ? new Vector2Tween(_cursorXY.Value, target, 260, Easing.CubeOut)
                    : new WaitTween<Vector2>(target, 0);
                _cursorPop = new FloatTween(_cursorPop.Value, 1f, 520, Easing.ElasticOut);
            } else if (Vector2.DistanceSquared(_cursorTarget, target) > 0.01f) {
                // Comparing against the target rather than the cell index also catches a resize.
                _cursorXY = new Vector2Tween(_cursorXY.Value, target, 260, Easing.CubeOut);
            }

            _cursorCell = cell;
            _cursorTarget = target;
        }

        public void Draw(ShapeBatch sb, ShapeFont font, Board board, BoardLayout layout) {
            // The wash goes under the grid so it doesn't tint the lines, the outline goes over
            // it so the macro grid can't paint across the dashes on the edges they share.
            DrawForcedWash(sb, board, layout);
            DrawMicroGrids(sb, layout);
            DrawMacroGrid(sb, board, layout);
            DrawForcedOutline(sb, board, layout);
            DrawLastMove(sb, layout);
            DrawMarks(sb, layout);
            DrawCursor(sb, layout);
            DrawWinner(sb, board, layout);
            DrawBanner(sb, font, board, layout);
        }

        /// How long the marching ants take to travel one dash plus one gap.
        const float AntPeriodMs = 2600f;
        /// Dashes around the perimeter of the forced macro.
        const int AntCount = 28;
        const float OutlineThickness = 2.5f;
        /// FillLine takes a radius, so the macro grid covers this much either side of a boundary.
        const float MacroGridRadius = 4f;

        bool TryForced(Board board, out int macro, out float glow, out Color tint) {
            macro = board.ForcedMacro ?? -1;
            glow = macro < 0 ? 0f : _macroGlow[macro].Value;
            tint = default;
            if (macro < 0 || board.IsOver || glow <= 0.001f) return false;

            (_, _, tint) = Palette(board.Turn);
            return true;
        }

        void DrawForcedWash(ShapeBatch sb, Board board, BoardLayout layout) {
            if (!TryForced(board, out int macro, out float glow, out Color tint)) return;

            sb.FillRectangleBlurred(layout.MacroOrigin(macro), new Vector2(layout.Macro),
                tint * (0.20f * glow), 22f * layout.Unit, 14f * layout.Unit);
        }

        void DrawForcedOutline(ShapeBatch sb, Board board, BoardLayout layout) {
            if (!TryForced(board, out int macro, out float glow, out Color tint)) return;

            // Clear of the macro grid rather than drawn over it. That line is given a radius,
            // so it straddles the cell boundary by MacroGridRadius either side, and anything
            // sharing those pixels fights with it whichever one goes down second.
            float inset = MacroGridRadius * layout.Unit + OutlineThickness * layout.Unit + 3f * layout.Unit;
            Vector2 origin = layout.MacroOrigin(macro) + new Vector2(inset);
            Vector2 size = new Vector2(layout.Macro - inset * 2f);

            // Offset is in periods, not world units, so the phase is the whole of it. Counting
            // the dashes instead of sizing them keeps them from popping when the window resizes.
            float phase = (float)(TweenHelper.TotalMS % (long)AntPeriodMs) / AntPeriodMs;
            var dash = DashStyle.FromCount(AntCount, 0.55f, -phase, DashCap.Round);

            sb.BorderRectangle(origin, size, tint * glow, OutlineThickness * layout.Unit, 14f * layout.Unit, dash: dash);
        }

        /// <summary>A ring around the cell the last play landed on, so an opponent's move is
        /// findable without having watched it arrive.</summary>
        /// In the mark's own color rather than a neutral one: the two players read this board
        /// by color already, and whose move it was is half of what the ring is for.
        void DrawLastMove(ShapeBatch sb, BoardLayout layout) {
            if (_seenLast < 0) return;

            int macro = _seenLast / 9;
            // A settled macro swallows its own cells, so the ring goes with them.
            float pop = _lastPop.Value * (1f - _macroPop[macro].Value);
            if (pop <= 0.001f) return;

            (_, _, Color glow) = Palette(_seenCells[_seenLast]);
            float half = layout.Micro * 0.5f - 3f * layout.Unit;
            Vector2 origin = layout.MicroCenter(macro, _seenLast % 9) - new Vector2(half);

            sb.BorderRectangle(origin, new Vector2(half * 2f), glow * (0.6f * pop),
                1.6f * layout.Unit, 6f * layout.Unit);
        }

        void DrawMicroGrids(ShapeBatch sb, BoardLayout layout) {
            float thickness = 1.6f * layout.Unit;

            for (int macro = 0; macro < 9; macro++) {
                // A settled board's own grid goes with it, so the big mark reads on its own.
                float presence = 1f - _macroPop[macro].Value;
                if (presence <= 0.001f) continue;

                Color c = Color.Lerp(GridDim, GridLive, _macroGlow[macro].Value) * presence;
                Vector2 origin = layout.MacroOrigin(macro);

                for (int i = 1; i <= 2; i++) {
                    float offset = i * layout.Micro;
                    sb.FillLine(origin + new Vector2(offset, 0f), origin + new Vector2(offset, layout.Macro), thickness, c);
                    sb.FillLine(origin + new Vector2(0f, offset), origin + new Vector2(layout.Macro, offset), thickness, c);
                }
            }
        }

        void DrawMacroGrid(ShapeBatch sb, Board board, BoardLayout layout) {
            float thickness = MacroGridRadius * layout.Unit;
            Color c = board.IsOver ? GridDim : MacroGrid;

            for (int i = 1; i <= 2; i++) {
                float offset = i * layout.Macro;
                sb.FillLine(
                    layout.Origin + new Vector2(offset, 0f),
                    layout.Origin + new Vector2(offset, layout.Size), thickness, c);
                sb.FillLine(
                    layout.Origin + new Vector2(0f, offset),
                    layout.Origin + new Vector2(layout.Size, offset), thickness, c);
            }
        }

        void DrawMarks(ShapeBatch sb, BoardLayout layout) {
            for (int macro = 0; macro < 9; macro++) {
                float settled = _macroPop[macro].Value;

                // The small marks fade as the macro mark that swallowed them grows in.
                if (settled < 0.999f) {
                    for (int micro = 0; micro < 9; micro++) {
                        int k = macro * 9 + micro;
                        float pop = _cellPop[k].Value;
                        if (pop <= 0.001f) continue;

                        DrawMark(sb, _seenCells[k], layout.MicroCenter(macro, micro),
                            layout.Micro * 0.34f, pop, 1f - settled, layout.Unit);
                    }
                }

                if (settled > 0.001f) {
                    DrawMark(sb, _seenMacro[macro], layout.MacroCenter(macro),
                        layout.Macro * 0.34f, settled, 1f, layout.Unit);
                }
            }
        }

        void DrawWinner(ShapeBatch sb, Board board, BoardLayout layout) {
            float pop = _winnerPop.Value;
            if (pop <= 0.001f) return;

            DrawMark(sb, _seenWinner, layout.Center, layout.Size * 0.30f, pop, 0.95f, layout.Unit);
        }

        void DrawCursor(ShapeBatch sb, BoardLayout layout) {
            float pop = _cursorPop.Value;
            if (pop <= 0.001f) return;

            Vector2 xy = _cursorXY.Value;
            (_, _, Color glow) = Palette(_cursorMark);
            float radius = layout.Micro * 0.34f;

            // Slightly smaller than a placed mark, and lit from behind, so a preview never
            // reads as a move that already happened.
            sb.FillCircleBlurred(xy, radius * pop, glow * (0.45f * pop), 18f * layout.Unit);
            DrawMark(sb, _cursorMark, xy, radius * 0.88f, pop, 0.7f, layout.Unit);
        }

        /// <summary>
        /// One mark at a size. <paramref name="radius"/> is the circle it fits inside,
        /// <paramref name="scale"/> is the pop animation and <paramref name="alpha"/> the fade.
        /// </summary>
        static void DrawMark(ShapeBatch sb, Mark mark, Vector2 center, float radius, float scale, float alpha, float unit) {
            if (mark == Mark.None || scale <= 0.001f || alpha <= 0.001f) return;

            float r = radius * scale;
            float stroke = MathF.Max(r * 0.22f, 1f);
            (Color top, Color bottom, _) = Palette(mark);
            var fill = new Gradient(
                center - new Vector2(0f, r), top * alpha,
                center + new Vector2(0f, r), bottom * alpha);

            if (mark == Mark.X) {
                // Pull the arms in so the X fits the same circle the O does.
                float arm = r * 0.72f;
                sb.FillLine(center + new Vector2(-arm, -arm), center + new Vector2(arm, arm), stroke, fill);
                sb.FillLine(center + new Vector2(-arm, arm), center + new Vector2(arm, -arm), stroke, fill);
            } else {
                sb.BorderCircle(center, r - stroke, fill, stroke * 2f);
            }
        }

        void DrawBanner(ShapeBatch sb, ShapeFont font, Board board, BoardLayout layout) {
            float pop = _bannerPop.Value;
            if (pop <= 0.001f) return;

            string text = board.Winner switch {
                Mark.X => "X wins",
                Mark.O => "O wins",
                _ => "Draw",
            };
            // A phone has no R, and the button under the board is the answer there.
            const string hint = "tap play again, or press R";

            // The whole panel grows a little as it fades in, so every measurement rides the
            // animated size rather than being laid out once and scaled after.
            float grow = 0.94f + 0.06f * pop;
            float size = MathF.Max(layout.Size * 0.085f, 12f) * grow;
            float hintSize = size * 0.42f;
            Vector2 textSize = font.MeasureString(text, size);
            Vector2 hintSizeXY = font.MeasureString(hint, hintSize);

            float gap = size * 0.35f;
            float contentWidth = MathF.Max(textSize.X, hintSizeXY.X);
            float contentHeight = textSize.Y + gap + hintSizeXY.Y;
            Vector2 padding = new Vector2(size * 1.1f, size * 0.8f);
            Vector2 panel = new Vector2(contentWidth, contentHeight) + padding * 2f;
            Vector2 panelXY = layout.Center - panel / 2f;
            float corner = 18f * layout.Unit;

            // The blur is the shadow under the slab, not the slab itself: spread across a panel
            // this size it would wash the fill out and the board would read straight through.
            sb.FillRectangleBlurred(panelXY + new Vector2(0f, 7f * layout.Unit), panel,
                TWColor.Black * (0.6f * pop), 26f * layout.Unit, corner);
            sb.FillRectangle(panelXY, panel, TWColor.Gray900 * (0.97f * pop), corner);
            sb.BorderRectangle(panelXY, panel, TWColor.Gray700 * pop, 2f * layout.Unit, corner);

            Color textColor = board.Winner switch {
                Mark.X => TWColor.Red300,
                Mark.O => TWColor.Blue300,
                _ => TWColor.Gray200,
            };

            Vector2 textXY = new Vector2(layout.Center.X - textSize.X / 2f, panelXY.Y + padding.Y);
            sb.DrawString(font, text, textXY, size, textColor * pop);
            sb.DrawString(font, hint,
                new Vector2(layout.Center.X - hintSizeXY.X / 2f, textXY.Y + textSize.Y + gap),
                hintSize, TWColor.Gray400 * pop);
        }
    }
}

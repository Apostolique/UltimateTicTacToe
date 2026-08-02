using System;
using Microsoft.Xna.Framework;

namespace GameProject {
    /// <summary>
    /// Where the board sits in the window. Everything is derived from the viewport, so the
    /// board keeps its shape at any size. <see cref="Unit"/> is the scale against a 600 wide
    /// reference board, and every stroke width multiplies by it so the lines stay in
    /// proportion instead of going hairline on a big window.
    /// </summary>
    public readonly struct BoardLayout {
        const float ReferenceSize = 600f;
        const float Margin = 28f;

        /// Room kept clear at the top for the turn indicator.
        public const float HudHeight = 56f;

        public readonly Vector2 Origin;
        public readonly float Size;
        public readonly float Macro;
        public readonly float Micro;
        public readonly float Unit;

        BoardLayout(Vector2 origin, float size) {
            Origin = origin;
            Size = size;
            Macro = size / 3f;
            Micro = size / 9f;
            Unit = size / ReferenceSize;
        }

        public static BoardLayout Fit(float viewportWidth, float viewportHeight) {
            float width = viewportWidth - Margin * 2f;
            float height = viewportHeight - HudHeight - Margin * 2f;
            float size = MathF.Max(MathF.Min(width, height), 1f);

            return new BoardLayout(
                new Vector2(
                    (viewportWidth - size) / 2f,
                    HudHeight + (viewportHeight - HudHeight - size) / 2f),
                size);
        }

        public Vector2 Center => Origin + new Vector2(Size / 2f);

        public Vector2 MacroOrigin(int macro) =>
            Origin + new Vector2(macro % 3, macro / 3) * Macro;
        public Vector2 MacroCenter(int macro) =>
            MacroOrigin(macro) + new Vector2(Macro / 2f);
        public Vector2 MicroCenter(int macro, int micro) =>
            MacroOrigin(macro) + new Vector2(micro % 3, micro / 3) * Micro + new Vector2(Micro / 2f);

        /// <summary>The cell under a point, or null when the point is off the board.</summary>
        public (int Macro, int Micro)? Pick(Vector2 xy) {
            Vector2 local = xy - Origin;
            if (local.X < 0f || local.Y < 0f || local.X >= Size || local.Y >= Size) return null;

            int macroX = Math.Clamp((int)(local.X / Macro), 0, 2);
            int macroY = Math.Clamp((int)(local.Y / Macro), 0, 2);
            int microX = Math.Clamp((int)((local.X - macroX * Macro) / Micro), 0, 2);
            int microY = Math.Clamp((int)((local.Y - macroY * Macro) / Micro), 0, 2);

            return (macroY * 3 + macroX, microY * 3 + microX);
        }
    }
}

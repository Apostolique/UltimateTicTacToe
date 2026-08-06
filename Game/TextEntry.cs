#nullable enable
using Microsoft.Xna.Framework;

namespace GameProject {
    /// <summary>
    /// The one seam for a text field the host knows about. A canvas isn't focusable, so a phone
    /// has nothing to raise a keyboard for and the field the game draws is only pixels. The
    /// browser host parks a real `input` over that rectangle, and this is where its contents
    /// come back.
    /// </summary>
    /// Everything with a keyboard already attached leaves <see cref="Host"/> null, and the
    /// field goes on reading key events.
    public static class TextEntry {
        /// <summary>Installed by the browser host on its first tick. Null everywhere else.</summary>
        public static ITextEntry? Host;

        /// <summary>Whether the real field has the keyboard.</summary>
        public static bool Focused { get; private set; }

        /// <summary>True for the one frame after the keyboard's go key.</summary>
        public static bool Submitted { get; private set; }

        /// <summary>What the real field holds, unfiltered.</summary>
        public static string Text { get; private set; } = "";

        static bool _shown;

        /// <summary>
        /// Reads the field. It goes before deciding what the field should hold: the contents are
        /// the player's until the game has seen them, and pushing last frame's string over a
        /// keypress that arrived since would eat it.
        /// </summary>
        public static void Poll() {
            if (Host == null || !_shown) {
                Focused = false;
                Submitted = false;
                Text = "";
                return;
            }

            (Focused, Submitted, Text) = Host.Read();
        }

        /// <summary>Parks the real field over the drawn one and makes it agree with
        /// <paramref name="text"/>. Call it every frame the drawn field is there.</summary>
        public static void Show(Vector2 xy, Vector2 size, string text, int maxLength) {
            if (Host == null) return;

            _shown = true;
            Host.Show(xy.X, xy.Y, size.X, size.Y, text, maxLength);
        }

        /// <summary>The drawn field is gone, so the keyboard goes down with it.</summary>
        public static void Hide() {
            if (Host == null || !_shown) return;

            _shown = false;
            Focused = false;
            Submitted = false;
            Text = "";
            Host.Hide();
        }
    }

    /// <summary>
    /// What a host has to be able to do for <see cref="TextEntry"/>. The rectangle is in the
    /// units the game lays out in.
    /// </summary>
    public interface ITextEntry {
        void Show(float x, float y, float width, float height, string text, int maxLength);
        void Hide();
        (bool Focused, bool Submitted, string Text) Read();
    }
}

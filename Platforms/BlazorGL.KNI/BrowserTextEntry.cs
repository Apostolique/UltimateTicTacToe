using Microsoft.JSInterop;

namespace GameProject
{
    /// <summary>
    /// Mirrors the game's field onto a real `input` in the page. The game lays out in css pixels
    /// here and the canvas fills the viewport, so a rectangle goes through as a fixed position
    /// with no conversion.
    /// </summary>
    /// The focus itself happens in `index.html`, off the pointer event. iOS only raises the
    /// keyboard for a `focus()` that a gesture is still running, and the game's reaction to a
    /// tap is a frame too late for that.
    sealed class BrowserTextEntry : ITextEntry
    {
        readonly IJSInProcessRuntime _js;

        public BrowserTextEntry(IJSInProcessRuntime js) => _js = js;

        public void Show(float x, float y, float width, float height, string text, int maxLength) =>
            _js.InvokeVoid("utttTextEntryShow", x, y, width, height, text, maxLength);

        public void Hide() => _js.InvokeVoid("utttTextEntryHide");

        public (bool Focused, bool Submitted, string Text) Read()
        {
            // Two flags and then the value verbatim, so a value that reads like a flag can't be
            // taken for one.
            string s = _js.Invoke<string>("utttTextEntryRead");
            if (s == null || s.Length < 2) return (false, false, "");

            return (s[0] == '1', s[1] == '1', s.Substring(2));
        }
    }
}

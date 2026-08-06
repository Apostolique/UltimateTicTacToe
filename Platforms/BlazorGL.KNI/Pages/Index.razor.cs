using System;
using Microsoft.JSInterop;
using Microsoft.Xna.Framework;

namespace GameProject.Pages
{
    public partial class Index
    {
        Game _game;

        protected override void OnAfterRender(bool firstRender)
        {
            base.OnAfterRender(firstRender);

            if (firstRender)
            {
                JsRuntime.InvokeAsync<object>("initRenderJS", DotNetObjectReference.Create(this));
            }
        }

        [JSInvokable]
        public void TickDotNet()
        {
            // The page sizes the canvas in device pixels, and this is how much denser that made
            // it plus the size the back buffer has to match. Read every frame rather than once,
            // since a rotation or a browser zoom moves it and KNI resets the canvas whenever the
            // window resizes. The call is in-process under WASM, so it costs no round trip.
            if (JsRuntime is IJSInProcessRuntime js)
            {
                GameRoot.UiScale = Math.Clamp(js.Invoke<float>("utttUiScale"), 1f, 8f);
                GameRoot.BackBuffer = new Point(
                    js.Invoke<int>("utttBackBufferWidth"),
                    js.Invoke<int>("utttBackBufferHeight"));
                GameRoot.VisibleHeight = js.Invoke<float>("utttVisibleHeight");

                // The page can raise a keyboard and the canvas can't, so the join code's field
                // gets mirrored onto a real input.
                TextEntry.Host ??= new BrowserTextEntry(js);
            }

            // init game
            if (_game == null)
            {
                _game = new GameRoot();
                _game.Run();
            }

            // run gameloop
            _game.Tick();
        }

    }
}

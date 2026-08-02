# UltimateTicTacToe
Ultimate tic-tac-toe implementation in MonoGame and KNI, with online multiplayer.

## Platforms

`Game/` holds all the code. Each folder under `Platforms/` is a host that links it in.

| Platform | Framework | Notes |
| --- | --- | --- |
| `DesktopGL` | MonoGame | Windows, macOS, Linux. This is the one the release workflow ships. |
| `WindowsDX` | MonoGame | Windows only. |
| `DesktopGL.KNI` | KNI | Same desktops, useful to debug KNI without a browser in the way. |
| `WindowsDX.KNI` | KNI | Windows only. |
| `BlazorGL.KNI` | KNI | Runs in the browser, and it's what goes to itch.io as an HTML5 build. |

`Server/` is the relay that puts two players together. See [its readme](Server/README.md).

## Code

`Game/` splits along one line, and the split is the point: the rules don't know the board is
drawn, and the drawing doesn't know the rules.

`Board.cs` is the game. Nine macro cells, each a tic-tac-toe board of its own, plus whose
turn it is, who owns what, and which macro the next play is confined to. No MonoGame in
it. Being sent to a macro that's already won or full frees the next player to go anywhere,
so a game can't deadlock on a board with no legal move in it.

`BoardLayout.cs` is where the board sits. Everything comes off the viewport, so it keeps
its shape at any window size, and every stroke width multiplies by `Unit` so lines stay in
proportion instead of going hairline on a big window.

`BoardView.cs` draws it and owns every animation. It keeps its own copy of what it drew
last, and `Sync` starts a tween for whatever changed since. That's the only place a tween
is ever created, which is what makes a local play, one that arrived over the network, and
a reset all animate the same.

`GameRoot.cs` wires those together with input, `Lobby.cs` for the online panel, and `Net.cs`.

## Restore

```
dotnet restore Platforms/DesktopGL
dotnet restore Platforms/WindowsDX
dotnet restore Platforms/DesktopGL.KNI
dotnet restore Platforms/WindowsDX.KNI
dotnet restore Platforms/BlazorGL.KNI
```

## Run

```
dotnet run --project Platforms/DesktopGL
dotnet run --project Platforms/WindowsDX
dotnet run --project Platforms/DesktopGL.KNI
dotnet run --project Platforms/WindowsDX.KNI
```

The browser build serves itself:

```
dotnet run --project Platforms/BlazorGL.KNI
```

## Debug

In vscode, you can debug by pressing F5. There's a launch configuration per platform.

## Publish

```
dotnet publish Platforms/DesktopGL -c Release -r win-x64 --output artifacts/build-windows
dotnet publish Platforms/DesktopGL -c Release -r osx-x64 --output artifacts/build-osx
dotnet publish Platforms/DesktopGL -c Release -r linux-x64 --output artifacts/build-linux
```

```
dotnet publish Platforms/WindowsDX -c Release -r win-x64 --output artifacts/build-windowsdx
```

The web build lands in `artifacts/web/wwwroot`, with the `index.html` at its root. That
folder is what you upload to itch.io:

```
dotnet publish Platforms/BlazorGL.KNI -c Release --output artifacts/web
```

Pushing a `v*` tag runs all four and sends them to itch.io on the `windows`, `osx`,
`linux`, and `html5` channels.

## Multiplayer

Hit **play online** in the top right, or `Tab`. From there you can host a game and share the
five character code, type someone else's code in, or hit **Find a match** and get paired with
whoever else is waiting. `Escape` closes the panel and `R` starts the game over, on both
sides. With nobody connected the board is just two players on one screen.

**Desktop and browser players are in the same pool.** Everything goes through a websocket
relay, so nobody needs a reachable address, there's no NAT traversal to fail, and a code
minted in a browser tab works from the desktop build and the other way round.

Everything the game asks of the network goes through `Game/Net.cs`, and the transport under
it is `Game/RelayClient.cs`. That one file serves both platforms:
`System.Net.WebSockets.ClientWebSocket` is backed by the browser's own WebSocket under WASM,
so there's no JS interop layer to keep in step. The relay itself is in
[`Server/`](Server/README.md), and `Settings.json` next to the executable points at it.

Being a relay rather than peer to peer costs a hop through Cloudflare. For a game that sends
two bytes a move and waits on a human between them, that buys one player pool and no TURN
bill for the price of latency nobody can perceive.

## Content

`Content/source-code-pro-medium.ttf` is [Source Code
Pro](https://github.com/adobe-fonts/source-code-pro), under the SIL Open Font License 1.1.
It's copied to the output as-is rather than built, because Apos.Shapes reads the outlines
itself.

# UltimateTicTacToe
Ultimate tic-tac-toe implementation in MonoGame with online multiplayer.

## Restore

```
dotnet restore Platforms/DesktopGL
dotnet restore Platforms/WindowsDX
```

## Run

```
dotnet run --project Platforms/DesktopGL
dotnet run --project Platforms/WindowsDX
```

## Debug

In vscode, you can debug by pressing F5.

## Publish

```
dotnet publish Platforms/DesktopGL -c Release -r win-x64 --output artifacts/build-windows
dotnet publish Platforms/DesktopGL -c Release -r osx-x64 --output artifacts/build-osx
dotnet publish Platforms/DesktopGL -c Release -r linux-x64 --output artifacts/build-linux
```

```
dotnet publish Platforms/WindowsDX -c Release -r win-x64 --output artifacts/build-windowsdx
```

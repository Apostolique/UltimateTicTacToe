#nullable enable
using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GameProject {
    /// <summary>
    /// A line-oriented websocket client for the relay. The same code runs on desktop and in
    /// the browser: <c>ClientWebSocket</c> is backed by the browser's own WebSocket under
    /// WASM, so there's no JS interop layer to keep in step.
    /// </summary>
    /// <remarks>
    /// Blazor WASM is single threaded, so nothing here may block. Reads and writes run as
    /// tasks whose continuations land between frames, and the game loop only ever touches the
    /// two queues. That also means <c>SendAsync</c> can't be called from two places at once,
    /// hence the outbox and its pump rather than sending inline.
    /// </remarks>
    public sealed class RelayClient {
        public enum Status {
            Offline,
            Connecting,
            Connected,
            Failed,
        }

        readonly ConcurrentQueue<string> _inbox = new();
        readonly ConcurrentQueue<string> _outbox = new();

        ClientWebSocket? _socket;
        CancellationTokenSource? _cts;
        int _generation;

        public Status State { get; private set; } = Status.Offline;
        public string? Error { get; private set; }
        public bool IsConnected => State == Status.Connected;

        public void Connect(string url) {
            Close();

            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || (uri.Scheme != "ws" && uri.Scheme != "wss")) {
                State = Status.Failed;
                Error = "The relay address needs to start with ws:// or wss://";
                return;
            }

            State = Status.Connecting;
            Error = null;

            var socket = new ClientWebSocket();
            var cts = new CancellationTokenSource();
            _socket = socket;
            _cts = cts;

            // Every connection carries the generation it was started under, so a socket that
            // finishes connecting after Close() can't resurrect itself over a newer one.
            int generation = ++_generation;
            _ = RunAsync(socket, cts, uri, generation);
        }

        async Task RunAsync(ClientWebSocket socket, CancellationTokenSource cts, Uri uri, int generation) {
            try {
                await socket.ConnectAsync(uri, cts.Token);
                if (generation != _generation) return;

                State = Status.Connected;
                await Task.WhenAll(ReceiveLoopAsync(socket, cts, generation), SendLoopAsync(socket, cts, generation));
            } catch (Exception e) when (e is WebSocketException or OperationCanceledException or ObjectDisposedException or InvalidOperationException) {
                // Almost always either the relay isn't deployed yet or RelayUrl points at the
                // wrong place, so say that rather than just "it broke".
                Fail(generation, "Couldn't reach the relay. Check RelayUrl in Settings.json.");
            } catch (Exception e) {
                Fail(generation, e.Message);
            } finally {
                if (generation == _generation && State != Status.Failed) {
                    State = Status.Offline;
                }
            }
        }

        async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationTokenSource cts, int generation) {
            var buffer = new byte[4096];

            while (socket.State == WebSocketState.Open && !cts.IsCancellationRequested) {
                int count = 0;
                ValueWebSocketReceiveResult result;
                do {
                    if (count == buffer.Length) return;
                    result = await socket.ReceiveAsync(buffer.AsMemory(count), cts.Token);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    count += result.Count;
                } while (!result.EndOfMessage);

                if (generation != _generation) return;
                if (count > 0) _inbox.Enqueue(Encoding.UTF8.GetString(buffer, 0, count));
            }
        }

        async Task SendLoopAsync(ClientWebSocket socket, CancellationTokenSource cts, int generation) {
            while (socket.State == WebSocketState.Open && !cts.IsCancellationRequested) {
                if (!_outbox.TryDequeue(out string? line)) {
                    // No timer to lean on in WASM, so idle by yielding back to the loop.
                    await Task.Delay(16, cts.Token);
                    continue;
                }
                if (generation != _generation) return;
                await socket.SendAsync(Encoding.UTF8.GetBytes(line), WebSocketMessageType.Text, true, cts.Token);
            }
        }

        void Fail(int generation, string message) {
            if (generation != _generation) return;
            State = Status.Failed;
            Error = message;
        }

        public void Send(string line) {
            if (State is Status.Connecting or Status.Connected) _outbox.Enqueue(line);
        }

        public bool TryRead(out string line) => _inbox.TryDequeue(out line!);

        public void Close() {
            _generation++;
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;

            ClientWebSocket? socket = _socket;
            _socket = null;
            if (socket != null) {
                // Abort rather than CloseAsync: nothing here waits on a clean handshake, and
                // the relay treats a dropped socket the same as a polite goodbye.
                try { socket.Abort(); } catch { }
                socket.Dispose();
            }

            while (_inbox.TryDequeue(out _)) { }
            while (_outbox.TryDequeue(out _)) { }

            State = Status.Offline;
            Error = null;
        }
    }
}

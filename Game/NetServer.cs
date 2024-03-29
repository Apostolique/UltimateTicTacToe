using System;
using System.Collections.Generic;
using LiteNetLib;

namespace GameProject {
    static class NetServer {
        const float SYNC_PLAYER_TIME = .01666666666f;

        public static bool IsRunning => _manager.IsRunning;
        internal static readonly int _maxPacketId;

        public enum Packets {
            SyncPlayer,
            MakePlay,
            ResetGame,
        }

        static double _syncPlayersTimer;

        static readonly EventBasedNetListener _listener = new EventBasedNetListener();
        internal static readonly NetManager _manager = new NetManager(_listener) { AutoRecycle = true, UpdateTime = 15, ChannelsCount = 4 };
        static readonly Dictionary<int, NetWriter> _packets = new Dictionary<int, NetWriter>();
        static readonly int _packetClearStart;
        static readonly NetWriter _initialData = new NetWriter();
        static readonly NetReader _r = new NetReader();
        static bool _hasInitialData;

        public static bool HasPeer => _manager.ConnectedPeersCount > 0;

        static NetServer() {
            var packets = Enum.GetValues(typeof(Packets));
            _maxPacketId = packets.Length - 1;
            foreach (var p in packets) {
                var w = new NetWriter();
                _packetClearStart = w.Put(0, _maxPacketId, (int)p);
                _packets.Add((int)p, w);
            }
            _listener.NetworkReceiveEvent += (peer, readerOutdated, channel, deliveryMethod) => {
                if (readerOutdated.EndOfData) {
                    GameRoot.Reset();
                    _hasInitialData = true;
                    return;
                }
                _r.ReadFrom(readerOutdated);
                var p = (NetClient.Packets)_r.ReadInt(0, NetClient._maxPacketId);
                if (p == NetClient.Packets.SyncPlayer) {
                    if (!GameRoot._isPlayer1) {
                        GameRoot.MousePosition.X = _r.ReadFloat();
                        GameRoot.MousePosition.Y = _r.ReadFloat();
                    }
                } else if (p == NetClient.Packets.MakePlay) {
                    var x = _r.ReadInt(0, 8);
                    var y = _r.ReadInt(0, 8);
                    GameRoot.MakePlay(x, y);
                } else if (p == NetClient.Packets.ResetGame) {
                    GameRoot.Reset();
                }
            };
            _listener.PeerConnectedEvent += peer => {
                _initialData.Clear(0);
                Send(_initialData, peer, 0, DeliveryMethod.ReliableOrdered);
            };
            _listener.PeerDisconnectedEvent += (peer, disconnectInfo) => {};
            _listener.ConnectionRequestEvent += request => {
                if (_manager.ConnectedPeersCount > 0) {
                    request.Reject();
                    return;
                }
                var peer = request.Accept();
                _hasInitialData = false;
            };
        }

        public static void Host() {
            _manager.Start(GameRoot.Settings.Port);
            _initialData.Clear();
        }
        public static void Stop() {
            _manager.Stop(true);
        }

        public static NetWriter CreatePacket(Packets packetId) {
            var p = _packets[(int)packetId];
            p.Clear(_packetClearStart);
            return p;
        }
        public static void PollEvents() {
            if (!IsRunning)
                return;
            _manager.PollEvents();
            if (_hasInitialData && (_syncPlayersTimer += GameRoot.DeltaTime) >= SYNC_PLAYER_TIME) {
                _syncPlayersTimer -= SYNC_PLAYER_TIME;
                if (GameRoot._isPlayer1) {
                    var w = CreatePacket(Packets.SyncPlayer);
                    w.Put(GameRoot.MousePosition.X);
                    w.Put(GameRoot.MousePosition.Y);
                    SendToAll(w, 1, DeliveryMethod.Sequenced);
                }
            }
        }

        public static void SendToAll(NetWriter writer, byte channel, DeliveryMethod deliveryMethod) => _manager.SendToAll(writer.Data, 0, writer.LengthBytes, channel, deliveryMethod);
        public static void SendToAllExcept(NetWriter writer, byte channel, DeliveryMethod deliveryMethod, NetPeer excludePeer) => _manager.SendToAll(writer.Data, 0, writer.LengthBytes, channel, deliveryMethod, excludePeer);
        public static void Send(NetWriter writer, NetPeer peer, byte channel, DeliveryMethod deliveryMethod) => peer.Send(writer.Data, 0, writer.LengthBytes, channel, deliveryMethod);
    }
}

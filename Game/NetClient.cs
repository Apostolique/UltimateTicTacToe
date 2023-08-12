using System;
using System.Collections.Generic;
using System.Diagnostics;
using LiteNetLib;
using LiteNetLib.Utils;

namespace GameProject {
    static class NetClient {
        const float SYNC_PLAYER_TIME = .01666666666f;

        public static bool IsRunning => _manager.IsRunning;
        internal static readonly int _maxPacketId;

        public enum Packets {
            SyncPlayer,
            MakePlay,
            ResetGame,
        }

        static bool _hasInitialData;
        static readonly EventBasedNetListener _listener = new EventBasedNetListener();
        static readonly NetManager _manager = new NetManager(_listener) { AutoRecycle = true, UpdateTime = 15, ChannelsCount = 4 };
        static readonly Dictionary<int, NetWriter> _packets = new Dictionary<int, NetWriter>();
        static readonly int _packetClearStartBits;
        static readonly NetReader _r = new NetReader();

        public static bool HasPeer => _manager.ConnectedPeersCount > 0;

        static double _syncPlayersTimer;

        static NetClient() {
            var packets = Enum.GetValues(typeof(Packets));
            _maxPacketId = packets.Length - 1;
            foreach (var p in packets) {
                var w = new NetWriter();
                _packetClearStartBits = w.Put(0, _maxPacketId, (int)p);
                _packets.Add((int)p, w);
            }
            _listener.NetworkReceiveEvent += InitialData;
            _listener.PeerDisconnectedEvent += (peer, disconnectInfo) => {
                if (_hasInitialData) {
                    _listener.NetworkReceiveEvent -= OngoingData;
                    _listener.NetworkReceiveEvent += InitialData;
                    _hasInitialData = false;
                }
                _manager.Stop(false);
                NetClient.Stop();
                NetServer.Host();
            };
        }

        public static void Join(string ip) {
            Debug.Assert(!_manager.IsRunning);
            _manager.Start();
            var w = new NetDataWriter();
            _manager.Connect(ip, GameRoot.Settings.Port, w);
        }
        public static void Stop() {
            _manager.Stop(true);
            if (_hasInitialData) {
                _listener.NetworkReceiveEvent -= OngoingData;
                _listener.NetworkReceiveEvent += InitialData;
                _hasInitialData = false;
            }
        }

        public static NetWriter CreatePacket(Packets packetId) {
            var p = _packets[(int)packetId];
            p.Clear(_packetClearStartBits);
            return p;
        }
        public static void PollEvents() {
            if (!IsRunning)
                return;
            _manager.PollEvents();
            if (_hasInitialData && (_syncPlayersTimer += GameRoot.DeltaTime) >= SYNC_PLAYER_TIME) {
                _syncPlayersTimer -= SYNC_PLAYER_TIME;
                if (!GameRoot._isPlayer1) {
                    var w = CreatePacket(Packets.SyncPlayer);
                    w.Put(GameRoot.MousePosition.X);
                    w.Put(GameRoot.MousePosition.Y);
                    Send(w, 1, DeliveryMethod.Sequenced);
                }
            }
        }

        public static void Send(NetWriter writer, byte channel, DeliveryMethod deliveryMethod) => _manager.FirstPeer.Send(writer.Data, 0, writer.LengthBytes, channel, deliveryMethod);

        static void InitialData(NetPeer peer, NetDataReader reader, byte channel, DeliveryMethod deliveryMethod) {
            _r.ReadFrom(reader);
            GameRoot.Reset();
            _manager.FirstPeer.Send(new byte[0], DeliveryMethod.ReliableSequenced);
            _hasInitialData = true;
            _listener.NetworkReceiveEvent -= InitialData;
            _listener.NetworkReceiveEvent += OngoingData;
        }
        static void OngoingData(NetPeer peer, NetDataReader reader, byte channel, DeliveryMethod deliveryMethod) {
            _r.ReadFrom(reader);
            var p = (NetServer.Packets)_r.ReadInt(0, NetServer._maxPacketId);
            if (p == NetServer.Packets.SyncPlayer) {
                if (GameRoot._isPlayer1) {
                    GameRoot.MousePosition.X = _r.ReadFloat();
                    GameRoot.MousePosition.Y = _r.ReadFloat();
                }
            } else if (p == NetServer.Packets.MakePlay) {
                var x = _r.ReadInt(0, 8);
                var y = _r.ReadInt(0, 8);
                GameRoot.MakePlay(x, y);
            } else if (p == NetServer.Packets.ResetGame) {
                GameRoot.Reset();
            }
        }
    }
}

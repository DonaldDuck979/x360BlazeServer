using Org.BouncyCastle.Asn1.Cms;
using Servers.Blaze.Models;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Servers
{
    public class RelayServer
    {
        public readonly short Port;
        private readonly Game _game;
        private readonly ConcurrentDictionary<uint, IPEndPoint> _whitelistedUsers = new();
        private readonly ConcurrentDictionary<IPEndPoint, uint> _endpointToUser = new();

        private UdpClient? _udpClient;
        private Task? _serverTask;
        private CancellationTokenSource? _cts;

        // [relay-trace] diagnostics for the "join doesn't stick" investigation.
        private long _recv, _fwd, _dropNoReceiver, _dropNotWhitelisted, _reg, _anyRecv;
        private long _lastLogMs;

        public RelayServer(Game game, short port)
        {
            Port = port;
            _game = game;
        }

        public void Start()
        {
            _cts = new CancellationTokenSource();
            _udpClient = new UdpClient(Port);
            _serverTask = RunAsync(_cts.Token);
            ServerLogger.Log($"[relay-trace] g{_game.GameData.GameId} relay listening on UDP {Port}");
        }

        public async Task StopAsync()
        {
            if (_cts == null || _udpClient == null || _serverTask == null)
                return;
            try
            {
                await _cts.CancelAsync();
                await _serverTask;
            }
            catch { }
            finally
            {
                _udpClient.Close();
                _cts.Dispose();
            }
        }

        private async Task RunAsync(CancellationToken ct)
        {
            while (true)
            {
                UdpReceiveResult result;
                try { result = await _udpClient!.ReceiveAsync(ct); }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (SocketException) { continue; }

                _ = ProcessPacketAsync(_udpClient, result);
            }
        }

        private bool WhitelistFromRegistrationPacket(IPEndPoint ep, byte[] buf)
        {
            uint blazeId = BinaryPrimitives.ReadUInt32BigEndian(buf.AsSpan(8));

            if (_whitelistedUsers.TryAdd(blazeId, ep))
            {
                _endpointToUser.TryAdd(ep, blazeId);
                Interlocked.Increment(ref _reg);
                ServerLogger.Log($"[relay-trace] g{_game.GameData.GameId} REGISTERED blaze={blazeId} from {ep} (whitelisted now: {string.Join(",", _whitelistedUsers.Keys)})");
                return true;
            }

            return false;
        }

        public bool RemoveFromWhitelistedUser(uint blazeId)
        {
            if (_whitelistedUsers.TryRemove(blazeId, out var ep))
            {
                _endpointToUser.TryRemove(ep, out _);
                return true;
            }
            return false;
        }

        private async Task ProcessPacketAsync(UdpClient udp, UdpReceiveResult result)
        {
            try
            {
                var buf = result.Buffer;
                if (result.RemoteEndPoint is not IPEndPoint ep)
                    return;

                // [relay-trace] log EVERY datagram (any size) that reaches this
                // relay, first 12 only, so we can tell whether the game clients are
                // hitting the relay at all (vs. never connecting).
                long anyN = Interlocked.Increment(ref _anyRecv);
                if (anyN <= 12)
                    ServerLogger.Log($"[relay-trace] g{_game.GameData.GameId} PKT#{anyN} {buf.Length}B from {ep} head={BitConverter.ToString(buf, 0, Math.Min(buf.Length, 8))}");

                if (buf.Length > 7000 || buf.Length < 16)
                    return;

                Interlocked.Increment(ref _recv);

                bool isRegPacket = buf[0] == 1 && buf[1] == 0 && buf[2] == 0 && buf.Length == 20;
                ushort receiverIdOffset = 0x0C;

                if (_game.PlayersInQueue > 0)
                {
                    // Validate incoming packet is a registration packet (first packet game ever sends)
                    if (isRegPacket)
                    {
                        if (!_endpointToUser.ContainsKey(ep))
                            WhitelistFromRegistrationPacket(ep, buf);

                        receiverIdOffset = 0x10;
                    }
                }
                else if (isRegPacket && !_endpointToUser.ContainsKey(ep))
                {
                    // [relay-trace] a registration packet arrived while the queue is
                    // empty, so it is IGNORED (not whitelisted). If the host registers
                    // while alone, this is exactly why later packets to it get dropped.
                    ServerLogger.Log($"[relay-trace] g{_game.GameData.GameId} reg packet from {ep} IGNORED (PlayersInQueue=0)");
                }

                uint receiverId = BinaryPrimitives.ReadUInt32BigEndian(buf.AsSpan(receiverIdOffset));

                if (_whitelistedUsers.TryGetValue(receiverId, out var targetEp))
                {
                    Interlocked.Increment(ref _fwd);
                    await udp.SendAsync(buf, buf.Length, targetEp);
                }
                else
                {
                    Interlocked.Increment(ref _dropNoReceiver);
                }

                MaybeLogSummary();
            }
            catch (ObjectDisposedException) { }
            catch (SocketException) { }
        }

        private void MaybeLogSummary()
        {
            long now = Environment.TickCount64;
            long last = Interlocked.Read(ref _lastLogMs);
            if (now - last < 3000) return;
            if (Interlocked.CompareExchange(ref _lastLogMs, now, last) != last) return;
            ServerLogger.Log($"[relay-trace] g{_game.GameData.GameId} recv={Interlocked.Read(ref _recv)} fwd={Interlocked.Read(ref _fwd)} dropNoReceiver={Interlocked.Read(ref _dropNoReceiver)} reg={Interlocked.Read(ref _reg)} whitelisted=[{string.Join(",", _whitelistedUsers.Keys)}] queue={_game.PlayersInQueue}");
        }
    }
}
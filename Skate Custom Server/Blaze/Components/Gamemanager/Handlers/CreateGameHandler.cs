using Blaze.Components.Gamemanager.Commands;
using Blaze.Components.Gamemanager.Models;
using Servers;
using Blaze.GamemanagerComponent;
using Servers.Blaze.Models;
using Blaze.MessageLists;
using Blaze.Components.UserSessions.Models;

namespace Blaze.Components.Gamemanager.Handlers
{
    public class CreateGameHandler
    {
        private static string[] _validKeys = {
            "gameCodeVersion",
            "challenge_type",
            "challenge_key",
            "ping_site",
            "is_private",
            "max_players",
            "is_ranked",
            "overall_skill",
            "challenge_skill",
            "is_team_challenge",
            "team_id",
            "previous_game_id",
            "world_key",
            "skatepark_crc",
            "difficulty_mode",
            "allow_proposals",
            "is_coop_challenge",
            "is_free_skate",
            "dlc_mask",
            "skatepark_owner_id"
        };

        // Staggered free-skate create window: simultaneous quick-match players all
        // send createGame at once (both see zero games and would each make their
        // own). We give each free-skate creator a DIFFERENT wait so they serialize
        // — the 1st (index 0) proceeds immediately and creates the one shared game,
        // each later one waits index*step and, by the time it wakes, converges into
        // that shared game instead of making a second. The window resets after a
        // quiet gap so a genuinely-separate later session still creates its own.
        private static readonly object _fsCreateLock = new object();
        private static int _fsCreateIndex = 0;
        private static long _lastFsCreateMs = 0;
        private const int FsStaggerStepMs = 2500;
        private const int FsWindowResetMs = 8000;

        public static async Task HandleRequest(User creator, byte[] packetBytes)
        {
            // Make sure player doesn't have a lobby running already
            if (creator.CurrentGame != null)
            {
                await ServerUtils.SendError(creator, packetBytes, ServerUtils.ErrorCode.GAMEMANAGER_ERR_PERMISSION_DENIED);
                return;
            }

            // [free-skate converge] Force every free-skate player into ONE shared
            // game so the whole lobby plays together. The stock flow makes each
            // quick-match player create its OWN game (matchmaking race: both see
            // zero games, both create; challenge_key in the match filter keeps
            // them apart even sequentially). Here: if this createGame is free-skate
            // and a joinable free-skate game already exists, JOIN it instead of
            // making a second one. Also catches a late joiner whose matchmake
            // skipped the now-IN_GAME shared game and fell through to createGame.
            {
                var peek = BlazeMessage.CreateModelFromRequest<CreateGameRequest>(packetBytes);
                bool isFreeSkate = peek.GameAttributes.TryGetValue("is_free_skate", out var fsVal)
                    && fsVal != "0" && fsVal != "false" && !string.IsNullOrEmpty(fsVal);
                if (isFreeSkate)
                {
                    // Staggered wait: P1 index 0 -> no wait, P2 -> one
                    // step, P3 -> two steps, ... so concurrent creators wake in order
                    // and later ones find the shared game the earlier one made.
                    int myIndex;
                    lock (_fsCreateLock)
                    {
                        long now = Environment.TickCount64;
                        if (now - _lastFsCreateMs > FsWindowResetMs) _fsCreateIndex = 0;
                        myIndex = _fsCreateIndex++;
                        _lastFsCreateMs = now;
                    }
                    if (myIndex > 0)
                    {
                        ServerLogger.Log($"[gm-trace] createGame FREE-SKATE STAGGER: {creator.UserIdentification.Name} index={myIndex} waiting {myIndex * FsStaggerStepMs}ms before converge");
                        await Task.Delay(myIndex * FsStaggerStepMs);
                    }

                    Game shared = ServerGlobals.Games.Values.FirstOrDefault(g =>
                        g.GameData.GameAttributes.TryGetValue("is_free_skate", out var v) && v == fsVal
                        && g.Players.Count < 6
                        && (g.GameData.GameState == (int)GameState.INITIALIZING
                            || g.GameData.GameState == (int)GameState.PRE_GAME
                            || g.GameData.GameState == (int)GameState.IN_GAME));
                    if (shared != null)
                    {
                        ServerLogger.Log($"[gm-trace] createGame FREE-SKATE CONVERGE: {creator.UserIdentification.Name} -> join existing g{shared.GameData.GameId} (state={(GameState)shared.GameData.GameState}, players={shared.Players.Count}) instead of new game");
                        var joinResp = BlazeMessage.CreateResponseFromModel(packetBytes, new JoinGameResponse { GameId = shared.GameData.GameId });
                        await creator.Stream.WriteAsync(joinResp.Serialize());
                        creator.IsMatchmaking = true;
                        await GameManagerUtils.UserJoinGame(creator, shared, false);
                        return;
                    }
                }
            }

            uint gameId = (uint)ServerGlobals.GetNextGameId();

            var request = BlazeMessage.CreateModelFromRequest<CreateGameRequest>(packetBytes);

            var response = BlazeMessage.CreateResponseFromModel(
                packetBytes,
                new JoinGameResponse
                {
                    GameId = gameId
                });

            await creator.Stream.WriteAsync(response.Serialize());

            var filteredAttributes = new Dictionary<string, string>();
            if (request.GameAttributes.Count <= _validKeys.Length)
            {
                foreach (var kvp in request.GameAttributes)
                {
                    if (_validKeys.Contains(kvp.Key) && kvp.Value.Length <= 30)
                    {
                        filteredAttributes[kvp.Key] = kvp.Value;
                    }
                }
            }

            var platformHostInfo = new HostInfo
            {
                PlayerId = creator.Session.BlazeId,
                SlotId = 1
            };

            var topologyHostInfo = new HostInfo
            {
                PlayerId = 123,
                SlotId = 0
            };

            var game = new Game();

            // Reserve and start Dirtycast server for lobby
            ushort relayServerPort = Convert.ToUInt16(17000 + gameId);
            int attempts = 0;
            while (ServerGlobals.LobbyRelayServers.Any(x => x.Value.Port == relayServerPort))
            {
                gameId = ServerGlobals.GetNextGameId();
                relayServerPort = Convert.ToUInt16(17000 + gameId);
                if (++attempts > 500)
                {
                    await ServerUtils.SendError(creator, packetBytes, ServerUtils.ErrorCode.GAMEMANAGER_ERR_PERMISSION_DENIED);
                    return;
                }
            }
            NetworkAddress relayServer = ServerUtils.ReserveLobbyRelayServer(game, relayServerPort);
            
            var replicatedGameData = new ReplicatedGameData
            {
                AdminPlayerList = new List<uint> { 123, creator.Session.BlazeId },
                GameAttributes = filteredAttributes,
                SlotCapacities = new List<ushort> { 6, 0 },
                GameId = gameId,
                GameName = creator.UserIdentification.Name,
                GameSettings = request.GameSettings,
                GameState = (int)GameState.INITIALIZING,
                HostConnections = new List<NetworkAddress> { relayServer },
                TopologyHostSessionId = Convert.ToUInt32(creator.Session.UserId),
                MaxPlayerCapacities = 6,
                NetworkQosData = creator.ExtendedData.QosData,
                NetworkTopology = (int)GameNetworkTopology.PEER_TO_PEER_DIRTYCAST_FAILOVER,
                PersistedGameId = request.PersistedGameId,
                PersistedGameIdSecret = request.PersistedGameIdSecret,
                TopologyHost = topologyHostInfo,
                PlatformHost = platformHostInfo,
                QueueCapacity = request.QueueCapacity,
                VoipTopology = (int)VoipTopology.VOIP_PEER_TO_PEER,
                GameProtocolVersionString = request.GameProtocolVersionString
            };

            game.GameData = replicatedGameData;

            ReplicatedGamePlayer playerData = GameManagerUtils.CreateReplicatedGamePlayer(creator, game, true);
            var player = new Player
            {
                PlayerData = playerData,
                UserData = creator
            };

            game.Players.Add(player);
            game.HostId = creator.UserIdentification.BlazeId;
            ServerGlobals.Games[gameId] = game;

            await ServerUtils.SendNotificationToUser(
                creator,
                new NotifyJoinGame
                {
                    Error = 0,
                    GameData = replicatedGameData,
                    Players = new List<ReplicatedGamePlayer> { playerData },
                    MatchmakingId = 123
                },
                BlazeComponent.Gamemanager,
                (ushort)GameManagerNotifications.NotifyJoinGame);

            creator.CurrentGame = game;
            creator.GamePlayer = player;
        }
    }
}
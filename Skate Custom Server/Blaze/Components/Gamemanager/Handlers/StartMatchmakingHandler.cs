using Blaze.Components.Gamemanager.Commands;
using Servers;
using Blaze.Components.Gamemanager.Models;
using Blaze.GamemanagerComponent;
using Servers.Blaze.Models;
using Blaze.MessageLists;

namespace Blaze.Components.Gamemanager.Handlers
{
    public class StartMatchmakingHandler
    {
        public static string[] FilteredAttributes = {
            "is_private",
            "is_ranked",
            "challenge_key",
            "challenge_type",
            "is_team_challenge",
            "is_free_skate",
            "is_coop_challenge",
            "gameCodeVersion"
        };

        public static async Task HandleRequest(User matchmaker, byte[] packetBytes)
        {
            // Make sure player doesn't have a lobby running already
            if (matchmaker.CurrentGame != null)
            {
                await ServerUtils.SendError(matchmaker, packetBytes, ServerUtils.ErrorCode.GAMEMANAGER_ERR_PERMISSION_DENIED);
                return;
            }

            BlazeMessage response = BlazeMessage.CreateResponseFromModel(
                packetBytes,
                new StartMatchmakingResponse
                {
                    MatchmakingId = 123
                });

            await matchmaker.Stream.WriteAsync(response.Serialize());

            var request = BlazeMessage.CreateModelFromRequest<StartMatchmakingRequest>(packetBytes);

            var wantedAttributes = request.MatchmakingAttributes;

            // [gm-trace] what this matchmake wants + how many games to scan.
            {
                string fs = wantedAttributes.TryGetValue("is_free_skate", out var v1) ? v1 : "?";
                string pv = wantedAttributes.TryGetValue("is_private", out var v2) ? v2 : "?";
                bool hasCk = wantedAttributes.ContainsKey("challenge_key");
                ServerLogger.Log($"[gm-trace] startMatchmaking user={matchmaker.UserIdentification.Name} is_free_skate={fs} is_private={pv} hasChallengeKey={hasCk} scanGames={ServerGlobals.Games.Count}");
            }

            foreach (var kv in ServerGlobals.Games)
            {
                Game game = kv.Value;

                if (game.GameData.GameState != (int)GameState.PRE_GAME)
                {
                    ServerLogger.Log($"[gm-trace]   skip g{game.GameData.GameId}: state={(GameState)game.GameData.GameState} (not PRE_GAME)");
                    continue;
                }

                bool hasSpace;
                lock (game.Lock)
                {
                    hasSpace = game.Players.Count < 6;
                }

                if (hasSpace)
                {
                    var gameAttributes = game.GameData.GameAttributes;

                    bool foundGame = true;
                    string mismatch = "";
                    foreach (string attribute in FilteredAttributes)
                    {
                        if (wantedAttributes.ContainsKey(attribute) && gameAttributes.ContainsKey(attribute))
                        {
                            if (wantedAttributes[attribute] != gameAttributes[attribute])
                            {
                                foundGame = false;
                                mismatch += $"{attribute}(want={wantedAttributes[attribute]},has={gameAttributes[attribute]}) ";
                            }
                        }
                    }

                    if (!foundGame)
                    {
                        ServerLogger.Log($"[gm-trace]   skip g{game.GameData.GameId}: attr mismatch {mismatch}");
                        continue;
                    }

                    ServerLogger.Log($"[gm-trace]   -> JOINED g{game.GameData.GameId} (PRE_GAME, players={game.Players.Count})");
                    matchmaker.IsMatchmaking = true;
                    await GameManagerUtils.UserJoinGame(matchmaker, game, true);
                    return;
                }
                else
                {
                    ServerLogger.Log($"[gm-trace]   skip g{game.GameData.GameId}: full");
                }
            }

            // In cases where matchmaking via Quick Match, a challenge_key won't be provided and we can't make a new lobby due to that
            bool fromQuickplay = !wantedAttributes.ContainsKey("challenge_key");
            ServerLogger.Log($"[gm-trace] startMatchmaking user={matchmaker.UserIdentification.Name} -> NO joinable PRE_GAME game; fromQuickplay={fromQuickplay} -> client makes its OWN game (SEPARATE session)");

            await ServerUtils.SendNotificationToUser(
                matchmaker,
                new NotifyMatchmakingFinished
                {
                    Fit = 100,
                    MaxFit = 100,
                    GameId = 0, // Sending 0 makes the game redirect to createGame command for making lobby
                    MatchmakingResult = fromQuickplay ? (int)MatchmakingResult.SESSION_ERROR_GAME_SETUP_FAILED : (int)MatchmakingResult.SUCCESS_JOINED_EXISTING_GAME,
                    MatchmakingSessionId = 123
                },
                BlazeComponent.Gamemanager,
                (ushort)GameManagerNotifications.NotifyMatchmakingFinished);
        }
    }
}
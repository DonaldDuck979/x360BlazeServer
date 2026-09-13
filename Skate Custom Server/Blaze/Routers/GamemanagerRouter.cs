using Blaze.Components.Gamemanager.Handlers;
using Servers;
using Servers.Blaze.Models;
using Blaze.MessageLists;

namespace Blaze
{
    public static class GamemanagerComponentRouter
    {
        public static async Task Handle(User user, byte[] receivedPacket)
        {
            GameManagerMessage gamemanagerMessage = (GameManagerMessage)TdfUtils.GetCommandFromPacket(receivedPacket);

            // [gm-trace] full command sequence, to diagnose why two players never
            // land in one session. Shows who sent what, their current game + state,
            // and how many games exist server-wide.
            string curGame = user.CurrentGame == null
                ? "none"
                : $"g{user.CurrentGame.GameData.GameId}/{(Blaze.Components.Gamemanager.Models.GameState)user.CurrentGame.GameData.GameState}";
            ServerLogger.Log($"[gm-trace] cmd={gamemanagerMessage} user={user.UserIdentification.Name}(blaze={user.Session.BlazeId}) curGame={curGame} totalGames={ServerGlobals.Games.Count}");

            switch (gamemanagerMessage)
            {
                case GameManagerMessage.createGame:
                    await CreateGameHandler.HandleRequest(user, receivedPacket);
                    break;
                case GameManagerMessage.finalizeGameCreation:
                    await FinalizeGameCreationHandler.HandleRequest(user, receivedPacket);
                    break;
                case GameManagerMessage.setGameAttributes:
                    await SetGameAttributesHandler.HandleRequest(user, receivedPacket);
                    break;
                case GameManagerMessage.setGameSettings:
                    await SetGameSettingsHandler.HandleRequest(user, receivedPacket);
                    break;
                case GameManagerMessage.advanceGameState:
                    await AdvanceGameStateHandler.HandleRequest(user, receivedPacket);
                    break;
                case GameManagerMessage.updateGameSession:
                    await ServerUtils.SendEmptyResponse(user, receivedPacket);
                    break;
                case GameManagerMessage.setPlayerCapacity:
                    await SetPlayerCapacityHandler.HandleRequest(user, receivedPacket);
                    break;
                case GameManagerMessage.removePlayer:
                    await RemovePlayerHandler.HandleRequest(user, receivedPacket);
                    break;
                case GameManagerMessage.startMatchmaking:
                    await StartMatchmakingHandler.HandleRequest(user, receivedPacket);
                    break;
                case GameManagerMessage.updateMeshConnection:
                    await UpdateMeshConnectionHandler.HandleRequest(user, receivedPacket);
                    break;
                case GameManagerMessage.migrateAdminPlayer:
                    await MigrateAdminPlayerHandler.HandleRequest(user, receivedPacket);
                    break;
                case GameManagerMessage.joinGame:
                    await JoinGameHandler.HandleRequest(user, receivedPacket);
                    break;
                case GameManagerMessage.replayGame:
                    await ReplayGameHandler.HandleRequest(user, receivedPacket);
                    break;
                case GameManagerMessage.cancelMatchmaking:
                    await CancelMatchmakingHandler.HandleRequest(user, receivedPacket);
                    break;
                default:
                    await ServerUtils.SendError(user, receivedPacket, ServerUtils.ErrorCode.ERR_COMMAND_NOT_FOUND);
                    break;
            }
        }
    }
}
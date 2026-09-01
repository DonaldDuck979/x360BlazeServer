using Blaze.Components.Authentication.Commands;
using Blaze.Components.Authentication.Models;
using Blaze.Components.UserSessions.Models;
using Microsoft.EntityFrameworkCore;
using Servers;
using Servers.Blaze.Models;
using Servers.Database;
using Servers.Models;

namespace Blaze.Components.Authentication
{
    // [skate3-360] Xbox 360 login: the 360 game sends gamertag + XUID (no PSN
    // ticket). Mirrors Ps3LoginHandler but skips ticket validation and keys the
    // account off the XUID.
    public class XboxLoginHandler
    {
        public static async Task HandleRequest(User user, byte[] packetBytes)
        {
            var loginRequest = BlazeMessage.CreateModelFromRequest<XboxLoginRequest>(packetBytes);

            ulong xuid = loginRequest.XUID;
            string gamertag = string.IsNullOrEmpty(loginRequest.Gamertag) ? "Player" : loginRequest.Gamertag;

            if (Ps3LoginHandler.UserBanned(gamertag))
                return;

            user.Platform = UserPlatform.Xbox360;

            // Allow single session per XUID
            var existing = ServerGlobals.Users.Values.FirstOrDefault(u =>
                u.Session.PersonaDetails.ExternalRef == xuid && u.Platform == user.Platform);
            if (existing != null)
            {
                try { existing.Stream.Close(); } catch { }
            }

            ServerLogger.LogSignIn(gamertag);

            await using var db = new AppDbContext();
            db.Database.Migrate();

            var userData = db.Users.FirstOrDefault(u => u.PsnId == xuid && u.Platform == user.Platform);
            if (userData == null)
            {
                userData = new UserDbData
                {
                    Platform = user.Platform,
                    PsnId = xuid,          // store the XUID here
                    DisplayName = gamertag
                };
                db.Users.Add(userData);
                await db.SaveChangesAsync();
            }

            SessionDetails sessionDetails = AuthUtils.CreateNewSessionDetails(gamertag, xuid, userData.BlazeId);

            BlazeMessage response = BlazeMessage.CreateResponseFromModel(
                packetBytes,
                new Ps3LoginResponse
                {
                    IsUnderage = false,
                    IsSpammable = false,
                    SessionDetails = sessionDetails
                });

            await user.Stream.WriteAsync(response.Serialize());

            // Store user session details for later use
            user.Session = sessionDetails;
            user.ExtendedData = new UserSessionExtendedData
            {
                DataMap = new Dictionary<uint, uint> { { 458823, 0 } }
            };

            user.UserIdentification = new UserIdentification
            {
                AccountId = user.Session.UserId,
                AccountLocale = 1701729619, // enUS
                BlazeId = user.Session.BlazeId,
                IsOnline = true,
                Name = user.Session.PersonaDetails.DisplayName,
                PersonaId = user.Session.PersonaDetails.PersonaId,
                ExternalId = sessionDetails.PersonaDetails.ExternalRef
            };

            user.IsAuthenticated = true;
            ServerGlobals.Users[sessionDetails.BlazeId] = user;
        }
    }
}

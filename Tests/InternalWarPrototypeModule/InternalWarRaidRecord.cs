using System;
using System.Globalization;

namespace AgesOfCalradiaInternalWarsTest
{
    internal sealed class InternalWarRaidRecord
    {
        internal string Id, ConflictId, SettlementId, LeaderPartyId, DefenderClanId;
        internal int StartDay;
        internal bool EventObserved, StopRequested, Closed;
        internal string Serialize()
        {
            return string.Join("|", new[] { "v1", Uri.EscapeDataString(Id), Uri.EscapeDataString(ConflictId),
                Uri.EscapeDataString(SettlementId), Uri.EscapeDataString(LeaderPartyId), Uri.EscapeDataString(DefenderClanId),
                StartDay.ToString(CultureInfo.InvariantCulture), EventObserved ? "1" : "0", StopRequested ? "1" : "0", Closed ? "1" : "0" });
        }
        internal static InternalWarRaidRecord Deserialize(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload)) return null;
            string[] parts = payload.Split('|');
            if (parts.Length != 10 || parts[0] != "v1") return null;
            for (int index = 1; index <= 5; index++)
                if (string.IsNullOrWhiteSpace(Uri.UnescapeDataString(parts[index]))) return null;
            for (int index = 7; index <= 9; index++) if (parts[index] != "0" && parts[index] != "1") return null;
            int day;
            if (!int.TryParse(parts[6], out day) || day < 0) return null;
            return new InternalWarRaidRecord { Id = Uri.UnescapeDataString(parts[1]), ConflictId = Uri.UnescapeDataString(parts[2]),
                SettlementId = Uri.UnescapeDataString(parts[3]), LeaderPartyId = Uri.UnescapeDataString(parts[4]),
                DefenderClanId = Uri.UnescapeDataString(parts[5]), StartDay = day,
                EventObserved = parts[7] == "1", StopRequested = parts[8] == "1", Closed = parts[9] == "1" };
        }
    }
}

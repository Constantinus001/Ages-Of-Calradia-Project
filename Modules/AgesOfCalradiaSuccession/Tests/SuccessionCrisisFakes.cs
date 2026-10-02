using System;
using TaleWorlds.CampaignSystem;
namespace TaleWorlds.CampaignSystem.Settlements
{
    public sealed class Settlement { }
    public sealed class Town { public Settlement Settlement = new Settlement(); }
}
namespace TaleWorlds.Localization
{
    public sealed class TextObject { public TextObject(string value) { } }
}
namespace TaleWorlds.Core
{
    public sealed class Banner { public Banner() { } public Banner(Banner source, uint a, uint b) { } }
}
namespace TaleWorlds.CampaignSystem.Actions
{
    public static class ChangeKingdomAction
    {
        public static Action AfterApply;
        public static int ApplyCount;
        public static void ApplyByJoinToKingdom(Clan clan, Kingdom realm, CampaignTime time, bool notify)
        {
            ApplyCount++;
            if (clan.Kingdom != null) clan.Kingdom.Clans.Remove(clan);
            clan.Kingdom = realm; realm.Clans.Add(clan);
            if (AfterApply != null) AfterApply();
        }
        public static void ApplyByCreateKingdom(Clan clan, Kingdom realm, bool notify)
        { ApplyByJoinToKingdom(clan, realm, CampaignTime.Now, notify); ChangeRulingClanAction.Apply(realm, clan); }
        public static void ApplyByJoinToKingdomByDefection(Clan clan, Kingdom original, Kingdom realm, CampaignTime time, bool notify)
        { ApplyByJoinToKingdom(clan, realm, time, notify); }
    }
    public static class DeclareWarAction
    {
        public static void ApplyByClaimOnThrone(Kingdom a, Kingdom b) { FactionManager.Wars.Add(a.StringId + ":" + b.StringId); }
    }
    public static class MakePeaceAction
    {
        public static void Apply(Kingdom a, Kingdom b) { FactionManager.Wars.Remove(a.StringId + ":" + b.StringId); FactionManager.Wars.Remove(b.StringId + ":" + a.StringId); }
    }
    public static class DestroyKingdomAction { public static void Apply(Kingdom realm) { realm.IsEliminated = true; } }
}
namespace AgesOfCalradiaSuccession
{
    internal static class SuccessionCampaignMapBorderBridge
    { internal static bool RequestRefresh(out string result) { result = "test"; return true; } }
}

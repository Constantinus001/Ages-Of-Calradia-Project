using System;
using AgesOfCalradiaSuccession;
using TaleWorlds.CampaignSystem;

internal static class SuccessionResolverVerifier
{
    private static Hero Add(Clan house, string id, float age, bool female = false, bool alive = true, Hero father = null, Hero mother = null)
    {
        var hero = new Hero { StringId = id, Age = age, IsFemale = female, IsAlive = alive, Clan = house, Father = father, Mother = mother };
        house.Heroes.Add(hero);
        if (father != null) father.Children.Add(hero);
        if (mother != null) mother.Children.Add(hero);
        return hero;
    }
    internal static void Run(Action<bool, string> check)
    {
        var realm = new Kingdom { StringId = "inheritance" };
        var house = new Clan { Kingdom = realm };
        realm.Clans.Add(house);
        Hero grandfather = Add(house, "grandfather", 90, alive: false);
        Hero monarch = Add(house, "monarch", 60, alive: false, father: grandfather);
        Hero sister = Add(house, "sister", 55, female: true, alive: false, father: grandfather);
        Hero nephew = Add(house, "sisters-son", 25, mother: sister);
        Hero uncle = Add(house, "uncle", 70);
        check(SuccessionResolver.FindLawfulDynasticHeir(house, monarch, SuccessionLaw.AgnaticPrimogeniture) == uncle,
            "agnatic collateral order must not elevate a sisters branch over house fallback");
        check(SuccessionResolver.FindLawfulDynasticHeir(house, monarch, SuccessionLaw.AbsolutePrimogeniture) == nephew,
            "absolute succession preserves sisters branch");

        Hero elder = Add(house, "elder", 35, father: monarch);
        Hero younger = Add(house, "younger", 30, father: monarch);
        Hero daughter = Add(house, "daughter", 40, female: true, father: monarch);
        check(SuccessionResolver.FindLawfulDynasticHeir(house, monarch, SuccessionLaw.AbsolutePrimogeniture) == daughter, "absolute order prefers eldest child");
        check(SuccessionResolver.FindLawfulDynasticHeir(house, monarch, SuccessionLaw.MalePreferencePrimogeniture) == elder, "male preference preserves eldest son order");
        elder.IsAlive = false;
        Hero grandchild = Add(house, "grandchild", 10, father: elder);
        check(SuccessionResolver.FindLawfulDynasticHeir(house, monarch, SuccessionLaw.MalePreferencePrimogeniture) == grandchild, "child of deceased elder son precedes younger son");
        check(SuccessionResolver.FindLawfulDynasticHeir(house, monarch, SuccessionLaw.HouseSeniority) == uncle, "house seniority chooses oldest living member");
        grandchild.IsActive = false;
        check(SuccessionResolver.FindLawfulDynasticHeir(house, monarch, SuccessionLaw.MalePreferencePrimogeniture) == younger, "inactive dynast does not block next eligible branch");

        var mercenary = new Clan { Kingdom = realm, IsClanTypeMercenary = true, Tier = 6, Renown = 999999 };
        mercenary.Leader = Add(mercenary, "mercenary", 40);
        realm.Clans.Add(mercenary);
        check(SuccessionResolver.RankEmergency(realm, house).Count == 0, "emergency succession cannot crown a mercenary clan");
        house.Leader = younger;
        var other = new Clan { Kingdom = realm, Tier = 6, Renown = 999999 };
        other.Leader = Add(other, "outsider", 50);
        realm.Clans.Add(other);
        check(SuccessionResolver.RankEmergency(realm, house)[0].Hero == younger, "emergency ruling house priority cannot be overridden by renown");
        elder.IsAlive = true;
        elder.Clan = new Clan { Kingdom = new Kingdom { StringId = "foreign" } };
        check(SuccessionResolver.FindLawfulDynasticHeir(house, monarch, SuccessionLaw.MalePreferencePrimogeniture) == younger,
            "foreign elder heir does not hide the next eligible dynastic heir");
        grandchild.IsActive = true;
        check(SuccessionResolver.FindLawfulDynasticHeir(house, monarch, SuccessionLaw.MalePreferencePrimogeniture) == grandchild,
            "resident descendant of foreign elder heir preserves branch priority");
        check(SuccessionResolver.RankEmergency(realm, house)[0].Score > SuccessionResolver.RankEmergency(realm, house)[1].Score,
            "emergency scores agree with guaranteed house priority");
        var residentHouse = new Clan { Kingdom = realm };
        grandchild.Clan = residentHouse;
        house.Kingdom = elder.Clan.Kingdom;
        check(SuccessionResolver.FindLawfulDynasticHeir(house, monarch, SuccessionLaw.MalePreferencePrimogeniture, realm) == grandchild,
            "departed dynasty cannot redirect the target kingdom for inheritance");
    }
}

using System;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace AgesOfCalradiaLogistics
{
    internal static class LogisticsPartySpeedMath
    {
        internal const float BaseTravelSpeed = 4f;
        internal const float MaximumMapSpeed = 8f;
        internal const float MinimumMapSpeed = 0.10f;

        internal static float CalibrateLandSpeed(float nativeFinalSpeed, float nativeBaseSpeed)
        {
            if (nativeBaseSpeed <= 0.001f) return Math.Max(MinimumMapSpeed, Math.Min(MaximumMapSpeed, nativeFinalSpeed));
            float calibrated = nativeFinalSpeed / nativeBaseSpeed * BaseTravelSpeed;
            return Math.Max(MinimumMapSpeed, Math.Min(MaximumMapSpeed, calibrated));
        }
    }

    /// <summary>
    /// Establishes a neutral campaign-map travel speed of 4 and a hard maximum
    /// of 8. Native party composition, terrain, weather, cargo, herd, army,
    /// prisoner, wound, and skill modifiers are calculated first, so penalties
    /// continue to reduce the calibrated result instead of being overwritten.
    /// </summary>
    internal sealed class LogisticsPartySpeedModel : PartySpeedModel
    {
        private static readonly TextObject CalibrationText = new TextObject(
            "{=AOCLogisticsTravelCalibration}Logistics travel pace (base 4; maximum 8)");
        private static readonly TextObject SupplyPressureText = new TextObject(
            "{=AOCLogisticsSupplyPressure}Operational supply condition");
        private readonly PartySpeedModel _calculationModel;

        internal LogisticsPartySpeedModel(PartySpeedModel installedModel)
        {
            if (installedModel == null) throw new ArgumentNullException(nameof(installedModel));
            _calculationModel = UnwrapProtectedCalendarModel(installedModel);
        }

        public override float BaseSpeed { get { return LogisticsPartySpeedMath.BaseTravelSpeed; } }

        public override float MinimumSpeed
        {
            get
            {
                float nativeBase = Math.Max(0.001f, _calculationModel.BaseSpeed);
                return Math.Max(LogisticsPartySpeedMath.MinimumMapSpeed,
                    Math.Min(LogisticsPartySpeedMath.BaseTravelSpeed,
                        _calculationModel.MinimumSpeed / nativeBase * LogisticsPartySpeedMath.BaseTravelSpeed));
            }
        }

        public override ExplainedNumber CalculateBaseSpeed(
            MobileParty party,
            bool includeDescriptions = false,
            int additionalTroopOnFootCount = 0,
            int additionalTroopOnHorseCount = 0)
        {
            return _calculationModel.CalculateBaseSpeed(
                party,
                includeDescriptions,
                additionalTroopOnFootCount,
                additionalTroopOnHorseCount);
        }

        public override ExplainedNumber CalculateFinalSpeed(MobileParty party, ExplainedNumber finalSpeed)
        {
            ExplainedNumber nativeResult = _calculationModel.CalculateFinalSpeed(party, finalSpeed);
            float calibrated = party != null && party.IsCurrentlyAtSea
                ? Math.Min(LogisticsPartySpeedMath.MaximumMapSpeed, nativeResult.ResultNumber)
                : LogisticsPartySpeedMath.CalibrateLandSpeed(nativeResult.ResultNumber, _calculationModel.BaseSpeed);

            float supplyFactor = party != null && !party.IsCurrentlyAtSea
                && LogisticsReserveBehavior.Active != null
                    ? LogisticsReserveBehavior.Active.GetSpeedFactor(party)
                    : 1f;
            float suppliedSpeed = calibrated * supplyFactor;

            // Preserve native tooltip lines and add only the calibration delta.
            nativeResult.Add(calibrated - nativeResult.ResultNumber, CalibrationText);
            if (supplyFactor < 1f)
            {
                nativeResult.Add(suppliedSpeed - calibrated, SupplyPressureText);
            }
            nativeResult.LimitMin(LogisticsPartySpeedMath.MinimumMapSpeed);
            nativeResult.LimitMax(LogisticsPartySpeedMath.MaximumMapSpeed);
            return nativeResult;
        }

        private static PartySpeedModel UnwrapProtectedCalendarModel(PartySpeedModel installedModel)
        {
            Type type = installedModel.GetType();
            if (!string.Equals(type.FullName, "TwelveMonthCalendar.CalendarPartySpeedModel", StringComparison.Ordinal))
            {
                return installedModel;
            }

            FieldInfo nativeField = type.GetField("_native", BindingFlags.Instance | BindingFlags.NonPublic);
            PartySpeedModel nativeModel = nativeField == null ? null : nativeField.GetValue(installedModel) as PartySpeedModel;
            return nativeModel ?? installedModel;
        }
    }
}

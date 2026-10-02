using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace AgesOfCalradia.CampaignSystems
{
    // Independent immutable campaign-start settings. Never extend procurement's
    // strict XML schema or reinterpret saved balances/acquisition costs.
    internal sealed class NavalCashoutSettings
    {
        internal readonly bool Enabled;
        internal readonly float HullBasis;
        internal readonly string Revision;
        internal NavalCashoutSettings(bool enabled, float hullBasis)
        {
            if (!NavalCashoutPolicy.Finite(hullBasis) || hullBasis < 0.01f || hullBasis > 1f)
                throw new ArgumentOutOfRangeException("hullBasis", "HullBasis must be between 0.01 and 1.");
            Enabled = enabled; HullBasis = hullBasis;
            using (var sha = SHA256.Create())
                Revision = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(
                    "1|" + enabled + "|" + hullBasis.ToString("R", CultureInfo.InvariantCulture)))).Replace("-", "");
        }
        internal static NavalCashoutSettings Disabled { get { return new NavalCashoutSettings(false, 1f); } }
        internal static NavalCashoutSettings Load(string path)
        {
            if (!File.Exists(path)) return Disabled;
            using (var reader = XmlReader.Create(path, new XmlReaderSettings {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 8192 }))
                return Parse(XDocument.Load(reader));
        }
        internal static NavalCashoutSettings Parse(XDocument doc)
        {
            var root = doc.Root;
            if (root == null || root.Name != "NavalEconomy" || root.HasElements
                || root.Attributes().Any(a => a.Name != "SchemaVersion" && a.Name != "Enabled" && a.Name != "HullBasis")
                || (string)root.Attribute("SchemaVersion") != "1" || !string.IsNullOrWhiteSpace(root.Value))
                throw new FormatException("Expected NavalEconomy schema 1 with only Enabled and HullBasis attributes.");
            bool enabled; float factor;
            if (!bool.TryParse((string)root.Attribute("Enabled"), out enabled)
                || !float.TryParse((string)root.Attribute("HullBasis"), NumberStyles.Float, CultureInfo.InvariantCulture, out factor))
                throw new FormatException("NavalEconomy requires valid Enabled and HullBasis.");
            return new NavalCashoutSettings(enabled, factor);
        }
    }

    internal static class NavalCashoutPolicy
    {
        internal static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }

        // Normalize ONLY the inconsistent hull component. Native upgrade-slot
        // maxima, rounding, owner perks and existing AI upgrade discounts remain
        // native. Repairs are applied later by native trade code exactly once.
        internal static bool TryBase(float nativeBase, float hull, bool alreadyDiscounted,
            float factor, out float effective)
        {
            effective = nativeBase;
            if (!Finite(nativeBase) || !Finite(hull) || !Finite(factor) || hull < 0
                || factor < 0.01f || factor > 1f || nativeBase < 0) return false;
            if (alreadyDiscounted) return true;
            if (nativeBase < hull) return false;
            effective = (nativeBase - hull) + hull * factor;
            return Finite(effective) && effective >= 0 && effective <= nativeBase;
        }

        internal static bool TryShare(int priorGold, int contribution, int totalContribution,
            long pool, out int total)
        {
            total = priorGold;
            if (priorGold < 0 || contribution < 0 || totalContribution <= 0
                || contribution > totalContribution || pool < 0 || pool > int.MaxValue) return false;
            // Same float division/multiplication/floor ordering as installed 1.4.8.
            double amount = Math.Floor(((float)contribution / totalContribution) * (float)pool);
            if (amount < 0 || amount > int.MaxValue - (long)priorGold) return false;
            total = priorGold + (int)amount;
            return true;
        }
    }
}

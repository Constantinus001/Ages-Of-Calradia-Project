using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem.Naval;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace AgesOfCalradiaReligions.Shipwright
{
    /// <summary>
    /// Transient campaign-UI wizard. It configures a new vanilla Ship instance
    /// and hands it to the native Trade-mode port screen; it owns no saved data,
    /// economy mutation, or fleet-transfer behavior.
    /// </summary>
    internal sealed class DromonBuilderSession
    {
        private static readonly string[] OrderedSlotTags =
        {
            "fore", "aft", "bow", "hull", "side", "deck", "sail", "roof"
        };

        private readonly Settlement _port;
        private readonly MobileParty _mainParty;
        private readonly Ship _preview;
        private readonly List<ShipUpgradePiece> _availablePieces;
        private int _slotIndex;

        private DromonBuilderSession(
            Settlement port,
            MobileParty mainParty,
            ShipHull hull,
            List<ShipUpgradePiece> availablePieces)
        {
            _port = port;
            _mainParty = mainParty;
            _preview = new Ship(hull);
            _availablePieces = availablePieces;
        }

        internal static void Start(Settlement port, MobileParty mainParty, ShipHull hull)
        {
            List<ShipUpgradePiece> availablePieces;
            string failure;
            if (!NavalUpgradeCatalog.TryGetAvailablePieces(port == null ? null : port.Town, out availablePieces, out failure))
            {
                ShipwrightDiagnostics.Warning("Dromon builder unavailable: " + failure + ".");
                InformationManager.DisplayMessage(new InformationMessage("The shipwright could not read this port's vanilla part catalog: " + failure + "."));
                return;
            }

            DromonBuilderSession session = new DromonBuilderSession(port, mainParty, hull, availablePieces);
            ShipwrightDiagnostics.Info(string.Format(
                "Starting eight-slot Dromon builder at {0} with {1} port-available vanilla pieces.",
                port.StringId,
                availablePieces.Count));
            session.ShowNextSlot();
        }

        private void ShowNextSlot()
        {
            while (_slotIndex < OrderedSlotTags.Length && !_preview.ShipHull.AvailableSlots.ContainsKey(OrderedSlotTags[_slotIndex]))
            {
                _slotIndex++;
            }

            if (_slotIndex >= OrderedSlotTags.Length)
            {
                ShowNamingStep();
                return;
            }

            string slotTag = OrderedSlotTags[_slotIndex];
            ShipSlot slot = _preview.ShipHull.AvailableSlots[slotTag];
            List<InquiryElement> choices = new List<InquiryElement>
            {
                new InquiryElement(null, "Leave " + GetSlotName(slot, slotTag) + " empty", null, true, "No part will be fitted in this slot.")
            };

            foreach (ShipUpgradePiece piece in _availablePieces
                .Where(candidate => candidate.DoesPieceMatchSlot(slot))
                .OrderBy(candidate => candidate.RequiredPortLevel)
                .ThenBy(candidate => candidate.Name.ToString(), StringComparer.CurrentCulture))
            {
                choices.Add(new InquiryElement(
                    piece,
                    piece.Name.ToString(),
                    null,
                    true,
                    BuildPieceHint(piece)));
            }

            int displayStep = _slotIndex + 1;
            MBInformationManager.ShowMultiSelectionInquiry(
                new MultiSelectionInquiryData(
                    string.Format("Dromon Builder — Step {0} of {1}: {2}", displayStep, OrderedSlotTags.Length + 1, GetSlotName(slot, slotTag)),
                    "Choose one vanilla War Sails part. The final 3D port screen will calculate the complete ship price and let you confirm or cancel.",
                    choices,
                    true,
                    1,
                    1,
                    "Fit part",
                    "Cancel commission",
                    ApplySlotChoice,
                    delegate { Cancel(); }),
                true);
        }

        private void ApplySlotChoice(List<InquiryElement> selected)
        {
            if (selected == null || selected.Count != 1)
            {
                Cancel();
                return;
            }

            string slotTag = OrderedSlotTags[_slotIndex];
            ShipUpgradePiece piece = selected[0].Identifier as ShipUpgradePiece;
            if (piece != null)
            {
                _preview.EquipUpgradePiece(slotTag, piece);
            }

            ShipwrightDiagnostics.Info(string.Format(
                "Dromon builder selected {0} for {1}.",
                piece == null ? "no part" : piece.StringId,
                slotTag));
            _slotIndex++;
            ShowNextSlot();
        }

        private void ShowNamingStep()
        {
            InformationManager.ShowTextInquiry(
                new TextInquiryData(
                    "Dromon Builder — Step 9 of 9: Name",
                    "Name the configured ship before opening the native War Sails 3D preview and purchase screen.",
                    true,
                    true,
                    "Open 3D preview",
                    "Cancel commission",
                    Finish,
                    Cancel,
                    false,
                    ValidateName,
                    string.Empty,
                    "Dromon"),
                true);
        }

        private void Finish(string name)
        {
            string trimmedName = name == null ? string.Empty : name.Trim();
            _preview.SetName(new TextObject("{=!}" + trimmedName));
            ShipwrightDiagnostics.Info("Dromon builder completed for '" + trimmedName + "'; opening native preview and purchase.");
            ShipwrightPortLauncher.OpenConfiguredCommissioning(_preview, _port, _mainParty);
        }

        private void Cancel()
        {
            ShipwrightDiagnostics.Info("Dromon builder cancelled before native purchase; transient preview discarded.");
            InformationManager.DisplayMessage(new InformationMessage("Dromon commissioning cancelled."));
        }

        private static Tuple<bool, string> ValidateName(string name)
        {
            string trimmedName = name == null ? string.Empty : name.Trim();
            if (trimmedName.Length == 0)
            {
                return new Tuple<bool, string>(false, "Enter a ship name.");
            }

            if (trimmedName.Length > 40)
            {
                return new Tuple<bool, string>(false, "Ship names are limited to 40 characters.");
            }

            return new Tuple<bool, string>(true, string.Empty);
        }

        private static string GetSlotName(ShipSlot slot, string fallback)
        {
            string nativeName = slot.GetSlotTypeName().ToString();
            return string.IsNullOrWhiteSpace(nativeName) ? Capitalize(fallback) : nativeName;
        }

        private static string Capitalize(string value)
        {
            return string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value.Substring(1);
        }

        private static string BuildPieceHint(ShipUpgradePiece piece)
        {
            string description = piece.Description == null ? string.Empty : piece.Description.ToString();
            return string.Format("Shipyard level {0}. {1}", piece.RequiredPortLevel, description).Trim();
        }
    }
}

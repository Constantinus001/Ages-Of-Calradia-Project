using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.InputSystem;

namespace AgesOfCalradiaInternalWarsTest
{
    // Main-thread observer. Hotkey works even when settlement menus cannot be selected.
    internal sealed class InternalWarDiagnosticMonitor
    {
        private const float AutomaticReportRetrySeconds = 60;
        private const float HealthIntervalSeconds = 60;
        private readonly InternalWarTestBehavior _owner;
        private string _lastLog = string.Empty;
        private string _lastSnapshot = string.Empty;
        private float _delay;
        private float _reportRetryDelay;
        private float _healthDelay;
        private string _lastReportFailure = string.Empty;
        private bool _inputFailed;
        private bool _nativeReadFailed;
        internal InternalWarDiagnosticMonitor(InternalWarTestBehavior owner) { _owner = owner; }

        internal void Tick(float dt)
        {
            // A failing disk/native snapshot boundary must not be retried on every state change.
            // Keep observing/logging transitions while deferring only automatic report writes.
            if (!float.IsNaN(dt) && !float.IsInfinity(dt)) _reportRetryDelay -= Math.Max(0, dt);
            if (!float.IsNaN(dt) && !float.IsInfinity(dt)) _healthDelay -= Math.Max(0, dt);
            if (!_inputFailed)
            {
                try
                {
                    if (Input.IsKeyPressed(InputKey.F10) && Input.IsKeyDown(InputKey.LeftControl) && Input.IsKeyDown(InputKey.LeftShift))
                    {
                        string path, message;
                        InternalWarDiagnosticsReport.TryWrite(_owner, "Ctrl+Shift+F10 hotkey", out path, out message);
                        InternalWarTestDiagnostics.Info(message);
                    }
                }
                catch (Exception exception)
                {
                    _inputFailed = true;
                    InternalWarTestDiagnostics.Error("Diagnostic hotkey unavailable; menu reports remain available.", exception);
                }
            }
            _delay -= Math.Max(0, dt);
            if (_delay > 0) return;
            _delay = 1;
            string registry = _owner.RegistrySignature;
            if (_owner.Conflict == null && _owner.CurrentRecord == null && _owner.Raid == null && registry.Length == 0
                && string.IsNullOrEmpty(_owner.RecoveryBlocker)) return;
            string signature = (_owner.Conflict == null ? "legacy" : _owner.Conflict.Id + "/" + _owner.Conflict.Phase)
                + " operation=" + (_owner.CurrentRecord == null ? "none" : _owner.CurrentRecord.ConflictId + "/"
                    + _owner.CurrentRecord.State + "/capture=" + _owner.CurrentRecord.CaptureApplied + "/clean=" + _owner.CurrentRecord.CleanupComplete)
                + " raid=" + (_owner.Raid == null ? "none" : _owner.Raid.Id + "/closed=" + _owner.Raid.Closed)
                + " blocked=" + _owner.RecoveryBlocker + " registry=" + registry
                + " controllers=" + string.Join(";", _owner.Controllers.Select(c =>
                    (c.Conflict == null ? "legacy" : c.Conflict.Id)
                    + ":" + (c.CurrentRecord == null ? "none" : c.CurrentRecord.ConflictId + "/" + c.CurrentRecord.State
                        + "/capture=" + c.CurrentRecord.CaptureApplied + "/clean=" + c.CurrentRecord.CleanupComplete)
                    + ":raid=" + (c.Raid == null ? "none" : c.Raid.Id + "/event=" + c.Raid.EventObserved
                        + "/stop=" + c.Raid.StopRequested + "/closed=" + c.Raid.Closed)
                    + ":battle=" + BattleSignature(c)));
            if (signature != _lastLog)
            {
                _lastLog = signature;
                InternalWarTestDiagnostics.Info("STATE: day=" + CampaignTime.Now.ToDays + " " + signature);
            }
            if ((signature == _lastSnapshot && _healthDelay > 0) || _reportRetryDelay > 0) return;
            string reportPath, result;
            if (InternalWarDiagnosticsReport.TryWrite(_owner,
                (signature == _lastSnapshot ? "periodic health: " : "state transition: ") + signature, out reportPath, out result))
            {
                _lastSnapshot = signature;
                _healthDelay = HealthIntervalSeconds;
                _lastReportFailure = string.Empty;
            }
            else
            {
                _reportRetryDelay = AutomaticReportRetrySeconds;
                if (result != _lastReportFailure) InternalWarTestDiagnostics.Info("Automatic snapshot deferred: " + result);
                _lastReportFailure = result;
            }
        }

        private string BattleSignature(InternalWarTestBehavior controller)
        {
            try
            {
                string id = controller.HasActiveRaid ? controller.Raid.LeaderPartyId
                    : controller.CurrentRecord != null && controller.CurrentRecord.IsOperational ? controller.CurrentRecord.LeaderPartyId : null;
                var party = id == null ? null : InternalWarTestBehavior.FindParty(id);
                var battle = party == null ? null : party.Party.MapEvent;
                return battle == null ? "none" : battle.EventType + "/" + battle.State + "/final=" + battle.IsFinalized;
            }
            catch (Exception exception)
            {
                if (!_nativeReadFailed) InternalWarTestDiagnostics.Error("Diagnostic battle state unavailable; gameplay is unchanged.", exception);
                _nativeReadFailed = true;
                return "unavailable";
            }
        }
    }
}

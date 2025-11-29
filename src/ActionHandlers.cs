using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace HostageRescue;

public partial class HostageRescuePlugin
{
    private void OnKeyStateChange(IOnClientKeyStateChangedEvent @event)
    {
        if (@event.Key != KeyKind.E)
            return;

        var player = Core.PlayerManager.GetPlayer(@event.PlayerId);
        if (player?.PlayerPawn?.IsValid != true)
            return;

        var playerId = player.PlayerID;

        var gameRules = Core.EntitySystem.GetGameRules();
        if (gameRules?.IsValid ?? false)
        {
            if (gameRules.WarmupPeriod)
                return;
        }

        if (@event.Pressed)
            StartHostageAction(player);
        else
            CancelHostageAction(playerId);
    }

    private void StartHostageAction(IPlayer player)
    {
        var playerId = player.PlayerID;
        var pawn = player.RequiredPlayerPawn;

        CancelHostageAction(playerId);

        // already carrying? drop it
        if (pawn.HostageServices?.CarriedHostage.Value is CBaseEntity carriedHostage && carriedHostage.IsValid)
        {
            var state = new ActionState { Type = ActionType.DroppingHostage };
            _playerActionState[playerId] = state;

            SetProgressBar(player, DROP_DURATION, CSPlayerBlockingUseAction_t.k_CSPlayerBlockingUseAction_HostageDropping);
            PlaySound(_hostageDropSound, pawn);

            var progressTimer = Core.Scheduler.DelayBySeconds(DROP_DURATION, () => CompleteDropHostage(player));
            _playerProgressTimers[playerId] = progressTimer;

            var validationTimer = Core.Scheduler.RepeatBySeconds(0.25f, () => ValidateDropHostageAction(player));
            _playerValidationTimers[playerId] = validationTimer;
        }
        else
        {
            // only Ts can grab hostages
            if (player.Controller.Team != Team.T)
                return;

            var hostage = GetHostageInView(player);
            if (hostage?.IsValid != true)
                return;

            var distance = (pawn.AbsOrigin!.Value - hostage.AbsOrigin!.Value).Length();
            if (distance > PICKUP_RANGE)
                return;

            if (hostage.Leader.Value != null || hostage.IsRescued)
                return;

            var state = new ActionState { Type = ActionType.PickingUpHostage, TargetHostage = hostage };
            _playerActionState[playerId] = state;

            SetProgressBar(player, PICKUP_DURATION, CSPlayerBlockingUseAction_t.k_CSPlayerBlockingUseAction_HostageGrabbing);
            PlaySound(_hostagePickupSound, hostage);

            var progressTimer = Core.Scheduler.DelayBySeconds(PICKUP_DURATION, () => CompletePickupHostage(player, hostage));
            _playerProgressTimers[playerId] = progressTimer;

            var validationTimer = Core.Scheduler.RepeatBySeconds(0.25f, () => ValidatePickupHostageAction(player, hostage));
            _playerValidationTimers[playerId] = validationTimer;
        }
    }

    private void CancelHostageAction(int playerId)
    {
        bool hadActiveAction = _playerActionState.ContainsKey(playerId);

        if (_playerProgressTimers.TryGetValue(playerId, out var progressTimer))
        {
            progressTimer.Cancel();
            _playerProgressTimers.Remove(playerId);
        }

        if (_playerValidationTimers.TryGetValue(playerId, out var validationTimer))
        {
            validationTimer.Cancel();
            _playerValidationTimers.Remove(playerId);
        }

        _playerActionState.Remove(playerId);

        if (hadActiveAction)
        {
            var player = Core.PlayerManager.GetPlayer(playerId);
            if (player?.PlayerPawn?.IsValid == true)
                SetProgressBar(player, 0, CSPlayerBlockingUseAction_t.k_CSPlayerBlockingUseAction_None);
        }
    }

    private void ValidatePickupHostageAction(IPlayer player, CHostage hostage)
    {
        var pawn = player.PlayerPawn;
        if (pawn?.AbsOrigin == null || hostage?.IsValid != true)
        {
            CancelHostageAction(player.PlayerID);
            return;
        }

        float distance = (pawn.AbsOrigin.Value - hostage.AbsOrigin!.Value).Length();
        if (distance >= PICKUP_RANGE)
        {
            CancelHostageAction(player.PlayerID);
            return;
        }

        var inView = GetHostageInView(player);
        if (inView?.Index != hostage.Index)
            CancelHostageAction(player.PlayerID);
    }

    private void ValidateDropHostageAction(IPlayer player)
    {
        var pawn = player.PlayerPawn;
        if (pawn?.IsValid != true)
        {
            CancelHostageAction(player.PlayerID);
            return;
        }

        if (pawn.HostageServices?.CarriedHostage.Value?.IsValid != true)
            CancelHostageAction(player.PlayerID);
    }

    private void CompletePickupHostage(IPlayer player, CHostage hostage)
    {
        CancelHostageAction(player.PlayerID);

        if (hostage?.IsValid != true)
            return;

        hostage.HostageState = 2;
        hostage.HostageStateUpdated();

        hostage.GrabSuccessTime.Value = Core.Engine.GlobalVars.CurrentTime;
        hostage.GrabSuccessTimeUpdated();

        if (hostage.AbsOrigin != null)
            hostage.GrabbedPos = hostage.AbsOrigin.Value;

        // small delay before actually attaching to player
        Core.Scheduler.DelayBySeconds(0.25f, () =>
        {
            if (hostage?.IsValid != true)
                return;

            hostage.HostageState = 3;
            hostage.HostageStateUpdated();

            hostage.Effects |= 32; // invisibility
            hostage.EffectsUpdated();

            InvokeHostageFollow(player, hostage);
            SetProgressBar(player, 0, CSPlayerBlockingUseAction_t.k_CSPlayerBlockingUseAction_None);
        });
    }

    private void CompleteDropHostage(IPlayer player)
    {
        CancelHostageAction(player.PlayerID);

        var pawn = player.PlayerPawn;
        if (pawn?.AbsOrigin != null)
        {
            var dropPos = GetDropPosition(pawn);
            InvokeHostageDrop(player, dropPos);
        }
    }
}

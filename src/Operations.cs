using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
using SwiftlyS2.Shared.Sounds;

namespace HostageRescue;

public partial class HostageRescuePlugin
{
    private CHostage? GetHostageInView(IPlayer player)
    {
        var gameRules = Core.EntitySystem.GetGameRules();
        if (gameRules == null)
            return null;

        var entity = gameRules.FindPickerEntity<CHostage>(player.Controller);
        if (entity?.IsValid == true && entity.DesignerName == "hostage_entity")
        {
            var pawn = player.PlayerPawn;
            if (pawn?.AbsOrigin != null && entity.AbsOrigin != null)
            {
                var distance = GetDistance(pawn.AbsOrigin.Value, entity.AbsOrigin.Value);
                if (distance <= PICKUP_RANGE)
                    return entity;
            }
        }

        return null;
    }

    private Vector GetDropPosition(CCSPlayerPawn pawn)
    {
        var playerPos = pawn.AbsOrigin ?? Vector.Zero;
        var playerRotation = pawn.AbsRotation ?? QAngle.Zero;

        // try dropping in front first
        float radianY = playerRotation.Yaw * (MathF.PI / 180f);
        var forward = new Vector(
            MathF.Cos(radianY) * PICKUP_RANGE,
            MathF.Sin(radianY) * PICKUP_RANGE,
            0
        );
        var initialPosition = playerPos + forward;

        if (IsValidHostagePosition(pawn, initialPosition))
            return initialPosition;

        return FindValidHostagePosition(pawn, initialPosition);
    }

    private static float GetDistance(Vector pos1, Vector pos2) => (pos1 - pos2).Length();

    private Vector FindValidHostagePosition(CCSPlayerPawn pawn, Vector initialPosition)
    {
        var playerPos = pawn.AbsOrigin ?? Vector.Zero;
        var playerRotation = pawn.AbsRotation ?? QAngle.Zero;

        if (IsValidHostagePosition(pawn, initialPosition))
            return initialPosition;

        float originalDropDistance = GetDistance(playerPos, initialPosition);
        float searchDistance = Math.Max(originalDropDistance, MINIMUM_SAFE_DISTANCE);
        float playerYaw = playerRotation.Yaw;

        // try sides first, then diagonals, then behind
        int[] smartAngles = [-90, 90, -60, 60, -120, 120, -30, 30, 180, 0];

        foreach (int relativeAngle in smartAngles)
        {
            float absoluteAngle = playerYaw + relativeAngle;
            float radians = absoluteAngle * (MathF.PI / 180f);
            var testPosition = new Vector(
                playerPos.X + MathF.Cos(radians) * searchDistance,
                playerPos.Y + MathF.Sin(radians) * searchDistance,
                playerPos.Z
            );

            if (IsValidHostagePosition(pawn, testPosition))
                return testPosition;
        }

        // fallback: just drop behind
        float backwardAngle = (playerYaw + 180) * (MathF.PI / 180f);
        return new Vector(
            playerPos.X + MathF.Cos(backwardAngle) * MINIMUM_SAFE_DISTANCE,
            playerPos.Y + MathF.Sin(backwardAngle) * MINIMUM_SAFE_DISTANCE,
            playerPos.Z
        );
    }

    private bool IsValidHostagePosition(CCSPlayerPawn playerPawn, Vector position)
    {
        var playerPos = playerPawn.AbsOrigin ?? Vector.Zero;

        if (GetDistance(position, playerPos) < 25f)
            return false;

        var allPlayers = Core.PlayerManager.GetAllPlayers();
        if (allPlayers != null)
        {
            foreach (var otherPlayer in allPlayers)
            {
                if (otherPlayer?.PlayerPawn?.AbsOrigin == null)
                    continue;

                var ownerEntity = playerPawn.OwnerEntity;
                if (ownerEntity.IsValid && otherPlayer.PlayerID == ownerEntity.EntityIndex)
                    continue;

                var otherPos = otherPlayer.PlayerPawn.AbsOrigin;
                if (otherPos.HasValue && GetDistance(position, otherPos.Value) < 25f)
                    return false;
            }
        }

        // hull trace for collision check
        var trace = new CGameTrace();

        var endPos = new Vector(
            position.X + (position.X - playerPos.X) * 0.3f,
            position.Y + (position.Y - playerPos.Y) * 0.3f,
            position.Z + 10
        );

        const MaskTrace maskPlayerSolid = MaskTrace.Solid | MaskTrace.Player | MaskTrace.Npc | MaskTrace.WorldGeometry | MaskTrace.PhysicsProp | MaskTrace.StaticLevel;

        Core.Trace.SimpleTrace(
            position,
            endPos,
            RayType_t.RAY_TYPE_HULL,
            RnQueryObjectSet.All,
            maskPlayerSolid,
            MaskTrace.Empty,
            MaskTrace.Empty,
            CollisionGroup.PlayerMovement,
            ref trace,
            playerPawn
        );

        return trace.Fraction > 0.5f;
    }

    private void InvokeHostageFollow(IPlayer player, CHostage hostage)
    {
        if (_cHostageFollow == null)
            return;

        var pawn = player.PlayerPawn;
        if (pawn?.IsValid != true || hostage?.IsValid != true)
            return;

        _cHostageFollow.Call(hostage.Address, pawn.Address);

        if (hostage.AbsOrigin != null)
            PlaySoundNearby(hostage.AbsOrigin.Value, _hostagePickupSound);
    }

    private void InvokeHostageDrop(IPlayer player, Vector? dropPosition = null)
    {
        if (_cHostageDrop == null)
            return;

        var pawn = player.PlayerPawn;
        if (pawn?.IsValid != true)
            return;

        var carriedHostage = pawn.HostageServices?.CarriedHostage.Value;
        if (carriedHostage?.IsValid != true)
            return;

        var dropPos = dropPosition ?? pawn.AbsOrigin ?? new Vector(0, 0, 0);
        var hostageHandle = carriedHostage as INativeHandle;

        if (hostageHandle?.Address == IntPtr.Zero)
            return;

        _cHostageDrop.Call(hostageHandle!.Address, ref dropPos, false);
        PlaySoundNearby(dropPos, _hostageDropSound);
    }

    private void SetProgressBar(IPlayer player, float duration, CSPlayerBlockingUseAction_t actionType)
    {
        var pawn = player.PlayerPawn;
        if (pawn?.IsValid != true)
            return;

        int progressTime = (int)Math.Ceiling(duration);
        float currentTime = Core.Engine.GlobalVars.CurrentTime;

        pawn.ProgressBarDuration = progressTime;
        pawn.ProgressBarDurationUpdated();

        pawn.ProgressBarStartTime = duration == 0 ? 0 : currentTime;
        pawn.ProgressBarStartTimeUpdated();

        pawn.SimulationTime = currentTime + duration;
        pawn.SimulationTimeUpdated();

        pawn.BlockingUseActionInProgress = actionType;
        pawn.BlockingUseActionInProgressUpdated();
    }

    private void PlaySoundNearby(Vector sourcePos, SoundEvent sound, float radius = 2000f)
    {
        var players = Core.PlayerManager.GetAllPlayers();
        if (players == null)
            return;

        foreach (var player in players)
        {
            if (!IsPlayerValid(player))
                continue;

            var pawnPos = player.PlayerPawn?.AbsOrigin;
            if (pawnPos == null)
                continue;

            float distance = CalculateDistance(sourcePos, pawnPos.Value);
            if (distance <= radius)
                sound.Recipients.AddRecipient(player.PlayerID);
        }

        sound.Emit();
    }

    private static bool IsPlayerValid(IPlayer? player)
    {
        return player?.IsValid == true && player.PlayerPawn?.IsValid == true && !player.IsFakeClient;
    }

    private static float CalculateDistance(Vector pos1, Vector pos2)
    {
        float dx = pos1.X - pos2.X;
        float dy = pos1.Y - pos2.Y;
        float dz = pos1.Z - pos2.Z;
        return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}

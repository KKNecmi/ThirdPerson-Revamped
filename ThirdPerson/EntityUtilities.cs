using System.Drawing;
using System.Linq;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Utils;
using CS2TraceRay.Class;
using CS2TraceRay.Enum;

namespace ThirdPersonRevamped;

public static class EntityUtilities
{
    private static readonly Dictionary<ulong, Vector> LastFallbackCameraPos = new();
    private static readonly Dictionary<ulong, QAngle> LastCameraAngles = new();
    private static readonly Dictionary<ulong, Vector> LastGoodCameraPos = new();
    private static readonly Dictionary<ulong, float> LastZUpdateTime = new();

    private static float GetTimeSeconds() =>
        (float)DateTimeOffset.Now.ToUnixTimeMilliseconds() / 1000f;

    public static void SetColor(this CDynamicProp? prop, Color colour)
    {
        if (prop != null && prop.IsValid)
        {
            prop.Render = colour;
            Utilities.SetStateChanged(prop, "CBaseModelEntity", "m_clrRender");
        }
    }

    public static void SetColor(this CPhysicsPropMultiplayer? prop, Color colour)
    {
        if (prop != null && prop.IsValid)
        {
            prop.Render = colour;
            Utilities.SetStateChanged(prop, "CBaseModelEntity", "m_clrRender");
        }
    }

    public static void UpdateCamera(this CDynamicProp _cameraProp, CCSPlayerController target)
    {
        if (target.IsNullOrInvalid() || !_cameraProp.IsValid)
            return;

        float safeDistance = target.CalculateCollisionSafeDistance(110f, 10f, 90f);
        var pawn = target.PlayerPawn!.Value!;
        _cameraProp.Teleport(
            target.CalculateSafeCameraPosition(safeDistance, 90),
            pawn.V_angle,
            new Vector()
        );
    }

    public static void UpdateCameraSmooth(
        this CPhysicsPropMultiplayer prop,
        CCSPlayerController player
    )
    {
        if (player.IsNullOrInvalid() || !prop.IsValid)
            return;

        const float desiredDistance = 90f;
        const float minHeightAbovePlayer = 70f;
        const float maxHeightAbovePlayer = 110f;
        const float minDistanceFromPlayer = 78f;
        const float maxDistanceFromPlayer = 78f;
        const float positionStabilization = 0.8f;

        float safeDistance = player.CalculateCollisionSafeDistance(desiredDistance, 10f, 70f);
        Vector targetPos = player.CalculateSafeCameraPosition(safeDistance, 70f);

        var pawn = player.PlayerPawn?.Value;
        if (pawn == null || pawn.AbsOrigin == null)
            return;

        Vector currentPos = prop.AbsOrigin ?? new Vector();
        Vector playerPos = pawn.AbsOrigin;

        float minZ = playerPos.Z + minHeightAbovePlayer;
        float maxZ = playerPos.Z + maxHeightAbovePlayer;

        targetPos.Z = Math.Clamp(targetPos.Z, minZ, maxZ);

        float currentTime = GetTimeSeconds();
        float timeSinceLastUpdate =
            currentTime
            - (
                LastZUpdateTime.TryGetValue(player.SteamID, out float lastTime)
                    ? lastTime
                    : currentTime
            );
        LastZUpdateTime[player.SteamID] = currentTime;

        float verticalVelocity = Math.Abs(pawn.AbsVelocity.Z);
        float horizontalSpeed = pawn.AbsVelocity.Length2D();
        float speedT = Math.Clamp(horizontalSpeed / 300f, 0f, 1f);
        float lerpFactor = 0.06f + (0.45f - 0.06f) * speedT;
        float effectiveLerp = Math.Clamp(lerpFactor * positionStabilization, 0.05f, 0.5f);

        Vector smoothedPos = currentPos.Lerp(targetPos, effectiveLerp);

        if (LastGoodCameraPos.TryGetValue(player.SteamID, out var lastGoodPos))
        {
            float zDiff = smoothedPos.Z - lastGoodPos.Z;
            float zResponse = Math.Clamp(Math.Max(verticalVelocity * 0.1f, 5f), 10f, 80f);
            float maxAllowedZChange = Math.Max(zResponse * timeSinceLastUpdate, 0.5f);
            if (Math.Abs(zDiff) > maxAllowedZChange)
                smoothedPos.Z = lastGoodPos.Z + Math.Sign(zDiff) * maxAllowedZChange;
            if (verticalVelocity < 5f && horizontalSpeed < 50f)
                LastFallbackCameraPos[player.SteamID] = smoothedPos;
        }

        smoothedPos.Z = Math.Clamp(smoothedPos.Z, minZ, maxZ);

        Vector toPlayer = playerPos - smoothedPos;
        float currentDistance = toPlayer.Length();
        if (currentDistance < minDistanceFromPlayer || currentDistance > maxDistanceFromPlayer)
        {
            Vector direction = toPlayer.Normalized();
            smoothedPos =
                playerPos
                - direction
                    * Math.Clamp(
                        currentDistance,
                        minDistanceFromPlayer,
                        Math.Min(desiredDistance, maxDistanceFromPlayer)
                    );
            smoothedPos.Z = Math.Max(smoothedPos.Z, minZ);
        }

        QAngle targetAngle = pawn.V_angle;
        prop.Teleport(smoothedPos, targetAngle, new Vector());
        LastGoodCameraPos[player.SteamID] = smoothedPos;
    }

    public static bool IsMoving(this CCSPlayerController player)
    {
        var velocity = player.PlayerPawn?.Value?.AbsVelocity;
        return velocity != null && (velocity.Length() > 15f || Math.Abs(velocity.Z) > 10f);
    }

    public static Vector CalculatePositionInFront(
        this CCSPlayerController player,
        float offSetXY,
        float offSetZ = 0
    )
    {
        var pawn = player.PlayerPawn?.Value;
        if (pawn?.AbsOrigin == null || pawn.EyeAngles == null)
            return new Vector(0, 0, 0);

        float yawAngleRadians = (float)(pawn.EyeAngles.Y * Math.PI / 180.0);
        return new Vector
        {
            X = pawn.AbsOrigin.X + offSetXY * (float)Math.Cos(yawAngleRadians),
            Y = pawn.AbsOrigin.Y + offSetXY * (float)Math.Sin(yawAngleRadians),
            Z = pawn.AbsOrigin.Z + offSetZ,
        };
    }

    public static bool IsInfrontOfPlayer(
        this CCSPlayerController player1,
        CCSPlayerController player2
    )
    {
        if (
            !player1.IsValid
            || !player2.IsValid
            || !player1.PlayerPawn.IsValid
            || !player2.PlayerPawn.IsValid
        )
            return false;

        var p1 = player1.PlayerPawn.Value;
        var p2 = player2.PlayerPawn.Value;
        var yaw = (float)(p1.EyeAngles.Y * Math.PI / 180.0);

        Vector p1Dir = new(MathF.Cos(yaw), MathF.Sin(yaw), 0);
        Vector p1ToP2 = p2.AbsOrigin - p1.AbsOrigin;
        return p1ToP2.Dot(p1Dir) < 0;
    }

    public static float Dot(this Vector v1, Vector v2) => v1.X * v2.X + v1.Y * v2.Y + v1.Z * v2.Z;

    public static void Health(this CCSPlayerController player, int health)
    {
        if (player.PlayerPawn == null || player.PlayerPawn.Value == null)
            return;

        player.Health = health;
        player.PlayerPawn.Value.Health = health;

        if (health > 100)
        {
            player.MaxHealth = health;
            player.PlayerPawn.Value.MaxHealth = health;
        }

        Utilities.SetStateChanged(player.PlayerPawn.Value, "CBaseEntity", "m_iHealth");
    }

    public static Vector Lerp(this Vector from, Vector to, float t)
    {
        return new Vector(
            from.X + (to.X - from.X) * t,
            from.Y + (to.Y - from.Y) * t,
            from.Z + (to.Z - from.Z) * t
        );
    }

    public static Vector Normalized(this Vector vec)
    {
        float length = vec.Length();
        return length == 0f ? new Vector(0, 0, 0) : vec / length;
    }

    public static float Length(this Vector vec) =>
        (float)Math.Sqrt(vec.X * vec.X + vec.Y * vec.Y + vec.Z * vec.Z);

    public static float Length2D(this Vector vec) =>
        (float)Math.Sqrt(vec.X * vec.X + vec.Y * vec.Y);

    public static bool IsNullOrInvalid(this CCSPlayerController? player) =>
        player == null || !player.IsValid || !player.PlayerPawn.IsValid;
}

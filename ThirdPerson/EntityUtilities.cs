using System.Drawing;
using System.Linq;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
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

    public static class DebugLogger
    {
        public static void Log(
            string tag,
            string message,
            CCSPlayerController? player = null,
            object? data = null
        )
        {
            string steamId = player != null ? player.SteamID.ToString() : "Unknown";
            string fullMessage = $"[{DateTime.Now:HH:mm:ss}] [{tag}] [Player: {steamId}] {message}";
            if (data != null)
                fullMessage += $" | Data: {data}";

            Console.WriteLine(fullMessage);
        }
    }

    private static float MoveTowards(float current, float target, float baseStepSize)
    {
        current = NormalizeAngle(current);
        target = NormalizeAngle(target);

        float delta = target - current;

        if (delta > 180)
            delta -= 360;
        else if (delta < -180)
            delta += 360;

        float dynamicStepSize = Math.Min(baseStepSize * Math.Abs(delta) / 180f, Math.Abs(delta));

        if (Math.Abs(delta) <= dynamicStepSize)
        {
            return target;
        }

        return NormalizeAngle(current + Math.Sign(delta) * dynamicStepSize);
    }

    private static float NormalizeAngle(float angle)
    {
        while (angle > 180)
            angle -= 360;
        while (angle < -180)
            angle += 360;
        return angle;
    }

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

    public static void UpdateCameraPositionRaw(
        CCSPlayerController player,
        CPhysicsPropMultiplayer camera
    )
    {
        var pawn = player.PlayerPawn?.Value;
        if (pawn == null || pawn.AbsOrigin == null)
            return;

        float desiredDistance = 100.0f;
        float verticalOffset = 70.0f;

        Vector targetPos = player.CalculateSafeCameraPosition(desiredDistance, verticalOffset);
        QAngle targetAngle = pawn.EyeAngles;

        camera.Teleport(targetPos, targetAngle, new Vector());
    }

    public static void UpdateCamera(this CDynamicProp _cameraProp, CCSPlayerController player)
    {
        if (player.IsNullOrInvalid() || !_cameraProp.IsValid)
            return;

        var pawn = player.PlayerPawn!.Value!;
        float safeDistance = player.CalculateCollisionSafeDistance(90f, 10f, 60f);

        Vector cameraPos = player.CalculateSafeCameraPosition(safeDistance, 90);

        QAngle cameraAngle = player.PlayerPawn.Value.EyeAngles;

        _cameraProp.Teleport(cameraPos, cameraAngle, new Vector());
    }

    private static readonly Dictionary<ulong, float> LastCameraDistances = new();

    private const int SmoothCamBaseStepSize = 32;

    public static void UpdateCameraSmooth(
        this CPhysicsPropMultiplayer prop,
        CCSPlayerController player
    )
    {
        if (player.IsNullOrInvalid() || !prop.IsValid)
            return;

        var pawn = player.PlayerPawn?.Value;
        if (IsMirrorEnabled(player))
        {
            if (pawn == null || pawn.AbsOrigin == null)
                return;

            var fixedPos = player.CalculateSafeCameraPosition(75f, 40f);
            var fixedAngle = GetMirrorAngle(player);

            prop.Teleport(fixedPos, fixedAngle, new Vector());
            return;
        }

        const float desiredDistance = 90f;
        const float minHeightAbovePlayer = 70f;
        const float maxHeightAbovePlayer = 110f;
        const float minDistanceFromPlayer = 78f;
        const float maxDistanceFromPlayer = 78f;
        const float positionStabilization = 0.8f;

        float safeDistance = player.CalculateCollisionSafeDistance(desiredDistance, 10f, 70f);
        Vector targetPos = player.CalculateSafeCameraPosition(safeDistance, 70f);

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

        float maxLerp = 0.45f;
        float minLerp = 0.06f;
        float speedT = Math.Clamp(horizontalSpeed / 300f, 0f, 1f);
        float lerpFactor = minLerp + (maxLerp - minLerp) * speedT;

        float effectiveLerp = Math.Clamp(lerpFactor * positionStabilization, 0.05f, 0.5f);

        Vector smoothedPos = currentPos.Lerp(targetPos, effectiveLerp);

        if (IsMirrorEnabled(player))
        {
            prop.Teleport(smoothedPos, pawn.V_angle, new Vector());
            return;
        }

        if (LastGoodCameraPos.TryGetValue(player.SteamID, out var lastGoodPos))
        {
            float zDiff = smoothedPos.Z - lastGoodPos.Z;

            float zResponse = Math.Clamp(Math.Max(verticalVelocity * 0.1f, 5f), 10f, 80f);
            float maxAllowedZChange = Math.Max(zResponse * timeSinceLastUpdate, 0.5f);

            if (Math.Abs(zDiff) > maxAllowedZChange)
            {
                smoothedPos.Z = lastGoodPos.Z + Math.Sign(zDiff) * maxAllowedZChange;
            }

            if (verticalVelocity < 5f && horizontalSpeed < 50f)
            {
                LastFallbackCameraPos[player.SteamID] = smoothedPos;
            }
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

        if (!IsMirrorEnabled(player))
        {
            LastGoodCameraPos[player.SteamID] = smoothedPos;
        }
    }

    public static bool IsMoving(this CCSPlayerController player)
    {
        var velocity = player.PlayerPawn?.Value?.AbsVelocity;
        if (velocity == null)
            return false;

        return velocity.Length() > 15f || Math.Abs(velocity.Z) > 10f;
    }

    public static Vector CalculateVelocity(Vector positionA, Vector positionB, float timeDuration)
    {
        Vector directionVector = positionB - positionA;

        float distance = directionVector.Length();

        if (timeDuration == 0)
        {
            timeDuration = 1;
        }

        float velocityMagnitude = distance / timeDuration;

        if (distance != 0)
        {
            directionVector /= distance;
        }

        Vector velocityVector = directionVector * velocityMagnitude;

        return velocityVector;
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
        float offsetX = offSetXY * (float)Math.Cos(yawAngleRadians);
        float offsetY = offSetXY * (float)Math.Sin(yawAngleRadians);

        return new Vector
        {
            X = pawn.AbsOrigin.X + offsetX,
            Y = pawn.AbsOrigin.Y + offsetY,
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

        var player1Pawn = player1.PlayerPawn.Value;
        var player2Pawn = player2.PlayerPawn.Value;
        var yawAngleRadians = (float)(player1Pawn!.EyeAngles.Y * Math.PI / 180.0);

        Vector player1Direction = new(MathF.Cos(yawAngleRadians), MathF.Sin(yawAngleRadians), 0);

        if (player1Pawn.AbsOrigin == null || player2Pawn.AbsOrigin == null)
            return false;

        Vector player1ToPlayer2 = player2Pawn.AbsOrigin - player1Pawn.AbsOrigin;

        float dotProduct = player1ToPlayer2.Dot(player1Direction);

        return dotProduct < 0;
    }

    public static float Dot(this Vector vector1, Vector vector2)
    {
        return vector1.X * vector2.X + vector1.Y * vector2.Y + vector1.Z * vector2.Z;
    }

    public static void Health(this CCSPlayerController player, int health)
    {
        if (player.PlayerPawn == null || player.PlayerPawn.Value == null)
        {
            return;
        }

        player.Health = health;
        player.PlayerPawn.Value.Health = health;

        if (health > 100)
        {
            player.MaxHealth = health;
            player.PlayerPawn.Value.MaxHealth = health;
        }

        Utilities.SetStateChanged(player.PlayerPawn.Value, "CBaseEntity", "m_iHealth");
    }

    public static float CalculateCollisionSafeDistance(
        this CCSPlayerController player,
        float maxDistance = 110f,
        float checkStep = 10f,
        float verticalOffset = 90f
    )
    {
        var pawn = player.PlayerPawn?.Value;

        float safeDistance = maxDistance;

        if (pawn?.AbsOrigin == null)
            return safeDistance;

        float yawRadians = pawn.EyeAngles!.Y * (float)Math.PI / 180f;
        var backward = new Vector(-MathF.Cos(yawRadians), -MathF.Sin(yawRadians), 0);
        var allPlayers = Utilities.GetPlayers();

        for (float d = checkStep; d <= maxDistance; d += checkStep)
        {
            var checkPos = pawn.AbsOrigin + backward * d + new Vector(0, 0, verticalOffset - 30f);

            var nearbyPlayers = allPlayers.Where(p =>
                p != null
                && p.IsValid
                && p.PlayerPawn.IsValid
                && p.PlayerPawn.Value.AbsOrigin != null
                && (p.PlayerPawn.Value.AbsOrigin - checkPos).Length() < 8.0f
            );

            if (nearbyPlayers.Any())
            {
                safeDistance = d - checkStep;
                break;
            }
        }

        return safeDistance;
    }

    public static Vector CalculateSafeCameraPosition(
        this CCSPlayerController player,
        float desiredDistance,
        float verticalOffset = 70f
    )
    {
        if (player.IsNullOrInvalid() || player.PlayerPawn?.Value?.AbsOrigin == null)
            return new Vector(0, 0, 0);

        var pawn = player.PlayerPawn.Value;
        if (pawn?.EyeAngles == null)
            return new Vector(0, 0, 0);

        Vector pawnPos = pawn.AbsOrigin;

        float yawRadians = pawn.EyeAngles.Y * (float)Math.PI / 180f;
        float pitchRadians = pawn.EyeAngles.X * (float)Math.PI / 180f;

        float pitchFactor =
            1.0f - Math.Clamp(Math.Abs(pitchRadians) / ((float)Math.PI / 2f), 0, 0.5f);
        verticalOffset *= pitchFactor;

        var eyePos = pawnPos + new Vector(0, 0, verticalOffset);
        var backwardDir = new Vector(-MathF.Cos(yawRadians), -MathF.Sin(yawRadians), 0);

        var targetCamPos = eyePos + backwardDir * desiredDistance;

        float minAllowedZ = pawnPos.Z;
        var groundTrace = TraceRay.TraceShape(
            targetCamPos + new Vector(0, 0, 50),
            targetCamPos + new Vector(0, 0, -200),
            (ulong)TraceMask.MaskSolid,
            0ul,
            pawn.Handle
        );

        if (groundTrace.DidHit())
        {
            minAllowedZ = Math.Max(minAllowedZ, groundTrace.Position.Z + 15f);
        }

        var trace = TraceRay.GetGameTraceByEyePosition(
            player,
            targetCamPos,
            (ulong)TraceMask.MaskShot
        );

        Vector finalPos;

        if (trace.DidHit())
        {
            Vector hitVec = trace.Position.ToVector();
            float distanceToWall = (hitVec - eyePos).Length();

            float clampedDistance;

            if (distanceToWall < 16f)
            {
                clampedDistance = 10f;
            }
            else if (distanceToWall < desiredDistance)
            {
                clampedDistance = Math.Clamp(distanceToWall - 6f, 10f, desiredDistance);
            }
            else
            {
                clampedDistance = desiredDistance;
            }

            finalPos = eyePos + backwardDir * clampedDistance;
        }
        else
        {
            finalPos = targetCamPos;
        }

        if (finalPos.Z < minAllowedZ)
        {
            finalPos.Z = minAllowedZ;
        }

        if (LastGoodCameraPos.TryGetValue(player.SteamID, out var lastPos))
        {
            float zDiff = finalPos.Z - lastPos.Z;

            if (player.IsMoving())
            {
                float lerpedZ = lastPos.Z + zDiff * 0.15f;

                if (Math.Abs(zDiff) < 0.25f)
                {
                    finalPos.Z = lastPos.Z;
                }
                else if (player.PlayerPawn.Value.AbsVelocity.Length2D() > 30f)
                {
                    finalPos.Z = lastPos.Z + zDiff * 0.2f;
                }
                else
                {
                    finalPos.Z = lastPos.Z;
                }
            }
            else if (Math.Abs(player.PlayerPawn.Value.AbsVelocity.Z) < 5f)
            {
                finalPos.Z = lastPos.Z;
            }
        }

        if ((finalPos - pawnPos).Length() < 10f)
        {
            finalPos = pawnPos - new Vector(0, 0, -70f);
        }

        LastGoodCameraPos[player.SteamID] = finalPos;
        return finalPos;
    }

    public static Vector CalculateSafeCameraPosition_StaticZ(
        this CCSPlayerController player,
        float desiredDistance,
        float fixedZ
    )
    {
        var pawn = player.PlayerPawn?.Value;
        if (pawn == null || pawn.AbsOrigin == null || pawn.EyeAngles == null)
            return new Vector(0, 0, 0);

        float yawRadians = pawn.EyeAngles.Y * (float)Math.PI / 180f;
        var backwardDir = new Vector(-MathF.Cos(yawRadians), -MathF.Sin(yawRadians), 0);
        var eyePos = new Vector(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, fixedZ);
        var targetCamPos = eyePos + backwardDir * desiredDistance;

        return targetCamPos;
    }

    public static Vector Lerp(this Vector from, Vector to, float t)
    {
        return new Vector(
            from.X + (to.X - from.X) * t,
            from.Y + (to.Y - from.Y) * t,
            from.Z + (to.Z - from.Z) * t
        );
    }

    public static Vector ToVector(this System.Numerics.Vector3 v)
    {
        return new Vector(v.X, v.Y, v.Z);
    }

    public static float LerpZ(float from, float to, float t)
    {
        return from + (to - from) * t;
    }

    public static Vector Round(this Vector vec, float step = 0.1f)
    {
        return new Vector(
            (float)Math.Round(vec.X / step) * step,
            (float)Math.Round(vec.Y / step) * step,
            (float)Math.Round(vec.Z / step) * step
        );
    }

    public static float Clamp(float value, float min, float max)
    {
        if (value < min)
            return min;
        if (value > max)
            return max;
        return value;
    }

    public static Vector Normalized(this Vector vec)
    {
        float length = vec.Length();
        return length == 0f ? new Vector(0, 0, 0) : vec / length;
    }

    public static float Length(this Vector vec)
    {
        return (float)Math.Sqrt(vec.X * vec.X + vec.Y * vec.Y + vec.Z * vec.Z);
    }

    public static float Length2D(this Vector vec)
    {
        return (float)Math.Sqrt(vec.X * vec.X + vec.Y * vec.Y);
    }

    public static bool IsNullOrInvalid(this CCSPlayerController? player)
    {
        return player == null || !player.IsValid || !player.PlayerPawn.IsValid;
    }

    public static bool IsMirrorEnabled(CCSPlayerController player)
    {
        return ThirdPersonRevamped.mirrorEnabled.ContainsKey(player)
            && ThirdPersonRevamped.mirrorEnabled[player];
    }

    public static QAngle GetMirrorAngle(CCSPlayerController player)
    {
        return ThirdPersonRevamped.mirrorAngle.TryGetValue(player, out var angle)
            ? angle
            : new QAngle(0, 0, 0);
    }
}

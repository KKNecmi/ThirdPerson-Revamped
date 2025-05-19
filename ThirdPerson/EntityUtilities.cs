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

            Console.WriteLine(fullMessage); // log to server console
        }
    }

    // Special handling for Yaw (and Pitch/Roll) to move in the shortest direction
    private static float MoveTowards(float current, float target, float baseStepSize)
    {
        // Normalize angles to the range [-180, 180]
        current = NormalizeAngle(current);
        target = NormalizeAngle(target);

        // Calculate the shortest direction to rotate
        float delta = target - current;

        // Ensure the shortest path is taken by adjusting delta
        if (delta > 180)
            delta -= 360;
        else if (delta < -180)
            delta += 360;

        // Dynamically adjust the step size based on the magnitude of the delta
        float dynamicStepSize = Math.Min(baseStepSize * Math.Abs(delta) / 180f, Math.Abs(delta));

        // Clamp the delta to the dynamicStepSize
        if (Math.Abs(delta) <= dynamicStepSize)
        {
            return target; // We have reached the target
        }

        // Move towards the target
        return NormalizeAngle(current + Math.Sign(delta) * dynamicStepSize);
    }

    // Normalize any angle to the range [-180, 180]
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

    private static readonly Dictionary<ulong, float> LastCameraDistances = new();

    private const int SmoothCamBaseStepSize = 32;

    public static void UpdateCameraSmooth(
        this CPhysicsPropMultiplayer prop,
        CCSPlayerController player
    )
    {
        if (player.IsNullOrInvalid() || !prop.IsValid)
            return;

        const float desiredDistance = 110f;
        const float minHeightAbovePlayer = 40f;
        const float maxHeightAbovePlayer = 120f;
        const float minDistanceFromPlayer = 75f;
        const float maxDistanceFromPlayer = 120f;
        const float positionStabilization = 0.8f;
        const float smoothingSpeed = 8f;

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

        float lerpFactor;
        if (verticalVelocity > 200f || horizontalSpeed > 300f)
        {
            lerpFactor = 0.45f; // faster when moving very quickly
        }
        else if (horizontalSpeed > 150f)
        {
            lerpFactor = 0.25f;
        }
        else if (verticalVelocity > 50f)
        {
            lerpFactor = 0.12f;
        }
        else
        {
            lerpFactor = 0.06f;
        }

        float effectiveLerp = Math.Clamp(lerpFactor * positionStabilization, 0.05f, 0.35f);

        // Smooth entire vector including Z
        Vector smoothedPos = currentPos.Lerp(targetPos, effectiveLerp);

        if (LastGoodCameraPos.TryGetValue(player.SteamID, out var lastGoodPos))
        {
            float zDiff = smoothedPos.Z - lastGoodPos.Z;

            float zResponse = Math.Clamp(Math.Max(verticalVelocity * 0.1f, 5f), 10f, 80f);
            float maxAllowedZChange = zResponse * timeSinceLastUpdate;

            if (Math.Abs(zDiff) > maxAllowedZChange)
            {
                smoothedPos.Z = lastGoodPos.Z + Math.Sign(zDiff) * maxAllowedZChange;
            }

            if (verticalVelocity < 5f && horizontalSpeed < 50f)
            {
                LastFallbackCameraPos[player.SteamID] = smoothedPos;
            }

            DebugLogger.Log(
                "Z_CHECK",
                $"Z={smoothedPos.Z:F2} | minZ={minZ:F2} | rawZ={targetPos.Z:F2} | zDiff={zDiff:F2} | verticalVel={verticalVelocity:F2} | maxZChange={maxAllowedZChange:F2}",
                player
            );
        }

        smoothedPos.Z = Math.Clamp(smoothedPos.Z, minZ, maxZ);

        Vector toPlayer = playerPos - smoothedPos;
        float currentDistance = toPlayer.Length();
        if (currentDistance < minDistanceFromPlayer || currentDistance > maxDistanceFromPlayer)
        {
            Vector direction = toPlayer.Normalized();
            smoothedPos =
                playerPos
                - direction * Math.Clamp(currentDistance, minDistanceFromPlayer, desiredDistance);
            smoothedPos.Z = Math.Max(smoothedPos.Z, minZ);
        }

        QAngle targetAngle = pawn.V_angle;

        prop.Teleport(smoothedPos, targetAngle, new Vector());
        LastGoodCameraPos[player.SteamID] = smoothedPos;

        DebugLogger.Log(
            "SMOOTH_CAMERA",
            "Following player",
            player,
            new
            {
                smoothedPos,
                targetAngle,
                playerPos = pawn.AbsOrigin,
                distance = (smoothedPos - playerPos).Length(),
            }
        );
    }

    public static bool IsMoving(this CCSPlayerController player)
    {
        var velocity = player.PlayerPawn?.Value?.AbsVelocity;
        if (velocity == null)
            return false;

        // Include vertical movement (falling/jumping)
        return velocity.Length() > 15f || Math.Abs(velocity.Z) > 10f;
    }

    public static Vector CalculateVelocity(Vector positionA, Vector positionB, float timeDuration)
    {
        // Step 1: Determine direction from A to B
        Vector directionVector = positionB - positionA;

        // Step 2: Calculate distance between A and B
        float distance = directionVector.Length();

        // Step 3: Choose a desired time duration for the movement
        // Ensure that timeDuration is not zero to avoid division by zero
        if (timeDuration == 0)
        {
            timeDuration = 1;
        }

        // Step 4: Calculate velocity magnitude based on distance and time
        float velocityMagnitude = distance / timeDuration;

        // Step 5: Normalize direction vector
        if (distance != 0)
        {
            directionVector /= distance;
        }

        // Step 6: Scale direction vector by velocity magnitude to get velocity vector
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

        // Calculate the direction vector of player1 based on yaw angle
        Vector player1Direction = new(MathF.Cos(yawAngleRadians), MathF.Sin(yawAngleRadians), 0);

        // Vector from player1 to player2
        if (player1Pawn.AbsOrigin == null || player2Pawn.AbsOrigin == null)
            return false;

        Vector player1ToPlayer2 = player2Pawn.AbsOrigin - player1Pawn.AbsOrigin;

        // Calculate dot product to determine if player2 is behind player1
        float dotProduct = player1ToPlayer2.Dot(player1Direction);

        // If the dot product is negative, player2 is behind player1
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
            if (pawn.AbsOrigin! == null)
                return safeDistance;

            var checkPos = pawn.AbsOrigin! + backward * d + new Vector(0, 0, verticalOffset - 30f);

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

        // Adjust vertical offset based on pitch
        float pitchFactor =
            1.0f - Math.Clamp(Math.Abs(pitchRadians) / ((float)Math.PI / 2f), 0, 0.5f);
        verticalOffset *= pitchFactor;

        // Calculate eye position
        var eyePos = pawnPos + new Vector(0, 0, verticalOffset);
        var backwardDir = new Vector(-MathF.Cos(yawRadians), -MathF.Sin(yawRadians), 0);

        // Calculate target position
        var targetCamPos = eyePos + backwardDir * desiredDistance;

        // Find ground level
        float minAllowedZ = pawnPos.Z + 20f;
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

        // Obstacle trace
        var trace = TraceRay.GetGameTraceByEyePosition(
            player,
            targetCamPos,
            (ulong)TraceMask.MaskShot
        );

        Vector finalPos;

        if (trace.DidHit())
        {
            Vector hitVec = trace.Position.ToVector();
            float rawDistance = (hitVec - eyePos).Length();
            float hitDistance = Math.Clamp(rawDistance - 5f, 55f, desiredDistance);
            finalPos = eyePos + backwardDir * hitDistance;
        }
        else
        {
            finalPos = targetCamPos;
        }

        // Apply Z constraints
        if (finalPos.Z < minAllowedZ)
        {
            finalPos.Z = minAllowedZ;
        }

        // Smooth Z movement
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
                    finalPos.Z = lastPos.Z; // don’t rise if standing or walking slowly
                }
            }
            else if (Math.Abs(player.PlayerPawn.Value.AbsVelocity.Z) < 5f)
            {
                finalPos.Z = lastPos.Z;
            }
        }

        if ((finalPos - pawnPos).Length() < 10f)
        {
            // fallback to fixed offset to avoid being inside player
            finalPos = pawnPos - new Vector(0, 0, -70f); // drop behind
        }

        LastGoodCameraPos[player.SteamID] = finalPos;
        return finalPos;
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
}

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
using CS2TraceRay.Struct;

namespace ThirdPersonRevamped;

public static class EntityUtilities
{

    private static float GetTimeSeconds() =>
        (float)DateTimeOffset.Now.ToUnixTimeMilliseconds() / 1000f;

    private static bool BlockCamera => ThirdPersonRevamped.BlockCamera;

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

    public static void UpdateCamera(this CDynamicProp _cameraProp, CCSPlayerController player)
    {
        if (player.IsNullOrInvalid() || !_cameraProp.IsValid)
            return;
            
        var pawn = player.PlayerPawn!.Value!;
        float safeDistance = player.CalculateCollisionSafeDistance(90f, 10f, 60f);

        Vector cameraPos = player.CalculateSafeCameraPosition(safeDistance, 90);

        QAngle cameraAngle = player.PlayerPawn.Value!.EyeAngles;

        _cameraProp.Teleport(cameraPos, cameraAngle, new Vector());
    }

    public static void UpdateCameraSmooth(
        this CPointCamera prop,
        CCSPlayerController player,
        float desiredDistance,
        float verticalOffset
    )
    {
        if (player.IsNullOrInvalid() || !prop.IsValid)
            return;

        var pawn = player.PlayerPawn?.Value;
        if (pawn == null || pawn.AbsOrigin == null)
            return;

        Vector targetPos = player.CalculateSafeCameraPosition(desiredDistance, verticalOffset);
        QAngle targetAngle = pawn.V_angle;

        Vector currentPos = prop.AbsOrigin ?? new Vector();

        float lerpFactor = 0.3f;
        
        Vector smoothedPos = currentPos.Lerp(targetPos, lerpFactor);

        prop.Teleport(smoothedPos, targetAngle, new Vector());
    }

    public static bool IsMoving(this CCSPlayerController player)
    {
        var velocity = player.PlayerPawn?.Value?.AbsVelocity;
        if (velocity == null)
            return false;

        return velocity.Length() > 15f || Math.Abs(velocity.Z) > 10f;
    }

    public static Vector CalculatePositionInFront(
        this CCSPlayerController player,
        float offSetXY,
        float offSetZ = 0
    )
    {
        var pawn = player.PlayerPawn?.Value;
        if (pawn?.AbsOrigin == null || pawn.V_angle == null)
            return new Vector(0, 0, 0);

        float yawAngleRadians = (float)(pawn.V_angle.Y * Math.PI / 180.0);
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

        if (player1Pawn.AbsOrigin == null || player2Pawn!.AbsOrigin == null)
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

        float yawRadians = pawn.V_angle!.Y * (float)Math.PI / 180f;
        var backward = new Vector(-MathF.Cos(yawRadians), -MathF.Sin(yawRadians), 0);
        var allPlayers = Utilities.GetPlayers();

        for (float d = checkStep; d <= maxDistance; d += checkStep)
        {
            var checkPos = pawn.AbsOrigin + backward * d + new Vector(0, 0, verticalOffset - 30f);

            var nearbyPlayers = allPlayers.Where(p =>
                p != null
                && p.IsValid
                && p.PlayerPawn.IsValid
                && p.PlayerPawn.Value?.AbsOrigin != null
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
        Vector pawnPos = pawn.AbsOrigin;

        float yawRadians = pawn.V_angle.Y * (float)Math.PI / 180f;
        var backwardDir = new Vector(-MathF.Cos(yawRadians), -MathF.Sin(yawRadians), 0);
        var eyePos = pawnPos + new Vector(0, 0, verticalOffset);
        var targetCamPos = eyePos + backwardDir * desiredDistance;

        Vector finalPos = targetCamPos;

        var trace = TraceRay.GetGameTraceByEyePosition(
            player,
            targetCamPos,
            (ulong)TraceMask.MaskShot
        );

        if (trace.DidHit() && BlockCamera)
        {
            Vector hitVec = trace.Position.ToVector();
            float distanceToWall = (hitVec - eyePos).Length();
            float clampedDistance = Math.Clamp(distanceToWall - 10f, 10f, desiredDistance);
            finalPos = eyePos + backwardDir * clampedDistance;
        }

        return finalPos;
    }

    public static Vector Lerp(this Vector from, Vector to, float t)
    {
        to.X = from.X + (to.X - from.X) * t;
        to.Y = from.Y + (to.Y - from.Y) * t;
        to.Z = from.Z + (to.Z - from.Z) * t;
        return to;
    }
    
    public static Vector ToVector(this System.Numerics.Vector3 v)
    {
        return new Vector(v.X, v.Y, v.Z);
    }

    public static float Clamp(float value, float min, float max)
    {
        if (value < min)
            return min;
        if (value > max)
            return max;
        return value;
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

    public static Vector Normalized(this Vector vec)
    {
        float length = vec.Length();
        return length == 0f ? new Vector(0, 0, 0) : vec / length;
    }
}

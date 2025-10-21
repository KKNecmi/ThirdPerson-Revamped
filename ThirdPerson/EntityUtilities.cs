using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using CS2TraceRay.Class;
using CS2TraceRay.Enum;

namespace ThirdPersonRevamped;

public static class EntityUtilities
{
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

    public static void SetColor(this CDynamicProp? prop, Color colour)
    {
        if (prop != null && prop.IsValid)
        {
            prop.Render = colour;
            Utilities.SetStateChanged(prop, "CBaseModelEntity", "m_clrRender");
        }
    }

    public static void UpdateCamera(
        this CDynamicProp _cameraProp,
        CCSPlayerController player,
        float desiredDistance,
        float verticalOffset
    )
    {
        if (player.IsNullOrInvalid() || !_cameraProp.IsValid)
            return;

        var pawn = player.PlayerPawn.Value;
        if (pawn == null) return;

        Vector cameraPos = player.CalculateSafeCameraPosition(desiredDistance, verticalOffset);
        QAngle cameraAngle = pawn.V_angle;

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

    public static bool IsNullOrInvalid(this CCSPlayerController? player)
    {
        return player == null || !player.IsValid || !player.PlayerPawn.IsValid;
    }
}

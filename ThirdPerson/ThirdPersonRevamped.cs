using System.Diagnostics.SymbolStore;
using System.Drawing;
using System.Text.Json.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.Utils;
using VectorSystem = System.Numerics;

namespace ThirdPersonRevamped
{
    public class ThirdPersonRevamped : BasePlugin, IPluginConfig<Config>
    {
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
                string fullMessage =
                    $"[{DateTime.Now:HH:mm:ss}] [{tag}] [Player: {steamId}] {message}";
                if (data != null)
                    fullMessage += $" | Data: {data}";

                Console.WriteLine(fullMessage);
            }
        }

        public override string ModuleName => "ThirdPersonRevamped";
        public override string ModuleVersion => "1.0.3";
        public override string ModuleAuthor => "Necmi";
        public override string ModuleDescription => "Improved Third Person with smooth camera";

        public Config Config { get; set; } = null!;
        public static Config cfg = null!;

        public void OnConfigParsed(Config config)
        {
            Config = config;
            cfg = config;
        }

        public static Dictionary<CCSPlayerController, bool> mirrorEnabled = new();
        public static Dictionary<CCSPlayerController, QAngle> mirrorAngle = new();
        public static Dictionary<CCSPlayerController, CDynamicProp> thirdPersonPool =
            new Dictionary<CCSPlayerController, CDynamicProp>();
        public static Dictionary<
            CCSPlayerController,
            CPhysicsPropMultiplayer
        > smoothThirdPersonPool = new Dictionary<CCSPlayerController, CPhysicsPropMultiplayer>();

        public static Dictionary<CCSPlayerController, WeaponList> weapons =
            new Dictionary<CCSPlayerController, WeaponList>();

        private static Dictionary<CCSPlayerController, float> lastMirrorUpdateTime = new();
        public static Dictionary<CCSPlayerController, Vector> mirrorPosition = new();

        public override void Load(bool hotReload)
        {
            RegisterListener<Listeners.OnTick>(OnGameFrame);
            RegisterEventHandler<EventRoundStart>(OnRoundStart);
            RegisterEventHandler<EventPlayerHurt>(OnPlayerHurt, HookMode.Pre);

            AddCommand("css_tp", "Allows to use thirdperson", OnTPCommand);
            AddCommand("css_thirdperson", "Allows to use thirdperson", OnTPCommand);
            AddCommand("css_mirror", "Enable Mirror Mode", OnMirrorCommand);
        }

        public void OnGameFrame()
        {
            foreach (var data in smoothThirdPersonPool)
            {
                var player = data.Key;
                var camera = data.Value;

                if (player.IsNullOrInvalid() || !camera.IsValid)
                    continue;

                var now = GetTimeSeconds();

                if (mirrorEnabled.TryGetValue(player, out bool isMirror) && isMirror)
                {
                    var fixedPos = player.CalculateSafeCameraPosition(75f, 40f);
                    var fixedAngle = mirrorAngle.ContainsKey(player)
                        ? mirrorAngle[player]
                        : player.PlayerPawn.Value.EyeAngles;
                    camera.Teleport(fixedPos, fixedAngle, new Vector());
                }
                else
                {
                    camera.UpdateCameraSmooth(player);
                }
            }

            foreach (var data in thirdPersonPool)
            {
                var player = data.Key;
                var camera = data.Value;

                if (player.IsNullOrInvalid() || !camera.IsValid)
                    continue;

                var pawn = player.PlayerPawn.Value;

                if (mirrorEnabled.TryGetValue(player, out bool isMirror) && isMirror)
                {
                    var fixedPos = player.CalculateSafeCameraPosition(75f);
                    var fixedAngle = mirrorAngle.ContainsKey(player)
                        ? mirrorAngle[player]
                        : player.PlayerPawn.Value.V_angle;
                    camera.Teleport(fixedPos, fixedAngle, new Vector());
                }
                else
                {
                    var cameraPos = player.CalculateSafeCameraPosition(90f, 90f);
                    var cameraAngle = player.PlayerPawn.Value.V_angle;
                    camera.Teleport(cameraPos, cameraAngle, new Vector());
                }
            }
        }

        private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
        {
            thirdPersonPool.Clear();
            smoothThirdPersonPool.Clear();
            return HookResult.Continue;
        }

        private HookResult OnPlayerHurt(EventPlayerHurt @event, GameEventInfo info)
        {
            var victim = @event.Userid;

            var attacker = @event.Attacker;

            if (attacker == null || victim == null)
                return HookResult.Continue;

            if (
                thirdPersonPool.ContainsKey(attacker) || smoothThirdPersonPool.ContainsKey(attacker)
            )
            {
                var isInfront = attacker.IsInfrontOfPlayer(victim);
                if (isInfront)
                {
                    victim.PlayerPawn.Value!.Health += @event.DmgHealth;
                    victim.PlayerPawn.Value!.ArmorValue += @event.DmgArmor;
                }
            }

            return HookResult.Continue;
        }

        public void OnTPCommand(CCSPlayerController? caller, CommandInfo command)
        {
            if (Config.UseOnlyAdmin && !AdminManager.PlayerHasPermissions(caller, Config.Flag))
            {
                command.ReplyToCommand(ReplaceColorTags(Config.NoPermission));
                return;
            }

            if (caller == null || !caller.PawnIsAlive)
                return;

            if (Config.UseSmooth)
            {
                SmoothThirdPerson(caller);
            }
            else
            {
                DefaultThirdPerson(caller);
            }
        }

        public void DefaultThirdPerson(CCSPlayerController caller)
        {
            if (!thirdPersonPool.ContainsKey(caller))
            {
                CDynamicProp? _cameraProp = Utilities.CreateEntityByName<CDynamicProp>(
                    "prop_dynamic"
                );

                if (_cameraProp == null)
                    return;

                _cameraProp.DispatchSpawn();
                _cameraProp.SetColor(Color.FromArgb(0, 255, 255, 255));
                _cameraProp.Teleport(
                    caller.CalculatePositionInFront(-110, 90),
                    caller.PlayerPawn.Value!.V_angle,
                    new Vector()
                );

                caller.PlayerPawn.Value!.CameraServices!.ViewEntity.Raw = _cameraProp
                    .EntityHandle
                    .Raw;
                Utilities.SetStateChanged(
                    caller.PlayerPawn.Value!,
                    "CBasePlayerPawn",
                    "m_pCameraServices"
                );

                Utilities.SetStateChanged(
                    caller.PlayerPawn!.Value!,
                    "CBasePlayerPawn",
                    "m_pCameraServices"
                );
                caller.PrintToChat(ReplaceColorTags(Config.Prefix + Config.OnActivated));
                thirdPersonPool.Add(caller, _cameraProp);

                AddTimer(
                    0.5f,
                    () =>
                    {
                        _cameraProp.Teleport(
                            caller.CalculatePositionInFront(-110, 90),
                            caller.PlayerPawn.Value.V_angle,
                            new Vector()
                        );
                    }
                );

                if (Config.StripOnUse)
                {
                    caller.PlayerPawn.Value.WeaponServices!.PreventWeaponPickup = true;

                    if (weapons.ContainsKey(caller))
                        weapons.Remove(caller);

                    var WeaponList = new WeaponList();

                    foreach (var weapon in caller.PlayerPawn.Value!.WeaponServices!.MyWeapons)
                    {
                        if (weapons.ContainsKey(caller))
                            continue;
                        if (WeaponList.weapons.ContainsKey(weapon.Value!.DesignerName!))
                            WeaponList.weapons[weapon.Value!.DesignerName!]++;
                        WeaponList.weapons.Add(weapon.Value!.DesignerName!, 1);
                    }

                    weapons.Add(caller, WeaponList);
                    caller.RemoveWeapons();
                }
            }
            else
            {
                caller!.PlayerPawn!.Value!.CameraServices!.ViewEntity.Raw = uint.MaxValue;
                AddTimer(
                    0.3f,
                    () =>
                        Utilities.SetStateChanged(
                            caller.PlayerPawn!.Value!,
                            "CBasePlayerPawn",
                            "m_pCameraServices"
                        )
                );
                if (thirdPersonPool[caller] != null && thirdPersonPool[caller].IsValid)
                    thirdPersonPool[caller].Remove();
                caller.PrintToChat(ReplaceColorTags(Config.Prefix + Config.OnDeactivated));
                thirdPersonPool.Remove(caller);

                caller.PlayerPawn.Value.WeaponServices!.PreventWeaponPickup = false;

                if (Config.StripOnUse)
                {
                    foreach (var weapon in weapons[caller].weapons)
                    {
                        for (int i = 1; i <= weapon.Value; i++)
                        {
                            caller.GiveNamedItem(weapon.Key);
                        }
                    }
                }
            }
        }

        public void SmoothThirdPerson(CCSPlayerController caller)
        {
            if (!smoothThirdPersonPool.ContainsKey(caller))
            {
                var _cameraProp = Utilities.CreateEntityByName<CPhysicsPropMultiplayer>(
                    "prop_physics_multiplayer"
                );

                if (_cameraProp == null)
                {
                    return;
                }

                _cameraProp.SetModel("models/editor/axis_helper_thick.mdl");
                _cameraProp.DispatchSpawn();
                _cameraProp.SetColor(Color.FromArgb(0, 255, 255, 255));

                _cameraProp.Collision.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_NEVER;
                _cameraProp.Collision.SolidFlags = 12;
                _cameraProp.Collision.SolidType = SolidType_t.SOLID_VPHYSICS;

                var initialPosition = caller.CalculatePositionInFront(-110, 75);
                var viewAngle = caller.PlayerPawn.Value.V_angle;

                _cameraProp.Teleport(initialPosition, viewAngle, new Vector());

                AddTimer(
                    0.1f,
                    () =>
                    {
                        if (_cameraProp.IsValid && caller.IsValid && caller.PlayerPawn.IsValid)
                        {
                            caller.PlayerPawn.Value.CameraServices!.ViewEntity.Raw = _cameraProp
                                .EntityHandle
                                .Raw;
                            Utilities.SetStateChanged(
                                caller.PlayerPawn.Value,
                                "CBasePlayerPawn",
                                "m_pCameraServices"
                            );
                        }
                    }
                );

                smoothThirdPersonPool.Add(caller, _cameraProp);
                caller.PrintToChat(ReplaceColorTags(Config.Prefix + Config.OnActivated));

                if (Config.StripOnUse)
                {
                    caller.PlayerPawn.Value.WeaponServices!.PreventWeaponPickup = true;

                    if (weapons.ContainsKey(caller))
                        weapons.Remove(caller);

                    var WeaponList = new WeaponList();

                    foreach (var weapon in caller.PlayerPawn.Value!.WeaponServices!.MyWeapons)
                    {
                        if (weapon?.Value == null)
                            continue;

                        var name = weapon.Value.DesignerName!;
                        if (WeaponList.weapons.ContainsKey(name))
                            WeaponList.weapons[name]++;
                        else
                            WeaponList.weapons[name] = 1;
                    }

                    weapons[caller] = WeaponList;
                    caller.RemoveWeapons();
                }
            }
            else
            {
                caller.PlayerPawn.Value!.CameraServices!.ViewEntity.Raw = uint.MaxValue;
                AddTimer(
                    0.3f,
                    () =>
                    {
                        Utilities.SetStateChanged(
                            caller.PlayerPawn.Value,
                            "CBasePlayerPawn",
                            "m_pCameraServices"
                        );
                    }
                );

                if (smoothThirdPersonPool[caller].IsValid)
                    smoothThirdPersonPool[caller].Remove();

                smoothThirdPersonPool.Remove(caller);
                caller.PrintToChat(ReplaceColorTags(Config.Prefix + Config.OnDeactivated));
                caller.PlayerPawn.Value.WeaponServices!.PreventWeaponPickup = false;

                if (Config.StripOnUse && weapons.ContainsKey(caller))
                {
                    foreach (var weapon in weapons[caller].weapons)
                    {
                        for (int i = 0; i < weapon.Value; i++)
                        {
                            caller.GiveNamedItem(weapon.Key);
                        }
                    }
                }
            }
        }

        public void OnMirrorCommand(CCSPlayerController? caller, CommandInfo? command = null)
        {
            if (caller == null || !caller.PawnIsAlive)
                return;

            if (Config.UseOnlyAdmin && !AdminManager.PlayerHasPermissions(caller, Config.Flag))
            {
                command?.ReplyToCommand(ReplaceColorTags(Config.NoPermission));
                return;
            }

            if (!thirdPersonPool.ContainsKey(caller) && !smoothThirdPersonPool.ContainsKey(caller))
            {
                caller.PrintToChat(ReplaceColorTags(Config.Prefix + Config.OnWarningMirror));
                return;
            }

            bool isEnabled = mirrorEnabled.ContainsKey(caller) && mirrorEnabled[caller];
            mirrorEnabled[caller] = !isEnabled;

            DebugLogger.Log("MIRROR_CMD", $"Mirror toggled: {mirrorEnabled[caller]}", caller);

            if (mirrorEnabled[caller])
            {
                mirrorAngle[caller] = caller.PlayerPawn.Value.EyeAngles;

                // 🆕 Store the fixed position ONCE
                mirrorPosition[caller] = caller.CalculateSafeCameraPosition(70f, 40f);

                lastMirrorUpdateTime[caller] = GetTimeSeconds();
                DebugLogger.Log("MIRROR_CMD", $"Stored Angle: {mirrorAngle[caller]}", caller);
                caller.PrintToChat(ReplaceColorTags(Config.Prefix + Config.OnActivatedMirror));
            }
            else
            {
                mirrorAngle.Remove(caller);
                caller.PrintToChat(ReplaceColorTags(Config.Prefix + Config.OnDeactivatedMirror));
            }
        }

        private static float GetTimeSeconds()
        {
            return (float)DateTimeOffset.Now.ToUnixTimeMilliseconds() / 1000f;
        }

        public string ReplaceColorTags(string input)
        {
            string[] colorPatterns =
            {
                "{DEFAULT}",
                "{DARKRED}",
                "{LIGHTPURPLE}",
                "{GREEN}",
                "{OLIVE}",
                "{LIME}",
                "{RED}",
                "{GREY}",
                "{YELLOW}",
                "{SILVER}",
                "{BLUE}",
                "{DARKBLUE}",
                "{ORANGE}",
                "{PURPLE}",
            };
            string[] colorReplacements =
            {
                "\x01",
                "\x02",
                "\x03",
                "\x04",
                "\x05",
                "\x06",
                "\x07",
                "\x08",
                "\x09",
                "\x0A",
                "\x0B",
                "\x0C",
                "\x10",
                "\x0E",
            };

            for (var i = 0; i < colorPatterns.Length; i++)
                input = input.Replace(colorPatterns[i], colorReplacements[i]);

            return input;
        }
    }

    public class WeaponList
    {
        public Dictionary<string, int> weapons = new Dictionary<string, int>();
    }

    public class Config : BasePluginConfig
    {
        [JsonPropertyName("OnActivated")]
        public string OnActivated { get; set; } = " | {YELLOW}ThirdPerson {GREEN}Activated";

        [JsonPropertyName("OnDeactivated")]
        public string OnDeactivated { get; set; } = " | {YELLOW}ThirdPerson {RED}Deactivated";

        [JsonPropertyName("OnActivatedMirror")]
        public string OnActivatedMirror { get; set; } = " | {YELLOW}Mirror Mode {GREEN}Activated";

        [JsonPropertyName("OnDeactivatedMirror")]
        public string OnDeactivatedMirror { get; set; } = " | {YELLOW}Mirror Mode {RED}Deactivated";

        [JsonPropertyName("OnWarningMirror")]
        public string OnWarningMirror { get; set; } =
            " | {YELLOW}Mirror Mode {GRAY}requires ThirdPerson to be active!";

        [JsonPropertyName("Prefix")]
        public string Prefix { get; set; } = " [{BLUE}ThirdPerson Revamped";

        [JsonPropertyName("UseOnlyAdmin")]
        public bool UseOnlyAdmin { get; set; } = false;

        [JsonPropertyName("OnlyAdminFlag")]
        public string Flag { get; set; } = "@css/slay";

        [JsonPropertyName("NoPermission")]
        public string NoPermission { get; set; } = "You don't have to access this command.";

        [JsonPropertyName("UseSmoothCam")]
        public bool UseSmooth { get; set; } = true;

        [JsonPropertyName("SmoothCamDuration")]
        public float SmoothDuration { get; set; } = 0.05f;

        [JsonPropertyName("StripOnUse")]
        public bool StripOnUse { get; set; } = false;
    }
}

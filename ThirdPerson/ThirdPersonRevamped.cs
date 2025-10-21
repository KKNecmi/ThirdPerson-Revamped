using System.Drawing;
using System.Text.Json.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

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
        public override string ModuleVersion => "1.0.7";
        public override string ModuleAuthor => "Necmi";
        public override string ModuleDescription => "Improved Third Person with smooth camera";

        public Config Config { get; set; } = null!;

        public void OnConfigParsed(Config config)
        {
            Config = config;
            BlockCamera = config.UseBlockCamera;
        }

        public static Dictionary<CCSPlayerController, CDynamicProp> thirdPersonPool = new Dictionary<CCSPlayerController, CDynamicProp>();
        public static Dictionary<CCSPlayerController, CPointCamera> smoothThirdPersonPool = new();

        public static Dictionary<CCSPlayerController, WeaponList> weapons =
            new Dictionary<CCSPlayerController, WeaponList>();

        public static bool BlockCamera { get; private set; }

        public override void Load(bool hotReload)
        {
            RegisterListener<Listeners.OnTick>(OnGameFrame);
            RegisterEventHandler<EventRoundStart>(OnRoundStart);
            RegisterEventHandler<EventPlayerHurt>(OnPlayerHurt, HookMode.Pre);

            RegisterTPCommands();
        }

        private void RegisterTPCommands()
        {
            AddCommand("css_thirdperson", "Third person", OnTPCommand);

            if (
                !string.IsNullOrEmpty(Config.CustomTPCommand)
                && Config.CustomTPCommand != "thirdperson"
            )
            {
                AddCommand($"css_{Config.CustomTPCommand}", "Third person command", OnTPCommand);
            }
        }

        public void OnGameFrame()
        {
            foreach (var data in smoothThirdPersonPool)
            {
                var player = data.Key;
                var camera = data.Value;

                if (player.IsNullOrInvalid() || !camera.IsValid)
                    continue;

                camera.UpdateCameraSmooth(player, Config.ThirdPersonDistance, Config.ThirdPersonHeight);
            }

            foreach (var data in thirdPersonPool)
            {
                var player = data.Key;
                var camera = data.Value;

                if (player.IsNullOrInvalid() || !camera.IsValid)
                    continue;

                camera.UpdateCamera(player, Config.ThirdPersonDistance, Config.ThirdPersonHeight);
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
                var _cameraProp = Utilities.CreateEntityByName<CPointCamera>("point_camera");
                if (_cameraProp == null)
                {
                    return;
                }

                _cameraProp.DispatchSpawn();

                var initialPosition = caller.CalculatePositionInFront(-110, 75);
                var viewAngle = caller.PlayerPawn.Value?.V_angle;

                _cameraProp.Teleport(initialPosition, viewAngle, new Vector());

                Server.NextFrame(() =>
                {
                    if (_cameraProp.IsValid && caller.IsValid && caller.PlayerPawn.IsValid)
                    {
                        caller.PlayerPawn.Value!.CameraServices!.ViewEntity.Raw = _cameraProp
                            .EntityHandle
                            .Raw;
                        Utilities.SetStateChanged(
                            caller.PlayerPawn.Value,
                            "CBasePlayerPawn",
                            "m_pCameraServices"
                        );
                    }
                });

                smoothThirdPersonPool.Add(caller, _cameraProp);
                caller.PrintToChat(ReplaceColorTags(Config.Prefix + Config.OnActivated));

                if (Config.StripOnUse)
                {
                    caller.PlayerPawn.Value!.WeaponServices!.PreventWeaponPickup = true;

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
}

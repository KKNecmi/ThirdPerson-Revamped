using System.Text.Json.Serialization;
using CounterStrikeSharp.API.Core;

namespace ThirdPersonRevamped;

public class Config : BasePluginConfig
{
    [JsonPropertyName("OnActivated")]
    public string OnActivated { get; set; } = " | {YELLOW}ThirdPerson {GREEN}Activated";

    [JsonPropertyName("OnDeactivated")]
    public string OnDeactivated { get; set; } = " | {YELLOW}ThirdPerson {RED}Deactivated";

    [JsonPropertyName("Prefix")]
    public string Prefix { get; set; } = " [{BLUE}ThirdPerson Revamped";

    [JsonPropertyName("CustomTPCommand")]
    public string CustomTPCommand { get; set; } = "tp";

    [JsonPropertyName("UseOnlyAdmin")]
    public bool UseOnlyAdmin { get; set; } = false;

    [JsonPropertyName("OnlyAdminFlag")]
    public string Flag { get; set; } = "@css/slay";

    [JsonPropertyName("NoPermission")]
    public string NoPermission { get; set; } = "You don't have to access this command.";

    [JsonPropertyName("BlockCamera")]
    public bool UseBlockCamera { get; set; } = true;

    [JsonPropertyName("UseSmoothCam")]
    public bool UseSmooth { get; set; } = true;

    [JsonPropertyName("ThirdPersonDistance")]
    public float ThirdPersonDistance { get; set; } = 110f;

    [JsonPropertyName("ThirdPersonHeight")]
    public float ThirdPersonHeight { get; set; } = 76f;

    [JsonPropertyName("StripOnUse")]
    public bool StripOnUse { get; set; } = false;
}
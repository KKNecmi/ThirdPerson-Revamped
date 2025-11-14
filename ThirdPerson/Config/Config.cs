using System.Text.Json.Serialization;
using CounterStrikeSharp.API.Core;

namespace ThirdPersonRevamped;

public class Config : BasePluginConfig
{
    [JsonPropertyName("CustomTPCommand")]
    public string CustomTPCommand { get; set; } = "tp";

    [JsonPropertyName("UseOnlyAdmin")]
    public bool UseOnlyAdmin { get; set; } = false;

    [JsonPropertyName("OnlyAdminFlag")]
    public string Flag { get; set; } = "@css/slay";

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
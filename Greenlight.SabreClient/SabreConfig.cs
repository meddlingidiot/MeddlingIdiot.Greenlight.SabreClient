using System.Text.Json;
using System.Text.Json.Serialization;

namespace Greenlight.SabreClient;

/// <summary>
/// The sabre, read from a JSON file the user can edit. Written out with the defaults the
/// first time it is missing, so "where do I change the metal" has an answer that does not
/// involve rebuilding anything.
/// </summary>
/// <remarks>
/// Kept in AppData rather than beside the executable: the executable lives under <c>bin</c>,
/// which a rebuild is entitled to delete, and losing somebody's hilts to a rebuild would be
/// its own small betrayal.
/// </remarks>
public sealed class SabreConfig
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Greenlight.Sabres", "sabre.json");

    /// <summary>Where the sabre stands, which way it points, and how big it is.</summary>
    public SabrePlacement Sabre { get; set; } = new();

    /// <summary>
    /// The hilt the sabre wears when there is nothing to report. Gunmetal and pewter: no
    /// colour, nothing claimed.
    /// </summary>
    /// <remarks>
    /// A hilt per state rather than one hilt in four colours, which is the whole trick — the
    /// sabre on the desk is a different sabre when the build is broken, and a shape reads
    /// across a room in a way a colour does not.
    /// </remarks>
    public SabreLook WhenOff { get; set; } = new()
    {
        Hilt = HiltStyle.Curved,
        Metal = "#6E7480",
        Accent = "#A8AEB8",
    };

    /// <summary>Everything passing: the classic hilt, brushed steel and gold.</summary>
    public SabreLook WhenGreen { get; set; } = new()
    {
        Hilt = HiltStyle.Graflex,
        Metal = "#C9CDD4",
        Accent = "#C9A227",
    };

    /// <summary>A pull request waiting on you: brass and bone, standing watch.</summary>
    public SabreLook WhenAmber { get; set; } = new()
    {
        Hilt = HiltStyle.Sentinel,
        Metal = "#B08D57",
        Accent = "#E8E2D0",
    };

    /// <summary>A broken pipeline: the crossguard, dark iron and hot copper.</summary>
    public SabreLook WhenRed { get; set; } = new()
    {
        Hilt = HiltStyle.Crossguard,
        Metal = "#8A8F98",
        Accent = "#B4642E",
    };

    /// <summary>
    /// How much of the screen the sabre may stand on. The work area by default, so a sabre
    /// dragged to the bottom edge does not end up over the Start button.
    /// </summary>
    public AreaChoice Area { get; set; } = AreaChoice.WorkArea;

    /// <summary>Overall opacity, for when the sabre is livelier than you want it to be.</summary>
    public double Opacity { get; set; } = 1.0;

    /// <summary>
    /// How wide the glow around the blade is, as a multiple of the blade itself. Low is a
    /// neon tube; high is the blade lighting up the desktop around it.
    /// </summary>
    public double Glow { get; set; } = 1.0;

    /// <summary>
    /// Draw the hilt when Greenlight is away, rather than nothing at all.
    /// </summary>
    /// <remarks>
    /// On by default, and the same judgement the cars make by parking rather than vanishing:
    /// a blank desktop looks like the app crashed, where a dark hilt looks like what it is.
    /// </remarks>
    public bool ShowHiltWhenOff { get; set; } = true;

    /// <summary>The hilt for a given state. What the canvas asks, every frame.</summary>
    public SabreLook LookFor(BladeState state) => state switch
    {
        BladeState.Green => WhenGreen,
        BladeState.Amber => WhenAmber,
        BladeState.Red => WhenRed,
        _ => WhenOff,
    };

    /// <summary>
    /// Load the file, writing the defaults out first if it is not there. A file that cannot be
    /// read or parsed falls back to the defaults rather than refusing to start: this is a desk
    /// toy, and a stray comma should not cost you the whole thing.
    /// </summary>
    public static SabreConfig Load(string? path = null)
    {
        var file = path ?? DefaultPath;

        try
        {
            if (!File.Exists(file))
            {
                var fresh = new SabreConfig();
                fresh.Save(file);
                return fresh;
            }

            var loaded = JsonSerializer.Deserialize<SabreConfig>(File.ReadAllText(file), Json);
            if (loaded is null) return new SabreConfig();

            // A file written before one of these existed deserializes it as null, and so does
            // a hand edit that deleted a block. Neither should be a crash on the next frame.
            var defaults = new SabreConfig();
            loaded.Sabre ??= defaults.Sabre;
            loaded.WhenOff ??= defaults.WhenOff;
            loaded.WhenGreen ??= defaults.WhenGreen;
            loaded.WhenAmber ??= defaults.WhenAmber;
            loaded.WhenRed ??= defaults.WhenRed;

            loaded.Opacity = Math.Clamp(loaded.Opacity, 0.1, 1.0);
            loaded.Glow = Math.Clamp(loaded.Glow, 0.0, 2.5);

            // Clamped on the way in, not only on the way out. The file is hand-editable, and
            // an anchor of 12 or a blade factor of -3 should give you a sabre you can find and
            // drag back rather than one that is somewhere off the side of the desktop.
            loaded.Sabre.AnchorX = Math.Clamp(loaded.Sabre.AnchorX, 0, 1);
            loaded.Sabre.AnchorY = Math.Clamp(loaded.Sabre.AnchorY, 0, 1);
            loaded.Sabre.Angle = (loaded.Sabre.Angle % 360 + 360) % 360;
            loaded.Sabre.BladeFactor = Math.Clamp(loaded.Sabre.BladeFactor, 0.08, 1.6);
            loaded.Sabre.HiltScale = Math.Clamp(loaded.Sabre.HiltScale, 0.4, 3.0);

            return loaded;
        }
        catch
        {
            return new SabreConfig();
        }
    }

    public void Save(string? path = null)
    {
        var file = path ?? DefaultPath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, JsonSerializer.Serialize(this, Json));
        }
        catch
        {
            // A toy that cannot write its config still runs perfectly well on the defaults.
        }
    }

    /// <summary>
    /// Take on everything from a freshly-read file, in place.
    /// </summary>
    /// <remarks>
    /// Copied into this instance rather than swapping it for the new one: the tray is holding
    /// this object, and it is the tray's menu that has to keep agreeing with the file.
    /// </remarks>
    public void CopyFrom(SabreConfig other)
    {
        Sabre = other.Sabre;
        WhenOff = other.WhenOff;
        WhenGreen = other.WhenGreen;
        WhenAmber = other.WhenAmber;
        WhenRed = other.WhenRed;
        Area = other.Area;
        Opacity = other.Opacity;
        Glow = other.Glow;
        ShowHiltWhenOff = other.ShowHiltWhenOff;
    }
}

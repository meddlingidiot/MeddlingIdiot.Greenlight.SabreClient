using System.Diagnostics;
using System.Runtime.Versioning;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Greenlight.SabreClient;

/// <summary>
/// The mascot in the notification area, and the menu hanging off him: the only part of this
/// toy a person can click without asking for it.
/// </summary>
/// <remarks>
/// <para>
/// The sabre is click-through by design — the overlay covers the whole desktop, so a window
/// that answered the mouse would be a desktop nobody could use. That leaves the tray for
/// everything: every setting in <see cref="SabreConfig"/> that can be changed while the thing
/// is running is reachable from here, arrange mode included, and each change is written
/// straight back to the file, so the menu and the JSON are always the same settings.
/// </para>
/// <para>
/// Avalonia's own <see cref="TrayIcon"/> rather than a tray library, because the sample is
/// meant to be readable — and because a sample that drags in a dependency to draw one icon is
/// making a point nobody asked for.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class SabreTray : IDisposable
{
    private static readonly Uri IconUri = new("avares://Greenlight.SabreClient/Assets/MeddlingIdiot.ico");

    private readonly SabreConfig _config;
    private readonly TrayIcon _tray;
    private readonly NativeMenuItem _status;
    private readonly NativeMenuItem _running;
    private readonly NativeMenuItem _arranging;
    private readonly NativeMenuItem _startup;

    public SabreTray(SabreConfig config)
    {
        _config = config;

        _status = new NativeMenuItem { Header = "Waiting for Greenlight…", IsEnabled = false };

        _running = new NativeMenuItem
        {
            Header = "Sabre out",
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = true,
        };
        _running.Click += (_, _) => SetRunning(!IsRunning?.Invoke() ?? true);

        _arranging = new NativeMenuItem
        {
            Header = "Move it about…",
            ToggleType = MenuItemToggleType.CheckBox,
        };
        _arranging.Click += (_, _) => SetArranging(!(IsArranging?.Invoke() ?? false));

        // Read from the registry rather than from a setting of ours, every time it is shown:
        // the user can turn this off in Task Manager's Startup tab, and a tick remembering
        // what we last wrote would then be telling them the opposite of the truth.
        _startup = Check("Start with Windows",
            WindowsStartup.IsEnabled,
            value => WindowsStartup.Set(value));

        var menu = BuildMenu();

        // The top-level items are not inside a submenu, so nothing else re-ticks them. Only
        // the startup one can actually change behind our back, but it can, and this is the
        // moment to notice.
        menu.Opening += (_, _) => _startup.IsChecked = WindowsStartup.IsEnabled();

        _tray = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(IconUri)),
            ToolTipText = "Greenlight sabre",
            Menu = menu,
            IsVisible = true,
        };

        // The one thing a left click can mean here. There is no main window to open, and a
        // tray icon that does nothing at all when clicked reads as a hung one.
        _tray.Clicked += (_, _) => SetRunning(!IsRunning?.Invoke() ?? true);
    }

    /// <summary>Whether the sabre is currently on the desktop.</summary>
    public Func<bool>? IsRunning { get; set; }

    /// <summary>Put the sabre out, or take it away.</summary>
    public Action<bool>? OnSetRunning { get; set; }

    /// <summary>Whether the sabre can currently be picked up with the mouse.</summary>
    public Func<bool>? IsArranging { get; set; }

    /// <summary>Let the sabre be dragged about, or stop letting it.</summary>
    public Action<bool>? OnSetArranging { get; set; }

    /// <summary>
    /// A setting changed that the overlay can absorb where it stands — which is all of them,
    /// because the sabre is drawn from its placement and its hilts each frame.
    /// </summary>
    public Action? OnConfigChanged { get; set; }

    /// <summary>Re-read the file, for hilts re-forged by hand.</summary>
    public Action? OnReloadConfig { get; set; }

    public Action? OnQuit { get; set; }

    /// <summary>Say what the sabre is doing, in the tooltip and at the top of the menu.</summary>
    public void ShowState(BladeState state, bool building)
    {
        var running = IsRunning?.Invoke() ?? true;

        _status.Header = state switch
        {
            BladeState.Green => building ? "Greenlight: green — building" : "Greenlight: green — all passing",
            BladeState.Amber => building
                ? "Greenlight: yellow — building"
                : "Greenlight: yellow — a pull request wants you",
            BladeState.Red => building ? "Greenlight: red — rebuilding" : "Greenlight: red — a pipeline is broken",
            _ => "Greenlight not running — blade out",
        };

        _running.IsChecked = running;
        _arranging.IsChecked = IsArranging?.Invoke() ?? false;

        _tray.ToolTipText = running
            ? $"Greenlight sabre — {Short(state)}{(building ? ", building" : string.Empty)}"
            : "Greenlight sabre — put away";
    }

    private static string Short(BladeState state) => state switch
    {
        BladeState.Green => "green",
        BladeState.Amber => "yellow",
        BladeState.Red => "red",
        _ => "not connected",
    };

    public void Dispose()
    {
        _tray.IsVisible = false;
        _tray.Dispose();
    }

    private void SetRunning(bool running)
    {
        OnSetRunning?.Invoke(running);
        _running.IsChecked = running;
        _tray.ToolTipText = running ? "Greenlight sabre" : "Greenlight sabre — put away";
    }

    private void SetArranging(bool arranging)
    {
        OnSetArranging?.Invoke(arranging);
        _arranging.IsChecked = IsArranging?.Invoke() ?? arranging;
    }

    private NativeMenu BuildMenu() =>
    [
        _status,
        new NativeMenuItemSeparator(),
        _running,
        _arranging,
        new NativeMenuItemSeparator(),
        Submenu("Which way it points",
            Angle("Straight up", 0),
            Angle("Leaning left", 340),
            Angle("Leaning right", 20),
            Angle("Across the screen", 90)),
        Submenu("Blade length",
            Blade("Short", 0.26),
            Blade("Ordinary", 0.50),
            Blade("Long", 0.75),
            Blade("Ridiculous", 1.10)),
        Submenu("Hilt size",
            Hilt("Small", 0.80),
            Hilt("Ordinary", 1.20),
            Hilt("Large", 1.70),
            Hilt("Enormous", 2.40)),
        Submenu("How much glow",
            Glow("None", 0.0),
            Glow("A little", 0.5),
            Glow("Ordinary", 1.0),
            Glow("Lighting up the room", 1.9)),
        Submenu("How solid",
            Opacity("Solid", 1.0),
            Opacity("Nearly solid", 0.8),
            Opacity("Half there", 0.5),
            Opacity("Barely there", 0.3)),
        Submenu("Where it stands",
            Area("Above the taskbar", AreaChoice.WorkArea),
            Area("The whole screen", AreaChoice.FullScreen)),
        Check("Leave the hilt when it goes dark",
            () => _config.ShowHiltWhenOff,
            value =>
            {
                _config.ShowHiltWhenOff = value;
                Persist();
                OnConfigChanged?.Invoke();
            }),
        _startup,
        new NativeMenuItemSeparator(),
        Item("Edit the sabre…", EditConfig),
        Item("Reload the file", () => OnReloadConfig?.Invoke()),
        new NativeMenuItemSeparator(),
        Item("Quit", () => OnQuit?.Invoke()),
    ];

    // ── the settings ──────────────────────────────────────────────────────────
    // Deliberately not here: which hilt goes with which state. Four hilts, each with two
    // metals and a style, is a menu nobody would finish reading — and it is the one thing
    // somebody will want to sit and fiddle with, which is what a file is for. The menu's job
    // there is just to make the file findable.

    private NativeMenuItem Angle(string header, double degrees) =>
        Choice(header, () => Math.Abs(_config.Sabre.Angle - degrees) < 0.001, () =>
        {
            _config.Sabre.Angle = degrees;
            Persist();
            OnConfigChanged?.Invoke();
        });

    private NativeMenuItem Blade(string header, double factor) =>
        Choice(header, () => Math.Abs(_config.Sabre.BladeFactor - factor) < 0.001, () =>
        {
            _config.Sabre.BladeFactor = factor;
            Persist();
            OnConfigChanged?.Invoke();
        });

    private NativeMenuItem Hilt(string header, double scale) =>
        Choice(header, () => Math.Abs(_config.Sabre.HiltScale - scale) < 0.001, () =>
        {
            _config.Sabre.HiltScale = scale;
            Persist();
            OnConfigChanged?.Invoke();
        });

    private NativeMenuItem Glow(string header, double glow) =>
        Choice(header, () => Math.Abs(_config.Glow - glow) < 0.001, () =>
        {
            _config.Glow = glow;
            Persist();
            OnConfigChanged?.Invoke();
        });

    private NativeMenuItem Opacity(string header, double opacity) =>
        Choice(header, () => Math.Abs(_config.Opacity - opacity) < 0.001, () =>
        {
            _config.Opacity = opacity;
            Persist();
            OnConfigChanged?.Invoke();
        });

    private NativeMenuItem Area(string header, AreaChoice area) =>
        Choice(header, () => _config.Area == area, () =>
        {
            _config.Area = area;
            Persist();
            OnConfigChanged?.Invoke();
        });

    // ── Menu plumbing ─────────────────────────────────────────────────────────
    // Each option asks the config what it should look like when the menu opens rather than
    // being ticked once at startup: the file is editable by hand and reloadable from this very
    // menu, and arrange mode rewrites half of it with the mouse — so anything remembering its
    // own state would start lying almost immediately.

    private static NativeMenuItem Item(string header, Action click)
    {
        var item = new NativeMenuItem { Header = header };
        item.Click += (_, _) => click();
        return item;
    }

    private static NativeMenuItem Submenu(string header, params NativeMenuItem[] items)
    {
        var menu = new NativeMenu();
        foreach (var item in items) menu.Add(item);

        void Retick()
        {
            foreach (var item in items)
                if (item.CommandParameter is Func<bool> isChosen)
                    item.IsChecked = isChosen();
        }

        // Twice, because neither moment is reliable on its own: picking an option has to move
        // the tick off the old one straight away, and opening the menu has to account for the
        // file having been edited behind its back.
        foreach (var item in items) item.Click += (_, _) => Retick();
        menu.Opening += (_, _) => Retick();

        return new NativeMenuItem { Header = header, Menu = menu };
    }

    private static NativeMenuItem Choice(string header, Func<bool> isChosen, Action choose)
    {
        var item = new NativeMenuItem
        {
            Header = header,
            ToggleType = MenuItemToggleType.Radio,
            IsChecked = isChosen(),

            // Parked here rather than in a dictionary: the menu owns its items, and a second
            // collection to keep in step with it is a second thing to get wrong.
            CommandParameter = isChosen,
        };

        item.Click += (_, _) => choose();
        return item;
    }

    private static NativeMenuItem Check(string header, Func<bool> isOn, Action<bool> set)
    {
        var item = new NativeMenuItem
        {
            Header = header,
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = isOn(),
        };

        item.Click += (_, _) =>
        {
            set(!isOn());
            item.IsChecked = isOn();
        };

        return item;
    }

    private void Persist() => _config.Save();

    /// <summary>Open <c>sabre.json</c> in whatever the machine opens JSON with.</summary>
    private void EditConfig()
    {
        try
        {
            // It is written out on first run, but a deleted file should still open something
            // rather than nothing.
            if (!File.Exists(SabreConfig.DefaultPath)) _config.Save();

            Process.Start(new ProcessStartInfo(SabreConfig.DefaultPath) { UseShellExecute = true });
        }
        catch
        {
            // No editor associated with .json, or the shell refused. A desk toy does not get
            // to interrupt anyone over it.
        }
    }
}

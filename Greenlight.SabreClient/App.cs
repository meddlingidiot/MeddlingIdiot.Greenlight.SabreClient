using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Greenlight.Sdk;
using Greenlight.Sdk.Protocol;

namespace Greenlight.SabreClient;

/// <summary>
/// The whole of the Greenlight integration, which is the point of the sample: attach,
/// translate the colour, and never care whether Greenlight is actually there.
/// </summary>
/// <remarks>
/// The tray icon, the overlay and arrange mode are ordinary Avalonia and have nothing to do
/// with Greenlight — the integration is still the twenty-odd lines in
/// <see cref="StartWatchingGreenlight"/>.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class App : Application
{
    private GreenlightClient? _greenlight;
    private SabreWindow? _window;
    private SabreTray? _tray;
    private SabreConfig _config = new();

    /// <summary>
    /// The last thing Greenlight said. Held here rather than only in the scene because the
    /// scene comes and goes — put away, brought back, rebuilt for a re-forged hilt — and a
    /// sabre that came back green after a restart would be the toy lying.
    /// </summary>
    private BladeState _state = BladeState.Off;

    private bool _building;

    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // The overlay is closed and reopened by the tray's put-away/bring-back, and there
            // is no other window — on the default setting, putting the sabre away would quit
            // the whole thing and take the tray icon with it.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _config = SabreConfig.Load();

            // If Windows is set to start this, make sure it is still pointed at the right
            // executable. An update moves the versioned copy out from under an older
            // registration, and the symptom is the sabre silently not coming back one
            // morning — weeks after anybody touched the setting.
            WindowsStartup.Refresh();

            _tray = new SabreTray(_config)
            {
                IsRunning = () => _window is not null,
                OnSetRunning = running =>
                {
                    if (running) ShowSabre();
                    else HideSabre();
                },
                IsArranging = () => _window?.IsArranging ?? false,
                OnSetArranging = SetArranging,
                OnConfigChanged = () => _window?.ApplyConfig(),
                OnReloadConfig = ReloadConfig,
                OnQuit = () => desktop.Shutdown(),
            };

            ShowSabre();
            StartWatchingGreenlight();

            desktop.Exit += async (_, _) =>
            {
                _tray?.Dispose();
                if (_greenlight is not null) await _greenlight.DisposeAsync();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void StartWatchingGreenlight()
    {
        _greenlight = new GreenlightClient();

        // Both of these arrive on a background thread — the SDK says so, loudly, and this is
        // what it means in practice. Touching the scene from the pipe's thread would be a
        // race against the render loop reading the same sabre.
        _greenlight.Changed += (_, e) => Apply(Translate(e.Snapshot.Status), e.Snapshot.IsBuilding);
        _greenlight.AvailabilityChanged += (_, e) =>
        {
            // Anything other than Connected means we have nothing to show, and saying so by
            // putting the blade out is more honest than leaving it lit on stale data.
            if (e.Availability != GreenlightAvailability.Connected) Apply(BladeState.Off, building: false);
        };

        // Deliberately not awaited and deliberately not guarded: StartAsync returns as soon
        // as the background loop is running, and an absent Greenlight is not an error. The
        // hilt sits dark until one turns up, then lights on its own.
        _ = _greenlight.StartAsync();
    }

    private void ShowSabre()
    {
        if (_window is not null) return;

        _window = new SabreWindow(_config, new SabreScene(_config.Sabre))
        {
            Scene = { State = _state, IsBuilding = _building },
        };

        // Arrange mode is a drag against the file: every move, aim and resize is written
        // where it lands, so closing the overlay never loses an arrangement.
        _window.Arranged += (_, _) => _config.Save();
        _window.ArrangingFinished += (_, _) => SetArranging(false);

        _window.Show();
        _tray?.ShowState(_state, _building);
    }

    private void HideSabre()
    {
        // Deliberately before the close: leaving arrange mode is what puts the click-through
        // styles back, and a window destroyed mid-arrange would take the desktop's mouse with
        // it until the sabre was brought out again.
        if (_window is not null) _window.IsArranging = false;

        _window?.Close();
        _window = null;
        _tray?.ShowState(_state, _building);
    }

    private void SetArranging(bool arranging)
    {
        // Nothing to arrange while it is put away, and turning the mode on would leave a
        // closed window holding an interactive overlay nobody can see.
        if (_window is null) return;

        _window.IsArranging = arranging;
        if (!arranging) _config.Save();

        _tray?.ShowState(_state, _building);
    }

    /// <summary>Re-read the file, for hilts re-forged by hand while this was running.</summary>
    private void ReloadConfig()
    {
        _config.CopyFrom(SabreConfig.Load());

        // The placement object is the one thing the overlay cannot absorb: the scene holds the
        // instance it was built with, and a reload hands the config a new one.
        if (_window is null) return;

        HideSabre();
        ShowSabre();
    }

    private static BladeState Translate(GreenlightStatus status) => status switch
    {
        GreenlightStatus.Green => BladeState.Green,
        GreenlightStatus.Yellow => BladeState.Amber,
        GreenlightStatus.Red => BladeState.Red,
        _ => BladeState.Off,
    };

    private void Apply(BladeState state, bool building) =>
        Dispatcher.UIThread.Post(() =>
        {
            _state = state;
            _building = building;

            if (_window is not null)
            {
                _window.Scene.State = state;

                // A build under way pulses the blade rather than recolouring it: Greenlight's
                // own rule is that a broken pipeline stays red while it rebuilds, and a toy
                // that went amber the moment the fix started would be contradicting it.
                _window.Scene.IsBuilding = building;
            }

            _tray?.ShowState(state, building);
        });
}

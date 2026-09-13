using System.Diagnostics;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace Greenlight.SabreClient;

/// <summary>
/// The desktop the sabre stands on: a transparent, always-on-top, click-through window over
/// the whole work area, with the sabre drawn on it.
/// </summary>
/// <remarks>
/// A window over the whole desktop rather than one shrink-wrapped around the sabre, because
/// the sabre can be aimed anywhere and drawn out to any length — a tight window would have to
/// be moved, resized and re-layered on every frame of every drag, and the glow would be
/// clipped at its own edges.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class SabreWindow : Window
{
    private readonly SabreConfig _config;
    private readonly SabreCanvas _canvas;
    private readonly DispatcherTimer _frames;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private TimeSpan _lastFrame;
    private TimeSpan _lastHousekeeping;
    private AreaBounds _area;

    private bool _arranging;
    private SabreGrip _grip;
    private ScenePoint _grabOffset;

    public SabreWindow(SabreConfig config, SabreScene scene)
    {
        _config = config;
        Scene = scene;

        Title = "Greenlight sabre";
        WindowDecorations = WindowDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;

        // Never take the caret out of somebody's editor. Paired with WS_EX_NOACTIVATE, which
        // is what makes a click landing here harmless in the first place.
        ShowActivated = false;

        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];

        _canvas = new SabreCanvas(scene, config) { Opacity = config.Opacity };
        Content = _canvas;

        // 60fps. A desk toy that stutters is worse than no desk toy, and the whole frame is
        // a few dozen filled paths.
        _frames = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _frames.Tick += OnFrame;
    }

    public SabreScene Scene { get; }

    /// <summary>Raised when a drag finishes, so the arrangement can be written to the file.</summary>
    public event EventHandler? Arranged;

    /// <summary>Raised when arrange mode ends by itself — Escape, or a click on nothing.</summary>
    public event EventHandler? ArrangingFinished;

    /// <summary>
    /// Whether the sabres can be picked up. Off, the overlay is furniture; on, it is a window
    /// covering the desktop, and nothing underneath it is clickable — so it is a mode
    /// somebody enters on purpose and leaves quickly.
    /// </summary>
    public bool IsArranging
    {
        get => _arranging;
        set
        {
            if (_arranging == value) return;

            _arranging = value;
            _canvas.IsArranging = value;

            // The blade is held out for the duration, whatever Greenlight says. There is
            // nothing to grab on a sabre that is switched off, and aiming it is the point.
            Scene.ForceLit = value;
            if (value) Scene.SnapLit();

            ClickThroughNative.SetInteractive(TryGetPlatformHandle()?.Handle ?? IntPtr.Zero, value);

            if (value) Activate();
            else
            {
                _grip = SabreGrip.None;
                _canvas.IsHeld = false;
                Cursor = Cursor.Default;
            }
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // Both of these need a real window handle, so neither can run before it is shown.
        ClickThroughNative.Apply(TryGetPlatformHandle()?.Handle ?? IntPtr.Zero);
        LayOutDesktop();

        _lastFrame = _clock.Elapsed;
        _frames.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _frames.Stop();
        base.OnClosed(e);
    }

    /// <summary>
    /// Take up a setting changed from the tray while the sabre is out: how solid it is, how
    /// much glow it throws, and how much of the screen it has.
    /// </summary>
    /// <remarks>
    /// The config object is shared with the tray, so there is nothing to pass — this is the
    /// overlay being told to go and look again.
    /// </remarks>
    public void ApplyConfig()
    {
        _canvas.Opacity = _config.Opacity;
        LayOutDesktop();
    }

    /// <summary>
    /// Put the window over the desktop and tell the scene how big it is. Re-run when the
    /// screen changes size or the taskbar moves.
    /// </summary>
    public void LayOutDesktop()
    {
        var area = DesktopArea.Find(_config.Area);
        if (area.IsEmpty) return;

        var scaling = RenderScaling <= 0 ? 1 : RenderScaling;

        // Position is physical, Width and Height are logical. Mixing those up puts the
        // overlay at a plausible-looking but wrong size on every scaled display, which is
        // most of them.
        Position = new PixelPoint(area.X, area.Y);
        Width = area.Width / scaling;
        Height = area.Height / scaling;

        _area = area;
        Scene.Resize(Width, Height);
    }

    // ── arrange mode ──────────────────────────────────────────────────────────
    // Handled on the window rather than the canvas: the canvas is not hit-testable, because
    // the rest of the time this window is something the mouse is supposed to pass through.

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsArranging) return;

        var point = At(e);
        var grip = Scene.HitTest(point);

        if (grip == SabreGrip.None)
        {
            // A click on bare desktop is the obvious way to say "done", and it saves people
            // going back to the tray for a mode they only entered to nudge the sabre.
            ArrangingFinished?.Invoke(this, EventArgs.Empty);
            return;
        }

        _grip = grip;
        _canvas.IsHeld = true;

        // The hilt is carried by the point it was grabbed at rather than snapping its pommel
        // to the cursor: a sabre that jumps out from under the mouse on mouse-down is the
        // difference between dragging something and flinging it.
        _grabOffset = grip == SabreGrip.Hilt ? point - Scene.Measure().Pommel : default;

        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!IsArranging) return;

        var point = At(e);

        if (_grip == SabreGrip.None)
        {
            // Nothing held: the cursor is the only thing saying what is grabbable, since
            // there is no other feedback on a window with no controls in it.
            Cursor = new Cursor(Scene.HitTest(point) switch
            {
                SabreGrip.Hilt => StandardCursorType.SizeAll,
                SabreGrip.Blade => StandardCursorType.Hand,
                _ => StandardCursorType.Arrow,
            });
            return;
        }

        if (_grip == SabreGrip.Hilt) Scene.MoveTo(point - _grabOffset);
        else Scene.AimAt(point, snapAngle: e.KeyModifiers.HasFlag(KeyModifiers.Shift));

        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_grip == SabreGrip.None) return;

        _grip = SabreGrip.None;
        _canvas.IsHeld = false;
        e.Pointer.Capture(null);

        // Written on release rather than on every frame of the drag: the file would otherwise
        // take a few hundred writes to move one sabre across the desk.
        Arranged?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (!IsArranging) return;

        if (Scene.HitTest(At(e)) == SabreGrip.None) return;

        Scene.Rescale(Math.Sign(e.Delta.Y) * 0.08);
        Arranged?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!IsArranging || e.Key != Key.Escape) return;

        ArrangingFinished?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private ScenePoint At(PointerEventArgs e)
    {
        var p = e.GetPosition(this);
        return new ScenePoint(p.X, p.Y);
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var now = _clock.Elapsed;
        var elapsed = now - _lastFrame;
        _lastFrame = now;

        // Once a second: re-claim the top of the z-order, and notice if the screen or the
        // taskbar has changed shape. Cheap, and much less code than listening for every way
        // Windows has of mentioning either.
        if (now - _lastHousekeeping >= TimeSpan.FromSeconds(1))
        {
            _lastHousekeeping = now;

            ClickThroughNative.KeepOnTop(TryGetPlatformHandle()?.Handle ?? IntPtr.Zero);

            if (DesktopArea.Find(_config.Area) != _area) LayOutDesktop();
        }

        Scene.Advance(elapsed);
        _canvas.InvalidateVisual();
    }
}

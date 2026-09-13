namespace Greenlight.SabreClient;

/// <summary>What the sabre is doing, which is Greenlight's aggregate colour by another name.</summary>
public enum BladeState
{
    /// <summary>
    /// No Greenlight attached, or it has nothing to say. The blade goes out and leaves the
    /// hilt sitting there — a lit blade on a four-minute-old snapshot would be the toy lying
    /// to you.
    /// </summary>
    Off,

    /// <summary>Everything passing.</summary>
    Green,

    /// <summary>A pull request wants you. Greenlight's yellow.</summary>
    Amber,

    /// <summary>A pipeline is broken.</summary>
    Red,
}

/// <summary>
/// The shape of the hilt. Silhouette and metalwork only — which one is on the desk at any
/// moment is <see cref="BladeState"/>'s to decide.
/// </summary>
public enum HiltStyle
{
    /// <summary>Ribbed field-repair hilt: stacked grip rings, a side clamp, a flat pommel.</summary>
    Graflex,

    /// <summary>Vented quillon housing, with two short crossguard blades at the throat.</summary>
    Crossguard,

    /// <summary>A curved duelling hilt, bowed along its length so it sits in the hand at an angle.</summary>
    Curved,

    /// <summary>Long, tapered, wrapped in a spiral of banding from pommel to emitter.</summary>
    Sentinel,

    /// <summary>Fluted, ringed the whole way along, with a jewelled bulb for a pommel.</summary>
    Duelist,
}

/// <summary>A point in the overlay's logical pixels.</summary>
public readonly record struct ScenePoint(double X, double Y)
{
    public static ScenePoint operator +(ScenePoint a, ScenePoint b) => new(a.X + b.X, a.Y + b.Y);

    public static ScenePoint operator -(ScenePoint a, ScenePoint b) => new(a.X - b.X, a.Y - b.Y);

    public static ScenePoint operator *(ScenePoint a, double k) => new(a.X * k, a.Y * k);

    public double Length => Math.Sqrt(X * X + Y * Y);
}

/// <summary>What the mouse is over, in arrange mode.</summary>
public enum SabreGrip
{
    /// <summary>Nothing. The click belongs to whatever is underneath.</summary>
    None,

    /// <summary>The hilt itself — drag to carry the sabre around.</summary>
    Hilt,

    /// <summary>The blade — drag to aim it, and to draw it out longer or shorter.</summary>
    Blade,
}

/// <summary>
/// One hilt, forged: its shape and its two metals. There is a separate one of these for each
/// state, which is how the sabre on the desk changes when the build does.
/// </summary>
public sealed class SabreLook
{
    public HiltStyle Hilt { get; set; } = HiltStyle.Graflex;

    /// <summary>The body metal. Any hex Avalonia can parse — <c>#C9CDD4</c>, <c>#CCC9CDD4</c>.</summary>
    public string Metal { get; set; } = "#C9CDD4";

    /// <summary>The inlay: rings, filigree, rivets, and the jewel in a pommel that has one.</summary>
    public string Accent { get; set; } = "#C9A227";
}

/// <summary>
/// Where the sabre stands, which way it points, and how big it is. One of these, because
/// there is one sabre — the hilt changes underneath it, but it does not move house when it
/// does.
/// </summary>
/// <remarks>
/// A mutable class rather than a record, because arrange mode edits it in place while the
/// tray menu and the file are both looking at the same instance — the same arrangement
/// <see cref="SabreConfig"/> relies on when it writes a drag straight back to disk.
/// </remarks>
public sealed class SabrePlacement
{
    /// <summary>
    /// Where the butt of the hilt sits, as a fraction of the overlay: 0 is the left or top
    /// edge, 1 the right or the bottom.
    /// </summary>
    /// <remarks>
    /// A fraction rather than a pixel so the sabre survives the screen it was placed on — a
    /// laptop undocked from a 4K monitor would otherwise find it jammed in the top-left
    /// corner.
    /// </remarks>
    public double AnchorX { get; set; } = 0.5;

    public double AnchorY { get; set; } = 0.97;

    /// <summary>Which way the blade points, in degrees clockwise from straight up.</summary>
    public double Angle { get; set; }

    /// <summary>How far the blade runs out, as a fraction of the overlay's height.</summary>
    public double BladeFactor { get; set; } = 0.5;

    /// <summary>How big the hilt is. 1.0 is the ordinary size for the screen it is on.</summary>
    public double HiltScale { get; set; } = 1.2;
}

/// <summary>
/// Every measurement of the sabre at a given overlay size: where the pommel is, where the
/// blade leaves the emitter, and how far its tip has got.
/// </summary>
/// <remarks>
/// Separate from both the drawing and the placement on purpose. The canvas needs it to draw,
/// and arrange mode needs exactly the same numbers to work out what the mouse is over — two
/// copies of this arithmetic would be two chances for the thing you can grab to sit somewhere
/// other than the thing you can see.
/// </remarks>
public readonly record struct SabreLayout(
    ScenePoint Pommel,
    ScenePoint Emitter,
    ScenePoint Tip,
    ScenePoint FullTip,
    ScenePoint Direction,
    double HiltLength,
    double HiltWidth,
    double BladeLength,
    double BladeWidth);

/// <summary>
/// The sabre itself: where it stands, which hilt it is currently wearing, how far the blade
/// has run out, and what the mouse is over when you are moving it. Deliberately free of
/// Avalonia — it is all arithmetic, so it can be tested without a window, which is the only
/// way the changeover and the arrange-mode maths were ever going to be checkable.
/// </summary>
public sealed class SabreScene
{
    /// <summary>Seconds for the blade to run all the way out, or all the way back in.</summary>
    private const double IgnitionSeconds = 0.42;

    /// <summary>
    /// Seconds the hilt sits dark between one blade going in and the next coming out.
    /// </summary>
    /// <remarks>
    /// The whole point of the beat. A hilt that changed shape under a lit blade would read as
    /// a glitch; a hilt swapped while the blade is in reads as somebody having drawn a
    /// different sabre, which is what the toy is trying to say.
    /// </remarks>
    private const double ChangeoverPause = 0.16;

    /// <summary>
    /// Seconds for one full breath of the build pulse — down and back up again.
    /// </summary>
    /// <remarks>
    /// Four seconds is about fifteen breaths a minute, which is roughly a person sitting
    /// still. That is the point of the number: at this rate the sabre never catches the eye,
    /// it is only ever noticed by somebody who was already looking at it — which is exactly
    /// what "a build is running" deserves and no more.
    /// </remarks>
    private const double PulseSeconds = 4.0;

    /// <summary>
    /// How far down the pulse dips.
    /// </summary>
    /// <remarks>
    /// Shallow on purpose. This lands almost entirely in the halo rather than the blade — the
    /// core barely moves at all — so it reads as the glow swelling and settling rather than
    /// as the blade dimming, and "dimming" is uncomfortably close to "going out", which
    /// already means something else here.
    /// </remarks>
    private const double PulseDepth = 0.16;

    /// <summary>Hilt length as a fraction of the overlay's height, at a scale of 1.</summary>
    private const double HiltHeightFactor = 0.085;

    private readonly Random _random;

    private BladeState _state = BladeState.Off;
    private double _pulsePhase;
    private double _pause;

    public SabreScene(SabrePlacement placement, int? randomSeed = null)
    {
        Placement = placement;
        _random = randomSeed is null ? new Random() : new Random(randomSeed.Value);
    }

    public SabrePlacement Placement { get; }

    public double Width { get; private set; } = 1920;

    public double Height { get; private set; } = 1080;

    /// <summary>
    /// What Greenlight last said. Setting it starts a changeover: the blade goes in, the hilt
    /// is swapped while it is dark, and the new one lights in the new colour.
    /// </summary>
    public BladeState State
    {
        get => _state;
        set
        {
            if (value == _state) return;
            _state = value;

            // Nothing on screen to put away, so there is nothing to wait for and no dark beat
            // worth showing — this is the first snapshot after a cold start, and the sabre
            // should simply light.
            if (Ignition <= 0)
            {
                Shown = value;
                _pause = 0;
            }
            else
            {
                _pause = ChangeoverPause;
            }
        }
    }

    /// <summary>
    /// The state the sabre is currently wearing — the hilt on the desk and the colour in the
    /// blade. It lags <see cref="State"/> for as long as the changeover takes.
    /// </summary>
    /// <remarks>
    /// This is what makes a blade retract in the colour it was lit in. Drawing from
    /// <see cref="State"/> instead would recolour the blade on its way down, which reads as
    /// the status having changed half a second before the blade vanished.
    /// </remarks>
    public BladeState Shown { get; private set; } = BladeState.Off;

    /// <summary>Whether the sabre is mid-swap: blade going in, or hilt sitting dark before it lights.</summary>
    public bool IsChangingOver => Shown != _state;

    /// <summary>A build is running. The blade breathes rather than recolours — Greenlight's own rule.</summary>
    public bool IsBuilding { get; set; }

    /// <summary>
    /// Hold the blade out regardless of what Greenlight says. Arrange mode turns this on: a
    /// blade you cannot see is a blade you cannot grab, and aiming it is the whole point of
    /// the mode.
    /// </summary>
    public bool ForceLit { get; set; }

    /// <summary>
    /// How much of the blade is out, 0 to 1. Animated rather than switched, because a blade
    /// that simply appears at full length reads as a drawing bug and not as an ignition.
    /// </summary>
    public double Ignition { get; private set; }

    /// <summary>
    /// A small waver on the blade's length, well under a pixel of meaning. A perfectly still
    /// blade looks printed on.
    /// </summary>
    public double Flicker { get; private set; } = 1.0;

    /// <summary>
    /// Where in the breath we are: 1 at the top, 0 at the bottom, and a flat 1 when nothing
    /// is building.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Brightness"/> because the two are spent differently. Alpha
    /// alone is a poor way to say "breathing" on a thin, saturated line — the eye reads a
    /// change in size far more readily than a change in brightness, so the canvas puts most of
    /// this into how far the halo reaches and only a little into how bright it is.
    /// </remarks>
    public double Pulse => IsBuilding ? 0.5 * (1 + Math.Cos(_pulsePhase * 2 * Math.PI)) : 1;

    /// <summary>
    /// How bright the blade is this frame: 1 at rest, easing down and back up while a build
    /// runs.
    /// </summary>
    public double Brightness => 1 - PulseDepth * (1 - Pulse);

    public void Resize(double width, double height)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
    }

    /// <summary>Move the scene on by one frame.</summary>
    public void Advance(TimeSpan elapsed)
    {
        // A frame that arrives after the machine has been asleep, or after a breakpoint, is
        // worth clamping: a two-minute step would run the blade out and finish the breath
        // inside one frame, which looks like a glitch rather than like time passing.
        var dt = Math.Clamp(elapsed.TotalSeconds, 0, 0.25);
        if (dt <= 0) return;

        _pulsePhase = (_pulsePhase + dt / PulseSeconds) % 1.0;

        if (IsChangingOver)
        {
            // In, wait, then swap. The ignition back out is the ordinary path below, on the
            // frame after the hilt has changed.
            if (Ignition > 0) Ignition = Math.Max(0, Ignition - dt / IgnitionSeconds);
            else if (_pause > 0) _pause = Math.Max(0, _pause - dt);
            else Shown = _state;
        }
        else if (ForceLit || Shown != BladeState.Off)
        {
            Ignition = Math.Min(1, Ignition + dt / IgnitionSeconds);
        }
        else
        {
            Ignition = Math.Max(0, Ignition - dt / IgnitionSeconds);
        }

        // Only while it is actually out. A waver on a blade of no length would make a
        // retracted sabre twitch.
        Flicker = Ignition > 0 ? 1 + (_random.NextDouble() - 0.5) * 0.012 : 1;
    }

    /// <summary>Run the blade straight out, with no ignition. For entering arrange mode.</summary>
    public void SnapLit()
    {
        Shown = _state;
        _pause = 0;
        Ignition = 1;
    }

    /// <summary>Every measurement of the sabre at the current overlay size.</summary>
    public SabreLayout Measure()
    {
        var hiltLength = Math.Clamp(Height * HiltHeightFactor * Placement.HiltScale, 34, 320);
        var hiltWidth = hiltLength * 0.22;

        var fullBlade = Math.Max(hiltLength * 0.6, Height * Placement.BladeFactor);
        var bladeWidth = Math.Clamp(hiltWidth * 0.52, 4, 26);

        // Straight up is 0 and the screen's Y grows downwards, so the direction runs from -Y
        // round towards +X. Getting this backwards puts the sabre upside down and drags it
        // the wrong way in arrange mode, which is the same mistake showing twice.
        var radians = Placement.Angle * Math.PI / 180;
        var direction = new ScenePoint(Math.Sin(radians), -Math.Cos(radians));

        var pommel = new ScenePoint(Placement.AnchorX * Width, Placement.AnchorY * Height);
        var emitter = pommel + direction * hiltLength;
        var extended = fullBlade * Ignition * Flicker;

        return new SabreLayout(
            pommel,
            emitter,
            emitter + direction * extended,
            emitter + direction * fullBlade,
            direction,
            hiltLength,
            hiltWidth,
            extended,
            bladeWidth);
    }

    /// <summary>What is under the mouse. Arrange mode uses it for both the cursor and the drag.</summary>
    public SabreGrip HitTest(ScenePoint point)
    {
        var layout = Measure();

        if (DistanceToSegment(point, layout.Pommel, layout.Emitter) <= layout.HiltWidth) return SabreGrip.Hilt;

        // Measured against the blade's full length rather than how far it has actually run
        // out: arrange mode holds the blade out anyway, and grabbing it should not depend on
        // catching it mid-ignition.
        var reach = Math.Max(layout.BladeWidth * 2.2, 12);
        return DistanceToSegment(point, layout.Emitter, layout.FullTip) <= reach ? SabreGrip.Blade : SabreGrip.None;
    }

    /// <summary>
    /// Carry the sabre somewhere else. <paramref name="pommel"/> is where the butt of the hilt
    /// should end up.
    /// </summary>
    public void MoveTo(ScenePoint pommel)
    {
        // The hilt is clamped to the overlay, not the blade: a hilt dragged off the edge is a
        // hilt you cannot get back, while a blade running off the screen is a perfectly
        // ordinary thing to want.
        Placement.AnchorX = Math.Clamp(pommel.X / Width, 0, 1);
        Placement.AnchorY = Math.Clamp(pommel.Y / Height, 0, 1);
    }

    /// <summary>
    /// Aim the sabre at a point and draw its blade out to reach it: the angle from the
    /// direction, the length from the distance less the hilt.
    /// </summary>
    /// <param name="snapAngle">Round the angle to the nearest 15°, for a sabre meant to stand straight.</param>
    public void AimAt(ScenePoint target, bool snapAngle = false)
    {
        var layout = Measure();
        var reach = target - layout.Pommel;
        var distance = reach.Length;

        // Right on top of the pommel there is no direction to read, and Atan2(0, 0) would
        // quietly snap the sabre bolt upright on the way past.
        if (distance < 1) return;

        var degrees = Math.Atan2(reach.X, -reach.Y) * 180 / Math.PI;
        Placement.Angle = snapAngle ? Snap(degrees) : (degrees % 360 + 360) % 360;
        Placement.BladeFactor = Math.Clamp((distance - layout.HiltLength) / Height, 0.08, 1.6);
    }

    /// <summary>Make the hilt bigger or smaller. The blade keeps the length it was given.</summary>
    public void Rescale(double delta) =>
        Placement.HiltScale = Math.Clamp(Placement.HiltScale + delta, 0.4, 3.0);

    /// <summary>Round an angle to the nearest step, and bring it back into 0–360.</summary>
    public static double Snap(double degrees, double step = 15)
    {
        if (step <= 0) return (degrees % 360 + 360) % 360;
        return (Math.Round(degrees / step) * step % 360 + 360) % 360;
    }

    private static double DistanceToSegment(ScenePoint p, ScenePoint a, ScenePoint b)
    {
        var ab = b - a;
        var lengthSquared = ab.X * ab.X + ab.Y * ab.Y;

        // A zero-length segment is a point, which is what a hilt scaled to nothing and an
        // unlit blade both are. Dividing by it would hand back NaN, and NaN compares false
        // against every threshold — so the sabre would silently stop answering the mouse.
        if (lengthSquared <= double.Epsilon) return (p - a).Length;

        var ap = p - a;
        var t = Math.Clamp((ap.X * ab.X + ap.Y * ab.Y) / lengthSquared, 0, 1);
        return (p - (a + ab * t)).Length;
    }
}

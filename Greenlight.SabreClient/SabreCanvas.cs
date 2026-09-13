using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Greenlight.SabreClient;

/// <summary>
/// Draws the sabres. Everything comes from the scene's numbers each frame — there are no
/// control instances per sabre, because three sabres' worth of layout and hit testing would
/// be three sabres' worth of work to no purpose on a window nobody can click.
/// </summary>
/// <remarks>
/// Every hilt is drawn in its own space: the origin at the butt of the hilt, the sabre
/// running along <c>+X</c>. That is what makes the metalwork writable at all — a fluted
/// grip or a row of rivets is an ordinary run of rectangles along a line, and the rotation
/// is the transform's problem rather than the trigonometry's.
/// </remarks>
public sealed class SabreCanvas : Control
{
    /// <summary>
    /// How far the halo draws in at the bottom of a build's breath, as a fraction of its
    /// resting reach.
    /// </summary>
    private const double GlowSwell = 0.22;

    /// <summary>
    /// How far the blade and its core draw in over the same breath.
    /// </summary>
    /// <remarks>
    /// Much shallower than the halo, and deliberately so: the halo is soft-edged and can lose
    /// a fifth of its reach without anyone reading it as a fault, where the core is a hard
    /// bright line against the desktop and the same treatment would have the blade visibly
    /// thinning and fattening — which is uncomfortably close to going out, and going out
    /// already means something else here.
    /// </remarks>
    private const double BladeSwell = 0.10;

    private static readonly Color GreenBlade = Color.FromRgb(64, 232, 124);
    private static readonly Color AmberBlade = Color.FromRgb(255, 176, 48);
    private static readonly Color RedBlade = Color.FromRgb(255, 62, 52);

    private static readonly IBrush GrooveBrush = new SolidColorBrush(Color.FromArgb(190, 16, 17, 20));
    private static readonly IBrush ShadowBrush = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0));

    private static readonly IBrush ArrangeVeil = new SolidColorBrush(Color.FromArgb(66, 6, 8, 14));
    private static readonly IBrush ArrangeCard = new SolidColorBrush(Color.FromArgb(226, 30, 34, 44));
    private static readonly IPen ArrangeCardEdge = new Pen(new SolidColorBrush(Color.FromArgb(70, 190, 208, 236)), 1);
    private static readonly IBrush ArrangeText = new SolidColorBrush(Color.FromRgb(232, 236, 244));
    private static readonly IPen ArrangeHandle = new Pen(new SolidColorBrush(Color.FromArgb(180, 210, 228, 255)), 1.4)
    {
        DashStyle = new DashStyle([3, 3], 0),
    };

    /// <summary>
    /// Brushes are cached per colour rather than rebuilt per frame: at 60fps a
    /// <see cref="Color.Parse"/> and a fresh gradient per hilt is pure waste, and a bad hex
    /// in a hand-edited file should degrade to a visible sabre rather than an exception
    /// sixty times a second.
    /// </summary>
    private readonly Dictionary<string, IBrush> _metals = [];

    private readonly Dictionary<string, IBrush> _accents = [];
    private readonly Dictionary<string, IBrush> _flats = [];

    private readonly SabreConfig _config;

    public SabreCanvas(SabreScene scene, SabreConfig config)
    {
        Scene = scene;
        _config = config;
        IsHitTestVisible = false;
    }

    public SabreScene Scene { get; }

    /// <summary>
    /// Whether the sabre is being moved about. Draws the veil, the handles and the crib
    /// sheet, none of which belong on screen the rest of the time.
    /// </summary>
    public bool IsArranging { get; set; }

    /// <summary>Whether it is being dragged right now, so its handles can be picked out.</summary>
    public bool IsHeld { get; set; }

    public override void Render(DrawingContext context)
    {
        if (Bounds.Width <= 1 || Bounds.Height <= 1) return;

        if (IsArranging)
        {
            // Something to see the sabre against, and something for the eye to read as a mode
            // rather than as the desktop having gone strange.
            context.FillRectangle(ArrangeVeil, new Rect(Bounds.Size));
        }

        // Everything is drawn from what the sabre is currently WEARING, not from what
        // Greenlight last said. The two differ for the length of a changeover, and that gap
        // is the effect: the blade goes in still showing the old colour, the hilt is swapped
        // in the dark, and the new one lights.
        var shown = Scene.Shown;
        var look = _config.LookFor(shown);
        var layout = Scene.Measure();

        // The hilt dims when there is nothing to report rather than vanishing: a bare desktop
        // looks like the app died, where a dark hilt looks like what it is.
        var lit = Scene.ForceLit || shown != BladeState.Off;
        var hiltOpacity = lit ? 1.0 : _config.ShowHiltWhenOff ? 0.42 : 0.0;

        if (hiltOpacity > 0 || layout.BladeLength > 0)
        {
            using var _ = context.PushTransform(LocalSpace(layout));

            if (layout.BladeLength > 0) DrawBlade(context, layout, look, BladeColor(shown));
            if (hiltOpacity > 0) DrawHilt(context, layout, look, hiltOpacity);
            if (IsArranging) DrawHandles(context, layout);
        }

        if (IsArranging) DrawCribSheet(context);
    }

    /// <summary>
    /// The matrix that puts the origin at the butt of the hilt with the sabre running along
    /// <c>+X</c>. Everything below is drawn in that space.
    /// </summary>
    private static Matrix LocalSpace(SabreLayout layout) =>
        Matrix.CreateRotation(Math.Atan2(layout.Direction.Y, layout.Direction.X))
        * Matrix.CreateTranslation(layout.Pommel.X, layout.Pommel.Y);

    // ── the blade ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Six passes, widest and faintest first. A blade is not a coloured line: it is a white
    /// core that has bleached out its own colour, wrapped in the colour, wrapped in as much
    /// halo as the desk can stand.
    /// </summary>
    /// <remarks>
    /// The halo is four flat strokes rather than a gradient because a gradient brush along a
    /// rotated line is a transform and a bounding box per blade per frame. Four steps is
    /// where the banding stops being visible; three is not.
    /// </remarks>
    private void DrawBlade(DrawingContext context, SabreLayout layout, SabreLook look, Color color)
    {
        var start = new Point(layout.HiltLength, 0);
        var end = new Point(layout.HiltLength + layout.BladeLength, 0);

        // The pulse is the whole of "a build is running": Greenlight's own rule is that a
        // broken pipeline stays red while it rebuilds, so the brightness moves and the
        // colour does not.
        var brightness = Scene.Brightness;
        var glow = _config.Glow;
        var w = layout.BladeWidth;

        // The breath runs through the whole blade, but not evenly: most of it goes into how
        // far the halo reaches, because a fifth off the radius of something soft-edged is
        // plainly visible from the corner of an eye where a fifth off its alpha is not. The
        // blade and the core take the same breath at half the depth, so the thing swells as
        // one piece rather than as a bright line inside a moving cloud.
        var reach = 1 - GlowSwell * (1 - Scene.Pulse);
        var swell = 1 - BladeSwell * (1 - Scene.Pulse);

        Stroke(color, 0.055 * glow * brightness, w * 6.4 * reach);
        Stroke(color, 0.085 * glow * brightness, w * 4.6 * reach);
        Stroke(color, 0.13 * glow * brightness, w * 3.2 * reach);
        Stroke(color, 0.22 * glow * brightness, w * 2.2 * reach);
        Stroke(color, 0.88 * brightness, w * 1.35 * swell);

        // The core breathes with the rest of it, in width and in brightness. It is kept the
        // brightest thing on screen throughout — a core that dipped as far as the halo does
        // would read as the blade faltering rather than as a build running.
        Stroke(Colors.White, 0.92 * brightness, w * 0.5 * swell);

        // The emitter takes the light it is throwing, which is what stops the blade looking
        // like it has been laid on top of the hilt.
        context.DrawEllipse(
            new SolidColorBrush(color, 0.34 * glow * brightness), null,
            start, w * 1.9 * reach, w * 1.9 * reach);

        if (look.Hilt == HiltStyle.Crossguard) DrawQuillons(context, layout, color, brightness);

        void Stroke(Color c, double opacity, double thickness)
        {
            if (opacity <= 0.004 || thickness <= 0) return;

            var pen = new Pen(new SolidColorBrush(c, Math.Clamp(opacity, 0, 1)), thickness)
            {
                LineCap = PenLineCap.Round,
            };

            context.DrawLine(pen, start, end);
        }
    }

    /// <summary>The two short blades out of the vents, at the throat of a crossguard hilt.</summary>
    private void DrawQuillons(DrawingContext context, SabreLayout layout, Color color, double brightness)
    {
        var x = layout.HiltLength * 0.86;

        // Held to the hilt as well as to the blade. A quillon that was only ever a fraction
        // of the blade would be a yard long on a sabre someone had drawn out across the
        // desktop, and the crossguard would stop reading as a crossguard.
        var reach = Math.Min(layout.BladeLength * 0.18, layout.HiltLength * 1.5);
        var w = layout.BladeWidth * 0.62;

        var halo = 1 - GlowSwell * (1 - Scene.Pulse);
        var swell = 1 - BladeSwell * (1 - Scene.Pulse);

        foreach (var sign in (double[])[-1, 1])
        {
            var from = new Point(x, sign * layout.HiltWidth * 0.7);
            var to = new Point(x, sign * (layout.HiltWidth * 0.7 + reach));

            // The same breath as the main blade. A crossguard whose quillons sat perfectly
            // still while the blade above them swelled would read as two different objects.
            Stroke(color, 0.20 * _config.Glow * brightness, w * 2.6 * halo);
            Stroke(color, 0.85 * brightness, w * 1.3 * swell);
            Stroke(Colors.White, 0.9 * brightness, w * 0.45 * swell);

            void Stroke(Color c, double opacity, double thickness)
            {
                if (opacity <= 0.004) return;
                context.DrawLine(
                    new Pen(new SolidColorBrush(c, Math.Clamp(opacity, 0, 1)), thickness) { LineCap = PenLineCap.Round },
                    from, to);
            }
        }
    }

    /// <summary>
    /// The colour of the blade the sabre is currently wearing. Nothing to cross-fade: a
    /// colour only ever changes while the blade is in, so there is never an old colour on
    /// screen to fade away from.
    /// </summary>
    private static Color BladeColor(BladeState shown) => shown switch
    {
        BladeState.Green => GreenBlade,
        BladeState.Amber => AmberBlade,
        BladeState.Red => RedBlade,

        // Off is only ever drawn lit in arrange mode, before anything has been reported at
        // all. A plain steel white claims nothing, which is the honest thing to claim at
        // that moment.
        _ => Color.FromRgb(206, 218, 236),
    };

    // ── the metalwork ─────────────────────────────────────────────────────────

    /// <summary>
    /// One hilt, in local space: <c>x</c> from 0 at the pommel to <c>length</c> at the
    /// emitter, <c>y</c> either side of the centreline.
    /// </summary>
    private void DrawHilt(DrawingContext context, SabreLayout layout, SabreLook look, double opacity)
    {
        using var _ = context.PushOpacity(opacity);

        var l = layout.HiltLength;
        var w = layout.HiltWidth / 2;
        var metal = Metal(look.Metal);
        var accent = Accent(look.Accent);
        var accentFlat = Flat(look.Accent);

        // Under the whole hilt, so it sits on the desktop instead of floating over it.
        context.DrawGeometry(ShadowBrush, null, Capsule(-w * 0.2, l + w * 0.2, w * 1.25, w * 0.35));

        switch (look.Hilt)
        {
            case HiltStyle.Crossguard:
                DrawCrossguard(context, l, w, metal, accent, accentFlat);
                break;

            case HiltStyle.Curved:
                DrawCurved(context, l, w, metal, accent, accentFlat);
                break;

            case HiltStyle.Sentinel:
                DrawSentinel(context, l, w, metal, accent, accentFlat);
                break;

            case HiltStyle.Duelist:
                DrawDuelist(context, l, w, metal, accent, accentFlat);
                break;

            default:
                DrawGraflex(context, l, w, metal, accent, accentFlat);
                break;
        }

        // The lengthwise specular, over everything. One hard highlight along the shoulder is
        // what makes a flat fill read as turned metal rather than as a coloured stick.
        context.DrawGeometry(
            new SolidColorBrush(Colors.White, 0.22), null,
            Capsule(l * 0.06, l * 0.94, -w * 0.42, w * 0.14));
    }

    /// <summary>Stacked grip rings, a side clamp, a control plate: the one everybody pictures.</summary>
    private void DrawGraflex(DrawingContext context, double l, double w, IBrush metal, IBrush accent, IBrush flat)
    {
        Body(context, metal, l * 0.05, l * 0.90, w);

        Ring(context, accent, l * 0.04, l * 0.11, w * 1.18);           // pommel cap
        Ring(context, flat, l * 0.115, l * 0.135, w * 1.05);

        Grooves(context, l * 0.20, l * 0.52, 7, w * 1.02);             // the grip
        Ring(context, accent, l * 0.535, l * 0.565, w * 1.12);

        // The clamp down one side. Nothing on a sabre is symmetrical about its own axis, and
        // this is the cheapest way to say so.
        context.DrawGeometry(metal, null,
            Rounded(l * 0.58, l * 0.74, -w * 1.55, -w * 0.35, w * 0.25));
        context.DrawGeometry(flat, null, Rounded(l * 0.61, l * 0.71, -w * 1.42, -w * 1.10, w * 0.1));

        Rivets(context, flat, l * 0.62, l * 0.70, 3, w * 0.16, w * 0.72);

        Shroud(context, metal, accent, l, w, prongs: 6);
    }

    /// <summary>A vented quillon housing at the throat, for the blades that come out sideways.</summary>
    private void DrawCrossguard(DrawingContext context, double l, double w, IBrush metal, IBrush accent, IBrush flat)
    {
        Body(context, metal, l * 0.03, l * 0.80, w * 0.94);

        Ring(context, accent, l * 0.02, l * 0.10, w * 1.1);
        Grooves(context, l * 0.16, l * 0.62, 9, w * 0.98);
        Rivets(context, flat, l * 0.20, l * 0.58, 5, w * 0.14, 0);

        // The housing: wider than the hilt, and slotted, because the vents are the whole
        // reason this hilt is shaped differently from the others.
        context.DrawGeometry(metal, null, Rounded(l * 0.78, l * 0.94, -w * 1.9, w * 1.9, w * 0.3));
        context.DrawGeometry(accent, null, Rounded(l * 0.80, l * 0.83, -w * 1.8, w * 1.8, w * 0.15));

        for (var i = 0; i < 3; i++)
        {
            var x = l * (0.845 + i * 0.032);
            context.DrawGeometry(GrooveBrush, null, Rounded(x, x + l * 0.016, -w * 1.72, -w * 0.9, w * 0.1));
            context.DrawGeometry(GrooveBrush, null, Rounded(x, x + l * 0.016, w * 0.9, w * 1.72, w * 0.1));
        }

        Shroud(context, metal, accent, l, w, prongs: 4);
    }

    /// <summary>
    /// Bowed along its length, so it reads as a hilt meant to be held at an angle.
    /// </summary>
    /// <remarks>
    /// Only the metal bows. The axis the blade leaves on, and the line arrange mode drags
    /// against, both stay straight — a hilt whose grabbable line wandered away from its own
    /// picture would feel broken in the hand long before anyone worked out why.
    /// </remarks>
    private void DrawCurved(DrawingContext context, double l, double w, IBrush metal, IBrush accent, IBrush flat)
    {
        var bow = w * 0.85;

        var body = new StreamGeometry();
        using (var ctx = body.Open())
        {
            // Two cubics with the same control offsets, one along each shoulder, so the
            // thickness stays even the whole way round the bend.
            ctx.BeginFigure(new Point(l * 0.03, -w), true);
            ctx.CubicBezierTo(
                new Point(l * 0.35, -w - bow), new Point(l * 0.65, -w - bow), new Point(l * 0.97, -w * 0.8));
            ctx.LineTo(new Point(l * 0.97, w * 0.8));
            ctx.CubicBezierTo(
                new Point(l * 0.65, w - bow), new Point(l * 0.35, w - bow), new Point(l * 0.03, w));
            ctx.EndFigure(true);
        }

        context.DrawGeometry(metal, null, body);

        // Rings following the bend, each nudged up by roughly the curve's own offset at that
        // point. Close enough at hilt sizes, and far cheaper than solving the curve.
        for (var i = 0; i < 8; i++)
        {
            var t = 0.22 + i * 0.058;
            var lift = -bow * 4 * t * (1 - t);
            context.DrawGeometry(GrooveBrush, null,
                Rounded(l * t, l * (t + 0.022), lift - w * 0.86, lift + w * 0.86, w * 0.2));
        }

        Ring(context, accent, l * 0.02, l * 0.09, w * 1.15);
        context.DrawGeometry(flat, null, Ellipse(l * 0.055, -bow * 0.1, w * 0.34, w * 0.34));

        Shroud(context, metal, accent, l, w * 0.85, prongs: 5);
    }

    /// <summary>Long, tapered, and wrapped pommel to emitter in a spiral of banding.</summary>
    private void DrawSentinel(DrawingContext context, double l, double w, IBrush metal, IBrush accent, IBrush flat)
    {
        // Tapered by hand rather than by a capsule: the taper is the silhouette here.
        var taper = new StreamGeometry();
        using (var ctx = taper.Open())
        {
            ctx.BeginFigure(new Point(l * 0.02, -w * 1.12), true);
            ctx.LineTo(new Point(l * 0.95, -w * 0.74));
            ctx.LineTo(new Point(l * 0.95, w * 0.74));
            ctx.LineTo(new Point(l * 0.02, w * 1.12));
            ctx.EndFigure(true);
        }

        context.DrawGeometry(metal, null, taper);

        // The wrap: parallelograms leaning along the hilt, which at this size is all a
        // spiral is. Drawn in the accent, so it is the thing you notice from across a desk.
        for (var i = 0; i < 9; i++)
        {
            var x = l * (0.10 + i * 0.085);
            var lean = l * 0.035;
            var band = new StreamGeometry();
            using (var ctx = band.Open())
            {
                ctx.BeginFigure(new Point(x, -w), true);
                ctx.LineTo(new Point(x + lean, -w));
                ctx.LineTo(new Point(x + lean - lean * 1.6, w));
                ctx.LineTo(new Point(x - lean * 1.6, w));
                ctx.EndFigure(true);
            }

            context.DrawGeometry(i % 2 == 0 ? accent : flat, null, band);
        }

        // The ring on the pommel, for the belt hook it would have had. Stroked rather than
        // filled: a second ellipse in the background colour would only be a hole over the
        // desktop wallpaper, and over a transparent window it would not even be that.
        context.DrawEllipse(null, new Pen(accent, w * 0.42), new Point(-w * 0.25, 0), w * 0.55, w * 0.95);

        Shroud(context, metal, accent, l, w * 0.8, prongs: 3);
    }

    /// <summary>Fluted the whole way along, with a jewelled bulb where a pommel would be.</summary>
    private void DrawDuelist(DrawingContext context, double l, double w, IBrush metal, IBrush accent, IBrush flat)
    {
        Body(context, metal, l * 0.10, l * 0.92, w * 0.88);

        // The bulb, and the stone set into it. Ornament with nothing to do, which is rather
        // the point of ornament.
        context.DrawGeometry(metal, null, Ellipse(l * 0.08, 0, w * 1.5, w * 1.45));
        context.DrawGeometry(accent, null, Ellipse(l * 0.075, 0, w * 0.7, w * 0.66));
        context.DrawGeometry(new SolidColorBrush(Colors.White, 0.55), null,
            Ellipse(l * 0.06, -w * 0.22, w * 0.22, w * 0.2));

        // Rings all the way up, alternating cut and inlay.
        for (var i = 0; i < 11; i++)
        {
            var x = l * (0.20 + i * 0.058);
            context.DrawGeometry(i % 2 == 0 ? GrooveBrush : flat, null,
                Rounded(x, x + l * 0.020, -w * 1.02, w * 1.02, w * 0.22));
        }

        Ring(context, accent, l * 0.855, l * 0.885, w * 1.16);
        Shroud(context, metal, accent, l, w, prongs: 8);
    }

    // ── the small parts every hilt is made of ─────────────────────────────────

    private static void Body(DrawingContext context, IBrush metal, double from, double to, double w) =>
        context.DrawGeometry(metal, null, Capsule(from, to, 0, w));

    /// <summary>A collar standing slightly proud of the body.</summary>
    private static void Ring(DrawingContext context, IBrush brush, double from, double to, double w) =>
        context.DrawGeometry(brush, null, Rounded(from, to, -w, w, w * 0.28));

    /// <summary>A run of cut grooves — the grip on anything that has one.</summary>
    private static void Grooves(DrawingContext context, double from, double to, int count, double w)
    {
        if (count <= 0 || to <= from) return;

        var pitch = (to - from) / count;
        for (var i = 0; i < count; i++)
        {
            var x = from + i * pitch;
            context.DrawGeometry(GrooveBrush, null, Rounded(x, x + pitch * 0.46, -w, w, w * 0.2));
        }
    }

    /// <summary>A line of rivets along the body. <paramref name="offset"/> shifts them off the centreline.</summary>
    private static void Rivets(
        DrawingContext context, IBrush brush, double from, double to, int count, double radius, double offset)
    {
        if (count <= 0) return;

        var step = count == 1 ? 0 : (to - from) / (count - 1);
        for (var i = 0; i < count; i++)
            context.DrawGeometry(brush, null, Ellipse(from + step * i, offset, radius, radius));
    }

    /// <summary>
    /// The emitter shroud: a flared collar with prongs around the mouth, which is where the
    /// blade comes from on every one of these.
    /// </summary>
    private static void Shroud(
        DrawingContext context, IBrush metal, IBrush accent, double l, double w, int prongs)
    {
        var flare = new StreamGeometry();
        using (var ctx = flare.Open())
        {
            ctx.BeginFigure(new Point(l * 0.86, -w * 1.0), true);
            ctx.LineTo(new Point(l * 1.0, -w * 1.45));
            ctx.LineTo(new Point(l * 1.0, w * 1.45));
            ctx.LineTo(new Point(l * 0.86, w * 1.0));
            ctx.EndFigure(true);
        }

        context.DrawGeometry(metal, null, flare);
        context.DrawGeometry(accent, null, Rounded(l * 0.875, l * 0.90, -w * 1.15, w * 1.15, w * 0.2));

        // The prongs are cut out of the mouth rather than added to it, so the shroud reads
        // as one machined piece instead of a collar with teeth glued on.
        if (prongs <= 1) return;

        var span = w * 2.9;
        var pitch = span / prongs;
        for (var i = 1; i < prongs; i++)
        {
            var y = -w * 1.45 + pitch * i;
            context.DrawGeometry(GrooveBrush, null,
                Rounded(l * 0.93, l * 1.005, y - pitch * 0.18, y + pitch * 0.18, pitch * 0.15));
        }
    }

    private static Geometry Capsule(double from, double to, double centre, double w) =>
        new RectangleGeometry(new Rect(from, centre - w, to - from, w * 2), w, w);

    private static Geometry Rounded(double from, double to, double top, double bottom, double radius) =>
        new RectangleGeometry(new Rect(from, top, to - from, bottom - top), radius, radius);

    private static Geometry Ellipse(double x, double y, double rx, double ry) =>
        new EllipseGeometry(new Rect(x - rx, y - ry, rx * 2, ry * 2));

    // ── brushes ───────────────────────────────────────────────────────────────

    /// <summary>
    /// A metal is a gradient across the hilt, not a colour: dark at the far shoulder, a hard
    /// highlight a third of the way over, mid-tone down the near side.
    /// </summary>
    /// <remarks>
    /// Relative units, so the same brush serves every part of every hilt whatever its size —
    /// the gradient runs across whichever rectangle it is handed, which in this local space
    /// is always across the sabre.
    /// </remarks>
    private IBrush Metal(string hex)
    {
        if (_metals.TryGetValue(hex, out var brush)) return brush;

        var c = Parse(hex, Color.FromRgb(180, 184, 192));
        brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            [
                new GradientStop(Shade(c, 0.30), 0.00),
                new GradientStop(Shade(c, 0.72), 0.16),
                new GradientStop(Shade(c, 1.38), 0.34),
                new GradientStop(Shade(c, 0.94), 0.52),
                new GradientStop(Shade(c, 0.52), 0.82),
                new GradientStop(Shade(c, 0.26), 1.00),
            ],
        };

        _metals[hex] = brush;
        return brush;
    }

    /// <summary>The inlay. Shallower than the body so it reads as sitting on top of it.</summary>
    private IBrush Accent(string hex)
    {
        if (_accents.TryGetValue(hex, out var brush)) return brush;

        var c = Parse(hex, Color.FromRgb(201, 162, 39));
        brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            [
                new GradientStop(Shade(c, 0.48), 0.00),
                new GradientStop(Shade(c, 1.30), 0.30),
                new GradientStop(Shade(c, 0.86), 0.60),
                new GradientStop(Shade(c, 0.42), 1.00),
            ],
        };

        _accents[hex] = brush;
        return brush;
    }

    /// <summary>The flat version of the accent, for the pieces a gradient would only muddle.</summary>
    private IBrush Flat(string hex)
    {
        if (_flats.TryGetValue(hex, out var brush)) return brush;

        brush = new SolidColorBrush(Shade(Parse(hex, Color.FromRgb(201, 162, 39)), 0.78));
        _flats[hex] = brush;
        return brush;
    }

    private static Color Parse(string hex, Color fallback)
    {
        try
        {
            return Color.Parse(hex);
        }
        catch
        {
            return fallback;
        }
    }

    private static Color Shade(Color c, double factor) => Color.FromArgb(
        c.A,
        (byte)Math.Clamp(c.R * factor, 0, 255),
        (byte)Math.Clamp(c.G * factor, 0, 255),
        (byte)Math.Clamp(c.B * factor, 0, 255));

    // ── arrange mode ──────────────────────────────────────────────────────────

    /// <summary>
    /// The two things you can grab, said out loud: a ring round the hilt to carry it, and one
    /// at the tip to aim by.
    /// </summary>
    private void DrawHandles(DrawingContext context, SabreLayout layout)
    {
        using var _ = context.PushOpacity(IsHeld ? 1.0 : 0.6);

        context.DrawGeometry(null, ArrangeHandle,
            Rounded(-layout.HiltWidth * 0.4, layout.HiltLength + layout.HiltWidth * 0.4,
                -layout.HiltWidth * 1.5, layout.HiltWidth * 1.5, layout.HiltWidth * 0.8));

        var tip = layout.HiltLength + Math.Max(layout.BladeLength, layout.HiltLength * 0.6);
        context.DrawEllipse(null, ArrangeHandle, new Point(tip, 0), layout.BladeWidth * 1.9, layout.BladeWidth * 1.9);
    }

    /// <summary>
    /// What the mouse does in here. On screen rather than in the README, because arrange mode
    /// is entered from a tray menu by somebody who is not reading anything.
    /// </summary>
    private void DrawCribSheet(DrawingContext context)
    {
        var text = new FormattedText(
            "Drag a hilt to move it  ·  drag a blade to aim and lengthen it  ·  hold Shift to snap to 15°\n"
            + "Scroll over a hilt to resize it  ·  Esc, or the tray, when you are done",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            14,
            ArrangeText)
        {
            TextAlignment = TextAlignment.Center,
        };

        var pad = 18.0;
        var width = text.Width + pad * 2;
        var height = text.Height + pad * 1.2;
        var x = (Bounds.Width - width) / 2;

        // At the top, because every default arrangement puts the sabres along the bottom and
        // a crib sheet over the thing being explained is not much of a crib sheet.
        var y = Math.Max(12, Bounds.Height * 0.06);

        context.DrawRectangle(ArrangeCard, ArrangeCardEdge, new RoundedRect(new Rect(x, y, width, height), 10));
        context.DrawText(text, new Point(x + pad, y + pad * 0.6));
    }
}

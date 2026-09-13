using Greenlight.SabreClient;

namespace Greenlight.SabreClient.UnitTests;

/// <summary>
/// The scene, tested without a window. Everything here is something you would not catch by
/// looking at the desktop: a hilt that swaps while the blade is still lit, a blade that loses
/// a little length every time it is dragged, a grabbable line that has drifted away from its
/// own picture, a pulse that dips to black.
/// </summary>
public class SabreSceneTests
{
    private static SabreScene Scene(SabrePlacement? placement = null)
    {
        var scene = new SabreScene(placement ?? new SabrePlacement(), randomSeed: 1);
        scene.Resize(1000, 1000);
        return scene;
    }

    private static void Run(SabreScene scene, double seconds, double step = 1.0 / 60)
    {
        for (var t = 0.0; t < seconds; t += step) scene.Advance(TimeSpan.FromSeconds(step));
    }

    // ── igniting and going out ────────────────────────────────────────────────

    [Fact]
    public void The_blade_starts_in()
    {
        var scene = Scene();
        Assert.Equal(0, scene.Ignition);
        Assert.Equal(0, scene.Measure().BladeLength);
    }

    [Fact]
    public void A_colour_runs_the_blade_out_and_off_again()
    {
        var scene = Scene();

        scene.State = BladeState.Green;
        Run(scene, 1.5);
        Assert.Equal(1, scene.Ignition, 3);

        scene.State = BladeState.Off;
        Run(scene, 1.5);
        Assert.Equal(0, scene.Ignition, 3);
    }

    [Fact]
    public void The_first_snapshot_lights_straight_away()
    {
        var scene = Scene();
        scene.State = BladeState.Green;

        // Cold start: there is no old hilt on screen to put away, so there is nothing to wait
        // for and no dark beat worth showing.
        Assert.False(scene.IsChangingOver);
        Assert.Equal(BladeState.Green, scene.Shown);
    }

    [Fact]
    public void A_frame_after_a_long_sleep_does_not_finish_the_ignition_in_one_step()
    {
        var scene = Scene();
        scene.State = BladeState.Green;

        scene.Advance(TimeSpan.FromMinutes(2));
        Assert.True(scene.Ignition < 1);
    }

    // ── changing hilts ────────────────────────────────────────────────────────

    [Fact]
    public void The_hilt_only_changes_while_the_blade_is_in()
    {
        var scene = Scene();
        scene.State = BladeState.Green;
        Run(scene, 1.5);

        scene.State = BladeState.Red;

        // The whole trick. A hilt that changed shape under a lit blade would read as a
        // glitch; swapped in the dark it reads as somebody having drawn a different sabre.
        var swapped = false;
        for (var i = 0; i < 120; i++)
        {
            scene.Advance(TimeSpan.FromSeconds(1.0 / 60));

            if (scene.Shown == BladeState.Red && !swapped)
            {
                swapped = true;
                Assert.Equal(0, scene.Ignition, 6);
            }

            if (!swapped) Assert.Equal(BladeState.Green, scene.Shown);
        }

        Assert.True(swapped);
    }

    [Fact]
    public void The_blade_retracts_in_the_colour_it_was_lit_in()
    {
        var scene = Scene();
        scene.State = BladeState.Red;
        Run(scene, 1.5);

        scene.State = BladeState.Green;
        Run(scene, 0.2);

        // The canvas draws from Shown, so this is what stops a red blade turning green on its
        // way into the hilt — which would read as the status having changed half a second
        // before the blade vanished.
        Assert.Equal(BladeState.Red, scene.Shown);
        Assert.True(scene.Ignition is > 0 and < 1);
    }

    [Fact]
    public void The_new_hilt_sits_dark_for_a_beat_before_it_lights()
    {
        var scene = Scene();
        scene.State = BladeState.Green;
        Run(scene, 1.5);

        scene.State = BladeState.Amber;

        // Run to the frame the hilt changes on, then one more frame.
        while (scene.Shown != BladeState.Amber) scene.Advance(TimeSpan.FromSeconds(1.0 / 60));
        scene.Advance(TimeSpan.FromSeconds(1.0 / 60));

        // Still barely out: the pause has to have been spent before the swap, otherwise the
        // new hilt is never actually seen dark and the changeover is just a flicker.
        Assert.True(scene.Ignition < 0.1);

        Run(scene, 1.0);
        Assert.Equal(1, scene.Ignition, 3);
    }

    [Fact]
    public void A_whole_changeover_takes_about_a_second()
    {
        var scene = Scene();
        scene.State = BladeState.Green;
        Run(scene, 1.5);

        scene.State = BladeState.Red;

        var seconds = 0.0;
        while ((scene.IsChangingOver || scene.Ignition < 1) && seconds < 5)
        {
            scene.Advance(TimeSpan.FromSeconds(1.0 / 60));
            seconds += 1.0 / 60;
        }

        // In, a beat, back out. Long enough to notice from across a desk, short enough that
        // it is not a thing you sit and wait for.
        Assert.InRange(seconds, 0.8, 1.2);
    }

    [Fact]
    public void A_second_change_mid_changeover_lands_on_the_latest_one()
    {
        var scene = Scene();
        scene.State = BladeState.Green;
        Run(scene, 1.5);

        scene.State = BladeState.Red;
        Run(scene, 0.1);
        scene.State = BladeState.Amber;

        Run(scene, 2);

        // Greenlight can change its mind faster than the animation runs. Ending up wearing the
        // red hilt because it was asked for first would be the toy reporting stale news.
        Assert.Equal(BladeState.Amber, scene.Shown);
        Assert.Equal(1, scene.Ignition, 3);
    }

    // ── the build pulse ───────────────────────────────────────────────────────

    [Fact]
    public void Brightness_sits_still_unless_something_is_building()
    {
        var scene = Scene();
        scene.State = BladeState.Green;
        Run(scene, 3);

        Assert.Equal(1, scene.Brightness, 6);
    }

    [Fact]
    public void The_build_pulse_breathes_without_going_dark()
    {
        var scene = Scene();
        scene.State = BladeState.Green;
        scene.IsBuilding = true;

        var low = double.MaxValue;
        var high = double.MinValue;

        // Ten seconds, so at least two full breaths are seen whatever the period.
        for (var i = 0; i < 600; i++)
        {
            scene.Advance(TimeSpan.FromSeconds(1.0 / 60));
            low = Math.Min(low, scene.Brightness);
            high = Math.Max(high, scene.Brightness);
        }

        // Gently. A desk toy that strobes between lit and dark is a migraine — but one that
        // never visibly moves is not saying anything at all, so the swell has to survive
        // being this shallow.
        Assert.InRange(low, 0.80, 0.88);
        Assert.Equal(1, high, 2);
        Assert.True(high - low > 0.1);
    }

    [Fact]
    public void Nothing_breathes_while_nothing_is_building()
    {
        var scene = Scene();
        scene.State = BladeState.Green;
        Run(scene, 3);

        // Both of them flat, not just the brightness: the canvas sizes the halo from Pulse,
        // so a Pulse that wandered while no build was running would have the glow quietly
        // breathing at rest.
        Assert.Equal(1, scene.Pulse, 6);
        Assert.Equal(1, scene.Brightness, 6);
    }

    [Fact]
    public void The_breath_is_spent_mostly_on_the_halo_rather_than_on_brightness()
    {
        var scene = Scene();
        scene.State = BladeState.Green;
        scene.IsBuilding = true;

        for (var i = 0; i < 8; i++) scene.Advance(TimeSpan.FromSeconds(0.25));

        // At the bottom of the breath the blade has barely dimmed — which is the point. The
        // visible part of the pulse is the halo drawing in, and that is driven by Pulse.
        Assert.Equal(0, scene.Pulse, 3);
        Assert.True(scene.Brightness > 0.8);
    }

    [Fact]
    public void One_breath_takes_four_seconds()
    {
        var scene = Scene();
        scene.State = BladeState.Green;
        scene.IsBuilding = true;

        // Quarter-second steps because Advance clamps anything longer, and eight of them land
        // on exactly two seconds with no floating-point drift to allow for.
        Assert.Equal(1, scene.Brightness, 3);

        for (var i = 0; i < 8; i++) scene.Advance(TimeSpan.FromSeconds(0.25));
        var trough = scene.Brightness;

        for (var i = 0; i < 8; i++) scene.Advance(TimeSpan.FromSeconds(0.25));

        // Out at two seconds, back at four. Slow enough that it is never the thing that
        // catches your eye — about fifteen breaths a minute, which is a person sitting still.
        Assert.Equal(0.84, trough, 3);
        Assert.Equal(1, scene.Brightness, 3);
    }

    // ── where the sabre is ────────────────────────────────────────────────────

    [Fact]
    public void An_angle_of_zero_points_the_blade_straight_up()
    {
        var scene = Scene(new SabrePlacement { AnchorX = 0.5, AnchorY = 0.9, Angle = 0 });
        scene.SnapLit();

        var layout = scene.Measure();

        // Straight up on a screen is a smaller Y, which is the sign error that would put the
        // sabre through the floor.
        Assert.Equal(layout.Pommel.X, layout.FullTip.X, 6);
        Assert.True(layout.FullTip.Y < layout.Pommel.Y);
    }

    [Theory]
    [InlineData(90, 1, 0)]
    [InlineData(180, 0, 1)]
    [InlineData(270, -1, 0)]
    public void An_angle_turns_the_sabre_clockwise(double angle, double dx, double dy)
    {
        var scene = Scene(new SabrePlacement { Angle = angle });
        var direction = scene.Measure().Direction;

        Assert.Equal(dx, direction.X, 6);
        Assert.Equal(dy, direction.Y, 6);
    }

    [Fact]
    public void The_sabre_keeps_its_place_when_the_screen_changes_size()
    {
        var scene = Scene(new SabrePlacement { AnchorX = 0.25, AnchorY = 0.5 });
        scene.Resize(2000, 1000);

        // The anchor is a fraction for exactly this: a laptop undocked from a 4K monitor
        // should find its sabre where it left it, not jammed in the corner.
        Assert.Equal(500, scene.Measure().Pommel.X, 6);
    }

    // ── arrange mode ──────────────────────────────────────────────────────────

    [Fact]
    public void The_hilt_and_the_blade_are_grabbable_where_they_are_drawn()
    {
        var scene = Scene(new SabrePlacement { AnchorX = 0.5, AnchorY = 0.9 });
        scene.SnapLit();

        var layout = scene.Measure();
        var midHilt = new ScenePoint(
            (layout.Pommel.X + layout.Emitter.X) / 2, (layout.Pommel.Y + layout.Emitter.Y) / 2);
        var midBlade = new ScenePoint(
            (layout.Emitter.X + layout.FullTip.X) / 2, (layout.Emitter.Y + layout.FullTip.Y) / 2);

        Assert.Equal(SabreGrip.Hilt, scene.HitTest(midHilt));
        Assert.Equal(SabreGrip.Blade, scene.HitTest(midBlade));
        Assert.Equal(SabreGrip.None, scene.HitTest(new ScenePoint(10, 10)));
    }

    [Fact]
    public void An_unlit_blade_is_still_grabbable()
    {
        // Arrange mode holds the blade out, but the ignition takes the best part of half a
        // second — grabbing it should not be a matter of catching it in time.
        var scene = Scene(new SabrePlacement { AnchorX = 0.5, AnchorY = 0.9 });

        var layout = scene.Measure();
        Assert.Equal(0, layout.BladeLength);

        var midBlade = new ScenePoint(
            (layout.Emitter.X + layout.FullTip.X) / 2, (layout.Emitter.Y + layout.FullTip.Y) / 2);
        Assert.Equal(SabreGrip.Blade, scene.HitTest(midBlade));
    }

    [Fact]
    public void Arrange_mode_holds_the_blade_out_with_no_greenlight_at_all()
    {
        var scene = Scene();
        scene.ForceLit = true;
        Run(scene, 1.0);

        // Nothing has ever been reported, so the sabre is Off — and a blade you cannot see is
        // a blade you cannot grab.
        Assert.Equal(BladeState.Off, scene.Shown);
        Assert.Equal(1, scene.Ignition, 3);
    }

    [Fact]
    public void Dragging_the_hilt_moves_it_and_keeps_it_on_screen()
    {
        var scene = Scene();

        scene.MoveTo(new ScenePoint(300, 400));
        Assert.Equal(300, scene.Measure().Pommel.X, 6);
        Assert.Equal(400, scene.Measure().Pommel.Y, 6);

        scene.MoveTo(new ScenePoint(-500, 9000));
        Assert.Equal(0, scene.Placement.AnchorX, 6);
        Assert.Equal(1, scene.Placement.AnchorY, 6);
    }

    [Fact]
    public void Aiming_points_the_blade_at_the_mouse_and_reaches_it()
    {
        var scene = Scene(new SabrePlacement { AnchorX = 0.5, AnchorY = 0.5 });
        scene.SnapLit();

        scene.AimAt(new ScenePoint(800, 500));

        var layout = scene.Measure();
        Assert.Equal(90, scene.Placement.Angle, 4);
        Assert.Equal(800, layout.FullTip.X, 3);
        Assert.Equal(500, layout.FullTip.Y, 3);
    }

    [Fact]
    public void Aiming_at_the_same_point_twice_does_not_shorten_the_blade()
    {
        var scene = Scene(new SabrePlacement { AnchorX = 0.5, AnchorY = 0.5 });
        scene.SnapLit();

        var target = new ScenePoint(760, 240);
        scene.AimAt(target);
        var once = scene.Placement.BladeFactor;

        for (var i = 0; i < 50; i++) scene.AimAt(target);

        // A drag is hundreds of these. If the hilt were subtracted from a distance that
        // already excluded it, the blade would creep shorter for as long as you held on.
        Assert.Equal(once, scene.Placement.BladeFactor, 9);
    }

    [Fact]
    public void Aiming_at_the_pommel_leaves_the_sabre_where_it_was()
    {
        var scene = Scene(new SabrePlacement { AnchorX = 0.5, AnchorY = 0.5, Angle = 135 });

        scene.AimAt(scene.Measure().Pommel);

        // Atan2(0, 0) is zero, so without the guard a drag through the pommel would snap the
        // sabre bolt upright on the way past.
        Assert.Equal(135, scene.Placement.Angle, 6);
    }

    [Fact]
    public void Shift_snaps_the_angle_to_fifteen_degrees()
    {
        var scene = Scene(new SabrePlacement { AnchorX = 0.5, AnchorY = 0.5 });

        scene.AimAt(new ScenePoint(700, 460), snapAngle: true);
        Assert.Equal(0, scene.Placement.Angle % 15, 6);
    }

    [Theory]
    [InlineData(7, 0)]
    [InlineData(-7, 0)]
    [InlineData(-20, 345)]
    [InlineData(358, 0)]
    public void Snapping_stays_inside_a_single_turn(double given, double expected) =>
        Assert.Equal(expected, SabreScene.Snap(given), 6);

    [Fact]
    public void The_hilt_cannot_be_scrolled_away_to_nothing()
    {
        var scene = Scene();

        for (var i = 0; i < 200; i++) scene.Rescale(-0.08);
        Assert.Equal(0.4, scene.Placement.HiltScale, 6);

        for (var i = 0; i < 200; i++) scene.Rescale(0.08);
        Assert.Equal(3.0, scene.Placement.HiltScale, 6);
    }

    [Fact]
    public void A_hilt_scaled_to_nothing_is_still_hit_testable()
    {
        var scene = Scene(new SabrePlacement { AnchorX = 0.5, AnchorY = 0.5, HiltScale = 0.4 });

        // The distance-to-segment maths divides by the segment's length. NaN compares false
        // against every threshold, so the failure here is not an exception — it is a sabre
        // that has quietly stopped answering the mouse.
        Assert.NotEqual(SabreGrip.None, scene.HitTest(new ScenePoint(500, 500)));
    }

    // ── starting with Windows ─────────────────────────────────────────────────
    // The registry writing itself is not tested — that would be a test that changes the
    // machine it runs on. What is tested is the only part with a decision in it: which
    // executable Windows gets pointed at.

    [Fact]
    public void An_installed_app_registers_the_shim_rather_than_the_versioned_copy()
    {
        const string running = @"C:\Users\x\AppData\Local\Greenlight.SabreClient\current\Greenlight.SabreClient.exe";
        const string shim = @"C:\Users\x\AppData\Local\Greenlight.SabreClient\Greenlight.SabreClient.exe";

        // The whole point. Registering the running path works perfectly until the first
        // update replaces `current`, and then fails at every login afterwards — which nobody
        // connects to a setting they turned on weeks earlier.
        Assert.Equal(shim, WindowsStartup.Launcher(running, path => path == shim));
    }

    [Fact]
    public void A_dev_build_registers_itself()
    {
        const string running = @"C:\src\Greenlight.SabreClient\bin\Debug\net10.0-windows\Greenlight.SabreClient.exe";

        Assert.Equal(running, WindowsStartup.Launcher(running, _ => false));
    }

    [Fact]
    public void A_missing_shim_falls_back_to_the_running_copy()
    {
        const string running = @"C:\portable\current\Greenlight.SabreClient.exe";

        // A folder that happens to be called "current" is not a promise. Pointing Windows at
        // an executable that is not there would be worse than pointing it at a versioned one.
        Assert.Equal(running, WindowsStartup.Launcher(running, _ => false));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_process_path_means_nothing_to_register(string? processPath) =>
        Assert.Null(WindowsStartup.Launcher(processPath, _ => true));

    // ── config ────────────────────────────────────────────────────────────────

    [Fact]
    public void Every_state_has_a_hilt_of_its_own()
    {
        var config = new SabreConfig();

        // The point of the whole thing: four states, four different sabres on the desk.
        var hilts = new[]
        {
            config.LookFor(BladeState.Off).Hilt,
            config.LookFor(BladeState.Green).Hilt,
            config.LookFor(BladeState.Amber).Hilt,
            config.LookFor(BladeState.Red).Hilt,
        };

        Assert.Equal(hilts.Length, hilts.Distinct().Count());
    }

    [Fact]
    public void A_file_that_cannot_be_parsed_falls_back_to_the_defaults()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sabre-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ not json at all ");

        try
        {
            // A desk toy, and a stray comma should not cost you the whole thing.
            Assert.NotNull(SabreConfig.Load(path).Sabre);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_hilt_deleted_by_hand_falls_back_rather_than_crashing_the_next_frame()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sabre-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{ "WhenRed": null, "Sabre": null }""");

        try
        {
            var loaded = SabreConfig.Load(path);
            Assert.NotNull(loaded.Sabre);
            Assert.NotNull(loaded.LookFor(BladeState.Red));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_hand_edited_sabre_is_clamped_back_onto_the_screen()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sabre-{Guid.NewGuid():N}.json");
        var config = new SabreConfig();
        config.Sabre.AnchorX = 12;
        config.Sabre.Angle = -45;
        config.Sabre.BladeFactor = -3;
        config.Save(path);

        try
        {
            var loaded = SabreConfig.Load(path);
            Assert.Equal(1, loaded.Sabre.AnchorX);
            Assert.Equal(315, loaded.Sabre.Angle);
            Assert.True(loaded.Sabre.BladeFactor > 0);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_re_forged_hilt_comes_back_as_it_was_left()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sabre-{Guid.NewGuid():N}.json");
        var config = new SabreConfig();
        config.WhenRed.Hilt = HiltStyle.Duelist;
        config.WhenRed.Metal = "#B87333";
        config.Sabre.Angle = 42;
        config.Save(path);

        try
        {
            var loaded = SabreConfig.Load(path);
            Assert.Equal(HiltStyle.Duelist, loaded.WhenRed.Hilt);
            Assert.Equal("#B87333", loaded.WhenRed.Metal);
            Assert.Equal(42, loaded.Sabre.Angle);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

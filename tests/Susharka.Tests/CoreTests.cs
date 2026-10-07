using System.IO;
using System.Windows.Input;
using Susharka.Core;
using Susharka.Views;
using Line = Susharka.Core.Line;

namespace Susharka.Tests;

public class LineTests
{
    [Fact]
    public void Oldest_photo_falls_off_when_full()
    {
        var line = new Line(maxItems: 3, new Random(1));
        var removed = new List<(string, RemovalReason)>();
        line.Removed += (i, r) => removed.Add((i.Path, r));

        foreach (var p in new[] { "a", "b", "c", "d" }) line.Hang(p);

        Assert.Equal(new[] { "b", "c", "d" }, line.Snapshot());
        Assert.Equal(new[] { ("a", RemovalReason.Overflow) }, removed);
    }

    [Fact]
    public void Tilts_stay_within_two_and_a_half_degrees()
    {
        var line = new Line(50, new Random(42));
        for (int i = 0; i < 50; i++) line.Hang("p" + i);
        Assert.All(line.Items, i => Assert.InRange(i.Tilt, -Line.MaxTilt, Line.MaxTilt));
        Assert.True(line.Items.Select(i => i.Tilt).Distinct().Count() > 40);
    }

    [Fact]
    public void Hanging_the_same_file_again_updates_instead_of_duplicating()
    {
        var line = new Line();
        var first = line.Hang(@"C:\x\shot.png");
        var updated = 0;
        line.Updated += _ => updated++;

        var again = line.Hang(@"c:\X\SHOT.png");

        Assert.Same(first, again);
        Assert.Single(line.Items);
        Assert.Equal(1, updated);
    }

    [Fact]
    public void Prune_removes_missing_files_as_gone()
    {
        var line = new Line();
        line.Hang("keep");
        line.Hang("lost");
        var reasons = new List<RemovalReason>();
        line.Removed += (_, r) => reasons.Add(r);

        var n = line.Prune(p => p == "keep");

        Assert.Equal(1, n);
        Assert.Equal(new[] { "keep" }, line.Snapshot());
        Assert.Equal(new[] { RemovalReason.Gone }, reasons);
    }

    [Fact]
    public void Restore_skips_missing_and_respects_capacity_without_animation()
    {
        var line = new Line(maxItems: 2);
        var animatedFlags = new List<bool>();
        line.Added += (_, animated) => animatedFlags.Add(animated);

        line.Restore(new[] { "a", "missing", "b", "c" }, p => p != "missing");

        Assert.Equal(new[] { "b", "c" }, line.Snapshot());
        Assert.All(animatedFlags, Assert.False);
    }

    [Fact]
    public void Lowering_capacity_trims_oldest()
    {
        var line = new Line(maxItems: 5);
        foreach (var p in new[] { "a", "b", "c", "d" }) line.Hang(p);
        line.MaxItems = 2;
        line.TrimToCapacity();
        Assert.Equal(new[] { "c", "d" }, line.Snapshot());
    }
}

public class RevealLogicTests
{
    private static RevealInput At(double t, bool band = false, bool zone = false, bool held = false, bool blocked = false)
        => new(t, band, zone, held, blocked);

    [Fact]
    public void Reveals_only_after_resting_in_the_band_for_the_delay()
    {
        var r = new RevealLogic { RevealDelay = 0.4 };
        Assert.False(r.Update(At(0.0, band: true)));
        Assert.False(r.Update(At(0.39, band: true)));
        Assert.True(r.Update(At(0.41, band: true)));
        Assert.True(r.Shown);
    }

    [Fact]
    public void Leaving_the_band_resets_the_dwell()
    {
        var r = new RevealLogic { RevealDelay = 0.4 };
        r.Update(At(0.0, band: true));
        r.Update(At(0.3, band: false));
        r.Update(At(0.35, band: true));
        Assert.False(r.Update(At(0.7, band: true)));
        Assert.True(r.Update(At(0.76, band: true)));
    }

    [Fact]
    public void Blocked_never_reveals_by_dwell()
    {
        var r = new RevealLogic { RevealDelay = 0.1 };
        for (double t = 0; t < 2; t += 0.05) r.Update(At(t, band: true, blocked: true));
        Assert.False(r.Shown);
    }

    [Fact]
    public void Retracts_after_pointer_has_left_the_zone_for_the_delay()
    {
        var r = new RevealLogic { RetractDelay = 0.5 };
        r.Set(true, 0);
        Assert.False(r.Update(At(1.0, zone: true)));
        Assert.False(r.Update(At(1.1)));
        Assert.False(r.Update(At(1.55)));
        Assert.True(r.Update(At(1.61)));
        Assert.False(r.Shown);
    }

    [Fact]
    public void Returning_to_the_zone_cancels_the_retract()
    {
        var r = new RevealLogic { RetractDelay = 0.5 };
        r.Set(true, 0);
        r.Update(At(0.1));
        r.Update(At(0.4, zone: true));
        Assert.False(r.Update(At(0.8)));
        Assert.True(r.Shown);
    }

    [Fact]
    public void Held_open_keeps_it_shown_and_reveals_immediately()
    {
        var r = new RevealLogic();
        Assert.True(r.Update(At(0, held: true)));
        for (double t = 0; t < 3; t += 0.1) r.Update(At(t, held: true));
        Assert.True(r.Shown);
    }
}

public class LineLayoutTests
{
    [Theory]
    [InlineData(1000, 18)]
    [InlineData(1920, 30)]
    [InlineData(3840, 30)]
    public void Sag_scales_with_width_up_to_30(double width, double expected)
        => Assert.Equal(expected, LineLayout.Sag(width), 3);

    [Fact]
    public void Rope_is_lowest_in_the_middle_and_symmetric()
    {
        const double w = 1600;
        var mid = LineLayout.RopeY(w / 2, w);
        Assert.Equal(LineLayout.RopeTop + LineLayout.Sag(w), mid, 6);
        Assert.Equal(LineLayout.RopeY(200, w), LineLayout.RopeY(w - 200, w), 6);
        Assert.True(LineLayout.RopeY(200, w) < mid);
        Assert.Equal(LineLayout.RopeTop, LineLayout.RopeY(-LineLayout.Overhang, w), 6);
    }

    [Fact]
    public void Slots_are_centred_and_evenly_spaced()
    {
        const double w = 1920;
        var xs = Enumerable.Range(0, 4).Select(i => LineLayout.SlotX(i, 4, w)).ToArray();
        Assert.Equal(w / 2, (xs[0] + xs[3]) / 2, 6);
        Assert.Equal(LineLayout.Spacing, xs[1] - xs[0], 6);
        Assert.Equal(w / 2, LineLayout.SlotX(0, 1, w), 6);
    }

    [Fact]
    public void Spacing_squeezes_on_narrow_screens()
    {
        var s = LineLayout.SpacingFor(8, 1000);
        Assert.True(s < LineLayout.Spacing);
        Assert.True(LineLayout.SlotX(0, 8, 1000) >= 0);
        Assert.True(LineLayout.SlotX(7, 8, 1000) <= 1000);
    }

    [Theory]
    [InlineData(1920, 1080, 142, 80)]
    [InlineData(400, 1200, 50, 150)]
    [InlineData(100, 100, 100, 100)]
    [InlineData(20, 10, 72, 36)]
    public void Photo_fits_box_keeping_aspect(int pw, int ph, double ew, double eh)
    {
        var (w, h) = LineLayout.PhotoSize(pw, ph);
        Assert.Equal(ew, w);
        Assert.Equal(eh, h);
    }
}

public class HotKeyTests
{
    [Fact]
    public void Default_capture_shortcut_round_trips()
    {
        var hk = HotKey.Parse("Ctrl+Shift+D4");
        Assert.Equal(ModifierKeys.Control | ModifierKeys.Shift, hk.Modifiers);
        Assert.Equal(Key.D4, hk.Key);
        Assert.Equal("Ctrl + Shift + 4", hk.Display());
        Assert.Equal(hk, HotKey.Parse(hk.Serialize()));
    }

    [Theory]
    [InlineData("ctrl+alt+t", ModifierKeys.Control | ModifierKeys.Alt, Key.T)]
    [InlineData("Win+Shift+5", ModifierKeys.Windows | ModifierKeys.Shift, Key.D5)]
    [InlineData("PrintScreen", ModifierKeys.None, Key.PrintScreen)]
    public void Parses_variants(string text, ModifierKeys mods, Key key)
    {
        var hk = HotKey.Parse(text);
        Assert.Equal(mods, hk.Modifiers);
        Assert.Equal(key, hk.Key);
        Assert.True(hk.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("Ctrl+Nonsense")]
    public void Rejects_unusable(string text) => Assert.False(HotKey.Parse(text).IsValid);
}

public class SettingsTests
{
    [Fact]
    public void Round_trips_and_clamps()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(dir.FullName, "settings.json");
            var s = Settings.Load(path);
            Assert.Equal("Ctrl+Shift+D4", s.CaptureHotKey);
            Assert.Equal(0.4, s.RevealDelay);
            Assert.Equal(8, s.MaxItems);

            s.MaxItems = 500;
            s.Hung = new List<string> { @"C:\a.png" };
            s.CaptureHotKey = "Ctrl+Alt+S";
            s.Save();

            var again = Settings.Load(path);
            Assert.Equal(30, again.MaxItems);
            Assert.Equal("Ctrl+Alt+S", again.CaptureHotKey);
            Assert.Equal(new[] { @"C:\a.png" }, again.Hung);
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public void Corrupt_file_falls_back_to_defaults()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, "{ not json");
        var s = Settings.Load(path);
        Assert.Equal(8, s.MaxItems);
        File.Delete(path);
    }

    [Fact]
    public void Screenshot_names_are_unique_within_a_second()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var now = new DateTime(2026, 10, 7, 15, 36, 12);
            var a = AppPaths.NewScreenshotPath(dir.FullName, now);
            File.WriteAllText(a, "");
            var b = AppPaths.NewScreenshotPath(dir.FullName, now);
            Assert.EndsWith("Screenshot 2026-10-07 at 15.36.12.png", a);
            Assert.EndsWith("Screenshot 2026-10-07 at 15.36.12 (2).png", b);
        }
        finally
        {
            dir.Delete(true);
        }
    }
}

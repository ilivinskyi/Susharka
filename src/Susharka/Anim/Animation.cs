using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Media;

namespace Susharka.Anim;

/// <summary>Something advanced once per frame. Return false when finished.</summary>
public interface IStepper
{
    bool Step(double dt);
}

/// <summary>A single frame loop on CompositionTarget.Rendering that only runs while something animates.</summary>
public static class AnimationLoop
{
    private static readonly List<IStepper> Active = new();
    private static readonly Stopwatch Clock = new();
    private static double _last;
    private static bool _hooked;

    public static void Add(IStepper s)
    {
        if (!Active.Contains(s)) Active.Add(s);
        if (_hooked) return;
        _hooked = true;
        Clock.Restart();
        _last = 0;
        CompositionTarget.Rendering += OnRendering;
    }

    public static void Remove(IStepper s) => Active.Remove(s);


    private static void OnRendering(object? sender, EventArgs e)
    {
        var now = Clock.Elapsed.TotalSeconds;
        var dt = Math.Min(now - _last, 1 / 20.0); // after a stall, don't jump
        _last = now;
        if (dt <= 0) return;

        foreach (var s in Active.ToArray())
        {
            if (Active.Contains(s) && !s.Step(dt)) Active.Remove(s);
        }
        if (Active.Count == 0)
        {
            CompositionTarget.Rendering -= OnRendering;
            _hooked = false;
        }
    }
}

/// <summary>
/// Damped spring (unit mass). Construct either from SwiftUI-style response/damping-fraction or from
/// raw stiffness/damping, so the tuned values read naturally.
/// </summary>
public sealed class Spring : IStepper
{
    private readonly Action<double> _apply;
    public double Value { get; private set; }
    public double Target { get; private set; }
    public double Velocity { get; set; }
    public double Stiffness { get; set; }
    public double Damping { get; set; }
    public Action? Settled { get; set; }
    public bool IsMoving { get; private set; }

    public Spring(double initial, Action<double> apply, double stiffness, double damping)
    {
        Value = Target = initial;
        _apply = apply;
        Stiffness = stiffness;
        Damping = damping;
        _apply(initial);
    }

    public static Spring Response(double initial, Action<double> apply, double response, double dampingFraction)
    {
        var (k, c) = FromResponse(response, dampingFraction);
        return new Spring(initial, apply, k, c);
    }

    public static (double Stiffness, double Damping) FromResponse(double response, double dampingFraction)
    {
        var w = 2 * Math.PI / response;
        return (w * w, 2 * dampingFraction * w);
    }

    public void SetResponse(double response, double dampingFraction)
        => (Stiffness, Damping) = FromResponse(response, dampingFraction);

    public void AnimateTo(double target)
    {
        Target = target;
        if (Math.Abs(Target - Value) < 1e-6 && Math.Abs(Velocity) < 1e-6) return;
        IsMoving = true;
        AnimationLoop.Add(this);
    }

    public void Kick(double velocity)
    {
        Velocity += velocity;
        IsMoving = true;
        AnimationLoop.Add(this);
    }

    public void Snap(double value)
    {
        Value = Target = value;
        Velocity = 0;
        IsMoving = false;
        AnimationLoop.Remove(this);
        _apply(value);
    }

    public bool Step(double dt)
    {
        // Semi-implicit Euler in small substeps: stable for the stiff springs used here.
        const double h = 1 / 480.0;
        for (double t = 0; t < dt; t += h)
        {
            var step = Math.Min(h, dt - t);
            var a = -Stiffness * (Value - Target) - Damping * Velocity;
            Velocity += a * step;
            Value += Velocity * step;
        }

        var scale = Math.Max(1e-3, Math.Abs(Target) * 1e-4);
        if (Math.Abs(Value - Target) < 0.002 + scale && Math.Abs(Velocity) < 0.01 + scale)
        {
            Value = Target;
            Velocity = 0;
            _apply(Value);
            IsMoving = false;
            Settled?.Invoke();
            return IsMoving; // a Settled handler may have started a new move

        }
        _apply(Value);
        return true;
    }
}

/// <summary>Fixed-duration animation from 0 to 1 through an easing curve.</summary>
public sealed class Tween : IStepper
{
    private readonly double _duration;
    private readonly Func<double, double> _ease;
    private readonly Action<double> _apply;
    private readonly Action? _done;
    private double _t;
    private bool _cancelled;

    private Tween(double duration, Func<double, double> ease, Action<double> apply, Action? done)
    {
        _duration = Math.Max(0.001, duration);
        _ease = ease;
        _apply = apply;
        _done = done;
    }

    public static Tween Run(double duration, Func<double, double> ease, Action<double> apply, Action? done = null)
    {
        var t = new Tween(duration, ease, apply, done);
        apply(ease(0));
        AnimationLoop.Add(t);
        return t;
    }

    public void Cancel()
    {
        _cancelled = true;
        AnimationLoop.Remove(this);
    }

    public bool Step(double dt)
    {
        if (_cancelled) return false;
        _t = Math.Min(1, _t + dt / _duration);
        _apply(_ease(_t));
        if (_t < 1) return true;
        _done?.Invoke();
        return false;
    }
}

public static class Ease
{
    public static double Linear(double t) => t;
    public static double OutQuad(double t) => 1 - (1 - t) * (1 - t);
    public static double InQuad(double t) => t * t;
    public static double InCubic(double t) => t * t * t;
    public static double OutCubic(double t) => 1 - Math.Pow(1 - t, 3);
    public static double InOutCubic(double t) => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
    public static double SmoothStep(double e0, double e1, double x)
    {
        var t = Math.Clamp((x - e0) / (e1 - e0), 0, 1);
        return t * t * (3 - 2 * t);
    }
    public static double Lerp(double a, double b, double t) => a + (b - a) * t;
}

using System;
using System.Collections.Generic;
using Godot;

namespace TerritoryWar;

public enum BallPhase { Inactive, Waiting, Flying }
public enum Ability { Storm, Gun, Shield, BigBall, Sniper, Shotgun }
public sealed class BallState
{
    public int Id, Faction;
    public BallPhase Phase;
    public double Value = 1, ReadyAt;
    public Vector2 Position, Previous, Velocity;
    public float Radius, Mass;
    public bool Lower;
    public bool IsDeath;
}
public readonly record struct Redemption(double Time, int BallId, int Faction, Ability Ability, double Value, bool IsDeath = false);

/// <summary>Fixed-step simulation. No scene tree, draw calls or visual random state.</summary>
public sealed class PinballSimulation
{
    public const int Capacity = 128;
    public const int StartingBallsPerFaction = 3;
    public const double StepSeconds = 1.0 / 120;
    public const float AbilityLeft = 30, AbilityWidth = 620, AbilityY = 976;
    // Belt order is independent of enum identity; the copied shield sits between sniper and storm.
    public static readonly Ability[] BeltAbilities =
    {
        Ability.Storm, Ability.Gun, Ability.Shield, Ability.BigBall, Ability.Shotgun, Ability.Sniper, Ability.Shield
    };
    public readonly BallState[] Balls = new BallState[Capacity];
    public readonly double[,] Redeemed = new double[4, 6];
    public readonly double[] SlotValues = new double[6];
    public readonly double[] SlotFlash = new double[6];
    public readonly Queue<Redemption> Events = new();
    public readonly int[] PendingRewards = new int[4];
    public readonly bool[] Eliminated = new bool[4];
    public readonly double[] PendingDeath = new double[4];
    public readonly SimulationConfig Config;
    // Separate from bounded UI history: consumers receive every redemption exactly once.
    public event Action<Redemption> AbilityRedeemed;
    public double Time { get; private set; }
    // Starting value steps at each doubling interval instead of growing continuously.
    public double BaseBallValue => Math.Pow(2,Math.Floor(Time/Math.Max(1,Config.BallValueDoublingSeconds)+1e-9));
    public int MultiplierHits { get; private set; }
    public int AbilityHits { get; private set; }
    private Random _random;
    private double _nextLaunch;
    public static readonly Vector2[] Pegs = {
        new(257,178), new(423,178), new(174,260), new(340,260), new(506,260),
        new(100,342), new(254,342), new(426,342), new(580,342),
        new(177,425), new(340,425), new(503,425),
        new(340,773), new(250,845), new(430,845), new(160,918), new(520,918)
    };
    public PinballSimulation(SimulationConfig config)
    {
        Config = config;
        for (int i = 0; i < Capacity; i++) Balls[i] = new BallState { Id = i };
        Reset();
    }
    public void Reset()
    {
        Time = _nextLaunch = 0; MultiplierHits = AbilityHits = 0;
        _random = new Random(Config.Seed);
        Array.Clear(Redeemed); Array.Clear(SlotValues); Array.Clear(SlotFlash); Array.Clear(PendingRewards); Events.Clear();
        Array.Clear(Eliminated); Array.Clear(PendingDeath);
        foreach (var b in Balls)
        {
            b.Phase = BallPhase.Inactive; b.Value = 1; b.Faction = 0; b.ReadyAt = 0;
            b.Position = b.Previous = b.Velocity = Vector2.Zero;
            b.Radius = b.Mass = 0; b.Lower = false; b.IsDeath = false;
        }
        for (int i = 0; i < StartingBallsPerFaction * 4; i++) Activate(Balls[i], i % 4);
    }
    private void Activate(BallState b, int faction)
    {
        b.Faction = faction; b.Value = BaseBallValue; b.Lower = false; b.IsDeath = false; b.ReadyAt = Time;
        b.Phase = BallPhase.Waiting; b.Position = b.Previous = new(340, 52); b.Velocity = Vector2.Zero;
        UpdateSize(b);
    }
    public void AwardBall(int faction) { if (!Eliminated[faction]) PendingRewards[faction]++; DrainRewards(); }
    private void DrainRewards()
    {
        for (int f = 0; f < 4; f++)
            foreach (var b in Balls)
                if (b.Phase == BallPhase.Inactive)
                {
                    if(PendingDeath[f]>0){Activate(b,f);b.IsDeath=true;b.Value=PendingDeath[f];PendingDeath[f]=0;UpdateSize(b);}
                    else if(!Eliminated[f]&&PendingRewards[f]>0){Activate(b,f);PendingRewards[f]--;}
                }
    }
    public double RadiusPivotLog => 8 + RadiusScaledTime / Math.Max(1, Config.RadiusPivotDoublingSeconds);
    private double RadiusScaledTime => Time * Math.Max(0, Config.RadiusTimeMultiplier);
    public void UpdateSize(BallState b)
    {
        double log = Math.Log2(Math.Max(1, b.Value));
        double radiusDecay = Math.Pow(1 + RadiusScaledTime / Config.TimeScale, 0.5);
        // Mass keeps the original time decay; RadiusTimeMultiplier is a radius-only tuning knob.
        double massDecay = Math.Pow(1 + Time / Config.TimeScale, 0.5);
        // The value pivot starts at 256 and grows continuously with match time.
        double pivot = RadiusPivotLog;
        double radiusGrowth = pivot * Math.Pow(log / pivot, Math.Max(1, Config.RadiusValueExponent));
        b.Radius = (float)Math.Max(Config.RadiusMin, Config.RadiusBase * (1 + Config.RadiusValueFactor * radiusGrowth) / radiusDecay);
        b.Mass = (float)Math.Clamp((1 + Config.MassValueFactor * log) / massDecay, 0.2, 20);
    }
    private float BaffleProgressAt(int slot, float time)
    {
        float period = Math.Max(0.1f, Config.BafflePeriod);
        float t = Mathf.PosMod(time + slot * Math.Max(0, Config.BaffleStagger), period);
        float lift = Math.Max(0.0001f, Config.BaffleLiftTime);
        float hold = Math.Max(0, Config.BaffleHoldTime);
        float retract = Math.Max(0.0001f, Config.BaffleRetractTime);
        if (t < lift) return t / lift;
        if (t < lift + hold) return 1;
        if (t < lift + hold + retract) return 1 - (t - lift - hold) / retract;
        return 0;
    }
    public float BaffleYAt(int slot, float time)
    {
        float rest = PinballGeometry.BaffleRestY(slot), top = PinballGeometry.BaffleLiftY(slot);
        return rest + (top - rest) * BaffleProgressAt(slot, time);
    }
    public float BaffleY(int slot) => BaffleYAt(slot, (float)Time);
    public (Vector2 A, Vector2 B) BaffleLineAt(int slot, float time)
    {
        float y = BaffleYAt(slot, time);
        float l = PinballGeometry.ChannelLeft(slot), r = PinballGeometry.ChannelRight(slot);
        float half = Math.Max(0, Config.BaffleSlant) * 0.5f;
        return slot <= 2
            ? (new Vector2(l, y + half), new Vector2(r, y - half))
            : (new Vector2(l, y - half), new Vector2(r, y + half));
    }
    public (Vector2 A, Vector2 B) BaffleLine(int slot) => BaffleLineAt(slot, (float)Time);
    // Rising baffles sweep stuck/oversized balls out of the pocket mouth.
    private void ResolveBaffles(BallState b, float dt)
    {
        if (b.Phase != BallPhase.Flying || dt <= 0) return;
        float previous = (float)Time - dt;
        for (int i = 0; i < 6; i++)
        {
            float l = PinballGeometry.ChannelLeft(i), r = PinballGeometry.ChannelRight(i);
            if (b.Radius * 2 <= MathF.Abs(r - l)) continue; // the slot fits; only oversized balls are cleared
            if (b.Position.X < MathF.Min(l, r) || b.Position.X > MathF.Max(l, r)) continue;
            var line = BaffleLine(i);
            Vector2 dir = (line.B - line.A).Normalized();
            Vector2 normal = new(-dir.Y, dir.X);
            if (normal.Y > 0) normal = -normal;
            float distance = (b.Position - line.A).Dot(normal);
            // Only balls overlapping the plate are swept; balls deeper in the pocket or below its floor are untouched.
            if (distance >= b.Radius || distance < -b.Radius) continue;
            float velocity = (BaffleY(i) - BaffleYAt(i, previous)) / dt;
            // A rising slanted baffle must not push a large ball through a side wall.
            CorrectPosition(b, normal * (b.Radius - distance));
            if(b.Phase!=BallPhase.Flying)return;
            float target = velocity * normal.Y;
            float along = b.Velocity.Dot(normal);
            if (along < target) b.Velocity += normal * (target - along);
            if (velocity < -1)
            {
                float outward = i <= 2 ? -1 : 1;
                if (b.Velocity.X * outward < Config.BaffleEjectSpeed) b.Velocity.X = outward * Config.BaffleEjectSpeed;
            }
        }
    }
    public float ConveyorOffset => (float)(Time * Config.ConveyorSpeed % AbilityWidth);
    public float AbilityRegionWidth => AbilityWidth / BeltAbilities.Length;
    public Ability AbilityAt(float x)
    {
        int index = Math.Clamp((int)(Mathf.PosMod(x - AbilityLeft - ConveyorOffset, AbilityWidth) / AbilityRegionWidth), 0, BeltAbilities.Length - 1);
        return BeltAbilities[index];
    }
    public float LauncherAngle => 0.72f * MathF.Sin((float)Time * 0.83f);
    public void Tick()
    {
        Time += StepSeconds;
        DrainRewards();
        foreach (var b in Balls)
        {
            if (b.Phase == BallPhase.Inactive) continue;
            UpdateSize(b);
            if (b.Phase == BallPhase.Waiting)
            {
                if (Time < b.ReadyAt || Time < _nextLaunch) continue;
                b.Phase = BallPhase.Flying; b.Position = b.Previous = new(340,65);
                float angle = LauncherAngle + (float)(_random.NextDouble() - 0.5) * 0.20f;
                b.Velocity = new Vector2(MathF.Sin(angle), MathF.Cos(angle)) * Config.LaunchSpeed;
                _nextLaunch = Time + Config.LaunchInterval;
            }
            Move(b, (float)StepSeconds);
            ResolveBaffles(b, (float)StepSeconds);
        }
        // Stable pair order. Position correction is itself swept against the board,
        // otherwise another ball could push this one through a perfectly valid wall.
        for (int pass = 0; pass < 3; pass++)
        for (int i = 0; i < Capacity; i++)
        {
        if (Balls[i].Phase != BallPhase.Flying) continue;
        for (int j = i + 1; j < Capacity; j++)
        {
            var a = Balls[i]; var b = Balls[j];
            if (a.Phase != BallPhase.Flying || b.Phase != BallPhase.Flying) continue;
            Vector2 delta = a.Position - b.Position;
            float distance = delta.Length(), radius = a.Radius + b.Radius;
            if (distance >= radius) continue;
            Vector2 n = distance > 0.001f ? delta / distance : Vector2.Right;
            float inverse = 1 / a.Mass + 1 / b.Mass;
            float overlap = radius - distance;
            CorrectPosition(a, n * overlap / a.Mass / inverse);
            CorrectPosition(b, -n * overlap / b.Mass / inverse);
            if (a.Phase != BallPhase.Flying || b.Phase != BallPhase.Flying) continue;
            float approach = (a.Velocity - b.Velocity).Dot(n);
            if (approach < 0) { float impulse = -(1 + Config.Restitution) * approach / inverse; a.Velocity += n * impulse / a.Mass; b.Velocity -= n * impulse / b.Mass; }
        }
        }
    }
    private void Move(BallState b, float dt)
    {
        b.Previous = b.Position;
        float acceleration = Config.Gravity;
        if (b.Position.X > 294 && b.Position.X < 386 && b.Position.Y > 510 && b.Position.Y < 685)
            acceleration -= Config.LiftForce / b.Mass;
        b.Velocity.Y += acceleration * dt;
        b.Velocity = b.Velocity.LimitLength(820);
        float remaining = dt;
        for (int contact = 0; contact < 8 && remaining > 1e-7f; contact++)
        {
            Vector2 motion = b.Velocity * remaining;
            float fraction = PinballGeometry.StaticSweep(b.Position, motion, b.Radius, out Vector2 normal, out bool bumper);
            if (TryTrigger(b, motion, fraction)) return;
            if (float.IsPositiveInfinity(fraction)) { b.Position += motion; break; }
            b.Position += motion * fraction + normal * 0.001f;
            float approach = b.Velocity.Dot(normal);
            if (approach < 0)
                b.Velocity -= normal * ((1 + Config.Restitution) * approach - (bumper ? Config.BumperKick : 0));
            remaining *= 1 - fraction;
        }
        b.Lower = b.Position.Y > 665;
    }
    private bool TryTrigger(BallState b, Vector2 motion, float firstWall)
    {
        float slotTime = PinballGeometry.SlotSweep(b.Position, motion, b.Radius, out int slot);
        if (slot >= 0 && slotTime <= firstWall)
        {
            b.Position += motion * slotTime;
            Multiply(b, slot); return true;
        }
        if (motion.Y <= 0) return false;
        float bottomTime = Math.Max(0, (AbilityY - b.Radius - b.Position.Y) / motion.Y);
        if (bottomTime > 1 || bottomTime > firstWall + 1e-5f) return false;
        b.Position += motion * bottomTime;
        Redeem(b, AbilityAt(b.Position.X)); return true;
    }
    private void CorrectPosition(BallState b, Vector2 correction)
    {
        if (correction.LengthSquared() < 1e-12f || b.Phase != BallPhase.Flying) return;
        float fraction = PinballGeometry.StaticSweep(b.Position, correction, b.Radius, out Vector2 normal, out _);
        if (TryTrigger(b, correction, fraction)) return;
        if (float.IsPositiveInfinity(fraction)) b.Position += correction;
        else b.Position += correction * fraction + normal * 0.001f;
        b.Lower = b.Position.Y > 665;
    }
    public void Multiply(BallState b, int slot)
    {
        if (b.Phase != BallPhase.Flying) return;
        b.Value *= PinballGeometry.Slots[slot].Factor; SlotValues[slot] = b.Value; SlotFlash[slot] = Time;
        MultiplierHits++; Return(b);
    }
    public void Redeem(BallState b, Ability ability)
    {
        if (b.Phase != BallPhase.Flying) return;
        if(b.IsDeath && (ability==Ability.Gun || ability==Ability.Shield))
        {
            b.Position.Y=AbilityY-b.Radius-0.01f;
            b.Velocity.Y=-Math.Max(180,Math.Abs(b.Velocity.Y)*Config.Restitution);
            return;
        }
        var ev = new Redemption(Time, b.Id, b.Faction, ability, b.Value,b.IsDeath);
        Redeemed[b.Faction, (int)ability] += b.Value;
        Events.Enqueue(ev); while (Events.Count > 100) Events.Dequeue();
        AbilityHits++;
        if(b.IsDeath)b.Phase=BallPhase.Inactive;
        else {b.Value=BaseBallValue;Return(b);}
        AbilityRedeemed?.Invoke(ev);
    }
    public double SacrificeLargest(int faction)
    {
        BallState best=null;
        foreach(var b in Balls)
            if(b.Phase!=BallPhase.Inactive&&b.Faction==faction&&!b.IsDeath&&(best==null||b.Value>best.Value))best=b;
        if(best==null)return 0;
        double value=best.Value;best.Phase=BallPhase.Inactive;return value;
    }
    public void Eliminate(int faction)
    {
        if(Eliminated[faction])return;
        Eliminated[faction]=true;
        BallState survivor=null;double sum=PendingRewards[faction]*BaseBallValue;PendingRewards[faction]=0;
        foreach(var b in Balls)
        {
            if(b.Phase==BallPhase.Inactive||b.Faction!=faction)continue;
            sum+=b.Value;survivor??=b;b.Phase=BallPhase.Inactive;
        }
        if(sum<=0)return;
        if(survivor==null){PendingDeath[faction]=sum;DrainRewards();return;}
        // Preserve one identity; merge at the launcher so differently sized input balls cannot embed the result in a rail.
        survivor.IsDeath=true;survivor.Value=sum;
        survivor.Phase=BallPhase.Waiting;survivor.ReadyAt=Time;survivor.Position=survivor.Previous=new(340,52);
        survivor.Velocity=Vector2.Zero;survivor.Lower=false;UpdateSize(survivor);
    }
    public bool HasDeathBalls
    {
        get {foreach(double v in PendingDeath)if(v>0)return true;foreach(var b in Balls)if(b.IsDeath&&b.Phase!=BallPhase.Inactive)return true;return false;}
    }
    private void Return(BallState b)
    {
        b.Phase = BallPhase.Waiting; b.ReadyAt = Time + 0.65; b.Lower = false;
        b.Position = b.Previous = new(340,52); b.Velocity = Vector2.Zero;
    }
}

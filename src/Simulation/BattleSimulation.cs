using System;
using System.Collections.Generic;
using Godot;

namespace TerritoryWar;

public sealed class FactionState
{
    public bool Alive = true, ReviveUsed;
    public double EliminatedAt = -1, SurvivalTime, DamageTaken;
    public int Revivals;
    public double Shield, GunReserve, Experience, NextShot, AreaTime, TotalExperience;
    public double PaintSpent, PaintRemainder, ShieldDamage, GunSpent;
    public int Level, ExtraBalls, UnshieldedHits;
    public double Threshold => 56000.0 * (Level + 2);
}
public sealed class ProjectileState
{
    public WeaponKind Kind;
    // Set once when a big ball crosses the neutron threshold; collapse is irreversible.
    public bool Neutron;
    public double InitialValue;
    // Visual event timestamp; never used by combat or economy decisions.
    public double LastPaintAt = double.NegativeInfinity;
    public float Radius, Mass;
    public Ability Source;
    public bool Death, Finale;
    public bool Active;
    public long Id;
    public int Faction;
    public double Value;
    public Vector2 Position, Previous, Velocity;
}
public sealed class StormWave
{
    public bool Death, Finale, Shotgun;
    public int Faction, Count;
    public double Budget, Due;
    public float Phase;
    // Per-pellet ragged pattern for shotgun volleys; null for storm waves.
    public float[] Angles, Distances, Speeds;
}

/// <summary>Deterministic combat, bounded entity pools and queued firepower.</summary>
public sealed partial class BattleSimulation
{
    public const int ProjectileCapacity = 2048;
    public const int MaxStormWaves = 5;
    public const int DeathSlot = 6, FinaleSlot = 7;
    public const float BaseRadius = 11;
    public static readonly Vector2[] Centers = { new(900,140), new(1700,140), new(900,940), new(1700,940) };
    public readonly TerritoryGrid Territory = new();
    public readonly FactionState[] Factions = new FactionState[4];
    public readonly ProjectileState[] Projectiles = new ProjectileState[ProjectileCapacity];
    public readonly List<StormWave> PendingWaves = new();
    public event Action<int> BaseHit;
    public readonly double[,] Invested = new double[4, FinaleSlot + 1];
    public readonly double[,] Damage = new double[4, FinaleSlot + 1];
    public readonly Queue<string> Notices = new();
    public readonly SimulationConfig Config;
    public double Time { get; private set; }
    public long TotalShots { get; private set; }
    public long Interceptions { get; private set; }
    public int ActiveProjectiles { get; private set; }
    private long _nextId;
    private Random _gunRandom, _shotgunRandom;
    private readonly Action<int> _awardBall;
    public BattleSimulation(SimulationConfig config, Action<int> awardBall)
    {
        Config = config; _awardBall = awardBall;
        for (int i = 0; i < Factions.Length; i++) Factions[i] = new FactionState();
        for (int i = 0; i < Projectiles.Length; i++) Projectiles[i] = new ProjectileState();
        for (int i = 0; i < BigBalls.Length; i++) BigBalls[i] = new ProjectileState();
        Reset();
    }
    public void Reset()
    {
        Time = 0; TotalShots = _nextId = ActiveProjectiles = 0; Interceptions=0;
        _gunRandom=new Random(unchecked(Config.Seed ^ 0x47a21f));
        _shotgunRandom=new Random(unchecked(Config.Seed ^ 0x6c8e9f));
        PendingWaves.Clear(); Notices.Clear(); Array.Clear(Invested);Array.Clear(Damage);ResetAdvanced();
        Territory.Reset();
        for (int i = 0; i < 4; i++)
        {
            Factions[i] = new FactionState { Shield = Math.Max(0, Config.InitialShield) };
            Territory.SeedDisk(Centers[i], Config.InitialTerritoryRadius, i);
        }
        foreach (var p in Projectiles)
        {
            p.Active = false; p.Id = 0; p.Faction = 0; p.Value = 0;
            p.Kind=WeaponKind.Normal;p.Neutron=false;
            p.Position = p.Previous = p.Velocity = Vector2.Zero;
        }
    }
    public float Angle(int faction) => (float)Time * Config.BaseRotationSpeed + faction * 1.8f;
    public double PaintCost => Config.PaintCostPerArea * Math.Pow(2, Math.Floor(Math.Max(0, Time - 60) / Math.Max(1, Config.PaintCostTimeScale) + 1e-9));
    public float ShieldRadius(int faction) => RadiusForShield(Factions[faction].Shield);
    public float RadiusForShield(double shield)
    {
        if (shield <= 0) return 0;
        float log=(float)Math.Log2(1 + shield);
        return Math.Clamp(Config.ShieldBaseRadius + Config.ShieldLogRadius * log + Config.ShieldLogQuadratic * log * log, 0, Config.ShieldMaxRadius);
    }
    public double ShieldForRadius(double radius)
    {
        if (radius <= 0) return 0;
        if (radius >= Config.ShieldMaxRadius) return double.PositiveInfinity;
        double b=Config.ShieldLogRadius, c=Config.ShieldLogQuadratic;
        if (c > 1e-9)
        {
            double log=(-b + Math.Sqrt(b * b + 4 * c * (radius - Config.ShieldBaseRadius))) / (2 * c);
            return Math.Max(0, Math.Pow(2, Math.Max(0, log)) - 1);
        }
        return Math.Max(0, Math.Pow(2, (radius - Config.ShieldBaseRadius) / Math.Max(1e-9, b)) - 1);
    }
    public void Accept(Redemption ev)
    {
        if (ev.Value <= 0 || !double.IsFinite(ev.Value)) throw new ArgumentOutOfRangeException(nameof(ev));
        if(!Factions[ev.Faction].Alive&&!ev.IsDeath)return;
        if(ev.IsDeath&&(ev.Ability==Ability.Shield||ev.Ability==Ability.Gun))return;
        Invested[ev.Faction,(int)ev.Ability]+=ev.Value;
        if(ev.IsDeath)Invested[ev.Faction,DeathSlot]+=ev.Value;
        switch (ev.Ability)
        {
            case Ability.Shield: Factions[ev.Faction].Shield += ev.Value; break;
            case Ability.Gun: Factions[ev.Faction].GunReserve += ev.Value; break;
            case Ability.Storm: QueueStorm(ev.Faction, ev.Value,ev.IsDeath); break;
            case Ability.Shotgun: QueueShotgun(ev.Faction, ev.Value,ev.IsDeath); break;
            case Ability.BigBall: QueueSpecial(ev,WeaponKind.BigBall);break;
            case Ability.Sniper: QueueSpecial(ev,WeaponKind.Sniper);break;
        }
        FlushSpecial();
    }
    private void QueueStorm(int faction, double budget,bool death=false,bool finale=false)
    {
        int perWave = Math.Clamp(Config.StormProjectilesPerWave, 1, ProjectileCapacity);
        int count = (int)Math.Clamp(Math.Ceiling(budget / Math.Max(1, Config.StormBaseProjectileValue)), 1, perWave * MaxStormWaves);
        int waves = (count + perWave - 1) / perWave;
        double remaining = budget;
        int remainingCount = count;
        for (int wave = 0; wave < waves; wave++)
        {
            int n = Math.Min(perWave, remainingCount);
            double share = wave == waves - 1 ? remaining : budget * n / count;
            PendingWaves.Add(new StormWave { Faction = faction, Count = n, Budget = share,
                Due = Time + wave * Config.StormWaveInterval, Phase = Angle(faction) + wave * 0.17f,Death=death,Finale=finale });
            remaining -= share; remainingCount -= n;
        }
    }
    private void QueueShotgun(int faction, double budget,bool death=false,bool finale=false)
    {
        if(budget<=0||!double.IsFinite(budget))return;
        // One volley spends the whole redemption at once; the pellets share the value.
        int pellets=Math.Clamp(Config.ShotgunPellets,1,ProjectileCapacity);
        float spread=Mathf.DegToRad(Mathf.Clamp(Config.ShotgunSpreadDegrees,0,80));
        float angleJitter=Mathf.DegToRad(Mathf.Clamp(Config.ShotgunAngleJitterDegrees,0,45));
        float spawnSpread=Mathf.Max(0,Config.ShotgunSpawnSpread);
        float speedJitter=Mathf.Clamp(Config.ShotgunSpeedJitter,0,0.5f);
        float phase=Angle(faction);
        var wave=new StormWave { Faction=faction, Count=pellets, Budget=budget, Due=Time,
            Phase=phase, Death=death, Finale=finale, Shotgun=true,
            Angles=new float[pellets], Distances=new float[pellets], Speeds=new float[pellets] };
        for(int j=0;j<pellets;j++)
        {
            // Keep the fan as a base, then jitter angle, launch distance and speed for a ragged front.
            float fan = pellets<=1?0:((float)j/(pellets-1)-0.5f)*spread;
            float jitter=(float)(_shotgunRandom.NextDouble()*2-1)*angleJitter;
            wave.Angles[j]=phase+fan+jitter;
            wave.Distances[j]=17+(float)_shotgunRandom.NextDouble()*spawnSpread;
            wave.Speeds[j]=1+(float)(_shotgunRandom.NextDouble()*2-1)*speedJitter;
        }
        PendingWaves.Add(wave);
    }
    public bool Spawn(int faction, Vector2 position, Vector2 velocity, double value)
        => SpawnWeapon(faction,position,velocity,value,WeaponKind.Normal,Ability.Gun);
    private void Fire()
    {
        FlushSpecial();
        // Whole waves wait for capacity; no budget is consumed by a blocked spawn.
        for (int i = 0; i < PendingWaves.Count;)
        {
            var wave = PendingWaves[i];
            if (wave.Due > Time || ProjectileCapacity - ActiveProjectiles < wave.Count) { i++; continue; }
            double remaining = wave.Budget;
            for (int j = 0; j < wave.Count; j++)
            {
                Vector2 direction;Vector2 position;float speed=Config.ProjectileSpeed;
                if(wave.Shotgun)
                {
                    direction=Vector2.FromAngle(wave.Angles[j]);
                    position=Centers[wave.Faction]+direction*wave.Distances[j];
                    speed*=wave.Speeds[j];
                }
                else
                {
                    direction = Vector2.FromAngle(wave.Phase + Mathf.Tau * j / wave.Count);
                    position=Centers[wave.Faction]+direction*17;
                }
                double value = j == wave.Count - 1 ? remaining : wave.Budget / wave.Count;
                SpawnWeapon(wave.Faction,position,direction*speed,value,
                    WeaponKind.Normal,wave.Shotgun?Ability.Shotgun:Ability.Storm,wave.Death,wave.Finale);
                remaining -= value;
            }
            PendingWaves.RemoveAt(i);
        }
        for (int f = 0; f < 4; f++)
        {
            var state = Factions[f];
            if (!state.Alive || state.GunReserve <= 0 || state.NextShot > Time || ActiveProjectiles == ProjectileCapacity) continue;
            // Preserve the old 0.12-second reserve decay while splitting it into finer shots.
            double interval=Math.Max(PinballSimulation.StepSeconds,Config.GunInterval);
            double fraction=Math.Clamp(Config.GunReserveConsumption,0,1);
            double cost=fraction>0
                ? (state.GunReserve+Config.GunBaseConsumption/fraction)*(1-Math.Pow(1-fraction,interval/0.12))
                : Config.GunBaseConsumption*interval/0.12;
            cost=Math.Min(state.GunReserve,cost);
            if (cost <= 0) continue;
            Vector2 direction = Vector2.FromAngle(Angle(f));
            float spread=Mathf.DegToRad(Math.Clamp(Config.GunSpreadDegrees,0,45));
            Vector2 shotDirection=direction.Rotated((float)(_gunRandom.NextDouble()*2-1)*spread);
            if (Spawn(f, Centers[f] + direction * 17, shotDirection * Config.ProjectileSpeed, cost))
            {
                state.GunReserve -= cost; state.GunSpent += cost;
                state.NextShot = Time + interval - 0.0000001;
            }
        }
    }
    public void Tick()
    {
        Time += PinballSimulation.StepSeconds;
        Fire();
        MoveWeapons((float)PinballSimulation.StepSeconds);
        PaintShields();
        IntegrateExperience(PinballSimulation.StepSeconds);
    }
    private void PaintShields()
    {
        double cost=PaintCost;
        for(int f=0;f<Factions.Length;f++)
        {
            var state=Factions[f];
            if(!state.Alive||state.Shield<=0)continue;
            // Snapshot the current radius for this step; spending immediately shrinks the shield.
            float radius=ShieldRadius(f);
            double budget=state.Shield;
            state.PaintSpent+=Territory.PaintDisk(Centers[f],radius,f,cost,ref budget);
            state.Shield=budget;
        }
    }
    public void IntegrateExperience(double seconds)
    {
        for (int f = 0; f < 4; f++)
        {
            var state = Factions[f];
            if(!state.Alive)continue;
            state.SurvivalTime+=seconds;
            double areaTime = Territory.Counts[f] * TerritoryGrid.CellArea * seconds;
            state.AreaTime += areaTime;
            double earned = areaTime * Config.ExperiencePerAreaSecond;
            state.Experience += earned; state.TotalExperience += earned;
            while (state.Experience >= state.Threshold)
            {
                state.Experience -= state.Threshold; state.Level++; state.ExtraBalls++;
                _awardBall(f);
            }
        }
    }
    public static float CircleTime(Vector2 position, Vector2 velocity, Vector2 center, float radius)
    {
        Vector2 offset = position - center;
        float c = offset.LengthSquared() - radius * radius;
        if (c <= 0) return 0;
        float a = velocity.LengthSquared(), b = offset.Dot(velocity);
        if (a < 0.00001f || b >= 0) return -1;
        float discriminant = b * b - a * c;
        return discriminant < 0 ? -1 : (-b - MathF.Sqrt(discriminant)) / a;
    }
    private void Retire(ProjectileState p)
    {
        if (!p.Active) return;
        p.Active = false;
        if(p.Kind==WeaponKind.BigBall)ActiveBigBalls--;else ActiveProjectiles--;
    }
}


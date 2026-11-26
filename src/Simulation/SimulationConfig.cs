using Godot;

namespace TerritoryWar;

// Fitting parameters, not claims about the reference game's internal formulas.
[GlobalClass]
public partial class SimulationConfig : Resource
{
    [Export] public int Seed { get; set; } = 1701;
    [Export] public float Gravity { get; set; } = 360;
    [Export] public float LiftForce { get; set; } = 690;
    [Export] public float Restitution { get; set; } = 0.70f;
    [Export] public float LaunchInterval { get; set; } = 0.24f;
    [Export] public float LaunchSpeed { get; set; } = 225;
    [Export] public float ConveyorSpeed { get; set; } = 13;
    [Export] public float RadiusBase { get; set; } = 7;
    [Export] public float RadiusValueFactor { get; set; } = 0.13f;
    [Export] public float RadiusValueExponent { get; set; } = 1.5f;
    [Export] public float RadiusPivotDoublingSeconds { get; set; } = 120f;
    [Export] public float RadiusMin { get; set; } = 3;
    [Export] public float MassValueFactor { get; set; } = 0.34f;
    [Export] public float TimeScale { get; set; } = 900;
    // Scales how strongly match time shrinks radius and advances the value pivot; 1 = original, 0 = no time effect.
    [Export] public float RadiusTimeMultiplier { get; set; } = 1;
    [Export] public float BallValueDoublingSeconds { get; set; } = 60;
    [Export] public float BumperKick { get; set; } = 42;

    [ExportGroup("Multiplier baffles")]
    [Export] public float BafflePeriod { get; set; } = 8f;
    [Export] public float BaffleLiftTime { get; set; } = 0.5f;
    [Export] public float BaffleHoldTime { get; set; } = 0.2f;
    [Export] public float BaffleRetractTime { get; set; } = 0.7f;
    [Export] public float BaffleStagger { get; set; } = 0.35f;
    [Export] public float BaffleSlant { get; set; } = 18f;
    [Export] public float BaffleEjectSpeed { get; set; } = 120f;

    [ExportGroup("Battlefield fitting parameters")]
    [Export] public double InitialShield { get; set; } = 20_000_000;
    [Export] public float InitialTerritoryRadius { get; set; } = 64;
    [Export] public double PaintCostPerArea { get; set; } = 0.02;
    [Export] public float PaintCostTimeScale { get; set; } = 240;
    // Per-cell cost holds for the first minute, then jumps x2 every PaintCostTimeScale seconds.
    [Export] public double ExperiencePerAreaSecond { get; set; } = 0.002;
    [Export] public float ProjectileSpeed { get; set; } = 360;
    [Export] public float ProjectileRadius { get; set; } = 3;
    [Export] public float ProjectilePaintRadius { get; set; } = 6;
    [Export] public int ShotgunPellets { get; set; } = 24;
    [Export] public float ShotgunSpreadDegrees { get; set; } = 24;
    [Export] public float ShotgunAngleJitterDegrees { get; set; } = 2;
    [Export] public float ShotgunSpawnSpread { get; set; } = 26;
    [Export] public float ShotgunSpeedJitter { get; set; } = 0.12f;
    [Export] public float GunInterval { get; set; } = 0.025f;
    [Export] public float GunSpreadDegrees { get; set; } = 5f;
    [Export] public double GunBaseConsumption { get; set; } = 1;
    [Export] public double GunReserveConsumption { get; set; } = 0.025;
    [Export] public float BaseRotationSpeed { get; set; } = 0.4f;
    [Export] public int StormProjectilesPerWave { get; set; } = 192;
    [Export] public double StormBaseProjectileValue { get; set; } = 4;
    [Export] public float StormWaveInterval { get; set; } = 1.5f;
    [Export] public float ShieldBaseRadius { get; set; } = -1.396865f;
    [Export] public float ShieldLogRadius { get; set; } = 2.229502f;
    [Export] public float ShieldLogQuadratic { get; set; } = 0.1673627f;
    [Export] public float ShieldMaxRadius { get; set; } = 240;
    // Presentation-only easing time for the shield shell; collision always uses the exact value.
    [Export] public float ShieldRadiusEaseSeconds { get; set; } = 0.2f;
    // Shield radius = base + log*L + quadratic*L^2 (L=log2(1+shield)): r(1)=1, meets the previous curve at 4M, caps at 240 at 4B.
    [ExportGroup("Advanced weapons")]
    [Export] public float BigBallSpeed { get; set; } = 95;
    [Export] public float BigBallRadiusBase { get; set; } = 8;
    [Export] public float BigBallRadiusLog { get; set; } = 2;
    [Export] public float BigBallMaxRadius { get; set; } = 50;
    [Export] public float ProjectileImpulse { get; set; } = 9;
    [Export] public float BigBallRestitution { get; set; } = 1.0f;
    [Export] public double BigBallShieldTransferRatio { get; set; } = 0.8;

    [ExportGroup("Neutron star")]
    // A big ball that reaches this value collapses once and stays a compact, massive body.
    [Export] public double BigBallNeutronThreshold { get; set; } = 1_000_000_000;
    [Export] public float BigBallNeutronRadius { get; set; } = 7;
    // Acceleration on projectiles is strength / distance^2, capped by the maximum.
    [Export] public float NeutronGravityStrength { get; set; } = 24_000_000;
    [Export] public float NeutronGravityMaxAcceleration { get; set; } = 6000;

    [ExportGroup("Sniper")]
    [Export] public float SniperSpeed { get; set; } = 720;
    [Export] public int SniperFragments { get; set; } = 8;
    [Export] public float FragmentSpread { get; set; } = 1.4f;
    [Export] public bool FinaleReserveStorm { get; set; } = true;
}

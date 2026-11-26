using System;
using Godot;
namespace TerritoryWar;

public partial class Battlefield
{
    private readonly double[] _shieldValues=new double[4],_shieldTimes=new double[4];
    private readonly float[] _shieldPulses=new float[4];
    private readonly float[] _shieldDisplayRadii=new float[4];
    private readonly bool[] _shieldRadiusReady=new bool[4];
    private readonly BattleSimulation[] _shieldBattles=new BattleSimulation[4];
    // Presentation-only exponential ease; a zero frame delta or zero duration snaps immediately.
    public static float EaseShieldRadius(float current,float target,float frameDelta,float easeSeconds)
    {
        if(frameDelta<=0||easeSeconds<=0.0001f)return target;
        float alpha=1-MathF.Exp(-Math.Min(frameDelta,0.1f)/easeSeconds);
        float radius=Mathf.Lerp(current,target,alpha);
        return MathF.Abs(radius-target)<0.02f?target:radius;
    }
    private float AnimateShield(int faction,float frameDelta)
    {
        double now=Simulation.Time,value=Battle.Factions[faction].Shield;
        bool reset=_shieldBattles[faction]!=Battle||now<_shieldTimes[faction];
        float dt=(float)Math.Max(0,now-_shieldTimes[faction]);
        float change=reset?0:(float)Math.Abs(Math.Log((1+value)/(1+_shieldValues[faction])));
        _shieldPulses[faction]=reset?0:Math.Max(_shieldPulses[faction]*MathF.Exp(-dt*5),Math.Clamp(change*5,0,1));
        _shieldMaterials[faction].SetShaderParameter("energy_pulse",_shieldPulses[faction]);
        _shieldMaterials[faction].SetShaderParameter("phase_offset",faction*1.73f);
        _shieldTimes[faction]=now;_shieldValues[faction]=value;_shieldBattles[faction]=Battle;
        float target=Battle.ShieldRadius(faction);
        if(reset||!_shieldRadiusReady[faction])
        {
            _shieldDisplayRadii[faction]=target;
            _shieldRadiusReady[faction]=true;
        }
        else _shieldDisplayRadii[faction]=EaseShieldRadius(_shieldDisplayRadii[faction],target,frameDelta,Battle.Config.ShieldRadiusEaseSeconds);
        return _shieldDisplayRadii[faction];
    }
}

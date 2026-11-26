using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace TerritoryWar;

public partial class Main
{
    // Explicit capture-only fixture: never runs in a normal match.
    private async Task CaptureAnimation()
    {
        bool showcase=OS.GetCmdlineUserArgs().Contains("--capture-showcase");
        bool gun=OS.GetCmdlineUserArgs().Contains("--capture-gun");
        bool paint=OS.GetCmdlineUserArgs().Contains("--capture-paint");
        bool neutron=OS.GetCmdlineUserArgs().Contains("--capture-neutron");
        bool shotgun=OS.GetCmdlineUserArgs().Contains("--capture-shotgun");
        bool baffle=OS.GetCmdlineUserArgs().Contains("--capture-baffle");
        bool flow=paint||OS.GetCmdlineUserArgs().Contains("--capture-flow");
        bool toggle=OS.GetCmdlineUserArgs().Contains("--capture-toggle-particles");
        string directory="res://.godot/animation-"+(paint?"paint":neutron?"neutron":shotgun?"shotgun":baffle?"baffle":flow?"flow":gun?"gun":showcase?"showcase":"match")+(toggle?"-toggle":"");
        DirAccess.MakeDirRecursiveAbsolute(directory);
        GetNode<Control>("HUD").Hide(); // Keep cursor hover from obscuring recorded effects.
        var overlay=new CanvasLayer {Layer=30};AddChild(overlay);
        var caption=new Label {Position=new Vector2(1020,35),Theme=GetNode<Control>("HUD").Theme};overlay.AddChild(caption);
        if(showcase)
        {
            Match=new MatchSimulation(new SimulationConfig {InitialShield=0,InitialTerritoryRadius=0});
            Simulation=Match.Pinball;_board.Simulation=Simulation;_battle.Simulation=Simulation;_battle.Battle=Match.Battle;
            Match.Battle.Territory.SeedDisk(new Vector2(1150,330),250,0);
            for(int i=0;i<21;i++)Match.Battle.SpawnWeapon(1,new Vector2(1150,510),Vector2.FromAngle((i-10)*0.045f)*330,
                10000,WeaponKind.Normal,Ability.Storm);
            Match.Battle.SpawnWeapon(2,new Vector2(1150,750),new Vector2(490,-35),1e6,WeaponKind.Sniper,Ability.Sniper);
            Match.Battle.SpawnWeapon(3,new Vector2(1550,900),new Vector2(-100,-30),10000,WeaponKind.BigBall,Ability.BigBall);
        }
        if(gun)
        {
            Match=new MatchSimulation(new SimulationConfig {InitialShield=0,InitialTerritoryRadius=0,BaseRotationSpeed=0});
            Simulation=Match.Pinball;_board.Simulation=Simulation;_battle.Simulation=Simulation;_battle.Battle=Match.Battle;
            Match.Battle.Territory.SeedDisk(BattleSimulation.Centers[0],1000,0);
            Match.Battle.Factions[0].GunReserve=10000;
        }
        if(neutron)
        {
            Match=new MatchSimulation(new SimulationConfig {InitialShield=0,InitialTerritoryRadius=0,PaintCostPerArea=0});
            Simulation=Match.Pinball;_board.Simulation=Simulation;_battle.Simulation=Simulation;_battle.Battle=Match.Battle;
            Vector2 center=new(1400,560);
            Match.Battle.SpawnWeapon(0,center,Vector2.Zero,2e9,WeaponKind.BigBall,Ability.BigBall);
            for(int i=0;i<48;i++)
            {
                Vector2 offset=Vector2.FromAngle(i*Mathf.Tau/48);
                Vector2 tangent=offset.Orthogonal()*260;
                Match.Battle.SpawnWeapon(1,center+offset*430,tangent,50,WeaponKind.Normal,Ability.Sniper);
            }
        }
        if(shotgun)
        {
            Match=new MatchSimulation(new SimulationConfig {InitialShield=0,InitialTerritoryRadius=0,PaintCostPerArea=0});
            Simulation=Match.Pinball;_board.Simulation=Simulation;_battle.Simulation=Simulation;_battle.Battle=Match.Battle;
            for(int f=0;f<4;f++)Match.Battle.Accept(new Redemption(0,f*10,f,Ability.Shotgun,2400));
        }
        if(baffle)
        {
            Match=new MatchSimulation(new SimulationConfig());
            Simulation=Match.Pinball;_board.Simulation=Simulation;_battle.Simulation=Simulation;_battle.Battle=Match.Battle;
            foreach(var b in Match.Pinball.Balls)b.Phase=BallPhase.Inactive;
            var lifted=Match.Pinball.Balls[0];
            lifted.Phase=BallPhase.Flying;lifted.Faction=0;lifted.Value=1e6;
            lifted.Position=lifted.Previous=new Vector2(241,515);lifted.Velocity=Vector2.Zero;lifted.Lower=true;
            Match.Pinball.UpdateSize(lifted);
        }
        if(flow)
        {
            Match=new MatchSimulation(new SimulationConfig());
            Simulation=Match.Pinball;_board.Simulation=Simulation;_battle.Simulation=Simulation;_battle.Battle=Match.Battle;
            for(int y=0;y<TerritoryGrid.Height;y++)for(int x=0;x<TerritoryGrid.Width;x++)
                Match.Battle.Territory.SetOwner(y*TerritoryGrid.Width+x,
                    (x<TerritoryGrid.Width/2+(paint?18*MathF.Sin(y*0.035f):0)?0:1)+
                    (y<TerritoryGrid.Height/2+(paint?22*MathF.Sin(x*0.03f+1):0)?0:2));
            for(int i=0;i<120*8;i++)StepMatch();
        }
        int frames=paint?150:neutron?60:shotgun?30:baffle?30:90;
        for(int frame=0;frame<frames;frame++)
        {
            if(flow&&!paint&&frame==30)Match.Battle.Factions[0].Shield*=0.75;
            if(paint&&frame>=6&&frame<=24)Match.Battle.Territory.SeedDisk(new Vector2(1360,630),35+(frame-6)*8,0);
            if(showcase&&frame%5==0)Match.Battle.SpawnWeapon(0,new Vector2(1150,330),new Vector2(480,0),10000,WeaponKind.Normal,Ability.Gun);
            if(toggle&&frame==30)SetParticlesEnabled(false);
            if(toggle&&frame==60)SetParticlesEnabled(true);
            caption.Text=(paint?"新涂色高亮 → 逐渐回落":neutron?"中子星坍缩 · 射弹引力弯折":shotgun?"霰弹 · 随机角度与前后错落":baffle?"升降挡板 · 显示与碰撞对齐":flow?"边缘流动 · 螺旋吸入 · 动态护盾":gun?"机枪散布 ±5° · 软尾亮头":showcase?"射弹动画验证":"自然对局动画")+$" · 粒子{(ParticlesEnabled?"开启":"关闭")}";
            for(int step=0;step<4;step++)StepMatch();
            Refresh();
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            GetViewport().GetTexture().GetImage().SavePng($"{directory}/frame-{frame:000}.png");
        }
        GD.Print($"ANIMATION CAPTURE PASS: {directory}, {frames} frames at 30 fps; particles={ParticlesEnabled}");
        overlay.QueueFree();
    }
}

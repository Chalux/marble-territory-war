using Godot;
using System;

namespace TerritoryWar;

// Kept outside the HUD so its visibility is independent of the spectator controls.
public partial class DebugPanel : CanvasLayer
{
    private Main _main;
    private PanelContainer _panel;
    private Label _stats, _message;
    private Button _pause;
    private OptionButton _faction;
    private SpinBox _amount, _seed, _radiusTime;
    private double _refreshIn;

    public void Initialize(Main main, Theme theme)
    {
        _main=main;Layer=20;
        _panel=new PanelContainer {Position=new Vector2(24,200),CustomMinimumSize=new Vector2(460,0),Theme=theme};
        var style=new StyleBoxFlat {BgColor=new Color("101b2bf5"),BorderColor=new Color("47617b"),
            BorderWidthLeft=1,BorderWidthRight=1,BorderWidthTop=1,BorderWidthBottom=1,
            CornerRadiusTopLeft=10,CornerRadiusTopRight=10,CornerRadiusBottomLeft=10,CornerRadiusBottomRight=10,
            ContentMarginLeft=20,ContentMarginRight=20,ContentMarginTop=16,ContentMarginBottom=16};
        _panel.AddThemeStyleboxOverride("panel",style);
        AddChild(_panel);
        var body=new VBoxContainer();body.AddThemeConstantOverride("separation",12);_panel.AddChild(body);
        var title=new HBoxContainer();body.AddChild(title);
        title.AddChild(new Label {Text="调试面板  [O]",SizeFlagsHorizontal=Control.SizeFlags.ExpandFill});
        AddButton(title,"关闭",()=>_panel.Hide());
        _stats=new Label();body.AddChild(_stats);
        var controls=new HBoxContainer();body.AddChild(controls);
        _pause=AddButton(controls,"暂停",()=>{_main.DebugTogglePause();UpdateStats();});
        AddButton(controls,"切换速度",()=>{_main.DebugChangeSpeed();UpdateStats();});
        AddButton(controls,"重新开始",()=>{
            _main.DebugReset();
            _seed.Value=_main.Config.Seed;
            _message.Text=$"已使用种子 {_main.Config.Seed} 重新开始";
            UpdateStats();
        });
        body.AddChild(new HSeparator());
        body.AddChild(new Label {Text="对局种子（相同种子可复现对局）"});
        _seed=new SpinBox {MinValue=0,MaxValue=int.MaxValue,Step=1,Value=_main.Config.Seed,
            SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};body.AddChild(_seed);
        var seedControls=new HBoxContainer();body.AddChild(seedControls);
        AddButton(seedControls,"应用并重开",()=>{_seed.Apply();ApplySeed((int)_seed.Value);});
        AddButton(seedControls,"随机种子并重开",()=>ApplySeed(_main.DebugRandomSeed()));
        body.AddChild(new HSeparator());
        body.AddChild(new Label {Text="补充数值（仅对存活阵营生效）"});
        var selection=new HBoxContainer();body.AddChild(selection);
        _faction=new OptionButton();
        for(int f=0;f<4;f++)_faction.AddItem(BattleSimulation.FactionName(f));
        _faction.ItemSelected+=_=>UpdateStats();
        selection.AddChild(_faction);
        _amount=new SpinBox {MinValue=1,MaxValue=1_000_000_000,Step=1,Value=10000,
            SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};selection.AddChild(_amount);
        var grants=new GridContainer {Columns=3};body.AddChild(grants);
        AddButton(grants,"增加风暴",()=>Grant(Ability.Storm));
        AddButton(grants,"增加机枪储备",()=>Grant(Ability.Gun));
        AddButton(grants,"增加护盾",()=>Grant(Ability.Shield));
        AddButton(grants,"增加大球",()=>Grant(Ability.BigBall));
        AddButton(grants,"增加狙击",()=>Grant(Ability.Sniper));
        AddButton(grants,"增加霰弹",()=>Grant(Ability.Shotgun));
        body.AddChild(new HSeparator());
        body.AddChild(new Label {Text="时间对弹珠半径的影响倍率（0 = 不受时间影响）"});
        var radiusControls=new HBoxContainer();body.AddChild(radiusControls);
        _radiusTime=new SpinBox {MinValue=0,MaxValue=4,Step=0.05,Value=_main.Config.RadiusTimeMultiplier,
            SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};radiusControls.AddChild(_radiusTime);
        AddButton(radiusControls,"应用",()=>{
            _radiusTime.Apply();
            _main.Config.RadiusTimeMultiplier=(float)_radiusTime.Value;
            _message.Text=$"半径时间倍率 {_radiusTime.Value:0.00}";
        });
        _message=new Label {Text="打开面板不会自动暂停对局"};body.AddChild(_message);
        _panel.Hide();
    }

    private static Button AddButton(Container parent,string text,Action action)
    {
        var button=new Button {Text=text,FocusMode=Control.FocusModeEnum.None};
        button.Pressed+=action;parent.AddChild(button);return button;
    }

    private void Grant(Ability ability)
    {
        int faction=_faction.Selected;
        if(_main.Match.Phase==MatchPhase.Finished){_message.Text="对局已结束，请先重新开始";return;}
        if(!_main.Match.Battle.Factions[faction].Alive){_message.Text="该阵营已淘汰";return;}
        _amount.Apply();
        _main.Match.Battle.Accept(new Redemption(Time:_main.Simulation.Time,BallId:-1,Faction:faction,Ability:ability,Value:_amount.Value));
        _message.Text=$"{BattleSimulation.FactionName(faction)}：{AbilityName(ability)} +{Main.Number(_amount.Value)}";
        UpdateStats();
    }

    private static string AbilityName(Ability ability)=>ability switch
    {
        Ability.Storm=>"风暴",Ability.Gun=>"机枪",Ability.Shield=>"护盾",
        Ability.BigBall=>"大球",Ability.Sniper=>"狙击",Ability.Shotgun=>"霰弹",_=>ability.ToString()
    };

    private void ApplySeed(int seed)
    {
        _main.DebugSetSeed(seed);
        _seed.Value=seed;
        _message.Text=$"已使用种子 {seed} 重新开始";
        UpdateStats();
    }

    public void Open()
    {
        if(_panel==null)return;
        _panel.Show();
        _seed.Value=_main.Config.Seed;
        _radiusTime.Value=_main.Config.RadiusTimeMultiplier;
        UpdateStats();
    }

    public override void _Input(InputEvent @event)
    {
        if(@event is not InputEventKey {Pressed:true,Echo:false} key||key.Keycode!=Key.O||_panel==null)return;
        if(_panel.Visible)_panel.Hide();else Open();
        GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        if(_panel==null||!_panel.Visible)return;
        _refreshIn-=delta;
        if(_refreshIn<=0){_refreshIn=0.25;UpdateStats();}
    }

    private void UpdateStats()
    {
        var battle=_main.Match.Battle;
        int faction=_faction.Selected;
        var state=battle.Factions[faction];
        _pause.Text=_main.DebugPaused?"继续":"暂停";
        _stats.Text=$"FPS {Engine.GetFramesPerSecond()}    速度 {_main.DebugSpeed}×    {(_main.DebugPaused?"已暂停":"运行中")}\n"+
            $"局内时间 {battle.Time:0.0} 秒    SEED {_main.Config.Seed}\n"+
            $"射弹 {battle.ActiveProjectiles}/{BattleSimulation.ProjectileCapacity}    大球 {battle.ActiveBigBalls}/{BattleSimulation.BigBallCapacity}    拦截 {battle.Interceptions}\n"+
            $"待发波次 {battle.PendingWaves.Count}    涂色 {battle.PaintCost*TerritoryGrid.CellArea:0.###} 点/格\n"+
            $"{BattleSimulation.FactionName(faction)} · {(state.Alive?"存活":"已淘汰")} · Lv.{state.Level}\n"+
            $"护盾 {Main.Number(state.Shield)}    储备 {Main.Number(state.GunReserve)}\n"+
            $"领土 {battle.Territory.Counts[faction]:N0} 格";
    }
}

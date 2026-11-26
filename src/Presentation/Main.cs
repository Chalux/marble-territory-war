using Godot;
using System;
using System.Linq;
namespace TerritoryWar;

public partial class Main : Node2D
{
    [Export] public SimulationConfig Config {get;set;}
    public static readonly Color[] FactionColors={new("ff3961"),new("21efaa"),new("36a8ff"),new("ffbd4b")};
    public PinballSimulation Simulation {get;private set;}
    public MatchSimulation Match {get;private set;}
    private PinballBoard _board; private Battlefield _battle;
    private Label _clock,_status,_events; private Button _pause,_speed;
    private double _accumulator; private bool _paused; private int _speedIndex;
    private ResultsView _results;private bool _reported;private Label _stage;
    private int _benchmarkFrames=-1;private readonly System.Collections.Generic.List<double> _frameTimes=new();
    private double _cpuTotal;
    private bool _stress;
    private int _stressShot;
    private long _previousFrame;
    private int _minShots=int.MaxValue, _maxShots;
    private bool _showInterface;
    private (bool Controls, bool Report)? _hudVisibility;
    public bool ParticlesEnabled {get;private set;}=true;
    private CheckButton _particles;
    private DebugPanel _debugPanel;
    private static readonly int[] Speeds={1,2,4,8};
    public override void _Ready()
    {
        if(OS.GetCmdlineUserArgs().Contains("--board-test")) { PinballBoardTests.RunStandalone(GetTree(), GetNode<PinballBoard>("PinballBoard")); return; }
        if(OS.GetCmdlineUserArgs().Contains("--self-test")) { SimulationTests.Run(GetTree(), GetNode<PinballBoard>("PinballBoard")); return; }
        if(OS.GetCmdlineUserArgs().Contains("--advanced-test")) { AdvancedTests.RunStandalone(GetTree()); return; }
        if(OS.GetCmdlineUserArgs().Contains("--complete-match")) { CompletionTest.Run(GetTree(),Config); return; }
        var args=OS.GetCmdlineUserArgs();
        if(!args.Contains("--capture")&&!args.Contains("--benchmark")&&!args.Contains("--benchmark-heavy"))
            Config.Seed=DebugRandomSeed();
        Match=new MatchSimulation(Config); Simulation=Match.Pinball;
        _board=GetNode<PinballBoard>("PinballBoard");_battle=GetNode<Battlefield>("Battlefield");
        _board.Simulation=Simulation;_battle.Simulation=Simulation;_battle.Battle=Match.Battle;
        _clock=GetNode<Label>("HUD/Clock");_status=GetNode<Label>("HUD/Status");_events=GetNode<Label>("HUD/Events");
        _stage=GetNode<Label>("HUD/Stage");_results=GetNode<ResultsView>("HUD/Results");_results.Restart=DebugReset;
        _pause=GetNode<Button>("HUD/Pause");_speed=GetNode<Button>("HUD/Speed");
        _pause.Pressed+=TogglePause;_speed.Pressed+=ChangeSpeed;
        GetNode<Button>("HUD/Reset").Pressed+=DebugReset;
        GetNode<Button>("HUD/Report").Pressed+=()=>{if(Match.Phase==MatchPhase.Finished)_results.Present(Match);};
        GetNode<Button>("HUD/NewSeed").Pressed+=()=>DebugSetSeed(DebugRandomSeed());
        _debugPanel=new DebugPanel();AddChild(_debugPanel);
        _debugPanel.Initialize(this,GetNode<Control>("HUD").Theme);
        _particles=GetNode<CheckButton>("HUD/Particles");
        _particles.Toggled+=SetParticlesEnabled;
        SetParticlesEnabled(!OS.GetCmdlineUserArgs().Contains("--no-particles"));
        Refresh();
        if(args.Contains("--capture-debug")) _debugPanel.Open();
        if(OS.GetCmdlineUserArgs().Contains("--capture")) CapturePreview();
        _stress=OS.GetCmdlineUserArgs().Contains("--benchmark-heavy");
        if(_stress || OS.GetCmdlineUserArgs().Contains("--benchmark"))
        {
            for(int i=0;i<120*(_stress?10:90);i++)StepMatch();
            _benchmarkFrames=0;Refresh();
        }
    }
    private void StepMatch()
    {
        Match.Tick();
        _board.ObserveStep();
        _battle.ObserveStep();
    }
    private async void CapturePreview()
    {
        double seconds=30;int report=-1;
        foreach(string arg in OS.GetCmdlineUserArgs())
        {
            if(arg.StartsWith("--capture-seconds="))double.TryParse(arg.Split('=')[1],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out seconds);
            if(arg.StartsWith("--capture-report="))int.TryParse(arg.Split('=')[1],out report);
        }
        int steps=(int)Math.Round(120*Math.Clamp(seconds,1,1800));
        for(int i=0;i<steps;i++)StepMatch();
        if(OS.GetCmdlineUserArgs().Contains("--capture-reset"))
        {
            Reset();
            for(int i=0;i<steps;i++)StepMatch();
        }
        _paused=true; Refresh();
        if(OS.GetCmdlineUserArgs().Contains("--capture-animation")) {await CaptureAnimation();GetTree().Quit();return;}
        if(report>=0&&Match.Phase!=MatchPhase.Finished){GD.PushError("Report capture requires a naturally finished match");GetTree().Quit(1);return;}
        if(report>=0&&Match.Phase==MatchPhase.Finished)_results.SelectTab(report);
        await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng(report>=0?$"res://.godot/report-{report}.png":"res://.godot/preview.png");
        GetTree().Quit();
    }
    private void TogglePause(){_paused=!_paused;_pause.Text=_paused?"继续  [空格]":"暂停  [空格]";}
    public void SetParticlesEnabled(bool enabled)
    {
        ParticlesEnabled=enabled;
        _particles?.SetPressedNoSignal(enabled);
        _battle.SetParticlesEnabled(enabled);
        _board.ParticlesEnabled=enabled;
    }
    internal bool DebugPaused=>_paused;
    internal int DebugSpeed=>Speeds[_speedIndex];
    internal void DebugTogglePause()=>TogglePause();
    internal void DebugChangeSpeed()=>ChangeSpeed();
    internal void DebugReset()=>DebugSetSeed(DebugRandomSeed());
    internal void DebugSetSeed(int seed){Config.Seed=seed;Reset();}
    internal int DebugRandomSeed()
    {
        int seed;
        do{seed=Random.Shared.Next(0,int.MaxValue);}while(seed==Config.Seed);
        return seed;
    }
    private void ChangeSpeed(){_speedIndex=(_speedIndex+1)%Speeds.Length;_speed.Text=$"{Speeds[_speedIndex]}×  [Tab]";}
    private void Reset(){Match.Reset();_accumulator=0;_reported=false;_paused=false;_pause.Text="暂停  [空格]";_results.Hide();Refresh();}
    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if(@event is not InputEventKey key || !key.Pressed || key.Echo)return;
        if(key.Keycode==Key.H)_showInterface=!_showInterface;
        if(key.Keycode==Key.P)SetParticlesEnabled(!ParticlesEnabled);
        if(key.Keycode==Key.Space)TogglePause();
        if(key.Keycode==Key.Tab)ChangeSpeed();
        if(key.Keycode==Key.R)DebugReset();
    }
    public override void _Process(double delta)
    {
        if(Simulation==null)return;
        var mouse=GetGlobalMousePosition();
        bool controls=_showInterface || (mouse.X>1000 && mouse.X<1800 && mouse.Y>980);
        bool report=controls && Match.Phase==MatchPhase.Finished;
        if(_hudVisibility!=(controls,report))
        {
            foreach(var child in GetNode<Control>("HUD").GetChildren())
                if(child is CanvasItem item && child!=_results)
                    item.Visible=child.Name=="ControlsHint" || (child.Name=="Report"?report:controls);
            _hudVisibility=(controls,report);
        }
        long begin=System.Diagnostics.Stopwatch.GetTimestamp();
        if(_stress)
        {
            for(int tick=0;tick<2;tick++)
            {
                StepMatch();
                while(Match.Battle.ActiveProjectiles<512)
                {
                    int faction=_stressShot%4;
                    var direction=Vector2.FromAngle(_stressShot++*2.399963f);
                    if(!Match.Battle.SpawnWeapon(faction,BattleSimulation.Centers[faction]+direction*18,
                        direction*Config.ProjectileSpeed,20,WeaponKind.Normal,
                        (_stressShot/4)%2==0?Ability.Gun:Ability.Storm))break;
                }
            }
        }
        else if(!_paused)
        {
            _accumulator+=Math.Min(delta,0.25)*Speeds[_speedIndex];
            while(_accumulator>=PinballSimulation.StepSeconds){StepMatch();_accumulator-=PinballSimulation.StepSeconds;}
        }
        Refresh();
        if(_benchmarkFrames>=0)
        {
            _benchmarkFrames++;
            long now=System.Diagnostics.Stopwatch.GetTimestamp();
            if(_benchmarkFrames>30){
                _frameTimes.Add(System.Diagnostics.Stopwatch.GetElapsedTime(_previousFrame,now).TotalMilliseconds);
                _cpuTotal+=System.Diagnostics.Stopwatch.GetElapsedTime(begin).TotalMilliseconds;
                _minShots=Math.Min(_minShots,Match.Battle.ActiveProjectiles);
                _maxShots=Math.Max(_maxShots,Match.Battle.ActiveProjectiles);
            }
            _previousFrame=now;
            if(_frameTimes.Count==300)
            {
                var sorted=_frameTimes.OrderBy(t=>t).ToArray();
                string result=System.Text.Json.JsonSerializer.Serialize(new{frames=300,averageFrameMs=_frameTimes.Average(),p95FrameMs=sorted[284],averageCpuMs=_cpuTotal/300,minShots=_minShots,maxShots=_maxShots,shots=Match.Battle.ActiveProjectiles,bigBalls=Match.Battle.ActiveBigBalls});
                using var file=Godot.FileAccess.Open("res://.godot/render-benchmark.json",Godot.FileAccess.ModeFlags.Write);file.StoreString(result);
                GD.Print("RENDER BENCHMARK "+result);GetTree().Quit();_benchmarkFrames=-1;
            }
        }
    }
    private void Refresh()
    {
        _board.Refresh();_battle.Refresh();
        if(Match.Phase==MatchPhase.Finished&&!_reported){_reported=true;_results.Present(Match);}
        if(!_clock.Visible)return;
        _stage.Text=Match.Phase==MatchPhase.Finished?"对局结束 · 按最终领土面积结算":Match.Phase==MatchPhase.Finale?$"终盘倒计时  {Match.FinaleRemaining:0.0} 秒":"自动观战 · 六类能力已启用";
        _clock.Text=$"{(int)Simulation.Time/60:00}:{(int)Simulation.Time%60:00}   /   SEED {Config.Seed}";
        _status.Text=$"弹珠 {Simulation.Balls.Count(b=>b.Phase!=BallPhase.Inactive)}   射弹 {Match.Battle.ActiveProjectiles}   大球 {Match.Battle.ActiveBigBalls}   兑换 {Simulation.AbilityHits}";
        string[] factions={"红","绿","蓝","黄"}; string[] abilities={"风暴","机枪","护盾","大球","狙击","霰弹"};
        var recent=Simulation.Events.TakeLast(3).Select(e=>$"{(int)e.Time/60:00}:{(int)e.Time%60:00}   {factions[e.Faction]} #{e.BallId+1:00}   {(e.IsDeath?"亡语·":"")}{abilities[(int)e.Ability]} +{Number(e.Value)}");
        _events.Text=string.Join("\n",recent.Concat(Match.Battle.Notices.TakeLast(2)));
    }
    public static string Number(double value) => value>=1e15?value.ToString("0.##E+0"):value>=1e12?(value/1e12).ToString("0.##")+"T":value>=1e9?(value/1e9).ToString("0.##")+"B":value>=1e6?(value/1e6).ToString("0.##")+"M":value>=1e3?(value/1e3).ToString("0.##")+"K":value.ToString("0");
}


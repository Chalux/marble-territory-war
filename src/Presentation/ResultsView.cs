using Godot;
using System;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace TerritoryWar;

public partial class ResultsView:Control
{
    public Action Restart;
    private MatchSimulation _match;
    private RichTextLabel _body;
    private Label _title,_message;
    public override void _Ready()
    {
        _body=GetNode<RichTextLabel>("Body");_title=GetNode<Label>("Title");_message=GetNode<Label>("Message");
        GetNode<Button>("Overview").Pressed+=()=>Render(0);
        GetNode<Button>("Firepower").Pressed+=()=>Render(1);
        GetNode<Button>("Metrics").Pressed+=()=>Render(2);
        GetNode<Button>("Close").Pressed+=()=>Hide();
        GetNode<Button>("Restart").Pressed+=()=>{Hide();Restart?.Invoke();};
        GetNode<Button>("Export").Pressed+=Export;
        Hide();
    }
    public void Present(MatchSimulation match)
    {
        _match=match;_message.Text="最终面积决定胜负；已淘汰阵营同样参与比较。";
        _title.Text=(match.Winners.Length>1?"并列获胜 · ":"胜利 · ")+string.Join(" / ",match.Winners.Select(BattleSimulation.FactionName));
        Render(0);Show();
    }
    private void Render(int tab)
    {
        if(_match==null)return;
        var battle=_match.Battle;var text=new StringBuilder();
        text.AppendLine($"[b]对局时长 {Time(battle.Time)}    随机种子 {battle.Config.Seed}    兑换 {_match.Pinball.AbilityHits} 次[/b]\n");
        for(int f=0;f<4;f++)
        {
            var s=battle.Factions[f];string color=Main.FactionColors[f].ToHtml(false);
            text.AppendLine($"[color=#{color}][b]{BattleSimulation.FactionName(f)}{(_match.Winners.Contains(f)?"  ★ 获胜":"")}[/b][/color]");
            if(tab==0)
            {
                text.AppendLine($"最终领土  {100.0*battle.Territory.Counts[f]/(TerritoryGrid.Width*TerritoryGrid.Height):0.00}%   ·   {battle.Territory.Counts[f]:N0} 格");
                text.AppendLine($"{(s.Alive?"据点存活":"淘汰于 "+Time(s.EliminatedAt))}   ·   复活 {s.Revivals} 次   ·   Lv.{s.Level}   ·   额外弹珠 {s.ExtraBalls}\n");
            }
            else if(tab==1)
            {
                string[] names={"风暴","机枪","护盾","大球","狙击","霰弹","亡语†","终盘风暴†"};
                text.AppendLine(string.Join("    ",Enumerable.Range(0,8).Select(i=>$"{names[i]} {Main.Number(battle.Invested[f,i])}")));
                text.AppendLine($"破盾伤害 {Main.Number(s.ShieldDamage)}   ·   涂色消耗 {Main.Number(s.PaintSpent)}   ·   承受伤害 {Main.Number(s.DamageTaken)}\n");
            }
            else
            {
                double totalArea=TerritoryGrid.Width*TerritoryGrid.Height*TerritoryGrid.CellArea;
                double average=s.SurvivalTime>0?s.AreaTime/s.SurvivalTime/totalArea:0;
                text.AppendLine($"存活时长 {Time(s.SurvivalTime)}     平均领土 {average:P1}     累计经验 {Main.Number(s.TotalExperience)}");
                text.AppendLine($"破盾输出 {Main.Number(s.ShieldDamage)}     承受伤害 {Main.Number(s.DamageTaken)}     额外弹珠 {s.ExtraBalls}\n");
            }
        }
        if(tab==1)text.AppendLine("[font_size=16]† 亡语是六类能力中的子集；终盘风暴由剩余机枪储量转换，不与原投入重复相加。[/font_size]");
        if(tab==2)text.AppendLine("[font_size=16]六项指标来自实际模拟统计。粒子仅为视觉，不统计虚构的粒子收益。[/font_size]");
        _body.Text=text.ToString();
    }
    public void SelectTab(int tab)=>Render(Math.Clamp(tab,0,2));
    private static string Time(double seconds)=>$"{(int)seconds/60:00}:{(int)seconds%60:00}";
    public static string BuildJson(MatchSimulation match)
    {
        var b=match.Battle;
        return JsonSerializer.Serialize(new {
            schema=2,seed=b.Config.Seed,seconds=b.Time,phase=match.Phase.ToString(),winners=match.Winners,
            parameters=typeof(SimulationConfig).GetProperties(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.DeclaredOnly).ToDictionary(p=>p.Name,p=>p.GetValue(b.Config)),
            factions=Enumerable.Range(0,4).Select(f=>new {
                name=BattleSimulation.FactionName(f),cells=b.Territory.Counts[f],alive=b.Factions[f].Alive,
                eliminatedAt=b.Factions[f].EliminatedAt,revivals=b.Factions[f].Revivals,
                level=b.Factions[f].Level,extraBalls=b.Factions[f].ExtraBalls,experience=b.Factions[f].TotalExperience,
                survival=b.Factions[f].SurvivalTime,areaTime=b.Factions[f].AreaTime,
                invested=Enumerable.Range(0,8).Select(i=>b.Invested[f,i]).ToArray(),
                damage=Enumerable.Range(0,8).Select(i=>b.Damage[f,i]).ToArray(),paintSpent=b.Factions[f].PaintSpent
            }).ToArray(),
            redemptions=match.RedemptionLog.Select(e=>new{e.Time,e.BallId,e.Faction,ability=e.Ability.ToString(),e.Value,e.IsDeath}).ToArray()
        },new JsonSerializerOptions{WriteIndented=true});
    }
    private void Export()
    {
        string directory=ProjectSettings.GlobalizePath("user://reports");
        var error=DirAccess.MakeDirRecursiveAbsolute(directory);
        if(error!=Error.Ok){_message.Text=$"无法创建报告目录：{error}";return;}
        string path=$"user://reports/match-{_match.Battle.Config.Seed}-{DateTime.Now:yyyyMMdd-HHmmss}.json";
        using var file=Godot.FileAccess.Open(path,Godot.FileAccess.ModeFlags.Write);
        if(file==null){_message.Text=$"导出失败：{Godot.FileAccess.GetOpenError()}";return;}
        file.StoreString(BuildJson(_match));_message.Text="已导出："+ProjectSettings.GlobalizePath(path);
    }
}

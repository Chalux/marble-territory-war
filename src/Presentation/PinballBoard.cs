using Godot;
using System;

namespace TerritoryWar;

// The board is authored in PinballBoard.tscn. This script binds state to existing nodes;
// Ball.tscn is instantiated for flying balls, and surplus conveyor cells are cloned from Cell0.
public partial class PinballBoard : Node2D
{
    [Export] public PackedScene BallScene { get; set; }
    public PinballSimulation Simulation;
    public bool ParticlesEnabled=true;
    private readonly BallView[] _views = new BallView[PinballSimulation.Capacity];
    private Node2D _balls, _barrel;
    private readonly Node2D[] _rims = new Node2D[6], _flashes = new Node2D[6];
    private readonly Label[] _values = new Label[6];
    private readonly double[] _displayedValues = new double[6];
    private readonly bool[] _displayedFlashes = new bool[6];
    private bool _slotsInitialized;
    private readonly Line2D[,] _baffles = new Line2D[6, 3];
    private readonly Line2D[] _arrows = new Line2D[16];
    private Node2D[] _cells, _contents;
    private Label[] _abilityNames, _abilityValues;
    private Node2D[,] _icons;
    private float _regionWidth;
    private const int AbilityIconCount = 6;
    private PinballSimulation _observedSimulation;
    private double _lastTime = -1;
    private static readonly Color Metal = new("a1a6aa"), Flash = new("ffc46b");
    private static readonly string[] AbilityNames = { "风暴", "机枪", "护盾", "大球", "狙击", "霰弹" };

    public override void _Ready()
    {
        _balls = GetNode<Node2D>("Balls");
        _barrel = GetNode<Node2D>("Launcher/Barrel");
        string[] layers = { "Shadow", "Body", "Edge" };
        for (int i = 0; i < 6; i++)
        {
            _rims[i] = GetNode<Node2D>($"Pockets/Pocket{i}/Rims");
            _flashes[i] = GetNode<Node2D>($"Pockets/Pocket{i}/Flash");
            _values[i] = GetNode<Label>($"Pockets/Pocket{i}/Value");
            for (int layer = 0; layer < 3; layer++)
                _baffles[i, layer] = GetNode<Line2D>($"Baffles/Baffle{i}/{layers[layer]}");
        }
        BuildConveyorCells();
        for (int i = 0; i < _arrows.Length; i++)
            _arrows[i] = GetNode<Line2D>($"LiftArrows/Arrow{i}");
    }

    // The belt can grow independently of the authored cells; extra cells are cloned from Cell0.
    private void BuildConveyorCells()
    {
        int beltCount = PinballSimulation.BeltAbilities.Length;
        _regionWidth = PinballSimulation.AbilityWidth / beltCount;
        int cellCount = beltCount + 1;
        var conveyor = GetNode<Control>("Conveyor");
        var template = GetNode<Node2D>("Conveyor/Cell0");
        _cells = new Node2D[cellCount];
        _contents = new Node2D[cellCount];
        _abilityNames = new Label[cellCount];
        _abilityValues = new Label[cellCount];
        _icons = new Node2D[cellCount, AbilityIconCount];
        for (int i = 0; i < cellCount; i++)
        {
            Node2D cell;
            if (i < 6) cell = GetNode<Node2D>($"Conveyor/Cell{i}");
            else
            {
                cell = (Node2D)template.Duplicate();
                cell.Name = $"Cell{i}";
                conveyor.AddChild(cell);
            }
            _cells[i] = cell;
            var content = cell.GetNode<Node2D>("Content");
            _contents[i] = content;
            _abilityNames[i] = content.GetNode<Label>("Name");
            _abilityValues[i] = content.GetNode<Label>("Value");
            for (int icon = 0; icon < 5; icon++) _icons[i, icon] = content.GetNode<Node2D>($"Icon{icon}");
            _icons[i, 5] = CreateShotgunIcon();
            content.AddChild(_icons[i, 5]);
            LayoutCell(i);
        }
    }

    private void LayoutCell(int i)
    {
        float left = _regionWidth * 0.36f, right = _regionWidth * 0.97f;
        var name = _abilityNames[i];
        var value = _abilityValues[i];
        name.OffsetLeft = left; name.OffsetRight = right;
        name.OffsetTop = 5; name.OffsetBottom = 29;
        value.OffsetLeft = left; value.OffsetRight = right;
        value.OffsetTop = 28; value.OffsetBottom = 54;
        name.AddThemeFontSizeOverride("font_size", Mathf.Clamp((int)(_regionWidth * 0.18f), 10, 16));
        value.AddThemeFontSizeOverride("font_size", Mathf.Clamp((int)(_regionWidth * 0.15f), 10, 16));
        float iconX = _regionWidth * 0.19f;
        float iconScale = Mathf.Clamp(_regionWidth / 124f * 0.98f, 0.55f, 0.9f);
        for (int icon = 0; icon < AbilityIconCount; icon++)
        {
            var node = _icons[i, icon];
            node.Position = new Vector2(iconX, 17);
            node.Scale = new Vector2(iconScale, iconScale);
            node.Visible = false;
        }
    }

    private static Node2D CreateShotgunIcon()
    {
        var icon = new Node2D { Name = "Icon5" };
        AddIconStroke(icon, new Vector2[] { new(-9, -3), new(0, -3) });
        AddIconStroke(icon, new Vector2[] { new(-9, 3), new(0, 3) });
        AddIconStroke(icon, new Vector2[] { new(3, -6), new(4.6f, -6) });
        AddIconStroke(icon, new Vector2[] { new(5, -1.5f), new(7, -1.5f) });
        AddIconStroke(icon, new Vector2[] { new(3, 3.5f), new(4.6f, 3.5f) });
        return icon;
    }

    private static void AddIconStroke(Node2D icon, Vector2[] points)
    {
        icon.AddChild(new Line2D { Width = 1.4f, DefaultColor = new Color("a0a5aa"), Antialiased = true, Points = points });
    }

    private void ReleaseBall(int index)
    {
        var view = _views[index];
        if (view == null) return;
        _balls.RemoveChild(view);
        view.QueueFree();
        _views[index] = null;
    }

    private void SyncBalls(bool observe)
    {
        if (_observedSimulation != Simulation || Simulation.Time < _lastTime)
        {
            for (int i = 0; i < _views.Length; i++) ReleaseBall(i);
            _observedSimulation = Simulation;
        }
        _lastTime = Simulation.Time;
        for (int i = 0; i < _views.Length; i++)
        {
            var state = Simulation.Balls[i];
            if (state.Phase != BallPhase.Flying)
            {
                ReleaseBall(i);
                continue;
            }
            if (_views[i] == null)
            {
                var view = BallScene.Instantiate<BallView>();
                view.Name = $"Ball{i:000}";
                _balls.AddChild(view);
                _views[i] = view;
                view.Observe(state, Simulation.Time);
            }
            else if (observe) _views[i].Observe(state, Simulation.Time);
            if (!observe) {_views[i].ParticlesEnabled=ParticlesEnabled;_views[i].ShowState(state);}
        }
    }

    public void ObserveStep()
    {
        if (Simulation != null) SyncBalls(true);
    }

    public void Refresh()
    {
        if (Simulation == null) return;
        SyncBalls(false);
        for (int i = 0; i < 6; i++)
        {
            bool flashing = Simulation.Time - Simulation.SlotFlash[i] < 0.45 && Simulation.SlotValues[i] > 0;
            Color color = flashing ? Flash : Metal;
            // Rims have baked metal colors; tint only their RGB ratio.
            _rims[i].Modulate = flashing ? new Color(Flash.R / Metal.R, Flash.G / Metal.G, Flash.B / Metal.B) : Colors.White;
            _flashes[i].Visible = flashing;
            if (!_slotsInitialized || _displayedValues[i] != Simulation.SlotValues[i])
            {
                _displayedValues[i] = Simulation.SlotValues[i];
                string value = Main.Number(_displayedValues[i]);
                _values[i].Text = value;
                var font = _values[i].GetThemeFont("font");
                _values[i].AddThemeFontSizeOverride("font_size", font.GetStringSize(value, HorizontalAlignment.Left, -1, 13).X > 40 ? 10 : 13);
            }
            if (!_slotsInitialized || _displayedFlashes[i] != flashing)
            {
                _displayedFlashes[i] = flashing;
                _values[i].AddThemeColorOverride("font_color", color);
            }
            var line = Simulation.BaffleLine(i);
            Vector2 dir = (line.B - line.A).Normalized();
            Vector2 down = new(-dir.Y, dir.X);
            if (down.Y < 0) down = -down;
            // The bright edge marks the collision surface; body and shadow hang below it.
            _baffles[i, 2].SetPointPosition(0, line.A);
            _baffles[i, 2].SetPointPosition(1, line.B);
            Vector2 bodyOffset = down * (_baffles[i, 1].Width * 0.5f);
            _baffles[i, 1].SetPointPosition(0, line.A + bodyOffset);
            _baffles[i, 1].SetPointPosition(1, line.B + bodyOffset);
            Vector2 shadowOffset = down * (_baffles[i, 0].Width * 0.5f);
            _baffles[i, 0].SetPointPosition(0, line.A + shadowOffset);
            _baffles[i, 0].SetPointPosition(1, line.B + shadowOffset);
            float rest = PinballGeometry.BaffleRestY(i), top = PinballGeometry.BaffleLiftY(i);
            float raise = Mathf.Clamp((rest - Simulation.BaffleY(i)) / Math.Max(0.001f, rest - top), 0, 1);
            _baffles[i, 2].DefaultColor = Metal.Lerp(Flash, raise * 0.55f);
        }
        _slotsInitialized = true;
        for (int i = 0; i < _arrows.Length; i++)
            _arrows[i].Position = new Vector2(304 + i * 23 % 72, 680 - Mathf.PosMod((float)Simulation.Time * 55 + i * 37, 154));
        _barrel.Rotation = -Simulation.LauncherAngle;

        float offset = Simulation.ConveyorOffset;
        int beltCount = PinballSimulation.BeltAbilities.Length;
        int first = -(int)MathF.Ceiling(offset / _regionWidth);
        for (int i = 0; i < _cells.Length; i++)
        {
            int cell = first + i, belt = ((cell % beltCount) + beltCount) % beltCount;
            Ability ability = PinballSimulation.BeltAbilities[belt];
            float x = cell * _regionWidth + offset;
            _cells[i].Position = new Vector2(x, 0);
            _contents[i].Visible = x + _regionWidth * 0.5f > 24 && x + _regionWidth * 0.5f < 596;
            _abilityNames[i].Text = AbilityNames[(int)ability];
            double total = 0;
            for (int faction = 0; faction < 4; faction++) total += Simulation.Redeemed[faction, (int)ability];
            _abilityValues[i].Text = Main.Number(total);
            for (int icon = 0; icon < AbilityIconCount; icon++) _icons[i, icon].Visible = icon == (int)ability;
        }
    }
}

using Godot;

namespace SimulacionTrafico.UI;

/// <summary>Lluvia en pantalla: un velo gris azulado sobre el mapa y gotas que caen. Solo es aspecto: no tapa la interfaz.</summary>
public partial class VistaLluvia : Control
{
    private const int Gotas = 220;
    private static readonly Color Velo = new(0.35f, 0.42f, 0.52f, 0.16f);
    private static readonly Color Gota = new(0.85f, 0.9f, 1f, 0.35f);
    private readonly Vector2[] _gotas = new Vector2[Gotas];
    private readonly float[] _velocidad = new float[Gotas];
    private readonly RandomNumberGenerator _rng = new() { Seed = 7 };

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        var tam = GetViewportRect().Size;
        for (int i = 0; i < Gotas; i++)
        {
            _gotas[i] = new Vector2(_rng.RandfRange(0f, tam.X), _rng.RandfRange(0f, tam.Y));
            _velocidad[i] = _rng.RandfRange(700f, 1100f);
        }
    }

    public override void _Process(double delta)
    {
        var tam = GetViewportRect().Size;
        for (int i = 0; i < Gotas; i++)
        {
            _gotas[i] += new Vector2(-0.18f, 1f) * _velocidad[i] * (float)delta;
            if (_gotas[i].Y > tam.Y) _gotas[i] = new Vector2(_rng.RandfRange(0f, tam.X + 100f), -20f);
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, GetViewportRect().Size), Velo);
        foreach (var g in _gotas) DrawLine(g, g + new Vector2(-2.5f, 14f), Gota, 1.2f);
    }
}

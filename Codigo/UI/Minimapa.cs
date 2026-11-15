using System.Linq;
using Godot;
using SimulacionTrafico.Camara;
using SimulacionTrafico.Mapa;

namespace SimulacionTrafico.UI;

/// <summary>
/// Minimapa en la esquina inferior derecha: calles simplificadas (se dibujan una sola vez)
/// y rectángulo de la zona visible (se redibuja cada fotograma). Clic o arrastre para mover la cámara.
/// </summary>
public partial class Minimapa : Control
{
    private const float Lado = 240f;
    private const float Margen = 12f;
    private const float CiudadGrande = 4000f; // a partir de este tamaño no se dibujan las calles locales

    private readonly Grafo _grafo;
    private readonly CamaraCiudad _camara;
    private readonly float _escala;
    private bool _arrastrando;

    public Minimapa(Grafo grafo, CamaraCiudad camara)
    {
        _grafo = grafo;
        _camara = camara;
        _escala = Lado / Mathf.Max(grafo.Limites.Size.X, grafo.Limites.Size.Y);
        var tam = grafo.Limites.Size * _escala;

        SetAnchorsPreset(LayoutPreset.BottomRight);
        OffsetLeft = -tam.X - Margen;
        OffsetTop = -tam.Y - Margen;
        OffsetRight = -Margen;
        OffsetBottom = -Margen;
        MouseFilter = MouseFilterEnum.Stop;
        ClipContents = true;

        AddChild(new CapaDibujo(DibujarFondo));
        AddChild(new CapaDibujo(DibujarVista) { Continuo = true });
    }

    private Vector2 AMini(Vector2 mundo) => (mundo - _grafo.Limites.Position) * _escala;

    private void DibujarFondo(CapaDibujo c)
    {
        c.DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.89f, 0.86f, 0.78f));

        foreach (var m in _grafo.Manzanas.Where(m => m.Parque))
            c.DrawRect(new Rect2(AMini(m.Rect.Position), m.Rect.Size * _escala), new Color(0.74f, 0.87f, 0.64f));

        bool grande = Mathf.Max(_grafo.Limites.Size.X, _grafo.Limites.Size.Y) > CiudadGrande;
        var colores = new[] { new Color(1f, 0.99f, 0.94f), new Color(1f, 0.96f, 0.62f), new Color(0.98f, 0.82f, 0.25f), new Color(0.96f, 0.68f, 0.20f) };
        foreach (var t in _grafo.Tramos.OrderBy(t => t.Categoria))
        {
            if (grande && t.Categoria < CategoriaVia.Arteria) continue;
            float ancho = grande ? (t.TotalCarriles >= 6 ? 2f : 1f)
                        : t.Categoria >= CategoriaVia.Arteria ? 3f : t.Categoria == CategoriaVia.Colectora ? 1.5f : 1f;
            c.DrawLine(AMini(t.A.Pos), AMini(t.B.Pos), colores[(int)t.Categoria], ancho);
        }
        c.DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.2f, 0.2f, 0.2f), false, 2f);
    }

    private void DibujarVista(CapaDibujo c)
    {
        var visible = GetViewportRect().Size / _camara.PixelesPorMetro;
        var rect = new Rect2(AMini(_camara.Position - visible / 2f), visible * _escala);
        c.DrawRect(rect, new Color(0.9f, 0.15f, 0.15f, 0.15f));
        c.DrawRect(rect, new Color(0.9f, 0.15f, 0.15f), false, 2f);
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            _arrastrando = mb.Pressed;
            if (mb.Pressed) Saltar(mb.Position);
        }
        else if (e is InputEventMouseMotion mm && _arrastrando)
        {
            Saltar(mm.Position);
        }
    }

    private void Saltar(Vector2 local) =>
        _camara.CentrarEn(_grafo.Limites.Position + local / _escala);
}

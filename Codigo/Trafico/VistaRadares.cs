using Godot;
using SimulacionTrafico.Camara;
using SimulacionTrafico.Mapa;
using SimulacionTrafico.Nucleo;

namespace SimulacionTrafico.Trafico;

/// <summary>
/// Dibuja los radares: una señal (Assets/iconos/radar.png) a cada lado de la calle, a mitad de su
/// longitud, que es donde mide. Se ve a cualquier zoom: de cerca mide unos 4 m; de lejos, unos 22 píxeles.
/// </summary>
public partial class VistaRadares : Node2D
{
    private readonly Grafo _grafo;
    private readonly CamaraCiudad _camara;
    private float _zoomDibujado = -1f;

    public VistaRadares(Grafo grafo, CamaraCiudad camara)
    {
        _grafo = grafo;
        _camara = camara;
        ZIndex = 9;
    }

    public override void _Process(double delta)
    {
        if (!Mathf.IsEqualApprox(_camara.PixelesPorMetro, _zoomDibujado)) QueueRedraw();
    }

    private Texture2D? _icono;

    public override void _Draw()
    {
        if (_icono == null)
            _icono = ImageTexture.CreateFromImage(Nucleo.Rutas.Imagen("res://Assets/iconos/radar.png"));
        float zoom = Mathf.Max(_camara.PixelesPorMetro, 1e-4f);
        _zoomDibujado = zoom;
        float tam = Mathf.Max(4f, 22f / zoom);   // de cerca, una señal de 4 m; de lejos, unos 22 píxeles
        foreach (var r in _grafo.Radares)
        {
            var t = r.Tramo;
            float s = r.Fraccion * t.Eje.Longitud;
            var p = t.Eje.Posicion(s);
            var d = t.Eje.Direccion(s);
            var lado = new Vector2(-d.Y, d.X);
            // De lejos, una sola señal sobre la calle; de cerca, una a cada lado.
            if (tam > t.AnchoCalzada * 1.5f)
            {
                DrawTextureRect(_icono, new Rect2(p - Vector2.One * tam / 2f, Vector2.One * tam), false);
                continue;
            }
            foreach (float signo in new[] { 1f, -1f })
            {
                var c = p + lado * signo * (t.AnchoCalzada / 2f + Escala.AnchoAcera / 2f + tam * 0.55f);
                DrawTextureRect(_icono, new Rect2(c - Vector2.One * tam / 2f, Vector2.One * tam), false);
            }
        }
    }
}

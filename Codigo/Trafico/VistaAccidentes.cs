using Godot;
using SimulacionTrafico.Camara;
using SimulacionTrafico.Vehiculos;

namespace SimulacionTrafico.Trafico;

/// <summary>
/// Marca los accidentes en curso con un círculo rojo semitransparente, visible a cualquier zoom: su tamaño nunca baja de unos
/// píxeles en pantalla (igual que los coches, que de lejos se dibujan como cuadrados) y en cerca cubre a los coches implicados.
/// </summary>
public partial class VistaAccidentes : Node2D
{
    private static readonly Color Relleno = new(1f, 0.1f, 0.1f, 0.35f);
    private static readonly Color Borde = new(0.85f, 0f, 0f, 0.9f);
    private static readonly Color Corte = new(1f, 0.1f, 0.1f, 0.30f);

    private readonly Simulacion _sim;
    private readonly CamaraCiudad _camara;

    public VistaAccidentes(Simulacion sim, CamaraCiudad camara)
    {
        _sim = sim;
        _camara = camara;
        ZIndex = 11;   // por encima de los coches
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        float zoom = Mathf.Max(_camara.PixelesPorMetro, 1e-4f);
        float radio = Mathf.Max(8f, 14f / zoom);   // 8 m de cerca, 14 píxeles de lejos
        foreach (var a in _sim.AccidentesActivos)
        {
            foreach (var l in a.Carriles) DrawPolyline(l.Eje.Puntos, Corte, Nucleo.Escala.AnchoCarril * 0.92f);   // carril cortado
            DrawCircle(a.Pos, radio, Relleno);
            DrawArc(a.Pos, radio, 0f, Mathf.Tau, 40, Borde, 2f / zoom);
        }
    }
}

using Godot;
using SimulacionTrafico.Nucleo;

namespace SimulacionTrafico.Mapa;

/// <summary>
/// Carril con un único sentido de circulación: es por donde van los coches.
/// El eje está en el sentido de la marcha y empieza/termina fuera de los cruces.
/// </summary>
public sealed class Carril : ViaCirculacion
{
    public int Id { get; }
    public Tramo Tramo { get; }
    public bool Ida { get; }

    /// <summary>0 = el más cercano a la línea central; crece hacia el borde de la calle.</summary>
    public int Indice { get; }
    public Nodo Origen { get; }
    public Nodo Destino { get; }

    /// <summary>Carril contiguo hacia la izquierda / derecha en el mismo sentido (null si no existe).</summary>
    public Carril? Izquierdo { get; set; }
    public Carril? Derecho { get; set; }

    public override float LimiteMs => Tramo.LimiteKmh * Escala.KmhAMs;

    /// <summary>Hasta cuándo está cortado por un accidente (tiempo de simulación). Nadie entra en un carril cortado ni lo elige al cambiar de carril.</summary>
    public double CortadoHasta { get; set; } = double.NegativeInfinity;

    public bool EstaCortado(double tiempo) => tiempo < CortadoHasta;

    /// <summary>
    /// Por dónde llega al cruce de su destino, según la dirección del final del carril: 0 hacia el este, 1 hacia el oeste, 2 hacia el sur,
    /// 3 hacia el norte. La consultan los semáforos en cada paso; el eje no cambia, así que se calcula una vez.
    /// </summary>
    public int Aproximacion { get; }

    public Carril(int id, Tramo tramo, bool ida, int indice, Nodo origen, Nodo destino, Trayectoria eje)
        : base(eje)
    {
        Id = id;
        Tramo = tramo;
        Ida = ida;
        Indice = indice;
        Origen = origen;
        Destino = destino;
        var d = eje.Direccion(eje.Longitud);
        Aproximacion = Mathf.Abs(d.X) >= Mathf.Abs(d.Y) ? (d.X > 0f ? 0 : 1) : (d.Y > 0f ? 2 : 3);
    }
}

using System.Collections.Generic;
using SimulacionTrafico.Vehiculos;

namespace SimulacionTrafico.Mapa;

/// <summary>
/// Cualquier recorrido por el que circulan coches: un carril o una conexión dentro de un cruce.
/// Guarda los coches que hay encima, ordenados de atrás (posición menor) a delante.
/// </summary>
public abstract class ViaCirculacion
{
    public Trayectoria Eje { get; }
    public float Longitud => Eje.Longitud;

    /// <summary>Límite de velocidad de la vía, en m/s.</summary>
    public abstract float LimiteMs { get; }

    /// <summary>Radio de curvatura más cerrado de la vía, en metros (infinito si es recta).</summary>
    public virtual float RadioMinimo => Eje.RadioMinimo;

    /// <summary>Coches sobre la vía, por posición creciente (el último es el que va más adelantado).</summary>
    public List<Coche> Coches { get; } = new();

    protected ViaCirculacion(Trayectoria eje) => Eje = eje;
}

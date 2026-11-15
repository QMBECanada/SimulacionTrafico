using System;
using SimulacionTrafico.Vehiculos;

namespace SimulacionTrafico.Mapa;

/// <summary>
/// Radar de una calle: controla todos los sentidos y todos los carriles de la calle donde está, a mitad de su longitud.
/// Cada coche que pasa queda registrado con su velocidad.
/// </summary>
public sealed class Radar
{
    public int Id { get; }
    public string Nombre { get; }
    public Tramo Tramo { get; }

    /// <summary>Posición a lo largo de la calle, de 0 (principio) a 1 (final).</summary>
    public float Fraccion { get; } = 0.5f;

    /// <summary>Se llama cuando pasa un coche (coche, instante de la simulación).</summary>
    public Action<Radar, Coche, double>? AlPasar { get; set; }

    public Radar(int id, Tramo tramo)
    {
        Id = id;
        Nombre = $"RD-{id + 1}";
        Tramo = tramo;
    }

    public void Pasa(Coche coche, double tiempo) => AlPasar?.Invoke(this, coche, tiempo);
}

using System.Collections.Generic;
using Godot;
using SimulacionTrafico.Vehiculos;

namespace SimulacionTrafico.Trafico;

/// <summary>
/// Un accidente: dos coches (o más, si otro choca después contra ellos) que se han golpeado. Se quedan inmóviles
/// el tiempo que marca la configuración (<c>DuracionAccidenteS</c>) y luego reanudan la marcha. Los coches parados estorban al tráfico como cualquier otro obstáculo.
/// </summary>
public sealed class Accidente
{
    /// <summary>Segundos tras reanudar durante los que los coches implicados no vuelven a contar como accidente (aún pueden estar tocándose).</summary>
    public const float InmunidadS = 20f;

    public int Id { get; }
    public double Inicio { get; }
    public double Fin { get; set; }
    public Vector2 Pos { get; }
    public string Lugar { get; }
    public List<Coche> Coches { get; } = new();

    /// <summary>Carriles cortados mientras dura el accidente: nadie entra en ellos.</summary>
    public List<Mapa.Carril> Carriles { get; } = new();

    public Accidente(int id, double inicio, Vector2 pos, string lugar, float duracionS)
    {
        Id = id;
        Inicio = inicio;
        Fin = inicio + duracionS;
        Pos = pos;
        Lugar = lugar;
    }
}

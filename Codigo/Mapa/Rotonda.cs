using System.Collections.Generic;
using System.Linq;
using Godot;

namespace SimulacionTrafico.Mapa;

/// <summary>
/// Rotonda: un anillo de un solo sentido (antihorario) alrededor de una isla central, con una entrada y salida por cada
/// calle que llega. Los coches del anillo tienen prioridad; los que entran ceden el paso y no entran si el anillo ya está
/// muy ocupado (como en la realidad: así el anillo no se llena y se bloquea). El anillo son tramos y nodos normales del grafo.
/// </summary>
public sealed record Rotonda(string Nombre, Vector2 Centro, float RadioIsla)
{
    /// <summary>Fracción del anillo (por longitud de coches) a partir de la cual nadie más entra.</summary>
    public const float OcupacionMaxima = 0.5f;

    /// <summary>Tramos del anillo.</summary>
    public List<Tramo> Tramos { get; } = new();

    /// <summary>Qué parte del anillo ocupan ahora los coches, de 0 (vacío) a 1 (lleno).</summary>
    public float Ocupacion()
    {
        float coches = 0f, largo = 0f;
        foreach (var t in Tramos)
            foreach (var carril in t.Carriles)
            {
                largo += carril.Longitud;
                foreach (var c in carril.Coches) coches += c.Longitud + 1f;
            }
        return largo > 0f ? coches / largo : 0f;
    }
}

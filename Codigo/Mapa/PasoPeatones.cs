using System.Collections.Generic;
using Godot;

namespace SimulacionTrafico.Mapa;

/// <summary>Una persona cruzando (o esperando para cruzar) un paso de peatones.</summary>
public sealed class Peaton
{
    public int Id { get; init; }
    public double Llegada { get; init; }          // cuándo llega a la acera
    public double? Inicio { get; set; }           // cuándo empieza a cruzar
    public float Velocidad { get; init; }         // m/s
    public bool DesdeA { get; init; }             // cruza de A a B (o de B a A)
    public float Recorrido { get; set; }          // metros cruzados
}

/// <summary>
/// Paso de peatones sobre una calle, junto a un cruce: una franja que atraviesa la calzada de lado a lado. Los peatones llegan a
/// una de las dos aceras, esperan y cruzan. Con semáforo cruzan con el verde de las calles paralelas a ellos (el grupo
/// <see cref="GrupoPeatones"/>); sin semáforo tienen preferencia, pero no se lanzan delante de un coche que ya no puede frenar.
/// Mientras haya alguien cruzando, los coches no pasan por él.
/// </summary>
public sealed class PasoPeatones
{
    public Tramo Tramo { get; }
    public Nodo Nodo { get; }
    /// <summary>Extremos de la franja (de acera a acera) y su centro.</summary>
    public Vector2 A { get; }
    public Vector2 B { get; }
    public Vector2 Centro => (A + B) / 2f;
    /// <summary>Distancia desde el centro del cruce hasta el centro de la franja, a lo largo de la calle.</summary>
    public float Distancia { get; }
    public float Largo => A.DistanceTo(B);
    public const float AnchoFranja = 3f;

    /// <summary>Grupo de semáforo con el que cruzan (el de las calles paralelas a la franja), si el cruce tiene semáforo.</summary>
    public int GrupoPeatones { get; }

    /// <summary>Peatones por segundo que llegan en hora punta (según la zona de la ciudad).</summary>
    public float TasaPunta { get; set; }

    public string Nombre { get; }

    public List<Peaton> Esperando { get; } = new();
    public List<Peaton> Cruzando { get; } = new();

    /// <summary>Hay alguien en la calzada: los coches no pueden pasar.</summary>
    public bool Ocupado => Cruzando.Count > 0;

    public PasoPeatones(Tramo tramo, Nodo nodo)
    {
        Tramo = tramo;
        Nodo = nodo;
        bool desdeA = tramo.A == nodo;
        // En la calle, pegada a la caja del cruce: la línea de detención queda un metro antes de la franja (ver Grafo.RetranqueoPaso).
        Distancia = nodo.Radio + AnchoFranja / 2f;
        float s = desdeA ? Distancia : tramo.Eje.Longitud - Distancia;
        var p = tramo.Eje.Posicion(s);
        var d = tramo.Eje.Direccion(s);
        var lado = new Vector2(-d.Y, d.X) * (tramo.AnchoCalzada / 2f + 1f);
        A = p - lado;
        B = p + lado;
        GrupoPeatones = Mathf.Abs(d.X) >= Mathf.Abs(d.Y) ? 1 : 0;   // franja sobre una calle horizontal: se cruza en vertical
        Nombre = $"P-{(nodo.Nombre != "" ? nodo.Nombre : "N" + nodo.Id)}-{(tramo.Nombre != "" ? tramo.Nombre : "T" + tramo.Id)}";
    }

    /// <summary>
    /// Un coche que cruza la franja por el punto <paramref name="punto"/> (donde la atraviesa su carril) debe esperar: hay alguien en su
    /// carril o a punto de llegar a él (en unos 2,5 s), o, si <paramref name="cederAlQueEspera"/>, alguien esperando en la acera de su lado
    /// de la calzada. Al que ya ha pasado su carril no lo espera.
    /// </summary>
    public bool Bloquea(Vector2 punto, bool cederAlQueEspera)
    {
        if (Cruzando.Count == 0 && (!cederAlQueEspera || Esperando.Count == 0)) return false;
        var dir = (B - A) / Mathf.Max(Largo, 0.01f);
        float x = (punto - A).Dot(dir);
        foreach (var p in Cruzando)
        {
            float r = p.DesdeA ? p.Recorrido : Largo - p.Recorrido;
            float porDelante = p.DesdeA ? x - r : r - x;   // lo que le falta para llegar al carril (negativo: ya lo pasó)
            if (porDelante > -1.8f && porDelante < 1.8f + p.Velocidad * 2.5f) return true;
        }
        if (cederAlQueEspera)
            foreach (var p in Esperando)
                if (p.DesdeA ? x < 0.6f * Largo : x > 0.4f * Largo) return true;
        return false;
    }

    /// <summary>Posición en el plano de un peatón (sobre la franja, o en la acera si espera).</summary>
    public Vector2 Posicion(Peaton p)
    {
        float f = Largo <= 0f ? 0f : Mathf.Clamp(p.Recorrido / Largo, 0f, 1f);
        return p.DesdeA ? A.Lerp(B, f) : B.Lerp(A, f);
    }
}

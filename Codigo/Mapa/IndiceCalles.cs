using System.Collections.Generic;
using Godot;

namespace SimulacionTrafico.Mapa;

/// <summary>Cuadrícula para encontrar rápido la calle o el cruce que hay bajo un punto del mapa (para saber qué señala el ratón).</summary>
public sealed class IndiceCalles
{
    private const float Celda = 100f;

    private readonly Dictionary<(int, int), List<Tramo>> _tramos = new();
    private readonly Dictionary<(int, int), List<Nodo>> _nodos = new();

    public IndiceCalles(Grafo grafo)
    {
        foreach (var t in grafo.Tramos)
        {
            // Se anota la calle en cada celda por la que pasa (puntos cada 40 m como mucho).
            float largo = t.Eje.Longitud;
            int pasos = Mathf.Max(1, (int)(largo / 40f));
            for (int i = 0; i <= pasos; i++)
            {
                var lista = Lista(_tramos, t.Eje.Posicion(largo * i / pasos));
                if (!lista.Contains(t)) lista.Add(t);
            }
        }
        foreach (var n in grafo.Nodos) Lista(_nodos, n.Pos).Add(n);
    }

    private static (int, int) Clave(Vector2 p) => ((int)Mathf.Floor(p.X / Celda), (int)Mathf.Floor(p.Y / Celda));

    private static List<T> Lista<T>(Dictionary<(int, int), List<T>> d, Vector2 p)
    {
        var k = Clave(p);
        if (!d.TryGetValue(k, out var l)) d[k] = l = new List<T>();
        return l;
    }

    /// <summary>
    /// El cruce (si el punto está dentro de su zona) o, si no, la calle más cercana a menos de <paramref name="tolerancia"/> metros
    /// del borde de su calzada.
    /// </summary>
    public (Nodo? nodo, Tramo? tramo) Buscar(Vector2 p, float tolerancia)
    {
        tolerancia = Mathf.Min(tolerancia, 40f);
        var (cx, cy) = Clave(p);

        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
                if (_nodos.TryGetValue((cx + dx, cy + dy), out var ns))
                    foreach (var n in ns)
                        if (n.Tramos.Count > 2 && p.DistanceTo(n.Pos) <= n.Radio + tolerancia * 0.5f) return (n, null);

        Tramo? mejor = null;
        float mejorD = float.PositiveInfinity;
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
                if (_tramos.TryGetValue((cx + dx, cy + dy), out var ts))
                    foreach (var t in ts)
                    {
                        float d = t.Eje.Posicion(t.Eje.ProyectarS(p)).DistanceTo(p) - t.AnchoCalzada / 2f;
                        if (d < mejorD)
                        {
                            mejorD = d;
                            mejor = t;
                        }
                    }
        return (null, mejorD <= tolerancia ? mejor : null);
    }
}

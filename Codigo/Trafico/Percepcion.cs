using System.Collections.Generic;
using Godot;
using SimulacionTrafico.Vehiculos;

namespace SimulacionTrafico.Trafico;

/// <summary>
/// Lo que «ven» los conductores: en cada paso se anota dónde está cada coche que circula y dónde estará en los próximos dos segundos
/// si sigue como va. Cada coche consulta a sus vecinos para no chocar (ver <see cref="Coche"/>). Es un índice espacial (celdas de 20 m)
/// para no comparar cada coche con todos.
/// </summary>
public sealed class Percepcion
{
    private const float Celda = 20f;
    private readonly Dictionary<long, List<Coche>> _celdas = new();
    private readonly Stack<List<Coche>> _libres = new();

    private static long Clave(int x, int y) => ((long)x << 32) ^ (uint)y;

    /// <summary>Se llama al principio de cada paso, antes de que se muevan los coches, con su futuro ya calculado (<see cref="Coche.CalcularFuturo"/>).</summary>
    public void Reconstruir(IReadOnlyList<Coche> coches)
    {
        foreach (var l in _celdas.Values)
        {
            l.Clear();
            _libres.Push(l);
        }
        _celdas.Clear();
        foreach (var c in coches)
        {
            long k = Clave((int)Mathf.Floor(c.Pos.X / Celda), (int)Mathf.Floor(c.Pos.Y / Celda));
            if (!_celdas.TryGetValue(k, out var lista)) _celdas[k] = lista = _libres.Count > 0 ? _libres.Pop() : new List<Coche>();
            lista.Add(c);
        }
    }

    /// <summary>Rellena <paramref name="salida"/> con los coches a menos de <paramref name="radio"/> metros (aproximado por celdas) de un punto.</summary>
    public void Cerca(Vector2 p, float radio, List<Coche> salida)
    {
        salida.Clear();
        int x0 = (int)Mathf.Floor((p.X - radio) / Celda), x1 = (int)Mathf.Floor((p.X + radio) / Celda);
        int y0 = (int)Mathf.Floor((p.Y - radio) / Celda), y1 = (int)Mathf.Floor((p.Y + radio) / Celda);
        float r2 = radio * radio;
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
                if (_celdas.TryGetValue(Clave(x, y), out var lista))
                    foreach (var c in lista)
                        if (c.Pos.DistanceSquaredTo(p) <= r2) salida.Add(c);
    }
}

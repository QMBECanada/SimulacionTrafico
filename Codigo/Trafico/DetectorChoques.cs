using System.Collections.Generic;
using Godot;
using SimulacionTrafico.Vehiculos;

namespace SimulacionTrafico.Trafico;

/// <summary>
/// Busca coches cuyos cuerpos se solapan. Cada coche es un rectángulo orientado (largo y ancho de su modelo, algo reducidos
/// para no dar por choque un roce); se agrupan en una cuadrícula para no comparar todos con todos.
/// </summary>
public sealed class DetectorChoques
{
    private const float Celda = 10f;
    /// <summary>Fracción del cuerpo que cuenta para el choque (los coches no son rectángulos perfectos).</summary>
    public const float ReduccionCuerpo = 0.85f;

    private readonly Dictionary<long, List<Coche>> _celdas = new();
    private readonly Stack<List<Coche>> _libres = new();
    private readonly List<(Coche a, Coche b, float profundidad)> _resultado = new();

    private static long Clave(int x, int y) => ((long)x << 32) ^ (uint)y;

    // Cada celda se compara con ella misma y con estas cuatro vecinas, para ver cada pareja una sola vez.
    private static readonly (int dx, int dy)[] Vecinas = { (1, 0), (-1, 1), (0, 1), (1, 1) };

    /// <summary>Parejas de coches que se tocan ahora. Los coches que no circulan o ya están en un accidente (si <paramref name="omitir"/> lo dice) no cuentan.</summary>
    public IReadOnlyList<(Coche a, Coche b, float profundidad)> Buscar(IReadOnlyList<Coche> coches, System.Func<Coche, bool>? omitir = null)
    {
        foreach (var l in _celdas.Values)
        {
            l.Clear();
            _libres.Push(l);
        }
        _celdas.Clear();
        _resultado.Clear();

        foreach (var c in coches)
        {
            if (omitir != null && omitir(c)) continue;
            long k = Clave((int)Mathf.Floor(c.Pos.X / Celda), (int)Mathf.Floor(c.Pos.Y / Celda));
            if (!_celdas.TryGetValue(k, out var lista)) _celdas[k] = lista = _libres.Count > 0 ? _libres.Pop() : new List<Coche>();
            lista.Add(c);
        }

        foreach (var (k, lista) in _celdas)
        {
            int cx = (int)(k >> 32), cy = (int)(uint)k;
            for (int i = 0; i < lista.Count; i++)
            {
                for (int j = i + 1; j < lista.Count; j++) Comprobar(lista[i], lista[j]);
                foreach (var (dx, dy) in Vecinas)
                    if (_celdas.TryGetValue(Clave(cx + dx, cy + dy), out var vecina))
                        foreach (var otro in vecina) Comprobar(lista[i], otro);
            }
        }
        return _resultado;
    }

    private void Comprobar(Coche a, Coche b)
    {
        var d = b.Pos - a.Pos;
        float alcance = (a.Longitud + b.Longitud) * 0.5f + 0.5f;
        if (d.LengthSquared() > alcance * alcance) return;
        // A distinto nivel (uno en un túnel o su boca, otro por encima): en el plano se cruzan, pero no se pueden chocar.
        if (a.Z != b.Z) return;
        float p = Penetracion(a, b);
        if (p > 0f) _resultado.Add((a, b, p));
    }

    /// <summary>Test de ejes separadores entre dos rectángulos orientados: cuánto se solapan, en metros (0 si no se tocan).</summary>
    private static float Penetracion(Coche a, Coche b)
    {
        var ua = Vector2.FromAngle(a.Rumbo);
        var va = new Vector2(-ua.Y, ua.X);
        var ub = Vector2.FromAngle(b.Rumbo);
        var vb = new Vector2(-ub.Y, ub.X);
        float hla = a.Longitud * 0.5f * ReduccionCuerpo, hwa = a.Modelo.AnchoM * 0.5f * ReduccionCuerpo;
        float hlb = b.Longitud * 0.5f * ReduccionCuerpo, hwb = b.Modelo.AnchoM * 0.5f * ReduccionCuerpo;
        var d = b.Pos - a.Pos;
        float minima = float.PositiveInfinity;

        for (int k = 0; k < 4; k++)
        {
            var eje = k switch { 0 => ua, 1 => va, 2 => ub, _ => vb };
            float ra = hla * Mathf.Abs(ua.Dot(eje)) + hwa * Mathf.Abs(va.Dot(eje));
            float rb = hlb * Mathf.Abs(ub.Dot(eje)) + hwb * Mathf.Abs(vb.Dot(eje));
            float p = ra + rb - Mathf.Abs(d.Dot(eje));
            if (p <= 0f) return 0f;
            if (p < minima) minima = p;
        }
        return minima;
    }
}

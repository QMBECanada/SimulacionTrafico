using System.Collections.Generic;
using Godot;

namespace SimulacionTrafico.Nucleo;

/// <summary>
/// Índice espacial de elementos fijos (cruces, pasos de peatones, paradas...): los reparte en celdas cuadradas para encontrar los que
/// caen en un rectángulo (la parte visible del mapa) sin recorrerlos todos.
/// </summary>
public sealed class RejillaEspacial<T>
{
    private readonly float _tamCelda;
    private readonly List<T> _elementos = new();
    private readonly List<Vector2> _posiciones = new();
    private readonly Dictionary<(int X, int Y), List<int>> _celdas = new();
    private readonly List<int> _encontrados = new();
    // Celdas ocupadas extremas: una búsqueda con un rectángulo enorme no recorre celdas vacías fuera de ellas.
    private int _minX = int.MaxValue, _minY = int.MaxValue, _maxX = int.MinValue, _maxY = int.MinValue;

    public RejillaEspacial(float tamCelda)
    {
        if (!(tamCelda > 0f)) throw new System.ArgumentOutOfRangeException(nameof(tamCelda));
        _tamCelda = tamCelda;
    }

    public int Count => _elementos.Count;

    private int Celda(float coordenada) => Mathf.FloorToInt(coordenada / _tamCelda);

    public void Agregar(Vector2 pos, T elemento)
    {
        int x = Celda(pos.X), y = Celda(pos.Y);
        if (!_celdas.TryGetValue((x, y), out var celda)) _celdas[(x, y)] = celda = new List<int>();
        celda.Add(_elementos.Count);
        _elementos.Add(elemento);
        _posiciones.Add(pos);
        if (x < _minX) _minX = x;
        if (x > _maxX) _maxX = x;
        if (y < _minY) _minY = y;
        if (y > _maxY) _maxY = y;
    }

    /// <summary>
    /// Deja en <paramref name="salida"/> (que vacía antes) los elementos cuya posición está dentro de <paramref name="rect"/>, con el
    /// mismo criterio que <see cref="Rect2.HasPoint"/>, en el orden en que se añadieron. Con coordenadas no finitas (NaN) no se
    /// garantiza esa equivalencia; la cámara y el mapa no las producen.
    /// </summary>
    public void Buscar(Rect2 rect, List<T> salida)
    {
        salida.Clear();
        if (_elementos.Count == 0) return;
        int x0 = System.Math.Max(Celda(rect.Position.X), _minX), x1 = System.Math.Min(Celda(rect.End.X), _maxX);
        int y0 = System.Math.Max(Celda(rect.Position.Y), _minY), y1 = System.Math.Min(Celda(rect.End.Y), _maxY);

        _encontrados.Clear();
        // Contadores long: con una celda en int.MaxValue un int desbordaría y el bucle no acabaría.
        for (long x = x0; x <= x1; x++)
            for (long y = y0; y <= y1; y++)
            {
                if (!_celdas.TryGetValue(((int)x, (int)y), out var celda)) continue;
                foreach (int i in celda)
                    if (rect.HasPoint(_posiciones[i])) _encontrados.Add(i);
            }
        _encontrados.Sort();   // cada elemento está en una sola celda: sin repetidos
        foreach (int i in _encontrados) salida.Add(_elementos[i]);
    }
}

using System.Collections.Generic;
using Godot;

namespace SimulacionTrafico.Mapa;

/// <summary>
/// Río: una línea con meandros que cruza la ciudad de un borde a otro, con un ancho constante. Se guarda como una
/// polilínea de puntos muy próximos y se consulta la distancia a ella con una cuadrícula espacial.
/// </summary>
public sealed class Rio
{
    private const float Celda = 100f;

    private readonly Dictionary<(int, int), List<int>> _celdas = new();

    public Vector2[] Puntos { get; }
    public float Ancho { get; }

    /// <summary>True si el río corre de oeste a este (de un borde lateral al otro); false si corre de norte a sur.</summary>
    public bool Horizontal => Mathf.Abs(Puntos[^1].X - Puntos[0].X) >= Mathf.Abs(Puntos[^1].Y - Puntos[0].Y);

    public Rio(Vector2[] puntos, float ancho)
    {
        Puntos = puntos;
        Ancho = ancho;
        for (int i = 0; i < puntos.Length - 1; i++)
            foreach (var p in new[] { puntos[i], puntos[i + 1] })
            {
                var clave = ((int)Mathf.Floor(p.X / Celda), (int)Mathf.Floor(p.Y / Celda));
                if (!_celdas.TryGetValue(clave, out var lista)) _celdas[clave] = lista = new List<int>();
                if (lista.Count == 0 || lista[^1] != i) lista.Add(i);
            }
    }

    /// <summary>Distancia del punto al eje del río, o infinito si está a más de <paramref name="maxima"/>.</summary>
    public float Distancia(Vector2 p, float maxima = 200f)
    {
        int cx = (int)Mathf.Floor(p.X / Celda), cy = (int)Mathf.Floor(p.Y / Celda);
        int radio = (int)Mathf.Ceil(maxima / Celda) + 1;
        float mejor = float.PositiveInfinity;
        for (int dx = -radio; dx <= radio; dx++)
            for (int dy = -radio; dy <= radio; dy++)
            {
                if (!_celdas.TryGetValue((cx + dx, cy + dy), out var segmentos)) continue;
                foreach (int i in segmentos)
                {
                    var a = Puntos[i];
                    var ab = Puntos[i + 1] - a;
                    float largo2 = ab.LengthSquared();
                    float t = largo2 > 1e-9f ? Mathf.Clamp((p - a).Dot(ab) / largo2, 0f, 1f) : 0f;
                    float d = (a + ab * t).DistanceTo(p);
                    if (d < mejor) mejor = d;
                }
            }
        return mejor <= maxima ? mejor : float.PositiveInfinity;
    }

    /// <summary>True si el punto está dentro del agua (con un margen opcional de orilla).</summary>
    public bool Dentro(Vector2 p, float margen = 0f) => Distancia(p, Ancho / 2f + margen + Celda) < Ancho / 2f + margen;

    /// <summary>
    /// Crea un río que cruza la ciudad de lado a lado, con meandros de dos escalas. Se mantiene en la zona central para no
    /// llegar a las esquinas de la circunvalación.
    /// </summary>
    public static Rio Crear(Rect2 limites, RandomNumberGenerator rng)
    {
        bool horizontal = rng.Randf() < 0.5f;               // corre de oeste a este (o de norte a sur)
        float largo = horizontal ? limites.Size.X : limites.Size.Y;
        float ancho = horizontal ? limites.Size.Y : limites.Size.X;

        float desplazamiento = rng.RandfRange(-0.15f, 0.15f) * ancho;
        float amplitud = Mathf.Min(0.08f * ancho, 220f);
        float onda1 = rng.RandfRange(900f, 1500f), onda2 = rng.RandfRange(350f, 520f);
        float fase1 = rng.RandfRange(0f, Mathf.Tau), fase2 = rng.RandfRange(0f, Mathf.Tau);
        float anchoRio = Mathf.Clamp(0.012f * Mathf.Max(largo, ancho) + 30f, 36f, 64f);

        var puntos = new List<Vector2>();
        float centro = horizontal ? limites.GetCenter().Y : limites.GetCenter().X;
        float inicio = (horizontal ? limites.Position.X : limites.Position.Y);
        float fin = (horizontal ? limites.End.X : limites.End.Y);
        for (float t = inicio; t <= fin; t += 6f)
        {
            float d = centro + desplazamiento
                      + amplitud * Mathf.Sin(Mathf.Tau * t / onda1 + fase1)
                      + 0.35f * amplitud * Mathf.Sin(Mathf.Tau * t / onda2 + fase2);
            puntos.Add(horizontal ? new Vector2(t, d) : new Vector2(d, t));
        }
        return new Rio(puntos.ToArray(), anchoRio);
    }
}

using System.Collections.Generic;
using Godot;

namespace SimulacionTrafico.Mapa;

public enum TipoGiro { Recto, Derecha, Izquierda }

/// <summary>Punto donde dos conexiones del mismo cruce se cruzan o confluyen.</summary>
/// <param name="Otra">La otra conexión.</param>
/// <param name="SPropio">Distancia desde el inicio de esta conexión hasta el punto de conflicto.</param>
/// <param name="SOtra">Distancia desde el inicio de la otra conexión hasta ese mismo punto.</param>
public sealed record Conflicto(Conexion Otra, float SPropio, float SOtra);

/// <summary>
/// Trayecto dentro de un cruce que une el final de un carril con el principio de otro.
/// Es una curva de Bézier; su radio de curvatura limita la velocidad según el conductor.
/// </summary>
public sealed class Conexion : ViaCirculacion
{
    public Carril Origen { get; }
    public Carril Destino { get; }
    public TipoGiro Giro { get; }

    /// <summary>Conexiones del mismo cruce con las que se cruza o confluye. Las rellena Grafo al preparar el nodo.</summary>
    public List<Conflicto> Conflictos { get; } = new();

    public override float LimiteMs => Mathf.Min(Origen.LimiteMs, Destino.LimiteMs);

    /// <summary>
    /// Radio con el que se calcula la velocidad en la curva. Las entradas y salidas de una rotonda (y los ramales de la circunvalación) son curvas muy cerradas en tan pocos
    /// metros, pero en una rotonda real los accesos se abren y se entra a unos 25-30 km/h: se usa un radio de al menos 20 m.
    /// </summary>
    public override float RadioMinimo =>
        Origen.Tramo.EsRotonda || Destino.Tramo.EsRotonda ? Mathf.Max(Eje.RadioMinimo, RadioMinimoRotonda)
        : Origen.Tramo.Categoria == CategoriaVia.Autovia || Destino.Tramo.Categoria == CategoriaVia.Autovia ? Mathf.Max(Eje.RadioMinimo, RadioMinimoRamal)
        : Mathf.Max(Eje.RadioMinimo, _grafo.RadioMinimoGiro);

    // En un giro real el coche no sigue el eje de los carriles: aprovecha la anchura del cruce y la esquina redondeada y traza una curva de
    // unos 10 m de radio o más (Grafo.RadioMinimoGiro). Aquí las cajas son pequeñas; se calcula la velocidad con al menos ese radio.
    private readonly Grafo _grafo;

    private const float RadioMinimoRotonda = 20f;
    /// <summary>Las salidas y entradas de la circunvalación son ramales amplios en la realidad (unos 35-40 km/h), no un giro cerrado de cruce.</summary>
    private const float RadioMinimoRamal = 50f;

    private Conexion(Carril origen, Carril destino, Trayectoria eje, TipoGiro giro, Grafo grafo) : base(eje)
    {
        _grafo = grafo;
        Origen = origen;
        Destino = destino;
        Giro = giro;
    }

    public static Conexion Crear(Carril a, Carril b, Grafo grafo)
    {
        var p0 = a.Eje.Puntos[^1];
        var d0 = a.Eje.Direccion(a.Eje.Longitud);
        var p3 = b.Eje.Puntos[0];
        var d1 = b.Eje.Direccion(0f);

        float k = 0.4f * p0.DistanceTo(p3);
        var p1 = p0 + d0 * k;
        var p2 = p3 - d1 * k;

        const int n = 16;
        var pts = new Vector2[n + 1];
        for (int i = 0; i <= n; i++)
        {
            float t = i / (float)n, u = 1f - t;
            pts[i] = u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
        }

        return new Conexion(a, b, new Trayectoria(pts), CalcularGiro(d0, d1), grafo);
    }

    /// <summary>Tipo de giro según la dirección de salida y la de entrada.</summary>
    public static TipoGiro CalcularGiro(Vector2 direccionSalida, Vector2 direccionEntrada)
    {
        // Con la Y hacia abajo, un producto cruzado positivo es un giro a la derecha.
        float giro = direccionSalida.Cross(direccionEntrada);
        return giro > 0.35f ? TipoGiro.Derecha : giro < -0.35f ? TipoGiro.Izquierda : TipoGiro.Recto;
    }
}

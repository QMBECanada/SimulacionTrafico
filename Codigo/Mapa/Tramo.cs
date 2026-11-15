using System.Collections.Generic;
using SimulacionTrafico.Nucleo;

namespace SimulacionTrafico.Mapa;

public enum CategoriaVia { Local = 0, Colectora = 1, Arteria = 2, Autovia = 3 }

/// <summary>
/// Calle entre dos nodos. Los carriles "de ida" van de A a B y los "de vuelta" de B a A,
/// ambos circulando por la derecha del sentido de la marcha.
/// </summary>
public sealed class Tramo
{
    public int Id { get; }

    /// <summary>Nombre estable para los ficheros de datos: H-fila-columna, V-fila-columna o C-n (circunvalación).</summary>
    public string Nombre { get; set; } = "";

    public Nodo A { get; }
    public Nodo B { get; }
    public CategoriaVia Categoria { get; }
    public int CarrilesIda { get; }
    public int CarrilesVuelta { get; }
    public float LimiteKmh { get; }

    /// <summary>Eje completo, de A a B (centro de los nodos). Recto salvo en las esquinas curvas de la circunvalación.</summary>
    public Trayectoria Eje { get; }

    /// <summary>Eje recortado donde empiezan y terminan los carriles (fuera de los cruces). Lo rellena Grafo.Construir.</summary>
    public Trayectoria EjeUtil { get; set; }

    public List<Carril> Carriles { get; } = new();

    /// <summary>True si es un tramo del anillo de una rotonda.</summary>
    public bool EsRotonda { get; set; }

    /// <summary>True si es un puente: va por encima de un río o de otra calle. Los coches se ven sobre él.</summary>
    public bool EsPuente { get; set; }

    /// <summary>True si es un túnel: pasa por debajo de otra calle. Los coches no se ven dentro.</summary>
    public bool EsTunel { get; set; }

    /// <summary>
    /// True si es un ramal de entrada o de salida de la autovía. Junto a la autovía corre paralelo a su carril derecho (carril de
    /// deceleración o de aceleración): su eje empieza o acaba con un tramo lateral, dentro del cruce, que no es parte del carril.
    /// </summary>
    public bool EsRamal { get; set; }

    /// <summary>La rotonda a la que pertenece este tramo (si es parte de un anillo).</summary>
    public Rotonda? Rotonda { get; set; }

    /// <summary>Radar de esta calle (null casi siempre).</summary>
    public Radar? Radar { get; set; }

    public int TotalCarriles => CarrilesIda + CarrilesVuelta;
    public float AnchoCalzada => TotalCarriles * Escala.AnchoCarril;
    public bool UnSentido => CarrilesIda == 0 || CarrilesVuelta == 0;

    /// <summary>Desplazamiento lateral (hacia la derecha de A-&gt;B) de la línea que separa los dos sentidos.</summary>
    public float OffsetLineaCentral => -AnchoCalzada / 2f + CarrilesVuelta * Escala.AnchoCarril;

    /// <summary>Desplazamiento del centro del carril k (0 = el más cercano a la línea central).</summary>
    public float OffsetCentroCarril(bool ida, int k) =>
        ida
            ? OffsetLineaCentral + (k + 0.5f) * Escala.AnchoCarril
            : OffsetLineaCentral - (k + 0.5f) * Escala.AnchoCarril;

    public Tramo(int id, Nodo a, Nodo b, CategoriaVia categoria, int carrilesIda, int carrilesVuelta, float limiteKmh,
        Trayectoria? eje = null)
    {
        Id = id;
        A = a;
        B = b;
        Categoria = categoria;
        CarrilesIda = carrilesIda;
        CarrilesVuelta = carrilesVuelta;
        LimiteKmh = limiteKmh;
        Eje = eje ?? new Trayectoria(new[] { a.Pos, b.Pos });
        EjeUtil = Eje;
    }
}

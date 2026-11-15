using System.Collections.Generic;
using System.Linq;
using Godot;

namespace SimulacionTrafico.Mapa;

/// <summary>Manzana de edificios o parque entre calles (rectángulo alineado con los ejes).</summary>
public sealed record Manzana(Rect2 Rect, bool Parque, int Semilla)
{
    /// <summary>Lugar que ocupa la manzana (null si son edificios sin más o un parque).</summary>
    public Lugar? Lugar { get; set; }
}

/// <summary>
/// Red de calles: nodos (cruces), tramos (calles) y carriles (por donde circulan los coches).
/// Es de solo lectura una vez construida y la comparten todos los coches.
/// </summary>
public sealed class Grafo
{
    public Rect2 Limites { get; }
    public List<Nodo> Nodos { get; } = new();
    public List<Tramo> Tramos { get; } = new();
    public List<Carril> Carriles { get; } = new();
    public List<Manzana> Manzanas { get; } = new();
    public List<Lugar> Lugares { get; } = new();
    public List<Rotonda> Rotondas { get; } = new();

    /// <summary>Río que cruza la ciudad (null si no hay).</summary>
    public Rio? Rio { get; set; }

    /// <summary>Radares de tráfico, en posiciones fijas (uno por cada 3 km², mínimo 2).</summary>
    public List<Radar> Radares { get; } = new();

    /// <summary>Cruces donde tienen su base los guardias de tráfico, repartidos por la ciudad (los primeros ya están bien repartidos).</summary>
    public List<Nodo> BasesGuardias { get; } = new();
    public Dictionary<TipoLugar, List<Lugar>> LugaresPorTipo { get; } = new();

    public void AgregarLugar(Lugar lugar)
    {
        Lugares.Add(lugar);
        if (!LugaresPorTipo.TryGetValue(lugar.Tipo, out var lista)) LugaresPorTipo[lugar.Tipo] = lista = new List<Lugar>();
        lista.Add(lugar);
    }

    private readonly Dictionary<(int, int), Conexion> _conexiones = new();

    public Grafo(Rect2 limites) => Limites = limites;

    /// <summary>Radio mínimo (m) con que se calcula la velocidad en los giros de los cruces. Lo fija la simulación con su configuración.</summary>
    public float RadioMinimoGiro { get; set; } = 10f;

    /// <summary>Multiplica las plazas de todos los lugares (1 = las normales; menos de 1 para estudiar aparcamientos llenos). Lo fija la simulación.</summary>
    public float FactorPlazas { get; set; } = 1f;

    /// <summary>Trayecto dentro del cruce entre dos carriles. Se crea la primera vez que alguien lo pide.</summary>
    public Conexion ObtenerConexion(Carril desde, Carril hasta)
    {
        var clave = (desde.Id, hasta.Id);
        if (!_conexiones.TryGetValue(clave, out var c))
            _conexiones[clave] = c = Conexion.Crear(desde, hasta, this);
        return c;
    }

    public Nodo AgregarNodo(Vector2 pos)
    {
        var n = new Nodo(Nodos.Count, pos);
        Nodos.Add(n);
        return n;
    }

    public Tramo AgregarTramo(Nodo a, Nodo b, CategoriaVia categoria, int ida, int vuelta, float limiteKmh, Trayectoria? eje = null)
    {
        var t = new Tramo(Tramos.Count, a, b, categoria, ida, vuelta, limiteKmh, eje);
        Tramos.Add(t);
        a.Tramos.Add(t);
        b.Tramos.Add(t);
        return t;
    }

    /// <summary>
    /// Carril por el que sale un coche que viene por <paramref name="desde"/> y entra en la calle <paramref name="hacia"/>:
    /// el de índice más parecido. Es siempre el mismo para el mismo par, así que las conexiones posibles son finitas.
    /// </summary>
    public Carril CarrilDestino(Carril desde, Tramo hacia) =>
        desde.Tramo.EsRamal && hacia.Categoria == CategoriaVia.Autovia
            ? hacia.Carriles.Where(l => l.Origen == desde.Destino).OrderByDescending(l => l.Indice).First()   // incorporación: al carril derecho
            : hacia.Carriles
            .Where(l => l.Origen == desde.Destino)
            .OrderBy(l => System.Math.Abs(l.Indice - desde.Indice))
            .First();

    /// <summary>Tipo de giro que haría un coche que sale de <paramref name="desde"/> hacia la calle <paramref name="hacia"/>.</summary>
    public TipoGiro GiroHacia(Carril desde, Tramo hacia) =>
        hacia.EsRamal && desde.Tramo.Categoria == CategoriaVia.Autovia && !desde.Tramo.EsRamal ? TipoGiro.Derecha
        : Conexion.CalcularGiro(desde.Eje.Direccion(desde.Longitud), CarrilDestino(desde, hacia).Eje.Direccion(0f));

    /// <summary>Carriles por los que se puede seguir desde el final de <paramref name="l"/> (sin cambio de sentido y sin los giros prohibidos).</summary>
    public static IEnumerable<Carril> SalientesPermitidos(Carril l) =>
        l.Destino.Salientes.Where(o => o.Tramo != l.Tramo && !l.Destino.GiroProhibido(l.Tramo, o.Tramo));

    /// <summary>Carriles desde los que se puede llegar al principio de <paramref name="l"/>.</summary>
    public static IEnumerable<Carril> EntrantesPermitidos(Carril l) =>
        l.Origen.Entrantes.Where(o => o.Tramo != l.Tramo && !l.Origen.GiroProhibido(o.Tramo, l.Tramo));

    /// <summary>Número de carriles de la calle en el mismo sentido que el dado.</summary>
    public static int CarrilesEnSentido(Carril c) => c.Ida ? c.Tramo.CarrilesIda : c.Tramo.CarrilesVuelta;

    /// <summary>
    /// A la izquierda se gira desde el carril más interior (índice 0) y a la derecha desde el más exterior;
    /// para seguir recto sirve cualquiera. Con un solo carril, todos los giros.
    /// </summary>
    public static bool PermiteGiro(Carril c, TipoGiro giro)
    {
        int n = CarrilesEnSentido(c);
        if (n <= 1) return true;
        return giro switch
        {
            TipoGiro.Izquierda => c.Indice == 0,
            TipoGiro.Derecha => c.Indice == n - 1,
            _ => true,
        };
    }

    /// <summary>Conexiones con las que la dada se cruza o confluye dentro de su cruce (se calculan la primera vez).</summary>
    public IReadOnlyList<Conflicto> ObtenerConflictos(Conexion m)
    {
        PrepararNodo(m.Origen.Destino);
        return m.Conflictos;
    }

    /// <summary>Calcula de una vez los conflictos de todos los cruces, para no hacerlo en mitad de la simulación.</summary>
    public void PrepararTodos()
    {
        foreach (var n in Nodos) PrepararNodo(n);
    }

    /// <summary>Dos trayectorias cuyos ejes pasan a menos de esto se disputan el espacio (dos coches de 1,8 m de ancho y algo de margen).</summary>
    private const float DistanciaCuerpos = 2.4f;

    private void PrepararNodo(Nodo n)
    {
        if (n.ConflictosListos) return;
        n.ConflictosListos = true;

        var conexiones = new List<Conexion>();
        foreach (var e in n.Entrantes)
            foreach (var grupo in n.Salientes.Where(l => l.Tramo != e.Tramo && !n.GiroProhibido(e.Tramo, l.Tramo)).GroupBy(l => l.Tramo))
                conexiones.Add(ObtenerConexion(e, CarrilDestino(e, grupo.Key)));
        n.Conexiones.AddRange(conexiones);

        for (int i = 0; i < conexiones.Count; i++)
            for (int j = i + 1; j < conexiones.Count; j++)
            {
                var a = conexiones[i];
                var b = conexiones[j];
                if (a.Origen.Tramo == b.Origen.Tramo) continue; // misma aproximación: no se disputan el paso
                if (a.Destino == b.Destino)
                {
                    a.Conflictos.Add(new Conflicto(b, a.Longitud, b.Longitud));
                    b.Conflictos.Add(new Conflicto(a, b.Longitud, a.Longitud));
                }
                else if (a.Eje.Interseccion(b.Eje, out float sa, out float sb)
                         || a.Eje.Acercamiento(b.Eje, DistanciaCuerpos, out sa, out sb))
                {
                    // Se cruzan, o pasan tan cerca que los coches (que tienen ancho) se tocarían: también es un conflicto.
                    a.Conflictos.Add(new Conflicto(b, sa, sb));
                    b.Conflictos.Add(new Conflicto(a, sb, sa));
                }
            }
    }

    /// <summary>Calcula radios de cruce y genera los carriles de todos los tramos. Llamar al terminar de añadir calles.</summary>
    public void Construir()
    {
        foreach (var n in Nodos)
            n.Radio = n.Tramos.Count == 0 ? 0f : n.Tramos.Max(t => t.AnchoCalzada) / 2f + 1f;

        foreach (var t in Tramos)
        {
            float largo = t.Eje.Longitud;
            // Con paso de peatones, el carril (y su línea de detención) acaba antes: la franja queda entre la línea y la caja del cruce.
            float ini = t.A.Radio + (TienePaso(t.A, t) ? RetranqueoPaso : 0f);
            float fin = largo - t.B.Radio - (TienePaso(t.B, t) ? RetranqueoPaso : 0f);
            if (t.EsRamal)
            {
                // Junto a la autovía el carril del ramal empieza (o acaba) al lado de su carril derecho, pasado el tramo lateral del eje.
                var p = t.Eje.Puntos;
                if (EsAutovia(t.A)) ini = p[0].DistanceTo(p[1]) + 2f;
                if (EsAutovia(t.B)) fin = largo - p[^1].DistanceTo(p[^2]) - 2f;
            }
            if (fin - ini < 2f)
            {
                ini = largo / 2f - 1f;
                fin = largo / 2f + 1f;
            }
            t.EjeUtil = t.Eje.Recortar(ini, fin);

            var ida = new List<Carril>();
            var vuelta = new List<Carril>();
            for (int k = 0; k < t.CarrilesIda; k++)
            {
                var eje = t.EjeUtil.Desplazar(t.OffsetCentroCarril(true, k));
                ida.Add(NuevoCarril(t, true, k, t.A, t.B, eje));
            }
            for (int k = 0; k < t.CarrilesVuelta; k++)
            {
                var eje = t.EjeUtil.Desplazar(t.OffsetCentroCarril(false, k)).Invertida();
                vuelta.Add(NuevoCarril(t, false, k, t.B, t.A, eje));
            }
            Vincular(ida);
            Vincular(vuelta);
        }

        // Semáforos: sus fases dependen de las calles del cruce (la onda verde la coordina la simulación, según su configuración).
        foreach (var n in Nodos) n.Semaforo?.Vincular(n);
    }

    /// <summary>Lo que se retrasa la línea de detención en una calle con paso de peatones: el ancho de la franja y un metro.</summary>
    public const float RetranqueoPaso = PasoPeatones.AnchoFranja + 1f;

    /// <summary>
    /// La calle <paramref name="t"/> tiene paso de peatones junto al cruce <paramref name="n"/>: en todos los cruces de tres o más calles salvo
    /// rotondas, autovía y ramales, en las calles que no son autovía, ramal, túnel ni anillo de rotonda y son lo bastante largas.
    /// </summary>
    public static bool TienePaso(Nodo n, Tramo t) =>
        n.Tramos.Count >= 3 && n.Control != TipoControl.Rotonda && !EsAutovia(n) && !n.Tramos.Any(x => x.EsRamal)
        && t.Categoria != CategoriaVia.Autovia && !t.EsRamal && !t.EsTunel && !t.EsRotonda
        && t.Eje.Longitud >= t.A.Radio + t.B.Radio + 2f * RetranqueoPaso + 8f;

    /// <summary>Nodo del borde del mapa donde una carretera sale de la ciudad (nombre PX-n): ahí se entra y se sale del exterior.</summary>
    public static bool EsBorde(Nodo n) => n.Nombre.StartsWith("PX-");

    /// <summary>Carril de una carretera que llega al borde: no tiene salida ni entrada por ese extremo, y no es un error.</summary>
    public static bool EsDelBorde(Carril l) => EsBorde(l.Origen) || EsBorde(l.Destino);

    /// <summary>True si por el nodo pasa la autovía (sin contar sus ramales).</summary>
    public static bool EsAutovia(Nodo n) => n.Tramos.Any(t => t.Categoria == CategoriaVia.Autovia && !t.EsRamal);

    private Carril NuevoCarril(Tramo t, bool ida, int k, Nodo origen, Nodo destino, Trayectoria eje)
    {
        var c = new Carril(Carriles.Count, t, ida, k, origen, destino, eje);
        Carriles.Add(c);
        t.Carriles.Add(c);
        origen.Salientes.Add(c);
        destino.Entrantes.Add(c);
        return c;
    }

    // Circulación por la derecha: el carril de índice mayor queda más a la derecha.
    private static void Vincular(List<Carril> carriles)
    {
        for (int i = 0; i < carriles.Count; i++)
        {
            if (i > 0) carriles[i].Izquierdo = carriles[i - 1];
            if (i < carriles.Count - 1) carriles[i].Derecho = carriles[i + 1];
        }
    }
}

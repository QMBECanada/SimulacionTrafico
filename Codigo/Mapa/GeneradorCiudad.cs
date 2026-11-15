using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SimulacionTrafico.Nucleo;

namespace SimulacionTrafico.Mapa;

/// <summary>
/// Genera una ciudad a partir de una semilla (mismo tamaño y semilla = misma ciudad).
/// Todas las calles son rectas y paralelas a los ejes: nada de diagonales. Las manzanas son rectángulos de
/// distinto tamaño: pequeñas en el centro y cada vez mayores hacia las afueras, con proporciones variadas.
/// Algunas manzanas grandes interrumpen una calle local: la calle termina contra ellas (cruces en T) en vez de
/// atravesar la ciudad de lado a lado. Las arterias y colectoras no se interrumpen nunca.
/// Hay avenida central 3/3, arterias 2/2 y 2/1, colectoras 1/1 y calles locales (algunas de sentido único).
/// Una autovía de circunvalación 3/3 rodea la ciudad con esquinas curvas y accesos desde arterias y colectoras.
/// Más adelante: rotondas, río y zonas con otros usos.
/// </summary>
public static partial class GeneradorCiudad
{
    // Tamaño de las manzanas (distancia entre calles): del centro a la periferia, con una variación aleatoria.
    private const float EspacioCentro = 70f;
    private const float EspacioPeriferia = 170f;
    private const float EspacioMinimo = 45f;

    private const float MargenBorde = 200f;       // de la primera calle al borde del mapa
    private const float SeparacionAnillo = 100f;  // de la última calle a la circunvalación
    private const float RadioEsquinaAnillo = 120f;
    private const float ProbParqueBase = 0.04f;
    private const float SeparacionMaximaPuentes = 900f;   // si dos puentes quedan más lejos, se añade otro entre ellos
    private const float ProbInterrupcion = 0.35f; // de los tramos de calle local que pueden interrumpirse

    private sealed record Via(CategoriaVia Categoria, int Ida, int Vuelta)
    {
        public float Mitad => (Ida + Vuelta) * Escala.AnchoCarril / 2f;
    }

    /// <param name="factorRotondas">Multiplica la probabilidad de que un cruce sea rotonda (0 = ninguna). Para experimentos.</param>
    public static Grafo Generar(float ancho, float alto, int semilla = 1, bool conRio = true, float factorRotondas = 1f)
    {
        // Con pocos puentes pueden quedar zonas aisladas o cruces sin entrada o sin salida (sobre todo por calles de sentido único que
        // acaban en la orilla). Se vuelve a generar la misma ciudad corrigiendo los cruces con problemas: si junto a ellos hay una calle
        // perpendicular al río sin puente, se le pone; si no, sus calles de sentido único pasan a ser de doble sentido.
        var forzados = new HashSet<(bool, int, int)>();
        var dobles = new HashSet<(bool, int, int)>();   // tramos de sentido único que pasan a doble sentido
        var sinGiros = new HashSet<string>();   // cruces donde no se prohíbe ningún giro porque dejaban el grafo desconectado
        Grafo g = null!;
        for (int intento = 0; intento < 14; intento++)
        {
            g = GenerarUnaVez(ancho, alto, semilla, conRio, factorRotondas, forzados, dobles, sinGiros, intento == 13, out var descartados);
            var malos = NodosProblema(g);
            if (malos.Count == 0) break;
            foreach (var n in malos) if (n.Nombre != "") sinGiros.Add(n.Nombre);
            foreach (var d in descartados.Where(d => malos.Contains(d.a) || malos.Contains(d.b))) forzados.Add(d.clave);
            foreach (var t in malos.SelectMany(n => n.Tramos).Where(t => t.CarrilesIda == 0 || t.CarrilesVuelta == 0))
                if (ClaveTramo(t.Nombre) is { } clave) dobles.Add(clave);
        }
        return g;
    }

    /// <summary>Clave (horizontal, línea, desde) de una calle de la cuadrícula a partir de su nombre («H-fila-columna» o «V-fila-columna»).</summary>
    private static (bool, int, int)? ClaveTramo(string nombre)
    {
        var partes = nombre.Split('-');
        if (partes.Length != 3 || !int.TryParse(partes[1], out int a) || !int.TryParse(partes[2], out int b)) return null;
        return partes[0] switch
        {
            "H" => (true, a - 1, b - 1),
            "V" => (false, b - 1, a - 1),
            _ => null,
        };
    }

    /// <summary>Nodos de carriles sin salida o fuera de la parte conectada con la circunvalación (vacío si el grafo está bien).</summary>
    private static HashSet<Nodo> NodosProblema(Grafo g)
    {
        var malos = new HashSet<Nodo>();
        foreach (var l in g.Carriles)
            if (!Grafo.EsDelBorde(l) && !Grafo.SalientesPermitidos(l).Any()) { malos.Add(l.Origen); malos.Add(l.Destino); }

        var nucleo = g.Carriles.FirstOrDefault(l => l.Tramo.Categoria == CategoriaVia.Autovia) ?? g.Carriles[0];
        HashSet<Carril> Alcance(bool adelante)
        {
            var visto = new HashSet<Carril> { nucleo };
            var cola = new Queue<Carril>();
            cola.Enqueue(nucleo);
            while (cola.Count > 0)
            {
                var l = cola.Dequeue();
                foreach (var o in adelante ? Grafo.SalientesPermitidos(l) : Grafo.EntrantesPermitidos(l))
                    if (visto.Add(o)) cola.Enqueue(o);
            }
            return visto;
        }
        var ida = Alcance(true);
        var vuelta = Alcance(false);
        foreach (var l in g.Carriles)
            if (!Grafo.EsDelBorde(l) && (!ida.Contains(l) || !vuelta.Contains(l))) { malos.Add(l.Origen); malos.Add(l.Destino); }
        return malos;
    }

    private static Grafo GenerarUnaVez(float ancho, float alto, int semilla, bool conRio, float factorRotondas, HashSet<(bool, int, int)> forzados, HashSet<(bool, int, int)> dobles,
        HashSet<string> sinGiros, bool todos,
        out List<((bool, int, int) clave, Nodo a, Nodo b)> descartados)
    {
        descartados = new List<((bool, int, int), Nodo, Nodo)>();
        var descartadosLocal = descartados;
        var rng = new RandomNumberGenerator { Seed = (ulong)semilla };
        var limites = new Rect2(-ancho / 2f, -alto / 2f, ancho, alto);

        var xs = Lineas(limites.Position.X, limites.End.X, rng);
        var ys = Lineas(limites.Position.Y, limites.End.Y, rng);
        var viasX = Enumerable.Range(0, xs.Count).Select(i => CrearVia(i, xs.Count, rng)).ToArray(); // calles verticales
        var viasY = Enumerable.Range(0, ys.Count).Select(j => CrearVia(j, ys.Count, rng)).ToArray(); // calles horizontales

        var rio = conRio ? Rio.Crear(limites, rng) : null;
        var g = new Grafo(limites) { Rio = rio };
        var nodos = new Nodo[xs.Count, ys.Count];

        // Los cruces que caen en el agua (o pegados a la orilla) no existen: las calles que pasan por ellos siguen de largo.
        float mitadRio = rio == null ? 0f : rio.Ancho / 2f;
        bool Agua(Vector2 p) => rio != null && rio.Distancia(p, mitadRio + 19f) < mitadRio + 18f;
        float DistRio(Vector2 p) => rio == null ? float.PositiveInfinity : rio.Distancia(p, mitadRio + 450f);
        var quitado = new bool[xs.Count, ys.Count];
        for (int i = 0; i < xs.Count; i++)
            for (int j = 0; j < ys.Count; j++)
            {
                quitado[i, j] = Agua(new Vector2(xs[i], ys[j]));
                if (!quitado[i, j]) (nodos[i, j] = g.AgregarNodo(new Vector2(xs[i], ys[j]))).Nombre = $"X-{j + 1}-{i + 1}";
            }
        bool CercaRio(int i, int j) => DistRio(new Vector2(xs[i], ys[j])) < mitadRio + 350f;

        var fusion = ElegirInterrupciones(xs.Count, ys.Count, viasX, viasY, rng, CercaRio);

        // Algunos cruces se convierten en rotondas: cada calle que llega a uno acaba en el anillo, no en el cruce.
        var puntas = new Dictionary<(int i, int j, int dir), Nodo>();
        CrearRotondas(g, xs, ys, viasX, viasY, fusion, puntas, rng, CercaRio, factorRotondas);
        // Nodo donde acaba la calle que sale del cruce (i,j) en la dirección dir (0 este, 1 sur, 2 oeste, 3 norte).
        Nodo Punta(int i, int j, int dir) => puntas.TryGetValue((i, j, dir), out var m) ? m : nodos[i, j];

        // El río se cruza por pocos puentes concretos (ver ElegirPuentes): solo las calles perpendiculares a él, siempre la avenida y
        // las arterias, y una colectora (o una local) donde entre dos puentes queda demasiada distancia. Las calles paralelas al río
        // no lo cruzan nunca: se cortan en la orilla, aunque un meandro les pase por delante.
        bool CruzaAgua(Vector2 a, Vector2 b)
        {
            if (rio == null) return false;
            float largo = a.DistanceTo(b);
            for (float d = 0f; d <= largo; d += 4f)
                if (rio.Distancia(a.Lerp(b, d / largo), mitadRio + 3f) < mitadRio + 2f) return true;
            return false;
        }

        // Tramos posibles de cada calle entre cruces que existen: (horizontal, línea, desde, hasta, cruza el agua).
        var tramosPlan = new List<(bool h, int linea, int desde, int hasta, bool agua)>();
        for (int j = 0; j < ys.Count; j++)
        {
            int prev = -1;
            for (int i = 0; i < xs.Count; i++)
            {
                if (quitado[i, j]) continue;
                if (prev >= 0)
                {
                    bool contiguo = i == prev + 1;
                    bool agua = !contiguo || CruzaAgua(nodos[prev, j].Pos, nodos[i, j].Pos);
                    if (agua || !fusion.QuitarH[prev, j]) tramosPlan.Add((true, j, prev, i, agua));
                }
                prev = i;
            }
        }
        for (int i = 0; i < xs.Count; i++)
        {
            int prev = -1;
            for (int j = 0; j < ys.Count; j++)
            {
                if (quitado[i, j]) continue;
                if (prev >= 0)
                {
                    bool contiguo = j == prev + 1;
                    bool agua = !contiguo || CruzaAgua(nodos[i, prev].Pos, nodos[i, j].Pos);
                    if (agua || !fusion.QuitarV[i, prev]) tramosPlan.Add((false, i, prev, j, agua));
                }
                prev = j;
            }
        }

        // Líneas con puente: solo las perpendiculares al río (si el río corre de oeste a este, las calles verticales).
        bool rioHorizontal = rio?.Horizontal ?? true;
        var puentes = rio == null ? new HashSet<int>()
            : ElegirPuentes(tramosPlan.Where(t => t.agua && t.h != rioHorizontal).Select(t => t.linea).Distinct().ToList(),
                            rioHorizontal ? xs : ys, rioHorizontal ? viasX : viasY, forzados, todos);
        foreach (var t in tramosPlan.Where(t => t.agua && t.h != rioHorizontal && !puentes.Contains(t.linea)))
            descartadosLocal.Add(((t.h, t.linea, 0), Extremo(t, true), Extremo(t, false)));
        Nodo Extremo((bool h, int linea, int desde, int hasta, bool agua) t, bool inicio) =>
            t.h ? nodos[inicio ? t.desde : t.hasta, t.linea] : nodos[t.linea, inicio ? t.desde : t.hasta];
        tramosPlan.RemoveAll(t => t.agua && (t.h == rioHorizontal || !puentes.Contains(t.linea)));

        // Las calles que se quedan sin salida en la orilla se recortan hasta el cruce anterior (un cruce con una sola calle no es
        // un cruce). Solo cerca del río, y no en los cruces del borde a los que puede llegar un acceso de la circunvalación.
        var grado = new int[xs.Count, ys.Count];
        void Contar((bool h, int linea, int desde, int hasta, bool agua) t, int signo)
        {
            if (t.h) { grado[t.desde, t.linea] += signo; grado[t.hasta, t.linea] += signo; }
            else { grado[t.linea, t.desde] += signo; grado[t.linea, t.hasta] += signo; }
        }
        foreach (var t in tramosPlan) Contar(t, 1);
        bool PosibleAcceso(int i, int j) =>
            ((i == 0 || i == xs.Count - 1) && viasY[j].Categoria >= CategoriaVia.Colectora) || ((j == 0 || j == ys.Count - 1) && viasX[i].Categoria >= CategoriaVia.Colectora);
        bool Recortable(int i, int j) => grado[i, j] == 1 && !PosibleAcceso(i, j) && CercaRio(i, j);
        for (bool cambio = true; cambio;)
        {
            cambio = false;
            for (int k = tramosPlan.Count - 1; k >= 0; k--)
            {
                var t = tramosPlan[k];
                var (ai, aj) = t.h ? (t.desde, t.linea) : (t.linea, t.desde);
                var (bi, bj) = t.h ? (t.hasta, t.linea) : (t.linea, t.hasta);
                if (!Recortable(ai, aj) && !Recortable(bi, bj)) continue;
                Contar(t, -1);
                tramosPlan.RemoveAt(k);
                cambio = true;
            }
        }

        foreach (var t in tramosPlan)
        {
            var via = t.h ? viasY[t.linea] : viasX[t.linea];
            // Junto al río, una calle de sentido único que acaba en la orilla deja cruces sin entrada o sin salida: es de doble sentido.
            var (ci, cj) = t.h ? (t.desde, t.linea) : (t.linea, t.desde);
            var (di, dj) = t.h ? (t.hasta, t.linea) : (t.linea, t.hasta);
            if ((via.Ida == 0 || via.Vuelta == 0) && (CercaRio(ci, cj) || CercaRio(di, dj) || dobles.Contains((t.h, t.linea, t.desde))))
                via = via with { Ida = 1, Vuelta = 1 };
            var tramo = t.h
                ? AgregarCalle(g, Punta(t.desde, t.linea, 0), Punta(t.hasta, t.linea, 2), via)
                : AgregarCalle(g, Punta(t.linea, t.desde, 1), Punta(t.linea, t.hasta, 3), via);
            tramo.Nombre = t.h ? $"H-{t.linea + 1}-{t.desde + 1}" : $"V-{t.desde + 1}-{t.linea + 1}";
            tramo.EsPuente = t.agua;
        }

        CrearCircunvalacion(g, nodos, xs, ys, viasX, viasY, rio);

        for (int i = 0; i < xs.Count; i++)
            for (int j = 0; j < ys.Count; j++)
                if (nodos[i, j] != null) AsignarControl(nodos[i, j], viasX[i].Categoria, viasY[j].Categoria, rng);

        var bloques = new List<(Manzana m, int i, int j, int i1, int j1)>();
        for (int i = 0; i < xs.Count - 1; i++)
            for (int j = 0; j < ys.Count - 1; j++)
            {
                if (fusion.Absorbida[i, j]) continue;

                // Una manzana puede ocupar dos celdas contiguas si una calle local se interrumpe entre ellas.
                int iFin = fusion.UnirDerecha[i, j] ? i + 2 : i + 1;
                int jFin = fusion.UnirAbajo[i, j] ? j + 2 : j + 1;
                float x0 = xs[i] + viasX[i].Mitad + Escala.AnchoAcera;
                float x1 = xs[iFin] - viasX[iFin].Mitad - Escala.AnchoAcera;
                float y0 = ys[j] + viasY[j].Mitad + Escala.AnchoAcera;
                float y1 = ys[jFin] - viasY[jFin].Mitad - Escala.AnchoAcera;
                if (x1 - x0 <= 10f || y1 - y0 <= 10f) continue;
                if (quitado[i, j] || quitado[iFin, j] || quitado[i, jFin] || quitado[iFin, jFin]) continue;
                // Las manzanas que tocan el agua no se construyen; las que quedan junto a la orilla son parque.
                float cercaRio = DistRio(new Vector2((x0 + x1) / 2f, (y0 + y1) / 2f)) - mitadRio - 0.5f * Mathf.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
                if (cercaRio < 40f) continue;

                // Las manzanas grandes tienen más probabilidad de ser parque.
                float probParque = Mathf.Clamp(ProbParqueBase + (x1 - x0) * (y1 - y0) / 200000f, 0f, 0.3f);
                var manzana = new Manzana(new Rect2(x0, y0, x1 - x0, y1 - y0),
                    rng.Randf() < probParque || cercaRio < 110f, rng.RandiRange(0, int.MaxValue));
                g.Manzanas.Add(manzana);
                bloques.Add((manzana, i, j, iFin, jFin));
            }

        if (rio != null) MarcarPuentes(g, rio);

        g.Construir();
        AsignarGirosProhibidos(g, new RandomNumberGenerator { Seed = (ulong)semilla * 2654435761UL + 3UL }, sinGiros);
        CrearLugares(g, Punta, bloques, rng);
        ElegirBasesGuardias(g, rng);
        ElegirRadares(g);
        return g;
    }

    /// <summary>
    /// Elige qué calles perpendiculares al río tienen puente (devuelve sus índices de línea). Siempre la avenida y las arterias; después,
    /// mientras entre dos puentes (o entre un puente y la circunvalación, que cruza el río por los extremos) quede más de
    /// <see cref="SeparacionMaximaPuentes"/>, se añade la colectora más centrada en ese hueco, o una local si no hay colectora.
    /// </summary>
    private static HashSet<int> ElegirPuentes(List<int> candidatas, List<float> coordenadas, Via[] vias, HashSet<(bool, int, int)> forzados, bool todos)
    {
        if (todos) return candidatas.ToHashSet();
        var elegidas = candidatas.Where(l => vias[l].Categoria >= CategoriaVia.Arteria || forzados.Any(f => f.Item2 == l)).ToHashSet();
        float inicio = coordenadas[0] - SeparacionAnillo, fin = coordenadas[^1] + SeparacionAnillo;   // la circunvalación
        while (true)
        {
            var marcas = elegidas.Select(l => coordenadas[l]).Append(inicio).Append(fin).OrderBy(c => c).ToList();
            int mejor = -1;
            float mejorPuntos = float.PositiveInfinity;
            for (int k = 0; k + 1 < marcas.Count; k++)
            {
                if (marcas[k + 1] - marcas[k] <= SeparacionMaximaPuentes) continue;
                float medio = (marcas[k] + marcas[k + 1]) / 2f;
                foreach (int l in candidatas)
                {
                    if (elegidas.Contains(l) || coordenadas[l] <= marcas[k] || coordenadas[l] >= marcas[k + 1]) continue;
                    float puntos = Mathf.Abs(coordenadas[l] - medio) + (vias[l].Categoria == CategoriaVia.Local ? 250f : 0f);
                    if (puntos < mejorPuntos) { mejorPuntos = puntos; mejor = l; }
                }
            }
            if (mejor < 0) return elegidas;
            elegidas.Add(mejor);
        }
    }

    private sealed class Interrupciones
    {
        public bool[,] QuitarV = null!;      // tramo vertical entre (i,j) y (i,j+1) que no existe
        public bool[,] QuitarH = null!;      // tramo horizontal entre (i,j) y (i+1,j) que no existe
        public bool[,] UnirDerecha = null!;  // la celda (i,j) y la (i+1,j) forman una sola manzana
        public bool[,] UnirAbajo = null!;    // la celda (i,j) y la (i,j+1) forman una sola manzana
        public bool[,] Absorbida = null!;    // la celda ya forma parte de la manzana de otra
    }

    /// <summary>
    /// Elige tramos de calle local que no se construyen porque una manzana ocupa su lugar. Para que el resultado
    /// siga siendo una ciudad transitable y con manzanas rectangulares:
    /// solo calles locales, solo tramos entre cruces interiores, como mucho un tramo quitado por cruce
    /// (así cada cruce se queda como mínimo en T) y como mucho una fusión por celda (la manzana resultante es un rectángulo).
    /// </summary>
    private static Interrupciones ElegirInterrupciones(int nx, int ny, Via[] viasX, Via[] viasY, RandomNumberGenerator rng, Func<int, int, bool> cercaRio)
    {
        var r = new Interrupciones
        {
            QuitarV = new bool[nx, ny], QuitarH = new bool[nx, ny],
            UnirDerecha = new bool[nx, ny], UnirAbajo = new bool[nx, ny], Absorbida = new bool[nx, ny],
        };
        var nodoUsado = new bool[nx, ny];
        var celdaUsada = new bool[nx, ny];
        // Cerca del río no se interrumpen calles (los puentes y túneles ya las alteran).
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
                if (cercaRio(i, j)) nodoUsado[i, j] = celdaUsada[i, j] = true;

        var candidatos = new List<(bool vertical, int i, int j)>();
        for (int i = 1; i < nx - 1; i++)
            for (int j = 1; j < ny - 2; j++)
                if (viasX[i].Categoria == CategoriaVia.Local) candidatos.Add((true, i, j));
        for (int j = 1; j < ny - 1; j++)
            for (int i = 1; i < nx - 2; i++)
                if (viasY[j].Categoria == CategoriaVia.Local) candidatos.Add((false, i, j));

        // Orden aleatorio (Fisher-Yates).
        for (int k = candidatos.Count - 1; k > 0; k--)
        {
            int m = rng.RandiRange(0, k);
            (candidatos[k], candidatos[m]) = (candidatos[m], candidatos[k]);
        }

        foreach (var (vertical, i, j) in candidatos)
        {
            if (rng.Randf() > ProbInterrupcion) continue;

            if (vertical)
            {
                // Quitar el tramo (i,j)-(i,j+1) junta las celdas (i-1,j) e (i,j).
                if (nodoUsado[i, j] || nodoUsado[i, j + 1] || celdaUsada[i - 1, j] || celdaUsada[i, j]) continue;
                nodoUsado[i, j] = nodoUsado[i, j + 1] = true;
                celdaUsada[i - 1, j] = celdaUsada[i, j] = true;
                r.QuitarV[i, j] = true;
                r.UnirDerecha[i - 1, j] = true;
                r.Absorbida[i, j] = true;
            }
            else
            {
                // Quitar el tramo (i,j)-(i+1,j) junta las celdas (i,j-1) e (i,j).
                if (nodoUsado[i, j] || nodoUsado[i + 1, j] || celdaUsada[i, j - 1] || celdaUsada[i, j]) continue;
                nodoUsado[i, j] = nodoUsado[i + 1, j] = true;
                celdaUsada[i, j - 1] = celdaUsada[i, j] = true;
                r.QuitarH[i, j] = true;
                r.UnirAbajo[i, j - 1] = true;
                r.Absorbida[i, j] = true;
            }
        }
        return r;
    }

    /// <summary>Posiciones de las calles a lo largo de un eje: más juntas en el centro, más separadas en las afueras.</summary>
    private static List<float> Lineas(float desde, float hasta, RandomNumberGenerator rng)
    {
        float centro = (desde + hasta) / 2f, mitad = (hasta - desde) / 2f;
        var res = new List<float>();
        float p = desde + MargenBorde;
        while (p <= hasta - MargenBorde)
        {
            res.Add(p);
            float u = Mathf.Clamp(Mathf.Abs(p - centro) / mitad, 0f, 1f);
            float espacio = Mathf.Lerp(EspacioCentro, EspacioPeriferia, u);
            float variacion = Mathf.Clamp(Mathf.Exp(rng.Randfn(0f, 0.3f)), 0.6f, 1.9f);
            p += Mathf.Max(EspacioMinimo, espacio * variacion);
        }
        return res;
    }

    private static Via CrearVia(int i, int total, RandomNumberGenerator rng)
    {
        if (i == total / 2) return new Via(CategoriaVia.Arteria, 3, 3);
        float r = rng.Randf();
        if (i == 0 || i == total - 1) return new Via(CategoriaVia.Colectora, 1, 1); // el borde de la cuadrícula nunca es de un sentido
        if (i % 6 == 3)
            return r < 0.70f ? new Via(CategoriaVia.Arteria, 2, 2)
                 : r < 0.85f ? new Via(CategoriaVia.Arteria, 2, 1)
                             : new Via(CategoriaVia.Arteria, 1, 2);
        if (i % 3 == 0) return new Via(CategoriaVia.Colectora, 1, 1);
        return r < 0.70f ? new Via(CategoriaVia.Local, 1, 1)
             : r < 0.85f ? new Via(CategoriaVia.Local, 1, 0)
                         : new Via(CategoriaVia.Local, 0, 1);
    }

    /// <summary>Límites de velocidad: todas las calles a 50 km/h y la autovía a 120 km/h.</summary>
    private static float Limite(CategoriaVia c) => c == CategoriaVia.Autovia ? 120f : 50f;

    /// <summary>Límite de una calle: las de un solo carril (un sentido, un carril) van a 30 km/h; el resto, a 50.</summary>
    private static float Limite(Via v) => v.Ida + v.Vuelta == 1 ? 30f : Limite(v.Categoria);

    /// <summary>Límite en el anillo de las rotondas.</summary>
    private const float LimiteRotonda = 40f;

    private static Tramo AgregarCalle(Grafo g, Nodo a, Nodo b, Via v) =>
        g.AgregarTramo(a, b, v.Categoria, v.Ida, v.Vuelta, Limite(v));

}

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SimulacionTrafico.Nucleo;

namespace SimulacionTrafico.Mapa;

// GeneradorCiudad: cruces (rotondas, giros prohibidos, señales), puentes, radares y bases de los guardias.
public static partial class GeneradorCiudad
{
    // ---- Rotondas ----

    private const float ProbRotondaLocal = 0.15f, ProbRotondaMixta = 0.25f, ProbRotondaColectoras = 0.45f;

    private const float SeparacionMinimaRotonda = 75f;   // las calles que llegan deben medir al menos esto (el anillo ocupa ya 30-34 m)

    /// <summary>
    /// Convierte en rotondas algunos cruces interiores entre calles locales y colectoras (más probable cuanto mayor es la
    /// calle; así hay menos semáforos). Cada calle que llega acaba en un nodo del anillo, que se rellena en <paramref name="puntas"/>.
    /// El anillo es de un sentido (antihorario, circulación por la derecha) y tiene dos carriles entre colectoras.
    /// </summary>
    private static void CrearRotondas(Grafo g, List<float> xs, List<float> ys, Via[] viasX, Via[] viasY, Interrupciones fusion,
        Dictionary<(int i, int j, int dir), Nodo> puntas, RandomNumberGenerator rng, Func<int, int, bool> cercaRio, float factorRotondas)
    {
        var elegidas = new HashSet<(int, int)>();
        for (int j = 1; j < ys.Count - 1; j++)
            for (int i = 1; i < xs.Count - 1; i++)
            {
                var cx = viasX[i].Categoria;
                var cy = viasY[j].Categoria;
                if (cx > CategoriaVia.Colectora || cy > CategoriaVia.Colectora) continue;
                if (elegidas.Contains((i - 1, j)) || elegidas.Contains((i, j - 1))) continue;   // sin rotondas pegadas
                if (cercaRio(i, j)) continue;

                bool este = !fusion.QuitarH[i, j], oeste = !fusion.QuitarH[i - 1, j];
                bool sur = !fusion.QuitarV[i, j], norte = !fusion.QuitarV[i, j - 1];
                if ((este ? 1 : 0) + (oeste ? 1 : 0) + (sur ? 1 : 0) + (norte ? 1 : 0) < 3) continue;
                if ((este && xs[i + 1] - xs[i] < SeparacionMinimaRotonda) || (oeste && xs[i] - xs[i - 1] < SeparacionMinimaRotonda) ||
                    (sur && ys[j + 1] - ys[j] < SeparacionMinimaRotonda) || (norte && ys[j] - ys[j - 1] < SeparacionMinimaRotonda)) continue;

                float prob = cx == CategoriaVia.Colectora && cy == CategoriaVia.Colectora ? ProbRotondaColectoras
                           : cx == CategoriaVia.Colectora || cy == CategoriaVia.Colectora ? ProbRotondaMixta : ProbRotondaLocal;
                if (rng.Randf() > prob * factorRotondas) continue;

                elegidas.Add((i, j));
                ConstruirRotonda(g, new Vector2(xs[i], ys[j]), i, j, cx == CategoriaVia.Colectora && cy == CategoriaVia.Colectora ? 2 : 1,
                    este, sur, oeste, norte, puntas);
            }
    }

    private static void ConstruirRotonda(Grafo g, Vector2 centro, int i, int j, int carriles, bool este, bool sur, bool oeste, bool norte,
        Dictionary<(int i, int j, int dir), Nodo> puntas)
    {
        // Radio del eje del anillo: con unos 3,5 m/s² de aceleración lateral (conductor normal) se circula a 37-40 km/h, cerca del límite de 40.
        float radio = carriles == 2 ? 34f : 30f;
        // En sentido antihorario visto desde arriba: este, norte, oeste, sur. Con la Y hacia abajo, los ángulos decrecen.
        var rotonda = new Rotonda($"R-{j + 1}-{i + 1}", centro, radio - carriles * Escala.AnchoCarril / 2f - 0.7f);
        var brazos = new List<(int dir, float angulo)>();
        if (este) brazos.Add((0, 0f));
        if (norte) brazos.Add((3, -0.5f * Mathf.Pi));
        if (oeste) brazos.Add((2, -Mathf.Pi));
        if (sur) brazos.Add((1, -1.5f * Mathf.Pi));

        var nodos = new List<Nodo>();
        for (int k = 0; k < brazos.Count; k++)
        {
            var a = brazos[k].angulo;
            var nodo = g.AgregarNodo(centro + radio * new Vector2(Mathf.Cos(a), Mathf.Sin(a)));
            nodo.Nombre = $"RX-{j + 1}-{i + 1}-{k + 1}";
            nodo.Control = TipoControl.Rotonda;
            nodos.Add(nodo);
            puntas[(i, j, brazos[k].dir)] = nodo;
        }

        for (int k = 0; k < brazos.Count; k++)
        {
            var desde = nodos[k];
            var hasta = nodos[(k + 1) % nodos.Count];
            float a0 = brazos[k].angulo, a1 = brazos[(k + 1) % brazos.Count].angulo;
            while (a1 >= a0) a1 -= 2f * Mathf.Pi;   // siempre en sentido antihorario

            int puntos = Mathf.Max(8, (int)((a0 - a1) * radio / 1.5f));
            var pts = Trayectoria.Arco(centro, radio, a0, a1, puntos).Puntos;
            pts[0] = desde.Pos;
            pts[^1] = hasta.Pos;
            var tramo = g.AgregarTramo(desde, hasta, CategoriaVia.Local, carriles, 0, LimiteRotonda, new Trayectoria(pts));
            tramo.Nombre = $"R-{j + 1}-{i + 1}-{k + 1}";
            tramo.EsRotonda = true;
            tramo.Rotonda = rotonda;
            rotonda.Tramos.Add(tramo);
        }

        // Los coches del anillo tienen prioridad; los que entran ceden el paso.
        foreach (var n in nodos)
            foreach (var t in n.Tramos.Where(t => t.EsRotonda)) n.Prioritarios.Add(t);

        g.Rotondas.Add(rotonda);
    }

    /// <summary>Los tramos que cruzan el agua sin ser ya túneles o puentes (los que tienen los dos cruces en seco) son puentes.</summary>
    private static void MarcarPuentes(Grafo g, Rio rio)
    {
        float mitad = rio.Ancho / 2f;
        foreach (var t in g.Tramos)
        {
            if (t.EsRotonda || t.EsPuente || t.EsTunel) continue;
            float largo = t.Eje.Longitud;
            if (float.IsInfinity(rio.Distancia(t.Eje.Posicion(largo / 2f), mitad + largo / 2f + 20f))) continue;
            for (float s = 0f; s <= largo; s += 4f)
                if (rio.Distancia(t.Eje.Posicion(s), mitad + 3f) < mitad + 2f) { t.EsPuente = true; break; }
        }
    }

    // ---- Giros prohibidos ----

    private const float ProbNoIzquierdaLocal = 0.06f;
    private const float ProbNoIzquierdaColectora = 0.20f;
    private const float ProbNoIzquierdaArteria = 0.30f;
    private const float ProbNoRectoLocal = 0.12f;

    /// <summary>
    /// No todas las calles permiten todos los giros en todos los cruces. Algunas aproximaciones no pueden girar a la izquierda (más
    /// a menudo cuanto mayor es la calle) y algunas calles locales que llegan a una arteria no pueden seguir recto. Siempre queda
    /// al menos una salida permitida desde cada aproximación; el grafo entero se comprueba después (ver <see cref="Generar"/>).
    /// Nunca en rotondas ni en la circunvalación.
    /// </summary>
    private static void AsignarGirosProhibidos(Grafo g, RandomNumberGenerator rng, HashSet<string> sinGiros)
    {
        foreach (var n in g.Nodos)
        {
            if (n.Control == TipoControl.Rotonda || n.Tramos.Count < 3 || n.Nombre == "" || sinGiros.Contains(n.Nombre)) continue;
            if (n.Tramos.Any(t => t.Categoria == CategoriaVia.Autovia || t.EsRotonda || t.EsRamal)) continue;
            bool conArteria = n.Tramos.Any(t => t.Categoria >= CategoriaVia.Arteria);

            foreach (var grupo in n.Entrantes.GroupBy(l => l.Tramo))
            {
                var e = grupo.First();
                var origen = e.Tramo;
                var salidas = n.Salientes.Where(l => l.Tramo != origen).Select(l => l.Tramo).Distinct().ToList();
                if (salidas.Count < 2) continue;
                int permitidas = salidas.Count;

                float pIzquierda = origen.Categoria switch
                {
                    CategoriaVia.Local => ProbNoIzquierdaLocal,
                    CategoriaVia.Colectora => ProbNoIzquierdaColectora,
                    _ => ProbNoIzquierdaArteria,
                };
                var izquierda = salidas.FirstOrDefault(h => g.GiroHacia(e, h) == TipoGiro.Izquierda);
                if (izquierda != null && rng.Randf() < pIzquierda && permitidas > 1)
                {
                    n.GirosProhibidos.Add((origen.Id, izquierda.Id));
                    permitidas--;
                }

                var recto = salidas.FirstOrDefault(h => g.GiroHacia(e, h) == TipoGiro.Recto);
                if (recto != null && origen.Categoria == CategoriaVia.Local && conArteria && rng.Randf() < ProbNoRectoLocal && permitidas > 1)
                    n.GirosProhibidos.Add((origen.Id, recto.Id));
            }
        }
    }

    // ---- Radares ----

    /// <summary>
    /// Un radar por cada 3 km² (mínimo 2), en arterias, avenidas y circunvalación, repartidos por la ciudad: cada nuevo
    /// en la calle más lejana de los ya colocados. Cada radar controla todos los sentidos de su calle.
    /// </summary>
    /// <summary>Radares que se ponen si no se indica otra cantidad: uno por cada 3 km², mínimo 2.</summary>
    public static int RadaresPorDefecto(float ancho, float alto) => System.Math.Max(2, (int)System.Math.Round(ancho * alto / 1e6 / 3.0));

    /// <summary>Máximo de radares que se pueden pedir.</summary>
    public const int RadaresMaximo = 100;

    /// <summary>
    /// Coloca los radares (quita los que hubiera): en arterias, avenida y circunvalación, cada uno en la calle más lejana de los ya
    /// puestos. Sin <paramref name="cantidad"/>, los que corresponden al tamaño de la ciudad; como mucho <see cref="RadaresMaximo"/>.
    /// </summary>
    public static void ElegirRadares(Grafo g, int? cantidadPedida = null)
    {
        foreach (var r in g.Radares) r.Tramo.Radar = null;
        g.Radares.Clear();
        int cantidad = System.Math.Clamp(cantidadPedida ?? RadaresPorDefecto(g.Limites.Size.X, g.Limites.Size.Y), 0, RadaresMaximo);
        if (cantidad == 0) return;
        var candidatos = g.Tramos.Where(t => t.Categoria >= CategoriaVia.Arteria && !t.EsRamal && !t.EsTunel && t.Eje.Longitud >= 60f).OrderBy(t => t.Id).ToList();
        if (candidatos.Count == 0) return;

        var punto = candidatos.Select(t => t.Eje.Posicion(t.Eje.Longitud / 2f)).ToArray();
        var distancia = new float[candidatos.Count];
        System.Array.Fill(distancia, float.PositiveInfinity);
        int actual = 0;
        for (int k = 0; k < cantidad && k < candidatos.Count; k++)
        {
            var radar = new Radar(g.Radares.Count, candidatos[actual]);
            candidatos[actual].Radar = radar;
            g.Radares.Add(radar);
            int siguiente = 0;
            float mayor = -1f;
            for (int i = 0; i < candidatos.Count; i++)
            {
                distancia[i] = Mathf.Min(distancia[i], punto[i].DistanceSquaredTo(punto[actual]));
                if (distancia[i] > mayor)
                {
                    mayor = distancia[i];
                    siguiente = i;
                }
            }
            actual = siguiente;
        }
    }

    // ---- Bases de los guardias ----

    private const int MaxBasesGuardias = 600;

    /// <summary>
    /// Elige hasta 600 cruces repartidos por la ciudad (cada nuevo, lo más lejos posible de los ya elegidos). Los guardias
    /// toman los primeros: sea cual sea su número, quedan bien repartidos.
    /// </summary>
    private static void ElegirBasesGuardias(Grafo g, RandomNumberGenerator rng)
    {
        var candidatos = g.Nodos.Where(n => n.Tramos.Count >= 3).ToList();
        if (candidatos.Count == 0) return;

        var distancia = new float[candidatos.Count];
        System.Array.Fill(distancia, float.PositiveInfinity);
        int actual = rng.RandiRange(0, candidatos.Count - 1);
        for (int k = 0; k < MaxBasesGuardias && k < candidatos.Count; k++)
        {
            g.BasesGuardias.Add(candidatos[actual]);
            int siguiente = 0;
            float mayor = -1f;
            for (int i = 0; i < candidatos.Count; i++)
            {
                distancia[i] = Mathf.Min(distancia[i], candidatos[i].Pos.DistanceSquaredTo(candidatos[actual].Pos));
                if (distancia[i] > mayor)
                {
                    mayor = distancia[i];
                    siguiente = i;
                }
            }
            actual = siguiente;
        }
    }

    // ---- Control de los cruces ----

    private static void AsignarControl(Nodo n, CategoriaVia h, CategoriaVia v, RandomNumberGenerator rng)
    {
        if (n.Tramos.Count < 3) return; // extremo o esquina del mapa

        if (h >= CategoriaVia.Colectora && v >= CategoriaVia.Colectora)
        {
            n.Control = TipoControl.Semaforo;
            n.Semaforo = new Semaforo(rng.RandfRange(20f, 32f), rng.RandfRange(20f, 32f), rng.RandfRange(0f, 90f));
            return;
        }

        n.Control = (h >= CategoriaVia.Colectora || v >= CategoriaVia.Colectora) ? TipoControl.Ceda
                  : rng.Randf() < 0.4f ? TipoControl.Stop : TipoControl.Ceda;

        // Calle con prioridad: la de mayor categoría; si empatan, la que atraviesa el cruce; si no, al azar.
        int horizontales = n.Tramos.Count(t => t.A.Pos.Y == t.B.Pos.Y);
        int verticales = n.Tramos.Count - horizontales;
        bool prioridadHorizontal = h != v ? h > v
                                 : horizontales != verticales ? horizontales > verticales
                                 : rng.Randf() < 0.5f;
        foreach (var t in n.Tramos)
            if ((t.A.Pos.Y == t.B.Pos.Y) == prioridadHorizontal) n.Prioritarios.Add(t);
    }
}

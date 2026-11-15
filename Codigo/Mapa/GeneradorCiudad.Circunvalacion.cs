using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SimulacionTrafico.Nucleo;

namespace SimulacionTrafico.Mapa;

// GeneradorCiudad: circunvalación (autovía alrededor de la ciudad) y sus enlaces con las calles y las carreteras.
public static partial class GeneradorCiudad
{
    // ---- Circunvalación ----

    /// <summary>
    /// Autovía 3/3 en forma de rectángulo con las esquinas redondeadas, a 100 m de la última calle. Se conecta con
    /// las arterias y colectoras que quedan lejos de las esquinas. En esos cruces tiene prioridad la autovía.
    /// </summary>
    private static void CrearCircunvalacion(Grafo g, Nodo[,] nodos, List<float> xs, List<float> ys, Via[] viasX, Via[] viasY, Rio? rio)
    {
        float R = RadioEsquinaAnillo;
        float x0 = xs[0] - SeparacionAnillo, x1 = xs[^1] + SeparacionAnillo;
        float y0 = ys[0] - SeparacionAnillo, y1 = ys[^1] + SeparacionAnillo;
        // Accesos: enlaces en diamante (ver ConstruirEnlace), solo en arterias y colectoras, lejos de las esquinas, separados entre sí lo
        // bastante para que quepan los ramales y sin agua en su zona. Se eligen primero las arterias.
        float L = LargoEnlace;
        float margen = R + L + 30f;
        bool Seco(Vector2 p) => rio == null || rio.Distancia(p, rio.Ancho / 2f + 30f) >= rio.Ancho / 2f + 25f;
        bool ZonaSeca(Vector2 centro, Vector2 a, Vector2 o)
        {
            for (float s = -L - 20f; s <= L + 20f; s += 10f)
                for (float d = -DistanciaTerminal - 10f; d <= DistanciaTerminal + 10f; d += 10f)
                    if (!Seco(centro + a * s + o * d)) return false;
            return true;
        }
        List<int> Elegir(List<float> coord, Via[] vias, float desde, float hasta, Func<int, bool> valido)
        {
            var elegidos = new List<int>();
            foreach (int k in Enumerable.Range(0, coord.Count).Where(k => vias[k].Categoria >= CategoriaVia.Colectora)
                         .OrderByDescending(k => vias[k].Categoria).ThenBy(k => k))
            {
                if (coord[k] < desde + margen || coord[k] > hasta - margen || !valido(k)) continue;
                if (elegidos.Any(e => Mathf.Abs(coord[e] - coord[k]) < 2f * L + 60f)) continue;
                elegidos.Add(k);
            }
            elegidos.Sort();
            return elegidos;
        }
        var accesosX = Elegir(xs, viasX, x0, x1, i => nodos[i, 0] != null && nodos[i, ys.Count - 1] != null
            && ZonaSeca(new Vector2(xs[i], y0), Vector2.Right, Vector2.Up) && ZonaSeca(new Vector2(xs[i], y1), Vector2.Right, Vector2.Down));
        var accesosY = Elegir(ys, viasY, y0, y1, j => nodos[0, j] != null && nodos[xs.Count - 1, j] != null
            && ZonaSeca(new Vector2(x0, ys[j]), Vector2.Down, Vector2.Left) && ZonaSeca(new Vector2(x1, ys[j]), Vector2.Down, Vector2.Right));

        // Nodos de cada lado, en el sentido creciente de la coordenada: en cada enlace, uno antes (P) y otro después (Q) de la calle.
        var enlaces = new List<Enlace>();
        List<Nodo> Lado(Vector2 inicio, Vector2 fin, Vector2 a, Vector2 o, IEnumerable<(Vector2 centro, Nodo ciudad, Via via, bool haciaFuera, string nombre)> accesos)
        {
            var lado = new List<Nodo> { g.AgregarNodo(inicio) };
            foreach (var (centro, ciudad, via, haciaFuera, nombre) in accesos)
            {
                var pNodo = g.AgregarNodo(centro - a * L);
                var qNodo = g.AgregarNodo(centro + a * L);
                lado.Add(pNodo);
                lado.Add(qNodo);
                enlaces.Add(new Enlace(centro, a, o, pNodo, qNodo, ciudad, via, haciaFuera, nombre));
            }
            lado.Add(g.AgregarNodo(fin));
            return lado;
        }
        var arriba = Lado(new Vector2(x0 + R, y0), new Vector2(x1 - R, y0), Vector2.Right, Vector2.Up,
            accesosX.Select(i => (new Vector2(xs[i], y0), nodos[i, 0], viasX[i], false, $"V-0-{i + 1}")));
        var abajo = Lado(new Vector2(x0 + R, y1), new Vector2(x1 - R, y1), Vector2.Right, Vector2.Down,
            accesosX.Select(i => (new Vector2(xs[i], y1), nodos[i, ys.Count - 1], viasX[i], true, $"V-{ys.Count}-{i + 1}")));
        var izquierda = Lado(new Vector2(x0, y0 + R), new Vector2(x0, y1 - R), Vector2.Down, Vector2.Left,
            accesosY.Select(j => (new Vector2(x0, ys[j]), nodos[0, j], viasY[j], false, $"H-{j + 1}-0")));
        var derecha = Lado(new Vector2(x1, y0 + R), new Vector2(x1, y1 - R), Vector2.Down, Vector2.Right,
            accesosY.Select(j => (new Vector2(x1, ys[j]), nodos[xs.Count - 1, j], viasY[j], true, $"H-{j + 1}-{xs.Count}")));

        // Tramos rectos de la autovía.
        foreach (var lado in new[] { arriba, abajo, izquierda, derecha })
            for (int k = 0; k < lado.Count - 1; k++)
                g.AgregarTramo(lado[k], lado[k + 1], CategoriaVia.Autovia, 3, 3, Limite(CategoriaVia.Autovia));

        // Esquinas: arcos de circunferencia que empalman de forma suave con los lados.
        Arco(g, izquierda[0], arriba[0], new Vector2(x0 + R, y0 + R), R, Mathf.Pi, 1.5f * Mathf.Pi);                  // arriba-izquierda
        Arco(g, arriba[^1], derecha[0], new Vector2(x1 - R, y0 + R), R, -0.5f * Mathf.Pi, 0f);                        // arriba-derecha
        Arco(g, derecha[^1], abajo[^1], new Vector2(x1 - R, y1 - R), R, 0f, 0.5f * Mathf.Pi);                         // abajo-derecha
        Arco(g, abajo[0], izquierda[^1], new Vector2(x0 + R, y1 - R), R, 0.5f * Mathf.Pi, Mathf.Pi);                  // abajo-izquierda

        NombrarCircunvalacion(arriba, derecha, abajo, izquierda);
        int numero = 1;
        // Carreteras al exterior: en cada lado, uno o dos enlaces (dos si el lado mide más de 5 km) prolongan su calle hasta el borde del
        // mapa. Primero los de arterias y, entre ellos, los más centrados; solo si la carretera no pasa por el agua.
        var exteriores = new HashSet<Enlace>();
        foreach (var lado in enlaces.GroupBy(e => e.O))
        {
            var o = lado.Key;
            float largoLado = Mathf.Abs(o.X) > 0.5f ? y1 - y0 : x1 - x0;
            int cuantos = largoLado > 5000f ? 2 : 1;
            var centroLado = new Vector2((x0 + x1) / 2f, (y0 + y1) / 2f);
            foreach (var e in lado.Where(e => CarreteraSeca(e, g.Limites, rio)).OrderByDescending(e => e.Via.Categoria)
                         .ThenBy(e => (e.Centro - centroLado).Dot(e.A) * (e.Centro - centroLado).Dot(e.A)).Take(cuantos))
                exteriores.Add(e);
        }
        int puerta = 1;
        foreach (var e in enlaces) ConstruirEnlace(g, e, numero++, exteriores.Contains(e) ? puerta++ : 0);
    }

    // ---- Enlaces de la circunvalación ----

    private const float LargoEnlace = 150f;        // de la calle a cada uno de los dos puntos donde los ramales salen de la autovía o entran en ella
    private const float DistanciaTerminal = 45f;   // del eje de la autovía al cruce de los ramales con la calle (a cada lado)
    private const float DistanciaPaso = 16f;       // del eje de la autovía a cada boca del paso inferior
    private const float SeparacionRamal = 12.25f;  // del eje de la autovía al eje del ramal junto a ella (pegado a su carril derecho)
    private const float RectoRamal = 40f;          // metros del ramal paralelos a la autovía (carril de deceleración o de aceleración)
    private const float LimiteSalida = 60f, LimiteEntrada = 100f;

    /// <param name="Centro">Punto de la autovía por donde pasa la calle.</param>
    /// <param name="A">Dirección de la autovía en ese lado (hacia donde crece la coordenada).</param>
    /// <param name="O">Hacia fuera de la ciudad.</param>
    /// <param name="HaciaFuera">Si las calles de esa línea van (de A a B) hacia fuera de la ciudad.</param>
    private sealed record Enlace(Vector2 Centro, Vector2 A, Vector2 O, Nodo P, Nodo Q, Nodo Ciudad, Via Via, bool HaciaFuera, string Nombre);

    /// <summary>
    /// Enlace en diamante: la calle pasa por debajo de la autovía (la autovía no tiene ningún cruce a nivel) y cuatro ramales de un carril
    /// la unen con ella. Para cada sentido de la autovía, la salida se separa por la derecha en P (o Q) y lleva al cruce de la calle de
    /// ese lado; la entrada sale de ese cruce y se incorpora por la derecha en Q (o P), corriendo antes unos metros junto al carril derecho.
    /// En P y Q solo se puede seguir por la autovía, salir por el ramal de ese sentido o incorporarse desde él: el resto de movimientos
    /// están prohibidos. Los que se incorporan ceden el paso. Los cruces de los ramales con la calle llevan semáforo si la calle es una
    /// arteria o una carretera de acceso; si no, los ramales ceden el paso.
    /// </summary>
    /// <summary>Velocidad de las carreteras de acceso a la ciudad.</summary>
    private const float LimiteCarretera = 80f;

    /// <summary>Del cruce exterior del enlace al borde del mapa, sin agua en el camino.</summary>
    private static bool CarreteraSeca(Enlace e, Rect2 limites, Rio? rio)
    {
        if (rio == null) return true;
        var desde = e.Centro + e.O * DistanciaTerminal;
        var hasta = AlBorde(desde, e.O, limites);
        for (float t = 0f; t <= 1f; t += 0.05f)
            if (rio.Distancia(desde.Lerp(hasta, t), rio.Ancho / 2f + 20f) < rio.Ancho / 2f + 15f) return false;
        return true;
    }

    /// <summary>Punto del borde del mapa al que se llega desde <paramref name="p"/> yendo en la dirección <paramref name="o"/> (horizontal o vertical).</summary>
    private static Vector2 AlBorde(Vector2 p, Vector2 o, Rect2 limites) =>
        o.X > 0.5f ? new Vector2(limites.End.X, p.Y) : o.X < -0.5f ? new Vector2(limites.Position.X, p.Y)
        : o.Y > 0.5f ? new Vector2(p.X, limites.End.Y) : new Vector2(p.X, limites.Position.Y);

    /// <param name="puerta">Si es mayor que 0, la calle sigue hacia fuera hasta el borde del mapa: es la carretera de acceso número <paramref name="puerta"/>.</param>
    private static void ConstruirEnlace(Grafo g, Enlace e, int numero, int puerta)
    {
        var (c, a, o) = (e.Centro, e.A, e.O);
        var terminalDentro = g.AgregarNodo(c - o * DistanciaTerminal);
        var terminalFuera = g.AgregarNodo(c + o * DistanciaTerminal);
        var pasoDentro = g.AgregarNodo(c - o * DistanciaPaso);
        var pasoFuera = g.AgregarNodo(c + o * DistanciaPaso);
        terminalDentro.Nombre = $"E-{numero}-1";
        terminalFuera.Nombre = $"E-{numero}-2";

        // La calle: de la ciudad al cruce interior, el paso inferior (túnel) y el cruce exterior. Siempre en el sentido de la línea.
        Tramo Calle(Nodo desdeCiudad, Nodo haciaFuera, string nombre)
        {
            var t = e.HaciaFuera ? AgregarCalle(g, desdeCiudad, haciaFuera, e.Via) : AgregarCalle(g, haciaFuera, desdeCiudad, e.Via);
            t.Nombre = nombre;
            return t;
        }
        Calle(e.Ciudad, terminalDentro, e.Nombre);
        Calle(terminalDentro, pasoDentro, $"{e.Nombre}-1");
        Calle(pasoDentro, pasoFuera, $"{e.Nombre}-2").EsTunel = true;
        Calle(pasoFuera, terminalFuera, $"{e.Nombre}-3");
        if (puerta > 0)
        {
            // Carretera de acceso, de 2 + 2 carriles, desde el cruce exterior del enlace hasta el borde del mapa (de A hacia fuera).
            var borde = g.AgregarNodo(AlBorde(terminalFuera.Pos, o, g.Limites));
            borde.Nombre = $"PX-{puerta}";
            g.AgregarTramo(terminalFuera, borde, CategoriaVia.Arteria, 2, 2, LimiteCarretera).Nombre = $"EXT-{puerta}";
        }

        // Ramales de cada sentido de la autovía. Se circula por la derecha: el ramal queda a la derecha del sentido de marcha.
        var ramales = new List<(Tramo salida, Tramo entrada, Vector2 d)>();
        foreach (var d in new[] { a, -a })
        {
            var derecha = new Vector2(-d.Y, d.X);
            var terminal = derecha.Dot(o) < 0f ? terminalDentro : terminalFuera;
            var (antes, despues) = d == a ? (e.P, e.Q) : (e.Q, e.P);

            var inicio = antes.Pos + derecha * SeparacionRamal;
            var puntosSalida = new List<Vector2> { antes.Pos, inicio, inicio + d * RectoRamal };
            puntosSalida.AddRange(Curva(inicio + d * RectoRamal, terminal.Pos, d).Skip(1));
            var salida = g.AgregarTramo(antes, terminal, CategoriaVia.Arteria, 1, 0, LimiteSalida, new Trayectoria(puntosSalida.ToArray()));

            var fin = despues.Pos + derecha * SeparacionRamal;
            var puntosEntrada = Curva(terminal.Pos, fin - d * RectoRamal, d).ToList();
            puntosEntrada.Add(fin);
            puntosEntrada.Add(despues.Pos);
            var entrada = g.AgregarTramo(terminal, despues, CategoriaVia.Arteria, 1, 0, LimiteEntrada, new Trayectoria(puntosEntrada.ToArray()));

            salida.EsRamal = entrada.EsRamal = true;
            string sentido = d == a ? "1" : "2";
            salida.Nombre = $"E-{numero}-salida-{sentido}";
            entrada.Nombre = $"E-{numero}-entrada-{sentido}";
            ramales.Add((salida, entrada, d));
        }

        // En P y Q: la autovía tiene prioridad y solo se permiten los movimientos del propio sentido.
        foreach (var nodo in new[] { e.P, e.Q })
        {
            var autovia = nodo.Tramos.Where(t => t.Categoria == CategoriaVia.Autovia).ToList();
            var otro = autovia.First(t => t.A == (nodo == e.P ? e.Q : e.P) || t.B == (nodo == e.P ? e.Q : e.P));   // el tramo bajo el que pasa la calle
            var exterior = autovia.First(t => t != otro);
            // En P circula hacia Q el sentido +A (llega por el tramo exterior); en Q, el sentido -A.
            var (salidaAqui, _, _) = ramales.First(r => r.salida.A == nodo);
            var (_, entradaAqui, _) = ramales.First(r => r.entrada.B == nodo);
            var permitidos = new HashSet<(Tramo, Tramo)> { (exterior, otro), (otro, exterior), (exterior, salidaAqui), (entradaAqui, exterior) };
            foreach (var desde in new[] { exterior, otro, entradaAqui })
                foreach (var hasta in new[] { exterior, otro, salidaAqui })
                    if (desde != hasta && !permitidos.Contains((desde, hasta))) nodo.GirosProhibidos.Add((desde.Id, hasta.Id));
            nodo.Control = TipoControl.Ceda;
            foreach (var t in autovia) nodo.Prioritarios.Add(t);
        }

        // En los cruces con la calle, los ramales ceden el paso y no se puede pasar de la salida a la entrada (volver a la autovía).
        // En arterias y en las carreteras de acceso hay mucho tráfico: semáforos (como en los enlaces reales con mucho tráfico); si no, ceda.
        bool conSemaforo = e.Via.Categoria >= CategoriaVia.Arteria || puerta > 0;
        foreach (var terminal in new[] { terminalDentro, terminalFuera })
        {
            terminal.Control = conSemaforo ? TipoControl.Semaforo : TipoControl.Ceda;
            if (conSemaforo) terminal.Semaforo = new Semaforo(30f, 20f, numero * 17f % 90f);
            foreach (var t in terminal.Tramos.Where(t => !t.EsRamal)) terminal.Prioritarios.Add(t);
            foreach (var salida in terminal.Tramos.Where(t => t.EsRamal && t.B == terminal))
                foreach (var entrada in terminal.Tramos.Where(t => t.EsRamal && t.A == terminal))
                    terminal.GirosProhibidos.Add((salida.Id, entrada.Id));
        }
    }

    /// <summary>Curva suave (Bézier) de <paramref name="desde"/> a <paramref name="hasta"/>, que sale y llega con la dirección <paramref name="d"/>.</summary>
    private static IEnumerable<Vector2> Curva(Vector2 desde, Vector2 hasta, Vector2 d)
    {
        float k = 0.45f * desde.DistanceTo(hasta);
        Vector2 p1 = desde + d * k, p2 = hasta - d * k;
        const int n = 24;
        for (int i = 0; i <= n; i++)
        {
            float t = i / (float)n, u = 1f - t;
            yield return u * u * u * desde + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * hasta;
        }
    }

    /// <summary>Numera los tramos (C-n) y los nodos (CX-n) de la circunvalación en sentido horario desde la esquina de arriba a la izquierda.</summary>
    private static void NombrarCircunvalacion(List<Nodo> arriba, List<Nodo> derecha, List<Nodo> abajo, List<Nodo> izquierda)
    {
        int t = 1, x = 1;
        static Tramo? Entre(Nodo a, Nodo b) => a.Tramos.FirstOrDefault(c => (c.A == a && c.B == b) || (c.A == b && c.B == a));
        void Tramo(Nodo a, Nodo b)
        {
            var tramo = Entre(a, b);
            if (tramo != null) tramo.Nombre = $"C-{t++}";
        }

        for (int k = 0; k < arriba.Count - 1; k++) Tramo(arriba[k], arriba[k + 1]);
        Tramo(arriba[^1], derecha[0]);                                           // esquina arriba-derecha
        for (int k = 0; k < derecha.Count - 1; k++) Tramo(derecha[k], derecha[k + 1]);
        Tramo(derecha[^1], abajo[^1]);                                           // esquina abajo-derecha
        for (int k = abajo.Count - 1; k > 0; k--) Tramo(abajo[k], abajo[k - 1]);
        Tramo(abajo[0], izquierda[^1]);                                          // esquina abajo-izquierda
        for (int k = izquierda.Count - 1; k > 0; k--) Tramo(izquierda[k], izquierda[k - 1]);
        Tramo(izquierda[0], arriba[0]);                                          // esquina arriba-izquierda

        foreach (var n in arriba) n.Nombre = $"CX-{x++}";
        foreach (var n in derecha) n.Nombre = $"CX-{x++}";
        for (int k = abajo.Count - 1; k >= 0; k--) abajo[k].Nombre = $"CX-{x++}";
        for (int k = izquierda.Count - 1; k >= 0; k--) izquierda[k].Nombre = $"CX-{x++}";
    }

    private static void Arco(Grafo g, Nodo a, Nodo b, Vector2 centro, float radio, float anguloInicial, float anguloFinal)
    {
        var arco = Trayectoria.Arco(centro, radio, anguloInicial, anguloFinal);
        var pts = arco.Puntos;
        pts[0] = a.Pos;      // los extremos coinciden exactamente con los nodos
        pts[^1] = b.Pos;
        g.AgregarTramo(a, b, CategoriaVia.Autovia, 3, 3, Limite(CategoriaVia.Autovia), new Trayectoria(pts));
    }
}

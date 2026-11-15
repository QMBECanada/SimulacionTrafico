using System.Collections.Generic;
using System.Linq;
using Godot;
using SimulacionTrafico.Camara;
using SimulacionTrafico.Mapa;
using SimulacionTrafico.Nucleo;
using SimulacionTrafico.Vehiculos;

namespace SimulacionTrafico;

/// <summary>
/// Informes por consola para depurar y ajustar el modelo (opciones <c>--diagnostico</c> y <c>--bench</c>). No forman parte de los
/// datos del estudio: los datos son solo los ficheros TSV en bruto.
/// </summary>
public static class Diagnostico
{
    /// <summary>Al terminar una simulación sin pantalla con <c>--diagnostico</c>: medias, frenadas, arranques, rotondas, esperas...</summary>
    public static void InformeFinal(Simulacion sim, Grafo grafo)
    {
        long totalEm = sim.EstadosMarcha.Values.Sum();
        foreach (var (em, n) in sim.EstadosMarcha.OrderByDescending(k => k.Value))
            GD.Print($"  MARCHA {em}: {100.0 * n / System.Math.Max(1, totalEm):0.0} %");
        long totalFrenadas = sim.Frenadas.Values.Sum();
        float anterior = 0f;
        foreach (var (tope, n) in sim.Frenadas)
        {
            GD.Print($"  FRENADA {anterior:0.0}-{tope:0.0} m/s²: {100.0 * n / System.Math.Max(1, totalFrenadas):0.00} % de los pasos frenando");
            anterior = tope;
        }
        // Solo las colas de verdad: al arrancar, el de delante aún está cerca (si no, esperaba por otra cosa: un ceda, un stop...).
        var ra = sim.ArranquesEnCola.Where(x => x.hueco < 12f).Select(x => x.retraso).OrderBy(x => x).ToList();
        if (ra.Count > 0) GD.Print($"  ARRANQUE EN COLA: {ra.Count} casos, el de detrás arranca {ra.Average():0.00} s después del de delante (mediana {ra[ra.Count / 2]:0.00}, 10 % {ra[ra.Count / 10]:0.00}, 90 % {ra[ra.Count * 9 / 10]:0.00})");
        var vl = sim.VelocidadSobreLimite.OrderBy(x => x).ToList();
        if (vl.Count > 0)
            GD.Print($"  VELOCIDAD/LÍMITE (crucero sin estorbos, {vl.Count} muestras): mínima {vl[0]:0.00}, 5 % {vl[vl.Count / 20]:0.00}, mediana {vl[vl.Count / 2]:0.00}, 95 % {vl[vl.Count * 19 / 20]:0.00}, máxima {vl[^1]:0.00}");
        foreach (var (tras, lista) in sim.PerfilArranque.OrderBy(k => k.Key))
            if (lista.Count > 0) GD.Print($"  ARRANQUE a los {tras} s: {lista.Average():0.0} km/h de media ({lista.Count} coches)");
        var enRotonda = sim.Coches.Where(k => (k.Via is Carril rl && rl.Tramo.EsRotonda) || (k.Via is Conexion rc && (rc.Origen.Tramo.EsRotonda || rc.Destino.Tramo.EsRotonda))).ToList();
        if (enRotonda.Count > 0)
            GD.Print($"  ROTONDAS: {enRotonda.Count} coches dentro o entrando/saliendo, velocidad media {enRotonda.Average(k => k.V) * 3.6:0.0} km/h (máxima {enRotonda.Max(k => k.V) * 3.6:0.0}), parados {enRotonda.Count(k => k.V < 0.5f)}");
        // Velocidad de los coches con la calle libre delante, por categoría de calle (para comprobar que llegan al límite).
        foreach (var g in sim.Coches.Where(k => k.Via is Carril lc && k.Hueco > 60f && k.S > 50f && lc.Longitud - k.S > 70f)
                     .GroupBy(k => ((Carril)k.Via).Tramo.Categoria + " " + ((Carril)k.Via).Tramo.LimiteKmh + "km/h " + (Grafo.CarrilesEnSentido((Carril)k.Via) > 1 ? "multicarril" : "un carril")).OrderBy(g => g.Key))
            GD.Print($"  LIBRES {g.Key}: {g.Count()} coches, velocidad media {g.Average(k => k.V) * 3.6:0.0} km/h (máxima {g.Max(k => k.V) * 3.6:0.0})");
        GD.Print($"  viaje medio {(sim.Llegadas > 0 ? sim.TiempoViajeTotal / sim.Llegadas / 60.0 : 0):0.0} min y {(sim.Llegadas > 0 ? sim.DistanciaViajeTotal / sim.Llegadas : 0):0} m, " +
                 $"planificados en línea recta {(sim.Planificados > 0 ? sim.DistanciaPlanificadaTotal / sim.Planificados : 0):0} m, " +
                 $"rutas {sim.Enrutador.Calculos} ({sim.Enrutador.MsTotales / Mathf.Max(1, sim.Enrutador.Calculos):0.00} ms), sin ruta {sim.SinRuta}, recalculadas {sim.ReRutas}, " +
                 $"cambios de carril {sim.Flota.Sum(k => k.CambiosCarril)}, velocidad media {VelocidadMedia(sim):0.0} km/h, esperaMax {sim.Coches.Select(k => k.Espera).DefaultIfEmpty(0f).Max():0} s, " +
                 $"parados {sim.Coches.Count(k => k.V < 0.5f)}, espera>60 s: {sim.Coches.Count(k => k.Espera > 60f)}, parados más de 5 min: {sim.Coches.Count(k => k.TiempoParado > 300f)}");
        int enRotondas = sim.Coches.Count(k => k.Via is Carril rc && rc.Tramo.EsRotonda);
        GD.Print($"  ROTONDAS: {grafo.Rotondas.Count} en la ciudad, {enRotondas} coches circulando por sus anillos ahora, " +
                 $"{sim.Coches.Count(k => k.Via is Carril rc2 && rc2.Tramo.EsRotonda && k.V < 0.5f)} de ellos parados");
        Ciclos(sim, 3);
        foreach (var rot in grafo.Rotondas.Where(r => r.Tramos.Sum(t => t.Carriles.Sum(l => l.Coches.Count(k => k.V < 0.5f))) >= 3).Take(2))
        {
            GD.Print($"  ANILLO {rot.Nombre}: ocupacion {rot.Ocupacion():0.00}");
            foreach (var t in rot.Tramos)
                foreach (var l in t.Carriles)
                {
                    GD.Print($"    {t.Nombre} (carril {l.Id}, largo {l.Longitud:0.0} m, {l.Origen.Nombre}->{l.Destino.Nombre}):");
                    foreach (var k in l.Coches)
                        GD.Print($"       coche {k.Id} s={k.S:0.0} v={k.V:0.0} motivo={k.Motivo} hueco={k.Hueco:0.0} -> {(k.Bloqueador == null ? "ninguno" : $"{k.Bloqueador.Id} ({(k.BloqueadorEsLider ? "lider" : "cruce")}) en {(k.Bloqueador.Via is Conexion ? "caja" : "carril " + (k.Bloqueador.Via as Carril)?.Id)}")}");
                }
            foreach (var n in rot.Tramos.SelectMany(t => new[] { t.A, t.B }).Distinct())
                foreach (var cx in n.Conexiones.Where(c => c.Coches.Count > 0))
                    GD.Print($"    caja {n.Nombre}: {cx.Origen.Tramo.Nombre}->{cx.Destino.Tramo.Nombre} coches {string.Join(",", cx.Coches.Select(k => $"{k.Id}(v={k.V:0.0},s={k.S:0.0}/{cx.Longitud:0.0},{k.Motivo})"))}");
        }
        var iv = sim.Guardias.Historial;
        GD.Print($"  GUARDIAS: {sim.Guardias.Guardias.Count} en la ciudad, {iv.Count} intervenciones de grupo terminadas (media de {(iv.Count > 0 ? iv.Average(x => x.Guardias) : 0):0.0} guardias y {(iv.Count > 0 ? iv.Average(x => x.Cruces) : 0):0.0} cruces) y {sim.Guardias.Actuando} guardias actuando, " +
                 $"turnos {iv.Sum(x => x.Turnos)} (forzados {iv.Sum(x => x.TurnosForzados)}), vueltas en U {iv.Sum(x => x.Vueltas)}, zonas en curso {sim.Guardias.Zonas.Count(z => z.Actuando)}, " +
                 $"duración media {(iv.Count > 0 ? iv.Average(x => x.Fin - x.Inicio) / 60.0 : 0):0.0} min");
        GD.Print($"  CIRCULOS DE ESPERA deshechos por iniciativa: {sim.CirculosDeshechos}");
        GD.Print($"  ATASCOS: {sim.Atascos.Historial.Count} registrados, {sim.Atascos.NumActivos} activos ahora, " +
                 $"rutas recalculadas por atasco {sim.Enrutador.ReRutasPorAtasco}");
        foreach (var k in sim.Coches.Where(k => k.Espera > 60f).OrderByDescending(k => k.Espera).Take(6))
        {
            var lane = k.Via as Carril; var b = k.Bloqueador;
            GD.Print($"  ESPERA LARGA: coche {k.Id} espera={k.Espera:0}s en {(k.Via is Conexion ? "CAJA" : "carril " + lane?.Id)} nodo={lane?.Destino.Id} control={lane?.Destino.Control} " +
                     $"motivo={k.Motivo} v={k.V:0.0} hueco={k.Hueco:0.0} destino={k.Destino?.Nombre} pendientes={k.CallesPendientes} " +
                     $"-> {(b == null ? "ninguno" : $"{b.Id} ({(k.BloqueadorEsLider ? "lider" : "cruce")}) v={b.V:0.0} motivo={b.Motivo} espera={b.Espera:0} {(b.Via is Conexion ? "EN CAJA" : "")} circulando={b.Circulando}")}");
        }
    }

    /// <summary>Al acabar la medida de <c>--bench</c>: fotogramas por segundo y un resumen del estado del tráfico.</summary>
    public static void InformeBench(Simulacion sim, Grafo grafo, CamaraCiudad camara, double segundos, int fotogramas)
    {
        GD.Print($"BENCH coches={sim.Coches.Count} zoom={camara.PixelesPorMetro:0.000} fps={fotogramas / segundos:0.0} " +
                 $"ms/fotograma={1000.0 * segundos / fotogramas:0.00} velMedia={VelocidadMedia(sim):0.0}km/h " +
                 $"parados={sim.Coches.Count(c => c.V < 0.5f)} esperandoEnCruce={sim.Coches.Count(c => c.Motivo != null && c.V < 0.5f)} " +
                 $"esperaMax={sim.Coches.Select(c => c.Espera).DefaultIfEmpty(0f).Max():0}s espera>60s={sim.Coches.Count(c => c.Espera > 60f)} " +
                 $"huecoMenor1m={sim.Coches.Count(c => c.Hueco < 1f)} " +
                 $"tiempoSim={Hora(sim.TiempoSimulado)} msPaso={sim.MsPorPaso:0.0} msPasoMax={sim.MsPorPasoMaximo:0.0}");
        double horas = sim.TiempoSimulado / 3600.0;
        GD.Print($"  MISIONES: {sim.Llegadas} llegadas ({sim.Llegadas / (double)Mathf.Max(1, sim.Flota.Count) / Mathf.Max(horas, 1e-6):0.0} por coche y hora), " +
                 $"viaje medio {(sim.Llegadas > 0 ? sim.TiempoViajeTotal / sim.Llegadas / 60.0 : 0):0.0} min y " +
                 $"{(sim.Llegadas > 0 ? sim.DistanciaViajeTotal / sim.Llegadas : 0):0} m");
        GD.Print($"  VIAJES PLANIFICADOS: {sim.Planificados}, distancia media en línea recta {(sim.Planificados > 0 ? sim.DistanciaPlanificadaTotal / sim.Planificados : 0):0} m " +
                 $"({100 * (sim.Planificados > 0 ? sim.DistanciaPlanificadaTotal / sim.Planificados : 0) / grafo.Limites.Size.X:0} % del ancho de la ciudad)");
        GD.Print($"  COCHES: {sim.Coches.Count} circulando, {sim.Flota.Count - sim.Coches.Count} dentro de un lugar ({100.0 * sim.Coches.Count / sim.Flota.Count:0} % circulando)");
        GD.Print($"  RUTAS: {sim.Enrutador.Calculos} calculadas ({sim.Enrutador.MsTotales / Mathf.Max(1, sim.Enrutador.Calculos):0.00} ms y {sim.Enrutador.Expansiones / Mathf.Max(1, sim.Enrutador.Calculos)} calles exploradas cada una), " +
                 $"sin ruta: {sim.SinRuta}, recalculadas por el camino: {sim.ReRutas} ({100.0 * sim.ReRutas / Mathf.Max(1, sim.Llegadas):0} % de los viajes)");
        int cambios = sim.Flota.Sum(k => k.CambiosCarril), obligados = sim.Flota.Sum(k => k.CambiosObligados);
        double minutos = sim.TiempoSimulado / 60.0;
        GD.Print($"  CAMBIOS DE CARRIL: {cambios} ({cambios / (double)sim.Flota.Count / minutos:0.00} por coche y minuto), " +
                 $"obligados por el giro: {obligados} ({(cambios > 0 ? 100.0 * obligados / cambios : 0):0} %)");
        var enAutovia = sim.Coches.Where(k => k.Via is Carril l && l.Tramo.Categoria == CategoriaVia.Autovia).ToList();
        GD.Print($"  AUTOVIA: {enAutovia.Count} coches ({100f * enAutovia.Count / Mathf.Max(1, sim.Coches.Count):0.0} %), velocidad media " +
                 $"{(enAutovia.Count > 0 ? enAutovia.Average(k => k.V) * Escala.MsAKmh : 0):0} km/h, parados {enAutovia.Count(k => k.V < 0.5f)}");
        // Distancia al coche de delante por tipo de conductor: parados (m) y en marcha (s de separación).
        foreach (var g in sim.Coches.Where(k => k.BloqueadorEsLider && k.Hueco < 40f).GroupBy(k => k.Conductor.Tipo).OrderBy(g => g.Key))
        {
            var parados = g.Where(k => k.V < 0.2f).Select(k => k.Hueco).ToList();
            var marcha = g.Where(k => k.V > 3f && k.V > 0.85f * k.VelocidadDeseadaActual).Select(k => k.Hueco / k.V).ToList();
            GD.Print($"  SEPARACION {g.Key,-9} parados: n={parados.Count,4} media={(parados.Count > 0 ? parados.Average() : 0):0.0} m   " +
                     $"en marcha: n={marcha.Count,4} media={(marcha.Count > 0 ? marcha.Average() : 0):0.00} s");
        }
        var bloqueados = sim.Coches.Where(k => k.Espera > 60f || (k.V < 0.5f && k.Bloqueador != null)).ToList();
        foreach (var g in bloqueados.GroupBy(k => (k.Motivo ?? "-", (k.Via as Carril)?.Destino.Control.ToString() ?? "-")))
            GD.Print($"  BLOQUEADOS {g.Count()} motivo='{g.Key.Item1}' cruce={g.Key.Item2}");
        // Cadenas de "espera a": busca ciclos (A espera a B, que espera a ... A).
        foreach (var k in sim.Coches.Where(k => k.Espera > 60f).OrderByDescending(k => k.Espera).Take(8))
        {
            var lane = k.Via as Carril; var m = k.Plan.Count > 1 ? k.Plan[1] as Conexion : null; var b = k.Bloqueador;
            GD.Print($"  LARGA: coche {k.Id} espera={k.Espera:0}s carril={lane?.Id} nodo={lane?.Destino.Id} control={lane?.Destino.Control} giro={m?.Giro} " +
                     $"motivo={k.Motivo} v={k.V:0.0} hueco={k.Hueco:0.0} tipo={k.Conductor.Tipo} -> {(b == null ? "ninguno" : $"{b.Id} ({(k.BloqueadorEsLider ? "lider" : "cruce")}) v={b.V:0.0} motivo={b.Motivo} espera={b.Espera:0} {(b.Via is Conexion ? "EN CAJA" : "")}")}");
        }
        int ciclos = 0;
        foreach (var k in bloqueados)
        {
            var visto = new List<Coche>();
            var cur = k;
            while (cur != null && !visto.Contains(cur) && visto.Count < 40)
            {
                visto.Add(cur);
                cur = cur.Bloqueador;
            }
            if (cur != null && visto.Contains(cur))
            {
                if (++ciclos <= 3)
                {
                    GD.Print("  CICLO:");
                    foreach (var x in visto.SkipWhile(x => x != cur))
                    {
                        var lane = x.Via as Carril;
                        var m = x.Plan.Count > 1 ? x.Plan[1] as Conexion : null;
                        GD.Print($"    coche {x.Id} {(x.Via is Conexion ? "EN CAJA" : "carril " + lane?.Id)} nodo={(lane?.Destino.Id)} control={(lane?.Destino.Control)} " +
                                 $"prioritario={(lane != null && lane.Destino.EsPrioritario(lane.Tramo))} giro={m?.Giro} rango={(m != null && lane != null ? lane.Destino.Rango(m) : -1)} " +
                                 $"v={x.V:0.0} motivo={x.Motivo} espera={x.Espera:0} -> espera a {x.Bloqueador?.Id} ({(x.BloqueadorEsLider ? "lider" : "cruce")})");
                    }
                }
            }
        }
        GD.Print($"  coches en ciclos de espera: {ciclos}");
        foreach (var k in bloqueados.Take(5))
        {
            var b = k.Bloqueador;
            GD.Print($"  ej: coche {k.Id} nodo={(k.Via as Carril)?.Destino.Id} espera={k.Espera:0}s motivo={k.Motivo} hueco={k.Hueco:0.0} " +
                     $"bloqueador={(b == null ? "ninguno" : $"{b.Id} v={b.V:0.0} motivo={b.Motivo} en {(b.Via is Conexion ? "caja" : "carril")}")}");
        }
    }

    /// <summary>Depuración: busca ciclos de espera (A espera a B, que espera a ... A) entre los coches parados e imprime algunos.</summary>
    private static void Ciclos(Simulacion sim, int maximo)
    {
        int impresos = 0;
        var yaVistos = new HashSet<Coche>();
        foreach (var inicio in sim.Coches.Where(k => k.TiempoParado > 300f))
        {
            var visto = new List<Coche>();
            var cur = inicio;
            while (cur != null && !visto.Contains(cur) && visto.Count < 60)
            {
                visto.Add(cur);
                cur = cur.Bloqueador;
            }
            if (cur == null || !visto.Contains(cur) || yaVistos.Contains(cur)) continue;
            var ciclo = visto.SkipWhile(x => x != cur).ToList();
            foreach (var x in ciclo) yaVistos.Add(x);
            if (impresos++ >= maximo) break;
            GD.Print($"  CICLO de {ciclo.Count} coches:");
            foreach (var x in ciclo)
            {
                var lane = x.Via as Carril;
                var m = x.Plan.Count > 1 ? x.Plan[1] as Conexion : null;
                GD.Print($"    coche {x.Id} {(x.Via is Conexion cx0 ? $"EN CAJA {cx0.Origen.Destino.Nombre}: {cx0.Origen.Tramo.Nombre}[{cx0.Origen.Indice}]->{cx0.Destino.Tramo.Nombre}[{cx0.Destino.Indice}] s={x.S:0.0}/{cx0.Longitud:0.0}" : "carril " + lane?.Id + " tramo " + lane?.Tramo.Nombre)} " +
                         $"nodo={lane?.Destino.Nombre} control={lane?.Destino.Control} giro={m?.Giro} motivo={x.Motivo} v={x.V:0.0} " +
                         $"-> espera a {x.Bloqueador?.Id} ({(x.BloqueadorEsLider ? "lider" : "cruce")})");
            }
        }
    }

    private static float VelocidadMedia(Simulacion sim) =>
        sim.Coches.Count == 0 ? 0f : sim.Coches.Average(c => c.V) * Escala.MsAKmh;

    private static string Hora(double s) => $"{(int)(s / 60):00}:{(int)(s % 60):00}";

    /// <summary>
    /// Comprueba que hay ruta entre lugares (<c>--probar-rutas</c>): desde cada lugar a otros 20 al azar. Imprime cuántas fallan y los
    /// primeros casos.
    /// </summary>
    public static void ProbarRutas(Grafo grafo)
    {
        var azar = new Aleatorio(12345);
        var enrutador = new Enrutador(grafo);
        var conductor = new Conductor(azar, new ContextoSimulacion(new ConfiguracionSimulacion()));
        int pruebas = 0, fallos = 0;
        var origenesMalos = new Dictionary<Lugar, int>();
        var destinosMalos = new Dictionary<Lugar, int>();
        foreach (var o in grafo.Lugares)
            for (int k = 0; k < 20; k++)
            {
                var d = grafo.Lugares[azar.RandiRange(0, grafo.Lugares.Count - 1)];
                if (d == o) continue;
                pruebas++;
                enrutador.NuevoPaso();
                var ruta = enrutador.Calcular(o.Salida.Tramo, o.Salida.Ida, o.SSalida / o.Salida.Longitud, d, conductor);
                if (ruta != null) continue;
                fallos++;
                origenesMalos[o] = origenesMalos.GetValueOrDefault(o) + 1;
                destinosMalos[d] = destinosMalos.GetValueOrDefault(d) + 1;
            }
        GD.Print($"RUTAS: {pruebas} pruebas, {fallos} sin ruta ({100.0 * fallos / System.Math.Max(1, pruebas):0.00} %)");
        foreach (var (l, n) in origenesMalos.OrderByDescending(x => x.Value).Take(6))
            GD.Print($"  origen {l.Nombre} ({l.Tipo}) acceso {l.Acceso.Tramo.Nombre} ida={l.Acceso.Ida}: {n} fallos");
        foreach (var (l, n) in destinosMalos.OrderByDescending(x => x.Value).Take(6))
            GD.Print($"  destino {l.Nombre} ({l.Tipo}) acceso {l.Acceso.Tramo.Nombre} ida={l.Acceso.Ida}: {n} fallos");
    }
}

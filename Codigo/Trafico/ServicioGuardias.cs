using System.Collections.Generic;
using System.Linq;
using Godot;
using SimulacionTrafico.Datos;
using SimulacionTrafico.Mapa;
using SimulacionTrafico.Nucleo;

namespace SimulacionTrafico.Trafico;

/// <summary>
/// Grupo de guardias que actúa a la vez sobre una zona atascada (varios cruces cercanos). Empiezan todos juntos cuando
/// han llegado (o ha pasado un tiempo), cambian de turno a la vez y se van todos juntos cuando la zona está libre.
/// </summary>
public sealed class Zona
{
    public const float PulsoS = 12f;

    private const double MinimoActuacion = 120.0;
    private const double SinAtascoParaIrse = 45.0;
    private const double ColaMediaResuelta = 1.5;   // coches en cola por cruce a partir de los cuales hay atasco
    private const double Maximo = 1800.0;
    private const double EsperaAlResto = 90.0;

    public int Id { get; }
    public List<Nodo> Nodos { get; } = new();
    public List<Guardia> Guardias { get; } = new();
    public double Creada { get; }
    public double? Inicio { get; private set; }
    public bool Actuando => Inicio != null;

    private readonly Dictionary<Nodo, int> _profundidad = new();
    private double _sinAtasco = -1;
    private double? _primeraLlegada;

    public Zona(int id, double creada)
    {
        Id = id;
        Creada = creada;
    }

    /// <summary>Distancia, en cruces, hasta salir de la zona (0 = fuera de ella).</summary>
    public int Profundidad(Nodo n) => _profundidad.TryGetValue(n, out int d) ? d : 0;

    /// <summary>Recalcula la profundidad de cada cruce de la zona: cuántos cruces hay que atravesar para salir de ella.</summary>
    public void CalcularProfundidad()
    {
        _profundidad.Clear();
        var dentro = new HashSet<Nodo>(Nodos);
        var cola = new Queue<Nodo>();
        var visto = new HashSet<Nodo>();

        // Los cruces vecinos de la zona, que están fuera, valen 0.
        foreach (var n in Nodos)
            foreach (var t in n.Tramos)
            {
                var otro = t.A == n ? t.B : t.A;
                if (!dentro.Contains(otro) && visto.Add(otro)) cola.Enqueue(otro);
            }

        var distancia = new Dictionary<Nodo, int>();
        foreach (var n in cola) distancia[n] = 0;
        while (cola.Count > 0)
        {
            var n = cola.Dequeue();
            foreach (var t in n.Tramos)
            {
                var otro = t.A == n ? t.B : t.A;
                if (!dentro.Contains(otro) || distancia.ContainsKey(otro)) continue;
                distancia[otro] = distancia[n] + 1;
                cola.Enqueue(otro);
            }
        }
        foreach (var n in Nodos) _profundidad[n] = distancia.TryGetValue(n, out int d) ? d : 1;
    }

    /// <summary>Se llama cada paso. Decide cuándo empiezan todos juntos y cuándo se van.</summary>
    public bool Actualizar(double ahora, DetectorAtascos detector)
    {
        if (!Actuando)
        {
            // Se cuenta sin crear listas: esto se mira en cada paso mientras llegan los guardias.
            int esperando = 0;
            foreach (var g in Guardias)
                if (g.Estado == EstadoGuardia.Esperando) esperando++;
            if (esperando > 0) _primeraLlegada ??= ahora;
            bool todos = esperando == Guardias.Count;
            bool tarde = _primeraLlegada != null && ahora - _primeraLlegada.Value >= EsperaAlResto;
            if (esperando > 0 && (todos || tarde))
            {
                Inicio = ahora;
                foreach (var g in Guardias)
                    if (g.Estado == EstadoGuardia.Esperando) g.Empezar(ahora);   // Empezar solo cambia el estado de ese guardia
            }
            return false;
        }

        // Los que llegan después se incorporan al grupo ya en marcha.
        foreach (var g in Guardias)
            if (g.Estado == EstadoGuardia.Esperando) g.Empezar(ahora);

        foreach (var g in Guardias)
            if (g.Estado == EstadoGuardia.Actuando) g.Actualizar(ahora);

        // La zona se da por resuelta cuando las colas ante sus cruces se han vaciado desde hace un rato. No se mira el
        // detector de atascos: el propio control de los guardias (una aproximación cada vez) crea colas lentas.
        bool atasco = Nodos.Sum(Guardia.ColaTotal) > Nodos.Count * ColaMediaResuelta;
        if (atasco) _sinAtasco = -1;
        else if (_sinAtasco < 0) _sinAtasco = ahora;

        bool resuelta = _sinAtasco >= 0 && ahora - _sinAtasco >= SinAtascoParaIrse && ahora - Inicio!.Value >= MinimoActuacion;
        return resuelta || ahora - Inicio!.Value >= Maximo;
    }
}

/// <summary>Una vez que un grupo de guardias ha atendido una zona, de principio a fin.</summary>
public sealed class Intervencion
{
    public int Zona { get; init; }
    public int Guardias { get; init; }
    public int Cruces { get; init; }
    public double Inicio { get; init; }
    public double Fin { get; set; }
    public int Turnos { get; set; }
    public int TurnosForzados { get; set; }
    public int Vueltas { get; set; }
}

/// <summary>
/// Dotación de guardias de una ciudad. Cada pocos segundos agrupa los cruces con atasco por cercanía en zonas y manda
/// un guardia a cada cruce atascado (el libre más cercano), que tarda en llegar según la distancia. Actúan como grupo
/// coordinado (ver <see cref="Zona"/>) y se retiran juntos cuando la zona se ha desatascado.
/// </summary>
public sealed class ServicioGuardias
{
    private const double IntervaloAvisos = 5.0;
    private const float VelocidadGuardiaMs = 12f;   // unos 43 km/h
    private const float FactorRodeo = 1.3f;         // las calles no van en línea recta
    private const double PreparacionS = 20.0;       // lo que tarda en salir
    private const float DistanciaZona = 300f;       // cruces atascados a menos de esto forman una misma zona
    private const int MaxCrucesPorZona = 16;

    /// <summary>
    /// Un guardia solo acude a un atasco grave: con al menos estos coches y que dure ya al menos este tiempo. Los atascos
    /// leves los resuelven los propios conductores (rutas alternativas, cesión del paso) y un guardia en un cruce que
    /// funciona solo lo empeoraría, porque atiende a una aproximación cada vez.
    /// </summary>
    public int CochesMinimosGrave { get; set; } = 8;
    public double DuracionMinimaGraveS { get; set; } = 180.0;

    private readonly DetectorAtascos _detector;
    private double _proximoAviso;
    private int _siguienteZona;

    public List<Guardia> Guardias { get; } = new();
    public List<Zona> Zonas { get; } = new();
    public List<Intervencion> Historial { get; } = new();

    /// <summary>
    /// Empieza a guardar la actuación de los guardias en <c>guardias.tsv</c> del estudio: una fila por hecho (llegada,
    /// inicio, cada turno y fin), con el cruce, la calle a la que se da paso, la orden, cuántos coches pasaron, las
    /// vueltas en U y las colas.
    /// </summary>
    public void Registrar(Estudio estudio)
    {
        var registro = estudio.Abrir("guardias.tsv", "Evento", "Hora", "T_s", "Guardia", "Zona", "Cruce", "Calle", "Direccion",
            "Orden", "DuracionS", "CochesPasaron", "VueltasU", "ColaInicio", "ColaFin", "GuardiasZona", "CrucesZona");
        foreach (var g in Guardias)
            g.AlRegistrar = f => registro.Fila(
                f.Evento, estudio.Hora(f.Tiempo), f.Tiempo, f.Guardia, f.Zona, Nombres.Cruce(f.Cruce),
                f.Calle != null ? Nombres.Calle(f.Calle) : "", f.Calle != null ? Nombres.Direccion(f.Calle, f.Calle.B == f.Cruce) : "",
                f.Orden, f.DuracionS, f.CochesPasaron, f.Vueltas, f.ColaInicio, f.ColaFin, f.GuardiasZona, f.CrucesZona);
    }

    public int Libres => Guardias.Count(g => g.Estado == EstadoGuardia.Libre);
    public int Actuando => Guardias.Count(g => g.Estado == EstadoGuardia.Actuando);

    public ServicioGuardias(IEnumerable<Nodo> bases, DetectorAtascos detector, ConfiguracionSimulacion config)
    {
        _detector = detector;
        foreach (var b in bases) Guardias.Add(new Guardia(Guardias.Count, b, config));
    }

    public void Actualizar(double ahora)
    {
        // Los guardias en camino llegan a su cruce.
        foreach (var g in Guardias)
            if (g.Estado == EstadoGuardia.EnCamino && ahora >= g.LlegadaPrevista) g.Llegar(ahora);

        // Cada zona decide cuándo empieza y cuándo termina.
        for (int i = Zonas.Count - 1; i >= 0; i--)
        {
            var z = Zonas[i];
            if (!z.Actualizar(ahora, _detector)) continue;

            Historial.Add(new Intervencion
            {
                Zona = z.Id,
                Guardias = z.Guardias.Count,
                Cruces = z.Nodos.Count,
                Inicio = z.Inicio!.Value,
                Fin = ahora,
                Turnos = z.Guardias.Sum(g => g.Turnos),
                TurnosForzados = z.Guardias.Sum(g => g.TurnosForzados),
                Vueltas = z.Guardias.Sum(g => g.Vueltas),
            });
            foreach (var g in z.Guardias) g.Terminar(ahora);
            Zonas.RemoveAt(i);
        }

        if (ahora >= _proximoAviso)
        {
            _proximoAviso = ahora + IntervaloAvisos;
            Despachar(ahora);
        }
    }

    /// <summary>
    /// Mira los cruces con atasco que aún no tienen guardia. Los cercanos a una zona ya en marcha la amplían; el resto
    /// se agrupa en zonas nuevas. Cada cruce recibe el guardia libre más cercano (el cruce más atascado, primero).
    /// </summary>
    private void Despachar(double ahora)
    {
        if (Libres == 0 || _detector.NumActivos == 0) return;

        var atendidos = new HashSet<Nodo>(Zonas.SelectMany(z => z.Nodos));
        var pendientes = _detector.Activos
            .Where(a => a.NodoFinal.Tramos.Count >= 3 && a.NodoFinal.Control != TipoControl.Rotonda && !atendidos.Contains(a.NodoFinal)
                        && a.MaxCoches >= CochesMinimosGrave && ahora - a.Inicio >= DuracionMinimaGraveS)
            .GroupBy(a => a.NodoFinal)
            .Select(g => (nodo: g.Key, coches: g.Sum(a => a.MaxCoches)))
            .OrderByDescending(x => x.coches)
            .ThenBy(x => x.nodo.Id)
            .ToList();
        if (pendientes.Count == 0) return;

        foreach (var (nodo, _) in pendientes)
        {
            if (Libres == 0) return;

            // ¿Está cerca de una zona que ya existe? Entonces se une a ella.
            var zona = Zonas.FirstOrDefault(z => z.Nodos.Count < MaxCrucesPorZona &&
                                                 z.Nodos.Any(n => n.Pos.DistanceTo(nodo.Pos) <= DistanciaZona));
            if (zona == null)
            {
                zona = new Zona(_siguienteZona++, ahora);
                Zonas.Add(zona);
            }

            var guardia = Guardias
                .Where(g => g.Estado == EstadoGuardia.Libre)
                .OrderBy(g => g.Posicion.Pos.DistanceSquaredTo(nodo.Pos))
                .ThenBy(g => g.Id)
                .First();

            double distancia = guardia.Posicion.Pos.DistanceTo(nodo.Pos) * FactorRodeo;
            guardia.Enviar(nodo, ahora + PreparacionS + distancia / VelocidadGuardiaMs);
            guardia.Zona = zona;
            zona.Guardias.Add(guardia);
            zona.Nodos.Add(nodo);
        }

        // Calles aledañas: con los guardias que sobren, cada zona se extiende a los cruces contiguos de sus cruces atascados,
        // para que actúen como semáforos sincronizados que desbloquean toda la zona y no solo el punto atascado.
        foreach (var zona in Zonas.Where(z => !z.Actuando || z.Nodos.Count < MaxCrucesPorZona).ToList())
        {
            foreach (var nodo in zona.Nodos.ToList())
            {
                foreach (var t in nodo.Tramos)
                {
                    if (Libres == 0 || zona.Nodos.Count >= MaxCrucesPorZona) break;
                    var vecino = t.A == nodo ? t.B : t.A;
                    // Una rotonda se regula sola (el anillo tiene prioridad): el guardia actúa en los cruces de alrededor.
                    if (vecino.Tramos.Count < 3 || vecino.Control == TipoControl.Rotonda || Zonas.Any(z => z.Nodos.Contains(vecino))) continue;

                    var guardia = Guardias
                        .Where(g => g.Estado == EstadoGuardia.Libre)
                        .OrderBy(g => g.Posicion.Pos.DistanceSquaredTo(vecino.Pos))
                        .ThenBy(g => g.Id)
                        .First();
                    double distancia = guardia.Posicion.Pos.DistanceTo(vecino.Pos) * FactorRodeo;
                    guardia.Enviar(vecino, ahora + PreparacionS + distancia / VelocidadGuardiaMs);
                    guardia.Zona = zona;
                    zona.Guardias.Add(guardia);
                    zona.Nodos.Add(vecino);
                }
            }
        }
        foreach (var zona in Zonas) zona.CalcularProfundidad();
    }
}

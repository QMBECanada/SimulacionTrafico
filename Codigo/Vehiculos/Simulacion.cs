using System.Collections.Generic;
using System.Linq;
using Godot;
using SimulacionTrafico.Nucleo;
using SimulacionTrafico.Mapa;
using SimulacionTrafico.Datos;
using SimulacionTrafico.Trafico;

namespace SimulacionTrafico.Vehiculos;

/// <summary>
/// Reloj de la simulación: avanza a pasos fijos de 0,1 s, independientes de los fotogramas.
/// Lleva el ciclo de vida de los coches (dentro de un lugar, salir, circular, llegar), pero no decide nada de
/// lo que hacen: cada coche elige su destino al terminar su estancia y planifica su ruta.
/// </summary>
public partial class Simulacion : Node
{
    public const float Paso = 0.1f;
    private const int MaxPasosPorFotograma = 40;
    private const double PresupuestoFotogramaMs = 12.0;   // tiempo real máximo para calcular pasos en un fotograma
    // Trabajo máximo por paso para calcular rutas (calles exploradas). Reparte el cálculo cuando salen muchos coches a
    // la vez y, al no depender del reloj, la simulación da siempre los mismos resultados.
    private const long PresupuestoExpansiones = 12000;

    private readonly Grafo _grafo;
    private readonly Aleatorio _azar;   // base: cada vehículo deriva de ella su propia secuencia (Derivar(id))
    private readonly float _factorEstancia;
    private double _acumulado;

    // Coches dentro de un lugar, ordenados por la hora a la que terminan su estancia.
    private readonly PriorityQueue<Coche, double> _dentro = new();
    private readonly PriorityQueue<Coche, double> _dentroAutobuses = new();   // los autobuses salen cuando les toca, sin demanda

    /// <summary>Pone el coche en la cola de salida de su lugar para esa hora.</summary>
    private void Encolar(Coche coche, double hora) =>
        (coche.Rutina?.Perfil == Perfil.Autobus ? _dentroAutobuses : _dentro).Enqueue(coche, hora);
    private readonly Dictionary<Lugar, int> _trabajosAsignados = new();   // trabajadores de cada lugar (para no pasar de sus plazas)
    private bool _rutinasColocadas;

    /// <summary>Segundos desde la medianoche del primer día (la hora de inicio más lo simulado; sin demanda, se empieza a las 8).</summary>
    /// <summary>Opciones de esta simulación, las condiciones del momento y lo que se prepara en cada paso para los conductores.</summary>
    public ContextoSimulacion Contexto { get; }
    private ConfiguracionSimulacion Config => Contexto.Config;

    public double HoraAbsolutaS => (Demanda?.HoraInicioS ?? 8.0 * 3600.0) + TiempoSimulado;
    // Coches que acaban de llegar y están maniobrando para ocupar su plaza (fuera de la calzada), por hora de fin.
    private readonly PriorityQueue<Coche, double> _aparcando = new();
    // Lugares con coches esperando hueco para salir.
    private readonly List<Lugar> _conSalidas = new();

    public Enrutador Enrutador { get; }
    public DetectorAtascos Atascos { get; }

    /// <summary>Central de tráfico: recibe los avisos de los vehículos (marcha y accidentes) y se los ofrece a los navegadores.</summary>
    public CentralTrafico Central { get; }
    public ServicioGuardias Guardias { get; }
    public Estudio? Estudio { get; private set; }

    private readonly Percepcion _percepcion = new();
    private long _pasoPercepcion;

    private RegistroSemaforos? _registroSemaforos;
    private RegistroTsv? _viajes, _eventosLugares;
    private double _proximaMedicion;
    private const float IntervaloMedicion = 5f;
    public CatalogoCoches Catalogo { get; }

    /// <summary>Todos los coches de la simulación, circulen o no.</summary>
    public List<Coche> Flota { get; } = new();

    /// <summary>Coches que están ahora en la calle.</summary>
    public List<Coche> Coches { get; } = new();

    /// <summary>
    /// Cuántos coches deben estar en la calle a cada hora. Si es null no hay control: los coches salen en cuanto
    /// terminan su estancia.
    /// </summary>
    public Demanda? Demanda { get; set; }

    private int _enSalida;               // coches que ya tienen ruta y esperan hueco para salir
    private double _cupoSalidas;         // salidas que se pueden hacer ahora según la demanda (sube con el ritmo de la hora)
    private int _flotaDemanda = -1;      // vehículos que siguen la demanda (todos menos los autobuses)

    /// <summary>Viajes empezados desde el principio, sin contar los de los autobuses (se planifica la ruta y el coche sale de su plaza).</summary>
    public int Salidas { get; private set; }

    /// <summary>Viajes por hora que marca la demanda ahora (infinito sin demanda).</summary>
    public double ViajesPorHoraObjetivo => Demanda == null ? double.PositiveInfinity
        : Demanda.ViajesPorHora(TiempoSimulado, _flotaDemanda >= 0 ? _flotaDemanda : Flota.Count);

    /// <summary>Coches en la calle o a punto de salir a ella.</summary>
    public int EnCalle => Coches.Count + _enSalida;

    /// <summary>Velocidad de la simulación respecto al tiempo real (1 = normal).</summary>
    public float Aceleracion { get; set; } = 1f;
    public double TiempoSimulado { get; private set; }

    /// <summary>Velocidad realmente conseguida respecto al tiempo real (media móvil): menor que la pedida si el equipo no llega.</summary>
    public float Factor { get; private set; } = 1f;

    /// <summary>Tiempo real que cuesta calcular un paso de simulación (media móvil), en milisegundos.</summary>
    public double MsPorPaso { get; private set; }
    public double MsPorPasoMaximo { get; private set; }

    // Estadísticas de las misiones
    public int Llegadas { get; private set; }
    public double TiempoViajeTotal { get; private set; }
    public double DistanciaViajeTotal { get; private set; }
    public int SinRuta { get; private set; }

    public int Planificados { get; private set; }
    public double DistanciaPlanificadaTotal { get; private set; }   // en línea recta, de puerta a puerta
    public int ReRutas { get; private set; }

    /// <summary>Fracción del paso actual ya transcurrida (0..1); sirve para interpolar el dibujo.</summary>
    public float Alfa => (float)(_acumulado / Paso);

    /// <param name="factorEstancia">Multiplica el tiempo que pasan los coches dentro de los lugares (1 = el normal).</param>
    /// <param name="numGuardias">Guardias de tráfico; -1 = los que corresponden al tamaño de la ciudad (cuatro por km², mínimo 4).</param>
    public Simulacion(Grafo grafo, CatalogoCoches catalogo, int numCoches, int semilla, ConfiguracionSimulacion config, float factorEstancia = 1f, int numGuardias = -1)
    {
        _grafo = grafo;
        Contexto = new ContextoSimulacion(config);
        grafo.RadioMinimoGiro = config.RadioMinimoGiro;
        grafo.FactorPlazas = config.FactorPlazas;
        foreach (var n in grafo.Nodos) n.Semaforo?.Configurar(config.SemaforosAdaptativos, config.FasesGiro);
        if (config.OndaVerde) Semaforo.Coordinar(grafo);
        // Todo lo que se prepara la primera vez que se pide, preparado ya: los coches lo consultan desde varios hilos a la vez.
        foreach (var n in grafo.Nodos) n.Semaforo?.AsegurarPreparado();
        grafo.PrepararTodos();   // conflictos de los cruces (no hace nada si ya están)
        Catalogo = catalogo;
        _factorEstancia = factorEstancia;
        _azar = new Aleatorio((ulong)semilla * 7919UL + 13UL);
        Enrutador = new Enrutador(grafo);
        Atascos = new DetectorAtascos(grafo);
        Enrutador.Congestion = Atascos.Congestion;
        Central = new CentralTrafico(grafo, Atascos);
        Enrutador.Central = Central;
        double areaKm2 = grafo.Limites.Size.X * grafo.Limites.Size.Y / 1e6;
        int guardias = numGuardias >= 0 ? numGuardias : System.Math.Max(4, (int)System.Math.Round(areaKm2 * 4.0));
        Guardias = new ServicioGuardias(grafo.BasesGuardias.Take(guardias), Atascos, config);
        Peatones = new ServicioPeatones(grafo, semilla, config.Peatones);
        CrearPuertas();
        for (int i = 0; i < numCoches; i++) CrearCoche(i);
        if (config.Autobuses) CrearAutobuses();
        // Plazas de cada lugar según su tipo y superficie (nunca menos que los coches con que empieza).
        // Plazas de cada lugar según su tipo y superficie; nunca menos que los coches con que empieza ni que los que trabajan en él
        // (con flotas grandes, los lugares de trabajo tienen el aparcamiento que necesitan).
        foreach (var l in grafo.Lugares)
            l.Capacidad = System.Math.Max(System.Math.Max(Lugar.PlazasPara(l.Tipo, l.Area, grafo.FactorPlazas), l.Dentro), (int)(_trabajosAsignados.GetValueOrDefault(l) * 1.1));
    }

    /// <summary>
    /// Empieza a guardar los datos de este estudio: las calles al empezar, los semáforos, los radares y los guardias a medida
    /// que ocurren, y los atascos al terminar. Los parámetros los escribe quien lo abre (ver <see cref="Exportador"/>).
    /// </summary>
    public void AbrirEstudio(Estudio estudio)
    {
        Estudio = estudio;
        Exportador.EscribirCalles(estudio, _grafo);
        Exportador.EscribirGirosProhibidos(estudio, _grafo);
        Exportador.EscribirConductores(estudio, Flota);
        Contexto.RegistrarRecorridos = true;
        _viajes = estudio.Abrir("viajes.tsv", "Coche", "Conductor", "Origen", "TipoOrigen", "Destino", "TipoDestino", "Motivo", "HoraSalida", "HoraLlegada",
            "T_salida", "T_llegada", "DistanciaM", "Paradas", "ParadoS", "Recalculos", "Choques", "Recorrido");
        _registroAccidentes = estudio.Abrir("accidentes.tsv", "Hora", "T_s", "Evento", "Accidente", "Lugar", "X", "Y", "Coche1", "Coche2",
            "Velocidad1Kmh", "Velocidad2Kmh", "Via1", "Via2", "Conductor1", "Conductor2", "SolapeM", "VelRelativaKmh", "CochesImplicados", "CarrilesCortados");
        _eventosLugares = estudio.Abrir("lugares.tsv", "Hora", "T_s", "Lugar", "Tipo", "Evento", "Coche", "DentroDespues", "Capacidad", "Esperando");
        _registroSemaforos = new RegistroSemaforos(_grafo, estudio);
        Peatones.AbrirEstudio(estudio);
        Guardias.Registrar(estudio);

        var radares = estudio.Abrir("radares.tsv", "Nombre", "Calle", "Direccion", "Carril", "Hora", "T_s", "VelocidadKmh", "Coche");
        foreach (var r in _grafo.Radares)
            r.AlPasar = (radar, coche, t) => radares.Fila(radar.Nombre, Nombres.Calle(radar.Tramo),
                Nombres.Direccion(radar.Tramo, ((Carril)coche.Via).Ida), ((Carril)coche.Via).Indice + 1,
                estudio.Hora(t), t, coche.V * 3.6f, coche.Id);
    }

    /// <summary>Termina el estudio: escribe los atascos y cierra todos los ficheros.</summary>
    public void CerrarEstudio()
    {
        if (Estudio == null) return;
        Peatones.CerrarEstudio();
        Exportador.EscribirAtascos(Estudio, Atascos);
        foreach (var r in _grafo.Radares) r.AlPasar = null;
        _viajes = _eventosLugares = _registroAccidentes = null;
        Contexto.RegistrarRecorridos = false;
        _registroSemaforos = null;   // el fotograma que queda hasta salir ya no debe escribir en ficheros cerrados
        Estudio.Dispose();
        Estudio = null;
    }

    public override void _Process(double delta)
    {
        _acumulado += delta * Aceleracion;
        int pasos = 0;
        long inicioFotograma = System.Diagnostics.Stopwatch.GetTimestamp();
        while (_acumulado >= Paso && pasos < MaxPasosPorFotograma &&
               (pasos == 0 || System.Diagnostics.Stopwatch.GetElapsedTime(inicioFotograma).TotalMilliseconds < PresupuestoFotogramaMs))
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            UnPaso();
            double ms = System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            MsPorPaso = MsPorPaso == 0 ? ms : MsPorPaso * 0.9 + ms * 0.1;
            if (ms > MsPorPasoMaximo) MsPorPasoMaximo = ms;
            _acumulado -= Paso;
            TiempoSimulado += Paso;
            pasos++;
        }
        if (_acumulado >= Paso) _acumulado = 0; // no puede seguir el ritmo: descarta el atraso
        if (Aceleracion > 0f && delta > 0)
            Factor = Factor * 0.97f + (float)(pasos * Paso / delta) * 0.03f;
    }

    /// <summary>Avanza la simulación hasta ese instante de golpe, sin esperar a los fotogramas (modo sin pantalla).</summary>
    public void AvanzarHasta(double segundos)
    {
        while (TiempoSimulado < segundos)
        {
            UnPaso();
            TiempoSimulado += Paso;
        }
    }

    /// <summary>Número que resume el estado de toda la flota: dos simulaciones idénticas dan la misma huella.</summary>
    public long Huella()
    {
        unchecked
        {
            long h = 17;
            foreach (var c in Flota)
            {
                h = h * 31 + (long)(c.Pos.X * 100f);
                h = h * 31 + (long)(c.Pos.Y * 100f);
                h = h * 31 + c.CambiosCarril;
                h = h * 31 + (c.Circulando ? 1 : 0);
            }
            return h * 31 + Llegadas;
        }
    }

    private List<Nodo>? _conSemaforo;
    private double _proximaRevisionCirculos;

    /// <summary>Círculos de espera deshechos (alguien tomó la iniciativa).</summary>
    public int CirculosDeshechos { get; private set; }

    /// <summary>
    /// Busca círculos de espera (A espera a B, que espera a C... que espera a A) entre los vehículos parados hace más de 15 s. En cada
    /// uno, el que va delante toma la iniciativa: deja de esperar al que tiene delante durante 6 s y avanza despacio. Es lo que
    /// pasa en la realidad cuando nadie se mueve.
    /// </summary>
    private void DeshacerCirculos()
    {
        var revisados = new HashSet<Coche>();
        foreach (var inicio in Coches)
        {
            if (inicio.V > 0.1f || inicio.TiempoParado < 15f || inicio.Bloqueador == null || revisados.Contains(inicio)) continue;
            var cadena = new List<Coche>();
            var actual = inicio;
            while (actual != null && actual.V < 0.1f && !cadena.Contains(actual) && cadena.Count < 40)
            {
                cadena.Add(actual);
                actual = actual.Bloqueador;
            }
            foreach (var c in cadena) revisados.Add(c);
            if (actual == null || !cadena.Contains(actual)) continue;
            var circulo = cadena.SkipWhile(c => c != actual).ToList();
            if (circulo.Any(c => TiempoSimulado < c.IniciativaHasta)) continue;
            // Toma la iniciativa el que va delante: el que menos camino tiene hasta el final de su vía (si empatan, el más agresivo; y si
            // también empatan en eso, el que gana el sorteo).
            var decidido = circulo.OrderBy(c => c.Via.Longitud - c.S).ThenByDescending(c => c.Conductor.Agresividad).ThenByDescending(c => c.Sorteo).First();
            decidido.Ignorado = decidido.Bloqueador;
            decidido.IniciativaHasta = TiempoSimulado + 6.0;
            CirculosDeshechos++;
        }
    }

    /// <summary>Peatones y pasos de peatones.</summary>
    public ServicioPeatones Peatones { get; }

    /// <summary>Si es de noche en ese segundo del día (0 a 86400). Sin indicarlo, de 21:00 a 7:00.</summary>
    public System.Func<double, bool>? EsDeNoche { get; set; }

    /// <summary>Gente en la calle a esa hora respecto a la hora punta (la forma de la demanda; sin demanda, la mitad).</summary>
    private float FactorPeatones(double tiempo)
    {
        if (Demanda == null) return 0.5f * Contexto.Condiciones.FactorPeatones;
        float maximo = Demanda.Aplicado.Max();
        return maximo <= 0f ? 0f : Demanda.Aplicado[Demanda.Hora(tiempo)] / maximo * Contexto.Condiciones.FactorPeatones;
    }

    /// <summary>
    /// Al empezar (cuando ya se conoce la hora de inicio): quien trabaja y a esa hora estaría en su jornada empieza en el trabajo; el resto,
    /// en casa. Cada uno saldrá cuando acabe su estancia según su rutina (la demanda puede adelantar salidas para llenar la calle).
    /// </summary>
    private void ColocarSegunHora()
    {
        _rutinasColocadas = true;
        double ahora = HoraAbsolutaS;
        float h = (float)(ahora % 86400.0 / 3600.0);
        _dentro.Clear();
        _dentroAutobuses.Clear();
        foreach (var c in Flota)
        {
            var r = c.Rutina;
            if (r?.Trabajo != null && r.Trabaja && h >= r.HoraSalida + 0.75f && h < r.HoraFin && c.LugarActual == r.Casa)
            {
                r.Casa.Dentro--;
                c.Entrar(r.Trabajo);
                r.DiaTrabajo = (int)(ahora / 86400.0);
                if (r.ComeFuera && h >= 14.5f) r.DiaComida = r.DiaTrabajo;
            }
            Encolar(c, c.Rutina == null ? c.Azar.RandfRange(0f, 300f) : TiempoSimulado + Misiones.Estancia(c, c.LugarActual!, ahora, c.Azar, _factorEstancia));
        }
        foreach (var l in _grafo.Lugares) l.Capacidad = System.Math.Max(l.Capacidad, l.Dentro);
    }

    /// <summary>
    /// Paso de todos los coches en dos fases (ver Coche): publican su estado, deciden todos a la vez en paralelo y aplican en orden.
    /// El resultado es el mismo con cualquier número de hilos.
    /// </summary>
    private void MoverCoches()
    {
        foreach (var c in Coches) c.Publicar();
        double t = TiempoSimulado;
        EnParalelo(Coches.Count, i => Coches[i].Decidir(Paso, t));
        for (int i = 0; i < Coches.Count; i++) Coches[i].Aplicar(Paso, t);
    }

    /// <summary>
    /// Ejecuta <paramref name="cuerpo"/> para 0..n-1 repartido entre los hilos de la configuración. Solo para trabajo en el que cada
    /// índice escribe únicamente en lo suyo: así el resultado no depende del reparto.
    /// </summary>
    private void EnParalelo(int n, System.Action<int> cuerpo)
    {
        int hilos = Config.HilosEfectivos;
        if (hilos <= 1 || n < CochesParaParalelo)
        {
            for (int i = 0; i < n; i++) cuerpo(i);
            return;
        }
        System.Threading.Tasks.Parallel.ForEach(System.Collections.Concurrent.Partitioner.Create(0, n, System.Math.Max(32, n / (hilos * 4))),
            new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = hilos },
            rango => { for (int i = rango.Item1; i < rango.Item2; i++) cuerpo(i); });
    }

    /// <summary>Con menos coches que esto en la calle, repartirlos entre hilos cuesta más de lo que ahorra.</summary>
    private const int CochesParaParalelo = 200;

    private void UnPaso()
    {
        if (!_rutinasColocadas) ColocarSegunHora();
        double segundoDelDia = HoraAbsolutaS % 86400.0;
        Contexto.Condiciones.Noche = EsDeNoche?.Invoke(segundoDelDia) ?? (segundoDelDia < 7 * 3600.0 || segundoDelDia >= 21 * 3600.0);
        Enrutador.NuevoPaso();
        if (Config.SemaforosAdaptativos)
        {
            _conSemaforo ??= _grafo.Nodos.Where(n => n.Control == TipoControl.Semaforo && n.Semaforo != null).ToList();
            foreach (var n in _conSemaforo) n.Semaforo!.Actualizar(n, TiempoSimulado);
        }
        if (TiempoSimulado >= _proximaRevisionCirculos)
        {
            DeshacerCirculos();
            _proximaRevisionCirculos = TiempoSimulado + 2.0;
        }
        if (TiempoSimulado >= _proximaMedicion)
        {
            foreach (var c in Coches) c.InformarMarcha(Central);   // cada vehículo informa de su calle y su velocidad
            Atascos.Cerrar(TiempoSimulado, IntervaloMedicion);
            _proximaMedicion = TiempoSimulado + IntervaloMedicion;
        }

        Guardias.Actualizar(TiempoSimulado);
        _registroSemaforos?.Actualizar(TiempoSimulado);
        Peatones.Actualizar(TiempoSimulado, Paso, FactorPeatones);
        Aparcar();
        LiberarDentro();
        ProcesarSalidas();

        // Lo que ven los conductores: dónde está cada coche y dónde estará en los próximos segundos.
        if (Config.Percepcion)
        {
            // Los conductores miran a su alrededor cada 0,3 s (un tiempo de reacción realista); entre medias recuerdan lo que vieron.
            Contexto.PercibirAhora = _pasoPercepcion++ % 3 == 0;
            if (Contexto.PercibirAhora)
            {
                EnParalelo(Coches.Count, i => Coches[i].CalcularFuturo());   // cada coche solo escribe su propio futuro
                _percepcion.Reconstruir(Coches);
            }
            Contexto.Entorno = _percepcion;
        }
        else Contexto.Entorno = null;
        MoverCoches();

        ProcesarAccidentes();
        if (DiagnosticoAceleracion) MedirArranques();

        // Los que han llegado a su puerta entran en el lugar y empiezan su estancia.
        for (int i = Coches.Count - 1; i >= 0; i--)
        {
            var c = Coches[i];
            if (c.Circulando) continue;

            var destino = c.Destino!;
            Llegadas++;
            TiempoViajeTotal += TiempoSimulado + Paso - c.InicioViaje;
            DistanciaViajeTotal += c.DistanciaViaje;
            ReRutas += c.ReRutas;

            double llegada = TiempoSimulado + Paso;
            bool hayPlaza = destino.Dentro < destino.Capacidad;
            c.Entrar(destino, hayPlaza);
            c.LlegadaT = llegada;
            if (_viajes != null)
            {
                var o = c.OrigenViaje;
                _viajes.Fila(c.Id, c.Conductor.Tipo, o?.Nombre, o?.Tipo, destino.Nombre, destino.Tipo, c.MotivoViaje, Estudio!.Hora(c.InicioViaje), Estudio.Hora(llegada),
                    c.InicioViaje, llegada, c.DistanciaViaje, c.ParadasViaje, c.ParadoViajeS, c.ReRutas, c.ChoquesViaje, c.Recorrido?.ToString());
            }
            // La maniobra y la espera de plaza ocurren dentro del lugar, fuera de la calzada: el coche ya no está en la calle.
            if (hayPlaza) _aparcando.Enqueue(c, llegada + Misiones.Maniobra(destino.Tipo, c.Azar, true));
            else destino.EsperandoPlaza.Enqueue(c);
            Evento(destino, "Llega", c, llegada);
            if (!hayPlaza) Evento(destino, "SinPlaza", c, llegada);

            Coches[i] = Coches[^1];
            Coches.RemoveAt(Coches.Count - 1);
        }
    }

}

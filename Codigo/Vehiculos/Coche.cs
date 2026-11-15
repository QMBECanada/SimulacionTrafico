using System.Collections.Generic;
using System.Linq;
using Godot;
using SimulacionTrafico.Mapa;
using SimulacionTrafico.Nucleo;
using SimulacionTrafico.Trafico;

namespace SimulacionTrafico.Vehiculos;

/// <summary>
/// Agente autónomo. Percibe solo lo cercano (el coche de delante, las curvas de su plan, la señal de su
/// cruce y los coches que pueden estorbarle en él), decide por su cuenta (qué carril tomar, si cede el paso)
/// y actúa (acelera o frena según su conductor). Nadie le dice qué hacer.
/// </summary>
public sealed partial class Coche
{
    private const float HorizonteVision = 250f;   // hasta dónde mira hacia delante, en metros
    private const float DistanciaControl = 70f;   // distancia mínima a la que atiende a las señales del cruce (crece con la velocidad)
    private const float VisionCruce = 60f;        // distancia mínima a la que ve venir a otros coches hacia el cruce (crece con su límite de velocidad)
    private const float DistanciaPreparacion = 200f; // a esta distancia del cruce empieza a colocarse en el carril de su giro
    private const float DuracionCambio = 2.5f;    // segundos que tarda en pasar de un carril al contiguo

    private readonly Grafo _grafo;
    private readonly Aleatorio _azar;
    private readonly ContextoSimulacion _contexto;

    /// <summary>Secuencia aleatoria propia de este vehículo: sus decisiones no dependen de las de los demás ni del orden en que se procesen.</summary>
    public Aleatorio Azar => _azar;

    /// <summary>
    /// Número que el conductor sortea al entrar en cada calle (no al entrar en el cruce ni al cambiar de carril: así no cambia mientras se
    /// acerca al cruce y lo cruza). Decide los empates con otro conductor (la misma prioridad en el cruce y la
    /// misma espera, un choque previsto sin preferencia clara, un círculo de espera...): pasa el del número más alto.
    /// </summary>
    public ulong Sorteo { get; private set; }

    /// <summary>En un empate con <paramref name="otro"/>, si pasa él antes que yo.</summary>
    public bool MeGanaElSorteo(Coche otro) => otro.Sorteo > Sorteo || (otro.Sorteo == Sorteo && otro.Id < Id);   // mismo número: casi imposible

    // Los sorteos van en una secuencia aparte: no cambian los demás números al azar del vehículo.
    private readonly Aleatorio _sorteos;

    /// <summary>Sortea al entrar en una calle (en la fase de aplicar o al colocarlo: mientras los demás deciden, no cambia).</summary>
    private void Sortear() => Sorteo = _sorteos.Siguiente();
    private readonly Enrutador _enrutador;

    // Misión: a dónde va y por qué calles (después de la actual)
    private readonly Queue<Tramo> _ruta = new();
    public bool Circulando { get; private set; }
    public Lugar? LugarActual { get; private set; }   // donde está si no circula
    public Lugar? Destino { get; private set; }       // adonde va si circula
    public double InicioViaje { get; private set; }
    public float DistanciaViaje { get; private set; }
    public int ReRutas { get; private set; }
    public int CallesPendientes => _ruta.Count;
    private double _tiempoActual;
    private double _ultimaRuta;      // cuándo calculó su ruta por última vez

    /// <summary>Segundos seguidos sin moverse (no solo en un cruce). Si es demasiado, la simulación lo saca de la calle.</summary>
    public float TiempoParado { get; private set; }

    // Datos en bruto del viaje en curso (se guardan en viajes.tsv al llegar)
    public Lugar? OrigenViaje { get; private set; }
    // Accidente en el que está implicado: inmóvil hasta DetenidoHasta; tras reanudar no cuenta como choque hasta InmuneHasta.
    public Trafico.Accidente? Accidente { get; private set; }
    public double DetenidoHasta { get; private set; }
    public double InmuneHasta { get; private set; }
    public bool EnAccidente(double tiempo) => tiempo < DetenidoHasta;

    /// <summary>Se queda inmóvil (accidente) hasta ese instante.</summary>
    public void Detener(double hasta, Trafico.Accidente accidente)
    {
        DetenidoHasta = hasta;
        Accidente = accidente;
        V = 0f;
        A = 0f;
        Motivo = "accidente";
    }

    /// <summary>Avisa a la central de que su accidente corta esos carriles (hasta lo que diga cada uno).</summary>
    public void AvisarAccidente(CentralTrafico central)
    {
        if (Accidente != null) central.AvisarCorte(Accidente.Carriles);
    }

    /// <summary>Informa a la central de por dónde va y a qué velocidad (solo por las calles, no dentro de los cruces).</summary>
    public void InformarMarcha(CentralTrafico central)
    {
        if (Via is Carril l) central.InformarMarcha(l, V);
    }

    /// <summary>
    /// Acaba el accidente: reanuda la marcha y avisa a la central de que sus carriles quedan despejados. Durante unos segundos no vuelve
    /// a contar como choque con quien aún esté tocando.
    /// </summary>
    public void Reanudar(double inmuneHasta, CentralTrafico central)
    {
        if (Accidente != null) central.AvisarDespejado(Accidente.Carriles);
        DetenidoHasta = 0;
        Accidente = null;
        InmuneHasta = inmuneHasta;
        Motivo = null;
    }
    public double LlegadaT { get; set; }       // cuándo llegó a la puerta del lugar
    public double ListoT { get; set; }         // cuándo termina de maniobrar para salir
    /// <summary>Rutina diaria del conductor (casa, trabajo, horarios). Null en simulaciones sin lugares.</summary>
    public Rutina? Rutina { get; set; }
    /// <summary>Por qué hace el viaje en curso: Trabajo, Colegio, Comida, Casa u Otro.</summary>
    public string MotivoViaje { get; set; } = "";
    /// <summary>Su última ruta falló: hasta entonces no se le adelanta la salida.</summary>
    public double NoSalirAntesDe { get; set; }
    public int ChoquesViaje { get; set; }                   // accidentes en los que se ha visto implicado durante el viaje
    public int ParadasViaje { get; private set; }          // veces que se ha detenido (V < 0,1 m/s) desde que salió
    public float ParadoViajeS { get; private set; }        // segundos detenido en total
    public System.Text.StringBuilder? Recorrido { get; private set; }   // Calle/Direccion@t_s;...

    public int Id { get; }
    public CatalogoCoches.Modelo Modelo { get; }
    public Conductor Conductor { get; }
    public float Longitud => Modelo.LargoM;

    public ViaCirculacion Via { get; private set; } = null!;
    public float S { get; private set; }     // posición a lo largo de la vía actual
    public float V { get; private set; }     // velocidad, m/s
    public float A { get; private set; }     // última aceleración, m/s²

    /// <summary>Vía actual y las siguientes que ya ha decidido recorrer (carril, conexión, carril...).</summary>
    public List<ViaCirculacion> Plan { get; } = new();

    // Estado dibujable (con el paso anterior para interpolar entre pasos de simulación)
    public Vector2 Pos { get; private set; }
    public Vector2 PosAnterior { get; private set; }
    public float Rumbo { get; private set; }
    public float RumboAnterior { get; private set; }

    // Estado del cruce que tiene delante
    private bool _haParado;          // ya hizo el alto obligatorio del stop
    private float _espera;           // segundos esperando en la línea de detención

    // Distancia mínima en parado: la del conductor por un factor que se vuelve a sortear cada vez que arranca
    private float _factorS0 = 1f;
    private float _factorArranque = 1f;

    /// <summary>Diagnóstico: cuándo arrancó por última vez desde parado.</summary>
    public double UltimoArranque { get; set; } = double.NegativeInfinity;
    public float DistanciaMinimaActual => Conductor.DistanciaMinima * _factorS0;

    // Cambio de carril
    private Tramo? _tramoSiguiente;       // calle por la que seguirá tras el próximo cruce
    private int? _indiceDeseado;          // carril en el que debe estar para ese giro (null: le sirve cualquiera)
    private float _enfriamientoCambio;    // tiempo hasta poder volver a cambiar
    private float _proximaEvaluacion;     // tiempo hasta volver a pensar en cambiar
    private float _desfaseLateral;        // metros que aún le faltan para estar centrado en su carril (animación)
    /// <summary>Metros que aún le faltan para estar centrado en su carril tras un cambio (negativo: aún está a su izquierda).</summary>
    public float DesfaseLateral => _desfaseLateral;

    /// <summary>True si el coche ocupa ese carril o, tras un cambio de carril reciente, todavía está pasando por él.</summary>
    public bool OcupaCarril(Carril carril) =>
        Via == carril || (Via is Carril v && ((v == carril.Derecho && _desfaseLateral < -1.2f) || (v == carril.Izquierdo && _desfaseLateral > 1.2f)));
    public int CambiosCarril { get; private set; }
    public int CambiosObligados { get; private set; }
    public int? IndiceDeseado => _indiceDeseado;

    // Arranque con retardo de reacción
    private bool _reaccionando;
    private float _retardo;

    // Datos de depuración
    public float Hueco { get; private set; } = float.PositiveInfinity;
    public float VelocidadDeseadaActual { get; private set; }
    public float Espera => _espera;

    /// <summary>Por qué está parado ante un cruce (null si no lo está).</summary>
    public string? Motivo { get; private set; }

    /// <summary>Coche que le limita ahora (el de delante o aquel al que cede el paso); solo para depuración.</summary>
    public Coche? Bloqueador { get; private set; }

    /// <summary>
    /// Iniciativa: cuando varios vehículos se esperan unos a otros en círculo y nadie se mueve, el que va delante (el que menos camino tiene
    /// hasta el final de su vía) deja de
    /// esperar al que tiene delante (<see cref="Ignorado"/>) hasta <see cref="IniciativaHasta"/> y avanza despacio (lo decide la simulación).
    /// </summary>
    public double IniciativaHasta { get; set; }
    public Coche? Ignorado { get; set; }
    private const float VelocidadIniciativa = 2.5f;
    private Coche? _bloqueoCruce;
    public bool BloqueadorEsLider { get; private set; }

    public Coche(int id, CatalogoCoches.Modelo modelo, Conductor conductor, Grafo grafo, Aleatorio azar, Enrutador enrutador, ContextoSimulacion contexto)
    {
        _contexto = contexto;
        Id = id;
        Modelo = modelo;
        Conductor = conductor;
        _grafo = grafo;
        _azar = azar;
        _sorteos = azar.Derivar(0x534F5254454FUL);
        _enrutador = enrutador;
    }

    /// <summary>Coloca el coche en un carril, en la posición s, con velocidad inicial v.</summary>
    public void Colocar(Carril carril, float s, float v)
    {
        if (Via != null) QuitarDe(Via.Coches);
        Via = carril;
        Sortear();
        S = s;
        V = v;
        _haParado = false;
        _espera = 0f;
        _llegadaCruce = double.PositiveInfinity;
        _reaccionando = false;
        _desfaseLateral = 0f;
        _enfriamientoCambio = 0f;
        _proximaEvaluacion = _azar.Randf();
        Plan.Clear();
        Plan.Add(carril);
        InsertarOrdenado(carril);
        Planificar();

        // Pose directa (sin suavizado ni interpolación): aparece justo ahí.
        Pos = Via.Eje.Posicion(S);
        Rumbo = Via.Eje.Direccion(S).Angle();
        PosAnterior = Pos;
        RumboAnterior = Rumbo;
    }

    private void InsertarOrdenado(ViaCirculacion via)
    {
        int i = via.Coches.Count;
        while (i > 0 && via.Coches[i - 1].S > S) i--;
        via.Coches.Insert(i, this);
        _indiceVia = i;
    }

    // Dónde estaba en la lista de su vía la última vez que se buscó. Es solo una pista: se comprueba antes de usarla, porque los coches
    // que entran por detrás o cambian de carril desplazan a los demás. Casi siempre acierta y evita recorrer la lista en cada paso.
    private int _indiceVia;

    /// <summary>Posición de este coche en la lista (-1 si no está), mirando primero donde estaba la última vez.</summary>
    private int IndiceEn(List<Coche> lista)
    {
        int h = _indiceVia;
        if ((uint)h < (uint)lista.Count && lista[h] == this) return h;
        if ((uint)(h + 1) < (uint)lista.Count && lista[h + 1] == this) return _indiceVia = h + 1;   // ha entrado uno por detrás
        return _indiceVia = lista.IndexOf(this);
    }

    private void QuitarDe(List<Coche> lista)
    {
        int i = IndiceEn(lista);
        if (i >= 0) lista.RemoveAt(i);
    }

    // ---- Paso en dos fases ----
    // Cada paso, todos los coches deciden a la vez (en paralelo) mirando cómo estaba todo al acabar el paso anterior, y después aplican
    // lo decidido uno tras otro, en orden. Así el resultado no depende de cuántos hilos haya ni de cómo se repartan los coches.
    // Durante Decidir un coche solo escribe en sí mismo y solo lee de los demás lo que no cambia en esa fase: su posición, velocidad,
    // plan y lo que publicaron al empezar el paso (Publicar). Lo que toca estado compartido (otra ruta, la vuelta en U, cambiar de
    // carril, moverse de vía) se hace en Aplicar.

    private bool _detenidoEstePaso;   // accidentado: este paso no se mueve
    private float _aceleracionDecidida;
    private Carril? _alternativaPendiente;                 // buscar otra salida del cruce (en Aplicar: calcula rutas)
    private (Carril carril, Guardia guardia)? _vueltaPendiente;   // dar la vuelta en U por orden del guardia (en Aplicar)

    // Lo que los demás ven de este coche mientras deciden: su estado al empezar el paso.
    private string? _motivoVisible;
    private float _huecoVisible = float.PositiveInfinity;
    private bool _esLiderVisible;
    private double _llegadaVisible = double.PositiveInfinity;

    /// <summary>
    /// Cuándo llegó a la línea del cruce que tiene delante (a menos de 12 m); infinito si aún no ha llegado. No cambia mientras espera:
    /// con la misma prioridad pasa el que llegó antes (y el sorteo si llegaron a la vez), así que la preferencia no va y viene.
    /// </summary>
    private double _llegadaCruce = double.PositiveInfinity;

    /// <summary>Con la misma prioridad en el cruce, si <paramref name="otro"/> pasa antes que yo: el que llegó antes y, si llegamos a la vez (o ninguno ha llegado aún), el sorteo.</summary>
    private bool TienePreferenciaSobreMi(Coche otro) =>
        otro._llegadaVisible < _llegadaVisible || (otro._llegadaVisible == _llegadaVisible && MeGanaElSorteo(otro));

    /// <summary>Al empezar el paso, antes de decidir: deja a la vista de los demás su estado actual.</summary>
    public void Publicar()
    {
        _motivoVisible = Motivo;
        _huecoVisible = Hueco;
        _esLiderVisible = BloqueadorEsLider;
        _llegadaVisible = _llegadaCruce;
    }

    /// <summary>Decide la aceleración de este paso mirando a su alrededor. Se llama a la vez para todos los coches (en paralelo).</summary>
    public void Decidir(float dt, double tiempo)
    {
        _tiempoActual = tiempo;
        _alternativaPendiente = null;
        _vueltaPendiente = null;
        _detenidoEstePaso = tiempo < DetenidoHasta;
        if (_detenidoEstePaso) return;   // accidentado: no se mueve
        Conductor.Actualizar(dt);

        float v0 = VelocidadObjetivo();
        // Toma la iniciativa para deshacer un círculo de espera: avanza despacio sin esperar a ese vehículo.
        bool iniciativa = tiempo < IniciativaHasta;
        if (iniciativa) v0 = Mathf.Min(v0, VelocidadIniciativa);
        VelocidadDeseadaActual = v0;
        var (hueco, vLider, lider) = BuscarLider();
        if (iniciativa && lider != null && lider == Ignorado) (hueco, vLider, lider) = (float.PositiveInfinity, 0f, null);
        Bloqueador = lider;
        BloqueadorEsLider = true;

        // El cruce que tiene delante puede comportarse como un obstáculo parado en la línea de detención.
        Motivo = null;
        _bloqueoCruce = null;
        if (Via is Carril carril && Plan.Count > 1 && Plan[1] is Conexion conexion)
        {
            float dLinea = carril.Longitud - S;
            float huecoLinea = dLinea - Longitud / 2f; // del parachoques delantero a la línea
            float distanciaControl = Mathf.Max(DistanciaControl, V * V / (2f * Conductor.FrenadaComoda) + 30f);
            if (huecoLinea < 12f && double.IsPositiveInfinity(_llegadaCruce)) _llegadaCruce = tiempo;
            if (dLinea < distanciaControl && huecoLinea > -0.5f && DebeParar(carril, conexion, huecoLinea, tiempo))
            {
                float h = Mathf.Max(huecoLinea + 1f, 0.1f);
                if (h < hueco)
                {
                    hueco = h;
                    vLider = 0f;
                    Bloqueador = _bloqueoCruce;
                    BloqueadorEsLider = false;
                }
                if (V < 0.5f && huecoLinea < 12f) _espera += dt;
            }
        }
        // Último recurso en los cruces: si prevé chocar con otro coche que tiene preferencia sobre mí, frena.
        var riesgo = RiesgoPrevisto();
        if (riesgo != null)
        {
            hueco = Mathf.Min(hueco, 0.3f);
            vLider = 0f;
            Bloqueador = riesgo;
            BloqueadorEsLider = false;
            if (V < 0.5f) _espera += dt;
        }
        // Y en todo momento: mira a su alrededor y, si va a tocar a alguien y le toca evitarlo, frena antes.
        if (_contexto.PercibirAhora)
        {
            (_visto, _huecoVisto) = RiesgoPercibido();
            _recorridoDesdeVisto = 0f;
        }
        var visto = _visto != null && _visto.Circulando ? _visto : null;
        float huecoVisto = Mathf.Max(0.3f, _huecoVisto - _recorridoDesdeVisto);
        _recorridoDesdeVisto += V * dt;
        if (visto != null && huecoVisto < hueco)
        {
            hueco = huecoVisto;
            vLider = 0f;
            Bloqueador = visto;
            BloqueadorEsLider = false;
            Motivo ??= "evita un choque";
        }
        Hueco = hueco;

        // Si va a reducir porque viene una curva, un giro o una calle más lenta, frena normal; si solo le sobra algo de velocidad, levanta el pie.
        float a = ModeloIDM.Aceleracion(Conductor, V, v0, hueco, vLider, DistanciaMinimaActual, _anticipando ? Conductor.FrenadaComoda : Conductor.FrenadaSuave);
        _aceleracionDecidida = AplicarReaccion(a, dt);
    }

    /// <summary>Aplica lo decidido: se mueve y, si llega al final de su vía, pasa a la siguiente.</summary>
    public void Aplicar(float dt, double tiempo)
    {
        PosAnterior = Pos;
        RumboAnterior = Rumbo;
        if (_detenidoEstePaso)
        {
            V = 0f;
            A = 0f;
            TiempoParado += dt;
            ParadoViajeS += dt;
            return;
        }
        // Lo decidido que toca estado compartido: rutas (cupo del enrutador), conexiones del cruce, el guardia.
        if (_alternativaPendiente is { } salidaLlena && IntentarAlternativa(salidaLlena)) Motivo = null;
        if (_vueltaPendiente is { } vuelta) IntentarVuelta(vuelta.carril, vuelta.guardia);

        float a = _aceleracionDecidida;
        A = a;
        V = Mathf.Max(0f, V + a * dt);
        float sAntes = S;
        S += V * dt;
        DistanciaViaje += V * dt;
        float parado = TiempoParado;
        TiempoParado = V < 0.1f ? TiempoParado + dt : 0f;
        if (V < 0.1f)
        {
            ParadoViajeS += dt;
            if (parado == 0f) ParadasViaje++;
        }

        // Radar: pasa por su punto a mitad de la calle.
        if (Via is Carril carrilRadar && carrilRadar.Tramo.Radar is { } radar)
        {
            float sRadar = radar.Fraccion * carrilRadar.Longitud;
            if (sAntes < sRadar && S >= sRadar) radar.Pasa(this, tiempo);
        }

        if (ComprobarLlegada()) return;
        while (S >= Via.Longitud) AvanzarVia();
        ActualizarPose(dt);
        EvaluarCambioCarril(dt);   // para el paso siguiente: cambiar de carril mueve listas que los demás leen al decidir
    }

    /// <summary>Si estaba parado y ahora puede salir, tarda su tiempo de reacción en arrancar.</summary>
    private float AplicarReaccion(float a, float dt)
    {
        if (V >= 0.2f || a <= 0.3f)
        {
            if (V >= 0.2f || a <= 0.05f) _reaccionando = false;
            return a;
        }
        // Parado en una cola: no arranca hasta que el de delante se ha separado lo suficiente (según su forma de conducir y con
        // algo de azar cada vez). Luego aún tarda su tiempo de reacción.
        if (!_reaccionando && BloqueadorEsLider && Bloqueador != null && !float.IsInfinity(Hueco)
            && Hueco < DistanciaMinimaActual * Conductor.FactorArranque * _factorArranque)
            return Mathf.Min(a, 0f);
        if (!_reaccionando)
        {
            _reaccionando = true;
            _retardo = Conductor.ReaccionAhora();
            _factorS0 = Mathf.Max(0.6f, 1f + _azar.Randfn(0f, 0.2f)); // la próxima vez que pare, dejará otra distancia
            _factorArranque = Mathf.Clamp(1f + _azar.Randfn(0f, 0.15f), 0.75f, 1.4f);   // y esperará otro hueco para arrancar (a veces menor)
        }
        if (_retardo <= 0f) return a;
        _retardo -= dt;
        return Mathf.Min(a, 0f);
    }

    private void ActualizarPose(float dt)
    {
        if (_desfaseLateral != 0f)
            _desfaseLateral = Mathf.MoveToward(_desfaseLateral, 0f, Escala.AnchoCarril / DuracionCambio * dt);

        var dir = Via.Eje.Direccion(S);
        Pos = Via.Eje.Posicion(S) + new Vector2(-dir.Y, dir.X) * _desfaseLateral;
        // El rumbo sigue a la dirección de la vía con un pequeño retardo, para que los giros no den saltos.
        Rumbo = Mathf.LerpAngle(RumboAnterior, dir.Angle(), 0.35f);
    }
}

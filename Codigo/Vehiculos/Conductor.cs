using Godot;
using SimulacionTrafico.Nucleo;
using SimulacionTrafico.Mapa;

namespace SimulacionTrafico.Vehiculos;

/// <summary>
/// Personalidad de un conductor. Un único valor de agresividad desplaza todos sus parámetros a la vez,
/// así que un conductor es coherente consigo mismo. Además, la velocidad y la distancia de seguridad
/// fluctúan con un ruido lento (proceso de Ornstein-Uhlenbeck), de modo que tampoco es idéntico en cada momento.
/// </summary>
/// <remarks>
/// Agresividad: de -2 (temeroso) a +2 (temerario). 0 es un conductor normal. La mayoría se reparte entre
/// -1,15 y +1,15; los extremos son minorías (ver <see cref="ProbTemeroso"/> y <see cref="ProbTemerario"/>).
/// </remarks>
public sealed class Conductor
{
    /// <summary>Proporción de conductores excesivamente prudentes o temerosos (agresividad de -2 a -1,2).</summary>
    public const float ProbTemeroso = 0.07f;

    /// <summary>Proporción de conductores temerarios (agresividad de +1,2 a +2).</summary>
    public const float ProbTemerario = 0.04f;

    private readonly Aleatorio _azar;
    private readonly Condiciones _condiciones;
    private float _ruidoVelocidad;
    private float _ruidoSeparacion;

    /// <summary>-2 temeroso ... -1 muy prudente ... 0 normal ... +1 agresivo ... +2 temerario.</summary>
    public float Agresividad { get; }

    /// <summary>Cuánto se pasa (o se queda corto) del límite de la vía, como fracción.</summary>
    public float ExcesoLimite { get; private set; }

    /// <summary>Tiempo de separación con el coche de delante, en segundos.</summary>
    public float TiempoSeparacionSeco { get; }
    public float TiempoSeparacion => TiempoSeparacionSeco * _condiciones.FactorSeparacion;

    /// <summary>Distancia mínima en parado, en metros.</summary>
    public float DistanciaMinimaSeco { get; }
    public float DistanciaMinima => DistanciaMinimaSeco * _condiciones.FactorDistanciaParado;

    /// <summary>Parado detrás de otro, arranca cuando el hueco llega a su distancia en parado por este factor (algo mayor que 1).</summary>
    public float FactorArranque { get; }

    /// <summary>Aceleración máxima, en m/s².</summary>
    public float AceleracionMaxSeco { get; }
    public float AceleracionMax => AceleracionMaxSeco * _condiciones.FactorAceleracion;

    /// <summary>
    /// Frenadas, en m/s², con valores reales: levantar el pie del acelerador (0,8-1,5), frenada urbana normal
    /// (2,0-3,0), frenada fuerte (4,0-6,0) y emergencia en seco (7,0-9,0). Cada conductor tiene la suya dentro de cada franja según su carácter.
    /// </summary>
    public float FrenadaSuave { get; private set; }
    public float FrenadaComodaSeco { get; }
    public float FrenadaComoda => FrenadaComodaSeco * _condiciones.FactorFrenadaComoda;
    public float FrenadaFuerteSeco { get; }
    public float FrenadaFuerte => FrenadaFuerteSeco * _condiciones.FactorFrenadaFuerte;
    public float FrenadaEmergenciaSeco { get; }
    public float FrenadaEmergencia => _condiciones.FrenadaEmergencia(FrenadaEmergenciaSeco, Clase);

    /// <summary>Aceleración lateral que tolera en curva, en m/s².</summary>
    public float AceleracionLateralSeco { get; }
    public float AceleracionLateral => AceleracionLateralSeco * _condiciones.FactorLateral;
    public float AceleracionLateralRotondaSeco { get; }
    public float AceleracionLateralRotonda => AceleracionLateralRotondaSeco * _condiciones.FactorLateral;

    /// <summary>Tiempo que tarda en reaccionar al arrancar, en segundos.</summary>
    public float TiempoReaccionSeco { get; }
    public float TiempoReaccion => TiempoReaccionSeco + _condiciones.ReaccionExtra;

    /// <summary>Hueco mínimo (en segundos) que exige para cruzar o incorporarse delante de otro coche.</summary>
    public float HuecoCriticoBaseSeco { get; }
    public float HuecoCriticoBase => HuecoCriticoBaseSeco * _condiciones.FactorHueco;

    /// <summary>Cuánto le importa el perjuicio a los demás al cambiar de carril: 0 egoísta, 1 muy cortés.</summary>
    public float Cortesia { get; }

    /// <summary>Mejora de aceleración (m/s²) que exige para cambiar de carril sin necesidad.</summary>
    public float UmbralCambioCarril { get; }

    /// <summary>Frenada máxima (m/s²) que acepta imponer al coche de atrás al meterse delante.</summary>
    public float FrenadaSeguridadCambio { get; }

    /// <summary>Si consulta el estado del tráfico (como un navegador) y evita las calles atascadas al elegir ruta.</summary>
    public bool UsaNavegador { get; }

    /// <summary>Segundos esperando en un cruce tras los cuales se arriesga a cruzar con un hueco pequeño.</summary>
    public float PacienciaS { get; }

    public string Tipo => Agresividad switch
    {
        <= -1.2f => "temeroso",
        <= -0.4f => "prudente",
        < 0.4f => "normal",
        < 1.2f => "agresivo",
        _ => "temerario",
    };

    /// <summary>Clase del vehículo que conduce.</summary>
    public ClaseVehiculo Clase { get; }

    /// <summary>Velocidad máxima del vehículo (m/s), aunque el límite sea mayor: camiones 90 km/h y autobuses 100 km/h.</summary>
    public float VelocidadMaximaMs { get; } = float.PositiveInfinity;

    public Conductor(Aleatorio azar, ContextoSimulacion contexto, ClaseVehiculo clase = ClaseVehiculo.Coche)
    {
        _azar = azar;
        _condiciones = contexto.Condiciones;
        var config = contexto.Config;
        Clase = clase;

        float sorteo = azar.Randf();
        Agresividad = sorteo < ProbTemeroso ? azar.RandfRange(-2f, -1.2f)
                    : sorteo > 1f - ProbTemerario ? azar.RandfRange(1.2f, 2f)
                    : Mathf.Clamp(azar.Randfn(0f, 0.4f), -1.15f, 1.15f);
        float a = Agresividad;

        // Respecto al límite de la vía: los más agresivos hasta un 20 % más, los más calmados hasta un 15 % menos.
        ExcesoLimite = Mathf.Clamp(0.03f + 0.085f * a, -FactorMinimo, FactorMaximo);
        TiempoSeparacionSeco = Mathf.Max(0.6f, 1.5f - 0.5f * a);
        DistanciaMinimaSeco = Mathf.Max(0.8f, 2.0f - 0.8f * a);
        // Parado en una cola, arranca cuando el hueco con el de delante es algo mayor que la distancia que deja en parado: un 10 % más
        // el agresivo, un 30 % el normal y hasta un 60 % el prudente (cada vez con algo de azar, que a veces lo deja por debajo).
        FactorArranque = Mathf.Clamp(1.3f - 0.12f * a, 1.05f, 1.6f);
        // De 0 a 50 km/h con la calle libre: unos 5 s el más agresivo, unos 6 s el normal y unos 10 s el más tranquilo (de 30 a 75 m).
        AceleracionMaxSeco = Mathf.Clamp(2.6f + 0.5f * a, 1.6f, 3.3f);
        FrenadaSuave = Mathf.Clamp(1.15f + 0.17f * a, 0.8f, 1.5f);
        FrenadaComodaSeco = Mathf.Clamp(2.5f + 0.25f * a, 2.0f, 3.0f);
        FrenadaFuerteSeco = Mathf.Clamp(5.0f + 0.5f * a, 4.0f, 6.0f);
        FrenadaEmergenciaSeco = Mathf.Clamp(8.0f + 0.5f * a, 7.0f, 9.0f);
        // Aceleración lateral aceptada en las curvas: unos 3,5 m/s² (0,35 g) en un conductor normal, como se mide en rotondas y giros
        // (el diseño usa un rozamiento lateral de 0,3 a 0,4). Da unos 30 km/h en una rotonda de 20 m de radio y 17 km/h en un giro cerrado de 6 m.
        AceleracionLateralSeco = Mathf.Max(1.5f, config.AceleracionLateralBase + 0.5f * a);
        AceleracionLateralRotondaSeco = Mathf.Max(2.0f, config.AceleracionLateralBaseRotonda + 0.9f * a);
        TiempoReaccionSeco = Mathf.Max(0.2f, 0.8f - 0.4f * a + azar.Randfn(0f, 0.1f));
        HuecoCriticoBaseSeco = Mathf.Clamp(2.0f * (1f - 0.3f * a), 0.8f, 3.4f);
        PacienciaS = 25f * Mathf.Clamp(1f - 0.3f * a, 0.6f, 2.2f);
        Cortesia = Mathf.Clamp(0.5f - 0.3f * a, 0f, 1f);
        UmbralCambioCarril = Mathf.Clamp(0.3f - 0.12f * a, 0.05f, 0.7f);
        FrenadaSeguridadCambio = Mathf.Clamp(2.5f + 0.5f * a, 2.0f, 4.0f);
        UsaNavegador = azar.Randf() < config.ProbabilidadNavegador;

        // Camiones y autobuses: arrancan y frenan mucho más despacio (y los autobuses aún más suave, por los pasajeros), toman las curvas
        // más despacio, dejan más distancia y piden más hueco para cruzar (tardan más en despejar el cruce), y no pasan de 90 o
        // 100 km/h ni en la autovía. Las motos arrancan y frenan más fuerte y se arriman más.
        switch (clase)
        {
            case ClaseVehiculo.Camion:
                VelocidadMaximaMs = 90f / 3.6f;
                ExcesoLimite = Mathf.Min(ExcesoLimite, 0.05f);
                AceleracionMaxSeco = Mathf.Clamp(0.8f + 0.1f * a, 0.6f, 1.0f);
                FrenadaSuave = Mathf.Clamp(0.8f + 0.1f * a, 0.6f, 1.0f);
                FrenadaComodaSeco = Mathf.Clamp(1.5f + 0.15f * a, 1.2f, 1.8f);
                FrenadaFuerteSeco = Mathf.Clamp(3.0f + 0.25f * a, 2.5f, 3.5f);
                FrenadaEmergenciaSeco = Mathf.Clamp(5.2f + 0.4f * a, 4.5f, 6.0f);
                AceleracionLateralSeco *= 0.65f;
                AceleracionLateralRotondaSeco *= 0.6f;
                TiempoSeparacionSeco *= 1.3f;
                DistanciaMinimaSeco += 1f;
                HuecoCriticoBaseSeco *= 1.35f;
                break;
            case ClaseVehiculo.Autobus:
                VelocidadMaximaMs = 100f / 3.6f;
                ExcesoLimite = Mathf.Min(ExcesoLimite, 0f);
                AceleracionMaxSeco = Mathf.Clamp(1.0f + 0.1f * a, 0.8f, 1.2f);
                FrenadaSuave = Mathf.Clamp(0.8f + 0.1f * a, 0.6f, 1.0f);
                FrenadaComodaSeco = Mathf.Clamp(1.5f + 0.15f * a, 1.3f, 1.8f);
                FrenadaFuerteSeco = Mathf.Clamp(3.0f + 0.2f * a, 2.5f, 3.5f);
                FrenadaEmergenciaSeco = Mathf.Clamp(5.5f + 0.3f * a, 5.0f, 6.0f);
                AceleracionLateralSeco *= 0.55f;
                AceleracionLateralRotondaSeco *= 0.5f;
                TiempoSeparacionSeco *= 1.25f;
                DistanciaMinimaSeco += 1f;
                HuecoCriticoBaseSeco *= 1.35f;
                break;
            case ClaseVehiculo.Moto:
                AceleracionMaxSeco = Mathf.Clamp(4.0f + 0.6f * a, 3.0f, 5.0f);
                FrenadaComodaSeco = Mathf.Clamp(3.0f + 0.25f * a, 2.5f, 3.5f);
                FrenadaFuerteSeco = Mathf.Clamp(6.0f + 0.5f * a, 5.0f, 7.0f);
                FrenadaEmergenciaSeco = Mathf.Clamp(9.0f + 0.5f * a, 8.0f, 10.0f);
                AceleracionLateralSeco *= 1.2f;
                AceleracionLateralRotondaSeco *= 1.15f;
                TiempoSeparacionSeco *= 0.9f;
                DistanciaMinimaSeco *= 0.6f;
                HuecoCriticoBaseSeco *= 0.85f;
                break;
        }
    }

    /// <summary>Variación lenta de la velocidad y la separación (cada conductor se desvía de su media).</summary>
    public void Actualizar(float dt)
    {
        _ruidoVelocidad = Ou(_ruidoVelocidad, dt, tau: 20f, sigma: 0.05f);
        _ruidoSeparacion = Ou(_ruidoSeparacion, dt, tau: 10f, sigma: 0.15f);
    }

    private float Ou(float x, float dt, float tau, float sigma) =>
        x - x / tau * dt + sigma * Mathf.Sqrt(2f * dt / tau) * _azar.Randfn(0f, 1f);

    /// <summary>Velocidad respecto al límite: nunca menos de un 15 % por debajo ni más de un 20 % por encima.</summary>
    public const float FactorMinimo = 0.15f, FactorMaximo = 0.20f;

    /// <summary>Velocidad a la que quiere ir en una vía con ese límite, en m/s.</summary>
    public float VelocidadDeseada(float limiteMs)
    {
        float ruido = 1f + _ruidoVelocidad;
        // Cada conductor circula en torno al límite de la vía (también en autovía), algo por encima o por debajo según su carácter y con
        // una pequeña variación lenta, siempre entre el 85 % y el 120 % del límite.
        return Mathf.Min(limiteMs * Mathf.Clamp((1f + ExcesoLimite) * ruido, 1f - FactorMinimo, 1f + FactorMaximo), VelocidadMaximaMs) * _condiciones.FactorVelocidad;
    }

    public float SeparacionActual => TiempoSeparacion * Mathf.Max(0.5f, 1f + _ruidoSeparacion);

    /// <summary>Velocidad media que espera llevar en una vía con ese límite, sin ruido: para calcular rutas.</summary>
    public float VelocidadParaRuta(float limiteMs) => Mathf.Min(limiteMs * (1f + ExcesoLimite), VelocidadMaximaMs) * _condiciones.FactorVelocidad;

    /// <summary>Cuánto le cuesta a este conductor ir por esa categoría de vía (1 = lo normal). Los temerosos rehúyen la autovía.</summary>
    public float FactorCosteVia(CategoriaVia categoria) =>
        categoria != CategoriaVia.Autovia ? 1f
        : Agresividad <= -1.2f ? 1.8f
        : Agresividad <= -0.4f ? 1.2f
        : 1f;

    /// <summary>Velocidad máxima que acepta en una curva de ese radio, en m/s.</summary>
    public float VelocidadEnCurva(float radio, bool rotonda = false) =>
        float.IsInfinity(radio) ? float.PositiveInfinity : Mathf.Sqrt((rotonda ? AceleracionLateralRotonda : AceleracionLateral) * radio);

    /// <summary>Tiempo de reacción de esta vez (varía algo cada vez que arranca).</summary>
    public float ReaccionAhora() => Mathf.Max(0.15f, TiempoReaccion * (1f + _azar.Randfn(0f, 0.2f)));

    /// <summary>
    /// Hueco que exige ahora. Se relaja hasta un 40 % tras 30 s esperando: nadie espera eternamente.
    /// </summary>
    public float HuecoCritico(float esperaS)
    {
        float relajacion = 1f - 0.4f * Mathf.Clamp(esperaS / 30f, 0f, 1f);
        return Mathf.Max(0.6f, HuecoCriticoBase * Mathf.Max(0.6f, 1f + _ruidoSeparacion) * relajacion);
    }
}

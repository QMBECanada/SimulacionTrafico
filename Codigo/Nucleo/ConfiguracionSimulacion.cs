using System;
using System.Collections.Generic;
using Godot;

namespace SimulacionTrafico.Nucleo;

/// <summary>
/// Opciones de una simulación: se fijan al empezar (línea de órdenes o pantalla inicial) y no cambian durante ella.
/// Es inmutable: cada cambio da una configuración nueva (<c>with</c> o <see cref="ConArgumento"/>).
/// </summary>
public sealed record ConfiguracionSimulacion
{
    // --- Flota y tráfico con el exterior ---
    public float ProporcionMotos { get; init; } = 0.09f;
    public float ProporcionCamiones { get; init; } = 0.05f;
    /// <summary>Proporción de la flota que vive fuera y entra a trabajar.</summary>
    public float ProporcionDeFuera { get; init; } = 0.15f;
    /// <summary>De los trabajadores de la ciudad, los que trabajan fuera.</summary>
    public float ProporcionTrabajaFuera { get; init; } = 0.05f;
    /// <summary>Proporción de la flota que solo pasa por la ciudad.</summary>
    public float ProporcionTransito { get; init; } = 0.05f;
    /// <summary>De los camiones de reparto, los que tienen su base fuera.</summary>
    public float ProporcionCamionesDeFuera { get; init; } = 0.3f;
    /// <summary>Si hay autobuses de línea (<c>--sinautobuses</c> los quita).</summary>
    public bool Autobuses { get; init; } = true;

    // --- Entorno ---
    /// <summary>Si los conductores miran a su alrededor para no chocar (para comparar: <c>--sinpercepcion</c>).</summary>
    public bool Percepcion { get; init; } = true;
    /// <summary>Si hay peatones en los pasos de cebra (<c>--sinpeatones</c>).</summary>
    public bool Peatones { get; init; } = true;
    /// <summary>Llueve durante todo el estudio (<c>--lluvia</c> o la casilla de la pantalla inicial).</summary>
    public bool Lluvia { get; init; }

    // --- Conducción ---
    /// <summary>Aceleración lateral aceptada por un conductor normal en los giros de los cruces (m/s²): da unos 13 km/h en un giro cerrado de 6 m.</summary>
    public float AceleracionLateralBase { get; init; } = 3.0f;
    /// <summary>
    /// Y en las rotondas, donde los accesos se abren y se circula más rápido: 3,5 m/s² (0,35 g, rozamiento lateral de diseño de 0,3 a 0,4) dan
    /// unos 30 km/h en un anillo de 20 m de radio.
    /// </summary>
    public float AceleracionLateralBaseRotonda { get; init; } = 3.5f;
    /// <summary>Proporción de conductores que usan navegador (0 a 1).</summary>
    public float ProbabilidadNavegador { get; init; } = 0.6f;
    /// <summary>Margen con que se planifica la frenada antes de una curva (multiplica la frenada cómoda).</summary>
    public float FactorPlanFrenada { get; init; } = 1.0f;

    // --- Semáforos y mapa ---
    /// <summary>Semáforos accionados por los coches que llegan; si no, tiempos fijos (<c>--semaforos=fijos</c>).</summary>
    public bool SemaforosAdaptativos { get; init; } = true;
    /// <summary>Fases propias de giro a la izquierda (<c>--sinfasegiro</c> las quita).</summary>
    public bool FasesGiro { get; init; } = true;
    /// <summary>Onda verde en las arterias (<c>--sinonda</c> la quita).</summary>
    public bool OndaVerde { get; init; } = true;
    /// <summary>Radio mínimo (m) con que se calcula la velocidad en los giros de los cruces.</summary>
    public float RadioMinimoGiro { get; init; } = 10f;
    /// <summary>Multiplica las plazas de aparcamiento de cada lugar.</summary>
    public float FactorPlazas { get; init; } = 1f;
    /// <summary>Multiplica la probabilidad de rotonda al generar ciudades.</summary>
    public float FactorRotondas { get; init; } = 1f;

    // --- Rendimiento ---
    /// <summary>Hilos para decidir los coches en paralelo (0 = los de por defecto). No cambia el resultado (<c>--hilos=N</c>).</summary>
    public int Hilos { get; init; }

    /// <summary>
    /// Por defecto, como mucho <see cref="HilosPorDefecto"/>: cada paso es muy corto y con más hilos se pierde en repartir lo que se gana
    /// (medido: de 3 a 8 hilos tarda lo mismo). Además, los lotes ejecutan varias simulaciones a la vez.
    /// </summary>
    public int HilosEfectivos => Hilos > 0 ? Hilos : Math.Min(System.Environment.ProcessorCount, HilosPorDefecto);
    public const int HilosPorDefecto = 6;

    // --- Guardias ---
    /// <summary>Los guardias pueden ordenar pasar aunque no quepa (<c>--sinforzado</c>).</summary>
    public bool GuardiaForzado { get; init; } = true;
    /// <summary>Los guardias pueden mandar dar la vuelta (<c>--sinvuelta</c>).</summary>
    public bool GuardiaVuelta { get; init; } = true;
    /// <summary>Los guardias pueden mandar pasar por el carril contrario (<c>--sinopuesta</c>).</summary>
    public bool GuardiaOpuesta { get; init; } = true;

    // --- Accidentes ---
    /// <summary>Segundos que quedan parados los coches de un accidente.</summary>
    public float DuracionAccidenteS { get; init; } = 300f;
    /// <summary>Solape mínimo (m) entre dos coches para que cuente como choque.</summary>
    public float ProfundidadMinimaChoque { get; init; } = 0.7f;
    /// <summary>Velocidad relativa mínima (m/s) para que cuente como choque (en la línea de órdenes, en km/h).</summary>
    public float VelocidadRelativaMinimaChoque { get; init; } = 8f;

    private static readonly Dictionary<string, Func<ConfiguracionSimulacion, ConfiguracionSimulacion>> Casillas = new()
    {
        ["--sinpeatones"] = c => c with { Peatones = false },
        ["--lluvia"] = c => c with { Lluvia = true },
        ["--sinautobuses"] = c => c with { Autobuses = false },
        ["--sinpercepcion"] = c => c with { Percepcion = false },
        ["--semaforos=fijos"] = c => c with { SemaforosAdaptativos = false },
        ["--semaforos=adaptativos"] = c => c with { SemaforosAdaptativos = true },
        ["--sinfasegiro"] = c => c with { FasesGiro = false },
        ["--sinonda"] = c => c with { OndaVerde = false },
        ["--sinopuesta"] = c => c with { GuardiaOpuesta = false },
        ["--sinforzado"] = c => c with { GuardiaForzado = false },
        ["--sinvuelta"] = c => c with { GuardiaVuelta = false },
    };

    private static readonly (string prefijo, Func<ConfiguracionSimulacion, float, ConfiguracionSimulacion> aplicar)[] Numericas =
    {
        ("--de-fuera=", (c, v) => c with { ProporcionDeFuera = v }),
        ("--trabajan-fuera=", (c, v) => c with { ProporcionTrabajaFuera = v }),
        ("--de-paso=", (c, v) => c with { ProporcionTransito = v }),
        ("--motos=", (c, v) => c with { ProporcionMotos = v }),
        ("--camiones=", (c, v) => c with { ProporcionCamiones = v }),
        ("--radio-giro=", (c, v) => c with { RadioMinimoGiro = v }),
        ("--plan-frenada=", (c, v) => c with { FactorPlanFrenada = v }),
        ("--lateral=", (c, v) => c with { AceleracionLateralBase = v }),
        ("--lateral-rotonda=", (c, v) => c with { AceleracionLateralBaseRotonda = v }),
        ("--navegador=", (c, v) => c with { ProbabilidadNavegador = v }),
        ("--plazas=", (c, v) => c with { FactorPlazas = v }),
        ("--rotondas=", (c, v) => c with { FactorRotondas = v }),
        ("--accidente-s=", (c, v) => c with { DuracionAccidenteS = v }),
        ("--choque-vel=", (c, v) => c with { VelocidadRelativaMinimaChoque = v / 3.6f }),
        ("--choque-solape=", (c, v) => c with { ProfundidadMinimaChoque = v }),
        ("--hilos=", (c, v) => c with { Hilos = (int)v }),
    };

    /// <summary>La configuración con este argumento de la línea de órdenes aplicado, o null si no es una opción de la simulación.</summary>
    public ConfiguracionSimulacion? ConArgumento(string argumento)
    {
        if (Casillas.TryGetValue(argumento, out var casilla)) return casilla(this);
        foreach (var (prefijo, aplicar) in Numericas)
            if (argumento.StartsWith(prefijo, StringComparison.Ordinal)) return aplicar(this, argumento[prefijo.Length..].ToFloat());
        return null;
    }
}

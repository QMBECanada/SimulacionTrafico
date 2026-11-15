using SimulacionTrafico.Nucleo;
using SimulacionTrafico.Trafico;

namespace SimulacionTrafico.Vehiculos;

/// <summary>
/// Lo que comparten todos los vehículos de una simulación: sus opciones, las condiciones del momento y lo que la simulación prepara
/// en cada paso para los conductores. Cada simulación tiene el suyo (nada es global).
/// </summary>
public sealed class ContextoSimulacion
{
    public ContextoSimulacion(ConfiguracionSimulacion config)
    {
        Config = config;
        Condiciones = new Condiciones(config.Lluvia);
    }

    public ConfiguracionSimulacion Config { get; }

    /// <summary>Lluvia (todo el estudio) y noche (la actualiza la simulación en cada paso).</summary>
    public Condiciones Condiciones { get; }

    /// <summary>Lo que ven todos los conductores (lo rellena la simulación en cada paso; null sin percepción).</summary>
    public Percepcion? Entorno { get; set; }

    /// <summary>True en los pasos en que los conductores vuelven a mirar a su alrededor.</summary>
    public bool PercibirAhora { get; set; } = true;

    /// <summary>Anotar por qué calles pasa cada vehículo y cuándo (solo con un estudio abierto).</summary>
    public bool RegistrarRecorridos { get; set; }
}

namespace SimulacionTrafico.Vehiculos;

/// <summary>
/// Condiciones de conducción del momento: lluvia (para todo el estudio) y noche (según la hora y el sol). Cambian lo que hace cada
/// conductor respecto a su forma de conducir en seco y de día:
/// <list type="bullet">
/// <item>Lluvia: la frenada de emergencia baja a 4,0-6,5 m/s² (valores reales en mojado), las frenadas fuerte y normal un 25 % y un 10 %,
/// la velocidad deseada un 10 %, la aceleración lateral aceptada en las curvas un 20 %; la separación con el de delante sube un 30 %,
/// la distancia en parado un 20 %, el hueco que se pide para cruzar un 15 % y el tiempo de reacción 0,1 s.</item>
/// <item>Noche (calles iluminadas): velocidad deseada un 5 % menos, separación un 10 % más, hueco para cruzar un 10 % más y
/// reacción 0,1 s más lenta.</item>
/// </list>
/// Los efectos se combinan.
/// </summary>
public sealed class Condiciones
{
    public Condiciones(bool lluvia) => Lluvia = lluvia;

    /// <summary>Llueve durante todo el estudio (<c>--lluvia</c> o la casilla de la pantalla inicial).</summary>
    public bool Lluvia { get; }

    /// <summary>Es de noche ahora (lo actualiza la simulación en cada paso).</summary>
    public bool Noche { get; set; }

    public float FactorVelocidad => (Lluvia ? 0.90f : 1f) * (Noche ? 0.95f : 1f);
    public float FactorSeparacion => (Lluvia ? 1.30f : 1f) * (Noche ? 1.10f : 1f);
    public float FactorDistanciaParado => Lluvia ? 1.20f : 1f;
    public float FactorHueco => (Lluvia ? 1.15f : 1f) * (Noche ? 1.10f : 1f);
    public float FactorLateral => Lluvia ? 0.80f : 1f;
    public float FactorFrenadaComoda => Lluvia ? 0.90f : 1f;
    public float FactorFrenadaFuerte => Lluvia ? 0.75f : 1f;
    public float FactorAceleracion => Lluvia ? 0.90f : 1f;
    public float ReaccionExtra => (Lluvia ? 0.1f : 0f) + (Noche ? 0.1f : 0f);

    /// <summary>
    /// Frenada de emergencia: en seco, la del conductor. En mojado, la de un coche (7-9 m/s² en seco) pasa a la misma posición dentro de
    /// 4,0-6,5; la de motos, camiones y autobuses se reduce en la misma proporción media (un 32 %).
    /// </summary>
    public float FrenadaEmergencia(float enSeco, ClaseVehiculo clase) =>
        !Lluvia ? enSeco : clase == ClaseVehiculo.Coche ? 4.0f + (enSeco - 7.0f) / 2.0f * 2.5f : enSeco * 0.68f;

    /// <summary>Gente a pie respecto a un día seco.</summary>
    public float FactorPeatones => Lluvia ? 0.7f : 1f;
}

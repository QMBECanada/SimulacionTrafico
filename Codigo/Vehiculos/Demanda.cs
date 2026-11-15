using Godot;
using SimulacionTrafico.Nucleo;

namespace SimulacionTrafico.Vehiculos;

/// <summary>
/// Cuántos viajes empiezan cada hora del día, como porcentaje de la flota (sin contar los autobuses). El usuario indica un porcentaje
/// por hora; el sistema lo varía al azar hasta un 15 % arriba o abajo. Se fija al empezar, así que con la misma semilla es siempre igual.
/// Quién sale y adónde va lo deciden las rutinas de los conductores; la demanda solo marca el ritmo: si a una hora acaban su estancia
/// más conductores de los que tocan, esperan; si acaban menos, salen antes de tiempo los siguientes de la cola.
/// </summary>
public sealed class Demanda
{
    /// <summary>
    /// Porcentaje de la flota que empieza un viaje en cada hora, por defecto: muy poco de noche, punta de mañana (8 h), mediodía y
    /// punta de tarde (17-18 h). Suma unos 3 viajes por vehículo y día, como en una ciudad real.
    /// </summary>
    public static readonly float[] CurvaPorDefecto =
    {
        //  0   1   2   3   4   5   6   7   8   9  10  11
            1,  0,  0,  0,  1,  2,  8, 21, 28, 16, 13, 14,
        // 12  13  14  15  16  17  18  19  20  21  22  23
           18, 20, 16, 17, 22, 28, 26, 18, 12,  8,  5,  3,
    };

    /// <summary>Variación al azar de cada hora, en tanto por uno (arriba o abajo).</summary>
    public const float Variacion = 0.15f;

    /// <summary>Hora de inicio de la simulación, en segundos desde la medianoche.</summary>
    public double HoraInicioS { get; }

    /// <summary>Lo que indicó el usuario y lo que se aplica (con la variación aleatoria), por hora.</summary>
    public float[] Indicado { get; }
    public float[] Aplicado { get; }

    public Demanda(float[] porcentajePorHora, double horaInicioS, Aleatorio azar, bool variar = true)
    {
        HoraInicioS = horaInicioS;
        Indicado = (float[])porcentajePorHora.Clone();
        Aplicado = new float[24];
        for (int h = 0; h < 24; h++)
            Aplicado[h] = Mathf.Max(0f, Indicado[h] * (variar ? 1f + azar.RandfRange(-Variacion, Variacion) : 1f));
    }

    public static Demanda Constante(float porcentaje, double horaInicioS, Aleatorio azar, bool variar = false)
    {
        var horas = new float[24];
        System.Array.Fill(horas, porcentaje);
        return new Demanda(horas, horaInicioS, azar, variar);
    }

    /// <summary>Segundo del día (0 a 86400) al cabo de <paramref name="tiempoSimulado"/> segundos de simulación.</summary>
    public double HoraDelDiaS(double tiempoSimulado) => (HoraInicioS + tiempoSimulado) % 86400.0;

    public int Hora(double tiempoSimulado) => (int)(HoraDelDiaS(tiempoSimulado) / 3600.0);

    /// <summary>Viajes por hora que deben empezar ahora en una flota de ese tamaño.</summary>
    public double ViajesPorHora(double tiempoSimulado, int flota) => Aplicado[Hora(tiempoSimulado)] / 100.0 * flota;
}

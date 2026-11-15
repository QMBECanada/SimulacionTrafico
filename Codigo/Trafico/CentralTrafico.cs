using System;
using System.Collections.Generic;
using SimulacionTrafico.Mapa;

namespace SimulacionTrafico.Trafico;

/// <summary>
/// Centro de información del tráfico. Los vehículos se monitorizan a sí mismos e informan: por qué calle van y a qué velocidad, y si
/// han tenido un accidente que corta su carril (y cuándo queda despejado). La central solo registra esos avisos y los ofrece a quien
/// los consulte (los conductores con navegador, al calcular su ruta); no da órdenes a nadie.
/// </summary>
public sealed class CentralTrafico
{
    private readonly Grafo _grafo;

    // Por calle y sentido: hasta cuándo están cortados todos sus carriles (el mínimo de lo que dicen los avisos de sus carriles).
    // Sin ningún carril cortado vale -infinito.
    private readonly double[] _cortadaHasta;

    /// <summary>Retenciones: congestión de cada calle y sentido a partir de las velocidades que informan los vehículos.</summary>
    public DetectorAtascos Atascos { get; }

    /// <summary>Instante de la última revisión de los avisos de accidente (se consulta con él si una calle está cortada).</summary>
    public double Ahora { get; private set; }

    public CentralTrafico(Grafo grafo, DetectorAtascos atascos)
    {
        _grafo = grafo;
        Atascos = atascos;
        _cortadaHasta = new double[grafo.Tramos.Count * 2];
        Array.Fill(_cortadaHasta, double.NegativeInfinity);
    }

    private static int Estado(Tramo t, bool ida) => t.Id * 2 + (ida ? 0 : 1);

    // ---- Avisos de los vehículos ----

    /// <summary>Un vehículo informa de por dónde va y a qué velocidad (cada pocos segundos).</summary>
    public void InformarMarcha(Carril carril, float velocidad) => Atascos.Anotar(carril, velocidad);

    /// <summary>Un vehículo accidentado avisa de que esos carriles quedan cortados (cada carril dice hasta cuándo).</summary>
    public void AvisarCorte(IEnumerable<Carril> carriles)
    {
        foreach (var l in carriles) Recalcular(l.Tramo, l.Ida);
    }

    /// <summary>Los vehículos de un accidente avisan de que reanudan la marcha: sus carriles quedan despejados.</summary>
    public void AvisarDespejado(IEnumerable<Carril> carriles)
    {
        foreach (var l in carriles) Recalcular(l.Tramo, l.Ida);
    }

    /// <summary>Revisa los avisos en este instante (las calles cortadas se consultan con él).</summary>
    public void Revisar(double ahora) => Ahora = ahora;

    // ---- Consultas ----

    /// <summary>True si todos los carriles de esa calle y sentido están cortados ahora.</summary>
    public bool Cortada(int estado) => Ahora < _cortadaHasta[estado];

    private void Recalcular(Tramo t, bool ida)
    {
        double hasta = double.PositiveInfinity;
        bool alguno = false;
        foreach (var l in t.Carriles)
        {
            if (l.Ida != ida) continue;
            alguno = true;
            if (l.CortadoHasta < hasta) hasta = l.CortadoHasta;
        }
        _cortadaHasta[Estado(t, ida)] = alguno ? hasta : double.NegativeInfinity;
    }
}

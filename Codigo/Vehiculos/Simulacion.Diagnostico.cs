using System.Collections.Generic;
using System.Linq;
using Godot;
using SimulacionTrafico.Nucleo;
using SimulacionTrafico.Mapa;
using SimulacionTrafico.Datos;
using SimulacionTrafico.Trafico;

namespace SimulacionTrafico.Vehiculos;

// Simulación: medidas de diagnóstico (arranques desde parado, frenadas, velocidad respecto al límite).
public partial class Simulacion : Node
{
    // --- Diagnóstico: aceleración desde parado con la calle libre delante (para compararla con datos reales) ---
    public bool DiagnosticoAceleracion { get; set; }
    private readonly Dictionary<Coche, double> _arranques = new();
    private readonly HashSet<Coche> _parados = new();
    public readonly Dictionary<int, List<float>> PerfilArranque = new() { [1] = new(), [3] = new(), [5] = new(), [7] = new() };

    /// <summary>Diagnóstico: frenadas usadas (pasos de 0,1 s por franja de deceleración) y velocidad respecto al límite de los que circulan sin estorbos.</summary>
    public readonly SortedDictionary<float, long> Frenadas = new() { [0.8f] = 0, [1.5f] = 0, [2.0f] = 0, [3.0f] = 0, [4.0f] = 0, [6.0f] = 0, [7.0f] = 0, [9.01f] = 0 };
    public readonly List<float> VelocidadSobreLimite = new();
    /// <summary>Diagnóstico: segundos entre que arranca el de delante y arranca el de detrás, en una cola parada.</summary>
    public readonly List<float> RetrasoArranque = new();
    public readonly List<(float vLider, float hueco, string motivo, float retraso)> ArranquesEnCola = new();

    public readonly Dictionary<string, long> EstadosMarcha = new();

    private void MedirArranques()
    {
        double t = TiempoSimulado;
        foreach (var c in Coches)
        {
            var em = c.EstadoMarcha();
            EstadosMarcha[em] = EstadosMarcha.GetValueOrDefault(em) + 1;
            if (c.A < -0.05f)
                foreach (var tope in Frenadas.Keys)
                    if (-c.A <= tope) { Frenadas[tope]++; break; }
            if (c.Via is Carril lc && c.Hueco > 80f && c.S > 50f && lc.Longitud - c.S > 70f && System.Math.Abs(c.A) < 0.2f && c.V > 2f)
                VelocidadSobreLimite.Add(c.V / lc.LimiteMs);
            if (c.V < 0.1f) { _parados.Add(c); _arranques.Remove(c); continue; }
            if (_parados.Contains(c))
            {
                c.UltimoArranque = t;
                if (c.BloqueadorEsLider && c.Bloqueador is { } lider && t - lider.UltimoArranque < 15.0) { RetrasoArranque.Add((float)(t - lider.UltimoArranque)); ArranquesEnCola.Add((lider.V, c.Hueco, c.Motivo ?? "-", (float)(t - lider.UltimoArranque))); }
            }
            if (_parados.Remove(c) && c.Hueco > 40f) _arranques[c] = t;
            if (_arranques.TryGetValue(c, out double ini))
            {
                if (c.Hueco < 25f) { _arranques.Remove(c); continue; }
                foreach (var (s, lista) in PerfilArranque)
                    if (System.Math.Abs(t - ini - s) < Paso / 2) lista.Add(c.V * 3.6f);
                if (t - ini > 7.5) _arranques.Remove(c);
            }
        }
    }
}

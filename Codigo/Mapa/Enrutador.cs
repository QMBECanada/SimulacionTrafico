using System.Collections.Generic;
using System.Diagnostics;
using Godot;
using SimulacionTrafico.Nucleo;
using SimulacionTrafico.Vehiculos;

namespace SimulacionTrafico.Mapa;

/// <summary>
/// Busca el camino más rápido (A*) entre una calle y la puerta de un lugar. Cada coche lo llama para planificar
/// su ruta, con el coste que corresponde a su conductor: cada uno valora distinto la autovía, por ejemplo.
/// El mapa que consulta es el mismo para todos y no cambia.
///
/// El grafo de búsqueda va por calles con sentido ("estados"): llegar a una calle desde otra equivale a girar en
/// el cruce, así que se pueden penalizar los giros y los cruces con semáforo, y no hay cambios de sentido.
/// La heurística es la cota ALT de <see cref="CotasRuta"/>, que nunca sobreestima: la ruta es siempre la más rápida para ese conductor.
/// </summary>
public sealed class Enrutador
{
    private readonly Grafo _grafo;
    private readonly CotasRuta _cotas;
    private readonly CotasRuta.Busqueda _busqueda = new();   // referencias activas de la búsqueda en curso
    private float _descuentoMeta;   // de la calle de la meta solo se cuenta la mitad (ver Buscar)
    private readonly float[] _coste;
    private readonly int[] _previo;
    private readonly int[] _version;   // en qué búsqueda se escribió el coste de cada estado
    private readonly int[] _cerrado;   // en qué búsqueda se cerró cada estado
    private int _numBusqueda;
    private readonly PriorityQueue<int, float> _abierta = new();

    /// <summary>
    /// Factor de congestión de cada calle y sentido (1 = sin tráfico), que ven los conductores con navegador.
    /// Lo mantiene el detector de atascos.
    /// </summary>
    public float[]? Congestion { get; set; }

    private int _reRutasPaso;
    private const int MaxReRutasPorPaso = 6;

    /// <summary>
    /// Central de tráfico a la que los vehículos avisan de los accidentes: las calles con todos sus carriles cortados no se usan
    /// para las rutas (null: no se sabe de ninguno).
    /// </summary>
    public Trafico.CentralTrafico? Central { get; set; }

    /// <summary>Se llama al empezar cada paso de simulación.</summary>
    public void NuevoPaso() => _reRutasPaso = 0;

    /// <summary>Un coche pide recalcular su ruta por un atasco: se permite un número limitado por paso.</summary>
    public bool ReservarReRuta() => _reRutasPaso++ < MaxReRutasPorPaso;

    /// <summary>True si la calle a la que va a entrar desde ese cruce está muy congestionada.</summary>
    public bool HayAtasco(Tramo siguiente, Nodo desde) =>
        Congestion != null && Congestion[Estado(siguiente, siguiente.A == desde)] >= 2.5f;

    public int ReRutasPorAtasco { get; set; }

    /// <summary>Sin cotas (heurística 0, un Dijkstra): solo para comprobar que las cotas no cambian la ruta.</summary>
    public bool SinCotas { get; init; }

    /// <summary>Tiempo estimado de la última ruta encontrada, en segundos, con el coste de su conductor.</summary>
    public float UltimoCoste { get; private set; }

    // Estadísticas
    public int Calculos { get; private set; }
    public int Fallos { get; private set; }

    /// <summary>Calles exploradas en total: medida de trabajo que no depende de la velocidad del ordenador.</summary>
    public long Expansiones { get; private set; }
    public double MsTotales { get; private set; }

    public Enrutador(Grafo grafo)
    {
        _grafo = grafo;
        _cotas = new CotasRuta(grafo);
        int n = grafo.Tramos.Count * 2 + 1;
        _coste = new float[n];
        _previo = new int[n];
        _version = new int[n];
        _cerrado = new int[n];
    }

    private static int Estado(Tramo t, bool ida) => CotasRuta.Estado(t, ida);
    private int EstadoMeta => _grafo.Tramos.Count * 2;

    /// <summary>
    /// Calles que debe recorrer, después de la actual, para llegar a la puerta del destino (vacío si ya está en
    /// la calle de la puerta y todavía no la ha pasado). Null si no hay camino.
    /// </summary>
    /// <param name="fraccion">Posición actual dentro del carril, de 0 (principio) a 1 (final).</param>
    public List<Tramo>? Calcular(Tramo tramo, bool ida, float fraccion, Lugar destino, Conductor conductor)
    {
        long t0 = Stopwatch.GetTimestamp();
        var ruta = Buscar(tramo, ida, fraccion, destino, conductor);
        MsTotales += Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        Calculos++;
        if (ruta == null) Fallos++;
        return ruta;
    }

    private List<Tramo>? Buscar(Tramo tramo, bool ida, float fraccion, Lugar destino, Conductor c)
    {
        int inicio = Estado(tramo, ida);
        var acceso = destino.Acceso;
        int meta = Estado(acceso.Tramo, acceso.Ida);
        int virtualMeta = EstadoMeta;

        // Ya está en la calle de la puerta y todavía no la ha pasado.
        if (inicio == meta && fraccion < destino.SAcceso / acceso.Longitud - 0.02f) return new List<Tramo>();

        _numBusqueda++;
        _abierta.Clear();
        _cotas.PrepararBusqueda(inicio, meta, _busqueda);
        _descuentoMeta = 0.5f * _cotas.CotaRecorrido(meta);
        Escribir(inicio, 0f, -1);
        _abierta.Enqueue(inicio, Heuristica(inicio));

        while (_abierta.TryDequeue(out int e, out _))
        {
            if (e == virtualMeta)
            {
                UltimoCoste = _coste[virtualMeta];
                return Reconstruir(inicio, virtualMeta, acceso.Tramo);
            }
            if (_cerrado[e] == _numBusqueda) continue;
            _cerrado[e] = _numBusqueda;
            Expansiones++;

            float g = _coste[e];

            // Calles que se pueden tomar en el cruce del final (sin cambios de sentido ni giros prohibidos), con lo que se pierde al girar.
            for (int k = _cotas.InicioSalidas(e), finSalidas = _cotas.FinSalidas(e); k < finSalidas; k++)
            {
                int s = _cotas.DestinoSalida(k);
                var f = _grafo.Tramos[s >> 1];
                bool idaF = (s & 1) == 0;
                // Calle cortada por un accidente: se evita, salvo si es la de la puerta del destino (no hay otra forma de llegar: se va y se
                // espera a que la abran, como haría cualquiera).
                if (Central != null && s != meta && Central.Cortada(s)) continue;
                float tramoF = Recorrido(f, idaF, c);
                float coste = g + _cotas.RetrasoSalida(k);

                if (s == meta)
                {
                    // Llegar a la calle de la puerta: se cuenta hasta la mitad de la calle, donde suele estar.
                    float total = coste + tramoF * 0.5f;
                    if (_version[virtualMeta] != _numBusqueda || total < _coste[virtualMeta])
                    {
                        Escribir(virtualMeta, total, e);
                        _abierta.Enqueue(virtualMeta, total);
                    }
                    continue;
                }

                float nuevo = coste + tramoF;
                if (_version[s] != _numBusqueda || nuevo < _coste[s])
                {
                    Escribir(s, nuevo, e);
                    _abierta.Enqueue(s, nuevo + Heuristica(s));
                }
            }
        }
        return null;
    }

    private void Escribir(int estado, float coste, int previo)
    {
        _coste[estado] = coste;
        _previo[estado] = previo;
        _version[estado] = _numBusqueda;
    }

    private List<Tramo> Reconstruir(int inicio, int virtualMeta, Tramo calleMeta)
    {
        var ruta = new List<Tramo> { calleMeta };
        for (int e = _previo[virtualMeta]; e != inicio; e = _previo[e])
            ruta.Add(_grafo.Tramos[e >> 1]);
        ruta.Reverse();
        return ruta;
    }

    /// <summary>
    /// Cota inferior de lo que falta desde el estado hasta la puerta: la ALT hasta entrar en la calle de la meta, menos la mitad de esa
    /// calle (de la que solo se cuenta la mitad). Nunca sobreestima, así que la primera vez que se saca la meta de la cola es la mejor ruta.
    /// </summary>
    private float Heuristica(int estado) => SinCotas ? 0f : Mathf.Max(0f, _cotas.Cota(estado, _busqueda) - _descuentoMeta);

    /// <summary>Segundos que tarda este conductor en recorrer la calle, con su preferencia por cada tipo de vía.</summary>
    private float Recorrido(Tramo t, bool ida, Conductor c)
    {
        float v = Mathf.Max(c.VelocidadParaRuta(t.LimiteKmh * Escala.KmhAMs), CotasRuta.VelocidadMinimaRuta);
        float congestion = c.UsaNavegador && Congestion != null ? Congestion[Estado(t, ida)] : 1f;
        return t.Eje.Longitud / v * c.FactorCosteVia(t.Categoria) * congestion;
    }
}

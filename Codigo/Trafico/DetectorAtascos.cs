using System.Collections.Generic;
using System.Linq;
using Godot;
using SimulacionTrafico.Mapa;
using SimulacionTrafico.Vehiculos;

namespace SimulacionTrafico.Trafico;

/// <summary>Parámetros que definen cuándo hay un atasco (todos configurables).</summary>
public sealed class ParametrosAtasco
{
    /// <summary>Velocidad media por debajo de la cual el tramo va lento, en km/h.</summary>
    public float VelocidadMaximaKmh { get; set; } = 10f;
    /// <summary>Segundos seguidos yendo lento para que cuente como atasco.</summary>
    public float DuracionMinimaS { get; set; } = 60f;
    /// <summary>Coches mínimos en el tramo.</summary>
    public int CochesMinimos { get; set; } = 4;
    /// <summary>El atasco termina cuando la velocidad media supera esto, en km/h...</summary>
    public float VelocidadFinKmh { get; set; } = 20f;
    /// <summary>...durante estos segundos.</summary>
    public float DuracionFinS { get; set; } = 30f;
}

/// <summary>Un atasco en una calle y sentido, desde que empieza hasta que se disuelve.</summary>
public sealed class Atasco
{
    public Tramo Tramo { get; }
    public bool Ida { get; }
    public double Inicio { get; }
    public double? Fin { get; set; }
    public int MaxCoches { get; set; }

    /// <summary>Cruce al que llega la calle atascada: donde está la cabeza de la cola.</summary>
    public Nodo NodoFinal => Ida ? Tramo.B : Tramo.A;

    public Atasco(Tramo tramo, bool ida, double inicio, int coches)
    {
        Tramo = tramo;
        Ida = ida;
        Inicio = inicio;
        MaxCoches = coches;
    }
}

/// <summary>
/// Reúne las velocidades que informan los vehículos (a través de <see cref="CentralTrafico"/>) y saca la velocidad media de cada calle y sentido. Un atasco empieza cuando, con al menos N coches, la velocidad
/// media se queda por debajo de X durante Y segundos, y solo cuenta mientras el semáforo del final de la calle no esté
/// en rojo (una cola ante un rojo es normal). Termina cuando la velocidad media supera Z durante W segundos.
/// También mantiene un factor de congestión por calle, que es lo que ven los conductores que usan navegador.
/// </summary>
public sealed class DetectorAtascos
{
    private readonly Grafo _grafo;
    private readonly float[] _suma;
    private readonly int[] _cuenta;
    private readonly float[] _lento;      // segundos seguidos yendo lento sin semáforo en rojo
    private readonly float[] _libre;      // segundos seguidos yendo con soltura (para cerrar un atasco)
    private readonly Atasco?[] _activo;
    private readonly bool[] _enListaCongestion;
    private readonly List<int> _tocados = new();
    private readonly List<int> _activos = new();
    private readonly List<int> _congestionados = new();

    public ParametrosAtasco Parametros { get; }

    /// <summary>Cuánto más tarda ahora un coche en recorrer cada calle y sentido que sin tráfico (1 = sin tráfico).</summary>
    public float[] Congestion { get; }

    public List<Atasco> Historial { get; } = new();
    public IEnumerable<Atasco> Activos => _activos.Select(e => _activo[e]!);
    public int NumActivos => _activos.Count;

    public DetectorAtascos(Grafo grafo, ParametrosAtasco? parametros = null)
    {
        _grafo = grafo;
        Parametros = parametros ?? new ParametrosAtasco();
        int n = grafo.Tramos.Count * 2;
        _suma = new float[n];
        _cuenta = new int[n];
        _lento = new float[n];
        _libre = new float[n];
        _activo = new Atasco?[n];
        _enListaCongestion = new bool[n];
        Congestion = new float[n];
        System.Array.Fill(Congestion, 1f);
    }

    public static int Id(Tramo t, bool ida) => t.Id * 2 + (ida ? 0 : 1);

    /// <summary>True si esa calle y sentido tienen ahora un atasco.</summary>
    public bool EstaActivo(Tramo t, bool ida) => _activo[Id(t, ida)] != null;

    /// <summary>Un vehículo informa de que va por ese carril a esa velocidad.</summary>
    public void Anotar(Carril l, float velocidad)
    {
        int e = Id(l.Tramo, l.Ida);
        if (_cuenta[e] == 0) _tocados.Add(e);
        _cuenta[e]++;
        _suma[e] += velocidad;
    }

    /// <summary>Cada pocos segundos, cuando ya han informado todos los vehículos: saca las medias y abre o cierra atascos.</summary>
    public void Cerrar(double tiempo, float dt)
    {
        foreach (int e in _tocados) Evaluar(e, tiempo, dt);

        // Calles atascadas que ahora no tienen ningún coche: se vacían, así que se cierran poco a poco.
        for (int i = _activos.Count - 1; i >= 0; i--)
        {
            int e = _activos[i];
            if (_cuenta[e] == 0) Evaluar(e, tiempo, dt);
        }

        // La congestión conocida se va olvidando en las calles por las que ya no pasa nadie.
        for (int i = _congestionados.Count - 1; i >= 0; i--)
        {
            int e = _congestionados[i];
            if (_cuenta[e] == 0)
            {
                Congestion[e] = Mathf.Lerp(Congestion[e], 1f, 0.4f);
                if (Congestion[e] < 1.05f)
                {
                    Congestion[e] = 1f;
                    _enListaCongestion[e] = false;
                    _congestionados[i] = _congestionados[^1];
                    _congestionados.RemoveAt(_congestionados.Count - 1);
                }
            }
        }

        foreach (int e in _tocados)
        {
            _cuenta[e] = 0;
            _suma[e] = 0f;
        }
        _tocados.Clear();
    }

    private void Evaluar(int e, double tiempo, float dt)
    {
        var tramo = _grafo.Tramos[e >> 1];
        bool ida = (e & 1) == 0;
        int n = _cuenta[e];
        float vMedia = n > 0 ? _suma[e] / n : float.PositiveInfinity;
        float kmh = vMedia * 3.6f;

        // Lo que ven los conductores con navegador.
        if (n >= 3)
        {
            float vLibre = tramo.LimiteKmh / 3.6f * 0.7f;
            float factor = Mathf.Clamp(vLibre / Mathf.Max(vMedia, 1.5f), 1f, 6f);
            Congestion[e] = Mathf.Lerp(Congestion[e], factor, 0.5f);
            if (Congestion[e] > 1.05f && !_enListaCongestion[e])
            {
                _enListaCongestion[e] = true;
                _congestionados.Add(e);
            }
        }

        var atasco = _activo[e];
        if (atasco == null)
        {
            bool lento = n >= Parametros.CochesMinimos && kmh < Parametros.VelocidadMaximaKmh;
            if (lento && !FinEnRojo(tramo, ida, tiempo))
            {
                _lento[e] += dt;
                if (_lento[e] >= Parametros.DuracionMinimaS)
                {
                    atasco = new Atasco(tramo, ida, tiempo - _lento[e], n);
                    _activo[e] = atasco;
                    _activos.Add(e);
                    Historial.Add(atasco);
                    _libre[e] = 0f;
                }
            }
            else
            {
                _lento[e] = 0f; // vuelve a empezar: no iba lento, o hay un semáforo en rojo
            }
        }
        else
        {
            atasco.MaxCoches = System.Math.Max(atasco.MaxCoches, n);
            if (n == 0 || kmh > Parametros.VelocidadFinKmh)
            {
                _libre[e] += dt;
                if (_libre[e] >= Parametros.DuracionFinS)
                {
                    atasco.Fin = tiempo;
                    _activo[e] = null;
                    _lento[e] = 0f;
                    int pos = _activos.IndexOf(e);
                    _activos[pos] = _activos[^1];
                    _activos.RemoveAt(_activos.Count - 1);
                }
            }
            else
            {
                _libre[e] = 0f;
            }
        }
    }

    /// <summary>True si el cruce al que llega la calle tiene un semáforo y su luz es roja para este sentido.</summary>
    private static bool FinEnRojo(Tramo tramo, bool ida, double tiempo)
    {
        var nodo = ida ? tramo.B : tramo.A;
        var carril = tramo.Carriles.FirstOrDefault(l => l.Ida == ida);
        if (carril == null) return false;
        // Con guardia, su orden de alto cuenta como el rojo.
        if (nodo.Guardia != null) return nodo.Guardia.OrdenPara(carril) == OrdenGuardia.Parar;
        return nodo.Control == TipoControl.Semaforo && nodo.EstadoPara(carril, tiempo) == EstadoSemaforo.Rojo;
    }
}

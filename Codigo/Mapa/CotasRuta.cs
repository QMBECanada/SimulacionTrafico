using System;
using System.Collections.Generic;
using Godot;
using SimulacionTrafico.Nucleo;
using SimulacionTrafico.Vehiculos;

namespace SimulacionTrafico.Mapa;

/// <summary>
/// Lo que el enrutador calcula una vez por ciudad: qué calles se pueden tomar desde cada calle con sentido («estado») y cuánto se pierde
/// en ese cruce, y unas cotas inferiores del tiempo entre dos estados (ALT: A* con puntos de referencia y desigualdad triangular).
///
/// La cota de cada calle es su longitud a la velocidad más alta a la que va nadie (el límite más <see cref="Conductor.FactorMaximo"/>):
/// la congestión, la lluvia, la noche y la preferencia por un tipo de vía solo encarecen, así que nunca sobreestima. Con las distancias
/// mínimas desde y hasta unos pocos puntos de referencia, d(s, m) ≥ max(d(L, m) − d(L, s), d(s, L) − d(m, L)) para cada referencia L.
/// </summary>
public sealed class CotasRuta
{
    public const int Referencias = 32;

    private readonly Grafo _grafo;
    public int Estados { get; }

    // Salidas de cada estado, en el mismo orden en que el enrutador recorría las calles del cruce: [_inicio[e], _inicio[e + 1]).
    private readonly int[] _inicio;
    private readonly int[] _destino;
    private readonly float[] _retraso;

    private readonly float[] _cota;    // cota inferior del tiempo en recorrer la calle de cada estado
    private readonly float[] _desde;   // [estado * Referencias + r]: tiempo mínimo desde la referencia r hasta el estado
    private readonly float[] _hasta;   // [estado * Referencias + r]: tiempo mínimo desde el estado hasta la referencia r
    private readonly int _numReferencias;

    public static int Estado(Tramo t, bool ida) => t.Id * 2 + (ida ? 0 : 1);

    public CotasRuta(Grafo grafo)
    {
        _grafo = grafo;
        Estados = grafo.Tramos.Count * 2;
        _cota = new float[Estados];
        foreach (var t in grafo.Tramos)
        {
            float vMax = Mathf.Max(t.LimiteKmh * Escala.KmhAMs * (1f + Conductor.FactorMaximo), VelocidadMinimaRuta);
            _cota[Estado(t, true)] = _cota[Estado(t, false)] = t.Eje.Longitud / vMax;
        }

        var inicio = new int[Estados + 1];
        var destino = new List<int>();
        var retraso = new List<float>();
        for (int e = 0; e < Estados; e++)
        {
            inicio[e] = destino.Count;
            var t = grafo.Tramos[e >> 1];
            bool idaE = (e & 1) == 0;
            var fin = idaE ? t.B : t.A;
            foreach (var f in fin.Tramos)
            {
                if (f == t) continue;                    // sin cambios de sentido
                if (fin.GiroProhibido(t, f)) continue;   // giro prohibido en este cruce
                bool idaF = f.A == fin;                  // sale de este nodo
                if ((idaF ? f.CarrilesIda : f.CarrilesVuelta) == 0) continue;
                destino.Add(Estado(f, idaF));
                retraso.Add(RetrasoCruce(fin, t, idaE, f, idaF));
            }
        }
        inicio[Estados] = destino.Count;
        _inicio = inicio;
        _destino = destino.ToArray();
        _retraso = retraso.ToArray();

        _numReferencias = Math.Min(Referencias, Estados);
        _desde = new float[Estados * Referencias];
        _hasta = new float[Estados * Referencias];
        ElegirYMedirReferencias();
    }

    /// <summary>Velocidad mínima que se usa al calcular rutas (la misma que el enrutador).</summary>
    public const float VelocidadMinimaRuta = 3f;

    public int InicioSalidas(int estado) => _inicio[estado];
    public int FinSalidas(int estado) => _inicio[estado + 1];
    public int DestinoSalida(int salida) => _destino[salida];
    public float RetrasoSalida(int salida) => _retraso[salida];
    public float CotaRecorrido(int estado) => _cota[estado];

    /// <summary>Segundos que se pierden en un cruce: por la señal que haya y por girar. No depende del conductor.</summary>
    public static float RetrasoCruce(Nodo n, Tramo entra, bool idaEntra, Tramo sale, bool idaSale)
    {
        float retraso = 0f;
        bool tienePrioridad = n.EsPrioritario(entra);
        switch (n.Control)
        {
            case TipoControl.Semaforo: retraso += 12f; break;
            case TipoControl.Stop when !tienePrioridad: retraso += 5f; break;
            case TipoControl.Ceda when !tienePrioridad: retraso += 2f; break;
            case TipoControl.Rotonda when !tienePrioridad: retraso += 3f; break;
        }

        var dirEntra = idaEntra ? entra.Eje.Direccion(entra.Eje.Longitud) : -entra.Eje.Direccion(0f);
        var dirSale = idaSale ? sale.Eje.Direccion(0f) : -sale.Eje.Direccion(sale.Eje.Longitud);
        switch (Conexion.CalcularGiro(dirEntra, dirSale))
        {
            case TipoGiro.Izquierda: retraso += n.Control == TipoControl.Semaforo ? 8f : 4f; break;
            case TipoGiro.Derecha: retraso += 1f; break;
        }
        return retraso;
    }

    /// <summary>Referencias que se usan en cada búsqueda: las que mejor acotan el viaje desde su origen.</summary>
    public const int ReferenciasActivas = 4;

    /// <summary>
    /// Prepara una búsqueda de <paramref name="estadoOrigen"/> a <paramref name="estadoMeta"/>: elige las referencias que mejor acotan ese
    /// viaje y copia en <paramref name="busqueda"/> lo que hace falta de ellas (ver <see cref="Cota"/>). Usar solo algunas sigue sin
    /// sobreestimar (es el máximo de menos cotas) y ahorra trabajo en cada calle explorada.
    /// </summary>
    public void PrepararBusqueda(int estadoOrigen, int estadoMeta, Busqueda busqueda)
    {
        busqueda.Activas = 0;
        Span<float> valor = stackalloc float[Referencias];
        for (int r = 0; r < _numReferencias; r++) valor[r] = CotaCon(r, estadoOrigen, estadoMeta);
        for (int k = 0; k < Math.Min(ReferenciasActivas, _numReferencias); k++)
        {
            int mejor = -1;
            for (int r = 0; r < _numReferencias; r++)
                if (valor[r] >= 0f && (mejor < 0 || valor[r] > valor[mejor])) mejor = r;
            if (mejor < 0) break;
            valor[mejor] = -1f;
            busqueda.Referencia[busqueda.Activas] = mejor;
            busqueda.DesdeMeta[busqueda.Activas] = _desde[estadoMeta * Referencias + mejor];
            busqueda.HastaMeta[busqueda.Activas] = _hasta[estadoMeta * Referencias + mejor];
            busqueda.Activas++;
        }
    }

    /// <summary>Lo que necesita cada búsqueda de sus referencias activas (una por enrutador: no se comparte entre hilos).</summary>
    public sealed class Busqueda
    {
        public int Activas;
        public readonly int[] Referencia = new int[ReferenciasActivas];
        public readonly float[] DesdeMeta = new float[ReferenciasActivas];
        public readonly float[] HastaMeta = new float[ReferenciasActivas];
    }

    private float CotaCon(int r, int s, int m)
    {
        float mejor = 0f;
        float ds = _desde[s * Referencias + r], dm = _desde[m * Referencias + r];
        if (!float.IsInfinity(ds) && !float.IsInfinity(dm) && dm - ds > mejor) mejor = dm - ds;
        float hs = _hasta[s * Referencias + r], hm = _hasta[m * Referencias + r];
        if (!float.IsInfinity(hs) && !float.IsInfinity(hm) && hs - hm > mejor) mejor = hs - hm;
        return mejor;
    }

    /// <summary>
    /// Cota inferior del tiempo desde el estado <paramref name="s"/> hasta entrar en la meta (contando entera la calle de la meta),
    /// con las referencias activas de la búsqueda.
    /// </summary>
    public float Cota(int s, Busqueda busqueda)
    {
        float mejor = 0f;
        int b = s * Referencias;
        for (int k = 0; k < busqueda.Activas; k++)
        {
            int r = busqueda.Referencia[k];
            float ds = _desde[b + r], dm = busqueda.DesdeMeta[k];
            if (!float.IsInfinity(ds) && !float.IsInfinity(dm) && dm - ds > mejor) mejor = dm - ds;
            float hs = _hasta[b + r], hm = busqueda.HastaMeta[k];
            if (!float.IsInfinity(hs) && !float.IsInfinity(hm) && hs - hm > mejor) mejor = hs - hm;
        }
        return mejor;
    }

    /// <summary>
    /// Elige las referencias de una en una, cada una en el estado más alejado de las ya elegidas (la primera, el más alejado del
    /// primer estado), y mide los tiempos mínimos desde y hasta cada una.
    /// </summary>
    private void ElegirYMedirReferencias()
    {
        if (_numReferencias == 0) return;
        // Salidas al revés (quién llega a cada estado) para medir «hasta la referencia».
        var cuentaEntradas = new int[Estados + 1];
        foreach (var d in _destino) cuentaEntradas[d + 1]++;
        for (int i = 0; i < Estados; i++) cuentaEntradas[i + 1] += cuentaEntradas[i];
        var inicioEntradas = (int[])cuentaEntradas.Clone();
        var origenEntrada = new int[_destino.Length];
        var retrasoEntrada = new float[_destino.Length];
        var llenado = (int[])cuentaEntradas.Clone();
        for (int e = 0; e < Estados; e++)
            for (int k = _inicio[e]; k < _inicio[e + 1]; k++)
            {
                int pos = llenado[_destino[k]]++;
                origenEntrada[pos] = e;
                retrasoEntrada[pos] = _retraso[k];
            }

        var minimo = new float[Estados];
        Array.Fill(minimo, float.PositiveInfinity);
        var dist = new float[Estados];
        var distAtras = new float[Estados];
        int candidata = 0;
        for (int r = 0; r < _numReferencias; r++)
        {
            Dijkstra(candidata, adelante: true, dist, inicioEntradas, origenEntrada, retrasoEntrada);
            Dijkstra(candidata, adelante: false, distAtras, inicioEntradas, origenEntrada, retrasoEntrada);
            for (int s = 0; s < Estados; s++)
            {
                _desde[s * Referencias + r] = dist[s];
                _hasta[s * Referencias + r] = distAtras[s];
                float d = Math.Min(dist[s], distAtras[s]);
                if (d < minimo[s]) minimo[s] = d;
            }
            // Siguiente: la más alejada (en tiempo, sin contar las inalcanzables) de todas las elegidas.
            float lejos = -1f;
            for (int s = 0; s < Estados; s++)
                if (!float.IsInfinity(minimo[s]) && minimo[s] > lejos) { lejos = minimo[s]; candidata = s; }
        }
    }

    /// <summary>Tiempos mínimos desde (adelante) o hasta (atrás) un estado. Entrar en un estado cuesta el cruce más su calle.</summary>
    private void Dijkstra(int origen, bool adelante, float[] dist, int[] inicioEntradas, int[] origenEntrada, float[] retrasoEntrada)
    {
        Array.Fill(dist, float.PositiveInfinity);
        dist[origen] = 0f;
        var cola = new PriorityQueue<int, float>();
        cola.Enqueue(origen, 0f);
        while (cola.TryDequeue(out int e, out float d))
        {
            if (d > dist[e]) continue;
            if (adelante)
            {
                for (int k = _inicio[e]; k < _inicio[e + 1]; k++)
                {
                    int f = _destino[k];
                    float nd = d + _retraso[k] + _cota[f];
                    if (nd < dist[f]) { dist[f] = nd; cola.Enqueue(f, nd); }
                }
            }
            else
            {
                // Hacia atrás: de e a quien llega a e. Ir de p a e cuesta el cruce más la calle de e.
                for (int k = inicioEntradas[e]; k < inicioEntradas[e + 1]; k++)
                {
                    int p = origenEntrada[k];
                    float nd = d + retrasoEntrada[k] + _cota[e];
                    if (nd < dist[p]) { dist[p] = nd; cola.Enqueue(p, nd); }
                }
            }
        }
    }
}

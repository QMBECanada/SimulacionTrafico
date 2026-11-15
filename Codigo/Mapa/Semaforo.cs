using System.Collections.Generic;
using System.Linq;
using Godot;

namespace SimulacionTrafico.Mapa;

public enum EstadoSemaforo { Verde, Ambar, Rojo }

/// <summary>
/// Semáforo de un cruce. Las aproximaciones se agrupan en dos grupos (las que llegan en horizontal y las que llegan en vertical) y el
/// semáforo pasa por una serie de fases. En cada grupo:
/// <list type="bullet">
/// <item>fase adelantada: solo una de las dos aproximaciones en verde, para que sus coches giren a la izquierda sin tráfico de frente;</item>
/// <item>fase común: las dos aproximaciones en verde (girar a la izquierda cediendo el paso al que viene de frente);</item>
/// <item>fase retrasada: solo la otra aproximación, para sus giros a la izquierda.</item>
/// </list>
/// Entre fases, las aproximaciones que pierden el verde pasan por el ámbar y el todo rojo; si ninguna lo pierde, hay 2 s de despeje para
/// que salgan del cruce los que estaban girando. Las fases de giro solo existen donde se puede girar a la izquierda; con el control
/// adaptativo solo se usan si hay alguien que quiere girar; con tiempos fijos, solo en las calles de dos carriles o más por sentido.
/// <para>
/// Onda verde: los semáforos de cada arteria se coordinan (ver <see cref="Coordinar"/>): comparten un ciclo de <see cref="CicloCoordinado"/>
/// segundos y cada uno abre la fase común de la arteria con el desfase justo para que un grupo de coches a unos 48 km/h los encuentre
/// en verde. Con el control adaptativo, esa fase se mantiene durante una ventana de cada ciclo y el resto del tiempo el semáforo sigue
/// siendo adaptativo.
/// </para>
/// </summary>
public sealed class Semaforo
{
    public float Verde0 { get; }
    public float Verde1 { get; }
    public float Ambar { get; }
    public float TodoRojo { get; }
    public float Desfase { get; }

    // Coches que han pasado en verde y en ámbar desde el último ciclo registrado, por calle.
    private readonly Dictionary<Tramo, int> _verde = new();
    private readonly Dictionary<Tramo, int> _ambar = new();

    /// <summary>Un coche de esa calle acaba de entrar en el cruce con la luz en ese estado.</summary>
    public void RegistrarPaso(Tramo calle, EstadoSemaforo estado)
    {
        var cuenta = estado == EstadoSemaforo.Verde ? _verde : estado == EstadoSemaforo.Ambar ? _ambar : null;
        if (cuenta != null) cuenta[calle] = cuenta.GetValueOrDefault(calle) + 1;
    }

    /// <summary>Devuelve los coches contados en verde y en ámbar para esa calle y empieza de nuevo.</summary>
    public (int verde, int ambar) TomarCuentas(Tramo calle)
    {
        int v = _verde.GetValueOrDefault(calle), a = _ambar.GetValueOrDefault(calle);
        _verde[calle] = 0;
        _ambar[calle] = 0;
        return (v, a);
    }

    public Semaforo(float verde0, float verde1, float desfase, float ambar = 3f, float todoRojo = 2f)
    {
        Verde0 = verde0;
        Verde1 = verde1;
        Desfase = desfase;
        Ambar = ambar;
        TodoRojo = todoRojo;
    }

    // ---- Aproximaciones y fases ----

    /// <summary>Aproximación de un carril que llega al cruce: 0 hacia el este, 1 hacia el oeste, 2 hacia el sur, 3 hacia el norte.</summary>
    public static int Aproximacion(Carril entrante) => entrante.Aproximacion;

    private sealed record Fase(int Mascara, int Grupo, bool Comun);

    /// <summary>Fases propias de giro a la izquierda (adelantada y retrasada). Para comparar, se pueden quitar (<c>--sinfasegiro</c>).</summary>
    private bool _fasesGiro = true;

    private const float VerdeFaseGiro = 7f;       // con tiempos fijos
    private const float VerdeMinimoGiro = 5f, VerdeMaximoGiro = 20f;
    private const float Despeje = 2f;

    private Nodo _nodo = null!;
    private List<Fase> _fases = new();
    private bool _preparado;

    /// <summary>Modo de control y fases de giro, según la configuración de la simulación; las fases se vuelven a preparar.</summary>
    public void Configurar(bool adaptativo, bool fasesGiro)
    {
        Adaptativo = adaptativo;
        _fasesGiro = fasesGiro;
        _preparado = false;
    }

    /// <summary>Prepara las fases según las calles del cruce. Lo llama el grafo al construirse.</summary>
    public void Vincular(Nodo nodo)
    {
        _nodo = nodo;
        _preparado = false;
    }

    /// <summary>Prepara las fases ya, si no lo están: antes de que los coches consulten las luces desde varios hilos a la vez.</summary>
    public void AsegurarPreparado()
    {
        if (!_preparado) Preparar();
    }

    private void Preparar()
    {
        _preparado = true;
        _fases = new List<Fase>();
        var aproximaciones = _nodo.Entrantes.GroupBy(Aproximacion).ToDictionary(g => g.Key, g => g.ToList());
        for (int grupo = 0; grupo < 2; grupo++)
        {
            int a = 2 * grupo, b = 2 * grupo + 1;
            int comun = (aproximaciones.ContainsKey(a) ? 1 << a : 0) | (aproximaciones.ContainsKey(b) ? 1 << b : 0);
            if (comun == 0) continue;
            bool Giro(int ap) => _fasesGiro && aproximaciones.TryGetValue(ap, out var carriles) && carriles.Any(l => PuedeGirarIzquierda(l))
                                 && (Adaptativo || carriles.Any(l => Grafo.CarrilesEnSentido(l) >= 2))
                                 && aproximaciones.ContainsKey(ap ^ 1);   // solo hace falta si hay tráfico de frente
            if (Giro(a)) _fases.Add(new Fase(1 << a, grupo, false));
            _fases.Add(new Fase(comun, grupo, true));
            if (Giro(b)) _fases.Add(new Fase(1 << b, grupo, false));
        }
        if (_fases.Count == 0) _fases.Add(new Fase(0, 0, true));
        PrepararTiempos();
    }

    private static bool PuedeGirarIzquierda(Carril l) =>
        Grafo.SalientesPermitidos(l).Any(o => Conexion.CalcularGiro(l.Eje.Direccion(l.Longitud), o.Eje.Direccion(0f)) == TipoGiro.Izquierda);

    private float Transicion(Fase de, Fase a) => (de.Mascara & ~a.Mascara) != 0 ? Ambar + TodoRojo : Despeje;

    private static EstadoSemaforo EstadoEnCambio(int bit, Fase de, Fase a, double transcurrido, float ambar)
    {
        bool antes = (de.Mascara & bit) != 0, despues = (a.Mascara & bit) != 0;
        if (antes && despues) return EstadoSemaforo.Verde;
        if (antes) return transcurrido < ambar ? EstadoSemaforo.Ambar : EstadoSemaforo.Rojo;
        return EstadoSemaforo.Rojo;
    }

    // ---- Tiempos fijos ----

    private float[] _verdes = System.Array.Empty<float>();   // verde de cada fase
    private float _ciclo;
    private float _desfaseEfectivo;

    private void PrepararTiempos()
    {
        _verdes = _fases.Select(f => f.Comun ? (f.Grupo == 0 ? Verde0 : Verde1) : VerdeFaseGiro).ToArray();
        float transiciones = 0f;
        for (int i = 0; i < _fases.Count; i++) transiciones += Transicion(_fases[i], _fases[(i + 1) % _fases.Count]);
        _desfaseEfectivo = Desfase;
        if (Coordinado)
        {
            // Ciclo común de la onda verde: las fases comunes se reparten lo que queda (55 % para la arteria).
            float fijo = transiciones + _fases.Where(f => !f.Comun).Sum(_ => VerdeFaseGiro);
            float queda = Mathf.Max(20f, CicloCoordinado - fijo);
            bool otroGrupo = _fases.Any(f => f.Comun && f.Grupo != GrupoCoordinado);
            for (int i = 0; i < _fases.Count; i++)
                if (_fases[i].Comun) _verdes[i] = !otroGrupo ? queda : _fases[i].Grupo == GrupoCoordinado ? 0.55f * queda : 0.45f * queda;
            // La fase común de la arteria debe empezar a la hora del desfase de la onda.
            float inicio = 0f;
            for (int i = 0; i < _fases.Count; i++)
            {
                if (_fases[i].Comun && _fases[i].Grupo == GrupoCoordinado) break;
                inicio += _verdes[i] + Transicion(_fases[i], _fases[(i + 1) % _fases.Count]);
            }
            _desfaseEfectivo = inicio - DesfaseOnda;
        }
        _ciclo = _verdes.Sum() + transiciones;
    }

    private EstadoSemaforo EstadoFijo(int bit, double tiempo)
    {
        float x = (float)(((tiempo + _desfaseEfectivo) % _ciclo + _ciclo) % _ciclo);
        for (int i = 0; i < _fases.Count; i++)
        {
            var f = _fases[i];
            var siguiente = _fases[(i + 1) % _fases.Count];
            if (x < _verdes[i]) return (f.Mascara & bit) != 0 ? EstadoSemaforo.Verde : EstadoSemaforo.Rojo;
            x -= _verdes[i];
            float tr = Transicion(f, siguiente);
            if (x < tr) return EstadoEnCambio(bit, f, siguiente, x, Ambar);
            x -= tr;
        }
        return EstadoSemaforo.Rojo;
    }

    // ---- Control adaptativo (accionado por el tráfico) ----

    /// <summary>
    /// Si es true (por defecto), los semáforos no siguen tiempos fijos: dan verde mientras siguen llegando coches que pueden pasar,
    /// lo cortan cuando no llega nadie o cuando los que esperan no caben al otro lado (calle de salida llena), y no lo dan a quien no
    /// lo necesita. Así, cuando una zona se colapsa, el semáforo deja de malgastar verde en las calles que no pueden avanzar.
    /// Es el control accionado de los semáforos reales con detectores (espiras en la calzada).
    /// </summary>
    public bool Adaptativo { get; private set; } = true;

    public const float VerdeMinimo = 7f;     // verde mínimo, para que arranquen los primeros de la cola
    public const float VerdeMaximo = 60f;    // nadie espera más de un verde máximo del otro grupo
    public const float Intervalo = 3f;       // si en 3 s no va a llegar ningún coche a la línea, el verde se corta
    public const float Deteccion = 60f;      // distancia a la línea a la que el detector ve los coches

    private int _actual = -1, _siguiente;
    private bool _cambiando;
    private double _inicioFase;
    private float _duracionCambio;

    /// <summary>Avanza el control adaptativo un paso. Solo se usa con <see cref="Adaptativo"/>.</summary>
    public void Actualizar(Nodo nodo, double tiempo)
    {
        if (!_preparado) Preparar();
        if (_actual < 0)
        {
            int grupo = (int)Desfase % 2;
            _actual = System.Math.Max(0, _fases.FindIndex(f => f.Comun && f.Grupo == grupo));
            _cambiando = false;
            _inicioFase = tiempo - (Desfase % VerdeMinimo);
        }
        double dur = tiempo - _inicioFase;
        if (_cambiando)
        {
            if (dur >= _duracionCambio)
            {
                _actual = _siguiente;
                _cambiando = false;
                _inicioFase = tiempo;
            }
            return;
        }

        var f = _fases[_actual];
        bool forzar = false;
        if (Coordinado)
        {
            double x = PosicionCiclo(tiempo);
            bool esArteria = f.Comun && f.Grupo == GrupoCoordinado;
            if (esArteria && x < VentanaCoordinada) return;                       // dentro de la ventana de la onda verde
            if (!esArteria && x >= CicloCoordinado - (Ambar + TodoRojo)) forzar = true;   // hay que cerrar para llegar a la ventana
        }
        int siguiente = forzar ? IndiceArteria() : SiguienteConDemanda(nodo);
        if (siguiente < 0 || siguiente == _actual) return;   // nadie espera en otra fase: sigue en verde
        float minimo = f.Comun ? VerdeMinimo : VerdeMinimoGiro, maximo = f.Comun ? VerdeMaximo : VerdeMaximoGiro;
        if (forzar || (dur >= minimo && (!SiguenLlegando(nodo, f) || SalidaLlena(nodo, f))) || dur >= maximo)
        {
            _siguiente = siguiente;
            _duracionCambio = Transicion(f, _fases[siguiente]);
            _cambiando = true;
            _inicioFase = tiempo;
        }
    }

    private int IndiceArteria() => _fases.FindIndex(f => f.Comun && f.Grupo == GrupoCoordinado);

    /// <summary>La primera fase, después de la actual, en la que hay alguien esperando que ahora no tiene verde.</summary>
    private int SiguienteConDemanda(Nodo nodo)
    {
        var actual = _fases[_actual];
        for (int k = 1; k < _fases.Count; k++)
        {
            int j = (_actual + k) % _fases.Count;
            var f = _fases[j];
            if (f.Comun ? HayDemanda(nodo, f.Mascara & ~actual.Mascara, false) : HayDemanda(nodo, f.Mascara, true)) return j;
        }
        return -1;
    }

    /// <summary>Quiere girar a la izquierda en este cruce.</summary>
    private static bool GiraIzquierda(Vehiculos.Coche c, Nodo nodo) =>
        c.Plan.Count > 1 && c.Plan[1] is Conexion cx && cx.Origen.Destino == nodo && cx.Giro == TipoGiro.Izquierda;

    /// <summary>Algún coche espera o se acerca a la línea en esas aproximaciones (si <paramref name="soloGiro"/>, solo los que van a girar a la izquierda).</summary>
    private static bool HayDemanda(Nodo nodo, int mascara, bool soloGiro)
    {
        if (mascara == 0) return false;
        foreach (var l in nodo.Entrantes)
        {
            if ((mascara & (1 << l.Aproximacion)) == 0) continue;
            for (int i = l.Coches.Count - 1; i >= 0; i--)
            {
                var c = l.Coches[i];
                if (l.Longitud - c.S > Deteccion) break;
                if (!soloGiro || GiraIzquierda(c, nodo)) return true;
            }
        }
        return false;
    }

    /// <summary>Algún coche de la fase va a llegar a la línea en los próximos segundos y puede pasar (su calle de salida no está llena).</summary>
    private static bool SiguenLlegando(Nodo nodo, Fase fase)
    {
        foreach (var l in nodo.Entrantes)
        {
            if ((fase.Mascara & (1 << l.Aproximacion)) == 0) continue;
            for (int i = l.Coches.Count - 1; i >= 0; i--)
            {
                var c = l.Coches[i];
                float distancia = l.Longitud - c.S - c.Longitud / 2f;
                if (distancia > Deteccion) break;
                if (c.Motivo is "no cabe al otro lado" or "carril cortado por un accidente") break;   // la cabeza no puede salir
                if (!fase.Comun && !GiraIzquierda(c, nodo)) continue;   // en la fase de giro solo cuentan los que giran
                if (distancia < 3f + Mathf.Max(c.V, 2f) * Intervalo) return true;
            }
        }
        return false;
    }

    /// <summary>Todas las cabezas de cola de la fase están paradas porque no caben al otro lado: dar verde aquí no sirve.</summary>
    private static bool SalidaLlena(Nodo nodo, Fase fase)
    {
        bool alguna = false;
        foreach (var l in nodo.Entrantes)
        {
            if ((fase.Mascara & (1 << l.Aproximacion)) == 0 || l.Coches.Count == 0) continue;
            var cabeza = l.Coches[^1];
            if (l.Longitud - cabeza.S > 15f) continue;
            if (cabeza.Motivo is not ("no cabe al otro lado" or "carril cortado por un accidente")) return false;
            alguna = true;
        }
        return alguna;
    }

    // ---- Onda verde ----

    public const float CicloCoordinado = 90f;
    public const float VentanaCoordinada = 32f;   // segundos de cada ciclo en que la arteria tiene verde seguro
    public const float VelocidadOnda = 48f / 3.6f;

    /// <summary>Si pertenece a una arteria con onda verde, el grupo de la arteria y el segundo del ciclo en que le toca abrir.</summary>
    public bool Coordinado { get; private set; }
    public int GrupoCoordinado { get; private set; }
    public float DesfaseOnda { get; private set; }

    private double PosicionCiclo(double tiempo) => ((tiempo - DesfaseOnda) % CicloCoordinado + CicloCoordinado) % CicloCoordinado;

    /// <summary>
    /// Coordina los semáforos de cada arteria (calles rectas de categoría arteria, incluida la avenida). Cada cruce con semáforo se asigna a
    /// la arteria más ancha que pasa por él. A lo largo de cada arteria, el desfase de cada semáforo es el tiempo que tarda en llegar a él,
    /// a <see cref="VelocidadOnda"/>, un coche que salió del primero; las arterias paralelas alternan el sentido de la onda, para que haya
    /// ondas en los dos sentidos por la ciudad.
    /// </summary>
    public static void Coordinar(Grafo grafo)
    {
        var lineas = new Dictionary<(bool horizontal, int coordenada), (int carriles, List<Nodo> nodos)>();
        foreach (var t in grafo.Tramos)
        {
            if (t.Categoria != CategoriaVia.Arteria || t.EsRotonda || t.EsRamal || t.Eje.Puntos.Length != 2) continue;
            bool h = Mathf.IsEqualApprox(t.A.Pos.Y, t.B.Pos.Y);
            if (!h && !Mathf.IsEqualApprox(t.A.Pos.X, t.B.Pos.X)) continue;
            var clave = (h, Mathf.RoundToInt(h ? t.A.Pos.Y : t.A.Pos.X));
            if (!lineas.TryGetValue(clave, out var linea)) lineas[clave] = linea = (0, new List<Nodo>());
            linea.nodos.Add(t.A);
            linea.nodos.Add(t.B);
            lineas[clave] = (System.Math.Max(linea.carriles, t.TotalCarriles), linea.nodos);
        }

        // Cada semáforo, para la arteria más ancha que pasa por él.
        var asignado = new Dictionary<Nodo, (bool horizontal, int coordenada)>();
        foreach (var (clave, (carriles, nodos)) in lineas.OrderByDescending(l => l.Value.carriles).ThenBy(l => l.Key.horizontal ? 0 : 1).ThenBy(l => l.Key.coordenada))
            foreach (var n in nodos)
                if (n.Semaforo != null && !asignado.ContainsKey(n)) asignado[n] = clave;

        foreach (var grupoLineas in lineas.Keys.GroupBy(k => k.horizontal))
        {
            int orden = 0;
            foreach (var clave in grupoLineas.OrderBy(k => k.coordenada))
            {
                var nodos = asignado.Where(a => a.Value == clave).Select(a => a.Key).ToList();
                if (nodos.Count < 2) { orden++; continue; }
                bool creciente = orden++ % 2 == 0;
                float Pos(Nodo n) => clave.horizontal ? n.Pos.X : n.Pos.Y;
                float primero = creciente ? nodos.Min(Pos) : nodos.Max(Pos);
                foreach (var n in nodos)
                {
                    var s = n.Semaforo!;
                    s.Coordinado = true;
                    s.GrupoCoordinado = clave.horizontal ? 0 : 1;
                    s.DesfaseOnda = Mathf.Abs(Pos(n) - primero) / VelocidadOnda % CicloCoordinado;
                    s._preparado = false;
                }
            }
        }
    }

    // ---- Estado ----

    /// <summary>Luz que ve un carril que llega al cruce.</summary>
    public EstadoSemaforo EstadoCarril(Carril entrante, double tiempo) => EstadoAproximacion(entrante.Aproximacion, tiempo);

    private EstadoSemaforo EstadoAproximacion(int aproximacion, double tiempo)
    {
        if (!_preparado) Preparar();
        int bit = 1 << aproximacion;
        if (!Adaptativo || _actual < 0) return EstadoFijo(bit, tiempo);
        var f = _fases[_actual];
        if (!_cambiando) return (f.Mascara & bit) != 0 ? EstadoSemaforo.Verde : EstadoSemaforo.Rojo;
        return EstadoEnCambio(bit, f, _fases[_siguiente], tiempo - _inicioFase, Ambar);
    }

    /// <summary>Luz de un grupo (0 horizontal, 1 vertical): verde si alguna de sus aproximaciones lo tiene; si no, ámbar si alguna lo tiene.</summary>
    public EstadoSemaforo Estado(int grupo, double tiempo)
    {
        var a = EstadoAproximacion(2 * grupo, tiempo);
        var b = EstadoAproximacion(2 * grupo + 1, tiempo);
        return a < b ? a : b;   // Verde < Ambar < Rojo
    }
}

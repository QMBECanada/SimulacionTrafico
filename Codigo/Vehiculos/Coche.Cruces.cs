using System.Collections.Generic;
using System.Linq;
using Godot;
using SimulacionTrafico.Mapa;
using SimulacionTrafico.Nucleo;
using SimulacionTrafico.Trafico;

namespace SimulacionTrafico.Vehiculos;

// Coche: lo que decide ante cada cruce (señales, preferencias, sitio al otro lado, otra salida, vuelta en U).
public sealed partial class Coche
{
    /// <summary>
    /// Decide si debe detenerse en la línea de detención: semáforo, stop, tráfico con prioridad o falta de
    /// espacio al otro lado. Cada coche lo decide por su cuenta con lo que ve.
    /// </summary>
    private bool DebeParar(Carril carril, Conexion m, float hueco, double tiempo)
    {
        var nodo = carril.Destino;
        float frenadaNecesaria = V * V / (2f * Mathf.Max(hueco, 0.1f));
        if (frenadaNecesaria > Conductor.FrenadaEmergencia) return false; // ni con una frenada de emergencia podría parar: sigue

        // El carril de salida está cortado por un accidente: no se entra, pase lo que pase con las señales o el guardia; se busca otra salida.
        if (m.Destino.EstaCortado(tiempo))
        {
            _alternativaPendiente = carril;
            Motivo = "carril cortado por un accidente";
            return true;
        }

        // Si hay un guardia, sus órdenes sustituyen a todas las señales y a las normas de preferencia.
        if (nodo.Guardia is { } guardia)
        {
            var orden = guardia.OrdenPara(carril);
            if (orden == OrdenGuardia.Parar)
            {
                Motivo = "guardia: alto";
                return true;
            }
            // Paso forzado: los primeros de la cola que llevan esperando mucho dan la vuelta en lugar de meterse en un cruce lleno.
            if (orden == OrdenGuardia.PasarForzado && _espera > 45f && _contexto.Config.GuardiaVuelta && !DestinoLibre(m))
                _vueltaPendiente = (carril, guardia);   // lo intenta al aplicar; con paso forzado no se detiene, dé la vuelta o no
            if (orden == OrdenGuardia.Pasar && !DestinoLibre(m))
            {
                _alternativaPendiente = carril;
                Motivo = "guardia: no cabe al otro lado";
                return true;
            }
            return false;
        }

        if (nodo.Control == TipoControl.Semaforo)
        {
            var estado = nodo.EstadoPara(carril, tiempo);
            if (estado == EstadoSemaforo.Rojo)
            {
                Motivo = "semáforo en rojo";
                return true;
            }
            if (estado == EstadoSemaforo.Ambar && frenadaNecesaria <= 1.5f * Conductor.FrenadaComoda)
            {
                Motivo = "semáforo en ámbar";
                return true;
            }
        }
        else if (nodo.Control == TipoControl.Stop && !nodo.EsPrioritario(carril.Tramo) && !_haParado)
        {
            // Ha hecho el alto si está parado junto a la línea (a la distancia que deja su conductor) o lleva ya unos segundos así.
            float distanciaParada = Mathf.Max(4f, DistanciaMinimaActual + 2f);
            if (V < 0.3f && (hueco < distanciaParada || _espera > 4f)) _haParado = true;
            else
            {
                Motivo = "stop";
                return true;
            }
        }

        // Peatones: nadie pasa por un paso de peatones con alguien cruzando (el de su calle, al entrar en el cruce, o el de la calle a la
        // que va). Sin semáforo, además, se cede el paso a los que esperan en la acera si se puede frenar con comodidad.
        if (_contexto.Config.Peatones && nodo.Pasos.Count > 0)
        {
            bool conSemaforo = nodo.Control == TipoControl.Semaforo && nodo.Guardia == null;
            bool ceder = !conSemaforo && frenadaNecesaria <= Conductor.FrenadaComoda;
            if ((nodo.PasoEn(carril.Tramo)?.Bloquea(carril.Eje.Puntos[^1], ceder) ?? false) ||
                (nodo.PasoEn(m.Destino.Tramo)?.Bloquea(m.Destino.Eje.Puntos[0], ceder) ?? false))
            {
                Motivo = "paso de peatones";
                return true;
            }
        }

        // Nadie entra en una rotonda que ya está muy ocupada: así el anillo no se llena y se bloquea.
        if (m.Destino.Tramo.Rotonda is { } rotonda && !m.Origen.Tramo.EsRotonda && rotonda.Ocupacion() > Rotonda.OcupacionMaxima)
        {
            Motivo = "rotonda llena";
            return true;
        }

        if (HayConflicto(m, nodo, hueco, tiempo))
        {
            Motivo = nodo.Control == TipoControl.Semaforo ? "cede el paso al girar" : "cede el paso";
            return true;
        }
        if (!DestinoLibre(m))
        {
            // Si lleva rato esperando porque la calle por la que quiere seguir está llena, prueba otra salida: así una fila de
            // coches cerrada en círculo (cada una esperando a la siguiente) no se queda bloqueada para siempre.
            _alternativaPendiente = carril;
            Motivo = "no cabe al otro lado";
            return true;
        }
        return false;
    }

    private double _ultimaAlternativa = double.NegativeInfinity;

    /// <summary>
    /// Tras esperar un buen rato ante una calle llena, elige otra salida del cruce que tenga sitio (y desde la que sigue habiendo ruta
    /// hasta su destino) y cambia su ruta. Como un conductor que, viendo que no avanza, gira por otra calle.
    /// </summary>
    private bool IntentarAlternativa(Carril carril)
    {
        // Con el carril de salida cortado por un accidente se busca otra salida enseguida; si solo está lleno, tras esperar un buen rato.
        bool cortado = Plan.Count > 1 && Plan[1] is Conexion actual && actual.Destino.EstaCortado(_tiempoActual);
        if (Destino == null || Plan.Count < 2 || _espera < (cortado ? 2f : 20f) || _tiempoActual - _ultimaAlternativa < (cortado ? 10.0 : 30.0)) return false;
        // Buscar rutas cuesta: se reparte con el mismo cupo por paso que los demás recálculos, y si no hay salida se espera un rato antes de volver a mirar.
        if (!_enrutador.ReservarReRuta()) return false;
        _ultimaAlternativa = _tiempoActual;

        var nodo = carril.Destino;
        Tramo? mejor = null;
        List<Tramo>? mejorResto = null;
        foreach (var t in nodo.Salientes.Where(l => l.Tramo != carril.Tramo).Select(l => l.Tramo).Distinct().ToList())
        {
            if (t == _tramoSiguiente || nodo.GiroProhibido(carril.Tramo, t)) continue;
            if (!Grafo.PermiteGiro(carril, _grafo.GiroHacia(carril, t))) continue;
            if (!DestinoLibre(_grafo.ObtenerConexion(carril, _grafo.CarrilDestino(carril, t)))) continue;
            var resto = _enrutador.Calcular(t, t.A == nodo, 0f, Destino, Conductor);
            if (resto == null) continue;
            if (mejorResto == null || resto.Count < mejorResto.Count)
            {
                mejor = t;
                mejorResto = resto;
            }
        }
        if (mejor == null || mejorResto == null) return false;

        ReRutas++;
        _ultimaRuta = _tiempoActual;
        _ultimaAlternativa = _tiempoActual;
        _ruta.Clear();
        _ruta.Enqueue(mejor);
        foreach (var t in mejorResto) _ruta.Enqueue(t);
        FijarPlan(carril, mejor);
        _espera = 0f;
        return true;
    }

    /// <summary>Hueco mínimo (s) con el que se arriesga a cruzar un coche que ha esperado demasiado.</summary>
    private const float HuecoForzadoS = 1.2f;

    /// <summary>
    /// Aceptación de huecos: ¿llegará algún coche con preferencia al punto de conflicto antes de que yo lo haya
    /// despejado más el margen que exige mi conductor?
    /// </summary>
    private bool HayConflicto(Conexion m, Nodo nodo, float hueco, double tiempo)
    {
        var conflictos = _grafo.ObtenerConflictos(m);
        if (conflictos.Count == 0) return false;

        int miRango = nodo.Rango(m);
        float margen = Conductor.HuecoCritico(_espera);
        // En el cruce no acelero sin límite: la curva y la calle de salida limitan mi velocidad (y por tanto cuánto tardo en despejar).
        float vMaxPropia = Mathf.Max(2f, Mathf.Min(Conductor.VelocidadDeseada(m.LimiteMs), Conductor.VelocidadEnCurva(m.RadioMinimo, EsDeRotonda(m))));

        // Tras esperar demasiado, se arriesga a cruzar con un hueco pequeño (el que viene frenará al verle dentro
        // del cruce). El agresivo lo hace antes (unos 17 s) y el temeroso mucho más tarde (unos 55 s).
        bool forzar = _espera > Conductor.PacienciaS;
        float HuecoForzado = HuecoForzadoS;

        // Con índice y no con foreach: recorrer una IReadOnlyList con foreach crea un enumerador en cada llamada (y esto se mira en cada paso).
        for (int k = 0; k < conflictos.Count; k++)
        {
            var c = conflictos[k];
            // Tiempo que tardaría en despejar ese punto (la cola del coche tiene que pasarlo), desde donde estoy.
            float distancia = Mathf.Max(hueco, 0f) + c.SPropio + Longitud;
            float tiempoPropio = TiempoEnRecorrer(distancia, V, Conductor.AceleracionMax, vMaxPropia);
            if (V < 0.5f) tiempoPropio += Conductor.TiempoReaccion;

            var otra = c.Otra;

            int rangoOtra = nodo.Rango(otra);

            // Coches que ya están dentro del cruce: los espero si aún no han pasado el punto de conflicto... salvo uno
            // parado, de menos prioridad que yo y que aún no ha llegado a ese punto: lo adelanto. Sin esto, en una rotonda
            // el que entra espera dentro del cruce a que se libere el anillo y los del anillo esperan a que salga él.
            foreach (var y in otra.Coches)
            {
                float frente = y.S + y.Longitud / 2f;
                if (y.S - y.Longitud / 2f > c.SOtra + 0.5f) continue; // ya lo despejó
                if (y.V < 0.3f && rangoOtra < miRango && c.SOtra - frente > 1f) continue;
                float tY = (c.SOtra - frente) / Mathf.Max(y.V, 2f);
                if (frente >= c.SOtra || tY < (forzar ? HuecoForzado : tiempoPropio + margen))
                {
                    _bloqueoCruce = y;
                    return true;
                }
            }

            // Coches que se acercan al cruce: solo cedo ante los que tienen más prioridad (o ante los que
            // empatan y llegaron antes a la línea; si llegaron a la vez, lo decide el sorteo).
            if (rangoOtra < miRango) continue;
            var carrilOtra = otra.Origen;
            if (nodo.Control == TipoControl.Semaforo && nodo.EstadoPara(carrilOtra, tiempo) == EstadoSemaforo.Rojo) continue;

            for (int i = carrilOtra.Coches.Count - 1; i >= 0; i--)
            {
                var y = carrilOtra.Coches[i];
                float dY = carrilOtra.Longitud - y.S;
                if (dY > Mathf.Max(VisionCruce, carrilOtra.LimiteMs * 7f)) break;
                if (y.Plan.Count < 2 || y.Plan[1] != otra) continue;

                // Un coche parado no llega al cruce: ni el que está atascado detrás de otro, ni el que tiene más
                // prioridad pero está esperando por otro motivo. Si luego arranca, me verá dentro del cruce y esperará.
                if (y.V < 0.5f && y._esLiderVisible && y._huecoVisible < 10f) continue;
                if (rangoOtra > miRango && y.V < 0.5f && y._motivoVisible != null) continue;

                if (rangoOtra == miRango && !TienePreferenciaSobreMi(y)) continue;
                float tLlegada = (dY - y.Longitud / 2f + c.SOtra) / Mathf.Max(y.V, 3f);
                if (tLlegada < (forzar ? HuecoForzado : tiempoPropio + margen))
                {
                    _bloqueoCruce = y;
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>Cruce al que llegará después de este (el de la calle de salida de su próximo giro), si lo sabe.</summary>
    public Nodo? NodoSiguiente => Plan.Count > 1 && Plan[1] is Conexion m ? m.Destino.Destino : null;

    /// <summary>True si, al girar como tiene previsto, cabría entero en el carril de salida (lo usa el guardia para dar paso).</summary>
    public bool CabeAlOtroLado() => Plan.Count > 1 && Plan[1] is Conexion m ? DestinoLibre(m) : true;

    /// <summary>
    /// Da la vuelta en U en el cruce (por orden de un guardia): sale por el carril contiguo de la misma calle en sentido
    /// contrario y calcula una ruta nueva desde ahí. Solo desde el carril más interior, en calles de doble sentido y si
    /// hay sitio al otro lado.
    /// </summary>
    private bool IntentarVuelta(Carril carril, Guardia guardia)
    {
        if (Destino == null || carril.Tramo.UnSentido || carril.Indice != 0 || Plan.Count < 2) return false;

        var opuesto = _grafo.CarrilDestino(carril, carril.Tramo);
        var m = _grafo.ObtenerConexion(carril, opuesto);
        if (!DestinoLibre(m)) return false;

        var nodo = carril.Destino;
        if (!nodo.Conexiones.Contains(m)) nodo.Conexiones.Add(m);

        Reenrutar(carril, carril.Tramo);   // la ruta nueva empieza por esta misma calle, en sentido contrario
        Plan.RemoveRange(1, Plan.Count - 1);
        Plan.Add(m);
        Plan.Add(opuesto);
        _tramoSiguiente = carril.Tramo;
        _indiceDeseado = null;
        _espera = 0f;
        guardia.RegistrarVuelta();
        return true;
    }

    /// <summary>No entra en el cruce si no cabe entero al otro lado (así no lo bloquea).</summary>
    private bool DestinoLibre(Conexion m)
    {
        var destino = m.Destino;
        if (destino.EstaCortado(_tiempoActual)) return false;   // carril cortado por un accidente
        if (destino.Coches.Count == 0 || destino.Longitud < Longitud + 2f) return true;
        var ultimo = destino.Coches[0];
        float libre = ultimo.S - ultimo.Longitud / 2f;
        float necesario = _espera > 30f ? Longitud * 0.5f : Longitud + 1.5f;
        if (libre >= necesario) return true;
        _bloqueoCruce = ultimo;
        return false;
    }

    /// <summary>Tiempo en recorrer una distancia desde la velocidad v acelerando con a.</summary>
    private static float TiempoEnRecorrer(float distancia, float v, float a, float vMax = float.PositiveInfinity)
    {
        if (distancia <= 0f) return 0f;
        if (v >= vMax) return distancia / vMax;
        // Acelera hasta vMax (si llega) y sigue a esa velocidad.
        float tAcel = (vMax - v) / a;
        float dAcel = (v + vMax) / 2f * tAcel;
        if (float.IsInfinity(vMax) || dAcel >= distancia) return (-v + Mathf.Sqrt(v * v + 2f * a * distancia)) / a;
        return tAcel + (distancia - dAcel) / vMax;
    }
}

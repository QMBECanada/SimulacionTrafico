using System.Collections.Generic;
using System.Linq;
using Godot;
using SimulacionTrafico.Mapa;
using SimulacionTrafico.Nucleo;
using SimulacionTrafico.Trafico;

namespace SimulacionTrafico.Vehiculos;

// Coche: avanzar de vía, planificar y seguir la ruta; salir de un lugar y llegar a otro.
public sealed partial class Coche
{
    private void AvanzarVia()
    {
        float sobrante = S - Via.Longitud;
        var carrilAnterior = Via as Carril;
        QuitarDe(Via.Coches);
        Plan.RemoveAt(0);
        if (Plan.Count == 0)
        {
            // No debería ocurrir: el plan siempre tiene la siguiente vía. Reubicamos por seguridad.
            Colocar(_grafo.Carriles[_azar.RandiRange(0, _grafo.Carriles.Count - 1)], 0f, 0f);
            return;
        }
        Via = Plan[0];
        if (Via is Carril) Sortear();   // en la calle, no al entrar en el cruce: el empate que se decidió al acercarse se mantiene dentro
        // Entra en un cruce que controla un guardia: el guardia cuenta los coches a los que ha dado paso.
        if (carrilAnterior != null && Via is Conexion)
        {
            var cruceAnterior = carrilAnterior.Destino;
            if (cruceAnterior.Guardia is { } guardiaCruce) guardiaCruce.RegistrarPaso(carrilAnterior.Tramo);
            else if (cruceAnterior.Control == TipoControl.Semaforo && cruceAnterior.Semaforo != null)
                cruceAnterior.Semaforo.RegistrarPaso(carrilAnterior.Tramo, cruceAnterior.EstadoPara(carrilAnterior, _tiempoActual));
        }
        S = sobrante;
        _haParado = false;
        _espera = 0f;
        _llegadaCruce = double.PositiveInfinity;
        InsertarOrdenado(Via);
        if (Via is Carril entrado)
        {
            if (Recorrido != null && entrado.Tramo != carrilAnterior?.Tramo) AnotarCalle(entrado, _tiempoActual);
            Planificar();
        }
    }

    /// <summary>
    /// Decide por qué calle seguirá tras el cruce y añade al plan la conexión y el carril de salida.
    /// Con misión sigue su ruta; si la perdió, la vuelve a calcular. Sin misión, elige al azar.
    /// </summary>
    private void Planificar()
    {
        if (Via is not Carril actual) return;

        // Al entrar en una calle de su ruta, deja de estar pendiente.
        if (_ruta.Count > 0 && _ruta.Peek() == actual.Tramo) _ruta.Dequeue();

        // Ya está en la calle de la puerta de su destino: solo le queda llegar a ella.
        if (Destino != null && _ruta.Count == 0 && EsCalleDestino(actual))
        {
            Plan.RemoveRange(1, Plan.Count - 1);
            _tramoSiguiente = null;
            _indiceDeseado = null;
            return;
        }

        // Con navegador: si la calle por la que iba a seguir está atascada, busca otro camino (como mucho cada 90 s).
        if (Destino != null && _ruta.Count > 0 && Conductor.UsaNavegador && _tiempoActual - _ultimaRuta > 90.0
            && _enrutador.HayAtasco(_ruta.Peek(), actual.Destino) && _enrutador.ReservarReRuta())
        {
            _enrutador.ReRutasPorAtasco++;
            Reenrutar(actual, null);
        }

        if (Destino != null && _ruta.Count == 0) Reenrutar(actual, null);

        Tramo? tramo = _ruta.Count > 0 ? _ruta.Peek() : null;
        if (tramo != null && !SaleDeEsteNodo(actual, tramo))
        {
            _ruta.Clear();
            tramo = null;
        }
        tramo ??= ElegirTramoSiguiente(actual, soloPermitidos: false);

        if (tramo == null)
        {
            // Callejón sin salida (por calles de un sentido): vuelve a empezar en otro sitio.
            _ruta.Clear();
            Colocar(_grafo.Carriles[_azar.RandiRange(0, _grafo.Carriles.Count - 1)], 0f, 0f);
            return;
        }
        FijarPlan(actual, tramo);
    }

    private bool EsCalleDestino(Carril carril) =>
        Destino != null && Destino.Acceso.Tramo == carril.Tramo && Destino.Acceso.Ida == carril.Ida;

    private static bool SaleDeEsteNodo(Carril actual, Tramo tramo) =>
        tramo != actual.Tramo && actual.Destino.Salientes.Any(l => l.Tramo == tramo);

    /// <summary>
    /// Calcula una ruta nueva hasta el destino. Si se indica <paramref name="primera"/>, empieza por esa calle
    /// (la que acaba de tomar al no poder girar como quería).
    /// </summary>
    private void Reenrutar(Carril actual, Tramo? primera)
    {
        if (Destino == null) return;
        ReRutas++;

        List<Tramo>? resto;
        if (primera == null)
            resto = _enrutador.Calcular(actual.Tramo, actual.Ida, S / actual.Longitud, Destino, Conductor);
        else
            resto = _enrutador.Calcular(primera, primera.A == actual.Destino, 0f, Destino, Conductor);

        _ultimaRuta = _tiempoActual;
        _ruta.Clear();
        if (primera != null) _ruta.Enqueue(primera);
        if (resto != null)
            foreach (var t in resto) _ruta.Enqueue(t);
    }

    /// <summary>Se queda dentro de un lugar (no circula ni ocupa la calle).</summary>
    public void Entrar(Lugar lugar, bool conPlaza = true)
    {
        if (Via != null) QuitarDe(Via.Coches);
        Plan.Clear();
        _ruta.Clear();
        Circulando = false;
        Destino = null;
        LugarActual = lugar;
        if (conPlaza) lugar.Dentro++;
        V = 0f;
        A = 0f;
        Hueco = float.PositiveInfinity;
        Motivo = null;
        Bloqueador = null;
    }

    private void AnotarCalle(Carril carril, double tiempo)
    {
        if (Recorrido!.Length > 0) Recorrido.Append(';');
        Recorrido.Append(Datos.Nombres.Calle(carril.Tramo)).Append('/').Append(Datos.Nombres.Direccion(carril.Tramo, carril.Ida))
                 .Append('@').Append(tiempo.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>Fija a dónde irá y por qué calles. Todavía no sale.</summary>
    public void PrepararSalida(Lugar destino, List<Tramo> ruta)
    {
        Destino = destino;
        _ruta.Clear();
        foreach (var t in ruta) _ruta.Enqueue(t);
    }

    /// <summary>Sale por la puerta de su lugar a la calle.</summary>
    public void Salir(double tiempo)
    {
        var origen = LugarActual!;
        origen.Dentro--;
        LugarActual = null;
        Circulando = true;
        InicioViaje = tiempo;
        OrigenViaje = origen;
        ChoquesViaje = 0;
        ParadasViaje = 0;
        ParadoViajeS = 0f;
        Recorrido = _contexto.RegistrarRecorridos ? new System.Text.StringBuilder() : null;
        if (Recorrido != null) AnotarCalle(origen.Salida, tiempo);
        _ultimaRuta = tiempo;
        DistanciaViaje = 0f;
        ReRutas = 0;
        TiempoParado = 0f;
        Colocar(origen.Salida, origen.SSalida, 0f);
    }

    /// <summary>Si ha llegado a la puerta de su destino, deja la calle. Devuelve true si ha llegado.</summary>
    private bool ComprobarLlegada()
    {
        if (Destino == null || _ruta.Count > 0 || Via is not Carril carril || Plan.Count != 1 || !EsCalleDestino(carril)) return false;
        float sPuerta = Destino.SAcceso * carril.Longitud / Destino.Acceso.Longitud;
        if (S < sPuerta && S < carril.Longitud - 1f) return false;

        QuitarDe(Via.Coches);
        Plan.Clear();
        Circulando = false;
        V = 0f;
        A = 0f;
        Hueco = float.PositiveInfinity;
        Motivo = null;
        Bloqueador = null;
        return true;
    }

    /// <summary>Plan = [carril actual, conexión del cruce, carril de salida] hacia esa calle.</summary>
    private void FijarPlan(Carril actual, Tramo tramo)
    {
        _tramoSiguiente = tramo;
        var salida = _grafo.CarrilDestino(actual, tramo);
        var giro = _grafo.GiroHacia(actual, tramo);
        int n = Grafo.CarrilesEnSentido(actual);
        _indiceDeseado = n <= 1 ? null
                       : giro == TipoGiro.Izquierda ? 0
                       : giro == TipoGiro.Derecha ? n - 1
                       : null;

        Plan.RemoveRange(1, Plan.Count - 1);
        Plan.Add(_grafo.ObtenerConexion(actual, salida));
        Plan.Add(salida);
    }

    /// <summary>
    /// Elige la calle siguiente: sin cambios de sentido, 60 % de seguir recto y 20 % para cada giro.
    /// Con <paramref name="soloPermitidos"/> descarta los giros que no se pueden hacer desde el carril actual.
    /// </summary>
    private Tramo? ElegirTramoSiguiente(Carril actual, bool soloPermitidos)
    {
        var grupos = actual.Destino.Salientes
            .Where(l => l.Tramo != actual.Tramo && !actual.Destino.GiroProhibido(actual.Tramo, l.Tramo))
            .GroupBy(l => l.Tramo)
            .Select(g => g.Key)
            .ToList();
        if (soloPermitidos)
        {
            var permitidos = grupos.Where(t => Grafo.PermiteGiro(actual, _grafo.GiroHacia(actual, t))).ToList();
            if (permitidos.Count > 0) grupos = permitidos;
        }
        if (grupos.Count == 0) return null;

        var dirFinal = actual.Eje.Direccion(actual.Longitud);
        var pesos = grupos.Select(t => dirFinal.Dot(_grafo.CarrilDestino(actual, t).Eje.Direccion(0f)) > 0.9f ? 0.6f : 0.2f).ToArray();
        float r = _azar.Randf() * pesos.Sum();
        int elegido = 0;
        for (; elegido < pesos.Length - 1; elegido++)
        {
            r -= pesos[elegido];
            if (r <= 0f) break;
        }
        return grupos[elegido];
    }
}

using System.Collections.Generic;
using System.Linq;
using Godot;
using SimulacionTrafico.Nucleo;
using SimulacionTrafico.Mapa;
using SimulacionTrafico.Datos;
using SimulacionTrafico.Trafico;

namespace SimulacionTrafico.Vehiculos;

// Simulación: lugares (aparcar, estancias, salidas con su ruta y hueco para salir a la calle).
public partial class Simulacion : Node
{
    private void Evento(Lugar lugar, string evento, Coche coche, double t) =>
        _eventosLugares?.Fila(Estudio!.Hora(t), t, lugar.Nombre, lugar.Tipo, evento, coche.Id, lugar.Dentro, lugar.Capacidad, lugar.EsperandoPlaza.Count);

    /// <summary>Los coches que terminan de maniobrar quedan aparcados y empieza su estancia.</summary>
    private void Aparcar()
    {
        while (_aparcando.TryPeek(out var coche, out double hora) && hora <= TiempoSimulado)
        {
            _aparcando.Dequeue();
            var lugar = coche.LugarActual!;
            Evento(lugar, "Aparca", coche, TiempoSimulado);
            Encolar(coche, TiempoSimulado + Misiones.Estancia(coche, lugar, HoraAbsolutaS, coche.Azar, _factorEstancia));
        }
    }

    /// <summary>Cuando se libera una plaza, entra el primero que esperaba (empieza su maniobra).</summary>
    private void AdmitirEsperando(Lugar lugar)
    {
        while (lugar.EsperandoPlaza.Count > 0 && lugar.Dentro < lugar.Capacidad)
        {
            var coche = lugar.EsperandoPlaza.Dequeue();
            lugar.Dentro++;
            _aparcando.Enqueue(coche, TiempoSimulado + Misiones.Maniobra(lugar.Tipo, coche.Azar, true));
            Evento(lugar, "ConPlaza", coche, TiempoSimulado);
        }
    }

    /// <summary>
    /// Los coches que terminan su estancia eligen su siguiente destino, planifican la ruta y se ponen a la cola de salida.
    /// Con demanda: si ya hay en la calle tantos coches como toca, los que terminan su estancia se quedan dentro; si faltan,
    /// salen coches antes de tiempo desde los lugares, poco a poco.
    /// </summary>
    private void LiberarDentro()
    {
        long inicio = Enrutador.Expansiones;
        int fallidos = 0;   // intentos sin ruta en este paso: no gastan cálculo, así que se limitan aparte
        if (_flotaDemanda < 0) _flotaDemanda = Flota.Count(c => c.Rutina?.Perfil != Perfil.Autobus);

        // Los autobuses, cuando les toca.
        while (_dentroAutobuses.TryPeek(out var bus, out double horaBus) && horaBus <= TiempoSimulado)
        {
            _dentroAutobuses.Dequeue();
            Planificar(bus, ref fallidos, false);
        }

        // El resto, al ritmo de la demanda: el cupo crece con los viajes por hora que tocan (como mucho, los de un minuto).
        bool conDemanda = Demanda != null;
        if (conDemanda)
        {
            double porSegundo = ViajesPorHoraObjetivo / 3600.0;
            _cupoSalidas = System.Math.Min(_cupoSalidas + porSegundo * Paso, System.Math.Max(2.0, porSegundo * 60.0));
        }
        List<(Coche, double)>? aplazados = null;
        while (_dentro.TryPeek(out var coche, out double hora))
        {
            if (conDemanda && _cupoSalidas < 1.0) break;   // ya han salido los que tocaban: los demás esperan
            if (Enrutador.Expansiones - inicio >= PresupuestoExpansiones) break;
            bool vencida = hora <= TiempoSimulado;
            if (!vencida)
            {
                // Faltan salidas para el ritmo de la hora: sale antes de tiempo el siguiente de la cola, salvo si su ruta acaba de fallar.
                if (!conDemanda) break;
                if (coche.NoSalirAntesDe > TiempoSimulado)
                {
                    _dentro.Dequeue();
                    (aplazados ??= new()).Add((coche, hora));
                    if (aplazados.Count >= 30) break;
                    continue;
                }
            }
            _dentro.Dequeue();
            if (Planificar(coche, ref fallidos, conDemanda) && fallidos >= 20) break;
        }
        if (aplazados != null) foreach (var (c, h) in aplazados) _dentro.Enqueue(c, h);
    }

    /// <summary>
    /// El coche elige su siguiente destino, planifica la ruta y se pone a la cola de salida de su lugar. Si no puede (sin destino, puerta
    /// cortada o sin ruta), vuelve a la cola para más tarde. Devuelve true si ha fallado.
    /// </summary>
    private bool Planificar(Coche coche, ref int fallidos, bool gastaCupo)
    {
        {
            var origen = coche.LugarActual!;

            var (destino, motivo) = Misiones.SiguienteDestino(_grafo, coche, origen, HoraAbsolutaS, coche.Azar);
            if (destino == null)
            {
                Encolar(coche, TiempoSimulado + 60.0);
                return true;
            }

            var acceso = origen.Salida;
            if (acceso.EstaCortado(TiempoSimulado))
            {
                // La calle de la puerta está cortada por un accidente: no puede salir hasta que se reabra.
                Encolar(coche, acceso.CortadoHasta + 1.0);
                coche.NoSalirAntesDe = acceso.CortadoHasta + 1.0;
                fallidos++;
                return true;
            }
            var ruta = Enrutador.Calcular(acceso.Tramo, acceso.Ida, origen.SSalida / acceso.Longitud, destino, coche.Conductor);
            if (ruta == null)
            {
                SinRuta++;
                Encolar(coche, TiempoSimulado + 120.0);
                coche.NoSalirAntesDe = TiempoSimulado + 120.0;   // no se le vuelve a adelantar la salida hasta entonces
                fallidos++;
                return true;
            }

            Planificados++;
            DistanciaPlanificadaTotal += origen.Puerta.DistanceTo(destino.Puerta);
            coche.PrepararSalida(destino, ruta);
            coche.MotivoViaje = motivo;
            coche.ListoT = TiempoSimulado + Misiones.Maniobra(origen.Tipo, coche.Azar, false);   // sale de su plaza hasta la puerta
            origen.Salidas.Enqueue(coche);
            if (origen.Salidas.Count == 1) _conSalidas.Add(origen);
            _enSalida++;
            if (gastaCupo) _cupoSalidas -= 1.0;
            if (coche.Rutina?.Perfil != Perfil.Autobus) Salidas++;
            return false;
        }
    }

    /// <summary>Sale a la calle el primero de la cola de cada lugar, si hay hueco en el carril de la puerta.</summary>
    private void ProcesarSalidas()
    {
        for (int i = _conSalidas.Count - 1; i >= 0; i--)
        {
            var lugar = _conSalidas[i];
            var coche = lugar.Salidas.Peek();
            if (TiempoSimulado >= coche.ListoT && HayHueco(lugar, coche)) SalirA(lugar, coche);

            if (lugar.Salidas.Count == 0)
            {
                _conSalidas[i] = _conSalidas[^1];
                _conSalidas.RemoveAt(_conSalidas.Count - 1);
            }
        }
    }

    private void SalirA(Lugar lugar, Coche coche)
    {
        lugar.Salidas.Dequeue();
        _enSalida--;
        coche.Salir(TiempoSimulado);
        Coches.Add(coche);
        Evento(lugar, "Sale", coche, TiempoSimulado);
        AdmitirEsperando(lugar);
    }

    /// <summary>Hay sitio si no hay coches pegados delante ni venga uno rápido por detrás.</summary>
    private bool HayHueco(Lugar lugar, Coche coche)
    {
        var salida = lugar.Salida;
        if (salida.EstaCortado(TiempoSimulado)) return false;   // la calle de la puerta está cortada por un accidente
        float s = lugar.SSalida;
        // Los de la propia calle y los que, desde un carril contiguo, aún están pasando por ella al cambiar de carril.
        for (int lado = 0; lado < 2; lado++)
            if ((lado == 0 ? salida.Izquierdo : salida.Derecho) is { } contiguo)
                foreach (var otro in contiguo.Coches)
                    if (otro.OcupaCarril(salida) && Estorba(otro)) return false;
        foreach (var otro in salida.Coches)
            if (Estorba(otro)) return false;
        return true;

        bool Estorba(Coche otro)
        {
            float distancia = otro.S - s;
            float medio = (otro.Longitud + coche.Longitud) / 2f;
            if (distancia >= 0f && distancia < medio + 5f) return true;                   // pegado delante
            return distancia < 0f && -distancia < medio + 4f + 1.5f * otro.V;             // viene por detrás
        }
    }
}

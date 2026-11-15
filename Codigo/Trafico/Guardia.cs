using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SimulacionTrafico.Mapa;
using SimulacionTrafico.Nucleo;
using SimulacionTrafico.Vehiculos;

namespace SimulacionTrafico.Trafico;

/// <summary>Una fila del registro de actuaciones de los guardias (fichero guardias.tsv).</summary>
/// <param name="Evento">LLEGADA, INICIO, TURNO o FIN.</param>
public sealed record FilaGuardia(
    string Evento, double Tiempo, int Guardia, int Zona, Nodo Cruce, Tramo? Calle, string Orden, double DuracionS,
    int CochesPasaron, int Vueltas, int ColaInicio, int ColaFin, int GuardiasZona, int CrucesZona);

public enum EstadoGuardia
{
    Libre,
    EnCamino,
    /// <summary>Ha llegado a su cruce y espera a que lleguen los demás de su zona para empezar todos a la vez.</summary>
    Esperando,
    Actuando,
}

/// <summary>Lo que el guardia ordena a una aproximación del cruce.</summary>
public enum OrdenGuardia
{
    /// <summary>Alto.</summary>
    Parar,
    /// <summary>Paso libre, siempre que quepan al otro lado.</summary>
    Pasar,
    /// <summary>Paso libre aunque no quepan enteros al otro lado; los de la cabeza de la cola pueden dar la vuelta.</summary>
    PasarForzado,
}

/// <summary>
/// Guardia de tráfico. Forma parte de una <see cref="Zona"/>: un grupo de guardias que actúa a la vez sobre varios
/// cruces atascados. En su cruce sustituye las instrucciones del semáforo, el stop o el ceda: da paso a una aproximación
/// cada vez y para las demás. Los turnos de todos los guardias de la zona cambian a la vez (en pulsos comunes) y dan
/// preferencia a los movimientos que sacan coches de la zona. Si nadie puede salir, ordena paso forzado y que los
/// primeros de la cola den la vuelta.
/// </summary>
public sealed class Guardia
{
    private enum Fase { Despeje, Paso }

    // Para experimentos: si puede ordenar paso forzado y si puede dar paso a la vez a la aproximación de enfrente cuando no hay giros
    // a la izquierda (las vueltas en U las decide cada coche con la misma configuración).
    private readonly bool _permitirForzado, _permitirOpuesta;

    private const float AlcanceCola = 45f;   // los coches a esta distancia de la línea cuentan como cola
    private const float PesoSalidaDeZona = 3f;
    private const float DespejeS = 3f;       // lo que se reserva al final de cada turno para vaciar el cruce

    public int Id { get; }
    public Nodo Base { get; }
    public Nodo Posicion { get; private set; }
    public EstadoGuardia Estado { get; private set; } = EstadoGuardia.Libre;
    public Nodo? Cruce { get; private set; }
    public Zona? Zona { get; set; }
    public double LlegadaPrevista { get; private set; }

    // Control del cruce
    private Fase _fase;
    private double _faseInicio;
    private float _duracionPaso;
    private Tramo? _aproximacion;
    private Tramo? _opuesta;     // la aproximación de enfrente, si puede pasar a la vez (ninguna de las dos puede girar a la izquierda)
    private Tramo? _ultima;
    private bool _forzado;
    private List<Tramo> _aproximaciones = new();

    // Registro de actuaciones
    /// <summary>Se llama con cada hecho que hay que registrar (llegada, inicio, turno, fin).</summary>
    public Action<FilaGuardia>? AlRegistrar { get; set; }
    private double _inicioActuacion;
    private double _inicioPaso;
    private int _pasaronTurno, _vueltasTurno, _colaInicioTurno;
    private int _pasaronTotal;

    // Estadísticas
    public int Turnos { get; private set; }
    public int TurnosForzados { get; private set; }
    public int Vueltas { get; set; }

    public Guardia(int id, Nodo @base, ConfiguracionSimulacion config)
    {
        _permitirForzado = config.GuardiaForzado;
        _permitirOpuesta = config.GuardiaOpuesta;
        Id = id;
        Base = @base;
        Posicion = @base;
    }

    /// <summary>Sale hacia un cruce.</summary>
    public void Enviar(Nodo cruce, double llegada)
    {
        Estado = EstadoGuardia.EnCamino;
        Cruce = cruce;
        LlegadaPrevista = llegada;
    }

    /// <summary>Ha llegado al cruce, pero aún no actúa: espera al resto del grupo.</summary>
    public void Llegar(double ahora)
    {
        Estado = EstadoGuardia.Esperando;
        Emitir("LLEGADA", ahora, null, "", 0, 0, 0, 0);
    }

    /// <summary>Un coche de la aproximación que tiene paso acaba de entrar en el cruce.</summary>
    public void RegistrarPaso(Tramo aproximacion)
    {
        _pasaronTotal++;
        if (aproximacion == _aproximacion || aproximacion == _opuesta) _pasaronTurno++;
    }

    /// <summary>Un coche acaba de dar la vuelta en U por orden del guardia.</summary>
    public void RegistrarVuelta()
    {
        Vueltas++;
        _vueltasTurno++;
    }

    private void Emitir(string evento, double ahora, Tramo? calle, string orden, double duracion, int pasaron, int colaIni, int colaFin) =>
        AlRegistrar?.Invoke(new FilaGuardia(evento, ahora, Id, Zona?.Id ?? -1, Cruce!, calle, orden, duracion, pasaron,
            evento == "TURNO" ? _vueltasTurno : Vueltas, colaIni, colaFin, Zona?.Guardias.Count ?? 1, Zona?.Nodos.Count ?? 1));

    /// <summary>Toma el control del cruce.</summary>
    public void Empezar(double ahora)
    {
        Estado = EstadoGuardia.Actuando;
        var cruce = Cruce!;
        Turnos = 0;
        TurnosForzados = 0;
        Vueltas = 0;
        _pasaronTotal = 0;
        _inicioActuacion = ahora;
        _ultima = null;
        _aproximaciones = cruce.Entrantes.Select(l => l.Tramo).Distinct().ToList();
        cruce.Guardia = this;
        _fase = Fase.Despeje; // empieza esperando a que se vacíe el cruce y a que llegue el pulso
        _faseInicio = ahora;
        Emitir("INICIO", ahora, null, "", 0, 0, 0, 0);
    }

    /// <summary>Se va: el cruce vuelve a funcionar con sus señales.</summary>
    public void Terminar(double ahora)
    {
        if (Estado == EstadoGuardia.Actuando)
            Emitir("FIN", ahora, null, "", ahora - _inicioActuacion, _pasaronTotal, 0, 0);
        if (Cruce != null) Cruce.Guardia = null;
        Posicion = Cruce ?? Posicion;
        Cruce = null;
        Zona = null;
        Estado = EstadoGuardia.Libre;
    }

    /// <summary>Qué ordena a los coches de este carril de entrada al cruce.</summary>
    public OrdenGuardia OrdenPara(Carril entrante)
    {
        if (_fase != Fase.Paso || (entrante.Tramo != _aproximacion && entrante.Tramo != _opuesta)) return OrdenGuardia.Parar;
        return _forzado ? OrdenGuardia.PasarForzado : OrdenGuardia.Pasar;
    }

    /// <summary>Se llama en cada paso mientras actúa.</summary>
    public void Actualizar(double ahora)
    {
        var cruce = Cruce!;
        double transcurrido = ahora - _faseInicio;
        float pulso = Zona != null ? global::SimulacionTrafico.Trafico.Zona.PulsoS : 0f;

        if (_fase == Fase.Despeje)
        {
            bool cajaLibre = CajaVacia(cruce) && transcurrido >= 1.5;
            // Los turnos empiezan en los pulsos comunes de la zona, para que todos los guardias cambien a la vez.
            bool enPulso = pulso <= 0f || (ahora % pulso) < 0.15 || transcurrido > pulso * 1.5;
            if ((cajaLibre || transcurrido > 6.0) && enPulso) ElegirAproximacion(ahora, cruce);
        }
        else
        {
            int cola = Cola(cruce, _aproximacion!);
            bool termina = pulso > 0f
                ? transcurrido >= _duracionPaso
                : transcurrido >= _duracionPaso || (transcurrido >= 4.0 && cola == 0);
            if (termina)
            {
                Emitir("TURNO", ahora, _aproximacion, (_forzado ? "PasarForzado" : "Pasar") + (_opuesta != null ? $" con {Datos.Nombres.Calle(_opuesta)}" : ""), ahora - _inicioPaso,
                    _pasaronTurno, _colaInicioTurno, cola);
                _fase = Fase.Despeje;
                _faseInicio = ahora;
            }
        }
    }

    private static bool CajaVacia(Nodo cruce) => cruce.Conexiones.All(c => c.Coches.Count == 0);

    /// <summary>Coches parados o casi parados cerca de la línea de detención en las calles de esa aproximación.</summary>
    /// <summary>Coches en cola ante todas las entradas del cruce.</summary>
    public static int ColaTotal(Nodo cruce) => cruce.Tramos.Distinct().Sum(t => Cola(cruce, t));

    private static int Cola(Nodo cruce, Tramo aproximacion)
    {
        int n = 0;
        foreach (var carril in cruce.Entrantes)
        {
            if (carril.Tramo != aproximacion) continue;
            for (int i = carril.Coches.Count - 1; i >= 0; i--)
            {
                var c = carril.Coches[i];
                if (carril.Longitud - c.S > AlcanceCola) break;
                if (c.V < 2f) n++;
            }
        }
        return n;
    }

    /// <summary>
    /// La aproximación de enfrente, si puede tener paso a la vez que <paramref name="t"/>: cuando en ninguna de las dos se puede girar a
    /// la izquierda (no hay esa salida o está prohibida), sus movimientos no se cruzan y se les da verde a los dos sentidos. Si hay
    /// giro a la izquierda posible, se da paso a una sola para que nadie cruce delante del que viene de frente.
    /// </summary>
    private Tramo? Opuesta(Nodo cruce, Tramo t)
    {
        if (!_permitirOpuesta) return null;
        var entrada = cruce.Entrantes.FirstOrDefault(l => l.Tramo == t);
        if (entrada == null || !SinIzquierda(cruce, entrada)) return null;
        var dir = entrada.Eje.Direccion(entrada.Longitud);
        foreach (var otro in _aproximaciones)
        {
            if (otro == t) continue;
            var e2 = cruce.Entrantes.FirstOrDefault(l => l.Tramo == otro);
            if (e2 != null && e2.Eje.Direccion(e2.Longitud).Dot(dir) < -0.8f && SinIzquierda(cruce, e2)) return otro;
        }
        return null;
    }

    /// <summary>True si desde esa entrada no se puede girar a la izquierda en este cruce.</summary>
    private static bool SinIzquierda(Nodo cruce, Carril entrada) =>
        !cruce.Salientes.Any(l => l.Tramo != entrada.Tramo && !cruce.GiroProhibido(entrada.Tramo, l.Tramo)
                                  && Conexion.CalcularGiro(entrada.Eje.Direccion(entrada.Longitud), l.Eje.Direccion(0f)) == TipoGiro.Izquierda);

    /// <summary>Elige a quién da paso ahora: la aproximación con más cola a cuyos coches les cabe salir.</summary>
    private void ElegirAproximacion(double ahora, Nodo cruce)
    {
        Tramo? mejor = null;
        float mejorPuntos = float.NegativeInfinity;
        Tramo? masCola = null;
        float maxPuntosCola = float.NegativeInfinity;

        foreach (var t in _aproximaciones)
        {
            int cola = Cola(cruce, t);
            if (cola == 0) continue;

            float espera = 0f, salidaDeZona = 0f;
            bool cabe = false;
            foreach (var carril in cruce.Entrantes)
            {
                if (carril.Tramo != t || carril.Coches.Count == 0) continue;
                var cabeza = carril.Coches[^1];
                espera = Mathf.Max(espera, cabeza.Espera);
                if (cabeza.CabeAlOtroLado()) cabe = true;

                // Coordinación: se prefieren los movimientos que sacan coches de la zona (hacia fuera).
                if (Zona != null && cabeza.NodoSiguiente is { } siguiente)
                    salidaDeZona = Mathf.Max(salidaDeZona, PesoSalidaDeZona * (Zona.Profundidad(cruce) - Zona.Profundidad(siguiente)));
            }

            float puntos = cola + espera / 20f + salidaDeZona - (t == _ultima ? 2f : 0f);
            if (puntos > maxPuntosCola)
            {
                maxPuntosCola = puntos;
                masCola = t;
            }
            if (cabe && puntos > mejorPuntos)
            {
                mejorPuntos = puntos;
                mejor = t;
            }
        }

        _forzado = false;
        if (mejor == null && masCola != null)
        {
            // Hay coches esperando pero a nadie le cabe salir.
            mejor = masCola;
            if (_permitirForzado)
            {
                _forzado = true;
                TurnosForzados++;
            }
        }
        else if (mejor == null)
        {
            // Nadie espera: turno corto por rotación, para los que llegan.
            int i = _ultima == null ? 0 : (_aproximaciones.IndexOf(_ultima) + 1) % _aproximaciones.Count;
            mejor = _aproximaciones[i];
        }

        _aproximacion = mejor;
        _opuesta = Opuesta(cruce, mejor);
        _ultima = mejor;
        _fase = Fase.Paso;
        _faseInicio = ahora;

        int colaElegida = Cola(cruce, mejor);
        _inicioPaso = ahora;
        _pasaronTurno = 0;
        _vueltasTurno = 0;
        _colaInicioTurno = colaElegida;
        float pulso = Zona != null ? global::SimulacionTrafico.Trafico.Zona.PulsoS : 0f;
        _duracionPaso = pulso > 0f
            ? pulso * (colaElegida > 6 ? 2f : 1f) - DespejeS // turnos de uno o dos pulsos (menos el despeje): todos los guardias cambian a la vez
            : Mathf.Clamp(5f + 1.5f * colaElegida, 8f, 30f);
        Turnos++;
    }
}

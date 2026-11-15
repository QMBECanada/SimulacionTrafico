using System.Diagnostics;
using System.Linq;
using Godot;
using SimulacionTrafico.Camara;
using SimulacionTrafico.Datos;
using SimulacionTrafico.Mapa;
using SimulacionTrafico.Nucleo;
using SimulacionTrafico.Trafico;
using SimulacionTrafico.UI;
using SimulacionTrafico.Vehiculos;

namespace SimulacionTrafico;

// Principal: textos de la interfaz (hora, ficha del coche) y parámetros del estudio.
public partial class Principal : Node2D
{
    /// <summary>Segundo del día (0 a 86400) según la hora de inicio y el tiempo simulado.</summary>
    private double HoraDelDiaS() => ((horaInicio ?? 8.0) * 3600.0 + _sim.TiempoSimulado) % 86400.0;

    private string TextoHora()
    {
        double h = HoraDelDiaS();
        string hora = $"{(int)(h / 3600):00}:{(int)(h % 3600 / 60):00}:{(int)(h % 60):00}";
        if (_sol == null) return $"Hora {hora}";
        return $"Hora {hora}   Sol {Sol.Formato(_sol.SalidaH)}-{Sol.Formato(_sol.PuestaH)}   {_sol.Localidad.Nombre}";
    }

    private float VelocidadMedia() =>
        _sim.Coches.Count == 0 ? 0f : _sim.Coches.Average(c => c.V) * Escala.MsAKmh;

    private string HoraDelDia()
    {
        double s = _sim.Demanda!.HoraDelDiaS(_sim.TiempoSimulado);
        return $"{(int)(s / 3600):00}:{(int)(s % 3600 / 60):00}";
    }

    private static string Hora(double s) => $"{(int)(s / 60):00}:{(int)(s % 60):00}";

    /// <summary>
    /// Datos del vehículo seguido: siempre los mismos campos y en el mismo orden (con «-» cuando no se aplican, por ejemplo si está
    /// dentro de un lugar), para que la tabla no cambie de forma.
    /// </summary>
    private System.Collections.Generic.List<(string, string)> DescribirCoche(Coche c, string aviso)
    {
        var d = c.Conductor;
        bool circula = c.Circulando;
        string clase = d.Clase switch
        {
            ClaseVehiculo.Moto => "moto",
            ClaseVehiculo.Camion => "camión",
            ClaseVehiculo.Autobus => "autobús",
            _ => "automóvil",
        };
        string via = !circula ? $"dentro de {c.LugarActual?.Nombre ?? "un lugar"}"
            : c.Via is Carril l
            ? $"{Datos.Nombres.Calle(l.Tramo)} carril {l.Indice + 1} de {Grafo.CarrilesEnSentido(l)}{(c.IndiceDeseado is { } deseado ? $" (debe ir al {deseado + 1})" : "")}" +
              (c.Z < 0 ? " · bajo tierra: no se ve" : "")
            : $"dentro del cruce {Datos.Nombres.Cruce(((Conexion)c.Via).Origen.Destino)}";
        string delante = !circula ? "-"
                       : !c.BloqueadorEsLider || float.IsInfinity(c.Hueco) ? "nadie"
                       : c.V < 0.2f ? $"parado a {c.Hueco:0.0} m"
                       : $"{c.Hueco:0.0} m ({c.Hueco / c.V:0.0} s)";
        var r = c.Rutina;
        string rutina = r == null ? "-"
            : r.Perfil == Perfil.Autobus ? $"autobús de la línea {r.Linea?.Nombre}"
            : r.Perfil == Perfil.Reparto ? $"reparto de {Hora24(r.HoraSalida)} a {Hora24(r.HoraFin)}"
            : r.Trabaja ? $"trabaja de {Hora24(r.HoraSalida)} a {Hora24(r.HoraFin)}{(r.Perfil == Perfil.TrabajadorConColegio ? ", lleva niños" : "")}"
            : "no trabaja fuera";
        return new System.Collections.Generic.List<(string, string)>
        {
            ("Vehículo", $"#{c.Id} {c.Modelo.Nombre} · {clase}, {c.Modelo.LargoM:0.0} m"),
            ("Va por", via),
            ("Velocidad", circula ? $"{c.V * Escala.MsAKmh:0.0} km/h" : "-"),
            ("Deseada", circula ? $"{c.ObjetivoAhora * Escala.MsAKmh:0.0} / {d.VelocidadDeseada(c.Via.LimiteMs) * Escala.MsAKmh:0.0} km/h" : "-"),
            ("Límite", !circula ? "-" : c.Via is Carril lc ? $"{lc.Tramo.LimiteKmh:0} km/h" : $"{c.Via.LimiteMs * Escala.MsAKmh:0} km/h"),
            ("Aceleración", circula ? $"{c.A:0.00} m/s²" : "-"),
            ("Detenido por", !circula || c.Motivo == null ? "-" : $"{c.Motivo} ({c.Espera:0} s)"),
            ("Delante", delante),
            ("Destino", circula && c.Destino is { } dd ? $"{dd.Nombre} ({c.MotivoViaje})" : "-"),
            ("Le quedan", circula && c.Destino != null ? $"{c.CallesPendientes} calles" : "-"),
            ("Lleva", circula && c.Destino != null ? $"{c.DistanciaViaje / 1000f:0.0} km, {(_sim.TiempoSimulado - c.InicioViaje) / 60.0:0.0} min" : "-"),
            ("Rutas recalculadas", $"{c.ReRutas}"),
            ("Rutina", rutina),
            ("Conductor", $"{d.Tipo} (agresividad {d.Agresividad:+0.00;-0.00})"),
            ("Respecto al límite", $"{100f * (1f + d.ExcesoLimite):0} % (±5 %)"),
            ("Separación", $"{d.TiempoSeparacion:0.00} s"),
            ("Distancia en parado", $"{d.DistanciaMinima:0.0} m"),
            ("Aceleración máx.", $"{d.AceleracionMax:0.0} m/s²"),
            ("Frenada normal", $"{d.FrenadaComoda:0.0} m/s²"),
            ("Hueco crítico", $"{d.HuecoCriticoBase:0.0} s"),
            ("Reacción", $"{d.TiempoReaccion:0.0} s"),
            ("Lateral (giros)", $"{d.AceleracionLateral:0.0} m/s²"),
            ("Navegador", d.UsaNavegador ? "sí" : "no"),
            ("Aviso", aviso == "" ? "-" : aviso),
        };
    }

    private static string Hora24(float horas) => $"{(int)horas:00}:{(int)(horas % 1f * 60f):00}";

    /// <summary>Todo lo necesario para repetir el estudio: se guarda en parametros.tsv.</summary>
    private System.Collections.Generic.IEnumerable<(string, object)> ParametrosDelEstudio(float tam, int semilla, float estancia, double horaInicio)
    {
        yield return ("Ciudad", _nombreCiudad);
        yield return ("Ciudad_ancho_m", _grafo.Limites.Size.X);
        yield return ("Ciudad_alto_m", _grafo.Limites.Size.Y);
        yield return ("Semilla", semilla);
        yield return ("HoraInicio", $"{(int)horaInicio:00}:{(int)(horaInicio % 1 * 60):00}");
        yield return ("VehiculosMaximos", _sim.Flota.Count);
        yield return ("Semaforos", _config.SemaforosAdaptativos ? "adaptativos" : "fijos");
        yield return ("FasesGiroIzquierda", _config.FasesGiro ? 1 : 0);
        yield return ("OndaVerde", _config.OndaVerde ? 1 : 0);
        yield return ("Lluvia", _config.Lluvia ? 1 : 0);
        yield return ("ProporcionMotos", _config.ProporcionMotos);
        yield return ("ProporcionCamiones", _config.ProporcionCamiones);
        yield return ("LineasAutobus", _sim.Lineas.Count);
        yield return ("PuertasCiudad", _sim.Puertas.Count);
        yield return ("ProporcionDeFuera", _config.ProporcionDeFuera);
        yield return ("ProporcionTrabajaFuera", _config.ProporcionTrabajaFuera);
        yield return ("ProporcionDePaso", _config.ProporcionTransito);
        yield return ("ProporcionCamionesDeFuera", _config.ProporcionCamionesDeFuera);
        yield return ("Autobuses", _sim.Lineas.Sum(l => l.Autobuses));
        yield return ("Peatones", _config.Peatones ? 1 : 0);
        yield return ("FactorPlazas", _config.FactorPlazas);
        if (horasSim > 0) yield return ("HorasSimuladas", horasSim);
        yield return ("Calentamiento_min", calentamientoMin);
        if (_sol != null)
        {
            yield return ("Sol_ciudad", _sol.Localidad.Nombre);
            yield return ("Sol_fecha", _sol.Fecha.ToString("dd/MM"));
            yield return ("Sol_salida", Sol.Formato(_sol.SalidaH));
            yield return ("Sol_puesta", Sol.Formato(_sol.PuestaH));
        }
        yield return ("FactorEstancia", estancia);
        yield return ("ProporcionNavegador", _config.ProbabilidadNavegador);
        yield return ("Guardias", _sim.Guardias.Guardias.Count);
        yield return ("Radares", _grafo.Radares.Count);
        yield return ("PasosPeatones", _sim.Peatones.Pasos.Count);
        yield return ("Peatones_porHora_centro", _config.Peatones ? ServicioPeatones.TasaCentro : 0f);
        yield return ("Peatones_porHora_afueras", _config.Peatones ? ServicioPeatones.TasaAfueras : 0f);
        yield return ("Nodos", _grafo.Nodos.Count);
        yield return ("Tramos", _grafo.Tramos.Count);
        yield return ("Carriles", _grafo.Carriles.Count);
        yield return ("Lugares", _grafo.Lugares.Count);
        var a = _sim.Atascos.Parametros;
        yield return ("Atasco_velocidadMaxima_kmh", a.VelocidadMaximaKmh);
        yield return ("Atasco_duracionMinima_s", a.DuracionMinimaS);
        yield return ("Atasco_cochesMinimos", a.CochesMinimos);
        yield return ("Atasco_velocidadFin_kmh", a.VelocidadFinKmh);
        yield return ("Atasco_duracionFin_s", a.DuracionFinS);
        yield return ("Guardias_cochesMinimosAtascoGrave", _sim.Guardias.CochesMinimosGrave);
        yield return ("Guardias_duracionMinimaAtascoGrave_s", _sim.Guardias.DuracionMinimaGraveS);
        if (_sim.Demanda != null)
            for (int h = 0; h < 24; h++)
            {
                yield return ($"Porcentaje_indicado_h{h:00}", _sim.Demanda.Indicado[h]);
                yield return ($"Porcentaje_aplicado_h{h:00}", _sim.Demanda.Aplicado[h]);
            }
    }
}

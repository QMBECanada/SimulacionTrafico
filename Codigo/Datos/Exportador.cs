using System.Collections.Generic;
using System.Linq;
using SimulacionTrafico.Mapa;
using SimulacionTrafico.Trafico;

namespace SimulacionTrafico.Datos;

/// <summary>Escribe los ficheros estáticos de un estudio (los que no se van llenando mientras corre la simulación).</summary>
public static class Exportador
{
    /// <summary>
    /// <c>calles.tsv</c>: una fila por calle y sentido (una calle de doble sentido son dos filas). Entradas y salidas son
    /// listas, separadas por comas, de las calles que enlazan con el principio y con el final de esa calle y sentido.
    /// </summary>
    public static void EscribirCalles(Estudio estudio, Grafo grafo)
    {
        using var registro = new RegistroTsv(System.IO.Path.Combine(estudio.Carpeta, "calles.tsv"),
            new[] { "Nombre", "LimiteKmh", "Tipo", "Direccion", "Carriles", "LongitudM", "CruceInicio", "CruceFin", "Entradas", "Salidas", "X1", "Y1", "X2", "Y2" });

        foreach (var t in grafo.Tramos.OrderBy(t => t.Nombre, System.StringComparer.Ordinal))
            foreach (bool ida in new[] { true, false })
            {
                int carriles = ida ? t.CarrilesIda : t.CarrilesVuelta;
                if (carriles == 0) continue;

                var inicio = ida ? t.A : t.B;
                var fin = ida ? t.B : t.A;
                // Entran coches desde las calles que llegan al cruce de inicio; salen a las que parten del cruce final.
                var entradas = inicio.Entrantes.Select(l => l.Tramo).Where(u => u != t && !inicio.GiroProhibido(u, t)).Distinct().Select(Nombres.Calle).OrderBy(n => n, System.StringComparer.Ordinal);
                var salidas = fin.Salientes.Select(l => l.Tramo).Where(u => u != t && !fin.GiroProhibido(t, u)).Distinct().Select(Nombres.Calle).OrderBy(n => n, System.StringComparer.Ordinal);

                registro.Fila(Nombres.Calle(t), t.LimiteKmh, t.EsRotonda ? "Rotonda" : t.EsTunel ? "Tunel" : t.EsPuente ? "Puente" : t.Categoria.ToString(), Nombres.Direccion(t, ida), carriles, t.Eje.Longitud,
                    Nombres.Cruce(inicio), Nombres.Cruce(fin), string.Join(",", entradas), string.Join(",", salidas),
                    inicio.Pos.X, inicio.Pos.Y, fin.Pos.X, fin.Pos.Y);
            }
    }

    /// <summary><c>giros_prohibidos.tsv</c>: los giros que no se pueden hacer en cada cruce (una fila por giro).</summary>
    public static void EscribirGirosProhibidos(Estudio estudio, Grafo grafo)
    {
        using var registro = new RegistroTsv(System.IO.Path.Combine(estudio.Carpeta, "giros_prohibidos.tsv"), new[] { "Cruce", "Desde", "Hasta", "Giro" });
        foreach (var n in grafo.Nodos.OrderBy(n => n.Nombre, System.StringComparer.Ordinal))
            foreach (var (d, h) in n.GirosProhibidos.OrderBy(p => p.desde).ThenBy(p => p.hasta))
            {
                var desde = grafo.Tramos[d];
                var hasta = grafo.Tramos[h];
                var entrante = n.Entrantes.First(l => l.Tramo == desde);
                registro.Fila(Nombres.Cruce(n), Nombres.Calle(desde), Nombres.Calle(hasta), grafo.GiroHacia(entrante, hasta));
            }
    }

    /// <summary><c>parametros.tsv</c>: todo lo necesario para repetir el estudio, una fila por parámetro.</summary>
    public static void EscribirParametros(Estudio estudio, IEnumerable<(string nombre, object valor)> filas)
    {
        using var registro = new RegistroTsv(System.IO.Path.Combine(estudio.Carpeta, "parametros.tsv"), new[] { "Parametro", "Valor" });
        foreach (var (nombre, valor) in filas) registro.Fila(nombre, valor);
    }

    /// <summary>
    /// <c>conductores.tsv</c>: una fila por coche con su modelo y todos los parámetros de su personalidad al empezar (datos en bruto).
    /// </summary>
    public static void EscribirConductores(Estudio estudio, IEnumerable<Vehiculos.Coche> flota)
    {
        using var registro = new RegistroTsv(System.IO.Path.Combine(estudio.Carpeta, "conductores.tsv"),
            new[] { "Coche", "Clase", "Modelo", "LargoM", "Tipo", "Agresividad", "ExcesoLimite", "TiempoSeparacionS", "DistanciaMinimaM",
                    "AceleracionMax", "FrenadaComoda", "AceleracionLateral", "TiempoReaccionS", "HuecoCriticoBaseS", "Cortesia", "UmbralCambioCarril",
                    "UsaNavegador", "PacienciaS", "Perfil", "Casa", "Trabajo", "Colegio", "HoraSalida", "HoraFinJornada", "ComeFuera" });
        foreach (var c in flota)
        {
            var d = c.Conductor;
            registro.Fila(c.Id, d.Clase, c.Modelo.Nombre, c.Longitud, d.Tipo, d.Agresividad, d.ExcesoLimite, d.TiempoSeparacionSeco, d.DistanciaMinimaSeco,
                d.AceleracionMaxSeco, d.FrenadaComodaSeco, d.AceleracionLateralSeco, d.TiempoReaccionSeco, d.HuecoCriticoBaseSeco, d.Cortesia, d.UmbralCambioCarril,
                d.UsaNavegador ? 1 : 0, d.PacienciaS, c.Rutina?.Perfil.ToString() ?? "", c.Rutina?.Casa.Nombre ?? "", c.Rutina?.Trabajo?.Nombre ?? "",
                c.Rutina?.Colegio?.Nombre ?? "", c.Rutina == null || !c.Rutina.Trabaja ? "" : HoraTexto(c.Rutina.HoraSalida),
                c.Rutina == null || !c.Rutina.Trabaja ? "" : HoraTexto(c.Rutina.HoraFin), c.Rutina is { Trabaja: true } ru ? (ru.ComeFuera ? 1 : 0) : "");
        }
    }

    /// <summary><c>atascos.tsv</c>: los atascos detectados, con la hora de inicio, la de fin (vacía si seguía) y los coches.</summary>
    public static void EscribirAtascos(Estudio estudio, DetectorAtascos detector)
    {
        using var registro = new RegistroTsv(System.IO.Path.Combine(estudio.Carpeta, "atascos.tsv"),
            new[] { "Calle", "Direccion", "HoraIni", "HoraFin", "Coches", "T_ini", "T_fin" });

        foreach (var a in detector.Historial.OrderBy(a => a.Inicio))
            registro.Fila(Nombres.Calle(a.Tramo), Nombres.Direccion(a.Tramo, a.Ida), estudio.Hora(a.Inicio),
                a.Fin != null ? estudio.Hora(a.Fin.Value) : "", a.MaxCoches, a.Inicio, a.Fin);
    }

    private static string HoraTexto(float horas) => $"{(int)horas:00}:{(int)(horas % 1f * 60f):00}";
}

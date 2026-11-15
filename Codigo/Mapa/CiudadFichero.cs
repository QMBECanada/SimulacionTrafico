using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;

namespace SimulacionTrafico.Mapa;

/// <summary>
/// Guarda y carga una ciudad en un fichero JSON. Una ciudad guardada es la misma siempre, aunque el generador cambie
/// más adelante, y sus nombres de calle y de cruce no se alteran: los ficheros de datos de estudios distintos son comparables.
/// Los identificadores se guardan en el orden en que se crearon, y al cargar se vuelven a crear en el mismo orden.
/// </summary>
public static class CiudadFichero
{
    private sealed class Datos
    {
        public int Version { get; set; } = 1;
        public int Semilla { get; set; }
        public double[] Limites { get; set; } = Array.Empty<double>();
        public List<NodoDatos> Nodos { get; set; } = new();
        public List<TramoDatos> Tramos { get; set; } = new();
        public List<LugarDatos> Lugares { get; set; } = new();
        public List<ManzanaDatos> Manzanas { get; set; } = new();
        public List<int> BasesGuardias { get; set; } = new();
        public List<int> Radares { get; set; } = new();
        public List<RotondaDatos> Rotondas { get; set; } = new();
        public double[]? Rio { get; set; }          // ancho, x0, y0, x1, y1...
    }

    private sealed class RotondaDatos
    {
        public string Nombre { get; set; } = "";
        public double X { get; set; }
        public double Y { get; set; }
        public double RadioIsla { get; set; }
    }

    private sealed class NodoDatos
    {
        public double X { get; set; }
        public double Y { get; set; }
        public string Nombre { get; set; } = "";
        public string Control { get; set; } = "";
        public double[]? Semaforo { get; set; }      // verde0, verde1, desfase, ámbar, todoRojo
        public List<int>? Prioritarios { get; set; } // tramos con prioridad
        public List<int>? Giros { get; set; }        // giros prohibidos: pares (calle de origen, calle de destino)
    }

    private sealed class TramoDatos
    {
        public int A { get; set; }
        public int B { get; set; }
        public string Nombre { get; set; } = "";
        public string Categoria { get; set; } = "";
        public int Ida { get; set; }
        public int Vuelta { get; set; }
        public double Limite { get; set; }
        public double[]? Eje { get; set; }           // x0, y0, x1, y1... solo si es curvo
        public bool Rotonda { get; set; }
        public bool Puente { get; set; }
        public bool Tunel { get; set; }
        public bool Ramal { get; set; }
    }

    private sealed class LugarDatos
    {
        public string Tipo { get; set; } = "";
        public string Nombre { get; set; } = "";
        public double[] Area { get; set; } = Array.Empty<double>();
        public int Tramo { get; set; }
        public bool Ida { get; set; }
        public int Indice { get; set; }
        public double S { get; set; }
        public double[] Puerta { get; set; } = Array.Empty<double>();
    }

    private sealed class ManzanaDatos
    {
        public double[] Rect { get; set; } = Array.Empty<double>();
        public bool Parque { get; set; }
        public int Semilla { get; set; }
        public int Lugar { get; set; } = -1;
    }

    private static readonly JsonSerializerOptions Opciones = new() { WriteIndented = false, PropertyNamingPolicy = null };

    public static void Guardar(Grafo g, string ruta, int semilla)
    {
        var d = new Datos
        {
            Semilla = semilla,
            Limites = new double[] { g.Limites.Position.X, g.Limites.Position.Y, g.Limites.Size.X, g.Limites.Size.Y },
            BasesGuardias = g.BasesGuardias.Select(n => n.Id).ToList(),
            Radares = g.Radares.Select(r => r.Tramo.Id).ToList(),
        };

        foreach (var n in g.Nodos)
            d.Nodos.Add(new NodoDatos
            {
                X = n.Pos.X, Y = n.Pos.Y, Nombre = n.Nombre, Control = n.Control.ToString(),
                Semaforo = n.Semaforo == null ? null
                    : new double[] { n.Semaforo.Verde0, n.Semaforo.Verde1, n.Semaforo.Desfase, n.Semaforo.Ambar, n.Semaforo.TodoRojo },
                Prioritarios = n.Prioritarios.Count == 0 ? null : n.Prioritarios.Select(t => t.Id).OrderBy(i => i).ToList(),
                Giros = n.GirosProhibidos.Count == 0 ? null : n.GirosProhibidos.OrderBy(p => p.desde).ThenBy(p => p.hasta).SelectMany(p => new[] { p.desde, p.hasta }).ToList(),
            });

        foreach (var t in g.Tramos)
            d.Tramos.Add(new TramoDatos
            {
                A = t.A.Id, B = t.B.Id, Nombre = t.Nombre, Categoria = t.Categoria.ToString(), Ida = t.CarrilesIda,
                Vuelta = t.CarrilesVuelta, Limite = t.LimiteKmh, Rotonda = t.EsRotonda, Puente = t.EsPuente, Tunel = t.EsTunel, Ramal = t.EsRamal,
                Eje = t.Eje.Puntos.Length > 2 ? t.Eje.Puntos.SelectMany(p => new double[] { p.X, p.Y }).ToArray() : null,
            });

        if (g.Rio != null)
            d.Rio = new[] { (double)g.Rio.Ancho }.Concat(g.Rio.Puntos.SelectMany(p => new double[] { Math.Round(p.X, 1), Math.Round(p.Y, 1) })).ToArray();

        foreach (var r in g.Rotondas)
            d.Rotondas.Add(new RotondaDatos { Nombre = r.Nombre, X = r.Centro.X, Y = r.Centro.Y, RadioIsla = r.RadioIsla });

        foreach (var l in g.Lugares)
            d.Lugares.Add(new LugarDatos
            {
                Tipo = l.Tipo.ToString(), Nombre = l.Nombre,
                Area = new double[] { l.Area.Position.X, l.Area.Position.Y, l.Area.Size.X, l.Area.Size.Y },
                Tramo = l.Acceso.Tramo.Id, Ida = l.Acceso.Ida, Indice = l.Acceso.Indice, S = l.SAcceso,
                Puerta = new double[] { l.Puerta.X, l.Puerta.Y },
            });

        foreach (var m in g.Manzanas)
            d.Manzanas.Add(new ManzanaDatos
            {
                Rect = new double[] { m.Rect.Position.X, m.Rect.Position.Y, m.Rect.Size.X, m.Rect.Size.Y },
                Parque = m.Parque, Semilla = m.Semilla, Lugar = m.Lugar?.Id ?? -1,
            });

        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        File.WriteAllText(ruta, JsonSerializer.Serialize(d, Opciones));
    }

    public static Grafo Cargar(string ruta, out int semilla)
    {
        // Una ruta res:// está dentro del programa (también en un ejecutable exportado): se lee con las funciones de Godot.
        string texto = ruta.StartsWith("res://") ? Godot.FileAccess.GetFileAsString(ruta) : File.ReadAllText(ruta);
        if (texto.Length == 0) throw new FileNotFoundException($"No se puede leer la ciudad {ruta}.");
        var d = JsonSerializer.Deserialize<Datos>(texto, Opciones)
                ?? throw new InvalidDataException($"La ciudad {ruta} está vacía.");
        semilla = d.Semilla;

        var g = new Grafo(new Rect2((float)d.Limites[0], (float)d.Limites[1], (float)d.Limites[2], (float)d.Limites[3]));

        foreach (var nd in d.Nodos)
        {
            var n = g.AgregarNodo(new Vector2((float)nd.X, (float)nd.Y));
            n.Nombre = nd.Nombre;
            n.Control = Enum.Parse<TipoControl>(nd.Control);
            if (nd.Semaforo is { Length: 5 } s)
                n.Semaforo = new Semaforo((float)s[0], (float)s[1], (float)s[2], (float)s[3], (float)s[4]);
        }

        foreach (var td in d.Tramos)
        {
            Trayectoria? eje = td.Eje == null ? null
                : new Trayectoria(Enumerable.Range(0, td.Eje.Length / 2).Select(i => new Vector2((float)td.Eje[2 * i], (float)td.Eje[2 * i + 1])));
            var t = g.AgregarTramo(g.Nodos[td.A], g.Nodos[td.B], Enum.Parse<CategoriaVia>(td.Categoria), td.Ida, td.Vuelta, (float)td.Limite, eje);
            t.Nombre = td.Nombre;
            t.EsRotonda = td.Rotonda;
            t.EsPuente = td.Puente;
            t.EsTunel = td.Tunel;
            t.EsRamal = td.Ramal;
        }

        if (d.Rio is { Length: > 4 } r)
            g.Rio = new Rio(Enumerable.Range(0, (r.Length - 1) / 2).Select(i => new Vector2((float)r[1 + 2 * i], (float)r[2 + 2 * i])).ToArray(), (float)r[0]);

        for (int i = 0; i < d.Nodos.Count; i++)
        {
            foreach (int id in d.Nodos[i].Prioritarios ?? new List<int>()) g.Nodos[i].Prioritarios.Add(g.Tramos[id]);
            var giros = d.Nodos[i].Giros;
            for (int k = 0; giros != null && k + 1 < giros.Count; k += 2) g.Nodos[i].GirosProhibidos.Add((giros[k], giros[k + 1]));
        }

        g.Construir();

        foreach (var ld in d.Lugares)
        {
            var tramo = g.Tramos[ld.Tramo];
            var carril = tramo.Carriles.First(c => c.Ida == ld.Ida && c.Indice == ld.Indice);
            g.AgregarLugar(new Lugar(g.Lugares.Count, Enum.Parse<TipoLugar>(ld.Tipo), ld.Nombre,
                new Rect2((float)ld.Area[0], (float)ld.Area[1], (float)ld.Area[2], (float)ld.Area[3]), carril, (float)ld.S,
                new Vector2((float)ld.Puerta[0], (float)ld.Puerta[1])));
        }

        foreach (var md in d.Manzanas)
            g.Manzanas.Add(new Manzana(new Rect2((float)md.Rect[0], (float)md.Rect[1], (float)md.Rect[2], (float)md.Rect[3]), md.Parque, md.Semilla)
            {
                Lugar = md.Lugar >= 0 ? g.Lugares[md.Lugar] : null,
            });

        foreach (var rd in d.Rotondas) g.Rotondas.Add(new Rotonda(rd.Nombre, new Vector2((float)rd.X, (float)rd.Y), (float)rd.RadioIsla));
        // Los tramos del anillo se llaman "R-fila-columna-k": se enlazan con su rotonda "R-fila-columna".
        foreach (var t in g.Tramos.Where(t => t.EsRotonda))
        {
            var rot = g.Rotondas.FirstOrDefault(r => r.Nombre == t.Nombre[..t.Nombre.LastIndexOf('-')]);
            if (rot == null) continue;
            t.Rotonda = rot;
            rot.Tramos.Add(t);
        }
        foreach (int id in d.BasesGuardias) g.BasesGuardias.Add(g.Nodos[id]);
        foreach (int id in d.Radares)
        {
            var radar = new Radar(g.Radares.Count, g.Tramos[id]);
            g.Tramos[id].Radar = radar;
            g.Radares.Add(radar);
        }
        return g;
    }
}

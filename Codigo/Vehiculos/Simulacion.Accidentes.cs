using System.Collections.Generic;
using System.Linq;
using Godot;
using SimulacionTrafico.Nucleo;
using SimulacionTrafico.Mapa;
using SimulacionTrafico.Datos;
using SimulacionTrafico.Trafico;

namespace SimulacionTrafico.Vehiculos;

// Simulación: accidentes (choques entre coches, carriles cortados y su registro).
public partial class Simulacion : Node
{
    private readonly DetectorChoques _detector = new();
    private readonly List<Accidente> _accidentesActivos = new();
    private RegistroTsv? _registroAccidentes;

    /// <summary>Si es false no se buscan choques entre coches (para comparar).</summary>
    public bool DetectarAccidentes { get; set; } = true;

    /// <summary>Accidentes en curso (coches aún inmóviles).</summary>
    public IReadOnlyList<Accidente> AccidentesActivos => _accidentesActivos;

    /// <summary>Accidentes ocurridos desde el principio.</summary>
    public int TotalAccidentes { get; private set; }

    /// <summary>
    /// Busca coches que se han golpeado y reanuda a los de los accidentes que ya han durado lo suyo. Un golpe cuenta como accidente
    /// si los cuerpos se solapan lo bastante y con velocidad relativa suficiente (un roce a poca velocidad en una cola no lo es).
    /// </summary>
    private void ProcesarAccidentes()
    {
        double t = TiempoSimulado;
        Central.Revisar(t);
        for (int i = _accidentesActivos.Count - 1; i >= 0; i--)
        {
            var acc = _accidentesActivos[i];
            if (t < acc.Fin) continue;
            foreach (var c in acc.Coches) c.Reanudar(t + Accidente.InmunidadS, Central);
            _registroAccidentes?.Fila(Estudio!.Hora(t), t, "Fin", acc.Id, acc.Lugar, acc.Pos.X, acc.Pos.Y, "", "", "", "", "", "", "", "", "", "", acc.Coches.Count, DescribirCarriles(acc));
            _accidentesActivos.RemoveAt(i);
        }
        if (!DetectarAccidentes) return;

        foreach (var (a, b, prof) in _detector.Buscar(Coches))
        {
            if (prof < Config.ProfundidadMinimaChoque) continue;
            float relV = (Vector2.FromAngle(a.Rumbo) * a.V - Vector2.FromAngle(b.Rumbo) * b.V).Length();
            if (relV < Config.VelocidadRelativaMinimaChoque) continue;
            if (a.InmuneHasta > t || b.InmuneHasta > t) continue;
            bool ea = a.EnAccidente(t), eb = b.EnAccidente(t);
            if (ea && eb) continue;
            float va = a.V, vb = b.V;   // antes de que los coches se detengan

            if (ea || eb)
            {
                // Otro coche choca contra uno ya accidentado: se suma al accidente y también se queda quieto.
                var parado = ea ? a : b;
                var nuevo = ea ? b : a;
                var acc = parado.Accidente!;
                nuevo.Detener(t + Config.DuracionAccidenteS, acc);
                acc.Coches.Add(nuevo);
                acc.Fin = System.Math.Max(acc.Fin, t + Config.DuracionAccidenteS);
                CortarCarriles(acc, nuevo);
                nuevo.AvisarAccidente(Central);
                RegistrarChoque("Suma", acc, nuevo, parado, nuevo == a ? va : vb, parado == a ? va : vb, prof, relV, t);
            }
            else
            {
                var acc = new Accidente(++TotalAccidentes, t, (a.Pos + b.Pos) / 2f, DescribirLugar(a, b), Config.DuracionAccidenteS);
                a.Detener(acc.Fin, acc);
                b.Detener(acc.Fin, acc);
                acc.Coches.Add(a);
                acc.Coches.Add(b);
                CortarCarriles(acc, a);
                CortarCarriles(acc, b);
                a.AvisarAccidente(Central);   // los implicados avisan del corte
                b.AvisarAccidente(Central);
                _accidentesActivos.Add(acc);
                RegistrarChoque("Inicio", acc, a, b, va, vb, prof, relV, t);
            }
        }
    }

    /// <summary>
    /// Corta los carriles de los coches implicados hasta el final del accidente: el de la calle donde están o, si están en un cruce, los de
    /// entrada y salida de su giro. Nadie entra en ellos mientras tanto.
    /// </summary>
    private static void CortarCarriles(Accidente acc, Coche c)
    {
        var carriles = c.Via switch
        {
            Carril l => new[] { l },
            Conexion cx => new[] { cx.Origen, cx.Destino },
            _ => System.Array.Empty<Carril>(),
        };
        foreach (var l in carriles)
        {
            if (!acc.Carriles.Contains(l)) acc.Carriles.Add(l);
        }
        foreach (var l in acc.Carriles) l.CortadoHasta = System.Math.Max(l.CortadoHasta, acc.Fin);
    }

    private static string DescribirCarriles(Accidente acc) =>
        string.Join(";", acc.Carriles.Select(l => $"{Nombres.Calle(l.Tramo)}/{Nombres.Direccion(l.Tramo, l.Ida)} carril {l.Indice + 1}"));

    /// <summary>Dónde ha sido: el cruce (con su tipo de control) o la calle y el sentido.</summary>
    private static string DescribirLugar(Coche a, Coche b)
    {
        var via = a.Via is Conexion ? a.Via : b.Via;
        if (via is Conexion cx)
        {
            var nodo = cx.Origen.Destino;
            return $"{Nombres.Cruce(nodo)} ({(cx.Origen.Tramo.EsRotonda || cx.Destino.Tramo.EsRotonda ? "Rotonda" : nodo.Control.ToString())})";
        }
        var l = (Carril)a.Via;
        return $"{Nombres.Calle(l.Tramo)} {Nombres.Direccion(l.Tramo, l.Ida)}";
    }

    private static string DescribirVia(Coche c) => c.Via switch
    {
        Carril l => $"{Nombres.Calle(l.Tramo)}/{Nombres.Direccion(l.Tramo, l.Ida)} carril {l.Indice + 1}",
        Conexion cx => $"cruce {Nombres.Cruce(cx.Origen.Destino)}: {Nombres.Calle(cx.Origen.Tramo)} a {Nombres.Calle(cx.Destino.Tramo)} ({cx.Giro})",
        _ => "",
    };

    private void RegistrarChoque(string evento, Accidente acc, Coche uno, Coche otro, float v1, float v2, float profundidad, float velRelativa, double t)
    {
        uno.ChoquesViaje++;
        otro.ChoquesViaje += evento == "Inicio" ? 1 : 0;
        _registroAccidentes?.Fila(Estudio!.Hora(t), t, evento, acc.Id, acc.Lugar, acc.Pos.X, acc.Pos.Y, uno.Id, otro.Id,
            System.Math.Round(v1 * 3.6, 1), System.Math.Round(v2 * 3.6, 1), DescribirVia(uno), DescribirVia(otro), uno.Conductor.Tipo, otro.Conductor.Tipo,
            System.Math.Round(profundidad, 2), System.Math.Round(velRelativa * 3.6, 1), "", DescribirCarriles(acc));
    }
}

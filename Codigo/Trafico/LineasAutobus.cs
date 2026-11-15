using System.Collections.Generic;
using System.Linq;
using Godot;
using SimulacionTrafico.Mapa;
using SimulacionTrafico.Nucleo;

namespace SimulacionTrafico.Trafico;

/// <summary>Línea de autobús: sus paradas en orden (ida y vuelta) y cuántos autobuses la recorren.</summary>
public sealed class LineaAutobus
{
    public string Nombre { get; init; } = "";
    public List<Lugar> Paradas { get; } = new();
    public int Autobuses { get; set; }
}

/// <summary>
/// Líneas de autobús: una por cada arteria recta de más de 1 km, de ida y vuelta por ella. Cada unos 400 m hay una parada en cada
/// sentido, en una dársena junto al carril derecho: el autobús se aparta del carril, para y se vuelve a incorporar cuando hay hueco. Se
/// calculan al empezar la simulación a partir de la ciudad (no se guardan en su fichero). Cada línea lleva los autobuses necesarios
/// para pasar cada unos 10 minutos.
/// </summary>
public static class LineasAutobus
{
    public const float SeparacionParadas = 400f;
    public const float FrecuenciaS = 600f;
    private const float VelocidadComercial = 18f / 3.6f;   // con paradas y semáforos
    private const float LargoMinimo = 1000f;

    public static List<LineaAutobus> Crear(Grafo grafo)
    {
        var lineas = new List<LineaAutobus>();
        // Arterias rectas, agrupadas por su recta (horizontal o vertical, y su coordenada).
        var porRecta = new Dictionary<(bool h, int c), List<Tramo>>();
        foreach (var t in grafo.Tramos)
        {
            if (t.Categoria != CategoriaVia.Arteria || t.EsRotonda || t.EsRamal || t.EsTunel || t.Eje.Puntos.Length != 2) continue;
            if (t.CarrilesIda == 0 || t.CarrilesVuelta == 0) continue;
            bool h = Mathf.IsEqualApprox(t.A.Pos.Y, t.B.Pos.Y);
            if (!h && !Mathf.IsEqualApprox(t.A.Pos.X, t.B.Pos.X)) continue;
            var clave = (h, Mathf.RoundToInt(h ? t.A.Pos.Y : t.A.Pos.X));
            if (!porRecta.TryGetValue(clave, out var lista)) porRecta[clave] = lista = new List<Tramo>();
            lista.Add(t);
        }

        int numero = 1;
        foreach (var (clave, tramos) in porRecta.OrderBy(k => k.Key.h ? 0 : 1).ThenBy(k => k.Key.c))
        {
            float Coord(Vector2 p) => clave.h ? p.X : p.Y;
            tramos.Sort((a, b) => Mathf.Min(Coord(a.A.Pos), Coord(a.B.Pos)).CompareTo(Mathf.Min(Coord(b.A.Pos), Coord(b.B.Pos))));
            float largo = tramos.Sum(t => t.Eje.Longitud);
            if (largo < LargoMinimo) continue;

            var linea = new LineaAutobus { Nombre = $"L{numero}" };
            var candidatas = new List<(Carril carril, float s, Vector2 puerta, string nombre)>();
            // Ida, en el sentido creciente de la coordenada; vuelta, en el contrario. Una parada cada SeparacionParadas, a mitad de tramo.
            foreach (bool ida in new[] { true, false })
            {
                var orden = ida ? tramos : Enumerable.Reverse(tramos).ToList();
                float acumulado = SeparacionParadas / 2f;
                int k = 1;
                foreach (var t in orden)
                {
                    acumulado += t.Eje.Longitud;
                    if (acumulado < SeparacionParadas || t.Eje.Longitud < 60f) continue;
                    // El carril derecho del sentido de la línea en este tramo.
                    bool sentidoAB = Coord(t.B.Pos) > Coord(t.A.Pos) == ida;
                    var carril = t.Carriles.Where(l => l.Ida == sentidoAB).OrderByDescending(l => l.Indice).FirstOrDefault();
                    if (carril == null) continue;
                    float s = 0.55f * carril.Longitud;
                    var p = carril.Eje.Posicion(s);
                    var d = carril.Eje.Direccion(s);
                    var derecha = new Vector2(-d.Y, d.X);
                    var puerta = p + derecha * (Escala.AnchoCarril / 2f + 1.6f);
                    candidatas.Add((carril, s, puerta, $"Parada-{linea.Nombre}-{(ida ? "ida" : "vuelta")}-{k++}"));
                    acumulado = 0f;
                }
            }
            if (candidatas.Count < 4) continue;
            foreach (var (carril, s, puerta, nombre) in candidatas)
            {
                var parada = new Lugar(grafo.Lugares.Count, TipoLugar.Parada, nombre, new Rect2(puerta - new Vector2(8f, 8f), new Vector2(16f, 16f)), carril, s, puerta);
                grafo.AgregarLugar(parada);
                linea.Paradas.Add(parada);
            }
            float vuelta = 2f * largo / VelocidadComercial + linea.Paradas.Count * 25f;
            linea.Autobuses = Mathf.Max(1, Mathf.CeilToInt(vuelta / FrecuenciaS));
            lineas.Add(linea);
            numero++;
        }
        return lineas;
    }
}

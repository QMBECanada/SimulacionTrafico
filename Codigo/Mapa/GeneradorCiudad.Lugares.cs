using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SimulacionTrafico.Nucleo;

namespace SimulacionTrafico.Mapa;

// GeneradorCiudad: zonas de usos y lugares (los destinos de los coches).
public static partial class GeneradorCiudad
{
    // ---- Lugares (destinos de los coches) ----

    // Probabilidad relativa de cada tipo de lugar según la zona: centro, zona intermedia y afueras.
    // ---- Zonas de usos ----

    /// <summary>Zona de la ciudad con un uso dominante: dentro de su radio, los lugares se eligen con su mezcla de tipos.</summary>
    private sealed record Zona(string Nombre, Vector2 Centro, float Radio, (TipoLugar tipo, float peso)[] Mezcla, float ProbLugar);

    private static readonly (TipoLugar, float)[] MezclaNegocios =
        { (TipoLugar.Oficinas, 55f), (TipoLugar.Restaurante, 14f), (TipoLugar.CentroComercial, 10f), (TipoLugar.Vivienda, 10f),
          (TipoLugar.Parking, 6f), (TipoLugar.Gimnasio, 3f), (TipoLugar.Hospital, 2f) };
    private static readonly (TipoLugar, float)[] MezclaIndustrial =
        { (TipoLugar.Fabrica, 80f), (TipoLugar.Oficinas, 8f), (TipoLugar.Gasolinera, 6f), (TipoLugar.Restaurante, 6f) };
    private static readonly (TipoLugar, float)[] MezclaComercial =
        { (TipoLugar.CentroComercial, 70f), (TipoLugar.Restaurante, 18f), (TipoLugar.Parking, 8f), (TipoLugar.Gasolinera, 4f) };
    private static readonly (TipoLugar, float)[] MezclaOcio =
        { (TipoLugar.Restaurante, 50f), (TipoLugar.Gimnasio, 28f), (TipoLugar.CentroComercial, 10f), (TipoLugar.Vivienda, 12f) };
    private static readonly (TipoLugar, float)[] MezclaMedica =
        { (TipoLugar.Hospital, 70f), (TipoLugar.Oficinas, 10f), (TipoLugar.Restaurante, 8f), (TipoLugar.Parking, 12f) };
    /// <summary>Barrios residenciales (todo lo que no es otra zona): viviendas con algún colegio, tienda, bar o gasolinera.</summary>
    private static readonly (TipoLugar, float)[] MezclaResidencial =
        { (TipoLugar.Vivienda, 82f), (TipoLugar.Colegio, 6f), (TipoLugar.Restaurante, 4f), (TipoLugar.CentroComercial, 3f),
          (TipoLugar.Gimnasio, 2f), (TipoLugar.Gasolinera, 2f), (TipoLugar.Oficinas, 1f) };

    /// <summary>
    /// Zonas de usos, para que la ciudad sea coherente: un distrito de negocios en el centro; polígonos industriales en la periferia
    /// (uno por cada 10 km², al menos uno); parques comerciales (uno por cada 6 km²), zonas de ocio (uno por cada 5 km²) y centros
    /// médicos (uno por cada 8 km²) repartidos; el resto, barrios residenciales.
    /// </summary>
    private static List<Zona> CrearZonas(Grafo g, RandomNumberGenerator rng)
    {
        var zonas = new List<Zona>();
        var c = g.Limites.GetCenter();
        var mitad = g.Limites.Size / 2f;
        float areaKm2 = g.Limites.Size.X * g.Limites.Size.Y / 1e6f;
        float lado = Mathf.Min(g.Limites.Size.X, g.Limites.Size.Y);
        zonas.Add(new Zona("negocios", c, Mathf.Max(250f, 0.15f * lado), MezclaNegocios, 0.75f));

        // Punto al azar a una «distancia relativa» del centro entre u0 y u1 (0 = centro, 1 = borde).
        Vector2 Punto(float u0, float u1)
        {
            float u = rng.RandfRange(u0, u1);
            float angulo = rng.RandfRange(0f, Mathf.Tau);
            var d = new Vector2(Mathf.Cos(angulo), Mathf.Sin(angulo));
            float escala = u / Mathf.Max(Mathf.Abs(d.X) / mitad.X, Mathf.Abs(d.Y) / mitad.Y);   // sobre el «cuadrado» de distancia u
            return c + d * escala;
        }
        int Cuantas(float porKm2) => Mathf.Max(1, Mathf.RoundToInt(areaKm2 / porKm2));
        for (int k = 0; k < Cuantas(10f); k++) zonas.Add(new Zona("industrial", Punto(0.72f, 0.9f), rng.RandfRange(250f, 450f), MezclaIndustrial, 0.8f));
        for (int k = 0; k < Cuantas(6f); k++) zonas.Add(new Zona("comercial", Punto(0.45f, 0.85f), rng.RandfRange(100f, 180f), MezclaComercial, 0.85f));
        for (int k = 0; k < Cuantas(5f); k++) zonas.Add(new Zona("ocio", Punto(0.2f, 0.7f), rng.RandfRange(120f, 200f), MezclaOcio, 0.8f));
        for (int k = 0; k < Cuantas(8f); k++) zonas.Add(new Zona("medica", Punto(0.2f, 0.8f), rng.RandfRange(80f, 130f), MezclaMedica, 0.85f));
        return zonas;
    }

    private static TipoLugar Sortear((TipoLugar tipo, float peso)[] mezcla, RandomNumberGenerator rng)
    {
        float total = mezcla.Sum(m => m.peso);
        float r = rng.Randf() * total;
        foreach (var (tipo, peso) in mezcla)
        {
            r -= peso;
            if (r <= 0f) return tipo;
        }
        return mezcla[^1].tipo;
    }

    /// <summary>
    /// Elige las manzanas que albergan un lugar (más en el centro y en las zonas de usos que en los barrios), su tipo según la zona y
    /// coloca la puerta en el carril de una de las calles que rodean la manzana, el más cercano a ella.
    /// </summary>
    private static void CrearLugares(Grafo g, Func<int, int, int, Nodo> punta, List<(Manzana m, int i, int j, int i1, int j1)> bloques, RandomNumberGenerator rng)
    {
        var contador = new Dictionary<TipoLugar, int>();
        var zonas = CrearZonas(g, new RandomNumberGenerator { Seed = rng.Randi() });
        var centro = g.Limites.GetCenter();
        var mitad = g.Limites.Size / 2f;

        foreach (var (m, i, j, i1, j1) in bloques)
        {
            if (m.Parque || m.Rect.Size.X < 20f || m.Rect.Size.Y < 20f) continue;
            var c = m.Rect.GetCenter();
            float u = Mathf.Clamp(Mathf.Max(Mathf.Abs(c.X - centro.X) / mitad.X, Mathf.Abs(c.Y - centro.Y) / mitad.Y), 0f, 1f);
            // La zona más pequeña que contiene la manzana manda; fuera de todas, barrio residencial.
            var zona = zonas.Where(z => z.Centro.DistanceTo(c) <= z.Radio).OrderBy(z => z.Radio).FirstOrDefault();
            if (rng.Randf() > (zona?.ProbLugar ?? Mathf.Lerp(0.6f, 0.4f, u))) continue;

            var tipo = Sortear(zona?.Mezcla ?? MezclaResidencial, rng);

            // Calles que rodean la manzana, agrupadas por lado; se elige un lado al azar (las gasolineras, la calle mayor).
            var lados = new List<List<Tramo>>
            {
                TramosEntre(punta, i, j, i1, j, horizontal: true),
                TramosEntre(punta, i, j1, i1, j1, horizontal: true),
                TramosEntre(punta, i, j, i, j1, horizontal: false),
                TramosEntre(punta, i1, j, i1, j1, horizontal: false),
            }.Where(l => l.Count > 0).ToList();
            if (lados.Count == 0) continue;

            List<Tramo> lado = tipo == TipoLugar.Gasolinera
                ? lados.OrderByDescending(l => l.Max(t => t.Categoria)).First()
                : lados[rng.RandiRange(0, lados.Count - 1)];
            var tramo = lado.OrderBy(t => t.Eje.Posicion(t.Eje.ProyectarS(c)).DistanceSquaredTo(c)).First();

            // Carril más cercano a la manzana: el que más se desplaza hacia el lado donde está.
            float sEje = tramo.Eje.ProyectarS(c);
            var dirEje = tramo.Eje.Direccion(sEje);
            var derecha = new Vector2(-dirEje.Y, dirEje.X);
            float ladoManzana = Mathf.Sign((c - tramo.Eje.Posicion(sEje)).Dot(derecha));
            var carril = tramo.Carriles.OrderByDescending(l => ladoManzana * tramo.OffsetCentroCarril(l.Ida, l.Indice)).First();

            float largo = carril.Longitud;
            float sPuerta = Mathf.Clamp(carril.Eje.ProyectarS(c), 0.25f * largo, 0.75f * largo);
            var puerta = carril.Eje.Posicion(sPuerta) + derecha * ladoManzana * (Escala.AnchoCarril / 2f + 0.8f);

            contador[tipo] = contador.GetValueOrDefault(tipo) + 1;
            var lugar = new Lugar(g.Lugares.Count, tipo, $"{tipo}-{contador[tipo]}", m.Rect, carril, sPuerta, puerta);
            m.Lugar = lugar;
            g.AgregarLugar(lugar);
        }
    }

    /// <summary>Tramos que existen entre los nodos (a1,b1) y (a2,b2) de un mismo lado de una manzana, en orden.</summary>
    private static List<Tramo> TramosEntre(Func<int, int, int, Nodo> punta, int i0, int j0, int i1, int j1, bool horizontal)
    {
        var res = new List<Tramo>();
        int pasos = horizontal ? i1 - i0 : j1 - j0;
        for (int k = 0; k < pasos; k++)
        {
            var a = horizontal ? punta(i0 + k, j0, 0) : punta(i0, j0 + k, 1);
            var b = horizontal ? punta(i0 + k + 1, j0, 2) : punta(i0, j0 + k + 1, 3);
            var t = a.Tramos.FirstOrDefault(x => (x.A == a && x.B == b) || (x.A == b && x.B == a));
            if (t != null) res.Add(t);
        }
        return res;
    }
}

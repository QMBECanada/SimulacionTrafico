using System.Collections.Generic;
using System.Linq;
using Godot;
using SimulacionTrafico.Camara;
using SimulacionTrafico.UI;

namespace SimulacionTrafico.Mapa;

/// <summary>
/// Dibuja el grafo de calles. La ciudad se reparte en chunks de 500 m; cada chunk tiene sus capas
/// y Godot solo dibuja las que caen dentro de la pantalla. Los detalles (rayas de carril, edificios
/// individuales, árboles) se ocultan al alejar el zoom.
/// Capas (z): 0 manzanas, 1 bordes de calle, 2..5 relleno por categoría, 6 marcas viales.
/// </summary>
public partial class VistaCiudad : Node2D
{
    private const float TamChunk = 500f;
    private const float ZoomDetalle = 0.5f; // px por metro a partir del cual se dibuja el detalle

    private static readonly Color ColorSuelo = new(0.89f, 0.86f, 0.78f);
    private static readonly Color ColorManzana = new(0.91f, 0.88f, 0.80f);
    private static readonly Color ColorEdificio = new(0.93f, 0.90f, 0.82f);
    private static readonly Color ColorParque = new(0.74f, 0.87f, 0.64f);
    private static readonly Color ColorArbol = new(0.55f, 0.76f, 0.47f);
    private static readonly Color ColorBordeCalle = new(0.78f, 0.74f, 0.62f);
    private static readonly Color ColorLineaCentral = new(0.80f, 0.60f, 0.10f);
    private static readonly Color ColorLineaCarril = new(0.72f, 0.70f, 0.62f);
    private static readonly Color ColorAgua = new(0.55f, 0.76f, 0.90f);
    private static readonly Color ColorOrilla = new(0.80f, 0.88f, 0.70f);
    private static readonly Color ColorBarandilla = new(0.36f, 0.36f, 0.40f);
    private static readonly Color ColorTunel = new(0.60f, 0.60f, 0.60f);
    // Gris pizarra: se distingue bien sobre todos los colores de las calles (que en este mapa son claros).
    private static readonly Color ColorFlecha = new(0.30f, 0.32f, 0.36f, 0.85f);
    private static readonly System.Collections.Generic.Dictionary<string, Texture2D> TexturasFlecha = new();

    /// <summary>Dibujo de flecha de Assets/flechas (blanco sobre transparente, mirando hacia arriba).</summary>
    private static Texture2D Flecha(string tipo)
    {
        if (TexturasFlecha.TryGetValue(tipo, out var t)) return t;
        var img = Image.LoadFromFile(ProjectSettings.GlobalizePath($"res://Assets/flechas/{tipo}.png"));
        img.GenerateMipmaps();
        return TexturasFlecha[tipo] = ImageTexture.CreateFromImage(img);
    }

    /// <summary>Pinta una flecha centrada en <paramref name="centro"/> que mira hacia <paramref name="dir"/>, de como mucho ese largo y ancho (m).</summary>
    private static void PintarFlecha(CanvasItem l, string tipo, Vector2 centro, Vector2 dir, float largo, float ancho)
    {
        var tex = Flecha(tipo);
        var tam = tex.GetSize();
        float escala = Mathf.Min(largo / tam.Y, ancho / tam.X);
        tam *= escala;
        l.DrawSetTransform(centro, dir.Angle() + Mathf.Pi / 2f);
        l.DrawTextureRect(tex, new Rect2(-tam / 2f, tam), false, ColorFlecha);
        l.DrawSetTransform(Vector2.Zero, 0f);
    }

    private static readonly Color[] ColorCalle =
    {
        new(1.00f, 0.99f, 0.94f),   // local
        new(1.00f, 0.96f, 0.62f),   // colectora
        new(0.98f, 0.85f, 0.35f),   // arteria
        new(0.96f, 0.68f, 0.20f),   // autovía
    };

    private sealed class Cubo
    {
        public readonly List<Tramo> Tramos = new();
        public readonly List<Nodo> Nodos = new();
        public readonly List<Manzana> Manzanas = new();
        public readonly List<Rotonda> Rotondas = new();
    }

    private const float ZoomCalles = 0.2f;  // por debajo solo se dibujan manzanas y arterias

    // Siempre: visible a cualquier zoom. Calles: calles locales/colectoras y contornos. Detalle: marcas, edificios, árboles.
    private enum Nivel { Siempre, Calles, Detalle }

    private readonly Grafo _grafo;
    private readonly CamaraCiudad _camara;
    private readonly List<Node2D> _capasCalles = new();
    private readonly List<Node2D> _capasDetalle = new();
    private readonly List<CanvasItem> _escenario = new();   // suelo, manzanas, edificios, río: se oscurecen más de noche
    private readonly List<CanvasItem> _vias = new();         // asfalto, bordes y marcas: se oscurecen menos
    private bool _callesVisible = true;
    private bool _detalleVisible = true;

    public int Chunks { get; private set; }

    public VistaCiudad(Grafo grafo, CamaraCiudad camara)
    {
        _grafo = grafo;
        _camara = camara;
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
    }

    public override void _Ready()
    {
        // Suelo bajo todo (aceras y espacios entre manzanas y calles).
        var suelo = new CapaDibujo(l => l.DrawRect(_grafo.Limites, ColorSuelo), -1);
        AddChild(suelo);
        _escenario.Add(suelo);
        if (_grafo.Rio is { } rio)
        {
            var capaRio = new CapaDibujo(l =>
            {
                // El río se prolonga más allá del borde y se recorta por el límite del mapa: entra y sale limpio por el borde,
                // sin sobresalir del área de la ciudad.
                var puntos = Prolongar(rio.Puntos, 400f);
                DibujarFranja(l, puntos, rio.Ancho + 70f, ColorOrilla);
                DibujarFranja(l, puntos, rio.Ancho + 3f, ColorAgua.Darkened(0.12f));
                DibujarFranja(l, puntos, rio.Ancho, ColorAgua);
            }, -1);
            AddChild(capaRio);
            _escenario.Add(capaRio);
        }

        var cubos = new Dictionary<Vector2I, Cubo>();
        Cubo De(Vector2 p)
        {
            var o = _grafo.Limites.Position;
            var k = new Vector2I(Mathf.FloorToInt((p.X - o.X) / TamChunk), Mathf.FloorToInt((p.Y - o.Y) / TamChunk));
            if (!cubos.TryGetValue(k, out var c)) cubos[k] = c = new Cubo();
            return c;
        }

        foreach (var t in _grafo.Tramos) De((t.A.Pos + t.B.Pos) / 2f).Tramos.Add(t);
        foreach (var n in _grafo.Nodos) De(n.Pos).Nodos.Add(n);
        foreach (var m in _grafo.Manzanas) De(m.Rect.GetCenter()).Manzanas.Add(m);
        foreach (var r in _grafo.Rotondas) De(r.Centro).Rotondas.Add(r);

        foreach (var cubo in cubos.Values) CrearCapas(cubo);
        Chunks = cubos.Count;
    }

    public override void _Process(double delta)
    {
        float zoom = _camara.PixelesPorMetro;
        Alternar(_capasCalles, zoom >= ZoomCalles, ref _callesVisible);
        Alternar(_capasDetalle, zoom >= ZoomDetalle, ref _detalleVisible);
    }

    private static void Alternar(List<Node2D> capas, bool ver, ref bool actual)
    {
        if (ver == actual) return;
        actual = ver;
        foreach (var capa in capas) capa.Visible = ver;
    }

    private void CrearCapas(Cubo c)
    {
        Agregar(new CapaDibujo(l => DibujarManzanas(l, c), 0), Nivel.Siempre, true);
        Agregar(new CapaDibujo(l => DibujarContornos(l, c), 0), Nivel.Calles, true);
        // Las islas de las rotondas van por encima de los edificios (que ocupan también esa zona), debajo del asfalto.
        if (c.Rotondas.Count > 0) Agregar(new CapaDibujo(l => DibujarIslas(l, c), 1), Nivel.Calles, true);
        Agregar(new CapaDibujo(l => DibujarEdificios(l, c), 0), Nivel.Detalle, true);

        if (c.Tramos.Any(t => t.EsPuente || t.EsTunel)) Agregar(new CapaDibujo(l => DibujarPuentes(l, c), 1), Nivel.Siempre);

        // Bordes y rellenos: las arterias y autovías se ven siempre; locales y colectoras solo al acercar.
        Agregar(new CapaDibujo(l => DibujarBordes(l, c, CategoriaVia.Local, CategoriaVia.Colectora), 1), Nivel.Calles);
        Agregar(new CapaDibujo(l => DibujarBordes(l, c, CategoriaVia.Arteria, CategoriaVia.Autovia), 1), Nivel.Siempre);
        for (int cat = 0; cat < ColorCalle.Length; cat++)
        {
            var categoria = (CategoriaVia)cat;
            if (!c.Tramos.Any(t => t.Categoria == categoria) && !c.Nodos.Any(n => n.CategoriaMaxima == categoria)) continue;
            Agregar(new CapaDibujo(l => DibujarRellenos(l, c, categoria), 2 + cat),
                categoria >= CategoriaVia.Arteria ? Nivel.Siempre : Nivel.Calles);
        }
        Agregar(new CapaDibujo(l => DibujarMarcas(l, c), 6), Nivel.Detalle);
    }

    private void Agregar(CapaDibujo capa, Nivel nivel, bool escenario = false)
    {
        AddChild(capa);
        (escenario ? _escenario : _vias).Add(capa);
        capa.Modulate = escenario ? _modEscenario : _modVias;
        if (nivel == Nivel.Calles)
        {
            _capasCalles.Add(capa);
            capa.Visible = _callesVisible;
        }
        else if (nivel == Nivel.Detalle)
        {
            _capasDetalle.Add(capa);
            capa.Visible = _detalleVisible;
        }
    }

    private static readonly Color NocheEscenario = new(0.30f, 0.34f, 0.52f);
    private static readonly Color NocheVias = new(0.58f, 0.61f, 0.76f);
    private Color _modEscenario = Colors.White, _modVias = Colors.White;

    /// <summary>Luz del día: 1 a pleno día (colores normales), 0 de noche (edificios y suelo oscuros y azulados, calles algo menos).</summary>
    public void SetLuz(float luz)
    {
        _modEscenario = NocheEscenario.Lerp(Colors.White, luz);
        _modVias = NocheVias.Lerp(Colors.White, luz);
        foreach (var c in _escenario) c.Modulate = _modEscenario;
        foreach (var c in _vias) c.Modulate = _modVias;
    }

    // ---- dibujo ----

    private static void DibujarManzanas(CanvasItem l, Cubo c)
    {
        foreach (var m in c.Manzanas)
        {
            // Los lugares (viviendas, oficinas...) se tiñen según su tipo.
            var color = m.Parque ? ColorParque
                      : m.Lugar != null ? ColorManzana.Lerp(Lugar.ColorDe(m.Lugar.Tipo), 0.65f)
                      : ColorManzana;
            l.DrawRect(m.Rect, color);
        }

    }

    private static void DibujarIslas(CanvasItem l, Cubo c)
    {
        foreach (var r in c.Rotondas)
        {
            l.DrawCircle(r.Centro, r.RadioIsla + 0.5f, ColorBordeCalle);
            l.DrawCircle(r.Centro, r.RadioIsla, ColorParque);
        }
    }

    private static void DibujarContornos(CanvasItem l, Cubo c)
    {
        foreach (var m in c.Manzanas)
            l.DrawRect(m.Rect, Colors.White, false, 1.5f);
    }

    /// <summary>Dibuja las calles agrupadas por anchura: un único comando por anchura.</summary>
    /// <summary>La polilínea con sus extremos prolongados en línea recta <paramref name="metros"/> más.</summary>
    private static Vector2[] Prolongar(Vector2[] p, float metros)
    {
        if (p.Length < 2) return p;
        var r = new Vector2[p.Length + 2];
        System.Array.Copy(p, 0, r, 1, p.Length);
        r[0] = p[0] + (p[0] - p[1]).Normalized() * metros;
        r[^1] = p[^1] + (p[^1] - p[^2]).Normalized() * metros;
        return r;
    }

    /// <summary>Franja de ese ancho a lo largo de la polilínea, recortada por los límites del mapa.</summary>
    private void DibujarFranja(CanvasItem l, Vector2[] puntos, float ancho, Color color)
    {
        var lim = _grafo.Limites;
        var marco = new[] { lim.Position, new Vector2(lim.End.X, lim.Position.Y), lim.End, new Vector2(lim.Position.X, lim.End.Y) };
        foreach (var franja in Geometry2D.OffsetPolyline(puntos, ancho / 2f, Geometry2D.PolyJoinType.Round, Geometry2D.PolyEndType.Butt))
        {
            if (Geometry2D.IsPolygonClockwise(franja) != Geometry2D.IsPolygonClockwise(marco)) continue;   // un hueco: no se rellena
            foreach (var trozo in Geometry2D.IntersectPolygons(franja, marco))
                if (trozo.Length >= 3) l.DrawColoredPolygon(trozo, color);
        }
    }

    private static void DibujarTramos(CanvasItem l, IEnumerable<Tramo> tramos, float extra, Color color)
    {
        var lista = tramos.ToList();

        // Las calles curvas se dibujan una a una como polilínea (une bien los segmentos, sin huecos en la curva).
        foreach (var t in lista.Where(t => t.Eje.Puntos.Length > 2))
            l.DrawPolyline(PuntosDibujo(t), color, t.AnchoCalzada + extra);

        foreach (var grupo in lista.Where(t => t.Eje.Puntos.Length == 2).GroupBy(t => t.AnchoCalzada))
        {
            var segmentos = new List<Vector2>();
            foreach (var t in grupo)
            {
                segmentos.Add(t.Eje.Puntos[0]);
                segmentos.Add(t.Eje.Puntos[1]);
            }
            l.DrawMultiline(segmentos.ToArray(), color, grupo.Key + extra);
        }
    }

    /// <summary>
    /// Puntos con los que se dibuja la calle. En los ramales de la autovía, el tramo lateral del eje (dentro del cruce) no se dibuja: el
    /// ramal empieza o acaba pegado al borde de la autovía y se prolonga unos metros junto a ella (cuña de salida o de entrada).
    /// </summary>
    private static Vector2[] PuntosDibujo(Tramo t)
    {
        if (!t.EsRamal || t.Eje.Puntos.Length < 4) return t.Eje.Puntos;
        var p = (Vector2[])t.Eje.Puntos.Clone();
        if (Grafo.EsAutovia(t.A)) p[0] = p[1] - (p[2] - p[1]).Normalized() * 16f;
        if (Grafo.EsAutovia(t.B)) p[^1] = p[^2] + (p[^2] - p[^3]).Normalized() * 16f;
        return p;
    }

    private static void DibujarEdificios(CanvasItem l, Cubo c)
    {
        var partes = new List<Rect2>();
        foreach (var m in c.Manzanas)
        {
            if (m.Lugar != null)
                l.DrawCircle(m.Lugar.Puerta, 2.2f, Lugar.ColorDe(m.Lugar.Tipo).Darkened(0.45f)); // la puerta
            var rng = new RandomNumberGenerator { Seed = (ulong)m.Semilla };
            if (m.Parque)
            {
                int arboles = Mathf.Clamp((int)(m.Rect.Size.X * m.Rect.Size.Y / 350f), 3, 30);
                for (int i = 0; i < arboles; i++)
                {
                    var p = m.Rect.Position + new Vector2(rng.Randf() * m.Rect.Size.X, rng.Randf() * m.Rect.Size.Y);
                    l.DrawCircle(p, rng.RandfRange(2.2f, 4.5f), ColorArbol);
                }
                continue;
            }
            partes.Clear();
            Subdividir(m.Rect, rng, partes, 0);
            foreach (var p in partes)
            {
                float v = rng.RandfRange(-0.04f, 0.04f);
                var color = new Color(ColorEdificio.R + v, ColorEdificio.G + v, ColorEdificio.B + v);
                l.DrawRect(p.Grow(-0.8f), color);
            }
        }
    }

    private static void Subdividir(Rect2 r, RandomNumberGenerator rng, List<Rect2> salida, int nivel)
    {
        if ((r.Size.X <= 36f && r.Size.Y <= 36f) || nivel > 4)
        {
            salida.Add(r);
            return;
        }
        float f = rng.RandfRange(0.35f, 0.65f);
        if (r.Size.X >= r.Size.Y)
        {
            float w = r.Size.X * f;
            Subdividir(new Rect2(r.Position, new Vector2(w, r.Size.Y)), rng, salida, nivel + 1);
            Subdividir(new Rect2(r.Position + new Vector2(w, 0), new Vector2(r.Size.X - w, r.Size.Y)), rng, salida, nivel + 1);
        }
        else
        {
            float h = r.Size.Y * f;
            Subdividir(new Rect2(r.Position, new Vector2(r.Size.X, h)), rng, salida, nivel + 1);
            Subdividir(new Rect2(r.Position + new Vector2(0, h), new Vector2(r.Size.X, r.Size.Y - h)), rng, salida, nivel + 1);
        }
    }

    /// <summary>Barandillas de los puentes y túneles (estos se dibujan grises, sin marcas: los coches no se ven dentro).</summary>
    private static void DibujarPuentes(CanvasItem l, Cubo c)
    {
        DibujarTramos(l, c.Tramos.Where(t => t.EsPuente), 3.2f, ColorBarandilla);
        var tuneles = c.Tramos.Where(t => t.EsTunel).ToList();
        DibujarTramos(l, tuneles, 2.4f, ColorBarandilla);
        DibujarTramos(l, tuneles, 0f, ColorTunel);
    }

    private static void DibujarBordes(CanvasItem l, Cubo c, CategoriaVia desde, CategoriaVia hasta)
    {
        DibujarTramos(l, c.Tramos.Where(t => !t.EsTunel && t.Categoria >= desde && t.Categoria <= hasta), 1.2f, ColorBordeCalle);
        foreach (var n in c.Nodos)
            if (n.Tramos.Count > 2 && n.CategoriaMaxima >= desde && n.CategoriaMaxima <= hasta)
                l.DrawCircle(n.Pos, n.Radio - 1f + 0.6f, ColorBordeCalle);
    }

    private static void DibujarRellenos(CanvasItem l, Cubo c, CategoriaVia categoria)
    {
        var color = ColorCalle[(int)categoria];
        DibujarTramos(l, c.Tramos.Where(t => !t.EsTunel && t.Categoria == categoria), 0f, color);
        foreach (var n in c.Nodos)
            if (n.Tramos.Count > 2 && n.CategoriaMaxima == categoria) l.DrawCircle(n.Pos, n.Radio - 1f, color);
    }

    private void DibujarMarcas(CanvasItem l, Cubo c)
    {
        var carriles = new List<Vector2>();
        foreach (var t in c.Tramos)
        {
            if (t.EsTunel) continue;
            if (t.CarrilesIda > 0 && t.CarrilesVuelta > 0)
                l.DrawPolyline(t.EjeUtil.Desplazar(t.OffsetLineaCentral).Puntos, ColorLineaCentral, 0.25f); // continua, también en curva
            for (int k = 1; k < t.CarrilesIda; k++)
                AgregarDiscontinua(t.EjeUtil.Desplazar(t.OffsetLineaCentral + k * Nucleo.Escala.AnchoCarril), carriles);
            for (int k = 1; k < t.CarrilesVuelta; k++)
                AgregarDiscontinua(t.EjeUtil.Desplazar(t.OffsetLineaCentral - k * Nucleo.Escala.AnchoCarril), carriles);
        }
        if (carriles.Count > 0) l.DrawMultiline(carriles.ToArray(), ColorLineaCarril, 0.15f);

        // Flechas pintadas en cada carril: antes de cada cruce, las salidas que se pueden tomar desde ese carril; y a lo largo de la
        // calle, el sentido de circulación.
        foreach (var t in c.Tramos)
            if (!t.EsTunel)
                foreach (var carril in t.Carriles)
                {
                    bool antesDelCruce = PintarFlechaCruce(l, carril);
                    PintarFlechasSentido(l, carril, antesDelCruce ? 16f : 0f);
                }
    }

    /// <summary>
    /// Flechas en el suelo antes del cruce (a 9 m de la línea de detención) con las salidas que se pueden tomar desde ese carril: seguir
    /// recto, girar a la izquierda o a la derecha, o varias a la vez. Tienen en cuenta los giros prohibidos y desde qué carril se puede
    /// girar (a la izquierda desde el de la izquierda, a la derecha desde el de la derecha). Devuelve false si no se pinta (calle muy corta,
    /// rotonda o cruce sin opciones).
    /// </summary>
    private bool PintarFlechaCruce(CanvasItem l, Carril carril)
    {
        var nodo = carril.Destino;
        if (carril.Longitud < 14f || nodo.Control == TipoControl.Rotonda || carril.Tramo.EsRotonda || nodo.Tramos.Count < 3) return false;

        bool recto = false, izquierda = false, derecha = false;
        foreach (var h in nodo.Salientes.Select(o => o.Tramo).Distinct())
        {
            if (h == carril.Tramo || nodo.GiroProhibido(carril.Tramo, h)) continue;
            var giro = _grafo.GiroHacia(carril, h);
            if (!Grafo.PermiteGiro(carril, giro)) continue;
            if (giro == TipoGiro.Recto) recto = true;
            else if (giro == TipoGiro.Izquierda) izquierda = true;
            else derecha = true;
        }
        string? tipo = (izquierda, recto, derecha) switch
        {
            (false, true, false) => "recto",
            (true, false, false) => "izquierda",
            (false, false, true) => "derecha",
            (true, true, false) => "izquierda_recto",
            (false, true, true) => "recto_derecha",
            (true, false, true) => "izquierda_derecha",
            (true, true, true) => "todas",
            _ => null,
        };
        if (tipo == null) return false;

        float s = carril.Longitud - 8.5f;
        PintarFlecha(l, tipo, carril.Eje.Posicion(s), carril.Eje.Direccion(s), 5f, 3.0f);
        return true;
    }

    /// <summary>
    /// Flechas de sentido repartidas por el carril (una cada unos 50 m), como las pintadas en el asfalto, sin acercarse a menos de
    /// <paramref name="margenFinal"/> metros del final (donde están las del cruce).
    /// </summary>
    private static void PintarFlechasSentido(CanvasItem l, Carril carril, float margenFinal = 0f)
    {
        float largo = carril.Longitud;
        if (largo < 16f) return;
        int n = Mathf.Max(1, (int)(largo / 50f));
        for (int k = 0; k < n; k++)
        {
            float s = largo * (k + 0.5f) / n;
            if (margenFinal > 0f && s > largo - margenFinal) continue;
            PintarFlecha(l, "recto", carril.Eje.Posicion(s), carril.Eje.Direccion(s), 4.5f, 2.0f);
        }
    }

    private static void AgregarDiscontinua(Trayectoria tr, List<Vector2> segmentos, float raya = 3f, float hueco = 5f)
    {
        for (float s = 0f; s < tr.Longitud; s += raya + hueco)
        {
            segmentos.Add(tr.Posicion(s));
            segmentos.Add(tr.Posicion(Mathf.Min(s + raya, tr.Longitud)));
        }
    }
}

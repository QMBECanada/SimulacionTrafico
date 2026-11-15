using Godot;
using SimulacionTrafico.Mapa;
using SimulacionTrafico.Nucleo;
using SimulacionTrafico.Vehiculos;

namespace Pruebas.Mapa;

/// <summary>El enrutador devuelve la ruta más rápida para el conductor, también cuando la más rápida va por la autovía.</summary>
public class EnrutadorTests
{
    /// <summary>
    /// Se llega por S→O. De O a D hay dos caminos: 8 km de carretera a 90 km/h, o subir 200 m, 8 km de autovía a 120 km/h y bajar 200 m.
    /// La puerta del destino está en la calle D→E.
    /// </summary>
    private sealed class Ciudad
    {
        public readonly Grafo G = new(new Rect2(-200f, -400f, 8600f, 600f));
        public readonly Tramo Llegada, Directa, Subida, Autovia, Bajada;
        public readonly Lugar Destino;

        public Ciudad(bool conAutovia = true)
        {
            var s = G.AgregarNodo(new Vector2(-100f, 0f));
            var o = G.AgregarNodo(Vector2.Zero);
            var d = G.AgregarNodo(new Vector2(8000f, 0f));
            var e = G.AgregarNodo(new Vector2(8100f, 0f));
            Llegada = G.AgregarTramo(s, o, CategoriaVia.Local, 1, 1, 50f);
            Directa = G.AgregarTramo(o, d, CategoriaVia.Arteria, 1, 1, 90f);
            var calleDestino = G.AgregarTramo(d, e, CategoriaVia.Local, 1, 1, 50f);
            if (conAutovia)
            {
                var u = G.AgregarNodo(new Vector2(0f, -200f));
                var v = G.AgregarNodo(new Vector2(8000f, -200f));
                Subida = G.AgregarTramo(o, u, CategoriaVia.Local, 1, 1, 50f);
                Autovia = G.AgregarTramo(u, v, CategoriaVia.Autovia, 2, 2, 120f);
                Bajada = G.AgregarTramo(v, d, CategoriaVia.Local, 1, 1, 50f);
            }
            else Subida = Autovia = Bajada = null!;
            G.Construir();
            var acceso = calleDestino.Carriles.First(l => l.Ida);
            Destino = new Lugar(0, TipoLugar.Oficinas, "destino", new Rect2(8040f, 10f, 20f, 20f), acceso, acceso.Longitud / 2f, new Vector2(8050f, 10f));
            G.AgregarLugar(Destino);
        }
    }

    /// <summary>Un conductor corriente: ni temeroso (rehúye la autovía) ni temerario.</summary>
    private static Conductor ConductorCorriente()
    {
        var contexto = new ContextoSimulacion(new ConfiguracionSimulacion());
        for (ulong semilla = 1; ; semilla++)
        {
            var c = new Conductor(new Aleatorio(semilla), contexto);
            if (c.Agresividad is > -0.3f and < 0.8f) return c;
        }
    }

    [Fact]
    public void Va_por_la_autovia_cuando_es_mas_rapido_aunque_de_un_rodeo()
    {
        var ciudad = new Ciudad();
        var ruta = new Enrutador(ciudad.G).Calcular(ciudad.Llegada, true, 0f, ciudad.Destino, ConductorCorriente());
        Assert.NotNull(ruta);
        Assert.Contains(ciudad.Autovia, ruta!);
        Assert.DoesNotContain(ciudad.Directa, ruta!);
    }

    /// <summary>Raíz del proyecto (donde está la carpeta Ciudades), subiendo desde la carpeta de la prueba.</summary>
    private static string RutaCiudad(int numero)
    {
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "Ciudades"))) dir = dir.Parent;
        return System.IO.Path.Combine(dir!.FullName, "Ciudades", $"ciudad{numero}.json");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void En_una_ciudad_real_las_cotas_dan_rutas_tan_rapidas_como_sin_cotas(int numero)
    {
        var g = CiudadFichero.Cargar(RutaCiudad(numero), out _);
        var conCotas = new Enrutador(g);
        var sinCotas = new Enrutador(g) { SinCotas = true };
        var contexto = new ContextoSimulacion(new ConfiguracionSimulacion());
        var azar = new Aleatorio(7);
        int comparadas = 0;
        for (int i = 0; i < 300; i++)
        {
            var conductor = new Conductor(azar.Derivar((ulong)i), contexto);   // de todos los caracteres: también los que evitan la autovía
            var o = g.Lugares[azar.RandiRange(0, g.Lugares.Count - 1)];
            var d = g.Lugares[azar.RandiRange(0, g.Lugares.Count - 1)];
            if (o == d) continue;
            float fraccion = o.SSalida / o.Salida.Longitud;
            var a = conCotas.Calcular(o.Salida.Tramo, o.Salida.Ida, fraccion, d, conductor);
            var b = sinCotas.Calcular(o.Salida.Tramo, o.Salida.Ida, fraccion, d, conductor);
            Assert.Equal(b == null, a == null);
            if (a == null) continue;
            Assert.Equal(sinCotas.UltimoCoste, conCotas.UltimoCoste, sinCotas.UltimoCoste * 1e-5f);
            comparadas++;
        }
        Assert.True(comparadas > 250);
        Assert.True(conCotas.Expansiones * 2 < sinCotas.Expansiones, $"las cotas apenas ahorran: {conCotas.Expansiones} frente a {sinCotas.Expansiones}");
    }

    [Fact]
    public void Sin_autovia_va_por_la_carretera_directa()
    {
        var ciudad = new Ciudad(conAutovia: false);
        var ruta = new Enrutador(ciudad.G).Calcular(ciudad.Llegada, true, 0f, ciudad.Destino, ConductorCorriente());
        Assert.NotNull(ruta);
        Assert.Equal(ciudad.Directa, ruta![0]);
    }
}

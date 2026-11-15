using Godot;
using SimulacionTrafico.Nucleo;

namespace Pruebas.Nucleo;

/// <summary>La rejilla encuentra lo mismo que recorrer todo con <see cref="Rect2.HasPoint"/>, y en el mismo orden.</summary>
public class RejillaEspacialTests
{
    private static (RejillaEspacial<int> rejilla, Vector2[] puntos) Rejilla(int n, float tamCelda, ulong semilla = 7)
    {
        var azar = new Aleatorio(semilla);
        var rejilla = new RejillaEspacial<int>(tamCelda);
        var puntos = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            // Coordenadas negativas y positivas, como en las ciudades (centradas en el origen).
            puntos[i] = new Vector2(azar.RandfRange(-3000f, 3000f), azar.RandfRange(-2000f, 2000f));
            rejilla.Agregar(puntos[i], i);
        }
        return (rejilla, puntos);
    }

    private static List<int> Buscar(RejillaEspacial<int> rejilla, Rect2 rect)
    {
        var salida = new List<int>();
        rejilla.Buscar(rect, salida);
        return salida;
    }

    [Fact]
    public void Cuenta_los_elementos_que_se_anaden()
    {
        var (rejilla, _) = Rejilla(500, 100f);
        Assert.Equal(500, rejilla.Count);
    }

    [Fact]
    public void Encuentra_lo_mismo_y_en_el_mismo_orden_que_recorrerlo_todo()
    {
        var (rejilla, puntos) = Rejilla(3000, 150f);
        var azar = new Aleatorio(99);
        for (int k = 0; k < 200; k++)
        {
            var pos = new Vector2(azar.RandfRange(-3500f, 3000f), azar.RandfRange(-2500f, 2000f));
            var rect = new Rect2(pos, new Vector2(azar.RandfRange(0f, 1500f), azar.RandfRange(0f, 1000f)));
            var esperado = Enumerable.Range(0, puntos.Length).Where(i => rect.HasPoint(puntos[i])).ToList();
            Assert.Equal(esperado, Buscar(rejilla, rect));
        }
    }

    [Fact]
    public void Un_rectangulo_que_lo_cubre_todo_los_devuelve_todos_en_orden()
    {
        var (rejilla, puntos) = Rejilla(1000, 200f);
        var todo = new Rect2(-1e6f, -1e6f, 2e6f, 2e6f);
        Assert.Equal(Enumerable.Range(0, puntos.Length).ToList(), Buscar(rejilla, todo));
    }

    [Fact]
    public void Los_bordes_siguen_el_criterio_de_HasPoint()
    {
        var rejilla = new RejillaEspacial<string>(100f);
        rejilla.Agregar(new Vector2(0f, 0f), "esquina");
        rejilla.Agregar(new Vector2(100f, 50f), "borde final");
        rejilla.Agregar(new Vector2(99.9f, 99.9f), "dentro");
        var salida = new List<string>();
        rejilla.Buscar(new Rect2(0f, 0f, 100f, 100f), salida);
        Assert.Equal(new[] { "esquina", "dentro" }, salida);
    }

    [Fact]
    public void Vacia_la_salida_antes_de_buscar()
    {
        var (rejilla, _) = Rejilla(100, 100f);
        var salida = new List<int> { -1, -2 };
        rejilla.Buscar(new Rect2(-1e6f, -1e6f, 2e6f, 2e6f), salida);
        Assert.DoesNotContain(-1, salida);
        Assert.Equal(100, salida.Count);
    }

    [Fact]
    public void Fuera_de_todo_no_encuentra_nada()
    {
        var (rejilla, _) = Rejilla(500, 100f);
        Assert.Empty(Buscar(rejilla, new Rect2(10000f, 10000f, 500f, 500f)));
    }

    [Fact]
    public void Un_rectangulo_de_tamano_negativo_o_nulo_no_encuentra_nada()
    {
        var (rejilla, _) = Rejilla(500, 100f);
        Assert.Empty(Buscar(rejilla, new Rect2(500f, 500f, -1000f, -1000f)));
        Assert.Empty(Buscar(rejilla, new Rect2(0f, 0f, 0f, 0f)));
    }

    [Fact]
    public void Las_fronteras_negativas_de_celda_siguen_el_criterio_de_HasPoint()
    {
        var rejilla = new RejillaEspacial<string>(250f);
        rejilla.Agregar(new Vector2(-250f, -500f), "en la frontera");
        rejilla.Agregar(new Vector2(-250.01f, -500f), "justo antes");
        var salida = new List<string>();
        rejilla.Buscar(new Rect2(-250f, -500f, 10f, 10f), salida);
        Assert.Equal(new[] { "en la frontera" }, salida);
        rejilla.Buscar(new Rect2(-260f, -505f, 10f, 10f), salida);
        Assert.Equal(new[] { "justo antes" }, salida);
    }
}

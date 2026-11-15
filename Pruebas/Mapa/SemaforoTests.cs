using Godot;
using SimulacionTrafico.Mapa;

namespace Pruebas.Mapa;

/// <summary>Aproximación de un carril que llega a un cruce: 0 hacia el este, 1 hacia el oeste, 2 hacia el sur, 3 hacia el norte (Y hacia abajo).</summary>
public class SemaforoTests
{
    private static Carril CarrilHacia(params Vector2[] puntos)
    {
        var a = new Nodo(0, puntos[0]);
        var b = new Nodo(1, puntos[^1]);
        var tramo = new Tramo(0, a, b, CategoriaVia.Local, 1, 0, 50f);
        return new Carril(0, tramo, true, 0, a, b, new Trayectoria(puntos));
    }

    [Theory]
    [InlineData(1f, 0f, 0)]
    [InlineData(-1f, 0f, 1)]
    [InlineData(0f, 1f, 2)]
    [InlineData(0f, -1f, 3)]
    [InlineData(1f, 1f, 0)]     // a 45° manda el eje X
    [InlineData(-1f, -1f, 1)]
    public void La_aproximacion_es_la_direccion_con_que_llega_el_carril(float dx, float dy, int esperada) =>
        Assert.Equal(esperada, Semaforo.Aproximacion(CarrilHacia(Vector2.Zero, new Vector2(dx, dy) * 50f)));

    [Fact]
    public void Cuenta_la_direccion_del_final_del_carril_no_la_del_principio() =>
        Assert.Equal(2, Semaforo.Aproximacion(CarrilHacia(Vector2.Zero, new Vector2(50f, 0f), new Vector2(50f, 40f))));
}

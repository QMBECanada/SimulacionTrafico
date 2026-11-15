using Godot;
using SimulacionTrafico.Mapa;

namespace Pruebas.Mapa;

/// <summary>Tipo de giro según la dirección con que se sale de un carril y la del carril al que se entra (Y hacia abajo).</summary>
public class ConexionTests
{
    [Fact]
    public void Seguir_en_la_misma_direccion_es_recto() =>
        Assert.Equal(TipoGiro.Recto, Conexion.CalcularGiro(Vector2.Right, Vector2.Right));

    [Fact]
    public void Ir_al_este_y_salir_hacia_el_sur_es_girar_a_la_derecha() =>
        Assert.Equal(TipoGiro.Derecha, Conexion.CalcularGiro(Vector2.Right, Vector2.Down));

    [Fact]
    public void Ir_al_este_y_salir_hacia_el_norte_es_girar_a_la_izquierda() =>
        Assert.Equal(TipoGiro.Izquierda, Conexion.CalcularGiro(Vector2.Right, Vector2.Up));
}

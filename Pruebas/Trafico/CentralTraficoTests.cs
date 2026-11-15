using Godot;
using SimulacionTrafico.Mapa;
using SimulacionTrafico.Trafico;

namespace Pruebas.Trafico;

/// <summary>La central solo sabe de cortes por los avisos: una calle está cortada si lo están todos sus carriles en ese sentido.</summary>
public class CentralTraficoTests
{
    private static (CentralTrafico central, Tramo calle, Carril[] ida, Carril vuelta) Ciudad()
    {
        var g = new Grafo(new Rect2(-10f, -10f, 520f, 20f));
        var a = g.AgregarNodo(Vector2.Zero);
        var b = g.AgregarNodo(new Vector2(500f, 0f));
        var calle = g.AgregarTramo(a, b, CategoriaVia.Arteria, 2, 1, 50f);
        g.Construir();
        var central = new CentralTrafico(g, new DetectorAtascos(g));
        return (central, calle, calle.Carriles.Where(l => l.Ida).ToArray(), calle.Carriles.First(l => !l.Ida));
    }

    private static int Estado(Tramo t, bool ida) => CotasRuta.Estado(t, ida);

    [Fact]
    public void Sin_avisos_no_hay_nada_cortado()
    {
        var (central, calle, _, _) = Ciudad();
        Assert.False(central.Cortada(Estado(calle, true)));
        Assert.False(central.Cortada(Estado(calle, false)));
    }

    [Fact]
    public void Con_un_carril_cortado_de_dos_la_calle_sigue_abierta()
    {
        var (central, calle, ida, _) = Ciudad();
        ida[0].CortadoHasta = 100.0;
        central.AvisarCorte(new[] { ida[0] });
        central.Revisar(10.0);
        Assert.False(central.Cortada(Estado(calle, true)));
    }

    [Fact]
    public void Con_todos_los_carriles_cortados_esta_cortada_hasta_el_primero_que_se_abre_y_solo_en_ese_sentido()
    {
        var (central, calle, ida, _) = Ciudad();
        ida[0].CortadoHasta = 100.0;
        ida[1].CortadoHasta = 60.0;
        central.AvisarCorte(ida);
        central.Revisar(10.0);
        Assert.True(central.Cortada(Estado(calle, true)));
        Assert.False(central.Cortada(Estado(calle, false)));
        central.Revisar(60.0);
        Assert.False(central.Cortada(Estado(calle, true)));
    }

    [Fact]
    public void Tras_el_aviso_de_despejado_la_calle_queda_abierta()
    {
        var (central, calle, _, vuelta) = Ciudad();
        vuelta.CortadoHasta = 50.0;
        central.AvisarCorte(new[] { vuelta });
        central.Revisar(20.0);
        Assert.True(central.Cortada(Estado(calle, false)));
        central.Revisar(50.0);
        central.AvisarDespejado(new[] { vuelta });
        Assert.False(central.Cortada(Estado(calle, false)));
    }
}

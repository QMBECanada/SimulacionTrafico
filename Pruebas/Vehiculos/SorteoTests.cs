using Godot;
using SimulacionTrafico.Mapa;
using SimulacionTrafico.Nucleo;
using SimulacionTrafico.Vehiculos;

namespace Pruebas.Vehiculos;

/// <summary>Los empates entre dos conductores los decide el azar (un sorteo de cada uno al entrar en cada calle), no su número.</summary>
public class SorteoTests
{
    private sealed class Ciudad
    {
        public readonly Grafo G = new(new Rect2(-10f, -10f, 1020f, 20f));
        public readonly Carril Primero, Segundo;
        public readonly Enrutador Enrutador;
        public readonly ContextoSimulacion Contexto = new(new ConfiguracionSimulacion());
        private static readonly CatalogoCoches.Modelo Modelo = new("A1.png", null!, 1.8f, 4.5f, Colors.Gray, ClaseVehiculo.Coche);

        public Ciudad()
        {
            var a = G.AgregarNodo(Vector2.Zero);
            var b = G.AgregarNodo(new Vector2(500f, 0f));
            var c = G.AgregarNodo(new Vector2(1000f, 0f));
            var t1 = G.AgregarTramo(a, b, CategoriaVia.Local, 1, 1, 50f);
            var t2 = G.AgregarTramo(b, c, CategoriaVia.Local, 1, 1, 50f);
            G.Construir();
            Primero = t1.Carriles.First(l => l.Ida);
            Segundo = t2.Carriles.First(l => l.Ida);
            Enrutador = new Enrutador(G);
        }

        public Coche Coche(int id, ulong semilla = 1)
        {
            var azar = new Aleatorio(semilla).Derivar((ulong)id);
            var coche = new Coche(id, Modelo, new Conductor(azar.Derivar(1), Contexto), G, azar, Enrutador, Contexto);
            coche.Colocar(Primero, 10f, 0f);
            return coche;
        }
    }

    [Fact]
    public void En_un_empate_pasa_exactamente_uno_de_los_dos()
    {
        var ciudad = new Ciudad();
        for (int i = 0; i < 200; i++)
        {
            var a = ciudad.Coche(i);
            var b = ciudad.Coche(i + 1000);
            Assert.NotEqual(a.MeGanaElSorteo(b), b.MeGanaElSorteo(a));
        }
    }

    [Fact]
    public void El_que_pasa_no_depende_de_su_numero()
    {
        var ciudad = new Ciudad();
        int ganaElMenor = 0;
        const int Pares = 400;
        for (int i = 0; i < Pares; i++)
            if (ciudad.Coche(i + 1000).MeGanaElSorteo(ciudad.Coche(i))) ganaElMenor++;
        Assert.InRange(ganaElMenor, Pares * 35 / 100, Pares * 65 / 100);
    }

    [Fact]
    public void Al_entrar_en_otra_calle_vuelve_a_sortear()
    {
        var ciudad = new Ciudad();
        var coche = ciudad.Coche(7);
        var antes = coche.Sorteo;
        coche.Colocar(ciudad.Segundo, 10f, 0f);
        Assert.NotEqual(antes, coche.Sorteo);
    }

    [Fact]
    public void Con_la_misma_semilla_sale_el_mismo_sorteo()
    {
        var ciudad = new Ciudad();
        Assert.Equal(ciudad.Coche(5, semilla: 3).Sorteo, ciudad.Coche(5, semilla: 3).Sorteo);
        Assert.NotEqual(ciudad.Coche(5, semilla: 3).Sorteo, ciudad.Coche(5, semilla: 4).Sorteo);
    }
}

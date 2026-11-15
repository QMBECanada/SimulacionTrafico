using SimulacionTrafico.Nucleo;

namespace Pruebas.Nucleo;

/// <summary>Opciones de la simulación: valores por defecto y lectura de los argumentos de la línea de órdenes.</summary>
public class ConfiguracionSimulacionTests
{
    private static readonly ConfiguracionSimulacion PorDefecto = new();

    private static ConfiguracionSimulacion Con(params string[] argumentos)
    {
        var c = PorDefecto;
        foreach (var a in argumentos) c = c.ConArgumento(a) ?? throw new Xunit.Sdk.XunitException($"argumento no reconocido: {a}");
        return c;
    }

    [Fact]
    public void Los_valores_por_defecto_son_los_de_siempre()
    {
        var c = PorDefecto;
        Assert.Equal(0.09f, c.ProporcionMotos);
        Assert.Equal(0.05f, c.ProporcionCamiones);
        Assert.Equal(0.15f, c.ProporcionDeFuera);
        Assert.Equal(0.05f, c.ProporcionTrabajaFuera);
        Assert.Equal(0.05f, c.ProporcionTransito);
        Assert.Equal(0.3f, c.ProporcionCamionesDeFuera);
        Assert.True(c.Autobuses);
        Assert.True(c.Percepcion);
        Assert.True(c.Peatones);
        Assert.False(c.Lluvia);
        Assert.Equal(3.0f, c.AceleracionLateralBase);
        Assert.Equal(3.5f, c.AceleracionLateralBaseRotonda);
        Assert.Equal(0.6f, c.ProbabilidadNavegador);
        Assert.Equal(1.0f, c.FactorPlanFrenada);
        Assert.True(c.SemaforosAdaptativos);
        Assert.True(c.FasesGiro);
        Assert.True(c.OndaVerde);
        Assert.Equal(10f, c.RadioMinimoGiro);
        Assert.Equal(1f, c.FactorPlazas);
        Assert.Equal(1f, c.FactorRotondas);
        Assert.True(c.GuardiaForzado);
        Assert.True(c.GuardiaVuelta);
        Assert.True(c.GuardiaOpuesta);
        Assert.Equal(300f, c.DuracionAccidenteS);
        Assert.Equal(0.7f, c.ProfundidadMinimaChoque);
        Assert.Equal(8f, c.VelocidadRelativaMinimaChoque);
    }

    [Fact]
    public void Las_casillas_quitan_o_ponen_cada_cosa()
    {
        var c = Con("--sinpeatones", "--lluvia", "--sinautobuses", "--sinpercepcion", "--sinfasegiro", "--sinonda",
                    "--sinopuesta", "--sinforzado", "--sinvuelta");
        Assert.False(c.Peatones);
        Assert.True(c.Lluvia);
        Assert.False(c.Autobuses);
        Assert.False(c.Percepcion);
        Assert.False(c.FasesGiro);
        Assert.False(c.OndaVerde);
        Assert.False(c.GuardiaOpuesta);
        Assert.False(c.GuardiaForzado);
        Assert.False(c.GuardiaVuelta);
    }

    [Fact]
    public void Los_valores_numericos_se_leen_tras_el_igual()
    {
        var c = Con("--de-fuera=0.3", "--trabajan-fuera=0.1", "--de-paso=0.2", "--motos=0.25", "--camiones=0.15",
                    "--radio-giro=12", "--plan-frenada=1.2", "--lateral=2.5", "--lateral-rotonda=3.25", "--navegador=0.4",
                    "--plazas=0.5", "--rotondas=2", "--accidente-s=200", "--choque-solape=0.5");
        Assert.Equal(0.3f, c.ProporcionDeFuera);
        Assert.Equal(0.1f, c.ProporcionTrabajaFuera);
        Assert.Equal(0.2f, c.ProporcionTransito);
        Assert.Equal(0.25f, c.ProporcionMotos);
        Assert.Equal(0.15f, c.ProporcionCamiones);
        Assert.Equal(12f, c.RadioMinimoGiro);
        Assert.Equal(1.2f, c.FactorPlanFrenada);
        Assert.Equal(2.5f, c.AceleracionLateralBase);
        Assert.Equal(3.25f, c.AceleracionLateralBaseRotonda);
        Assert.Equal(0.4f, c.ProbabilidadNavegador);
        Assert.Equal(0.5f, c.FactorPlazas);
        Assert.Equal(2f, c.FactorRotondas);
        Assert.Equal(200f, c.DuracionAccidenteS);
        Assert.Equal(0.5f, c.ProfundidadMinimaChoque);
    }

    [Fact]
    public void Por_defecto_usa_todos_los_nucleos_y_se_puede_fijar_cuantos_hilos()
    {
        Assert.Equal(0, PorDefecto.Hilos);
        Assert.Equal(4, Con("--hilos=4").Hilos);
        Assert.Equal(1, Con("--hilos=1").Hilos);
    }

    [Fact]
    public void Si_no_se_dice_cuantos_hilos_usa_los_nucleos_hasta_el_maximo_por_defecto()
    {
        Assert.Equal(System.Math.Min(System.Environment.ProcessorCount, ConfiguracionSimulacion.HilosPorDefecto), PorDefecto.HilosEfectivos);
        Assert.Equal(3, Con("--hilos=3").HilosEfectivos);
    }

    [Fact]
    public void La_velocidad_de_choque_se_da_en_kmh_y_se_guarda_en_ms() =>
        Assert.Equal(20f / 3.6f, Con("--choque-vel=20").VelocidadRelativaMinimaChoque, 5);

    [Fact]
    public void Con_dos_modos_de_semaforos_manda_el_ultimo()
    {
        Assert.False(Con("--semaforos=adaptativos", "--semaforos=fijos").SemaforosAdaptativos);
        Assert.True(Con("--semaforos=fijos", "--semaforos=adaptativos").SemaforosAdaptativos);
    }

    [Theory]
    [InlineData("--coches=100")]
    [InlineData("--semilla=3")]
    [InlineData("--lluvias")]
    public void Lo_que_no_es_una_opcion_de_la_simulacion_no_se_reconoce(string argumento) =>
        Assert.Null(PorDefecto.ConArgumento(argumento));

    [Fact]
    public void Leer_un_argumento_no_cambia_la_configuracion_original()
    {
        var original = new ConfiguracionSimulacion();
        _ = original.ConArgumento("--lluvia");
        Assert.Equal(new ConfiguracionSimulacion(), original);
        Assert.False(original.Lluvia);
    }
}

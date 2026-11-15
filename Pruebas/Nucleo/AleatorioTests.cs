using SimulacionTrafico.Nucleo;

namespace Pruebas.Nucleo;

/// <summary>Generador aleatorio propio: determinista, con rangos correctos y secuencias derivadas independientes.</summary>
public class AleatorioTests
{
    private const int Muestras = 100_000;

    private static ulong[] Secuencia(Aleatorio a, int n = 16) => Enumerable.Range(0, n).Select(_ => a.Siguiente()).ToArray();

    [Fact]
    public void La_misma_semilla_da_la_misma_secuencia() =>
        Assert.Equal(Secuencia(new Aleatorio(42)), Secuencia(new Aleatorio(42)));

    [Fact]
    public void Semillas_distintas_dan_secuencias_distintas() =>
        Assert.NotEqual(Secuencia(new Aleatorio(42)), Secuencia(new Aleatorio(43)));

    [Fact]
    public void La_secuencia_no_se_repite_en_los_primeros_valores()
    {
        var valores = Secuencia(new Aleatorio(0), 1000);
        Assert.Equal(valores.Length, valores.Distinct().Count());
    }

    [Fact]
    public void Randf_esta_en_cero_uno_y_su_media_es_un_medio()
    {
        var a = new Aleatorio(7);
        double suma = 0;
        for (int i = 0; i < Muestras; i++)
        {
            float x = a.Randf();
            Assert.InRange(x, 0f, 1f);
            Assert.NotEqual(1f, x);
            suma += x;
        }
        Assert.InRange(suma / Muestras, 0.49, 0.51);
    }

    [Fact]
    public void RandfRange_respeta_los_limites()
    {
        var a = new Aleatorio(8);
        double suma = 0;
        for (int i = 0; i < Muestras; i++)
        {
            float x = a.RandfRange(-3f, 5f);
            Assert.InRange(x, -3f, 5f);
            suma += x;
        }
        Assert.InRange(suma / Muestras, 0.9, 1.1);
    }

    [Fact]
    public void RandiRange_incluye_los_dos_extremos_y_nada_fuera()
    {
        var a = new Aleatorio(9);
        var vistos = new int[5];
        for (int i = 0; i < Muestras; i++)
        {
            int x = a.RandiRange(2, 6);
            Assert.InRange(x, 2, 6);
            vistos[x - 2]++;
        }
        Assert.All(vistos, n => Assert.InRange(n, Muestras / 5 * 0.95, Muestras / 5 * 1.05));
    }

    [Fact]
    public void RandiRange_con_extremos_iguales_devuelve_ese_valor() =>
        Assert.Equal(4, new Aleatorio(1).RandiRange(4, 4));

    [Fact]
    public void Randfn_tiene_la_media_y_la_desviacion_pedidas()
    {
        var a = new Aleatorio(10);
        double suma = 0, suma2 = 0;
        for (int i = 0; i < Muestras; i++)
        {
            double x = a.Randfn(5f, 2f);
            Assert.False(double.IsNaN(x) || double.IsInfinity(x));
            suma += x;
            suma2 += x * x;
        }
        double media = suma / Muestras;
        double desv = Math.Sqrt(suma2 / Muestras - media * media);
        Assert.InRange(media, 4.97, 5.03);
        Assert.InRange(desv, 1.97, 2.03);
    }

    [Fact]
    public void Derivar_es_determinista_y_no_consume_del_padre()
    {
        var a = new Aleatorio(11);
        var b = new Aleatorio(11);
        var hijoA = a.Derivar(5);
        Assert.Equal(Secuencia(hijoA), Secuencia(b.Derivar(5)));
        Assert.Equal(Secuencia(a), Secuencia(b));
    }

    [Fact]
    public void Derivar_con_claves_distintas_da_secuencias_distintas_y_distintas_del_padre()
    {
        var a = new Aleatorio(12);
        var s1 = Secuencia(a.Derivar(1));
        var s2 = Secuencia(a.Derivar(2));
        Assert.NotEqual(s1, s2);
        Assert.NotEqual(s1, Secuencia(new Aleatorio(12)));
    }

    [Fact]
    public void Las_derivadas_de_claves_consecutivas_no_estan_correlacionadas()
    {
        var a = new Aleatorio(13);
        double sumaXy = 0, sumaX = 0, sumaY = 0, sumaX2 = 0, sumaY2 = 0;
        const int n = 20_000;
        for (int i = 0; i < n; i++)
        {
            double x = a.Derivar((ulong)i).Randf();
            double y = a.Derivar((ulong)i + 1).Randf();
            sumaX += x; sumaY += y; sumaXy += x * y; sumaX2 += x * x; sumaY2 += y * y;
        }
        double cov = sumaXy / n - sumaX / n * (sumaY / n);
        double r = cov / Math.Sqrt((sumaX2 / n - Math.Pow(sumaX / n, 2)) * (sumaY2 / n - Math.Pow(sumaY / n, 2)));
        Assert.InRange(r, -0.03, 0.03);
    }
}

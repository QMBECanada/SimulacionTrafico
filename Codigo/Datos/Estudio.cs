using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Godot;

namespace SimulacionTrafico.Datos;

/// <summary>
/// Un estudio: una simulación cuyos datos se guardan en su propia carpeta <c>Estudios/&lt;nombre&gt;/</c>, un fichero
/// TSV por tema. Si ya existe una carpeta con ese nombre se crea otra con un número detrás, para no pisar datos.
/// </summary>
public sealed class Estudio : IDisposable
{
    private readonly System.Collections.Generic.List<RegistroTsv> _registros = new();

    public string Nombre { get; }
    public string Carpeta { get; }

    /// <summary>Hora de inicio de la simulación, en segundos desde la medianoche (para escribir la hora del día).</summary>
    public double HoraInicioS { get; }

    private Estudio(string nombre, string carpeta, double horaInicioS)
    {
        Nombre = nombre;
        Carpeta = carpeta;
        HoraInicioS = horaInicioS;
    }

    public static Estudio Crear(string nombre, double horaInicioS)
    {
        string limpio = string.Concat(nombre.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        if (limpio.Length == 0) limpio = "estudio";

        string raiz = Path.Combine(ProjectSettings.GlobalizePath("res://"), "Estudios");
        string carpeta = Path.Combine(raiz, limpio);
        for (int n = 2; Directory.Exists(carpeta); n++) carpeta = Path.Combine(raiz, $"{limpio}_{n}");
        Directory.CreateDirectory(carpeta);
        return new Estudio(Path.GetFileName(carpeta), carpeta, horaInicioS);
    }

    /// <summary>Abre un fichero TSV del estudio con esa cabecera.</summary>
    public RegistroTsv Abrir(string fichero, params string[] cabecera)
    {
        var r = new RegistroTsv(Path.Combine(Carpeta, fichero), cabecera);
        _registros.Add(r);
        return r;
    }

    /// <summary>Hora del día (HH:MM:SS) a los <paramref name="tiempoSimulado"/> segundos de simulación.</summary>
    public string Hora(double tiempoSimulado)
    {
        double s = (HoraInicioS + tiempoSimulado) % 86400.0;
        return $"{(int)(s / 3600):00}:{(int)(s % 3600 / 60):00}:{(int)(s % 60):00}";
    }

    public void Dispose()
    {
        foreach (var r in _registros) r.Dispose();
        _registros.Clear();
    }
}

/// <summary>
/// Fichero TSV (valores separados por tabuladores): UTF-8 con marca de orden de bytes para que Excel respete los
/// acentos, punto decimal y cabecera en la primera línea.
/// </summary>
public sealed class RegistroTsv : IDisposable
{
    private const int FilasPorVolcado = 200;

    private readonly StreamWriter _escritor;
    private int _pendientes;

    public string Ruta { get; }
    public int Filas { get; private set; }

    public RegistroTsv(string ruta, string[] cabecera)
    {
        Ruta = ruta;
        _escritor = new StreamWriter(ruta, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        _escritor.WriteLine(string.Join('\t', cabecera));
    }

    public void Fila(params object?[] valores)
    {
        _escritor.WriteLine(string.Join('\t', valores.Select(Formatear)));
        Filas++;
        if (++_pendientes >= FilasPorVolcado)
        {
            _escritor.Flush();
            _pendientes = 0;
        }
    }

    private static string Formatear(object? v) => v switch
    {
        null => "",
        double d => d.ToString("0.###", CultureInfo.InvariantCulture),
        float f => f.ToString("0.###", CultureInfo.InvariantCulture),
        IFormattable x => x.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString()!.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' '),
    };

    public void Dispose()
    {
        _escritor.Flush();
        _escritor.Dispose();
    }
}

using System;
using System.Linq;

namespace SimulacionTrafico.Nucleo;

/// <summary>Ciudad del mundo para calcular el sol: coordenadas y huso horario (con o sin horario de verano europeo).</summary>
public sealed record Localidad(string Nombre, double Latitud, double Longitud, int Huso, bool VeranoEuropeo = false);

public static class Localidades
{
    public static readonly Localidad[] Todas =
    {
        // España
        new("Madrid", 40.42, -3.70, 1, true), new("Barcelona", 41.39, 2.17, 1, true), new("Valencia", 39.47, -0.38, 1, true),
        new("Sevilla", 37.39, -5.98, 1, true), new("Zaragoza", 41.65, -0.89, 1, true), new("Málaga", 36.72, -4.42, 1, true),
        new("Bilbao", 43.26, -2.93, 1, true), new("Palma de Mallorca", 39.57, 2.65, 1, true),
        new("Santiago de Compostela", 42.88, -8.54, 1, true), new("Las Palmas de Gran Canaria", 28.12, -15.43, 0, true),
        new("Teruel", 40.35, -1.11, 1, true),
        // Iberoamérica (capitales; sin horario de verano)
        new("Ciudad de México", 19.43, -99.13, -6), new("Bogotá", 4.71, -74.07, -5), new("Lima", -12.05, -77.04, -5),
        new("Quito", -0.18, -78.47, -5), new("Caracas", 10.49, -66.88, -4), new("La Paz", -16.50, -68.15, -4),
        new("Santiago de Chile", -33.45, -70.67, -4), new("Buenos Aires", -34.60, -58.38, -3), new("Montevideo", -34.90, -56.16, -3),
        new("Asunción", -25.26, -57.58, -3), new("Brasilia", -15.79, -47.88, -3), new("Lisboa", 38.72, -9.14, 0, true),
        new("San José (Costa Rica)", 9.93, -84.08, -6), new("Ciudad de Panamá", 8.98, -79.52, -5),
        new("Ciudad de Guatemala", 14.63, -90.51, -6), new("Tegucigalpa", 14.07, -87.19, -6), new("San Salvador", 13.69, -89.19, -6),
        new("Managua", 12.11, -86.27, -6), new("La Habana", 23.11, -82.37, -5), new("Santo Domingo", 18.49, -69.93, -4),
    };

    /// <summary>Ciudad que propone la pantalla inicial.</summary>
    public const string PorDefecto = "Teruel";

    public static int Indice(string nombre)
    {
        for (int i = 0; i < Todas.Length; i++)
            if (Todas[i].Nombre.Equals(nombre, StringComparison.OrdinalIgnoreCase)) return i;
        for (int i = 0; i < Todas.Length; i++)
            if (Todas[i].Nombre.Contains(nombre, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }
}

/// <summary>
/// El sol de una ciudad en un día del año: hora de salida y de puesta y cuánta luz hay a cada hora (0 noche cerrada, 1 día).
/// Fórmulas astronómicas sencillas (declinación y ecuación del tiempo); precisión de unos minutos.
/// </summary>
public sealed class Sol
{
    private readonly double _latitud;       // radianes
    private readonly double _declinacion;   // radianes
    private readonly double _mediodiaH;     // mediodía solar, hora civil local

    public Localidad Localidad { get; }
    public DateOnly Fecha { get; }
    public bool HorarioVerano { get; }

    /// <summary>Salida y puesta del sol, en horas civiles locales (0 a 24).</summary>
    public double SalidaH { get; }
    public double PuestaH { get; }

    public Sol(Localidad localidad, int mes, int dia)
    {
        Localidad = localidad;
        int año = DateTime.Now.Year;
        mes = Math.Clamp(mes, 1, 12);
        dia = Math.Clamp(dia, 1, DateTime.DaysInMonth(año, mes));
        Fecha = new DateOnly(año, mes, dia);
        int n = Fecha.DayOfYear;

        HorarioVerano = localidad.VeranoEuropeo && Fecha >= UltimoDomingo(año, 3) && Fecha < UltimoDomingo(año, 10);
        double huso = localidad.Huso + (HorarioVerano ? 1 : 0);

        _latitud = localidad.Latitud * Math.PI / 180.0;
        _declinacion = 23.44 * Math.Sin(2 * Math.PI * (284 + n) / 365.0) * Math.PI / 180.0;
        double b = 2 * Math.PI * (n - 81) / 365.0;
        double ecuacionTiempoMin = 9.87 * Math.Sin(2 * b) - 7.53 * Math.Cos(b) - 1.5 * Math.Sin(b);
        _mediodiaH = 12 + (huso * 15 - localidad.Longitud) / 15.0 - ecuacionTiempoMin / 60.0;

        double cosW = (Math.Sin(-0.833 * Math.PI / 180.0) - Math.Sin(_latitud) * Math.Sin(_declinacion)) / (Math.Cos(_latitud) * Math.Cos(_declinacion));
        double w = Math.Acos(Math.Clamp(cosW, -1, 1)) * 180.0 / Math.PI / 15.0; // horas entre el mediodía y la salida
        SalidaH = _mediodiaH - w;
        PuestaH = _mediodiaH + w;
    }

    private static DateOnly UltimoDomingo(int año, int mes)
    {
        var d = new DateOnly(año, mes, DateTime.DaysInMonth(año, mes));
        return d.AddDays(-(int)d.DayOfWeek);
    }

    /// <summary>Altura del sol sobre el horizonte, en grados, a esa hora del día (segundos desde la medianoche).</summary>
    public double Altura(double horaDelDiaS)
    {
        double h = (horaDelDiaS / 3600.0 - _mediodiaH) * 15.0 * Math.PI / 180.0;
        double seno = Math.Sin(_latitud) * Math.Sin(_declinacion) + Math.Cos(_latitud) * Math.Cos(_declinacion) * Math.Cos(h);
        return Math.Asin(Math.Clamp(seno, -1, 1)) * 180.0 / Math.PI;
    }

    /// <summary>Cuánta luz hay: 0 con el sol 6° bajo el horizonte (noche) y 1 a partir de 4° sobre él; el crepúsculo es gradual.</summary>
    public float Luz(double horaDelDiaS)
    {
        double x = Math.Clamp((Altura(horaDelDiaS) + 6.0) / 10.0, 0.0, 1.0);
        return (float)(x * x * (3 - 2 * x));
    }

    public static string Formato(double horas)
    {
        horas = ((horas % 24) + 24) % 24;
        return $"{(int)horas:00}:{(int)((horas - Math.Floor(horas)) * 60):00}";
    }

    public string Descripcion => $"{Localidad.Nombre}, {Fecha:dd/MM}: sale {Formato(SalidaH)}, se pone {Formato(PuestaH)}" + (HorarioVerano ? " (horario de verano)" : "");
}

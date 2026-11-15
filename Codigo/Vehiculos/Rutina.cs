using Godot;
using SimulacionTrafico.Nucleo;
using SimulacionTrafico.Mapa;

namespace SimulacionTrafico.Vehiculos;

/// <summary>Qué hace el conductor un día cualquiera.</summary>
public enum Perfil
{
    /// <summary>Va por la mañana de casa al trabajo y vuelve por la tarde.</summary>
    Trabajador,
    /// <summary>Como el trabajador, pero por la mañana deja antes a los niños en el colegio.</summary>
    TrabajadorConColegio,
    /// <summary>No va a trabajar: hace recados y vuelve a casa (jubilados, estudiantes, personas que trabajan en casa...).</summary>
    SinTrabajo,
    /// <summary>Camión de reparto: sale de su base (una fábrica) por la mañana, reparte y vuelve a cargar hasta el fin de su turno.</summary>
    Reparto,
    /// <summary>Autobús de línea: recorre sus paradas en orden, de 6:00 a 23:00.</summary>
    Autobus,
    /// <summary>De paso: entra por una puerta de la ciudad y sale por otra; entre medias, pasa un tiempo fuera.</summary>
    Transito,
}

/// <summary>
/// Rutina diaria de un conductor: su casa, su trabajo (si lo tiene), el colegio de sus hijos (si los lleva) y sus horarios.
/// Con ella, los viajes dependen de la hora del día: por la mañana de casa al trabajo, por la tarde de vuelta, y recados y ocio
/// según la hora. Se decide al crear el coche, así que con la misma semilla es siempre la misma.
/// </summary>
public sealed class Rutina
{
    public Perfil Perfil { get; }
    public Lugar Casa { get; }
    public Lugar? Trabajo { get; }
    public Lugar? Colegio { get; }

    /// <summary>Hora a la que sale de casa hacia el trabajo (o el colegio), en horas del día.</summary>
    public float HoraSalida { get; }
    /// <summary>Hora a la que termina su jornada, en horas del día.</summary>
    public float HoraFin { get; }
    /// <summary>Si sale a comer fuera del trabajo.</summary>
    public bool ComeFuera { get; }

    /// <summary>Último día (número de día desde el inicio, contando la hora de inicio) en que fue al trabajo y en que terminó la jornada.</summary>
    public int DiaTrabajo { get; set; } = -1;
    public int DiaFinJornada { get; set; } = -1;
    public int DiaColegio { get; set; } = -1;
    public int DiaComida { get; set; } = -1;

    public Rutina(Perfil perfil, Lugar casa, Lugar? trabajo, Lugar? colegio, Aleatorio azar)
    {
        Perfil = perfil;
        Casa = casa;
        Trabajo = trabajo;
        Colegio = colegio;
        // Entrada al trabajo hacia las 8 (de 6:30 a 10) y salida unas 8-9 horas después; con niños, antes para pasar por el colegio.
        HoraSalida = perfil == Perfil.Reparto ? Mathf.Clamp(azar.Randfn(6.5f, 0.6f), 5f, 9f)
                   : perfil == Perfil.SinTrabajo ? Mathf.Clamp(azar.Randfn(10f, 1.5f), 8f, 13f)
                   : Mathf.Clamp(azar.Randfn(perfil == Perfil.TrabajadorConColegio ? 7.9f : 7.6f, 0.6f), 6.2f, 10f);
        HoraFin = perfil == Perfil.Reparto ? Mathf.Clamp(azar.Randfn(17.5f, 1f), 14f, 21f)
                : perfil == Perfil.SinTrabajo ? 0f : Mathf.Clamp(HoraSalida + azar.Randfn(8.8f, 0.9f), 13.5f, 20.5f);
        ComeFuera = perfil is Perfil.Trabajador or Perfil.TrabajadorConColegio && azar.Randf() < 0.3f;
    }

    /// <summary>Línea del autobús y parada a la que va (o en la que está).</summary>
    public Trafico.LineaAutobus? Linea { get; init; }
    public int Parada { get; set; }

    public bool Trabaja => Perfil is Perfil.Trabajador or Perfil.TrabajadorConColegio && Trabajo != null;
}

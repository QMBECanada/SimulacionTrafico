using System.Collections.Generic;
using Godot;
using SimulacionTrafico.Nucleo;
using SimulacionTrafico.Mapa;

namespace SimulacionTrafico.Vehiculos;

/// <summary>
/// Reglas de las misiones: cuánto se queda un coche en cada tipo de lugar, a qué tipo de lugar va después y a cuál
/// en concreto (los cercanos son más probables). Cada coche las consulta para decidir su siguiente destino.
/// </summary>
public static class Misiones
{
    /// <summary>Estancia en cada tipo de lugar: media, desviación y mínimo, en segundos.</summary>
    private static readonly Dictionary<TipoLugar, (float media, float desv, float minimo)> Estancias = new()
    {
        [TipoLugar.Vivienda] = (10800f, 7200f, 600f),
        [TipoLugar.Oficinas] = (18000f, 5400f, 1800f),
        [TipoLugar.CentroComercial] = (4500f, 1800f, 900f),
        [TipoLugar.Gimnasio] = (4200f, 1200f, 1800f),
        [TipoLugar.Colegio] = (14400f, 3600f, 3600f),
        [TipoLugar.Hospital] = (7200f, 3600f, 900f),
        [TipoLugar.Restaurante] = (3600f, 1500f, 1200f),
        [TipoLugar.Gasolinera] = (420f, 180f, 120f),
        [TipoLugar.Parking] = (1200f, 900f, 120f),
        [TipoLugar.Fabrica] = (2400f, 1200f, 600f),
        [TipoLugar.Exterior] = (7200f, 3600f, 1800f),
    };

    /// <summary>A qué tipo de lugar se va desde otro, con su peso relativo.</summary>
    private static readonly Dictionary<TipoLugar, (TipoLugar tipo, float peso)[]> Transiciones = new()
    {
        [TipoLugar.Vivienda] = new[]
        {
            (TipoLugar.Oficinas, 35f), (TipoLugar.CentroComercial, 15f), (TipoLugar.Restaurante, 12f), (TipoLugar.Vivienda, 10f),
            (TipoLugar.Gimnasio, 8f), (TipoLugar.Colegio, 7f), (TipoLugar.Parking, 6f), (TipoLugar.Gasolinera, 5f), (TipoLugar.Hospital, 2f),
        },
        [TipoLugar.Oficinas] = new[]
        {
            (TipoLugar.Vivienda, 45f), (TipoLugar.Restaurante, 15f), (TipoLugar.Gimnasio, 10f), (TipoLugar.CentroComercial, 10f),
            (TipoLugar.Parking, 5f), (TipoLugar.Gasolinera, 5f), (TipoLugar.Oficinas, 5f), (TipoLugar.Hospital, 3f), (TipoLugar.Colegio, 2f),
        },
        [TipoLugar.CentroComercial] = new[]
        {
            (TipoLugar.Vivienda, 50f), (TipoLugar.Restaurante, 18f), (TipoLugar.Parking, 8f), (TipoLugar.Gasolinera, 6f),
            (TipoLugar.Oficinas, 6f), (TipoLugar.Gimnasio, 5f), (TipoLugar.CentroComercial, 5f), (TipoLugar.Colegio, 2f),
        },
        [TipoLugar.Gimnasio] = new[]
        {
            (TipoLugar.Vivienda, 60f), (TipoLugar.Restaurante, 12f), (TipoLugar.Oficinas, 10f), (TipoLugar.CentroComercial, 8f), (TipoLugar.Gasolinera, 5f),
            (TipoLugar.Parking, 5f),
        },
        [TipoLugar.Colegio] = new[]
        {
            (TipoLugar.Vivienda, 55f), (TipoLugar.Oficinas, 20f), (TipoLugar.CentroComercial, 10f), (TipoLugar.Restaurante, 8f), (TipoLugar.Gasolinera, 4f),
            (TipoLugar.Hospital, 3f),
        },
        [TipoLugar.Hospital] = new[]
        {
            (TipoLugar.Vivienda, 65f), (TipoLugar.Oficinas, 12f), (TipoLugar.Restaurante, 10f), (TipoLugar.CentroComercial, 8f), (TipoLugar.Gasolinera, 5f),
        },
        [TipoLugar.Restaurante] = new[]
        {
            (TipoLugar.Vivienda, 40f), (TipoLugar.Oficinas, 30f), (TipoLugar.CentroComercial, 12f), (TipoLugar.Gimnasio, 6f), (TipoLugar.Gasolinera, 6f),
            (TipoLugar.Parking, 6f),
        },
        [TipoLugar.Gasolinera] = new[]
        {
            (TipoLugar.Vivienda, 45f), (TipoLugar.Oficinas, 30f), (TipoLugar.CentroComercial, 12f), (TipoLugar.Restaurante, 8f), (TipoLugar.Gimnasio, 5f),
        },
        [TipoLugar.Fabrica] = new[]
        {
            (TipoLugar.Vivienda, 55f), (TipoLugar.Restaurante, 15f), (TipoLugar.Gasolinera, 10f), (TipoLugar.CentroComercial, 10f),
            (TipoLugar.Oficinas, 5f), (TipoLugar.Fabrica, 5f),
        },
        [TipoLugar.Exterior] = new[] { (TipoLugar.Oficinas, 40f), (TipoLugar.CentroComercial, 30f), (TipoLugar.Restaurante, 15f), (TipoLugar.Hospital, 15f) },
        [TipoLugar.Parking] = new[]
        {
            (TipoLugar.Oficinas, 30f), (TipoLugar.CentroComercial, 30f), (TipoLugar.Restaurante, 20f), (TipoLugar.Vivienda, 15f), (TipoLugar.Hospital, 5f),
        },
    };

    /// <summary>Cuántos candidatos del tipo elegido se consideran al decidir a cuál ir.</summary>
    private const int Candidatos = 12;

    /// <summary>
    /// Distancia (m) a la que un lugar es unas cuatro veces menos probable que uno pegado: una cuarta parte del lado
    /// mayor de la ciudad, entre 600 y 3000 m. Así los viajes son proporcionales al tamaño de la ciudad.
    /// </summary>
    private static float DistanciaMedia(Grafo grafo) =>
        Mathf.Clamp(0.25f * Mathf.Max(grafo.Limites.Size.X, grafo.Limites.Size.Y), 600f, 3000f);

    /// <summary>
    /// Segundos que tarda un coche en maniobrar dentro del lugar, fuera de la calzada: entrar en su plaza (más en los parkings, que
    /// son grandes y hay que buscarla) o salir de ella hasta la puerta.
    /// </summary>
    public static float Maniobra(TipoLugar tipo, Aleatorio azar, bool entrando) =>
        tipo == TipoLugar.Parada ? azar.RandfRange(2f, 4f)   // arrimarse a la parada o salir de ella
        : entrando ? (tipo == TipoLugar.Parking || tipo == TipoLugar.CentroComercial ? azar.RandfRange(12f, 35f) : azar.RandfRange(6f, 18f))
                 : azar.RandfRange(5f, 14f);

    /// <summary>Hora de comienzo y fin del servicio de autobuses.</summary>
    public const float InicioServicio = 6f, FinServicio = 23f;

    // ---- Viajes según la hora del día (con la rutina de cada conductor) ----

    /// <summary>Lugares de trabajo y su peso (además de las oficinas, trabajan en tiendas, hospitales, colegios, restaurantes...).</summary>
    private static readonly (TipoLugar tipo, float peso)[] TiposTrabajo =
    {
        (TipoLugar.Oficinas, 42f), (TipoLugar.Fabrica, 22f), (TipoLugar.CentroComercial, 12f), (TipoLugar.Hospital, 8f), (TipoLugar.Colegio, 7f),
        (TipoLugar.Restaurante, 6f), (TipoLugar.Gimnasio, 2f), (TipoLugar.Gasolinera, 1f),
    };

    /// <summary>Recados y ocio desde casa según la hora: de día, por la tarde-noche y de madrugada.</summary>
    private static readonly (TipoLugar tipo, float peso)[] OcioDia =
    {
        (TipoLugar.CentroComercial, 25f), (TipoLugar.Restaurante, 15f), (TipoLugar.Gimnasio, 14f), (TipoLugar.Vivienda, 14f),
        (TipoLugar.Parking, 10f), (TipoLugar.Gasolinera, 8f), (TipoLugar.Hospital, 6f), (TipoLugar.Colegio, 8f),
    };
    private static readonly (TipoLugar tipo, float peso)[] OcioNoche =
    {
        (TipoLugar.Restaurante, 35f), (TipoLugar.Vivienda, 25f), (TipoLugar.CentroComercial, 15f), (TipoLugar.Gimnasio, 10f),
        (TipoLugar.Gasolinera, 10f), (TipoLugar.Parking, 5f),
    };
    private static readonly (TipoLugar tipo, float peso)[] OcioMadrugada =
    {
        (TipoLugar.Vivienda, 40f), (TipoLugar.Gasolinera, 25f), (TipoLugar.Restaurante, 20f), (TipoLugar.Hospital, 15f),
    };
    /// <summary>Al salir del trabajo, si no va directo a casa.</summary>
    private static readonly (TipoLugar tipo, float peso)[] TrasElTrabajo =
    {
        (TipoLugar.CentroComercial, 35f), (TipoLugar.Gimnasio, 30f), (TipoLugar.Restaurante, 25f), (TipoLugar.Gasolinera, 10f),
    };

    /// <summary>Adónde lleva la carga un camión de reparto.</summary>
    private static readonly (TipoLugar tipo, float peso)[] ClientesReparto =
    {
        (TipoLugar.CentroComercial, 35f), (TipoLugar.Restaurante, 20f), (TipoLugar.Fabrica, 15f), (TipoLugar.Oficinas, 10f),
        (TipoLugar.Gasolinera, 8f), (TipoLugar.Hospital, 7f), (TipoLugar.Parking, 5f),
    };

    /// <summary>Camión de reparto nuevo, con base en ese lugar.</summary>
    public static Rutina CrearRutinaReparto(Lugar base_, Aleatorio azar) => new(Perfil.Reparto, base_, null, null, azar);

    private static TipoLugar Sortear((TipoLugar tipo, float peso)[] opciones, Aleatorio azar)
    {
        float total = 0f;
        foreach (var (_, peso) in opciones) total += peso;
        float r = azar.Randf() * total;
        foreach (var (tipo, peso) in opciones)
        {
            r -= peso;
            if (r <= 0f) return tipo;
        }
        return opciones[^1].tipo;
    }

    /// <summary>Hora del día (0 a 24) y número de día de un instante dado en segundos desde la medianoche del primer día.</summary>
    private static (float hora, int dia) HoraYDia(double horaAbsolutaS) => ((float)(horaAbsolutaS % 86400.0 / 3600.0), (int)(horaAbsolutaS / 86400.0));

    /// <summary>
    /// Perfil de un conductor nuevo: un 58 % trabaja, un 12 % trabaja y además lleva a los niños al colegio por la mañana, y un 30 %
    /// no trabaja fuera de casa. El trabajo se elige entre los lugares de los tipos donde se trabaja, con más peso para los que tienen
    /// más plazas libres y para los no muy lejanos (los viajes al trabajo son más largos que los recados).
    /// </summary>
    /// <param name="puertas">Puertas de la ciudad: con probabilidad <paramref name="trabajaFuera"/>, quien trabaja lo hace fuera.</param>
    /// <param name="trabajador">Siempre trabaja (los que viven fuera y entran a trabajar).</param>
    public static Rutina CrearRutina(Grafo grafo, Lugar casa, Dictionary<Lugar, int> asignados, Aleatorio azar,
        IReadOnlyList<Lugar>? puertas = null, float trabajaFuera = 0f, bool trabajador = false)
    {
        float sorteo = azar.Randf();
        var perfil = trabajador ? Perfil.Trabajador : sorteo < 0.58f ? Perfil.Trabajador : sorteo < 0.70f ? Perfil.TrabajadorConColegio : Perfil.SinTrabajo;
        Lugar? trabajo = null, colegio = null;
        if (perfil != Perfil.SinTrabajo && !trabajador && puertas is { Count: > 0 } && azar.Randf() < trabajaFuera)
            trabajo = puertas[azar.RandiRange(0, puertas.Count - 1)];   // trabaja fuera de la ciudad
        else if (perfil != Perfil.SinTrabajo)
        {
            var tipoTrabajo = Sortear(TiposTrabajo, azar);
            trabajo = ElegirTrabajo(grafo, casa, tipoTrabajo, asignados, azar, true)
                      ?? ElegirTrabajo(grafo, casa, TipoLugar.Oficinas, asignados, azar, true)
                      ?? ElegirTrabajo(grafo, casa, tipoTrabajo, asignados, azar, false);   // con flotas grandes: aunque esté lleno
            if (trabajo == null) perfil = Perfil.SinTrabajo;
            else asignados[trabajo] = asignados.GetValueOrDefault(trabajo) + 1;
        }
        if (perfil == Perfil.TrabajadorConColegio)
        {
            colegio = ElegirLugar(grafo, casa, TipoLugar.Colegio, azar);
            if (colegio == null) perfil = Perfil.Trabajador;
        }
        return new Rutina(perfil, casa, trabajo, colegio, azar);
    }

    private static Lugar? ElegirTrabajo(Grafo grafo, Lugar casa, TipoLugar tipo, Dictionary<Lugar, int> asignados, Aleatorio azar, bool conPlazas)
    {
        if (!grafo.LugaresPorTipo.TryGetValue(tipo, out var lista) || lista.Count == 0) return null;
        float distanciaMedia = 1.5f * DistanciaMedia(grafo);
        Lugar? mejor = null;
        float total = 0f;
        for (int i = 0; i < 16; i++)
        {
            var l = lista[azar.RandiRange(0, lista.Count - 1)];
            int libres = conPlazas ? Lugar.PlazasPara(l.Tipo, l.Area, grafo.FactorPlazas) - asignados.GetValueOrDefault(l) : Lugar.PlazasPara(l.Tipo, l.Area, grafo.FactorPlazas);
            if (libres <= 0) continue;
            float w = libres / Mathf.Pow(1f + l.Puerta.DistanceTo(casa.Puerta) / distanciaMedia, 2f);
            total += w;
            if (azar.Randf() * total < w) mejor = l;   // muestreo proporcional al peso, en una pasada
        }
        return mejor;
    }

    /// <summary>
    /// Siguiente destino de un coche que sale de <paramref name="origen"/> a la hora dada, con el motivo del viaje (Trabajo, Colegio,
    /// Comida, Casa u Otro). Durante su jornada, el que trabaja va al trabajo (antes al colegio si lleva niños) y puede salir a comer;
    /// al terminarla vuelve a casa o hace algo antes; desde casa, recados u ocio según la hora; desde otros sitios suele volver a casa.
    /// </summary>
    public static (Lugar? destino, string motivo) SiguienteDestino(Grafo grafo, Coche coche, Lugar origen, double horaAbsolutaS, Aleatorio azar)
    {
        var r = coche.Rutina;
        if (r == null) return (ElegirLugar(grafo, origen, SiguienteTipo(origen.Tipo, azar), azar), "Otro");
        var (h, dia) = HoraYDia(horaAbsolutaS);
        (Lugar?, string) Ir(TipoLugar tipo, string motivo) => (ElegirLugar(grafo, origen, tipo, azar) ?? r.Casa, motivo);
        (Lugar?, string) ACasa() => origen == r.Casa ? Ir(TipoLugar.Vivienda, "Otro") : (r.Casa, "Casa");

        if (r.Perfil == Perfil.Transito)
        {
            // De paso: de la puerta por la que entra a otra cualquiera.
            var puertas = grafo.LugaresPorTipo.GetValueOrDefault(TipoLugar.Exterior);
            if (puertas is not { Count: > 1 }) return (null, "Transito");
            Lugar otra;
            do otra = puertas[azar.RandiRange(0, puertas.Count - 1)]; while (otra == origen);
            return (otra, "Transito");
        }

        if (r.Perfil == Perfil.Autobus && r.Linea != null)
        {
            // Siguiente parada de su línea (de la última de la vuelta se pasa a la primera de la ida).
            r.Parada = (r.Parada + 1) % r.Linea.Paradas.Count;
            return (r.Linea.Paradas[r.Parada], "Linea");
        }

        if (r.Perfil == Perfil.Reparto)
        {
            // En su turno: de la base a un cliente, y de un cliente a otro o (una de cada cuatro veces) a la base a cargar.
            bool turno = h >= r.HoraSalida - 0.25f && h < r.HoraFin;
            if (!turno) return origen == r.Casa ? Ir(TipoLugar.Gasolinera, "Reparto") : (r.Casa, "Base");
            return origen == r.Casa || azar.Randf() < 0.75f ? Ir(Sortear(ClientesReparto, azar), "Reparto") : (r.Casa, "Base");
        }

        if (r.Trabaja && r.DiaFinJornada != dia && h >= 4f && h < r.HoraFin)
        {
            if (origen == r.Trabajo)
            {
                if (r.ComeFuera && r.DiaComida != dia && h >= 12.5f && h < 15.5f)
                {
                    r.DiaComida = dia;
                    return Ir(TipoLugar.Restaurante, "Comida");
                }
                if (h < r.HoraFin - 1.5f) return Ir(Sortear(OcioDia, azar), "Otro");   // sale antes de tiempo: un recado y vuelve
                r.DiaFinJornada = dia;
                return azar.Randf() < 0.72f ? ACasa() : Ir(Sortear(TrasElTrabajo, azar), "Otro");
            }
            if (r.Perfil == Perfil.TrabajadorConColegio && r.Colegio != null && r.DiaColegio != dia && origen == r.Casa && h < 11f)
            {
                r.DiaColegio = dia;
                return (r.Colegio, "Colegio");
            }
            r.DiaTrabajo = dia;
            return (r.Trabajo, "Trabajo");
        }
        if (origen == r.Trabajo)
        {
            r.DiaFinJornada = dia;
            return azar.Randf() < 0.72f ? ACasa() : Ir(Sortear(TrasElTrabajo, azar), "Otro");
        }
        if (origen == r.Casa)
            return Ir(Sortear(h >= 6f && h < 20f ? OcioDia : h >= 20f ? OcioNoche : OcioMadrugada, azar), "Otro");
        // Desde cualquier otro sitio: casi siempre a casa de noche, y a menudo de día.
        float aCasa = h >= 20f || h < 6f ? 0.85f : 0.65f;
        return azar.Randf() < aCasa ? ACasa() : Ir(Sortear(h >= 20f || h < 6f ? OcioNoche : OcioDia, azar), "Otro");
    }

    /// <summary>
    /// Segundos que se queda en el lugar al que acaba de llegar. En casa, hasta su hora de salida por la mañana si aún no ha ido a trabajar,
    /// y por la noche hasta la mañana siguiente; en el trabajo, hasta la hora de comer (si come fuera) o hasta el fin de la jornada; al
    /// dejar a los niños, unos minutos; en la comida, de 40 a 70 minutos. En lo demás, la estancia habitual del tipo de lugar
    /// (multiplicada por <paramref name="factor"/>).
    /// </summary>
    public static double Estancia(Coche coche, Lugar lugar, double horaAbsolutaS, Aleatorio azar, float factor)
    {
        var r = coche.Rutina;
        if (r != null)
        {
            var (h, dia) = HoraYDia(horaAbsolutaS);
            double Hasta(int d, float hora) => d * 86400.0 + hora * 3600.0 - horaAbsolutaS;
            if (r.Perfil == Perfil.Transito)
            {
                // Fuera de la ciudad, de media hora a cuatro horas; de noche, hasta la mañana.
                if (h >= 22f || h < 5f) return System.Math.Max(600.0, Hasta(h >= 22f ? dia + 1 : dia, azar.RandfRange(5.5f, 9f)));
                return azar.RandfRange(1800f, 14400f);
            }
            if (r.Perfil == Perfil.Autobus)
            {
                // En cada parada, de 15 a 35 s (suben y bajan viajeros); fuera del horario de servicio, espera en la parada hasta que empiece.
                if (h >= Misiones.InicioServicio && h < Misiones.FinServicio) return azar.RandfRange(15f, 35f);
                return System.Math.Max(60.0, Hasta(h >= Misiones.FinServicio ? dia + 1 : dia, Misiones.InicioServicio));
            }
            if (r.Perfil == Perfil.Reparto)
            {
                // Descargar en un cliente, de 15 a 40 min; cargar en la base, de 20 a 60; fuera del turno, en la base hasta el día siguiente.
                if (lugar != r.Casa) return azar.RandfRange(900f, 2400f);
                if (h >= r.HoraSalida - 0.25f && h < r.HoraFin) return azar.RandfRange(1200f, 3600f);
                return System.Math.Max(600.0, Hasta(h >= r.HoraFin ? dia + 1 : dia, r.HoraSalida) + azar.RandfRange(0f, 600f));
            }
            if (lugar == r.Casa)
            {
                bool pendiente = r.Trabaja ? r.DiaTrabajo != dia : true;
                if (h >= 4f && h < r.HoraSalida && pendiente) return System.Math.Max(60.0, Hasta(dia, r.HoraSalida) + azar.RandfRange(0f, 300f));
                if (h >= 20.5f) return System.Math.Max(600.0, Hasta(dia + 1, r.HoraSalida) + azar.RandfRange(0f, 600f));
                if (h < 4f) return System.Math.Max(600.0, Hasta(dia, r.HoraSalida) + azar.RandfRange(0f, 600f));
            }
            else if (lugar == r.Trabajo && r.DiaFinJornada != dia)
            {
                if (r.ComeFuera && r.DiaComida != dia && h < 13f) return System.Math.Max(1800.0, Hasta(dia, azar.RandfRange(13f, 14.5f)));
                return System.Math.Max(1800.0, Hasta(dia, r.HoraFin));
            }
            else if (lugar == r.Colegio && r.DiaColegio == dia && r.DiaTrabajo != dia && h < 11f)
                return azar.RandfRange(240f, 600f);
            else if (lugar.Tipo == TipoLugar.Restaurante && r.DiaComida == dia && r.DiaFinJornada != dia && h >= 12f && h < 16f)
                return azar.RandfRange(2400f, 4200f);
        }
        return Estancia(lugar.Tipo, azar) * factor;
    }

    public static float Estancia(TipoLugar tipo, Aleatorio azar)
    {
        var (media, desv, minimo) = Estancias[tipo];
        return Mathf.Max(minimo, azar.Randfn(media, desv));
    }

    public static TipoLugar SiguienteTipo(TipoLugar actual, Aleatorio azar) => Sortear(Transiciones[actual], azar);

    /// <summary>
    /// Lugar concreto de ese tipo al que irá: de unos cuantos al azar, los más cercanos pesan más.
    /// Devuelve null si no hay ninguno distinto del actual.
    /// </summary>
    public static Lugar? ElegirLugar(Grafo grafo, Lugar origen, TipoLugar tipo, Aleatorio azar)
    {
        if (!grafo.LugaresPorTipo.TryGetValue(tipo, out var lista) || lista.Count == 0) return null;

        float distanciaMedia = DistanciaMedia(grafo);
        Lugar? mejor = null;
        float total = 0f;
        var pesos = new float[Candidatos];
        var elegidos = new Lugar?[Candidatos];
        int n = 0;
        for (int i = 0; i < Candidatos; i++)
        {
            var l = lista[azar.RandiRange(0, lista.Count - 1)];
            if (l == origen) continue;
            float d = l.Puerta.DistanceTo(origen.Puerta);
            float w = 1f / Mathf.Pow(1f + d / distanciaMedia, 2f);
            elegidos[n] = l;
            pesos[n] = w;
            total += w;
            n++;
        }
        if (n == 0) return null;

        float r = azar.Randf() * total;
        for (int i = 0; i < n; i++)
        {
            r -= pesos[i];
            mejor = elegidos[i];
            if (r <= 0f) break;
        }
        return mejor;
    }
}

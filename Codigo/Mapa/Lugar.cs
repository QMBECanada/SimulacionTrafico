using System.Collections.Generic;
using Godot;
using SimulacionTrafico.Vehiculos;

namespace SimulacionTrafico.Mapa;

public enum TipoLugar
{
    Vivienda,
    Oficinas,
    CentroComercial,
    Gimnasio,
    Colegio,
    Hospital,
    Restaurante,
    Gasolinera,
    Parking,
    Fabrica,
    /// <summary>Parada de autobús (dársena junto al carril derecho). Solo la usan los autobuses.</summary>
    Parada,
    /// <summary>Puerta de la ciudad: al final de una carretera, en el borde. Se llega por el carril que va hacia fuera y se sale por el que va hacia dentro.</summary>
    Exterior,
}

/// <summary>
/// Sitio al que van los coches (vivienda, oficinas, centro comercial...). Ocupa una manzana y tiene una puerta
/// en un carril de la calle: por ahí entran los coches (y desaparecen) y salen cuando terminan su estancia.
/// </summary>
public sealed class Lugar
{
    public int Id { get; }
    public TipoLugar Tipo { get; }
    public string Nombre { get; }
    public Rect2 Area { get; }

    /// <summary>Carril de la calle donde está la puerta y posición de la puerta a lo largo de ese carril.</summary>
    public Carril Acceso { get; }
    public float SAcceso { get; }

    /// <summary>
    /// Carril y posición por los que se sale a la calle. Casi siempre los mismos que los de llegada; en las puertas de la ciudad, el carril
    /// que va hacia dentro (se llega por el que va hacia fuera).
    /// </summary>
    public Carril Salida { get; private set; }
    public float SSalida { get; private set; }

    public void UsarSalida(Carril carril, float s)
    {
        Salida = carril;
        SSalida = s;
    }

    /// <summary>Punto del mapa donde se dibuja la puerta.</summary>
    public Vector2 Puerta { get; }

    /// <summary>Coches que ya han terminado su estancia y esperan hueco para salir a la calle.</summary>
    public Queue<Coche> Salidas { get; } = new();

    /// <summary>Coches que hay dentro ahora (con plaza: aparcando, estacionados o preparándose para salir).</summary>
    public int Dentro { get; set; }

    /// <summary>Plazas de aparcamiento. Si no hay ninguna libre, el coche que llega espera fuera de la calzada hasta que se libere una.</summary>
    public int Capacidad { get; set; } = int.MaxValue;

    /// <summary>Coches que han llegado a la puerta y esperan una plaza (fuera de la calle: no estorban al tráfico).</summary>
    public Queue<Coche> EsperandoPlaza { get; } = new();

    /// <summary>Plazas que corresponden a un lugar de ese tipo y superficie (una por cierta superficie, mínimo 4), por <paramref name="factorPlazas"/>.</summary>
    public static int PlazasPara(TipoLugar tipo, Rect2 area, float factorPlazas)
    {
        float m2PorPlaza = tipo switch
        {
            TipoLugar.Vivienda => 80f,
            TipoLugar.Oficinas => 40f,
            TipoLugar.CentroComercial => 15f,
            TipoLugar.Gimnasio => 30f,
            TipoLugar.Colegio => 50f,
            TipoLugar.Hospital => 30f,
            TipoLugar.Restaurante => 30f,
            TipoLugar.Gasolinera => 200f,
            TipoLugar.Fabrica => 120f,
            TipoLugar.Parada => float.PositiveInfinity,
            TipoLugar.Exterior => float.PositiveInfinity,
            _ => 20f,   // parking
        };
        if (tipo == TipoLugar.Parada) return 2;   // caben dos autobuses en la dársena
        if (tipo == TipoLugar.Exterior) return int.MaxValue / 4;   // el exterior no se llena
        return Mathf.Max(4, (int)(area.Size.X * area.Size.Y / m2PorPlaza * factorPlazas));
    }

    public Lugar(int id, TipoLugar tipo, string nombre, Rect2 area, Carril acceso, float sAcceso, Vector2 puerta)
    {
        Id = id;
        Tipo = tipo;
        Nombre = nombre;
        Area = area;
        Acceso = acceso;
        SAcceso = sAcceso;
        Salida = acceso;
        SSalida = sAcceso;
        Puerta = puerta;
    }

    public static Color ColorDe(TipoLugar tipo) => tipo switch
    {
        TipoLugar.Vivienda => new Color(0.94f, 0.85f, 0.78f),
        TipoLugar.Oficinas => new Color(0.76f, 0.81f, 0.92f),
        TipoLugar.CentroComercial => new Color(0.96f, 0.74f, 0.80f),
        TipoLugar.Gimnasio => new Color(0.97f, 0.78f, 0.50f),
        TipoLugar.Colegio => new Color(0.98f, 0.92f, 0.50f),
        TipoLugar.Hospital => new Color(0.95f, 0.55f, 0.55f),
        TipoLugar.Restaurante => new Color(0.88f, 0.68f, 0.50f),
        TipoLugar.Gasolinera => new Color(0.72f, 0.72f, 0.74f),
        TipoLugar.Fabrica => new Color(0.62f, 0.60f, 0.70f),
        TipoLugar.Parada => new Color(0.15f, 0.35f, 0.80f),
        TipoLugar.Exterior => new Color(0.35f, 0.75f, 0.45f),
        _ => new Color(0.66f, 0.76f, 0.86f), // parking
    };
}

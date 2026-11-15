using Godot;

namespace SimulacionTrafico.Vehiculos;

/// <summary>
/// Intelligent Driver Model: aceleración de un coche según su velocidad, la que desea y el coche que tiene delante.
/// </summary>
public static class ModeloIDM
{
    private const float FrenadaMaxima = 9f; // m/s², límite físico

    /// <summary>Exponente de la aceleración libre: cuanto mayor, más cerca de su velocidad deseada sigue acelerando (el clásico es 4).</summary>
    private const float ExponenteLibre = 6f;

    /// <param name="v">Velocidad propia (m/s).</param>
    /// <param name="v0">Velocidad deseada (m/s).</param>
    /// <param name="hueco">Distancia libre hasta el coche de delante (m); infinito si no hay.</param>
    /// <param name="vLider">Velocidad del coche de delante (m/s).</param>
    /// <param name="distanciaMinima">Distancia que deja en parado (m).</param>
    /// <param name="frenadaLibre">Lo más que frena si solo le sobra velocidad (sin nadie delante que le obligue): por defecto, levantar el pie.</param>
    public static float Aceleracion(Conductor c, float v, float v0, float hueco, float vLider, float distanciaMinima, float frenadaLibre = -1f)
    {
        float a = c.AceleracionMax;
        v0 = Mathf.Max(v0, 0.1f);
        float libre = 1f - Mathf.Pow(v / v0, ExponenteLibre);
        float topeLibre = frenadaLibre > 0f ? frenadaLibre : c.FrenadaSuave;
        if (float.IsInfinity(hueco)) return Mathf.Clamp(a * libre, -topeLibre, a);

        float dv = v - vLider;
        float sDeseada = distanciaMinima +
                         Mathf.Max(0f, v * c.SeparacionActual + v * dv / (2f * Mathf.Sqrt(a * c.FrenadaComoda)));
        float cociente = sDeseada / Mathf.Max(hueco, 0.1f);
        float interaccion = a * cociente * cociente;
        float acc = a * libre - interaccion;
        if (acc >= 0f) return Mathf.Min(acc, a);
        // Solo le sobra velocidad (el de delante está lejos): levanta el pie o frena lo justo.
        if (interaccion < 0.1f * a) return Mathf.Max(acc, -topeLibre);
        // Por el coche de delante o un obstáculo: frenada normal o fuerte; la de emergencia solo si físicamente no basta con la fuerte.
        // (se cuenta con quedar a un metro al menos: tocar al de delante ya es un choque)
        float necesaria = dv > 0f ? dv * dv / (2f * Mathf.Max(hueco - 1f, 0.1f)) : 0f;
        float tope = necesaria > c.FrenadaFuerte ? Mathf.Min(c.FrenadaEmergencia, FrenadaMaxima) : c.FrenadaFuerte;
        return Mathf.Max(acc, -tope);
    }
}

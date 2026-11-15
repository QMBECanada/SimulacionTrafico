namespace SimulacionTrafico.Nucleo;

/// <summary>
/// Medidas del mundo. Convención: 1 unidad de Godot = 1 metro.
/// </summary>
public static class Escala
{
    public const float AnchoCarril = 3.5f;
    public const float AnchoAcera = 2.5f;
    public const float KmhAMs = 1f / 3.6f;

    /// <summary>Escala de las imágenes de los coches (Assets/coches): su tamaño en píxeles dividido por esto son metros.</summary>
    public const float PixelesPorMetroSprites = 40f;
    public const float MsAKmh = 3.6f;
}

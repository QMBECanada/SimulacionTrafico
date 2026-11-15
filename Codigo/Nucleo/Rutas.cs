using Godot;

namespace SimulacionTrafico.Nucleo;

/// <summary>
/// Dónde están las cosas, tanto si el programa se ejecuta desde el proyecto (con Godot) como si es un ejecutable exportado.
/// Lo que va dentro del programa (ciudades, dibujos) se lee con rutas <c>res://</c>; lo que escribe (estudios, lotes) va a la carpeta
/// del proyecto o, en un ejecutable exportado, a la carpeta del ejecutable.
/// </summary>
public static class Rutas
{
    /// <summary>True en un ejecutable exportado; false al ejecutar el proyecto con Godot.</summary>
    public static bool Exportado => OS.HasFeature("template");

    /// <summary>Carpeta del proyecto o, en un ejecutable exportado, la del ejecutable.</summary>
    public static string Carpeta => Exportado
        ? System.IO.Path.GetDirectoryName(OS.GetExecutablePath())!
        : ProjectSettings.GlobalizePath("res://").TrimEnd('/', '\\');

    /// <summary>Carpeta donde se guardan los estudios.</summary>
    public static string Estudios => System.IO.Path.Combine(Carpeta, "Estudios");

    /// <summary>Imagen de un PNG del programa (por ejemplo <c>res://Assets/flechas/recto.png</c>), descomprimida y con mipmaps.</summary>
    public static Image Imagen(string ruta)
    {
        var img = GD.Load<Texture2D>(ruta).GetImage();
        if (img.IsCompressed()) img.Decompress();
        img.GenerateMipmaps();
        return img;
    }
}

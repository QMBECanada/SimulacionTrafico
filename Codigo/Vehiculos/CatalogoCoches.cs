using System.Collections.Generic;
using System.Linq;
using Godot;
using SimulacionTrafico.Nucleo;

namespace SimulacionTrafico.Vehiculos;

/// <summary>Clase de vehículo: cambia sus medidas y su forma de acelerar, frenar y girar (ver <see cref="Conductor"/>).</summary>
public enum ClaseVehiculo { Coche, Moto, Camion, Autobus }

/// <summary>
/// Carga los sprites de los vehículos: Assets/coches, Assets/motos, Assets/camiones y Assets/autobuses. Cada imagen ya tiene el tamaño real del coche a
/// <see cref="Escala.PixelesPorMetroSprites"/> píxeles por metro, así que su tamaño en metros es el de la imagen
/// dividido por esa constante: no hace falta ningún otro dato.
/// </summary>
public sealed class CatalogoCoches
{
    /// <param name="ColorMedio">Color medio del sprite; se usa para dibujar el coche como un punto cuando el zoom está lejos.</param>
    public sealed record Modelo(string Archivo, Texture2D Textura, float AnchoM, float LargoM, Color ColorMedio, ClaseVehiculo Clase)
    {
        /// <summary>Nombre del modelo: letra de la clase (A automóvil, M moto, C camión, B bus) y número, como A27.</summary>
        public string Nombre => System.IO.Path.GetFileNameWithoutExtension(Archivo);
    }

    public IReadOnlyList<Modelo> Modelos { get; }

    private CatalogoCoches(List<Modelo> modelos) => Modelos = modelos;

    /// <summary>Modelos de una clase (vacío si no hay ninguno).</summary>
    public IReadOnlyList<Modelo> De(ClaseVehiculo clase) => Modelos.Where(m => m.Clase == clase).ToList();

    private static readonly (string carpeta, ClaseVehiculo clase)[] Carpetas =
    {
        ("res://Assets/coches", ClaseVehiculo.Coche), ("res://Assets/motos", ClaseVehiculo.Moto),
        ("res://Assets/camiones", ClaseVehiculo.Camion), ("res://Assets/autobuses", ClaseVehiculo.Autobus),
    };

    public static CatalogoCoches Cargar()
    {
        var modelos = new List<Modelo>();
        foreach (var (carpeta, clase) in Carpetas)
            if (DirAccess.DirExistsAbsolute(carpeta)) modelos.AddRange(CargarCarpeta(carpeta, clase));
        return new CatalogoCoches(modelos);
    }

    private static List<Modelo> CargarCarpeta(string carpeta, ClaseVehiculo clase)
    {
        // Los PNG importados aparecen como "x.png" en el editor y como "x.png.import" en una exportación.
        var archivos = DirAccess.GetFilesAt(carpeta)
            .Select(f => f.EndsWith(".import") ? f[..^".import".Length] : f)
            .Where(f => f.EndsWith(".png"))
            .Distinct()
            .OrderBy(f => f, System.StringComparer.Ordinal)
            .ToList();

        var modelos = new List<Modelo>();
        foreach (var archivo in archivos)
        {
            var textura = GD.Load<Texture2D>($"{carpeta}/{archivo}");
            var tam = textura.GetSize();
            modelos.Add(new Modelo(archivo, textura, tam.X / Escala.PixelesPorMetroSprites, tam.Y / Escala.PixelesPorMetroSprites,
                ColorMedio(textura), clase));
        }
        return modelos;
    }

    private static Color ColorMedio(Texture2D textura)
    {
        var img = textura.GetImage();
        if (img.IsCompressed()) img.Decompress();
        float r = 0, g = 0, b = 0;
        int n = 0;
        for (int y = 0; y < img.GetHeight(); y += 3)
            for (int x = 0; x < img.GetWidth(); x += 3)
            {
                var p = img.GetPixel(x, y);
                if (p.A < 0.5f) continue;
                r += p.R; g += p.G; b += p.B;
                n++;
            }
        return n == 0 ? Colors.Gray : new Color(r / n, g / n, b / n);
    }
}

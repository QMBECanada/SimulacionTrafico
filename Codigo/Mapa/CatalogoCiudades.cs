using System.Diagnostics;
using System.Linq;
using Godot;

namespace SimulacionTrafico.Mapa;

/// <summary>
/// Las cinco ciudades fijas del proyecto. Cada una se genera una sola vez (<c>--generar-ciudades</c>) y se guarda en
/// <c>Ciudades/ciudadN.json</c>; a partir de ahí siempre se carga el fichero, así que son siempre la misma ciudad.
/// </summary>
public static class CatalogoCiudades
{
    public static readonly (string nombre, float ancho, float alto, int semilla)[] Ciudades =
    {
        ("Ciudad 1 (3 x 2 km)", 3000f, 2000f, 101),
        ("Ciudad 2 (6 x 4 km)", 6000f, 4000f, 102),
        ("Ciudad 3 (12 x 8 km)", 12000f, 8000f, 103),
        ("Ciudad 4 (10 x 10 km)", 10000f, 10000f, 104),
        ("Ciudad 5 (6 x 2 km)", 6000f, 2000f, 105),
    };

    public static string Nombre(int numero) => Ciudades[numero - 1].nombre;

    public static string Ruta(int numero) =>
        System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), "Ciudades", $"ciudad{numero}.json");

    /// <summary>Genera las cinco ciudades y las guarda (sobrescribe las que ya haya: hacerlo solo para crearlas).</summary>
    public static void GenerarTodas(float factorRotondas = 1f)
    {
        for (int i = 1; i <= Ciudades.Length; i++)
        {
            var (nombre, ancho, alto, semilla) = Ciudades[i - 1];
            var reloj = Stopwatch.StartNew();
            var g = GeneradorCiudad.Generar(ancho, alto, semilla, factorRotondas: factorRotondas);
            double msGenerar = reloj.Elapsed.TotalMilliseconds;
            CiudadFichero.Guardar(g, Ruta(i), semilla);
            long kb = new System.IO.FileInfo(Ruta(i)).Length / 1024;
            GD.Print($"{nombre}: {g.Nodos.Count} cruces, {g.Tramos.Count} calles, {g.Manzanas.Count} manzanas, {g.Lugares.Count} lugares, " +
                     $"{g.Tramos.Count(t => t.EsPuente)} puentes, {g.Tramos.Count(t => t.EsTunel)} túneles, {g.Radares.Count} radares, {g.BasesGuardias.Count} bases de guardias; generada en {msGenerar:0} ms, {kb} KB en {Ruta(i)}");
        }
    }
}

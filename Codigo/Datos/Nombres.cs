using Godot;
using SimulacionTrafico.Mapa;

namespace SimulacionTrafico.Datos;

/// <summary>
/// Nombres de calles y cruces para los ficheros de datos.
/// Son los nombres estables que asigna el generador (H-fila-columna, V-fila-columna, C-n, X-fila-columna...); si un
/// elemento no tiene nombre, se usa su identificador interno.
/// </summary>
public static class Nombres
{
    public static string Cruce(Nodo n) => n.Nombre != "" ? n.Nombre : $"N{n.Id}";

    public static string Calle(Tramo t) => t.Nombre != "" ? t.Nombre : $"T{t.Id}";

    /// <summary>
    /// Dirección de circulación: NS, SN (norte-sur y sur-norte), EO, OE (este-oeste y oeste-este), o "horario" y "antihorario" en los arcos
    /// (esquinas de la circunvalación y rotondas). <paramref name="ida"/>: de A a B.
    /// </summary>
    public static string Direccion(Tramo t, bool ida)
    {
        var a = ida ? t.A.Pos : t.B.Pos;
        var b = ida ? t.B.Pos : t.A.Pos;
        if (t.Eje.Puntos.Length > 2)
        {
            // En un arco: horario o antihorario según gire al recorrerlo (con la Y hacia abajo, producto cruzado positivo = horario).
            var p = ida ? t.Eje.Puntos : System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Reverse(t.Eje.Puntos));
            int m = p.Length / 2;
            return (p[m] - p[0]).Cross(p[^1] - p[m]) > 0f ? "horario" : "antihorario";
        }
        if (Mathf.IsEqualApprox(a.Y, b.Y)) return b.X > a.X ? "OE" : "EO";
        if (Mathf.IsEqualApprox(a.X, b.X)) return b.Y > a.Y ? "NS" : "SN";
        return "curva";
    }
}

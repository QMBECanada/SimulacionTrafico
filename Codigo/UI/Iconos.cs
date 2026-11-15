using Godot;

namespace SimulacionTrafico.UI;

/// <summary>Iconos de los botones de la barra del reloj, dibujados por el programa (blancos sobre fondo transparente).</summary>
public static class Iconos
{
    private const int Tam = 32;
    private static ImageTexture? _pausa, _reproducir, _parar, _menu;

    public static ImageTexture Pausa => _pausa ??= Crear(img =>
    {
        Rect(img, 8, 6, 14, 26);
        Rect(img, 18, 6, 24, 26);
    });

    public static ImageTexture Reproducir => _reproducir ??= Crear(img =>
        Triangulo(img, new Vector2(9, 5), new Vector2(9, 27), new Vector2(26, 16)));

    public static ImageTexture Parar => _parar ??= Crear(img => Rect(img, 7, 7, 25, 25));

    public static ImageTexture Menu => _menu ??= Crear(img =>
    {
        Rect(img, 5, 7, 27, 11);
        Rect(img, 5, 14, 27, 18);
        Rect(img, 5, 21, 27, 25);
    });

    private static ImageTexture Crear(System.Action<Image> dibujar)
    {
        var img = Image.CreateEmpty(Tam, Tam, false, Image.Format.Rgba8);
        dibujar(img);
        return ImageTexture.CreateFromImage(img);
    }

    private static void Rect(Image img, int x0, int y0, int x1, int y1)
    {
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++) img.SetPixel(x, y, Colors.White);
    }

    /// <summary>Triángulo relleno, con los bordes suavizados por sobremuestreo.</summary>
    private static void Triangulo(Image img, Vector2 a, Vector2 b, Vector2 c)
    {
        const int n = 4;
        for (int y = 0; y < Tam; y++)
            for (int x = 0; x < Tam; x++)
            {
                int dentro = 0;
                for (int sy = 0; sy < n; sy++)
                    for (int sx = 0; sx < n; sx++)
                        if (Contiene(a, b, c, new Vector2(x + (sx + 0.5f) / n, y + (sy + 0.5f) / n))) dentro++;
                if (dentro > 0) img.SetPixel(x, y, new Color(1f, 1f, 1f, dentro / (float)(n * n)));
            }
    }

    private static bool Contiene(Vector2 a, Vector2 b, Vector2 c, Vector2 p)
    {
        float d1 = (p - b).Cross(a - b), d2 = (p - c).Cross(b - c), d3 = (p - a).Cross(c - a);
        bool negativo = d1 < 0 || d2 < 0 || d3 < 0, positivo = d1 > 0 || d2 > 0 || d3 > 0;
        return !(negativo && positivo);
    }
}

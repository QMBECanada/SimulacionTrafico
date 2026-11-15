using Godot;

namespace SimulacionTrafico.UI;

/// <summary>
/// Cursor del ratón grande, dibujado por el propio programa: se oculta el del sistema y se pinta una flecha de 64 píxeles
/// (el doble que la de Windows) sobre todo lo demás, en el mapa y en las opciones por igual. Con el cursor del sistema ocurría
/// que sobre el mapa seguía viéndose el pequeño.
/// </summary>
public partial class CursorGrande : CanvasLayer
{
    private const int Tam = 64;

    // Contorno de la flecha en coordenadas de 0 a 1 (la punta arriba a la izquierda).
    private static readonly Vector2[] Forma =
    {
        new(0.00f, 0.00f), new(0.00f, 0.80f), new(0.19f, 0.63f), new(0.31f, 0.94f),
        new(0.45f, 0.88f), new(0.33f, 0.58f), new(0.58f, 0.58f),
    };

    private static readonly Vector2 Punta = new(2f, 2f);

    /// <summary>Solo para pruebas: posición en pantalla donde dibujar la flecha en vez de la del ratón.</summary>
    public static Vector2? PosicionForzada { get; set; }

    private TextureRect? _flecha;

    public override void _Ready()
    {
        Layer = 128;   // por encima de todo
        if (DisplayServer.GetName() == "headless")
        {
            SetProcess(false);
            return;
        }
        _flecha = new TextureRect
        {
            Texture = ImageTexture.CreateFromImage(Dibujar()),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Size = new Vector2(Tam, Tam),
            TextureFilter = CanvasItem.TextureFilterEnum.Linear,
        };
        AddChild(_flecha);
        Input.MouseMode = Input.MouseModeEnum.Hidden;
    }

    public override void _Process(double delta)
    {
        if (_flecha == null) return;
        var p = PosicionForzada ?? GetViewport().GetMousePosition();
        _flecha.Visible = GetViewport().GetVisibleRect().HasPoint(p);
        _flecha.Position = p - Punta;
    }

    public override void _ExitTree()
    {
        if (_flecha != null) Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    /// <summary>Guarda la imagen del cursor (para verla).</summary>
    public static void Guardar(string ruta) => Dibujar().SavePng(ruta);

    private static Image Dibujar()
    {
        var img = Image.CreateEmpty(Tam, Tam, false, Image.Format.Rgba8);
        var pts = new Vector2[Forma.Length];
        for (int i = 0; i < Forma.Length; i++) pts[i] = Forma[i] * (Tam - 6) + Punta;

        // Sobremuestreo 4x4 para suavizar los bordes.
        const int n = 4;
        for (int y = 0; y < Tam; y++)
            for (int x = 0; x < Tam; x++)
            {
                float dentro = 0f, borde = 0f;
                for (int sy = 0; sy < n; sy++)
                    for (int sx = 0; sx < n; sx++)
                    {
                        var p = new Vector2(x + (sx + 0.5f) / n, y + (sy + 0.5f) / n);
                        if (Contiene(pts, p)) dentro++;
                        else if (DistanciaAlContorno(pts, p) < 2.2f) borde++;
                    }
                if (dentro + borde <= 0f) continue;
                // Blanco por dentro, negro en el borde; el alfa es la cobertura.
                float c = dentro / (dentro + borde) > 0.5f ? 1f : 0f;
                img.SetPixel(x, y, new Color(c, c, c, Mathf.Min(1f, (dentro + borde) / (n * n))));
            }
        return img;
    }

    private static bool Contiene(Vector2[] poligono, Vector2 p)
    {
        bool dentro = false;
        for (int i = 0, j = poligono.Length - 1; i < poligono.Length; j = i++)
            if ((poligono[i].Y > p.Y) != (poligono[j].Y > p.Y) &&
                p.X < (poligono[j].X - poligono[i].X) * (p.Y - poligono[i].Y) / (poligono[j].Y - poligono[i].Y) + poligono[i].X)
                dentro = !dentro;
        return dentro;
    }

    private static float DistanciaAlContorno(Vector2[] poligono, Vector2 p)
    {
        float mejor = float.PositiveInfinity;
        for (int i = 0, j = poligono.Length - 1; i < poligono.Length; j = i++)
        {
            var ab = poligono[i] - poligono[j];
            float t = Mathf.Clamp((p - poligono[j]).Dot(ab) / ab.LengthSquared(), 0f, 1f);
            mejor = Mathf.Min(mejor, (poligono[j] + ab * t).DistanceTo(p));
        }
        return mejor;
    }
}

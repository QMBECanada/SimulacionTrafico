using Godot;

namespace SimulacionTrafico.Camara;

/// <summary>
/// Cámara cenital. Zoom con la rueda (hacia el cursor), arrastre con el ratón y movimiento con WASD o flechas.
/// El zoom se expresa en píxeles por metro.
/// </summary>
public partial class CamaraCiudad : Camera2D
{
    public Rect2 Limites { get; set; }

    /// <summary>Si no es null, la cámara sigue esa posición. Moverla a mano (arrastre, teclas, minimapa) lo cancela.</summary>
    public System.Func<Vector2>? Seguir { get; set; }
    public float ZoomMin { get; set; } = 0.02f;
    public float ZoomMax { get; set; } = 40f;

    private const float FactorRueda = 1.15f;
    private const float VelocidadPantalla = 500f; // px de pantalla por segundo
    private bool _arrastrando;

    public float PixelesPorMetro => Zoom.X;

    /// <summary>La parte del mapa que se ve en pantalla (en metros), ampliada <paramref name="margen"/> metros por cada lado.</summary>
    public Rect2 RectVisible(float margen)
    {
        var tamVista = GetViewportRect().Size / PixelesPorMetro;
        return new Rect2(Position - tamVista / 2f, tamVista).Grow(margen);
    }

    public override void _Ready()
    {
        Zoom = Vector2.One * 4f;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb)
        {
            if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelUp) CambiarZoom(FactorRueda, mb.Position);
            else if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelDown) CambiarZoom(1f / FactorRueda, mb.Position);
            else if (mb.ButtonIndex is MouseButton.Left or MouseButton.Middle or MouseButton.Right)
                _arrastrando = mb.Pressed;
        }
        else if (e is InputEventMouseMotion mm && _arrastrando)
        {
            Seguir = null;
            Position -= mm.Relative / Zoom.X;
            Ajustar();
        }
    }

    public override void _Process(double delta)
    {
        var dir = Vector2.Zero;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) dir.X -= 1;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) dir.X += 1;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) dir.Y -= 1;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) dir.Y += 1;
        if (dir == Vector2.Zero)
        {
            if (Seguir != null) Position = Seguir();
            return;
        }
        Seguir = null;

        float turbo = Input.IsKeyPressed(Key.Shift) ? 3f : 1f;
        Position += dir.Normalized() * VelocidadPantalla * turbo / Zoom.X * (float)delta;
        Ajustar();
    }

    public void CentrarEn(Vector2 mundo)
    {
        Seguir = null;
        Position = mundo;
        Ajustar();
    }

    private void CambiarZoom(float factor, Vector2 ratonEnPantalla)
    {
        float antes = Zoom.X;
        float despues = Mathf.Clamp(antes * factor, ZoomMin, ZoomMax);
        if (Mathf.IsEqualApprox(antes, despues)) return;

        // Mantener bajo el cursor el mismo punto del mundo.
        var centro = GetViewportRect().Size / 2f;
        var mundoBajoCursor = Position + (ratonEnPantalla - centro) / antes;
        Zoom = Vector2.One * despues;
        Position = mundoBajoCursor - (ratonEnPantalla - centro) / despues;
        Ajustar();
    }

    private void Ajustar()
    {
        Position = new Vector2(
            Mathf.Clamp(Position.X, Limites.Position.X, Limites.End.X),
            Mathf.Clamp(Position.Y, Limites.Position.Y, Limites.End.Y));
    }
}

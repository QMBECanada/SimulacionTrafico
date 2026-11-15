using System;
using Godot;

namespace SimulacionTrafico.UI;

/// <summary>
/// Pantalla del modo «solo datos»: la simulación avanza todo lo rápido que puede sin dibujar el mapa, y aquí solo se ve el avance.
/// Al terminar ofrece abrir la carpeta con los datos o volver al menú.
/// </summary>
public partial class PantallaProgreso : Control
{
    public event Action? Cancelar;
    public event Action? VolverAlMenu;

    private readonly string _titulo;
    private Label _texto = null!;
    private ProgressBar _barra = null!;
    private Button _cancelar = null!, _menu = null!, _carpeta = null!;
    private string? _rutaDatos;

    public PantallaProgreso(string titulo) => _titulo = titulo;

    public override void _Ready()
    {
        Size = GetViewportRect().Size;
        GetViewport().SizeChanged += () => Size = GetViewportRect().Size;

        var fondo = new ColorRect { Color = new Color(0.16f, 0.17f, 0.19f) };
        fondo.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(fondo);
        var centro = new CenterContainer();
        centro.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(centro);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(620, 0) };
        centro.AddChild(panel);
        var margen = new MarginContainer();
        foreach (var lado in new[] { "left", "right", "top", "bottom" }) margen.AddThemeConstantOverride($"margin_{lado}", 22);
        panel.AddChild(margen);
        var caja = new VBoxContainer();
        caja.AddThemeConstantOverride("separation", 12);
        margen.AddChild(caja);

        var titulo = new Label { Text = "Generando datos (sin gráficos)", HorizontalAlignment = HorizontalAlignment.Center };
        titulo.AddThemeFontSizeOverride("font_size", 24);
        caja.AddChild(titulo);
        caja.AddChild(new Label { Text = _titulo, HorizontalAlignment = HorizontalAlignment.Center });
        _barra = new ProgressBar { MinValue = 0, MaxValue = 100, CustomMinimumSize = new Vector2(0, 26) };
        caja.AddChild(_barra);
        _texto = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        caja.AddChild(_texto);

        var botones = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        botones.AddThemeConstantOverride("separation", 12);
        _cancelar = new Button { Text = "Cancelar (guarda lo hecho)" };
        _cancelar.Pressed += () => Cancelar?.Invoke();
        _carpeta = new Button { Text = "Abrir la carpeta de datos", Visible = false };
        _carpeta.Pressed += () => { if (_rutaDatos != null) OS.ShellOpen(_rutaDatos); };
        _menu = new Button { Text = "Volver al menú", Visible = false };
        _menu.Pressed += () => VolverAlMenu?.Invoke();
        botones.AddChild(_cancelar);
        botones.AddChild(_carpeta);
        botones.AddChild(_menu);
        caja.AddChild(botones);
    }

    public void Actualizar(double fraccion, string texto)
    {
        _barra.Value = 100.0 * Math.Clamp(fraccion, 0.0, 1.0);
        _texto.Text = texto;
    }

    /// <summary>Ha terminado (o se ha cancelado): muestra dónde están los datos y los botones para seguir.</summary>
    public void Terminar(string texto, string? rutaDatos)
    {
        _texto.Text = texto;
        _rutaDatos = rutaDatos;
        _cancelar.Visible = false;
        _carpeta.Visible = rutaDatos != null;
        _menu.Visible = true;
    }
}

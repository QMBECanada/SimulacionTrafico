using System;
using Godot;
using SimulacionTrafico.Vehiculos;

namespace SimulacionTrafico.UI;

/// <summary>
/// Barra del reloj, siempre visible: pausa / reanudar, parar, deslizador de compresión del tiempo (de tiempo real, 1×,
/// a 60×) con la velocidad que de verdad se consigue, y la hora del día con la salida y puesta del sol.
/// </summary>
public partial class RelojUI : PanelContainer
{
    public const float VelocidadMinima = 1f, VelocidadMaxima = 60f, VelocidadPorDefecto = 10f;

    private readonly Simulacion _sim;
    private readonly Action _parar;
    private readonly Action _menu;
    private readonly Func<string> _textoHora;
    private HSlider _deslizador = null!;
    private Button _pausa = null!, _pararBoton = null!;
    private Label _hora = null!, _velocidad = null!;
    private float _factor;
    private bool _pausado, _terminada;

    public RelojUI(Simulacion sim, Action parar, Action menu, Func<string> textoHora, float velocidadInicial)
    {
        _sim = sim;
        _parar = parar;
        _menu = menu;
        _textoHora = textoHora;
        _factor = Mathf.Clamp(velocidadInicial > 0f ? velocidadInicial : VelocidadPorDefecto, VelocidadMinima, VelocidadMaxima);
    }

    public override void _Ready()
    {
        var margen = new MarginContainer();
        foreach (var lado in new[] { "left", "right", "top", "bottom" }) margen.AddThemeConstantOverride($"margin_{lado}", 8);
        AddChild(margen);
        var fila = new HBoxContainer();
        fila.AddThemeConstantOverride("separation", 10);
        margen.AddChild(fila);

        _pausa = new Button { Icon = Iconos.Pausa, CustomMinimumSize = new Vector2(46, 38), FocusMode = FocusModeEnum.None, TooltipText = "Pausa" };
        _pausa.Pressed += AlternarPausa;
        _pararBoton = new Button { Icon = Iconos.Parar, CustomMinimumSize = new Vector2(46, 38), FocusMode = FocusModeEnum.None, TooltipText = "Parar: termina la simulación y guarda los datos" };
        _pararBoton.Pressed += () => { if (!_terminada) _parar(); };
        fila.AddChild(_pausa);
        fila.AddChild(_pararBoton);
        var menu = new Button { Icon = Iconos.Menu, CustomMinimumSize = new Vector2(46, 38), FocusMode = FocusModeEnum.None, TooltipText = "Menú: termina la simulación y vuelve a la pantalla inicial (tecla M)" };
        menu.Pressed += _menu;
        fila.AddChild(menu);

        _deslizador = new HSlider
        {
            MinValue = VelocidadMinima, MaxValue = VelocidadMaxima, Step = 1, Value = _factor,
            CustomMinimumSize = new Vector2(170, 0), SizeFlagsVertical = SizeFlags.ShrinkCenter, FocusMode = FocusModeEnum.None,
        };
        _deslizador.ValueChanged += v => _factor = (float)v;
        fila.AddChild(_deslizador);

        _velocidad = new Label { CustomMinimumSize = new Vector2(150, 0) };
        _hora = new Label();
        fila.AddChild(_velocidad);
        fila.AddChild(new VSeparator());
        fila.AddChild(_hora);

        GetViewport().SizeChanged += Colocar;
        Colocar();
    }

    /// <summary>Arriba a la derecha.</summary>
    private void Colocar() => Position = new Vector2(GetViewportRect().Size.X - Size.X - 12, 8);

    public void AlternarPausa()
    {
        if (_terminada) return;
        _pausado = !_pausado;
    }

    /// <summary>Fija la compresión del tiempo (1 a 60) y reanuda si estaba en pausa.</summary>
    public void FijarVelocidad(float v)
    {
        if (_terminada) return;
        _factor = Mathf.Clamp(v, VelocidadMinima, VelocidadMaxima);
        _deslizador.SetValueNoSignal(_factor);
        _pausado = false;
    }

    public void MarcarTerminada()
    {
        _terminada = true;
        _sim.Aceleracion = 0f;
    }

    public override void _Process(double delta)
    {
        _sim.Aceleracion = _pausado || _terminada ? 0f : _factor;
        _pausa.Icon = _pausado ? Iconos.Reproducir : Iconos.Pausa;
        _pausa.TooltipText = _pausado ? "Reanudar" : "Pausa";
        _pausa.Disabled = _pararBoton.Disabled = _terminada;
        _deslizador.Editable = !_terminada;

        _velocidad.Text = _terminada ? "Terminada"
            : _pausado ? "En pausa"
            : $"Velocidad x{_factor:0}" + (_sim.Factor < _factor * 0.9f ? $"  (el equipo llega a x{_sim.Factor:0.0})" : "");
        _hora.Text = _textoHora();
        Colocar();
    }
}

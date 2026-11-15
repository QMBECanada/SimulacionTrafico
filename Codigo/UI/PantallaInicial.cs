using System;
using Godot;
using SimulacionTrafico.Mapa;
using SimulacionTrafico.Nucleo;
using SimulacionTrafico.Vehiculos;

namespace SimulacionTrafico.UI;

/// <summary>Lo que el usuario elige en la pantalla inicial.</summary>
public sealed record ConfiguracionInicial(
    int Ciudad, double HoraInicio, double Horas, int Vehiculos, int Semilla, float Navegador, float[] PorcentajePorHora, string? Estudio,
    int Localidad, int Mes, int Dia, double CalentamientoMin, bool SoloDatos, bool SemaforosAdaptativos, int Radares, bool Lluvia, bool Peatones,
    float DeFuera, float TrabajanFuera, float DePaso);

/// <summary>
/// Pantalla inicial: ciudad, hora de inicio y duración (máximo 24 horas), vehículos máximos, semilla, conductores con
/// navegador, nombre del estudio y porcentaje de coches en la calle para cada hora del día.
/// </summary>
public partial class PantallaInicial : Control
{
    public event Action<ConfiguracionInicial>? Iniciar;

    private OptionButton _ciudad = null!, _localidad = null!;
    private SpinBox _radares = null!;
    private SpinBox _mes = null!, _dia = null!, _calentamiento = null!;
    private SpinBox _horaInicio = null!, _horas = null!, _vehiculos = null!, _semilla = null!, _navegador = null!, _todasIgual = null!;
    private SpinBox _deFuera = null!, _trabajanFuera = null!, _dePaso = null!;
    private CheckBox _guardar = null!, _soloDatos = null!, _adaptativos = null!, _lluvia = null!, _peatones = null!;
    private LineEdit _nombre = null!;
    private Label _aviso = null!;
    private readonly SpinBox[] _porcentajes = new SpinBox[24];

    public override void _Ready()
    {
        // Dentro de un CanvasLayer no hay contenedor padre: se ajusta al tamaño de la ventana y se mantiene al cambiarla.
        Size = GetViewportRect().Size;
        GetViewport().SizeChanged += () => Size = GetViewportRect().Size;

        // Fondo: la portada, cubriendo toda la ventana (sin deformarla).
        var fondo = new ColorRect { Color = new Color(0.05f, 0.07f, 0.12f) };
        fondo.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(fondo);
        if (ResourceLoader.Exists("res://Assets/portada/portada.png"))
        {
            var portada = new TextureRect
            {
                Texture = GD.Load<Texture2D>("res://Assets/portada/portada.png"),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            portada.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(portada);
        }

        // El menú, a la derecha: a la izquierda queda a la vista el título de la portada.
        var fila = new HBoxContainer();
        fila.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(fila);
        fila.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        var centro = new CenterContainer();
        fila.AddChild(centro);
        fila.AddChild(new Control { CustomMinimumSize = new Vector2(16, 0), MouseFilter = MouseFilterEnum.Ignore });

        // El menú, sobre fondo oscuro semitransparente como las tablas de información de la simulación.
        var panel = new PanelContainer();
        var sombra = new StyleBoxFlat { BgColor = new Color(0.08f, 0.09f, 0.11f, 0.72f) };
        sombra.SetCornerRadiusAll(6);
        panel.AddThemeStyleboxOverride("panel", sombra);
        centro.AddChild(panel);
        var margen = new MarginContainer();
        foreach (var lado in new[] { "left", "right", "top", "bottom" }) margen.AddThemeConstantOverride($"margin_{lado}", 22);
        panel.AddChild(margen);
        var caja = new VBoxContainer();
        caja.AddThemeConstantOverride("separation", 6);
        margen.AddChild(caja);

        var titulo = new Label { Text = "Simulación de tráfico", HorizontalAlignment = HorizontalAlignment.Center };
        titulo.AddThemeFontSizeOverride("font_size", 26);
        caja.AddChild(titulo);

        var rejilla = new GridContainer { Columns = 2 };
        rejilla.AddThemeConstantOverride("h_separation", 16);
        rejilla.AddThemeConstantOverride("v_separation", 4);
        caja.AddChild(rejilla);

        _ciudad = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var c in CatalogoCiudades.Ciudades) _ciudad.AddItem(c.nombre);
        _ciudad.ItemSelected += _ => AjustarVehiculos();
        Fila(rejilla, "Ciudad", _ciudad);

        _horaInicio = Numero(0, 23, 8);
        Fila(rejilla, "Hora de inicio (0-23)", _horaInicio);
        _horas = Numero(1, 24, 12);
        Fila(rejilla, "Horas a simular (máximo 24)", _horas);
        _calentamiento = Numero(0, 120, 15);
        Fila(rejilla, "Calentamiento sin registrar (min)", _calentamiento);
        _vehiculos = Numero(100, 300000, 18000, 100);
        Fila(rejilla, "Vehículos (flota de la ciudad)", _vehiculos);
        _semilla = Numero(1, 999999, 1);
        Fila(rejilla, "Semilla", _semilla);
        _navegador = Numero(0, 100, 60);
        Fila(rejilla, "Conductores con navegador (%)", _navegador);
        _radares = Numero(0, GeneradorCiudad.RadaresMaximo, 2);
        Fila(rejilla, "Radares (máximo 100)", _radares);

        // Sol: la ciudad del mundo y el día del año fijan la salida y la puesta del sol (de noche los edificios se oscurecen).
        _localidad = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var l in Localidades.Todas) _localidad.AddItem(l.Nombre);
        _localidad.Selected = Localidades.Indice(Localidades.PorDefecto);
        Fila(rejilla, "Sol: ciudad del mundo", _localidad);
        var hoy = Time.GetDateDictFromSystem();
        _mes = Numero(1, 12, (int)hoy["month"]);
        _dia = Numero(1, 31, (int)hoy["day"]);
        var fecha = new HBoxContainer();
        fecha.AddChild(new Label { Text = "mes " });
        fecha.AddChild(_mes);
        fecha.AddChild(new Label { Text = "  día " });
        fecha.AddChild(_dia);
        Fila(rejilla, "Sol: fecha", fecha);

        var estudio = new HBoxContainer();
        _guardar = new CheckBox { Text = "Guardar datos como", ButtonPressed = true };
        _nombre = new LineEdit { Text = "estudio", CustomMinimumSize = new Vector2(160, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        estudio.AddChild(_guardar);
        estudio.AddChild(_nombre);
        rejilla.AddChild(new Label { Text = "Estudio (carpeta Estudios/)" });
        rejilla.AddChild(estudio);

        _adaptativos = new CheckBox { Text = "Adaptativos (se ajustan al tráfico)", ButtonPressed = true };
        Fila(rejilla, "Semáforos", _adaptativos);
        var condiciones = new HBoxContainer();
        condiciones.AddThemeConstantOverride("separation", 18);
        _lluvia = new CheckBox { Text = "Lluvia (todo el estudio)" };
        _peatones = new CheckBox { Text = "Peatones", ButtonPressed = true };
        condiciones.AddChild(_lluvia);
        condiciones.AddChild(_peatones);
        Fila(rejilla, "Condiciones", condiciones);
        var exterior = new HBoxContainer();
        exterior.AddThemeConstantOverride("separation", 8);
        _deFuera = Numero(0, 100, 15);
        _trabajanFuera = Numero(0, 100, 5);
        _dePaso = Numero(0, 100, 5);
        foreach (var (texto, caja2) in new[] { ("viven fuera", _deFuera), ("trabajan fuera", _trabajanFuera), ("de paso", _dePaso) })
        {
            caja2.CustomMinimumSize = new Vector2(70, 0);
            exterior.AddChild(new Label { Text = texto });
            exterior.AddChild(caja2);
        }
        Fila(rejilla, "Exterior (%)", exterior);
        _soloDatos = new CheckBox { Text = "Solo datos: sin gráficos, lo más rápido posible" };
        Fila(rejilla, "Modo", _soloDatos);

        caja.AddChild(new HSeparator());
        caja.AddChild(new Label { Text = "Viajes que empiezan cada hora, en % de la flota (unos 3 al día por vehículo; ±15 % al azar)" });

        var horasGrid = new GridContainer { Columns = 8 };
        horasGrid.AddThemeConstantOverride("h_separation", 6);
        horasGrid.AddThemeConstantOverride("v_separation", 4);
        caja.AddChild(horasGrid);
        for (int h = 0; h < 24; h++)
        {
            var celda = new HBoxContainer();
            celda.AddChild(new Label { Text = $"{h:00}h", CustomMinimumSize = new Vector2(30, 0) });
            var s = Numero(0, 100, (int)Demanda.CurvaPorDefecto[h]);
            s.CustomMinimumSize = new Vector2(62, 0);
            _porcentajes[h] = s;
            celda.AddChild(s);
            horasGrid.AddChild(celda);
        }

        var atajos = new HBoxContainer();
        var porDefecto = new Button { Text = "Curva por defecto" };
        porDefecto.Pressed += () => { for (int h = 0; h < 24; h++) _porcentajes[h].Value = Demanda.CurvaPorDefecto[h]; };
        _todasIgual = Numero(0, 100, 12);
        var igual = new Button { Text = "Todas las horas a (%):" };
        igual.Pressed += () => { foreach (var s in _porcentajes) s.Value = _todasIgual.Value; };
        atajos.AddChild(porDefecto);
        atajos.AddChild(igual);
        atajos.AddChild(_todasIgual);
        caja.AddChild(atajos);

        _aviso = new Label { Modulate = new Color(1f, 0.8f, 0.4f) };
        caja.AddChild(_aviso);

        var iniciar = new Button { Text = "Iniciar simulación", CustomMinimumSize = new Vector2(0, 40) };
        iniciar.Pressed += Aceptar;
        caja.AddChild(iniciar);

        caja.AddChild(new HSeparator());
        var creditos = new RichTextLabel
        {
            BbcodeEnabled = true, FitContent = true, ScrollActive = false,
            Text = "[center][b]Créditos[/b] · " +
                   "Programación y test: Claude, [url=https://claude.ai]https://claude.ai[/url] · " +
                   "Diseño vehículos: Gpt, [url=https://chatgpt.com]https://chatgpt.com[/url]\n" +
                   "FDE, arnés y dirección: Vicente Simón, [url=https://www.linkedin.com/in/vicentesimon]https://www.linkedin.com/in/vicentesimon[/url][/center]",
        };
        creditos.AddThemeFontSizeOverride("normal_font_size", 13);
        creditos.AddThemeFontSizeOverride("bold_font_size", 13);
        creditos.MetaClicked += meta => OS.ShellOpen(meta.AsString());   // abre el enlace en el navegador
        caja.AddChild(creditos);

        AjustarVehiculos();
    }

    /// <summary>Valores iniciales razonables según el tamaño de la ciudad: la flota y los radares.</summary>
    private void AjustarVehiculos()
    {
        var (_, ancho, alto, _) = CatalogoCiudades.Ciudades[_ciudad.Selected];
        _radares.Value = GeneradorCiudad.RadaresPorDefecto(ancho, alto);
        // Unos 3000 vehículos por km² (una ciudad media), como mucho 200 000.
        _vehiculos.Value = Mathf.Clamp(Mathf.Round(3000f * ancho * alto / 1e6f / 100f) * 100f, 1000f, 200000f);
    }

    private void Aceptar()
    {
        int ciudad = _ciudad.Selected + 1;
        if (!Godot.FileAccess.FileExists(CatalogoCiudades.Ruta(ciudad)))
        {
            _aviso.Text = "Falta el fichero de esa ciudad (ejecuta con --generar-ciudades).";
            return;
        }
        if (_soloDatos.ButtonPressed && !_guardar.ButtonPressed)
        {
            _aviso.Text = "En el modo solo datos hay que guardar el estudio (marca «Guardar datos como»).";
            return;
        }
        var porcentajes = new float[24];
        for (int h = 0; h < 24; h++) porcentajes[h] = (float)_porcentajes[h].Value;
        Iniciar?.Invoke(new ConfiguracionInicial(ciudad, _horaInicio.Value, _horas.Value, (int)_vehiculos.Value, (int)_semilla.Value,
            (float)_navegador.Value / 100f, porcentajes, _guardar.ButtonPressed ? _nombre.Text : null,
            _localidad.Selected, (int)_mes.Value, (int)_dia.Value, _calentamiento.Value, _soloDatos.ButtonPressed, _adaptativos.ButtonPressed, (int)_radares.Value,
            _lluvia.ButtonPressed, _peatones.ButtonPressed,
            (float)_deFuera.Value / 100f, (float)_trabajanFuera.Value / 100f, (float)_dePaso.Value / 100f));
    }

    private static SpinBox Numero(double min, double max, double valor, double paso = 1) =>
        new() { MinValue = min, MaxValue = max, Value = valor, Step = paso, SizeFlagsHorizontal = SizeFlags.ExpandFill };

    private static void Fila(GridContainer g, string texto, Control control)
    {
        g.AddChild(new Label { Text = texto });
        g.AddChild(control);
    }
}

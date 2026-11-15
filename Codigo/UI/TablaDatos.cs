using System.Collections.Generic;
using Godot;

namespace SimulacionTrafico.UI;

/// <summary>
/// Tabla de datos en pantalla: pares «nombre: valor» en columnas, sobre un fondo oscuro semitransparente. La usa la información
/// general de la simulación y la del coche seguido.
/// </summary>
public partial class TablaDatos : PanelContainer
{
    private static readonly Color ColorNombre = new(0.72f, 0.75f, 0.8f);
    private static readonly Color ColorValor = new(1f, 1f, 1f);

    private readonly int _pares;
    private GridContainer _rejilla = null!;
    private readonly List<Label> _celdas = new();

    /// <param name="pares">Cuántos pares nombre-valor por fila.</param>
    public TablaDatos(int pares) => _pares = pares;

    /// <summary>Si se da, la tabla ocupa siempre este tamaño (no cambia con lo que muestre) y reparte el ancho entre las columnas.</summary>
    public Vector2? TamanoFijo { get; set; }

    /// <summary>Texto que se muestra cuando no hay filas (si es null, la tabla se oculta).</summary>
    public string? TextoVacio { get; set; }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        var fondo = new StyleBoxFlat { BgColor = new Color(0.08f, 0.09f, 0.11f, 0.72f) };
        fondo.SetCornerRadiusAll(4);
        fondo.SetContentMarginAll(8);
        AddThemeStyleboxOverride("panel", fondo);
        _rejilla = new GridContainer { Columns = _pares * 2, MouseFilter = MouseFilterEnum.Ignore };
        _rejilla.AddThemeConstantOverride("h_separation", 12);
        _rejilla.AddThemeConstantOverride("v_separation", 1);
        AddChild(_rejilla);
    }

    /// <summary>Muestra estas filas (se reparten en columnas de izquierda a derecha). Sin filas, la tabla se oculta.</summary>
    public void Mostrar(IReadOnlyList<(string nombre, string valor)> filas)
    {
        if (filas.Count == 0 && TextoVacio != null) filas = new[] { ("", TextoVacio) };
        Visible = filas.Count > 0;
        if (!Visible || _rejilla == null) return;
        if (TamanoFijo is { } fijo)
        {
            CustomMinimumSize = fijo;
            Size = fijo;
            float util = fijo.X - 16f - 12f * (_pares * 2 - 1);
            foreach (var (l, i) in System.Linq.Enumerable.Select(_celdas, (l, i) => (l, i)))
                l.CustomMinimumSize = new Vector2(util / _pares * (i % 2 == 0 ? 0.38f : 0.62f), 0);
        }
        int celdas = (filas.Count + _pares - 1) / _pares * _pares * 2;
        while (_celdas.Count < celdas)
        {
            var l = new Label { MouseFilter = MouseFilterEnum.Ignore };
            l.AddThemeFontSizeOverride("font_size", 14);
            l.AddThemeColorOverride("font_color", _celdas.Count % 2 == 0 ? ColorNombre : ColorValor);
            if (TamanoFijo is { } f)
            {
                float util = f.X - 16f - 12f * (_pares * 2 - 1);
                l.CustomMinimumSize = new Vector2(util / _pares * (_celdas.Count % 2 == 0 ? 0.38f : 0.62f), 0);
                l.ClipText = true;
            }
            _rejilla.AddChild(l);
            _celdas.Add(l);
        }
        for (int i = 0; i < _celdas.Count; i++)
        {
            bool usada = i < celdas;
            _celdas[i].Visible = usada;
            if (!usada) continue;
            int fila = i / 2;
            _celdas[i].Text = fila < filas.Count ? (i % 2 == 0 ? filas[fila].nombre : filas[fila].valor) : "";
        }
        if (TamanoFijo is { } t) Size = t; else ResetSize();
    }
}

using System.Linq;
using Godot;
using SimulacionTrafico.Camara;
using SimulacionTrafico.Datos;
using SimulacionTrafico.Mapa;

namespace SimulacionTrafico.UI;

/// <summary>
/// Al poner el ratón sobre una calle (o un cruce) muestra su nombre y sus datos en una etiqueta junto al cursor, y la resalta
/// en el mapa. Los nombres son los de los ficheros de datos (H-fila-columna, V-fila-columna, C-n, R-... para las rotondas).
/// </summary>
public partial class InfoCalle : Node2D
{
    private static readonly Color ColorResalte = new(1f, 0.95f, 0.2f, 0.5f);

    /// <summary>Solo para pruebas: punto del mapa que se trata como si el ratón estuviera encima.</summary>
    public static Vector2? PuntoForzado { get; set; }

    private readonly Grafo _grafo;
    private readonly CamaraCiudad _camara;
    private readonly IndiceCalles _indice;
    private readonly PanelContainer _panel;
    private readonly Label _texto;
    private Tramo? _tramo;
    private Nodo? _nodo;

    public InfoCalle(Grafo grafo, CamaraCiudad camara, CanvasLayer capa)
    {
        _grafo = grafo;
        _camara = camara;
        _indice = new IndiceCalles(grafo);
        ZIndex = 9;

        _panel = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        var margen = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        foreach (var lado in new[] { "left", "right", "top", "bottom" }) margen.AddThemeConstantOverride($"margin_{lado}", 6);
        _texto = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        margen.AddChild(_texto);
        _panel.AddChild(margen);
        capa.AddChild(_panel);
    }

    public override void _Process(double delta)
    {
        var vp = GetViewport();
        // Sobre los controles (barra del reloj, minimapa) o arrastrando el mapa no se muestra nada.
        if (PuntoForzado == null && vp.GuiGetHoveredControl() != null || Input.IsMouseButtonPressed(MouseButton.Left) || Input.IsMouseButtonPressed(MouseButton.Middle))
        {
            Mostrar(null, null);
            return;
        }

        var punto = PuntoForzado ?? _camara.GetGlobalMousePosition();
        var (nodo, tramo) = _indice.Buscar(punto, 8f / Mathf.Max(_camara.PixelesPorMetro, 1e-4f));
        Mostrar(nodo, tramo);
        if (_panel.Visible)
        {
            var pos = (PuntoForzado is { } f ? _camara.GetCanvasTransform() * f : vp.GetMousePosition()) + new Vector2(36f, 30f);
            var tam = _panel.Size;
            var pantalla = vp.GetVisibleRect().Size;
            pos.X = Mathf.Min(pos.X, pantalla.X - tam.X - 4f);
            pos.Y = Mathf.Min(pos.Y, pantalla.Y - tam.Y - 4f);
            _panel.Position = pos;
        }
    }

    private void Mostrar(Nodo? nodo, Tramo? tramo)
    {
        if (nodo == _nodo && tramo == _tramo) return;
        _nodo = nodo;
        _tramo = tramo;
        _panel.Visible = nodo != null || tramo != null;
        if (nodo != null) _texto.Text = DescribirCruce(nodo);
        else if (tramo != null) _texto.Text = DescribirCalle(tramo);
        _panel.ResetSize();
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_tramo != null) DrawPolyline(_tramo.Eje.Puntos, ColorResalte, _tramo.AnchoCalzada + 2f);
        else if (_nodo != null) DrawCircle(_nodo.Pos, _nodo.Radio + 1f, ColorResalte);
    }

    private static string DescribirCalle(Tramo t)
    {
        string tipo = t.EsRotonda ? "Rotonda" : t.EsTunel ? "Túnel" : t.EsPuente ? "Puente" : t.Categoria.ToString();
        string carriles = t.UnSentido ? $"{t.TotalCarriles} carril{(t.TotalCarriles > 1 ? "es" : "")}, un sentido"
                                      : $"{t.CarrilesIda} + {t.CarrilesVuelta} carriles";
        var sentidos = new[] { true, false }.Where(ida => (ida ? t.CarrilesIda : t.CarrilesVuelta) > 0).Select(ida => Nombres.Direccion(t, ida));
        return $"{Nombres.Calle(t)}\n{tipo} · {carriles} · {t.LimiteKmh:0} km/h\nSentido: {string.Join(" y ", sentidos)} · {t.Eje.Longitud:0} m"
               + (t.Radar != null ? $"\nRadar {t.Radar.Nombre} (a mitad de la calle, en todos los sentidos)" : "");
    }

    private string DescribirCruce(Nodo n)
    {
        string control = n.Control switch
        {
            TipoControl.Semaforo => "semáforo",
            TipoControl.Stop => "stop",
            TipoControl.Ceda => "ceda el paso",
            TipoControl.Rotonda => "entrada a rotonda",
            _ => "sin control",
        };
        var calles = n.Tramos.Select(Nombres.Calle).Distinct().OrderBy(c => c, System.StringComparer.Ordinal);
        string texto = $"Cruce {Nombres.Cruce(n)}\n{control}\nCalles: {string.Join(", ", calles)}";
        foreach (var (d, h) in n.GirosProhibidos.OrderBy(p => p.desde).ThenBy(p => p.hasta))
        {
            var desde = _grafo.Tramos[d];
            var hasta = _grafo.Tramos[h];
            var entrante = n.Entrantes.FirstOrDefault(l => l.Tramo == desde);
            string giro = entrante == null ? "girar" : _grafo.GiroHacia(entrante, hasta) switch
            {
                TipoGiro.Izquierda => "girar a la izquierda",
                TipoGiro.Derecha => "girar a la derecha",
                _ => "seguir recto",
            };
            texto += $"\nProhibido {giro}: {Nombres.Calle(desde)} → {Nombres.Calle(hasta)}";
        }
        return texto;
    }
}

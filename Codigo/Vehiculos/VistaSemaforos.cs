using System.Collections.Generic;
using Godot;
using SimulacionTrafico.Camara;
using SimulacionTrafico.Mapa;
using SimulacionTrafico.Nucleo;
using SimulacionTrafico.Trafico;

namespace SimulacionTrafico.Vehiculos;

/// <summary>
/// Dibuja la línea de detención de cada aproximación con señal, solo cuando el zoom permite verla:
/// semáforo (verde, ámbar o rojo según su estado), stop (rojo) y ceda (naranja).
/// </summary>
public partial class VistaSemaforos : Node2D
{
    private const float ZoomMinimo = 0.8f;

    private static readonly Color Verde = new(0.15f, 0.75f, 0.25f);
    private static readonly Color Ambar = new(1.0f, 0.75f, 0.05f);
    private static readonly Color Rojo = new(0.88f, 0.12f, 0.12f);
    private static readonly Color Naranja = new(0.95f, 0.55f, 0.10f);
    private static readonly Color Morado = new(0.60f, 0.25f, 0.80f);
    private static readonly Color AzulGuardia = new(0.10f, 0.30f, 0.85f);

    private readonly CamaraCiudad _camara;
    private readonly Simulacion _sim;
    private readonly RejillaEspacial<Nodo> _controlados = new(250f);
    private readonly List<Nodo> _visibles = new();

    public VistaSemaforos(Grafo grafo, CamaraCiudad camara, Simulacion sim)
    {
        _camara = camara;
        _sim = sim;
        foreach (var n in grafo.Nodos)
            if (n.Control != TipoControl.Ninguno) _controlados.Agregar(n.Pos, n);
        ZIndex = 8;
        ZAsRelative = false;
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        float zoom = _camara.PixelesPorMetro;
        if (zoom < ZoomMinimo) return;
        var rect = _camara.RectVisible(30f);

        _controlados.Buscar(rect, _visibles);
        foreach (var n in _visibles)
        {
            if (n.Guardia != null)
            {
                // El guardia: un círculo azul con aro blanco en el centro del cruce.
                DrawCircle(n.Pos, 4.2f, Colors.White);
                DrawCircle(n.Pos, 3.2f, AzulGuardia);
            }
            foreach (var e in n.Entrantes)
            {
                Color color;
                if (n.Guardia is { } guardia)
                    color = guardia.OrdenPara(e) switch
                    {
                        OrdenGuardia.Pasar => Verde,
                        OrdenGuardia.PasarForzado => Morado,
                        _ => Rojo,
                    };
                else if (n.Control == TipoControl.Semaforo)
                    color = n.EstadoPara(e, _sim.TiempoSimulado) switch
                    {
                        EstadoSemaforo.Verde => Verde,
                        EstadoSemaforo.Ambar => Ambar,
                        _ => Rojo,
                    };
                else if (n.EsPrioritario(e.Tramo)) continue;
                else color = n.Control == TipoControl.Stop ? Rojo : Naranja;

                var p = e.Eje.Puntos[^1];
                var d = e.Eje.Direccion(e.Longitud);
                var perp = new Vector2(-d.Y, d.X) * (Escala.AnchoCarril / 2f - 0.15f);
                DrawLine(p - perp, p + perp, color, 0.6f);
            }
        }
    }
}

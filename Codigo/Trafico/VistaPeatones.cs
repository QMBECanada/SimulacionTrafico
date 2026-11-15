using System.Collections.Generic;
using Godot;
using SimulacionTrafico.Camara;
using SimulacionTrafico.Mapa;
using SimulacionTrafico.Nucleo;
using SimulacionTrafico.Vehiculos;

namespace SimulacionTrafico.Trafico;

/// <summary>
/// Dibuja los pasos de peatones (franjas blancas) y las personas que esperan en la acera o cruzan, solo al acercarse y solo en la
/// parte visible del mapa.
/// </summary>
public partial class VistaPeatones : Node2D
{
    private const float ZoomMinimo = 1.5f;
    private static readonly Color Franja = new(0.97f, 0.97f, 0.97f, 0.9f);
    private static readonly Color Persona = new(0.18f, 0.2f, 0.35f);
    private static readonly Color Borde = new(1f, 1f, 1f);

    private readonly Simulacion _sim;
    private readonly CamaraCiudad _camara;
    // Pasos, puertas y paradas no cambian durante la simulación: se buscan en la parte visible con un índice espacial.
    private readonly RejillaEspacial<PasoPeatones> _pasos = new(250f);
    private readonly RejillaEspacial<Lugar> _puertas = new(250f);
    private readonly RejillaEspacial<Lugar> _paradas = new(250f);
    private readonly List<PasoPeatones> _pasosVisibles = new();
    private readonly List<Lugar> _puertasVisibles = new();
    private readonly List<Lugar> _paradasVisibles = new();

    public VistaPeatones(Simulacion sim, CamaraCiudad camara)
    {
        _sim = sim;
        _camara = camara;
        foreach (var paso in sim.Peatones.Pasos) _pasos.Agregar(paso.Centro, paso);
        foreach (var puerta in sim.Puertas) _puertas.Agregar(puerta.Puerta, puerta);
        foreach (var linea in sim.Lineas)
            foreach (var parada in linea.Paradas) _paradas.Agregar(parada.Puerta, parada);
        ZIndex = 7;   // sobre las marcas de la calzada, bajo los semáforos y los coches
        ZAsRelative = false;
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        float zoom = _camara.PixelesPorMetro;
        if (zoom < ZoomMinimo) return;
        var rect = _camara.RectVisible(20f);

        _pasos.Buscar(rect, _pasosVisibles);   // sin peatones no hay pasos
        foreach (var paso in _pasosVisibles)
        {
            // Franjas paralelas a la calle, de 0,5 m, cada metro, a lo largo de toda la calzada.
            var a = paso.A;
            var cruce = (paso.B - a).Normalized();
            var calle = new Vector2(cruce.Y, -cruce.X) * (PasoPeatones.AnchoFranja / 2f);
            for (float s = 1.25f; s < paso.Largo - 1f; s += 1f)
            {
                var q = a + cruce * s;
                DrawLine(q - calle, q + calle, Franja, 0.5f);
            }
        }
        _puertas.Buscar(rect, _puertasVisibles);
        foreach (var puerta in _puertasVisibles)
        {
            var t = puerta.Acceso.Tramo;
            var d = puerta.Acceso.Eje.Direccion(puerta.Acceso.Longitud);
            var lado = new Vector2(-d.Y, d.X) * (t.AnchoCalzada / 2f + 1f);
            var p = puerta.Puerta - d * 2f;
            DrawLine(p - lado, p + lado, Lugar.ColorDe(TipoLugar.Exterior), 3f);
            DrawLine(p - lado - d * 2.2f, p + lado - d * 2.2f, Colors.White, 0.8f);
        }
        _paradas.Buscar(rect, _paradasVisibles);
        foreach (var parada in _paradasVisibles)
        {
            var d = parada.Acceso.Eje.Direccion(parada.SAcceso);
            var lado = new Vector2(-d.Y, d.X);
            var centro = parada.Puerta + lado * 0.6f;
            var puntos = new[] { centro - d * 7f - lado * 1.4f, centro + d * 7f - lado * 1.4f, centro + d * 7f + lado * 1.4f, centro - d * 7f + lado * 1.4f };
            DrawColoredPolygon(puntos, Colors.White);
            var dentro = new[] { centro - d * 6.6f - lado * 1.0f, centro + d * 6.6f - lado * 1.0f, centro + d * 6.6f + lado * 1.0f, centro - d * 6.6f + lado * 1.0f };
            DrawColoredPolygon(dentro, Lugar.ColorDe(TipoLugar.Parada));
        }
        foreach (var paso in _sim.Peatones.Activos)
        {
            if (!rect.HasPoint(paso.Centro)) continue;
            foreach (var p in paso.Cruzando) Dibujar(paso.Posicion(p));
            // Los que esperan, en la acera de su lado, uno junto a otro.
            int ka = 0, kb = 0;
            var dir = (paso.B - paso.A).Normalized();
            var lado = new Vector2(dir.Y, -dir.X);
            foreach (var p in paso.Esperando)
            {
                int k = p.DesdeA ? ka++ : kb++;
                var borde = p.DesdeA ? paso.A - dir * 0.6f : paso.B + dir * 0.6f;
                Dibujar(borde + lado * ((k % 4) - 1.5f) * 0.7f - (p.DesdeA ? dir : -dir) * (k / 4) * 0.7f);
            }
        }
    }

    private void Dibujar(Vector2 p)
    {
        DrawCircle(p, 0.55f, Borde);
        DrawCircle(p, 0.42f, Persona);
    }
}

using System;
using Godot;

namespace SimulacionTrafico.UI;

/// <summary>
/// Nodo que dibuja con una función externa. Permite montar el mapa en capas (z) sin una clase por capa.
/// Si <see cref="Continuo"/> es true, se redibuja cada fotograma.
/// </summary>
public partial class CapaDibujo : Node2D
{
    private readonly Action<CapaDibujo> _dibujar;
    public bool Continuo { get; set; }

    public CapaDibujo(Action<CapaDibujo> dibujar, int z = 0, bool zAbsoluto = false)
    {
        _dibujar = dibujar;
        ZIndex = z;
        ZAsRelative = !zAbsoluto;
    }

    public override void _Draw() => _dibujar(this);

    public override void _Process(double delta)
    {
        if (Continuo) QueueRedraw();
    }
}

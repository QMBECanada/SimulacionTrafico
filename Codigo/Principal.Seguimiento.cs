using System.Diagnostics;
using System.Linq;
using Godot;
using SimulacionTrafico.Camara;
using SimulacionTrafico.Datos;
using SimulacionTrafico.Mapa;
using SimulacionTrafico.Nucleo;
using SimulacionTrafico.Trafico;
using SimulacionTrafico.UI;
using SimulacionTrafico.Vehiculos;

namespace SimulacionTrafico;

// Principal: teclado y ratón, y seguir a un coche con la cámara.
public partial class Principal : Node2D
{
    public override void _UnhandledInput(InputEvent e)
    {
        if (_sim == null) return;   // aún en la pantalla inicial
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.M }) { VolverAlMenu(); return; }
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Q })
        {
            // Salir del programa: se termina la simulación (se cierra el estudio con sus ficheros) y se cierra la ventana.
            Parar();
            GetTree().Quit();
            return;
        }
        if (_terminada) return;
        if (e is not InputEventKey { Pressed: true, Echo: false } k) return;
        switch (k.Keycode)
        {
            case Key.C: SeguirSiguiente(Mathf.Max(_camara.PixelesPorMetro, 6f)); break;
            case Key.Escape: DejarDeSeguir(); break;
            case Key.A: IrAAccidente(); break;
            case Key.Key1: _reloj?.FijarVelocidad(VelocidadesSim[0]); break;
            case Key.Key2: _reloj?.FijarVelocidad(VelocidadesSim[1]); break;
            case Key.Key3: _reloj?.FijarVelocidad(VelocidadesSim[2]); break;
            case Key.Key4: _reloj?.FijarVelocidad(VelocidadesSim[3]); break;
            case Key.Space: _reloj?.AlternarPausa(); break;
        }
    }

    /// <summary>Pasa a seguir al siguiente coche que esté circulando (por número de coche, dando la vuelta a la flota).</summary>
    private void SeguirSiguiente(float zoom)
    {
        var flota = _sim.Flota;
        int n = flota.Count;
        if (n == 0) return;
        int inicio = _seguidoCoche == null ? 0 : _seguidoCoche.Id + 1;
        for (int k = 0; k < n; k++)
        {
            var c = flota[(inicio + k) % n];
            if (c.Circulando && c != _seguidoCoche)
            {
                SeguirCoche(c, zoom);
                return;
            }
        }
    }

    private void SeguirCoche(Coche coche, float zoom)
    {
        _seguidoCoche = coche;
        _seguidoSinCircular = 0f;
        _vistaCoches.Seleccionado = coche;
        _camara.Zoom = Vector2.One * Mathf.Clamp(zoom, _camara.ZoomMin, _camara.ZoomMax);
        _camara.Seguir = () => coche.PosAnterior.Lerp(coche.Pos, Mathf.Clamp(_sim.Alfa, 0f, 1f));
        _camara.Position = coche.Pos;
    }

    private int _accidenteVisto = -1;
    private bool irAccidentePendiente;   // solo para pruebas (--ver-accidente): va al primer accidente que ocurra

    /// <summary>Lleva la cámara al siguiente accidente en curso (tecla A).</summary>
    private void IrAAccidente()
    {
        var activos = _sim.AccidentesActivos;
        if (activos.Count == 0) return;
        _accidenteVisto = (_accidenteVisto + 1) % activos.Count;
        DejarDeSeguir();
        _camara.Zoom = Vector2.One * Mathf.Clamp(Mathf.Max(_camara.PixelesPorMetro, 5f), _camara.ZoomMin, _camara.ZoomMax);
        _camara.CentrarEn(activos[_accidenteVisto].Pos);
    }

    private void DejarDeSeguir()
    {
        _camara.Seguir = null;
        _vistaCoches.Seleccionado = null;
        _seguidoCoche = null;
    }
}

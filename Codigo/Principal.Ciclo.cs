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

// Principal: modo «solo datos», terminar la simulación y volver a la pantalla inicial.
public partial class Principal : Node2D
{
    /// <summary>Modo «solo datos»: no se dibuja el mapa; la simulación avanza a tramos en cada fotograma y solo se ve el avance.</summary>
    private void IniciarSoloDatos()
    {
        _sim.Aceleracion = 0f;   // la simulación no avanza sola: la mueve AvanzarSoloDatos
        _finSoloDatos = (horasSim > 0 ? horasSim : 24.0) * 3600.0 + calentamientoMin * 60.0;
        var capa = Mundo(new CanvasLayer());
        _progreso = new PantallaProgreso($"{_nombreCiudad} · {numCoches} vehículos · {(horasSim > 0 ? horasSim : 24):0.##} h desde las {(int)(horaInicio ?? 8):00}:00");
        _progreso.Cancelar += () => TerminarSoloDatos(cancelado: true);
        _progreso.VolverAlMenu += VolverAlMenu;
        capa.AddChild(_progreso);
        _relojSolo = Stopwatch.StartNew();
        _soloDatosActivo = true;
        if (captura != null) CapturarYSalir(captura, esperaCaptura);
    }

    private void AvanzarSoloDatos()
    {
        var presupuesto = Stopwatch.StartNew();
        double inicioCalentamiento = calentamientoMin * 60.0;
        while (presupuesto.ElapsedMilliseconds < 60 && _sim.TiempoSimulado < _finSoloDatos)
        {
            if (_estudio != null && _sim.Estudio == null && _sim.TiempoSimulado >= inicioCalentamiento) _sim.AbrirEstudio(_estudio);
            double siguiente = System.Math.Min(_sim.TiempoSimulado + 10.0, _finSoloDatos);
            if (_sim.Estudio == null && _sim.TiempoSimulado < inicioCalentamiento) siguiente = System.Math.Min(siguiente, inicioCalentamiento);
            _sim.AvanzarHasta(siguiente);
        }
        double real = _relojSolo!.Elapsed.TotalSeconds;
        _progreso!.Actualizar(_sim.TiempoSimulado / _finSoloDatos,
            $"Hora simulada {TextoHora()[5..13]} · {_sim.Llegadas} llegadas · {_sim.TotalAccidentes} accidentes · " +
            $"{_sim.TiempoSimulado / System.Math.Max(real, 1e-6):0}× el tiempo real · {real:0} s");
        if (_sim.TiempoSimulado >= _finSoloDatos) TerminarSoloDatos(cancelado: false);
    }

    private void TerminarSoloDatos(bool cancelado)
    {
        if (!_soloDatosActivo) return;
        _soloDatosActivo = false;
        string? carpeta = _estudio?.Carpeta;
        Parar();   // cierra el estudio y deja los ficheros
        double real = _relojSolo?.Elapsed.TotalSeconds ?? 0;
        _progreso?.Terminar(
            (cancelado ? $"Cancelado a las {_sim.TiempoSimulado / 3600.0:0.##} h simuladas" : $"Terminado: {_sim.TiempoSimulado / 3600.0:0.##} h simuladas") +
            $" en {real:0} s.\nDatos en {carpeta}", carpeta);
    }

    private T Mundo<T>(T nodo) where T : Node
    {
        AddChild(nodo);
        _nodosSim.Add(nodo);
        return nodo;
    }

    /// <summary>Vuelve a la pantalla inicial sin cerrar el programa: termina la simulación (guarda el estudio) y quita todo lo suyo.</summary>
    private void VolverAlMenu()
    {
        if (_volviendo || _sim == null) return;
        _volviendo = true;
        Callable.From(VolverAlMenuDiferido).CallDeferred();   // no se puede quitar la interfaz desde un botón de esa misma interfaz
    }

    private void VolverAlMenuDiferido()
    {
        Parar();
        foreach (var n in _nodosSim) n.QueueFree();
        _nodosSim.Clear();
        _info = null;
        _tablaCoche = null!;
        _tablaGeneral = null!;
        _vistaCoches = null!;
        _vista = null!;
        _sim = null!;
        _camara = null!;
        _reloj = null;
        _sol = null;
        _estudio = null;
        _terminada = false;
        _seguidoCoche = null;
        _avisoSeguirT = 0f;
        _seguirPendiente = false;
        _luzAplicada = -1f;
        porcentaje = null;
        _volviendo = false;
        _soloDatosActivo = false;
        _progreso = null;
        MostrarPantallaInicial();
    }

    /// <summary>Termina la simulación (por el botón Parar o porque se cumplieron las horas): cierra el estudio y deja los ficheros.</summary>
    private void Parar()
    {
        if (_terminada) return;
        _terminada = true;
        _reloj?.MarcarTerminada();
        _sim.Aceleracion = 0f;
        _sim.CerrarEstudio();
        GD.Print($"Simulación terminada a las {_sim.TiempoSimulado / 3600.0:0.##} h simuladas." + (_estudio != null ? $" Datos en {_estudio.Carpeta}" : ""));
    }
}

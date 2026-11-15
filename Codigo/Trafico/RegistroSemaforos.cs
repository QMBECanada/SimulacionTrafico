using System.Collections.Generic;
using System.Linq;
using SimulacionTrafico.Datos;
using SimulacionTrafico.Mapa;

namespace SimulacionTrafico.Trafico;

/// <summary>
/// Guarda en <c>semaforos.tsv</c> cada ciclo completo de cada semáforo, una fila por calle que llega al cruce: la hora a la
/// que se puso en verde, cuántos coches pasaron en verde, la hora del naranja, cuántos pasaron en naranja y la hora del
/// rojo. Cuando un guardia controla el cruce, el semáforo no se registra.
/// </summary>
public sealed class RegistroSemaforos
{
    private sealed class Grupo
    {
        public Nodo Nodo = null!;
        public int Numero;
        public List<Tramo> Aproximaciones = new();
        public EstadoSemaforo? Estado;
        public double? HoraVerde, HoraAmbar;
    }

    private readonly List<Grupo> _grupos = new();
    private readonly RegistroTsv _registro;
    private readonly Estudio _estudio;

    public RegistroSemaforos(Grafo grafo, Estudio estudio)
    {
        _estudio = estudio;
        _registro = estudio.Abrir("semaforos.tsv", "Nombre", "Calle", "Direccion", "HoraVerde", "CochesVerde", "HoraNaranja",
            "CochesNaranja", "HoraRojo");

        foreach (var nodo in grafo.Nodos.Where(n => n.Control == TipoControl.Semaforo && n.Semaforo != null))
            for (int g = 0; g < 2; g++)
            {
                var aprox = nodo.Entrantes.Where(l => Nodo.Grupo(l) == g).Select(l => l.Tramo).Distinct().ToList();
                if (aprox.Count > 0) _grupos.Add(new Grupo { Nodo = nodo, Numero = g, Aproximaciones = aprox });
            }
    }

    public void Actualizar(double tiempo)
    {
        foreach (var g in _grupos)
        {
            if (g.Nodo.Guardia != null)
            {
                g.Estado = null; // con un guardia no hay ciclo que registrar
                g.HoraVerde = g.HoraAmbar = null;
                continue;
            }

            var estado = g.Nodo.Semaforo!.Estado(g.Numero, tiempo);
            if (estado == g.Estado) continue;

            switch (estado)
            {
                case EstadoSemaforo.Verde:
                    // Solo cuenta si hemos visto cómo se ponía en verde (no si ya lo estaba al empezar el estudio).
                    g.HoraVerde = g.Estado != null ? tiempo : null;
                    g.HoraAmbar = null;
                    foreach (var t in g.Aproximaciones) g.Nodo.Semaforo.TomarCuentas(t); // empieza a contar de cero
                    break;
                case EstadoSemaforo.Ambar:
                    g.HoraAmbar = tiempo;
                    break;
                case EstadoSemaforo.Rojo:
                    // Termina el ciclo: se escribe solo si lo hemos visto desde el verde.
                    if (g.HoraVerde != null && g.HoraAmbar != null)
                        foreach (var t in g.Aproximaciones)
                        {
                            var (verde, ambar) = g.Nodo.Semaforo.TomarCuentas(t);
                            _registro.Fila($"S-{Nombres.Cruce(g.Nodo)}-{Nombres.Calle(t)}", Nombres.Calle(t),
                                Nombres.Direccion(t, ida: t.B == g.Nodo), _estudio.Hora(g.HoraVerde.Value), verde,
                                _estudio.Hora(g.HoraAmbar.Value), ambar, _estudio.Hora(tiempo));
                        }
                    g.HoraVerde = g.HoraAmbar = null;
                    break;
            }
            g.Estado = estado;
        }
    }
}

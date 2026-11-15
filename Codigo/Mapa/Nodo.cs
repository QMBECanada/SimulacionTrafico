using System.Collections.Generic;
using System.Linq;
using Godot;
using SimulacionTrafico.Trafico;

namespace SimulacionTrafico.Mapa;

public enum TipoControl { Ninguno, Semaforo, Stop, Ceda, Rotonda }

/// <summary>Cruce o extremo de calle.</summary>
public sealed class Nodo
{
    public int Id { get; }
    public Vector2 Pos { get; }

    /// <summary>Nombre estable para los ficheros de datos: X-fila-columna en la cuadrícula, CX-n en la circunvalación.</summary>
    public string Nombre { get; set; } = "";

    /// <summary>
    /// Semáforo: todas las aproximaciones obedecen la luz. Stop y ceda: solo las aproximaciones que no están
    /// en <see cref="Prioritarios"/> tienen la señal.
    /// </summary>
    public TipoControl Control { get; set; } = TipoControl.Ninguno;
    public Semaforo? Semaforo { get; set; }

    /// <summary>Guardia de tráfico que controla el cruce ahora (null casi siempre). Sus órdenes sustituyen a las señales.</summary>
    public Guardia? Guardia { get; set; }

    /// <summary>Trayectos posibles dentro del cruce (los rellena Grafo al preparar el nodo).</summary>
    public List<Conexion> Conexiones { get; } = new();

    /// <summary>
    /// Giros que no se pueden hacer en este cruce: pares (calle de la que se viene, calle a la que se quiere ir), por el número de cada una.
    /// Por ejemplo, prohibido girar a la izquierda desde una arteria o seguir recto desde una calle local.
    /// </summary>
    public HashSet<(int desde, int hasta)> GirosProhibidos { get; } = new();

    public bool GiroProhibido(Tramo desde, Tramo hasta) => GirosProhibidos.Count > 0 && GirosProhibidos.Contains((desde.Id, hasta.Id));

    /// <summary>Pasos de peatones junto al cruce, por calle (los crea el servicio de peatones).</summary>
    public Dictionary<Tramo, PasoPeatones> Pasos { get; } = new();

    public PasoPeatones? PasoEn(Tramo t) => Pasos.Count > 0 && Pasos.TryGetValue(t, out var p) ? p : null;

    /// <summary>Calles con prioridad en un cruce con stop o ceda.</summary>
    public HashSet<Tramo> Prioritarios { get; } = new();

    /// <summary>Calles que llegan a este nodo.</summary>
    public List<Tramo> Tramos { get; } = new();

    /// <summary>Carriles que terminan / empiezan en este nodo.</summary>
    public List<Carril> Entrantes { get; } = new();
    public List<Carril> Salientes { get; } = new();

    /// <summary>Distancia al centro a la que se detienen los carriles (mitad de la calle más ancha + margen).</summary>
    public float Radio { get; set; }

    /// <summary>True cuando ya se han calculado los conflictos entre las conexiones de este cruce.</summary>
    public bool ConflictosListos { get; set; }

    public CategoriaVia CategoriaMaxima => Tramos.Count == 0 ? CategoriaVia.Local : Tramos.Max(t => t.Categoria);

    public Nodo(int id, Vector2 pos)
    {
        Id = id;
        Pos = pos;
    }

    public bool EsPrioritario(Tramo t) => Control is TipoControl.Ninguno or TipoControl.Semaforo || Prioritarios.Contains(t);

    /// <summary>Grupo de semáforo de una aproximación: 0 si llega en horizontal, 1 si llega en vertical.</summary>
    public static int Grupo(Carril entrante)
    {
        var d = entrante.Eje.Direccion(entrante.Longitud);
        return Mathf.Abs(d.X) >= Mathf.Abs(d.Y) ? 0 : 1;
    }

    public EstadoSemaforo EstadoPara(Carril entrante, double tiempo) =>
        Semaforo?.EstadoCarril(entrante, tiempo) ?? EstadoSemaforo.Verde;

    /// <summary>
    /// Rango de prioridad de un movimiento: mayor rango pasa primero. Las calles prioritarias y los semáforos
    /// valen 4 y las que tienen señal valen 0; girar a la izquierda resta uno porque cede ante el tráfico de frente.
    /// </summary>
    public int Rango(Conexion m) =>
        (EsPrioritario(m.Origen.Tramo) ? 4 : 0) + (m.Giro == TipoGiro.Izquierda ? 0 : 1);
}

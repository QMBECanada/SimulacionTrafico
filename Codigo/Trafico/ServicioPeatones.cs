using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SimulacionTrafico.Nucleo;
using SimulacionTrafico.Datos;
using SimulacionTrafico.Mapa;

namespace SimulacionTrafico.Trafico;

/// <summary>
/// Peatones: pasos de peatones en todas las calles que llegan a un cruce (salvo la autovía, sus ramales, los túneles y las rotondas),
/// personas que llegan a ellos al azar y cruzan. Llegan más en el centro que en las afueras y según la hora del día (la misma forma
/// que la demanda de coches). Con semáforo cruzan con su verde; sin semáforo tienen preferencia: esperan solo si viene un coche que
/// ya no puede frenar, y los coches que pueden frenar les ceden el paso.
/// </summary>
public sealed class ServicioPeatones
{
    /// <summary>Si es false no hay peatones (<c>--sinpeatones</c>).</summary>
    public bool Activo { get; }

    /// <summary>Peatones por hora que llegan a un paso en hora punta: en el centro de la ciudad y en las afueras.</summary>
    public const float TasaCentro = 120f, TasaAfueras = 10f;

    public List<PasoPeatones> Pasos { get; } = new();
    public int Cruces { get; private set; }

    private readonly PriorityQueue<PasoPeatones, double> _llegadas = new();
    private readonly HashSet<PasoPeatones> _activos = new();
    private readonly Aleatorio _azar;
    private int _siguienteId;
    private RegistroTsv? _registro;
    private Estudio? _estudio;

    public IEnumerable<PasoPeatones> Activos => _activos;

    public ServicioPeatones(Grafo grafo, int semilla, bool activo)
    {
        Activo = activo;
        _azar = new Aleatorio((ulong)semilla * 6151UL + 29UL);
        if (!Activo) return;
        var centro = grafo.Limites.GetCenter();
        var mitad = grafo.Limites.Size / 2f;
        foreach (var n in grafo.Nodos)
        {
            foreach (var t in n.Tramos)
            {
                if (!Grafo.TienePaso(n, t)) continue;
                var paso = new PasoPeatones(t, n);
                float u = Mathf.Clamp(Mathf.Max(Mathf.Abs(n.Pos.X - centro.X) / mitad.X, Mathf.Abs(n.Pos.Y - centro.Y) / mitad.Y), 0f, 1f);
                paso.TasaPunta = Mathf.Lerp(TasaCentro, TasaAfueras, u) / 3600f;
                n.Pasos[t] = paso;
                Pasos.Add(paso);
                _llegadas.Enqueue(paso, Exponencial(paso.TasaPunta));
            }
        }
    }

    private double Exponencial(float tasa) => -Math.Log(1.0 - _azar.Randf() * 0.999999) / Math.Max(tasa, 1e-6f);

    public void AbrirEstudio(Estudio estudio)
    {
        _estudio = estudio;
        _registro = estudio.Abrir("peatones.tsv", "Paso", "Cruce", "Calle", "ConSemaforo", "Hora", "T_llegada", "T_inicio", "T_fin", "EsperaS", "LargoM");
    }

    /// <summary>Deja de escribir (el fichero lo cierra el estudio, que cierra todos los suyos).</summary>
    public void CerrarEstudio()
    {
        _registro = null;
        _estudio = null;
    }

    /// <param name="factorHora">De 0 a 1: cuánta gente hay en la calle a esa hora respecto a la hora punta.</param>
    public void Actualizar(double t, float dt, Func<double, float> factorHora)
    {
        if (!Activo) return;
        // Llegadas: proceso de Poisson con la tasa de hora punta, aceptando cada llegada con la proporción de la hora (aclarado).
        while (_llegadas.TryPeek(out var paso, out double hora) && hora <= t)
        {
            _llegadas.Dequeue();
            if (_azar.Randf() < factorHora(hora))
            {
                paso.Esperando.Add(new Peaton
                {
                    Id = _siguienteId++, Llegada = hora, DesdeA = _azar.Randf() < 0.5f,
                    Velocidad = Mathf.Clamp(_azar.Randfn(1.35f, 0.2f), 0.8f, 1.8f),
                });
                _activos.Add(paso);
            }
            _llegadas.Enqueue(paso, hora + Exponencial(paso.TasaPunta));
        }

        List<PasoPeatones>? vacios = null;
        foreach (var paso in _activos)
        {
            if (paso.Esperando.Count > 0 && PuedenEmpezar(paso, t))
            {
                foreach (var p in paso.Esperando) p.Inicio = t;
                paso.Cruzando.AddRange(paso.Esperando);
                paso.Esperando.Clear();
            }
            for (int i = paso.Cruzando.Count - 1; i >= 0; i--)
            {
                var p = paso.Cruzando[i];
                p.Recorrido += p.Velocidad * dt;
                if (p.Recorrido < paso.Largo) continue;
                paso.Cruzando.RemoveAt(i);
                Cruces++;
                if (_registro != null && _estudio != null)
                    _registro.Fila(paso.Nombre, Nombres.Cruce(paso.Nodo), Nombres.Calle(paso.Tramo), paso.Nodo.Control == TipoControl.Semaforo ? 1 : 0,
                        _estudio.Hora(p.Llegada), Math.Round(p.Llegada, 1), Math.Round(p.Inicio!.Value, 1), Math.Round(t, 1),
                        Math.Round(p.Inicio.Value - p.Llegada, 1), Math.Round(paso.Largo, 1));
            }
            if (paso.Esperando.Count == 0 && paso.Cruzando.Count == 0) (vacios ??= new()).Add(paso);
        }
        if (vacios != null) foreach (var p in vacios) _activos.Remove(p);
    }

    /// <summary>Los que esperan pueden empezar a cruzar: con semáforo, si tienen verde; si no, si no viene un coche que ya no puede frenar.</summary>
    private static bool PuedenEmpezar(PasoPeatones paso, double t)
    {
        var nodo = paso.Nodo;
        if (nodo.Guardia == null && nodo.Control == TipoControl.Semaforo && nodo.Semaforo != null)
            return nodo.Semaforo.Estado(paso.GrupoPeatones, t) == EstadoSemaforo.Verde && !CocheEncima(paso);
        return !CocheCerca(paso);
    }

    /// <summary>Algún coche está pasando por la franja (al salir del cruce por esta calle o al entrar en él).</summary>
    private static bool CocheEncima(PasoPeatones paso)
    {
        foreach (var l in paso.Tramo.Carriles)
        {
            if (l.Origen == paso.Nodo)
            {
                foreach (var c in l.Coches) if (c.S - c.Longitud < 2f) return true;   // acaba de salir del cruce
            }
            else if (l.Coches.Count > 0)
            {
                var c = l.Coches[^1];
                if (l.Longitud - c.S < 1f && c.V > 0.5f) return true;   // entrando en el cruce
            }
        }
        foreach (var cx in paso.Nodo.Conexiones)
            if ((cx.Destino.Tramo == paso.Tramo || cx.Origen.Tramo == paso.Tramo) && cx.Coches.Count > 0) return true;
        return false;
    }

    /// <summary>Viene un coche que no podría parar con una frenada cómoda antes del paso (o ya está encima).</summary>
    private static bool CocheCerca(PasoPeatones paso)
    {
        if (CocheEncima(paso)) return true;
        foreach (var l in paso.Tramo.Carriles)
        {
            if (l.Destino != paso.Nodo) continue;
            for (int i = l.Coches.Count - 1; i >= 0; i--)
            {
                var c = l.Coches[i];
                float d = l.Longitud - c.S - c.Longitud / 2f;
                if (d > 60f) break;
                if (c.V > 1f && d < c.V * c.V / (2f * c.Conductor.FrenadaComoda) + 2f) return true;
            }
        }
        return false;
    }
}

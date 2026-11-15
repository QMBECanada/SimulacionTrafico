using System.Collections.Generic;
using System.Linq;
using Godot;
using SimulacionTrafico.Mapa;
using SimulacionTrafico.Nucleo;
using SimulacionTrafico.Trafico;

namespace SimulacionTrafico.Vehiculos;

// Coche: velocidad que quiere llevar y coche que tiene delante.
public sealed partial class Coche
{
    /// <summary>True si la vía es el anillo de una rotonda o una entrada o salida suya.</summary>
    private static bool EsDeRotonda(ViaCirculacion via) =>
        via is Carril l ? l.Tramo.EsRotonda : via is Conexion c && (c.Origen.Tramo.EsRotonda || c.Destino.Tramo.EsRotonda);

    /// <summary>Diagnóstico: en qué está el coche ahora (crucero, acelerando, siguiendo, cruce, curva, evita un choque...).</summary>
    public string EstadoMarcha()
    {
        if (V < 0.5f) return "parado";
        float vVia = Conductor.VelocidadDeseada(Via.LimiteMs);
        string sitio = Via is Conexion ? "en cruce" : EsCalleDestino((Carril)Via) && Plan.Count == 1 ? "calle destino" : "calle";
        if (V >= 0.9f * vVia) return sitio + ": crucero";
        if (_visto != null && Bloqueador == _visto) return sitio + ": evita choque";
        if (Motivo != null) return sitio + ": cruce (" + Motivo + ")";
        if (BloqueadorEsLider && Hueco < 60f) return sitio + ": siguiendo";
        if (_anticipando) return sitio + (A > 0.2f ? ": acelerando (limitado por curva)" : ": frena por curva/giro");
        if (A > 0.2f) return sitio + ": acelerando";
        return sitio + ": otro (lento)";
    }

    /// <summary>
    /// Velocidad que busca ahora mismo (la primera cifra de «Deseada: 22 / 51,5»): la deseada en la vía, rebajada por la curva o el giro
    /// que viene, por el coche de delante si lo tiene cerca, y 0 si tiene que parar (semáforo, ceda, stop, evitar un choque...).
    /// </summary>
    public float ObjetivoAhora =>
        Motivo != null ? 0f
        : BloqueadorEsLider && Bloqueador != null && Hueco < V * 3f + 10f ? Mathf.Min(VelocidadDeseadaActual, Bloqueador.V)
        : VelocidadDeseadaActual;

    private bool _anticipando;   // la velocidad objetivo es menor que la de su vía por una curva o una vía más lenta que viene

    /// <summary>Velocidad que quiere llevar ahora: la de la vía, reducida por las curvas y límites que vienen.</summary>
    private float VelocidadObjetivo()
    {
        float v0 = Conductor.VelocidadDeseada(Via.LimiteMs);
        float vVia = v0;
        v0 = Mathf.Min(v0, Conductor.VelocidadEnCurva(Via.RadioMinimo, EsDeRotonda(Via)));

        float dist = Via.Longitud - S;
        float b = _contexto.Config.FactorPlanFrenada * Conductor.FrenadaComoda;   // planifica con margen: así llega a la curva sin pasar de la frenada normal
        for (int i = 1; i < Plan.Count && dist < HorizonteVision; i++)
        {
            var via = Plan[i];
            float limite = Mathf.Min(Conductor.VelocidadDeseada(via.LimiteMs), Conductor.VelocidadEnCurva(via.RadioMinimo, EsDeRotonda(via)));
            if (!float.IsInfinity(limite))
                v0 = Mathf.Min(v0, Mathf.Sqrt(limite * limite + 2f * b * dist)); // llegar a esa velocidad al empezar la vía
            dist += via.Longitud;
        }
        _anticipando = v0 < vVia - 0.01f;
        return v0;
    }

    private (float hueco, float vLider, Coche? lider) BuscarLider()
    {
        var mejor = BuscarLiderRuta();
        var hermano = BuscarLiderHermano();
        if (hermano.hueco < mejor.hueco) mejor = hermano;
        var transito = BuscarLiderEnTransito();
        return transito.hueco < mejor.hueco ? transito : mejor;
    }

    /// <summary>
    /// Coche que acaba de cambiar de carril hacia el de al lado pero que, mientras termina el desplazamiento, todavía está pasando por
    /// mi carril: para el coche que va detrás es un obstáculo igual que si fuera de su carril.
    /// </summary>
    private (float hueco, float vLider, Coche? lider) BuscarLiderEnTransito()
    {
        if (Via is not Carril mio) return (float.PositiveInfinity, 0f, null);
        float mejor = float.PositiveInfinity, vLider = 0f;
        Coche? lider = null;
        for (int lado = 0; lado < 2; lado++)
        {
            var vecino = lado == 0 ? mio.Izquierdo : mio.Derecho;
            if (vecino == null) continue;
            foreach (var otro in vecino.Coches)
            {
                if (otro.S <= S || !otro.OcupaCarril(mio)) continue;
                if (otro.S - S > 30f) break;
                float h = otro.S - S - (otro.Longitud + Longitud) / 2f;
                if (h < mejor) { mejor = h; vLider = otro.V; lider = otro; }
            }
        }
        return (mejor, vLider, lider);
    }

    /// <summary>
    /// Coche que acaba de tomar otra conexión de la misma entrada del cruce (otro giro desde el mismo carril): sigue delante
    /// de mí aunque ya no esté en mi vía, y si no lo veo lo alcanzo cuando frena en la curva.
    /// </summary>
    private (float hueco, float vLider, Coche? lider) BuscarLiderHermano()
    {
        Conexion m;
        float desfase;
        if (Via is Conexion actual) { m = actual; desfase = -S; }
        else if (Plan.Count > 1 && Plan[1] is Conexion siguiente && Via.Longitud - S < 30f) { m = siguiente; desfase = Via.Longitud - S; }
        else return (float.PositiveInfinity, 0f, null);

        float mejor = float.PositiveInfinity, vLider = 0f;
        Coche? lider = null;
        foreach (var hermana in m.Origen.Destino.Conexiones)
        {
            if (hermana == m || hermana.Origen != m.Origen) continue;
            foreach (var otro in hermana.Coches)
            {
                if (otro.S > 20f) break;                    // ya se ha separado mucho de mi trayectoria
                if (Via is Conexion && otro.S <= S) continue; // está detrás de mí
                float h = desfase + otro.S - (otro.Longitud + Longitud) / 2f;
                if (h < mejor)
                {
                    mejor = h;
                    vLider = otro.V;
                    lider = otro;
                }
            }
        }
        return (mejor, vLider, lider);
    }

    private (float hueco, float vLider, Coche? lider) BuscarLiderRuta()
    {
        // El de delante en mi vía es el más cercano que va de verdad por delante (por su posición, no por el orden de la lista: si dos
        // coches quedan casi superpuestos, el orden de la lista puede no coincidir con el de la calle y se esperarían en círculo).
        // Casi siempre la lista está en orden y basta con mirar el siguiente; solo si no va por delante se busca en toda la lista.
        var lista = Via.Coches;
        int yo = IndiceEn(lista);
        Coche? cercano = null;
        if (yo >= 0 && yo + 1 < lista.Count && lista[yo + 1].S > S && (yo + 2 >= lista.Count || lista[yo + 2].S >= lista[yo + 1].S))
            cercano = lista[yo + 1];
        else
            foreach (var otro in lista)
                if (otro != this && (otro.S > S || (otro.S == S && otro.Id > Id)) && (cercano == null || otro.S < cercano.S)) cercano = otro;
        if (cercano != null) return (cercano.S - S - (cercano.Longitud + Longitud) / 2f, cercano.V, cercano);

        float dist = Via.Longitud - S;
        for (int i = 1; i < Plan.Count && dist < HorizonteVision; i++)
        {
            var via = Plan[i];
            if (via.Coches.Count > 0)
            {
                var l = via.Coches[0];
                return (dist + l.S - (l.Longitud + Longitud) / 2f, l.V, l);
            }
            dist += via.Longitud;
        }
        return (float.PositiveInfinity, 0f, null);
    }
}

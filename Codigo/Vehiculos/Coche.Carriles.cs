using System.Collections.Generic;
using System.Linq;
using Godot;
using SimulacionTrafico.Mapa;
using SimulacionTrafico.Nucleo;
using SimulacionTrafico.Trafico;

namespace SimulacionTrafico.Vehiculos;

// Coche: cambio de carril (MOBIL).
public sealed partial class Coche
{
    /// <summary>
    /// Cada medio segundo, más o menos, el coche piensa si le conviene cambiar al carril de al lado.
    /// Decide con el criterio MOBIL: lo que gana él más la parte que le importa de lo que ganan o pierden los demás
    /// (según su cortesía), siempre que no obligue al coche de atrás a frenar más de lo que su carácter acepta.
    /// Además tiene que estar en el carril que le permite hacer su giro (izquierda: interior; derecha: exterior).
    /// </summary>
    private void EvaluarCambioCarril(float dt)
    {
        _enfriamientoCambio -= dt;
        _proximaEvaluacion -= dt;
        if (_proximaEvaluacion > 0f || Via is not Carril carril) return;
        _proximaEvaluacion = 0.4f + _azar.Randf() * 0.3f;

        float dLinea = carril.Longitud - S;
        bool obligado = _indiceDeseado.HasValue && carril.Indice != _indiceDeseado.Value;

        // No llegó a tiempo a su carril: seguirá por otra calle que sí pueda tomar desde donde está.
        if (obligado && dLinea < 25f)
        {
            var otro = ElegirTramoSiguiente(carril, soloPermitidos: true);
            if (otro != null)
            {
                if (Destino != null) Reenrutar(carril, otro); // la ruta ya no vale: se calcula desde la calle que va a tomar
                FijarPlan(carril, otro);
            }
            return;
        }
        if (dLinea < 12f) return;
        if (!obligado && (_enfriamientoCambio > 0f || V < 1f)) return;

        Carril? mejor = null;
        float mejorIncentivo = float.NegativeInfinity;
        for (int lado = 0; lado < 2; lado++)
        {
            var candidato = lado == 0 ? carril.Izquierdo : carril.Derecho;
            if (candidato == null) continue;
            float? incentivo = Incentivo(carril, candidato, dLinea, obligado);
            if (incentivo is { } v && v > mejorIncentivo)
            {
                mejor = candidato;
                mejorIncentivo = v;
            }
        }

        float umbral = obligado ? 0.05f : Conductor.UmbralCambioCarril;
        if (mejor != null && mejorIncentivo > umbral) CambiarA(carril, mejor, obligado);
    }

    /// <summary>Incentivo de pasar al carril contiguo (null si no es seguro hacerlo).</summary>
    private float? Incentivo(Carril actual, Carril destino, float dLinea, bool obligado)
    {
        if (destino.EstaCortado(_tiempoActual)) return null;   // no se cambia a un carril cortado por un accidente
        float sNuevo = S * destino.Longitud / actual.Longitud;
        var (liderN, seguidorN) = Vecinos(destino, sNuevo);

        float huecoDelante = liderN == null ? float.PositiveInfinity : liderN.S - sNuevo - (liderN.Longitud + Longitud) / 2f;
        float huecoAtras = seguidorN == null ? float.PositiveInfinity : sNuevo - seguidorN.S - (seguidorN.Longitud + Longitud) / 2f;
        if (huecoDelante < Mathf.Max(1.5f, DistanciaMinimaActual * 0.8f) || huecoAtras < 1.5f) return null;

        // Cómo voy yo ahora y cómo iría en el otro carril.
        float v0 = Mathf.Max(VelocidadDeseadaActual, 3f);
        var (huecoActual, vLiderActual, _) = BuscarLider();
        float aAhora = ModeloIDM.Aceleracion(Conductor, V, v0, huecoActual, vLiderActual, DistanciaMinimaActual);
        float aNuevo = ModeloIDM.Aceleracion(Conductor, V, v0, huecoDelante, liderN?.V ?? 0f, DistanciaMinimaActual);

        float otros = 0f;

        // El coche que quedaría detrás de mí en el carril destino: tendría que frenar.
        if (seguidorN != null)
        {
            float huecoSegAntes = liderN == null ? float.PositiveInfinity
                : liderN.S - seguidorN.S - (liderN.Longitud + seguidorN.Longitud) / 2f;
            float aAntes = AceleracionDe(seguidorN, huecoSegAntes, liderN?.V ?? 0f);
            float aDespues = AceleracionDe(seguidorN, huecoAtras, V);
            float margen = Conductor.FrenadaSeguridadCambio + (obligado && dLinea < 60f ? 1.5f : 0f);
            if (aDespues < -margen) return null;
            otros += aDespues - aAntes;
        }

        // El coche que dejo detrás en mi carril: mejora, porque su líder pasa a ser el mío.
        var lista = Via.Coches;
        int yo = IndiceEn(lista);
        if (yo > 0)
        {
            var seg = lista[yo - 1];
            var lid = yo + 1 < lista.Count ? lista[yo + 1] : null;
            float huecoAntes = S - seg.S - (Longitud + seg.Longitud) / 2f;
            float huecoDespues = lid == null ? float.PositiveInfinity : lid.S - seg.S - (lid.Longitud + seg.Longitud) / 2f;
            otros += AceleracionDe(seg, huecoDespues, lid?.V ?? 0f) - AceleracionDe(seg, huecoAntes, V);
        }

        float incentivo = (aNuevo - aAhora) + Conductor.Cortesia * otros;

        // Preferencia por el carril de su giro, cada vez más fuerte a medida que se acerca al cruce.
        if (_indiceDeseado is { } deseado)
        {
            int antes = Mathf.Abs(actual.Indice - deseado), despues = Mathf.Abs(destino.Indice - deseado);
            float urgencia = Mathf.Clamp(1f - (dLinea - 25f) / (DistanciaPreparacion - 25f), 0f, 1f);
            incentivo += (antes - despues) * (0.3f + 2.5f * urgencia);
        }

        if (destino == actual.Derecho) incentivo += 0.12f; // si nada lo impide, circula por la derecha
        return incentivo;
    }

    private static float AceleracionDe(Coche c, float hueco, float vLider) =>
        ModeloIDM.Aceleracion(c.Conductor, c.V, Mathf.Max(c.VelocidadDeseadaActual, 3f), hueco, vLider, c.DistanciaMinimaActual);

    /// <summary>Coche inmediatamente delante y detrás de una posición s de un carril.</summary>
    private static (Coche? lider, Coche? seguidor) Vecinos(Carril carril, float s)
    {
        var lista = carril.Coches;
        int k = lista.Count;
        while (k > 0 && lista[k - 1].S > s) k--;
        return (k < lista.Count ? lista[k] : null, k > 0 ? lista[k - 1] : null);
    }

    private void CambiarA(Carril actual, Carril destino, bool obligado)
    {
        bool haciaDerecha = destino == actual.Derecho;
        QuitarDe(actual.Coches);
        S = S * destino.Longitud / actual.Longitud;
        Via = destino;
        InsertarOrdenado(destino);
        Plan[0] = destino;
        if (_tramoSiguiente != null) FijarPlan(destino, _tramoSiguiente);

        // El coche cambia de carril al instante; la imagen se desliza hasta el centro del nuevo.
        _desfaseLateral = (haciaDerecha ? -1f : 1f) * Escala.AnchoCarril;
        _enfriamientoCambio = 3f + 3f * _azar.Randf();
        CambiosCarril++;
        if (obligado) CambiosObligados++;
    }
}

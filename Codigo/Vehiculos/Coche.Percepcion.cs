using System.Collections.Generic;
using System.Linq;
using Godot;
using SimulacionTrafico.Mapa;
using SimulacionTrafico.Nucleo;
using SimulacionTrafico.Trafico;

namespace SimulacionTrafico.Vehiculos;

// Coche: percepción (dónde estará cada uno en los próximos segundos y riesgo de chocar).
public sealed partial class Coche
{
    private Coche? _visto;              // el coche que vio que iba a tocar (si le tocaba a él evitarlo)
    private float _huecoVisto = float.PositiveInfinity;
    private float _recorridoDesdeVisto;

    public const int PasosFuturo = 5;
    public const float DtFuturo = 0.4f;
    private const float DistanciaChoque = 2.6f;   // dos coches cuyos centros quedarían más cerca que esto se tocan

    /// <summary>Dónde estará en 0; 0,4; 0,8 ... 2 s si mantiene velocidad y trayectoria (incluido lo que le queda de un cambio de carril).</summary>
    public Vector2[] Futuro { get; } = new Vector2[PasosFuturo + 1];

    public void CalcularFuturo()
    {
        var lateral = Pos - Via.Eje.Posicion(S);   // lo que aún le falta para centrarse tras un cambio de carril
        Futuro[0] = Pos;
        for (int k = 1; k <= PasosFuturo; k++)
        {
            float t = k * DtFuturo;
            Futuro[k] = PosicionFutura(t) + lateral * Mathf.Max(0f, 1f - t / DuracionCambio);
        }
    }

    /// <summary>
    /// Nivel (Z) por el que circula: -1 bajo tierra (en un túnel o en las bocas, dentro del cruce que lo une con la calle), 0 en la
    /// superficie y +1 en un puente. Solo chocan, se perciben y se ven en pantalla los vehículos del mismo nivel.
    /// </summary>
    public int Z => NivelDe(Via);

    public static int NivelDe(ViaCirculacion? via) => via switch
    {
        Carril l => Nivel(l.Tramo),
        Conexion c => Nivel(c.Origen.Tramo) < 0 || Nivel(c.Destino.Tramo) < 0 ? -1
                    : Nivel(c.Origen.Tramo) > 0 && Nivel(c.Destino.Tramo) > 0 ? 1 : 0,
        _ => 0,
    };

    private static int Nivel(Tramo t) => t.EsTunel ? -1 : t.EsPuente ? 1 : 0;

    /// <summary>
    /// El conductor mira a su alrededor (delante y a los lados, hasta donde llegará en dos segundos más un margen) y comprueba si,
    /// siguiendo todos como van, va a tocar a otro coche. Si es así y le toca a él evitarlo, devuelve ese coche y a qué distancia debe
    /// pararse. Le toca evitarlo si el otro va delante en su misma dirección (alcance), si está parado (un obstáculo, un accidente) o, en
    /// un cruce de trayectorias, si el otro viene por su derecha (la norma general de preferencia). Las preferencias dentro de un cruce
    /// las resuelven las normas del cruce y <see cref="RiesgoPrevisto"/>; esto es lo que hace cualquier conductor atento en todo momento.
    /// </summary>
    [System.ThreadStatic] private static List<Coche>? _vecinosHilo;
    private static List<Coche> _vecinos => _vecinosHilo ??= new List<Coche>();

    private (Coche? otro, float hueco) RiesgoPercibido()
    {
        var entorno = _contexto.Entorno;
        if (entorno == null || V < 0.5f) return (null, float.PositiveInfinity);
        var dir = Vector2.FromAngle(Rumbo);
        var derecha = new Vector2(-dir.Y, dir.X);
        float radio = 8f + V * PasosFuturo * DtFuturo + 5f;
        int miNivel = Z;

        Coche? peor = null;
        float mejorHueco = float.PositiveInfinity;
        entorno.Cerca(Pos, radio, _vecinos);
        foreach (var otro in _vecinos)
        {
            if (otro == this) continue;
            var d = otro.Pos - Pos;
            if (d.Dot(dir) < -1f) continue;                                         // está detrás: no lo ve ni le toca a él
            if (otro.Via == Via && otro.S > S) continue;                            // su líder en el carril: ya lo sigue
            if (BloqueadorEsLider && otro == Bloqueador) continue;
            if (otro.Z != miNivel) continue;   // a otro nivel (túnel, puente): ni lo ve ni puede chocar con él
            // Dentro del mismo cruce las preferencias son las del cruce.
            if (Via is Conexion c1 && otro.Via is Conexion c2 && c1.Origen.Destino == c2.Origen.Destino) continue;

            int choque = -1;
            for (int k = 1; k <= PasosFuturo; k++)
                if ((Futuro[k] - otro.Futuro[k]).LengthSquared() < DistanciaChoque * DistanciaChoque) { choque = k; break; }
            if (choque < 0) continue;

            var dirOtro = Vector2.FromAngle(otro.Rumbo);
            bool cedo;
            if (otro.V < 0.5f) cedo = true;                                         // obstáculo parado: lo esquiva o para
            else if (dir.Dot(dirOtro) > 0.7f) cedo = d.Dot(dir) > 0f;               // misma dirección: cede el de detrás
            else cedo = d.Dot(derecha) > 0f || (Mathf.Abs(d.Dot(derecha)) < 0.5f && MeGanaElSorteo(otro));   // cruce: preferencia a la derecha; sin derecha clara, el sorteo
            if (!cedo) continue;

            float hueco = Mathf.Max(0.3f, (Futuro[choque] - Pos).Length() - 4f);  // pararse antes del punto del golpe
            if (hueco < mejorHueco)
            {
                mejorHueco = hueco;
                peor = otro;
            }
        }
        return (peor, mejorHueco);
    }

    /// <summary>Dónde estará dentro de <paramref name="t"/> segundos si mantiene la velocidad, siguiendo las vías de su plan.</summary>
    private Vector2 PosicionFutura(float t)
    {
        float s = S + V * t;
        var via = Via;
        for (int i = 1; i < Plan.Count && s > via.Longitud; i++)
        {
            s -= via.Longitud;
            via = Plan[i];
        }
        return via.Eje.Posicion(Mathf.Min(s, via.Longitud));
    }

    /// <summary>
    /// Coche con el que va a chocar en el próximo segundo y medio si ambos siguen como van, entre los que tienen preferencia sobre mí
    /// en este cruce (más prioridad, o la misma y gana el sorteo). Solo mira cuando estoy en el cruce o a punto de entrar.
    /// Es una red de seguridad: el resto del tiempo basta con ceder el paso.
    /// </summary>
    private Coche? RiesgoPrevisto()
    {
        Conexion? m = Via as Conexion;
        if (m == null && Via is Carril carril && Plan.Count > 1 && carril.Longitud - S < 12f) m = Plan[1] as Conexion;
        if (m == null || V < 0.3f) return null;

        var nodo = m.Origen.Destino;
        int miRango = nodo.Rango(m);
        var conflictos = _grafo.ObtenerConflictos(m);
        for (int k = 0; k < conflictos.Count; k++)   // con índice: foreach sobre la interfaz crearía un enumerador en cada paso
        {
            var otra = conflictos[k].Otra;
            int rangoOtra = nodo.Rango(otra);
            if (rangoOtra < miRango) continue;

            foreach (var y in otra.Coches)
                if (y != this && (rangoOtra > miRango || MeGanaElSorteo(y)) && Chocarian(y)) return y;
            foreach (var y in otra.Origen.Coches)
                if (y != this && otra.Origen.Longitud - y.S < 12f && y.Plan.Count > 1 && y.Plan[1] == otra
                    && (rangoOtra > miRango || MeGanaElSorteo(y)) && Chocarian(y)) return y;
        }
        return null;
    }

    private bool Chocarian(Coche y)
    {
        for (float t = 0.3f; t <= 1.5f; t += 0.3f)
            if (PosicionFutura(t).DistanceSquaredTo(y.PosicionFutura(t)) < 2.6f * 2.6f) return true;
        return false;
    }
}

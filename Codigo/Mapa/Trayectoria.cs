using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace SimulacionTrafico.Mapa;

/// <summary>
/// Línea quebrada con longitud acumulada. Sirve como eje de calles y de carriles:
/// permite preguntar posición y dirección a una distancia s del inicio.
/// </summary>
public sealed class Trayectoria
{
    public Vector2[] Puntos { get; }
    private readonly float[] _acum;

    public float Longitud => _acum[^1];

    public Trayectoria(IEnumerable<Vector2> puntos)
    {
        Puntos = puntos.ToArray();
        if (Puntos.Length < 2) throw new ArgumentException("Una trayectoria necesita al menos 2 puntos.");
        _acum = new float[Puntos.Length];
        for (int i = 1; i < Puntos.Length; i++)
            _acum[i] = _acum[i - 1] + Puntos[i - 1].DistanceTo(Puntos[i]);
        RadioMinimo = CalcularRadioMinimo();
    }

    private int Segmento(float s)
    {
        if (s <= 0f) return 0;
        if (s >= Longitud) return Puntos.Length - 2;
        int lo = 0, hi = Puntos.Length - 2;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (_acum[mid] <= s) lo = mid; else hi = mid - 1;
        }
        return lo;
    }

    public Vector2 Posicion(float s)
    {
        int i = Segmento(s);
        float largo = _acum[i + 1] - _acum[i];
        float t = largo > 1e-6f ? Mathf.Clamp((s - _acum[i]) / largo, 0f, 1f) : 0f;
        return Puntos[i].Lerp(Puntos[i + 1], t);
    }

    public Vector2 Direccion(float s)
    {
        int i = Segmento(s);
        return (Puntos[i + 1] - Puntos[i]).Normalized();
    }

    /// <summary>Tramo de la trayectoria entre las distancias ini y fin.</summary>
    public Trayectoria Recortar(float ini, float fin)
    {
        ini = Mathf.Clamp(ini, 0f, Longitud);
        fin = Mathf.Clamp(fin, ini, Longitud);
        var pts = new List<Vector2> { Posicion(ini) };
        for (int i = 1; i < Puntos.Length - 1; i++)
            // Se descartan los puntos casi encima de los extremos: un tramo diminuto, al desplazarlo lateralmente en una curva cerrada,
            // puede invertirse y dar una dirección opuesta al principio del carril (los coches giraban sobre sí mismos).
            if (_acum[i] > ini + 0.4f && _acum[i] < fin - 0.4f) pts.Add(Puntos[i]);
        pts.Add(Posicion(fin));
        return new Trayectoria(pts);
    }

    /// <summary>Copia desplazada lateralmente (positivo = a la derecha del sentido de la marcha).</summary>
    public Trayectoria Desplazar(float offset)
    {
        var res = new Vector2[Puntos.Length];
        for (int i = 0; i < Puntos.Length; i++)
        {
            Vector2 n = Vector2.Zero;
            if (i > 0) n += Derecha(Puntos[i] - Puntos[i - 1]);
            if (i < Puntos.Length - 1) n += Derecha(Puntos[i + 1] - Puntos[i]);
            res[i] = Puntos[i] + n.Normalized() * offset;
        }
        return new Trayectoria(res);
    }

    public Trayectoria Invertida() => new(Puntos.Reverse());

    /// <summary>Distancia a lo largo de la trayectoria del punto más cercano a <paramref name="p"/>.</summary>
    public float ProyectarS(Vector2 p)
    {
        float mejorS = 0f, mejorDist = float.PositiveInfinity;
        for (int i = 0; i < Puntos.Length - 1; i++)
        {
            var a = Puntos[i];
            var ab = Puntos[i + 1] - a;
            float largo2 = ab.LengthSquared();
            float t = largo2 > 1e-9f ? Mathf.Clamp((p - a).Dot(ab) / largo2, 0f, 1f) : 0f;
            float d = (a + ab * t).DistanceSquaredTo(p);
            if (d < mejorDist)
            {
                mejorDist = d;
                mejorS = _acum[i] + t * Mathf.Sqrt(largo2);
            }
        }
        return mejorS;
    }

    /// <summary>Radio de curvatura más cerrado, en metros (infinito si es recto). Se calcula al construirla: los coches lo consultan
    /// a la vez desde varios hilos y una caché que se rellenara al leer sería una carrera.</summary>
    public float RadioMinimo { get; }

    private float CalcularRadioMinimo()
    {
        float radio = float.PositiveInfinity;
        for (int i = 1; i < Puntos.Length - 1; i++)
        {
            var ab = Puntos[i] - Puntos[i - 1];
            var bc = Puntos[i + 1] - Puntos[i];
            float cruz = Mathf.Abs(ab.Cross(bc));
            if (cruz < 1e-4f) continue;
            // Radio de la circunferencia que pasa por tres puntos: |ab||bc||ac| / (2|ab x bc|)
            float r = ab.Length() * bc.Length() * (Puntos[i + 1] - Puntos[i - 1]).Length() / (2f * cruz);
            radio = Mathf.Min(radio, r);
        }
        return radio > 2000f ? float.PositiveInfinity : radio;
    }

    /// <summary>Arco de circunferencia (ángulos en radianes, con la Y hacia abajo como en pantalla).</summary>
    public static Trayectoria Arco(Vector2 centro, float radio, float anguloInicial, float anguloFinal, int puntos = 120)
    {
        var pts = new Vector2[puntos + 1];
        for (int i = 0; i <= puntos; i++)
        {
            float a = Mathf.Lerp(anguloInicial, anguloFinal, i / (float)puntos);
            pts[i] = centro + radio * new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        }
        return new Trayectoria(pts);
    }

    /// <summary>
    /// Punto en que esta trayectoria y otra pasan más cerca, si es a menos de <paramref name="distancia"/> metros (sin llegar a cruzarse).
    /// Se recorren las dos cada metro.
    /// </summary>
    public bool Acercamiento(Trayectoria otra, float distancia, out float s1, out float s2)
    {
        s1 = s2 = 0f;
        float mejor = distancia * distancia;
        bool hay = false;
        for (float a = 0f; a <= Longitud; a += 1f)
        {
            var p = Posicion(a);
            for (float b = 0f; b <= otra.Longitud; b += 1f)
            {
                float d = p.DistanceSquaredTo(otra.Posicion(b));
                if (d < mejor)
                {
                    mejor = d;
                    s1 = a;
                    s2 = b;
                    hay = true;
                }
            }
        }
        return hay;
    }

    /// <summary>
    /// Primer punto (a lo largo de esta trayectoria) donde se cruza con otra.
    /// Devuelve la distancia recorrida en cada una hasta ese punto.
    /// </summary>
    public bool Interseccion(Trayectoria otra, out float s1, out float s2)
    {
        s1 = s2 = 0f;
        for (int i = 0; i < Puntos.Length - 1; i++)
        {
            var a0 = Puntos[i];
            var r = Puntos[i + 1] - a0;
            for (int j = 0; j < otra.Puntos.Length - 1; j++)
            {
                var b0 = otra.Puntos[j];
                var q = otra.Puntos[j + 1] - b0;
                float den = r.Cross(q);
                if (Mathf.Abs(den) < 1e-6f) continue;
                var d = b0 - a0;
                float t = d.Cross(q) / den;
                float u = d.Cross(r) / den;
                if (t < 0f || t > 1f || u < 0f || u > 1f) continue;
                s1 = _acum[i] + t * r.Length();
                s2 = otra._acum[j] + u * q.Length();
                return true;
            }
        }
        return false;
    }

    private static Vector2 Derecha(Vector2 v)
    {
        var d = v.Normalized();
        return new Vector2(-d.Y, d.X);
    }
}

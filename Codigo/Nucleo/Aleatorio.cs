using System;
using System.Numerics;

namespace SimulacionTrafico.Nucleo;

/// <summary>
/// Generador pseudoaleatorio propio (xoshiro256** sembrado con SplitMix64), en C# puro: no necesita el motor de Godot
/// y cada vehículo puede tener su propia secuencia con <see cref="Derivar"/>. API como la de RandomNumberGenerator de Godot.
/// </summary>
public sealed class Aleatorio
{
    private const ulong Dorado = 0x9E3779B97F4A7C15UL;

    private readonly ulong _semilla;
    private ulong _s0, _s1, _s2, _s3;

    public Aleatorio(ulong semilla)
    {
        _semilla = semilla;
        ulong x = semilla;
        _s0 = SplitMix64(ref x);
        _s1 = SplitMix64(ref x);
        _s2 = SplitMix64(ref x);
        _s3 = SplitMix64(ref x);
    }

    private static ulong SplitMix64(ref ulong x)
    {
        x += Dorado;
        return Mezclar(x);
    }

    private static ulong Mezclar(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Siguiente valor de 64 bits (xoshiro256**).</summary>
    public ulong Siguiente()
    {
        ulong resultado = BitOperations.RotateLeft(_s1 * 5, 7) * 9;
        ulong t = _s1 << 17;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = BitOperations.RotateLeft(_s3, 45);
        return resultado;
    }

    public uint Randi() => (uint)(Siguiente() >> 32);

    /// <summary>Real uniforme en [0, 1): 24 bits, los que caben en la mantisa de un float.</summary>
    public float Randf() => (Siguiente() >> 40) * (1f / (1 << 24));

    public float RandfRange(float desde, float hasta) => desde + (hasta - desde) * Randf();

    /// <summary>Entero uniforme entre los dos extremos, ambos incluidos.</summary>
    public int RandiRange(int desde, int hasta)
    {
        if (hasta < desde) (desde, hasta) = (hasta, desde);
        ulong amplitud = (ulong)((long)hasta - desde + 1);
        return (int)(desde + (long)Math.BigMul(Siguiente(), amplitud, out _));
    }

    /// <summary>Normal con la media y la desviación dadas (Box-Muller).</summary>
    public float Randfn(float media = 0f, float desviacion = 1f)
    {
        double u1 = ((Siguiente() >> 11) + 1) * (1.0 / (1UL << 53));
        double u2 = (Siguiente() >> 11) * (1.0 / (1UL << 53));
        return media + desviacion * (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
    }

    /// <summary>
    /// Secuencia independiente y determinista para la clave dada. Depende solo de la semilla de este generador y de la clave,
    /// no de cuántos números se hayan sacado ya, y no consume nada de este generador.
    /// </summary>
    public Aleatorio Derivar(ulong clave) => new(Mezclar(_semilla ^ Mezclar(clave * Dorado + 0x632BE59BD9B4E019UL)));
}

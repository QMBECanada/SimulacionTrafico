using System.Collections.Generic;
using System.Linq;
using Godot;
using SimulacionTrafico.Camara;

namespace SimulacionTrafico.Vehiculos;

/// <summary>
/// Dibuja los coches con MultiMesh (miles de instancias en pocas llamadas a la GPU): un MultiMesh por modelo
/// de coche cuando el zoom permite ver sprites, y un único MultiMesh de puntos de color cuando se ve desde lejos.
/// Solo se escriben las instancias visibles e interpola entre pasos de simulación para que el movimiento sea fluido.
/// Además resalta el coche seguido y su plan de ruta.
/// </summary>
public partial class VistaCoches : Node2D
{
    private const float ZoomSprites = 1.2f; // por debajo se dibujan como puntos
    private static readonly Aabb AabbMundo = new(new Vector3(-30000f, -30000f, -1f), new Vector3(60000f, 60000f, 2f));
    private const int FloatsTransform = 8;  // Transform2D: 2 filas de 4
    private const int FloatsConColor = 12;

    private sealed class GrupoModelo
    {
        public MultiMeshInstance2D Nodo = null!;
        public MultiMesh Mm = null!;
        public float[] Buffer = null!;
        public int Visibles;
    }

    private readonly Simulacion _sim;
    private readonly CamaraCiudad _camara;
    private readonly Dictionary<CatalogoCoches.Modelo, GrupoModelo> _grupos = new();
    private readonly MultiMeshInstance2D _nodoPuntos;
    private readonly MultiMesh _puntos;
    private readonly float[] _bufferPuntos;

    public Coche? Seleccionado { get; set; }
    public int Visibles { get; private set; }

    public VistaCoches(Simulacion sim, CamaraCiudad camara)
    {
        _sim = sim;
        _camara = camara;
        ZIndex = 10;
        ZAsRelative = false;

        foreach (var grupo in sim.Flota.GroupBy(c => c.Modelo))
        {
            var modelo = grupo.Key;
            int n = grupo.Count();
            var mm = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,
                Mesh = CrearCuadrado(new Vector2(modelo.AnchoM, modelo.LargoM)),
                InstanceCount = n,
                VisibleInstanceCount = 0,
                CustomAabb = AabbMundo,   // sin esto el dibujo de un grupo con pocos coches podía descartarse por un cálculo de tamaño desfasado
            };
            var nodo = new MultiMeshInstance2D
            {
                Multimesh = mm,
                Texture = modelo.Textura,
                TextureFilter = TextureFilterEnum.LinearWithMipmaps,
            };
            AddChild(nodo);
            _grupos[modelo] = new GrupoModelo { Nodo = nodo, Mm = mm, Buffer = new float[n * FloatsTransform] };
        }

        _puntos = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,
            UseColors = true,
            Mesh = CrearCuadrado(Vector2.One),
            InstanceCount = sim.Flota.Count,
            VisibleInstanceCount = 0,
            CustomAabb = AabbMundo,
        };
        _nodoPuntos = new MultiMeshInstance2D { Multimesh = _puntos };
        AddChild(_nodoPuntos);
        _bufferPuntos = new float[sim.Flota.Count * FloatsConColor];
    }

    /// <summary>Cuadrado centrado con UV en la orientación del canvas 2D (el QuadMesh de Godot sale volteado).</summary>
    private static ArrayMesh CrearCuadrado(Vector2 tam)
    {
        var h = tam / 2f;
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = new[] { new Vector2(-h.X, -h.Y), new Vector2(h.X, -h.Y), new Vector2(h.X, h.Y), new Vector2(-h.X, h.Y) };
        arrays[(int)Mesh.ArrayType.TexUV] = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        arrays[(int)Mesh.ArrayType.Index] = new[] { 0, 1, 2, 0, 2, 3 };
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    public override void _Process(double delta)
    {
        float zoom = _camara.PixelesPorMetro;
        var rect = _camara.RectVisible(12f);
        float alfa = Mathf.Clamp(_sim.Alfa, 0f, 1f);
        bool sprites = zoom >= ZoomSprites;

        foreach (var g in _grupos.Values) g.Visibles = 0;
        int puntos = 0;
        int visibles = 0;

        foreach (var c in _sim.Coches)
        {
            var pos = c.PosAnterior.Lerp(c.Pos, alfa);
            if (!rect.HasPoint(pos)) continue;
            if (c.Z < 0) continue;   // bajo tierra (túnel y sus bocas) no se ven
            visibles++;

            if (sprites)
            {
                var g = _grupos[c.Modelo];
                float rumbo = Mathf.LerpAngle(c.RumboAnterior, c.Rumbo, alfa) + Mathf.Pi / 2f; // el sprite mira hacia arriba
                float cos = Mathf.Cos(rumbo), sin = Mathf.Sin(rumbo);
                int i = g.Visibles++ * FloatsTransform;
                var b = g.Buffer;
                b[i] = cos; b[i + 1] = -sin; b[i + 2] = 0f; b[i + 3] = pos.X;
                b[i + 4] = sin; b[i + 5] = cos; b[i + 6] = 0f; b[i + 7] = pos.Y;
            }
            else
            {
                float lado = Mathf.Max(c.Longitud * 0.8f, 5f / zoom);
                var color = c.Modelo.ColorMedio;
                int i = puntos++ * FloatsConColor;
                var b = _bufferPuntos;
                b[i] = lado; b[i + 1] = 0f; b[i + 2] = 0f; b[i + 3] = pos.X;
                b[i + 4] = 0f; b[i + 5] = lado; b[i + 6] = 0f; b[i + 7] = pos.Y;
                b[i + 8] = color.R; b[i + 9] = color.G; b[i + 10] = color.B; b[i + 11] = 1f;
            }
        }
        Visibles = visibles;

        foreach (var g in _grupos.Values)
        {
            g.Nodo.Visible = sprites && g.Visibles > 0;
            if (!g.Nodo.Visible) continue;
            g.Mm.Buffer = g.Buffer;
            g.Mm.VisibleInstanceCount = g.Visibles;
        }

        _nodoPuntos.Visible = !sprites && puntos > 0;
        if (_nodoPuntos.Visible)
        {
            _puntos.Buffer = _bufferPuntos;
            _puntos.VisibleInstanceCount = puntos;
        }

        QueueRedraw(); // el resalte del coche seguido
    }

    public override void _Draw()
    {
        if (Seleccionado == null) return;
        float zoom = _camara.PixelesPorMetro;
        float alfa = Mathf.Clamp(_sim.Alfa, 0f, 1f);
        foreach (var via in Seleccionado.Plan)
            DrawPolyline(via.Eje.Puntos, new Color(0.15f, 0.45f, 0.95f, 0.6f), 0.6f);
        // Dentro de un túnel los coches no se ven; el seguido sí, como una silueta transparente, para no perderlo de vista.
        if (Seleccionado.Z < 0)
        {
            var m = Seleccionado.Modelo;
            float rumbo = Mathf.LerpAngle(Seleccionado.RumboAnterior, Seleccionado.Rumbo, alfa) + Mathf.Pi / 2f;
            DrawSetTransform(Seleccionado.PosAnterior.Lerp(Seleccionado.Pos, alfa), rumbo);
            DrawTextureRect(m.Textura, new Rect2(-m.AnchoM / 2f, -m.LargoM / 2f, m.AnchoM, m.LargoM), false, new Color(1f, 1f, 1f, 0.6f));
            DrawSetTransform(Vector2.Zero, 0f);
        }
        DrawArc(Seleccionado.PosAnterior.Lerp(Seleccionado.Pos, alfa), Mathf.Max(3.5f, 10f / zoom), 0f, Mathf.Tau, 32,
            new Color(0.9f, 0.1f, 0.1f, 0.85f), Mathf.Max(0.5f, 2f / zoom));

        // Su destino: un anillo verde en la puerta del lugar al que va.
        if (Seleccionado.Destino is { } destino)
            DrawArc(destino.Puerta, Mathf.Max(5f, 14f / zoom), 0f, Mathf.Tau, 32, new Color(0.1f, 0.6f, 0.2f, 0.9f), Mathf.Max(0.8f, 2.5f / zoom));
    }
}

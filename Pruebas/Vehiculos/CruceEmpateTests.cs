using Godot;
using SimulacionTrafico.Mapa;
using SimulacionTrafico.Nucleo;
using SimulacionTrafico.Vehiculos;

namespace Pruebas.Vehiculos;

/// <summary>
/// Dos coches con la misma prioridad llegan a la vez a un cruce con stop y sus caminos se cruzan: pasa uno (el que llegó antes o, si
/// llegan a la vez, el que gana el sorteo) y después el otro, sin quedarse los dos cediéndose el paso por turnos.
/// </summary>
public class CruceEmpateTests
{
    private const float Paso = 0.1f;

    private sealed class Cruce
    {
        public readonly Grafo G = new(new Rect2(-400f, -400f, 800f, 800f));
        public readonly ContextoSimulacion Contexto = new(new ConfiguracionSimulacion());
        public readonly Enrutador Enrutador;
        public readonly Carril DesdeOeste, DesdeSur;
        public readonly Tramo HaciaEste, HaciaNorte;
        public readonly Lugar DestinoEste, DestinoNorte;
        private static readonly CatalogoCoches.Modelo Modelo = new("A1.png", null!, 1.8f, 4.5f, Colors.Gray, ClaseVehiculo.Coche);

        public Cruce(TipoControl control = TipoControl.Stop)
        {
            var c = G.AgregarNodo(Vector2.Zero);
            var o = G.AgregarNodo(new Vector2(-300f, 0f));
            var e = G.AgregarNodo(new Vector2(300f, 0f));
            var s = G.AgregarNodo(new Vector2(0f, 300f));
            var n = G.AgregarNodo(new Vector2(0f, -300f));
            var oc = G.AgregarTramo(o, c, CategoriaVia.Local, 1, 1, 50f);
            HaciaEste = G.AgregarTramo(c, e, CategoriaVia.Local, 1, 1, 50f);
            var sc = G.AgregarTramo(s, c, CategoriaVia.Local, 1, 1, 50f);
            HaciaNorte = G.AgregarTramo(c, n, CategoriaVia.Local, 1, 1, 50f);
            G.Construir();
            c.Control = control;   // sin calles prioritarias: todos con la misma prioridad
            G.PrepararTodos();
            DesdeOeste = oc.Carriles.First(l => l.Ida);
            DesdeSur = sc.Carriles.First(l => l.Ida);
            DestinoEste = Destino(1, HaciaEste);
            DestinoNorte = Destino(2, HaciaNorte);
            Enrutador = new Enrutador(G);
        }

        private Lugar Destino(int id, Tramo calle)
        {
            var acceso = calle.Carriles.First(l => l.Ida);
            var puerta = acceso.Eje.Posicion(acceso.Longitud - 20f);
            var lugar = new Lugar(id, TipoLugar.Oficinas, $"destino {id}", new Rect2(puerta, new Vector2(10f, 10f)), acceso, acceso.Longitud - 20f, puerta);
            G.AgregarLugar(lugar);
            return lugar;
        }

        /// <summary>Coche con un conductor igual para todos (llegan a la vez si salen a la misma distancia); su azar cambia su sorteo.</summary>
        public Coche Coche(int id, Carril carril, Lugar destino, Tramo calle, float distanciaLinea = 60f, float v = 0f, ulong azar = 5)
        {
            var coche = new Coche(id, Modelo, new Conductor(new Aleatorio(11), Contexto), G, new Aleatorio(azar), Enrutador, Contexto);
            coche.PrepararSalida(destino, new List<Tramo> { calle });
            coche.Colocar(carril, carril.Longitud - distanciaLinea, v);
            return coche;
        }

        /// <summary>Avanza la simulación de los dos coches y devuelve cuándo entra cada uno en el cruce (null si no entra en 120 s).</summary>
        public (double? a, double? b) Simular(Coche a, Coche b) => Simular(a, b, out _);

        /// <summary>Igual, y la distancia mínima a la que han estado los dos coches.</summary>
        public (double? a, double? b) Simular(Coche a, Coche b, out float distanciaMinima)
        {
            var coches = new[] { a, b };
            double? entraA = null, entraB = null;
            distanciaMinima = float.PositiveInfinity;
            double t = 0;
            for (int k = 0; k < 1200 && (entraA == null || entraB == null); k++, t += Paso)
            {
                foreach (var c in coches) c.Publicar();
                foreach (var c in coches) c.Decidir(Paso, t);
                foreach (var c in coches) c.Aplicar(Paso, t);
                if (entraA == null && a.Via is Conexion) entraA = t;
                if (entraB == null && b.Via is Conexion) entraB = t;
                distanciaMinima = Mathf.Min(distanciaMinima, a.Pos.DistanceTo(b.Pos));
            }
            return (entraA, entraB);
        }
    }

    [Fact]
    public void Llegan_a_la_vez_pasa_uno_enseguida_y_despues_el_otro()
    {
        var cruce = new Cruce();
        var a = cruce.Coche(1, cruce.DesdeOeste, cruce.DestinoEste, cruce.HaciaEste);
        var b = cruce.Coche(2, cruce.DesdeSur, cruce.DestinoNorte, cruce.HaciaNorte);
        var (entraA, entraB) = cruce.Simular(a, b);
        Assert.NotNull(entraA);
        Assert.NotNull(entraB);
        double primero = System.Math.Min(entraA!.Value, entraB!.Value), segundo = System.Math.Max(entraA.Value, entraB.Value);
        // Desde parados a 60 m: unos 10 s hasta la línea, el alto y su tiempo de reacción. Sin esperar a perder la paciencia (17 s o más).
        Assert.True(primero < 20.0, $"el primero entra en el cruce a los {primero:0.0} s (el segundo a los {segundo:0.0} s)");
        // El segundo entra en cuanto el primero despeja, sin esperar a perder la paciencia.
        Assert.True(segundo > primero && segundo - primero < 10.0, $"el primero entra a los {primero:0.0} s y el segundo a los {segundo:0.0} s");
    }

    [Fact]
    public void Pasa_el_que_llego_antes_aunque_pierda_el_sorteo()
    {
        var cruce = new Cruce();
        // Un azar con el que el coche que llega antes (a) pierde el sorteo contra el otro (b).
        ulong azar = 1;
        while (!cruce.Coche(1, cruce.DesdeOeste, cruce.DestinoEste, cruce.HaciaEste, azar: azar)
                    .MeGanaElSorteo(cruce.Coche(2, cruce.DesdeSur, cruce.DestinoNorte, cruce.HaciaNorte, azar: azar + 100))) azar++;
        var cruce2 = new Cruce();
        var a = cruce2.Coche(1, cruce2.DesdeOeste, cruce2.DestinoEste, cruce2.HaciaEste, distanciaLinea: 40f, azar: azar);
        var b = cruce2.Coche(2, cruce2.DesdeSur, cruce2.DestinoNorte, cruce2.HaciaNorte, distanciaLinea: 60f, azar: azar + 100);
        Assert.True(a.MeGanaElSorteo(b));
        var (entraA, entraB) = cruce2.Simular(a, b);
        Assert.NotNull(entraA);
        Assert.NotNull(entraB);
        Assert.True(entraA < entraB, $"a entra a los {entraA:0.0} s y b a los {entraB:0.0} s");
    }
}

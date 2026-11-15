using System.Collections.Generic;
using System.Linq;
using Godot;
using SimulacionTrafico.Nucleo;
using SimulacionTrafico.Mapa;
using SimulacionTrafico.Datos;
using SimulacionTrafico.Trafico;

namespace SimulacionTrafico.Vehiculos;

// Simulación: creación de la flota (puertas de la ciudad, coches y autobuses).
public partial class Simulacion : Node
{
    /// <summary>Líneas de autobús (si la configuración lleva autobuses).</summary>
    public List<LineaAutobus> Lineas { get; private set; } = new();

    /// <summary>
    /// Crea las líneas de autobús y sus autobuses, repartidos a lo largo de cada línea (cada uno empieza en una parada distinta).
    /// Los autobuses no cuentan para la demanda: salen cuando les toca.
    /// </summary>
    private void CrearAutobuses()
    {
        var modelos = Catalogo.De(ClaseVehiculo.Autobus);
        if (modelos.Count == 0) return;
        Lineas = LineasAutobus.Crear(_grafo);
        foreach (var linea in Lineas)
            for (int k = 0; k < linea.Autobuses; k++)
            {
                var azar = _azar.Derivar((ulong)Flota.Count);   // secuencia propia del autobús
                var modelo = modelos[azar.RandiRange(0, modelos.Count - 1)];
                var bus = new Coche(Flota.Count, modelo, new Conductor(azar.Derivar(1), Contexto, ClaseVehiculo.Autobus), _grafo, azar, Enrutador, Contexto);
                int parada = k * linea.Paradas.Count / linea.Autobuses;
                bus.Entrar(linea.Paradas[parada]);
                bus.Rutina = new Rutina(Perfil.Autobus, linea.Paradas[parada], null, null, azar) { Linea = linea, Parada = parada };
                Flota.Add(bus);
                Encolar(bus, azar.RandfRange(0f, 60f));
            }
    }

    /// <summary>Puertas de la ciudad: una por cada carretera que llega al borde.</summary>
    public List<Lugar> Puertas { get; } = new();

    /// <summary>
    /// Crea una puerta (lugar de tipo Exterior) al final de cada carretera que llega al borde del mapa: se llega por el carril que va
    /// hacia fuera (y el vehículo sale del mapa) y se sale por el que va hacia dentro (entra en la ciudad).
    /// </summary>
    private void CrearPuertas()
    {
        foreach (var n in _grafo.Nodos.Where(Grafo.EsBorde))
        {
            var t = n.Tramos.FirstOrDefault();
            var haciaFuera = t?.Carriles.Where(l => l.Destino == n).OrderByDescending(l => l.Indice).FirstOrDefault();
            var haciaDentro = t?.Carriles.Where(l => l.Origen == n).OrderByDescending(l => l.Indice).FirstOrDefault();
            if (haciaFuera == null || haciaDentro == null) continue;
            var puerta = new Lugar(_grafo.Lugares.Count, TipoLugar.Exterior, $"Exterior-{n.Nombre[3..]}", new Rect2(n.Pos - new Vector2(5f, 5f), new Vector2(10f, 10f)),
                haciaFuera, 0.85f * haciaFuera.Longitud, n.Pos);
            puerta.UsarSalida(haciaDentro, 0.15f * haciaDentro.Longitud);
            _grafo.AgregarLugar(puerta);
            Puertas.Add(puerta);
        }
    }

    private Lugar PuertaAlAzar(Aleatorio azar) => Puertas[azar.RandiRange(0, Puertas.Count - 1)];

    private void CrearCoche(int id)
    {
        var azar = _azar.Derivar((ulong)id);   // secuencia propia del vehículo: no depende del orden en que se creen ni de los demás
        // De paso por la ciudad: entra por una puerta y sale por otra (coches, camiones y alguna moto).
        if (Puertas.Count > 1 && azar.Randf() < Config.ProporcionTransito)
        {
            float s = azar.Randf();
            var claseTransito = s < 0.25f ? ClaseVehiculo.Camion : s < 0.30f ? ClaseVehiculo.Moto : ClaseVehiculo.Coche;
            var modelosTransito = Catalogo.De(claseTransito);
            if (modelosTransito.Count == 0) { claseTransito = ClaseVehiculo.Coche; modelosTransito = Catalogo.De(claseTransito); }
            var deTransito = new Coche(id, modelosTransito[azar.RandiRange(0, modelosTransito.Count - 1)], new Conductor(azar.Derivar(1), Contexto, claseTransito), _grafo, azar, Enrutador, Contexto);
            var entrada = PuertaAlAzar(azar);
            deTransito.Entrar(entrada);
            deTransito.Rutina = new Rutina(Perfil.Transito, entrada, null, null, azar);
            Flota.Add(deTransito);
            Encolar(deTransito, azar.RandfRange(0f, 300f));
            return;
        }

        // Clase del vehículo: motos y camiones en su proporción; el resto, coches.
        float sorteo = azar.Randf();
        var clase = sorteo < Config.ProporcionMotos ? ClaseVehiculo.Moto : sorteo < Config.ProporcionMotos + Config.ProporcionCamiones ? ClaseVehiculo.Camion : ClaseVehiculo.Coche;
        var modelos = Catalogo.De(clase);
        if (modelos.Count == 0) { clase = ClaseVehiculo.Coche; modelos = Catalogo.De(clase); }
        var modelo = modelos[azar.RandiRange(0, modelos.Count - 1)];
        var coche = new Coche(id, modelo, new Conductor(azar.Derivar(1), Contexto, clase), _grafo, azar, Enrutador, Contexto);

        if (clase == ClaseVehiculo.Camion)
        {
            // Los camiones de reparto tienen su base en una fábrica (o, si no hay, en un centro comercial).
            var bases = Puertas.Count > 0 && azar.Randf() < Config.ProporcionCamionesDeFuera ? Puertas   // reparte desde fuera de la ciudad
                      : _grafo.LugaresPorTipo.GetValueOrDefault(TipoLugar.Fabrica) ?? _grafo.LugaresPorTipo.GetValueOrDefault(TipoLugar.CentroComercial);
            if (bases is { Count: > 0 })
            {
                var base_ = bases[azar.RandiRange(0, bases.Count - 1)];
                coche.Entrar(base_);
                coche.Rutina = Misiones.CrearRutinaReparto(base_, azar);
                Flota.Add(coche);
                Encolar(coche, azar.RandfRange(0f, 300f));
                return;
            }
        }

        // Empieza dentro de una vivienda y sale al cabo de un rato al azar (así no salen todos a la vez).
        var casas = _grafo.LugaresPorTipo.GetValueOrDefault(TipoLugar.Vivienda);
        var origen = casas is { Count: > 0 }
            ? casas[azar.RandiRange(0, casas.Count - 1)]
            : _grafo.Lugares[azar.RandiRange(0, _grafo.Lugares.Count - 1)];
        coche.Entrar(origen);
        if (Puertas.Count > 0 && azar.Randf() < Config.ProporcionDeFuera)
        {
            // Vive fuera de la ciudad y entra a trabajar: su casa es una puerta de la ciudad.
            origen.Dentro--;
            var puertaCasa = PuertaAlAzar(azar);
            coche.Entrar(puertaCasa);
            coche.Rutina = Misiones.CrearRutina(_grafo, puertaCasa, _trabajosAsignados, azar, null, 0f, trabajador: true);
        }
        else if (origen.Tipo == TipoLugar.Vivienda)
            coche.Rutina = Misiones.CrearRutina(_grafo, origen, _trabajosAsignados, azar, Puertas, Config.ProporcionTrabajaFuera);
        Flota.Add(coche);
        Encolar(coche, azar.RandfRange(0f, 300f));
    }
}

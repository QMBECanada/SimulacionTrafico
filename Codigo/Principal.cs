using System.Diagnostics;
using System.Linq;
using Godot;
using SimulacionTrafico.Camara;
using SimulacionTrafico.Datos;
using SimulacionTrafico.Mapa;
using SimulacionTrafico.Nucleo;
using SimulacionTrafico.Trafico;
using SimulacionTrafico.UI;
using SimulacionTrafico.Vehiculos;

namespace SimulacionTrafico;

public partial class Principal : Node2D
{
    private static readonly float[] VelocidadesSim = { 1f, 10f, 30f, 60f };

    private CamaraCiudad _camara = null!;
    private VistaCiudad _vista = null!;
    private VistaCoches _vistaCoches = null!;
    private Simulacion _sim = null!;
    private Grafo _grafo = null!;
    private Label? _info;
    private TablaDatos _tablaGeneral = null!, _tablaCoche = null!;
    private PanelContainer _barraTeclas = null!;
    private int? numRadares;
    private Estudio? _estudio;
    private string _nombreCiudad = "";
    private bool _seguirPendiente;
    private float _zoomSeguir;
    private double _msGeneracion;
    private double _msCruces;
    private Coche? _seguidoCoche;        // el coche que sigue la cámara (tecla C)
    private float _seguidoSinCircular;   // segundos que lleva el coche seguido sin circular (ha llegado a un lugar)
    private string _avisoSeguir = "";
    private float _avisoSeguirT;

    private double _benchSegundos; // > 0: mide FPS durante esos segundos, lo imprime y sale
    private double _benchTiempo;
    private int _benchFotogramas;

    private float tam = 2000f;
    private int semilla = 1, numCoches = 40;
    private float estancia = 1f;
    private float esperaCaptura = 1f;
    private int numGuardias = -1;
    private int? ciudad = null;
    private bool generarCiudades = false;
    private string? nombreEstudio = null;
    private ConfiguracionSimulacion _config = new();   // opciones de la simulación (argumentos y pantalla inicial)
    private double? horaInicio = null;
    private float? porcentaje = null;
    private double? hasta = null;
    private float? zoom = null;
    private float aceleracion = 1f;
    private Vector2? pos = null;
    private string? captura = null;
    private bool seguir = false;
    private bool tamDado, sinMenu, forzarMenu;
    private double horasSim;              // > 0: la simulación se detiene al cabo de esas horas
    private float[]? curva;                // porcentaje por hora elegido en la pantalla inicial
    private int? vehiculosElegidos;
    private bool _terminada;
    private int? localidad;                  // ciudad del mundo para el sol (índice en Localidades.Todas)
    private int mes = 6, dia = 21;
    private double calentamientoMin;         // minutos iniciales sin registrar datos
    private Sol? _sol;
    private RelojUI? _reloj;
    private float _luzAplicada = -1f;
    private readonly System.Collections.Generic.List<Node> _nodosSim = new();   // todo lo que crea una simulación, para poder quitarlo al volver al menú
    private bool _volviendo;
    private bool soloDatos;                 // modo «solo datos»: sin dibujar, lo más rápido posible
    private bool _soloDatosActivo;
    private double _finSoloDatos;
    private PantallaProgreso? _progreso;
    private Stopwatch? _relojSolo;
    private float volverTras;   // solo para pruebas: vuelve al menú pasados esos segundos
    private string? lote;
    private bool detectarAccidentes = true;
    private bool probarRutas;   // comprueba que hay ruta entre los lugares y sale
    private bool diagnostico;   // salida detallada por consola para depurar (medias, esperas, ciclos...)
    private int paralelo;

    public override void _Ready()
    {
        AddChild(new CursorGrande());
        // Argumentos de prueba:
        // godot --path . -- --tam=2000 --coches=40 --semilla=1 --zoom=4 --pos=0,0 --captura=ruta.png --bench=5 --seguir --sim=5
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (_config.ConArgumento(arg) is { } conArgumento) _config = conArgumento;
            else if (arg.StartsWith("--tam=")) { tam = arg["--tam=".Length..].ToFloat(); tamDado = true; }
            else if (arg == "--sinmenu") sinMenu = true;
            else if (arg == "--menu") forzarMenu = true;
            else if (arg == "--diagnostico") diagnostico = true;
            else if (arg == "--probar-rutas") probarRutas = true;
            else if (arg == "--sinaccidentes") detectarAccidentes = false;
            else if (arg == "--solo-datos") soloDatos = true;
            else if (arg.StartsWith("--radares=")) numRadares = arg["--radares=".Length..].ToInt();
            else if (arg == "--ver-accidente") irAccidentePendiente = true;
            else if (arg.StartsWith("--cursor-en=")) { var q = arg["--cursor-en=".Length..].Split(','); CursorGrande.PosicionForzada = new Vector2(q[0].ToFloat(), q[1].ToFloat()); }
            else if (arg.StartsWith("--volver-tras=")) volverTras = arg["--volver-tras=".Length..].ToFloat();
            else if (arg.StartsWith("--info-en="))
            {
                var q = arg["--info-en=".Length..].Split(',');
                InfoCalle.PuntoForzado = new Vector2(q[0].ToFloat(), q[1].ToFloat());
            }
            else if (arg.StartsWith("--cursor-png=")) CursorGrande.Guardar(arg["--cursor-png=".Length..]);
            else if (arg.StartsWith("--lote=")) lote = arg["--lote=".Length..];
            else if (arg.StartsWith("--paralelo=")) paralelo = arg["--paralelo=".Length..].ToInt();
            else if (arg.StartsWith("--curva="))
            {
                var v = arg["--curva=".Length..].Split(',').Select(x => x.Trim().ToFloat()).ToArray();
                curva = v.Length == 24 ? v : Enumerable.Repeat(v[0], 24).ToArray();
            }
            else if (arg.StartsWith("--lugar=")) localidad = Localidades.Indice(arg["--lugar=".Length..]) is var li && li >= 0 ? li : null;
            else if (arg.StartsWith("--fecha="))
            {
                var f = arg["--fecha=".Length..].Split('-');
                mes = f[0].ToInt();
                dia = f.Length > 1 ? f[1].ToInt() : 1;
            }
            else if (arg.StartsWith("--calentamiento=")) calentamientoMin = arg["--calentamiento=".Length..].ToFloat();
            else if (arg.StartsWith("--horas=")) horasSim = arg["--horas=".Length..].ToFloat();
            else if (arg.StartsWith("--semilla=")) semilla = arg["--semilla=".Length..].ToInt();
            else if (arg.StartsWith("--coches=")) numCoches = arg["--coches=".Length..].ToInt();
            else if (arg.StartsWith("--estancia=")) estancia = arg["--estancia=".Length..].ToFloat();
            else if (arg.StartsWith("--hasta=")) hasta = arg["--hasta=".Length..].ToFloat();
            else if (arg.StartsWith("--hora=")) horaInicio = arg["--hora=".Length..].ToFloat();
            else if (arg.StartsWith("--porcentaje=")) porcentaje = arg["--porcentaje=".Length..].ToFloat();
            else if (arg.StartsWith("--guardias=")) numGuardias = arg["--guardias=".Length..].ToInt();
            else if (arg.StartsWith("--estudio=")) nombreEstudio = arg["--estudio=".Length..];
            else if (arg.StartsWith("--ciudad=")) ciudad = arg["--ciudad=".Length..].ToInt();
            else if (arg == "--generar-ciudades") generarCiudades = true;
            else if (arg.StartsWith("--espera=")) esperaCaptura = arg["--espera=".Length..].ToFloat();
            else if (arg.StartsWith("--sim=")) aceleracion = arg["--sim=".Length..].ToFloat();
            else if (arg.StartsWith("--zoom=")) zoom = arg["--zoom=".Length..].ToFloat();
            else if (arg.StartsWith("--pos="))
            {
                var p = arg["--pos=".Length..].Split(',');
                pos = new Vector2(p[0].ToFloat(), p[1].ToFloat());
            }
            else if (arg.StartsWith("--captura=")) captura = arg["--captura=".Length..];
            else if (arg.StartsWith("--bench=")) _benchSegundos = arg["--bench=".Length..].ToFloat();
            else if (arg == "--seguir") seguir = true;
        }

        // Sin argumentos que fijen la ciudad o el modo de prueba, y con pantalla, se empieza por la pantalla inicial.
        bool conPantalla = DisplayServer.GetName() != "headless";
        if (lote != null)
        {
            // Lote de estudios en paralelo (cada uno en su proceso sin pantalla).
            int codigo = Lote.Ejecutar(lote, paralelo);
            GetTree().Quit(codigo);
            return;
        }
        if (!conPantalla && !generarCiudades && hasta == null)
        {
            // Modo sin pantalla: sin horas indicadas se simula un día; el calentamiento se suma al principio.
            if (horasSim <= 0) horasSim = 24;
            hasta = horasSim * 3600.0 + calentamientoMin * 60.0;
        }
        if (conPantalla && !sinMenu && (forzarMenu || (ciudad == null && !tamDado && hasta == null && captura == null && _benchSegundos <= 0 && !generarCiudades)))
        {
            MostrarPantallaInicial();
            if (captura != null) CapturarYSalir(captura, esperaCaptura);
            return;
        }
        Arrancar();
    }

    private void MostrarPantallaInicial()
    {
        var capaMenu = new CanvasLayer();
        AddChild(capaMenu);
        var menu = new PantallaInicial();
        capaMenu.AddChild(menu);
        menu.Iniciar += cfg =>
        {
            ciudad = cfg.Ciudad;
            semilla = cfg.Semilla;
            vehiculosElegidos = cfg.Vehiculos;
            horaInicio = cfg.HoraInicio;
            horasSim = cfg.Horas;
            numRadares = cfg.Radares;
            curva = cfg.PorcentajePorHora;
            nombreEstudio = cfg.Estudio;
            localidad = cfg.Localidad;
            mes = cfg.Mes;
            dia = cfg.Dia;
            calentamientoMin = cfg.CalentamientoMin;
            aceleracion = RelojUI.VelocidadPorDefecto;
            soloDatos = cfg.SoloDatos;
            _config = _config with
            {
                ProbabilidadNavegador = cfg.Navegador,
                SemaforosAdaptativos = cfg.SemaforosAdaptativos,
                Lluvia = cfg.Lluvia,
                Peatones = cfg.Peatones,
                ProporcionDeFuera = cfg.DeFuera,
                ProporcionTrabajaFuera = cfg.TrabajanFuera,
                ProporcionTransito = cfg.DePaso,
            };
            capaMenu.QueueFree();
            Arrancar();
        };
    }

    private void Arrancar()
    {
        if (vehiculosElegidos != null) numCoches = vehiculosElegidos.Value;
        var reloj = Stopwatch.StartNew();
        if (generarCiudades)
        {
            CatalogoCiudades.GenerarTodas(_config.FactorRotondas);
            GetTree().Quit();
            return;
        }
        if (ciudad != null)
        {
            _grafo = CiudadFichero.Cargar(CatalogoCiudades.Ruta(ciudad.Value), out _);
            _nombreCiudad = CatalogoCiudades.Nombre(ciudad.Value);
        }
        else
        {
            _grafo = GeneradorCiudad.Generar(tam, tam, semilla, factorRotondas: _config.FactorRotondas);
            _nombreCiudad = $"generada {tam / 1000f:0.#} x {tam / 1000f:0.#} km (semilla {semilla})";
        }
        if (numRadares != null) GeneradorCiudad.ElegirRadares(_grafo, numRadares);   // radares pedidos (como mucho 100)
        _msGeneracion = reloj.Elapsed.TotalMilliseconds;
        reloj.Restart();
        _grafo.PrepararTodos();
        _msCruces = reloj.Elapsed.TotalMilliseconds;
        GD.Print($"Ciudad: {_grafo.Nodos.Count} nodos generados en {_msGeneracion:0} ms, conflictos de cruces en {_msCruces:0} ms");
        ComprobarGrafo();
        if (probarRutas) { Diagnostico.ProbarRutas(_grafo); GetTree().Quit(); return; }

        if (soloDatos) _camara = new CamaraCiudad { Limites = _grafo.Limites };   // no se dibuja nada: la cámara no se añade
        var vp = GetViewportRect().Size;
        float zoomAjustado = Mathf.Min(vp.X / _grafo.Limites.Size.X, vp.Y / _grafo.Limites.Size.Y);
        _camara = new CamaraCiudad
        {
            Limites = _grafo.Limites,
            Position = pos ?? Vector2.Zero,
            ZoomMin = zoomAjustado * 0.8f,
        };
        if (!soloDatos)
        {
            Mundo(_camara);
            _camara.MakeCurrent();
            _camara.Zoom = Vector2.One * Mathf.Max(zoom ?? 1f, _camara.ZoomMin);

            _vista = new VistaCiudad(_grafo, _camara);
            Mundo(_vista);
        }

        _sim = new Simulacion(_grafo, CatalogoCoches.Cargar(), numCoches, semilla, _config, estancia, numGuardias) { Aceleracion = aceleracion };
        if (horaInicio != null || porcentaje != null || curva != null)
        {
            // Con --porcentaje es constante y sin variación; solo con --hora se usa la curva por defecto (con ±5 puntos al azar).
            var azarDemanda = new Aleatorio((ulong)semilla * 104729UL + 7UL);
            double inicioS = (horaInicio ?? 8.0) * 3600.0;
            _sim.Demanda = porcentaje != null
                ? Demanda.Constante(porcentaje.Value, inicioS, azarDemanda)
                : new Demanda(curva ?? Demanda.CurvaPorDefecto, inicioS, azarDemanda);
        }
        if (localidad != null) _sol = new Sol(Localidades.Todas[localidad.Value], mes, dia);
        // De noche cuando apenas queda luz del sol (con la ciudad del mundo elegida); si no, de 21:00 a 7:00.
        if (_sol is { } sol) _sim.EsDeNoche = segundoDelDia => sol.Luz(segundoDelDia) < 0.35f;
        if (nombreEstudio != null)
        {
            // Cada estudio guarda sus datos en su carpeta: Estudios/<nombre>/
            _estudio = Estudio.Crear(nombreEstudio, (horaInicio ?? 0.0) * 3600.0);
            // Con calentamiento, los datos empiezan a guardarse cuando pasa ese tiempo (ver _Process y el modo sin pantalla).
            if (calentamientoMin <= 0) _sim.AbrirEstudio(_estudio);
            Exportador.EscribirParametros(_estudio, ParametrosDelEstudio(tam, semilla, estancia, horaInicio ?? 0.0));
            GD.Print($"Estudio '{_estudio.Nombre}': los datos se guardan en {_estudio.Carpeta}");
        }
        _sim.DetectarAccidentes = detectarAccidentes;
        _sim.DiagnosticoAceleracion = diagnostico;
        Mundo(_sim);
        if (soloDatos)
        {
            IniciarSoloDatos();
            return;
        }
        Mundo(new VistaSemaforos(_grafo, _camara, _sim));
        _vistaCoches = new VistaCoches(_sim, _camara);
        Mundo(_vistaCoches);
        Mundo(new VistaAccidentes(_sim, _camara));
        Mundo(new VistaRadares(_grafo, _camara));
        Mundo(new VistaPeatones(_sim, _camara));

        if (hasta != null)
        {
            // Modo sin pantalla: avanza todo lo rápido que puede y resume el resultado.
            var reloj2 = Stopwatch.StartNew();
            if (_estudio != null && calentamientoMin > 0)
            {
                _sim.AvanzarHasta(System.Math.Min(calentamientoMin * 60.0, hasta.Value));
                _sim.AbrirEstudio(_estudio);
            }
            // Informe hora a hora para ver cómo sigue la simulación a la demanda.
            for (double t = 3600; ; t += 3600)
            {
                double fin = System.Math.Min(t, hasta.Value);
                int salidasAntes = _sim.Salidas;
                _sim.AvanzarHasta(fin);
                if (_sim.Demanda != null)
                    GD.Print($"  {_sim.Demanda.Hora(_sim.TiempoSimulado - 1):00}h  viajes objetivo {(int)System.Math.Round(_sim.Demanda.ViajesPorHora(_sim.TiempoSimulado - 1, _sim.Flota.Count(c => c.Rutina?.Perfil != Perfil.Autobus))),6}  empezados {_sim.Salidas - salidasAntes,6}  en la calle {_sim.EnCalle,5}  " +
                             $"llegadas {_sim.Llegadas}" + (diagnostico ? $"  velocidad media {VelocidadMedia():0.0} km/h" : ""));
                if (fin >= hasta.Value) break;
            }
            double seg = reloj2.Elapsed.TotalSeconds;
            GD.Print($"SIN PANTALLA: {hasta.Value:0} s simulados en {seg:0.0} s reales (x{hasta.Value / Mathf.Max(seg, 1e-6):0}), " +
                     $"{_sim.Coches.Count} circulando de {_sim.Flota.Count}, llegadas {_sim.Llegadas}, HUELLA {_sim.Huella()}");
            if (diagnostico) Diagnostico.InformeFinal(_sim, _grafo);
            _sim.CerrarEstudio();
            GetTree().Quit();
            return;
        }

        var capa = Mundo(new CanvasLayer());
        if (_config.Lluvia) capa.AddChild(new VistaLluvia());   // debajo de la interfaz
        capa.AddChild(new Minimapa(_grafo, _camara));
        Mundo(new InfoCalle(_grafo, _camara, capa));
        if (volverTras > 0f) GetTree().CreateTimer(volverTras).Timeout += VolverAlMenu;

        _reloj = new RelojUI(_sim, Parar, VolverAlMenu, TextoHora, aceleracion);
        capa.AddChild(_reloj);
        _tablaGeneral = new TablaDatos(3) { Position = new Vector2(12, 8) };
        capa.AddChild(_tablaGeneral);
        // Teclas y ratón: en una barra abajo, sobre fondo oscuro.
        _barraTeclas = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        var fondoTeclas = new StyleBoxFlat { BgColor = new Color(0.08f, 0.09f, 0.11f, 0.72f) };
        fondoTeclas.SetCornerRadiusAll(4);
        fondoTeclas.SetContentMarginAll(6);
        _barraTeclas.AddThemeStyleboxOverride("panel", fondoTeclas);
        _info = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        _info.AddThemeFontSizeOverride("font_size", 14);
        _info.AddThemeColorOverride("font_color", new Color(0.92f, 0.93f, 0.95f));
        _barraTeclas.AddChild(_info);
        capa.AddChild(_barraTeclas);
        _tablaCoche = new TablaDatos(2);   // sin coche seguido, el panel no se ve
        capa.AddChild(_tablaCoche);

        _seguirPendiente = seguir;
        _zoomSeguir = zoom ?? 8f;
        if (captura != null) CapturarYSalir(captura, esperaCaptura);
    }

    /// <summary>Comprueba que desde cualquier carril se puede llegar a cualquier otro y que no hay callejones sin salida.</summary>
    private void ComprobarGrafo()
    {
        var sinSalida = _grafo.Carriles.Count(l => !Grafo.SalientesPermitidos(l).Any());

        // Carriles alcanzables siguiendo las salidas posibles, y carriles desde los que se puede volver al primero.
        System.Collections.Generic.HashSet<Carril> Alcance(Carril origen, bool adelante)
        {
            var visto = new System.Collections.Generic.HashSet<Carril> { origen };
            var cola = new System.Collections.Generic.Queue<Carril>();
            cola.Enqueue(origen);
            while (cola.Count > 0)
            {
                var l = cola.Dequeue();
                var vecinos = adelante ? Grafo.SalientesPermitidos(l) : Grafo.EntrantesPermitidos(l);
                foreach (var o in vecinos)
                    if (visto.Add(o)) cola.Enqueue(o);
            }
            return visto;
        }

        var inicio = _grafo.Carriles[0];
        int ida = Alcance(inicio, true).Count, vuelta = Alcance(inicio, false).Count;
        GD.Print($"Grafo: {_grafo.Carriles.Count} carriles, alcanzables desde el primero: {ida}, desde los que se llega a él: {vuelta}, " +
                 $"carriles sin salida: {sinSalida}, giros prohibidos: {_grafo.Nodos.Sum(n => n.GirosProhibidos.Count)}, manzanas: {_grafo.Manzanas.Count}, rotondas: {_grafo.Rotondas.Count}" +
                 (_grafo.Rotondas.Count > 0 ? $" (la primera en {_grafo.Rotondas[0].Centro})" : ""));
    }

    public override void _Process(double delta)
    {
        if (_soloDatosActivo)
        {
            AvanzarSoloDatos();
            return;
        }
        if (_info == null) return; // modo sin pantalla: no hay interfaz
        if (_seguirPendiente && _sim.Coches.Count > 0)
        {
            _seguirPendiente = false;
            SeguirSiguiente(_zoomSeguir);
        }

        // El coche seguido deja la calle al llegar a su destino (y no se ve dentro de un lugar). Si lleva unos segundos así, se pasa
        // al siguiente coche que circule en vez de dejar la cámara parada en una puerta.
        if (_seguidoCoche != null)
        {
            if (_seguidoCoche.Circulando) _seguidoSinCircular = 0f;
            else if ((_seguidoSinCircular += (float)delta) > 4f && _sim.Coches.Count > 0)
            {
                var anterior = _seguidoCoche;
                SeguirSiguiente(_camara.PixelesPorMetro);
                if (_seguidoCoche != anterior)
                {
                    _avisoSeguir = $"El coche {anterior.Id} ha llegado a {anterior.LugarActual?.Nombre ?? "su destino"}. Ahora sigues al coche {_seguidoCoche!.Id}.";
                    _avisoSeguirT = 6f;
                }
            }
        }
        if (_avisoSeguirT > 0f) _avisoSeguirT -= (float)delta;
        if (irAccidentePendiente && _sim.AccidentesActivos.Count > 0)
        {
            irAccidentePendiente = false;
            IrAAccidente();
        }

        if (_benchSegundos > 0)
        {
            _benchTiempo += delta;
            if (_benchTiempo > 1.0) _benchFotogramas++; // el primer segundo se descarta (carga y compilación de shaders)
            if (_benchTiempo > 1.0 + _benchSegundos)
            {
                Diagnostico.InformeBench(_sim, _grafo, _camara, _benchSegundos, _benchFotogramas);
                GetTree().Quit();
            }
        }

        if (_estudio != null && _sim.Estudio == null && !_terminada && _sim.TiempoSimulado >= calentamientoMin * 60.0)
            _sim.AbrirEstudio(_estudio);   // termina el calentamiento: desde aquí se guardan los datos

        if (horasSim > 0 && !_terminada && _sim.TiempoSimulado >= horasSim * 3600.0 + calentamientoMin * 60.0) Parar();

        if (_sol != null)
        {
            float luz = _sol.Luz(HoraDelDiaS());
            if (Mathf.Abs(luz - _luzAplicada) > 0.004f)
            {
                _luzAplicada = luz;
                _vista.SetLuz(luz);
            }
        }

        var general = new System.Collections.Generic.List<(string, string)>
        {
            ("Ciudad", $"{_grafo.Limites.Size.X / 1000f:0.#} x {_grafo.Limites.Size.Y / 1000f:0.#} km"),
            ("Circulando", $"{_sim.Coches.Count} de {_sim.Flota.Count}"),
            ("Llegadas", $"{_sim.Llegadas}"),
            ("Velocidad media", $"{VelocidadMedia():0.0} km/h"),
            ("Tiempo", Hora(_sim.TiempoSimulado)),
            ("Hora del día", _sim.Demanda != null ? HoraDelDia() : "-"),
            ("Viajes por hora", _sim.Demanda != null ? $"{_sim.ViajesPorHoraObjetivo:0}" : "-"),
            ("Accidentes", $"{_sim.AccidentesActivos.Count} en curso de {_sim.TotalAccidentes}"),
            ("Simulación", $"x{_sim.Aceleracion:0.#}"),
            ("Visibles", $"{_vistaCoches.Visibles}"),
            ("Zoom", $"{_camara.PixelesPorMetro:0.00} px/m"),
            ("FPS", $"{Engine.GetFramesPerSecond()}"),
        };
        _tablaGeneral.Mostrar(general);
        // Abajo: la barra de teclas (hasta el minimapa) y, encima, el panel fijo del coche seguido (la mitad del ancho).
        var pantalla = GetViewportRect().Size;
        const float LadoMinimapa = 240f + 24f;
        _barraTeclas.CustomMinimumSize = new Vector2(pantalla.X - 24f - LadoMinimapa - 12f, 0);
        _barraTeclas.Size = _barraTeclas.CustomMinimumSize;
        _barraTeclas.Position = new Vector2(12, pantalla.Y - _barraTeclas.Size.Y - 12);
        _tablaCoche.TamanoFijo = new Vector2(Mathf.Min(pantalla.X * 0.5f, pantalla.X - LadoMinimapa - 36f), 272f);
        _tablaCoche.Position = new Vector2(12, _barraTeclas.Position.Y - 272f - 8f);
        _info.Text = _terminada ? "SIMULACIÓN TERMINADA"
            : "Rueda: zoom   Arrastrar o WASD: mover   C: seguir coche   Esc: soltar   A: ir a un accidente   1-4: velocidad   Espacio: pausa   M: menú   Q: salir";
        var filasCoche = _vistaCoches.Seleccionado is { } c ? DescribirCoche(c, _avisoSeguirT > 0f ? _avisoSeguir : "") : new System.Collections.Generic.List<(string, string)>();
        _tablaCoche.Mostrar(filasCoche);
    }

    public override void _ExitTree() => _sim?.CerrarEstudio();

    private async void CapturarYSalir(string ruta, float espera)
    {
        await ToSignal(GetTree().CreateTimer(espera), SceneTreeTimer.SignalName.Timeout);
        GetViewport().GetTexture().GetImage().SavePng(ruta);
        GetTree().Quit();
    }
}

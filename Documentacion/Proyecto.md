# Simulación de tráfico: diseño

Cómo está hecho el simulador y por qué funciona como funciona. El uso está en [Instrucciones.md](Instrucciones.md) y el contenido de los
ficheros de datos en [Datos.md](Datos.md).

## 1. Objetivo y principios

El simulador produce **datos de circulación en bruto** de ciudades inventadas, vistas desde arriba. Cada estudio guarda en ficheros TSV todo
lo que ocurre (viajes, semáforos, radares, atascos, accidentes, guardias, peatones...); el análisis se hace fuera del programa. La parte
visual sirve para comprobar y entender la simulación.

Principios del modelo:

1. Cada vehículo quiere llegar a su destino **lo más rápido posible**, fiel a su forma de conducir.
2. Cada vehículo intenta **por todos los medios no chocar**: ve lo que tiene alrededor y actúa en consecuencia.
3. Los vehículos están sujetos a la **física**: aceleraciones, distancias de frenada y aceleración lateral en las curvas.
4. La **ciudad** (semáforos, guardias) intenta que la circulación sea fluida.
5. **Sin artificios:** no se teletransportan, eliminan ni inventan vehículos, ni se saltan las normas para que algo funcione. Los problemas
   se resuelven con mecanismos que existen en una ciudad real.
6. **Los atascos forman parte del resultado.** Lo que no debe ocurrir es un bloqueo permanente (un ciclo en el que nadie puede moverse).
7. **Cada vehículo es un agente autónomo:** percibe, decide y actúa por sí mismo. No hay un cerebro central que lo coordine.
8. **Reproducible:** con la misma ciudad, los mismos parámetros y la misma semilla, la simulación es idéntica, con cualquier número de hilos.

## 2. Tecnología y organización

- **Godot 4.7 (.NET)** con **C#** (net9.0), renderer Compatibility. Todo el código es C#.
- 1 unidad de Godot = 1 metro. Paso fijo de simulación de 0,1 s, con interpolación al dibujar.

```
SimulacionTrafico/
  project.godot, SimulacionTrafico.csproj, SimulacionTrafico.sln
  export_presets.cfg         exportación del ejecutable de Windows
  Escenas/Principal.tscn     escena principal
  Codigo/
    Principal*.cs            arranque, argumentos, interfaz, modo sin pantalla
    Diagnostico.cs           informes por consola para depurar (--diagnostico, --bench); no son datos del estudio
    Nucleo/                  medidas (Escala), azar propio (Aleatorio), configuración, sol, índice espacial
    Mapa/                    grafo (Nodo, Tramo, Carril, Conexion, Trayectoria), semáforos, rotondas, lugares, radares, río,
                             generador de ciudades, ficheros de ciudad, rutas (Enrutador, CotasRuta), dibujo del mapa
    Vehiculos/               vehículo (Coche), conductor, IDM, simulación, demanda, rutinas, misiones, dibujo de vehículos
    Trafico/                 atascos, central de tráfico, guardias, accidentes, choques, percepción, peatones, autobuses
    Datos/                   estudios y ficheros TSV, lotes
    Camara/, UI/             cámara, pantalla inicial, reloj, tablas, minimapa
  Assets/                    dibujos de vehículos, flechas del suelo, iconos y portada
  Ciudades/                  las cinco ciudades fijas (JSON)
  Pruebas/                   pruebas automáticas (xUnit)
  Documentacion/             este documento, las instrucciones y los ficheros de datos
```

## 3. Ciudades

### Cinco ciudades fijas

| Ciudad | Tamaño |
|---|---|
| 1 | 3 × 2 km |
| 2 | 6 × 4 km |
| 3 | 12 × 8 km |
| 4 | 10 × 10 km |
| 5 | 6 × 2 km |

Cada ciudad se generó una vez con una semilla fija y se guarda en `Ciudades/ciudadN.json`: cruces, calles (con sus curvas), controles,
semáforos, calles con prioridad, giros prohibidos, manzanas, lugares, bases de guardias, radares y río. Siempre se carga el fichero, así que
las ciudades y los nombres de calles y cruces no cambian aunque cambie el generador. `--generar-ciudades` las vuelve a generar. `--tam=N`
genera al momento una ciudad cuadrada de N metros, sin guardarla.

### Generador (`Mapa/GeneradorCiudad`)

Mismo tamaño y semilla dan la misma ciudad.

- **Trazado ortogonal, sin diagonales.** La separación entre calles depende de la zona (unos 70 m en el centro y 170 m en la periferia, con
  un factor al azar), así que hay manzanas de muchos tamaños y proporciones.
- **Calles interrumpidas:** algunas calles locales se sustituyen por una manzana; la calle termina contra ella y los cruces vecinos quedan
  en T. Nunca se interrumpen arterias ni colectoras, ni dos veces el mismo cruce.
- **Jerarquía:** avenida central de 3 + 3 carriles en cada eje, arterias de 2 + 2 y 2 + 1, colectoras de 1 + 1 y locales de 1 + 1 (algunas
  de sentido único, nunca las del borde).
- **Autovía de circunvalación** de 3 + 3 carriles que rodea la ciudad, con las esquinas curvas. Se une a algunas arterias y colectoras con
  **enlaces en diamante**: la calle pasa por debajo (paso inferior, un túnel corto) y cuatro ramales de un carril la unen con la autovía, con
  una salida por la derecha y una entrada que corre junto al carril derecho antes de incorporarse cediendo el paso. La autovía no tiene
  cruces a nivel.
- **Carreteras de acceso:** en cada lado de la ciudad, uno o dos enlaces prolongan su calle hasta el borde del mapa (2 + 2 carriles). Al
  final de cada una hay una **puerta de la ciudad** (lugar de tipo `Exterior`): por ahí salen y entran los vehículos que vienen de fuera, van
  fuera o están de paso.
- **Río** con meandros que cruza la ciudad de borde a borde, cerca del centro. Lo cruzan **pocos puentes**: la avenida, las arterias y,
  donde entre dos puentes quedan más de 900 m, una colectora. Las calles paralelas al río se cortan en la orilla. Las manzanas que tocan el
  agua no se construyen y las de la orilla son parque.
- **Rotondas** en algunos cruces interiores entre calles locales y colectoras: anillo de un sentido (antihorario), de 30 m de radio (34 m y
  dos carriles entre colectoras), con una isla central.
- **Control de cada cruce:** semáforo entre calles colectoras o mayores; stop o ceda el paso en los demás, con una calle prioritaria (la de
  mayor categoría; si empatan, la que atraviesa el cruce). Los cruces de los ramales con la calle llevan semáforo si la calle es una
  arteria o una carretera de acceso.
- **Giros prohibidos:** en algunas aproximaciones (nunca en rotondas ni en la autovía) se prohíbe girar a la izquierda, y en algunas calles
  locales que llegan a una arteria, seguir recto. Siempre queda al menos una salida.
- **Zonas de usos:** distrito de negocios en el centro, polígonos industriales en la periferia, parques comerciales, zonas de ocio, centros
  médicos y barrios residenciales. Los lugares de cada manzana se eligen según su zona.
- **Comprobación:** el grafo de cada ciudad es fuertemente conexo (desde cualquier carril se llega a cualquier otro) y no hay carriles sin
  salida. Si un giro prohibido o el río rompen esa condición, se corrige al generar.

**Límites de velocidad:** 50 km/h en todas las calles, 30 km/h en las de un solo carril, 40 km/h en el anillo de las rotondas, 120 km/h en
la autovía, 80 km/h en las carreteras de acceso, 60 km/h en los ramales de salida y 100 km/h en los de entrada.

### Grafo (`Mapa/`)

- **Nodo:** cruce o extremo, con su control (ninguno, semáforo, stop, ceda, rotonda), las calles que llegan y su radio.
- **Tramo:** calle entre dos nodos, con categoría (local, colectora, arteria, autovía), carriles por sentido y límite. Su eje es una
  `Trayectoria` (línea quebrada), así que admite curvas. Puede ser puente o túnel.
- **Carril:** un sentido de circulación, con su eje recortado fuera de los cruces y sus carriles contiguos.
- **Conexión:** trayecto dentro de un cruce entre dos carriles (curva de Bézier), con su giro, su radio y los **conflictos** con otras
  conexiones (dónde se cruzan o confluyen, también si sus trayectorias pasan a menos de 2,4 m). Se calculan todos al cargar la ciudad.
- **Nivel:** cada vehículo está a nivel −1 (en un túnel y sus bocas), 0 (superficie) o +1 (puente). Solo se ven, se perciben y chocan los
  del mismo nivel.

### Nombres estables

Calles `H-fila-columna` y `V-fila-columna`, circunvalación `C-n`, enlaces `E-n-...`, carreteras de acceso `EXT-n`, rotondas `R-...`; cruces
`X-fila-columna`, `CX-n`, `RX-...`, `PX-n`; lugares `<Tipo>-n`; radares `RD-n`; semáforos `S-<cruce>-<calle>`; pasos de peatones
`P-<cruce>-<calle>`. Sentidos `OE`, `EO`, `NS`, `SN`, `horario` y `antihorario`. Detalle en [Datos.md](Datos.md).

## 4. Vehículos y conductores

### Clases

Coches (86 % de la flota por defecto), motos (9 %), camiones de reparto (5 %) y autobuses de línea (aparte). Cada vehículo tiene un dibujo
de `Assets/` a su tamaño real: los PNG están a 40 píxeles por metro, así que su tamaño en metros es el de la imagen entre 40. Los modelos se
nombran con la letra de la clase (`A` automóvil, `M` moto, `C` camión, `B` autobús) y un número.

### Personalidad (`Vehiculos/Conductor`)

Cada conductor tiene una **agresividad** `a` de −2 (temeroso) a +2 (temerario): un 7 % entre −2 y −1,2, un 4 % entre 1,2 y 2, y el resto con
una normal de media 0 y desviación 0,4, limitada a ±1,15. Todos sus parámetros dependen de ella:

| Parámetro (coche, en seco y de día) | Fórmula y límites | a = −2 | a = 0 | a = +2 |
|---|---|---|---|---|
| Velocidad respecto al límite | 3 % + 8,5 %·a (de −15 % a +20 %) | −14 % | +3 % | +20 % |
| Tiempo de separación | 1,5 − 0,5·a s (mín. 0,6) | 2,5 s | 1,5 s | 0,6 s |
| Distancia en parado | 2,0 − 0,8·a m (mín. 0,8) | 3,6 m | 2,0 m | 0,8 m |
| Aceleración máxima | 2,6 + 0,5·a m/s² (1,6 a 3,3) | 1,6 | 2,6 | 3,3 |
| Frenada normal | 2,5 + 0,25·a m/s² (2,0 a 3,0) | 2,0 | 2,5 | 3,0 |
| Frenada fuerte | 5,0 + 0,5·a m/s² (4,0 a 6,0) | 4,0 | 5,0 | 6,0 |
| Frenada de emergencia | 8,0 + 0,5·a m/s² (7,0 a 9,0) | 7,0 | 8,0 | 9,0 |
| Aceleración lateral en giros | 3,0 + 0,5·a m/s² (mín. 1,5) | 2,0 | 3,0 | 4,0 |
| Aceleración lateral en rotondas | 3,5 + 0,9·a m/s² (mín. 2,0) | 2,0 | 3,5 | 5,3 |
| Tiempo de reacción al arrancar | 0,8 − 0,4·a s (mín. 0,2), con azar | 1,6 s | 0,8 s | 0,2 s |
| Hueco para cruzar | 2,0·(1 − 0,3·a) s (0,8 a 3,4) | 3,2 s | 2,0 s | 0,8 s |
| Paciencia en un cruce | 25·(1 − 0,3·a) s (15 a 55) | 40 s | 25 s | 15 s |
| Cortesía al cambiar de carril | 0,5 − 0,3·a (0 a 1) | 1,0 | 0,5 | 0 |
| Ventaja para cambiar de carril | 0,3 − 0,12·a m/s² (0,05 a 0,7) | 0,54 | 0,30 | 0,06 |

- **Camiones y autobuses** aceleran y frenan mucho más despacio, toman las curvas más despacio (un 65 % y un 55 % de la aceleración lateral),
  dejan más distancia, piden más hueco y no pasan de 90 y 100 km/h (los autobuses, nunca por encima del límite). **Las motos** aceleran y
  frenan más fuerte, toman las curvas un 20 % más rápido y se arriman más.
- **Variación por acción:** la velocidad (5 %) y la separación (15 %) fluctúan de forma lenta y continua (proceso de Ornstein-Uhlenbeck); el
  tiempo de reacción y la distancia que deja en parado cambian cada vez que arranca. Así cada conductor es coherente consigo mismo pero no
  idéntico en cada acción.
- **Navegador:** el 60 % de los conductores (parámetro) evita las calles atascadas.
- **Lluvia:** frenada de emergencia en mojado de 4,0 a 6,5 m/s², frenadas y aceleraciones menores, velocidad un 10 % menor, más separación,
  más distancia en parado, más hueco y más reacción. **Noche:** algo menos de velocidad y algo más de separación, hueco y reacción.

### Rutina y viajes (`Vehiculos/Rutina`, `Misiones`, `Demanda`)

- Cada conductor tiene una **rutina**: su casa, un perfil (trabaja, trabaja y lleva a los niños al colegio, o no trabaja fuera), un trabajo
  fijo, sus horarios (salida hacia las 7:30 de media, jornada de unas 9 horas) y si come fuera. Los camiones de reparto van de su base (una
  fábrica) a clientes durante su turno; los autobuses recorren su línea; una parte de la flota vive fuera, trabaja fuera o está de paso.
- **El destino depende de la hora y de dónde está:** al trabajo por la mañana, a comer, de vuelta a casa al acabar la jornada, recados y ocio
  según la hora. De unos 12 lugares candidatos del tipo elegido, pesan más los cercanos.
- **Demanda:** una curva de 24 porcentajes dice qué parte de la flota empieza un viaje cada hora (por defecto, unos 3 viajes por vehículo y
  día, con puntas a las 8 y a las 17-18 h); la simulación varía cada hora hasta un 15 % al azar. Las rutinas deciden quién sale y adónde; la
  demanda marca el ritmo de salidas.
- **Lugares y aparcamiento:** cada lugar tiene plazas según su tipo y superficie. Al llegar a la puerta, el vehículo sale de la calle y
  maniobra fuera de la calzada hasta su plaza; si no hay plaza, espera dentro en una cola, sin estorbar. Al salir tarda unos segundos en
  llegar de su plaza a la puerta.
- **Autobuses:** una línea por cada arteria recta de más de 1 km, de ida y vuelta, con una parada (dársena junto al carril derecho) cada unos
  400 m, de 6:00 a 23:00 y con un autobús cada unos 10 minutos.

### Rutas (`Mapa/Enrutador`, `CotasRuta`)

- Cada vehículo calcula **su propia ruta más rápida**, con el coste de su conductor: su velocidad en cada tipo de vía, el retraso de cada
  semáforo, stop, ceda o giro, y lo que rehúye la autovía si es prudente o temeroso. Los conductores con navegador suman además la congestión
  actual de cada calle.
- **A\* con cotas ALT:** se busca sobre calles con sentido (llegar a una calle desde otra equivale a un giro), con una heurística de
  referencias precalculadas que nunca sobreestima, así que la ruta encontrada es siempre la óptima para ese conductor.
- Con navegador, al entrar en una calle recalcula la ruta si la siguiente está muy congestionada (como mucho cada 90 s); los recálculos por
  paso tienen un cupo fijo, que no depende del reloj, para conservar la reproducibilidad.
- Si no llega al carril de su giro, elige otra calle posible y recalcula desde ella. Si lleva mucho rato esperando porque la calle de salida
  está llena (o cortada por un accidente), prueba otra salida del cruce que tenga sitio.
- **Central de tráfico** (`Trafico/CentralTrafico`): los vehículos le informan de su marcha, de los accidentes y de cuándo se despeja una
  calle; la central solo registra, y las rutas evitan las calles con todos sus carriles cortados.

## 5. Conducción

### Percibir, decidir, actuar

Cada vehículo solo percibe lo local: el vehículo de delante (en su vía o en las siguientes de su plan), las curvas y límites de su plan, la
señal de su cruce, los vehículos que pueden entrar en conflicto con él en el cruce y los de los carriles contiguos. Acelera y frena con el
**modelo IDM**; un obstáculo en la línea de detención (rojo, stop, ceda, peatones, falta de sitio al otro lado) se trata como un vehículo
parado ahí.

- **Velocidad deseada** en una vía: el límite por su factor (del 85 % al 120 %), con su variación lenta, y nunca más que su velocidad máxima.
- **Curvas:** velocidad = √(aceleración lateral · radio), con un radio de al menos 10 m en los giros de los cruces y de 20 m en las entradas
  y salidas de las rotondas. El vehículo frena antes, con su frenada normal, para llegar a esa velocidad al principio de la curva.
- **Frenadas:** levanta el pie (0,8-1,5 m/s²) si le sobra poca velocidad, frena con su frenada normal en los casos habituales, con la
  fuerte si no basta y con la de emergencia solo si no puede evitar el golpe de otro modo.
- **Arranque en cola:** parado detrás de otro, no arranca hasta que el hueco es algo mayor que la distancia que deja en parado (un 10 % más
  el agresivo y hasta un 60 % el prudente, con azar) y después tarda su tiempo de reacción: la cola arranca en cadena.

### Cambio de carril (MOBIL)

Cada medio segundo, más o menos, el vehículo compara con el IDM cómo iría en su carril y en el de al lado, y suma la parte que le importa
(su cortesía) de lo que ganan o pierden los vehículos afectados. Cambia si la ventaja supera su umbral y es seguro (huecos mínimos y sin
obligar al de atrás a frenar más de lo que su carácter acepta). A la izquierda se gira desde el carril izquierdo y a la derecha desde el
derecho: cuanto más cerca del cruce, más pesa ponerse en el carril de su giro. No cambia a menos de 12 m del cruce, ni casi parado si no es
obligado, ni en los segundos siguientes a otro cambio. En pantalla, el dibujo se desliza al carril nuevo en 2,5 s.

### Cruces

Sin gestor central: cada vehículo decide si entra con lo que ve.

1. **Semáforo:** para en rojo; en ámbar, si puede parar con comodidad.
2. **Stop:** alto completo y después la regla 3.
3. **Ceder el paso (aceptación de huecos):** para cada conflicto de su movimiento calcula cuánto tardaría en despejarlo y espera si otro
   vehículo con preferencia llegará antes de eso más el margen de su conductor. Los que ya están dentro del cruce se esperan siempre.
   Prioridad: calle prioritaria o semáforo por encima de la señalizada; girar a la izquierda, por debajo de seguir o girar a la derecha.
   **Con la misma prioridad pasa el que llegó antes a la línea** (a menos de 12 m) y, si llegaron a la vez, lo decide un **sorteo**: cada
   conductor sortea un número al entrar en cada calle y pasa el más alto.
4. **No bloquear el cruce:** no entra si no cabe entero en el carril de salida.
5. **Paciencia:** el hueco que exige baja hasta un 40 % tras 30 s esperando; pasada su paciencia, acepta huecos de 1,2 s (el que viene
   frenará al verle dentro).

Un vehículo parado detrás de otro, o uno con más prioridad que está esperando por otro motivo, no cuenta como tráfico que llega: si luego
arranca, verá al otro dentro del cruce y esperará.

**Rotondas:** los del anillo pasan primero; los que entran ceden con la misma regla de huecos. Nadie entra si más de la mitad del anillo
está ocupada.

### Percepción y choques

- Cada 0,3 s cada vehículo calcula dónde estará en los próximos 2 s si sigue como va, y mira a los vehículos de delante y de los lados. Si
  alguno va a quedar a menos de 2,6 m y le toca evitarlo (el otro va delante en su dirección, está parado o, si sus trayectorias se cruzan,
  viene por su derecha), frena antes del punto del golpe. Si no hay una derecha clara, decide el sorteo.
- En el cruce, como último recurso, frena si prevé chocar en el próximo segundo y medio con un vehículo que tiene preferencia sobre él.
- **Accidente:** dos vehículos que se solapan al menos 0,7 m con una velocidad relativa de al menos 29 km/h. Los implicados quedan detenidos
  5 minutos y sus carriles quedan cortados mientras tanto (nadie entra en ellos; los que querían ir por ahí buscan otra salida). Si otro
  choca contra ellos, se suma al accidente. Los umbrales y la duración son parámetros.

### Círculos de espera

Cada 2 s se buscan círculos de espera (A espera a B, que espera a ... A) entre vehículos parados hace más de 15 s. En cada uno, el que va
delante (el que menos camino tiene hasta el final de su vía; si empatan, el más agresivo, y si no, el sorteo) toma la iniciativa: durante 6 s
deja de esperar al que tiene delante y avanza despacio.

## 6. Control del tráfico

### Semáforos (`Mapa/Semaforo`)

- **Fases por aproximación:** en cada grupo de calles, fase adelantada (un sentido, para girar a la izquierda sin tráfico de frente), común
  (los dos sentidos; a la izquierda cediendo) y retrasada. Entre fases, ámbar de 3 s y todo rojo de 2 s para los que pierden el verde.
- **Adaptativos** (por defecto): dan verde mientras siguen llegando vehículos que pueden pasar (detectores a 60 m de la línea), lo cortan
  cuando no va a llegar nadie en 3 s o cuando los que esperan no caben al otro lado, y no lo dan a una calle donde no espera nadie; verde
  mínimo de 7 s y máximo de 60 s. Las fases de giro solo se usan si alguien quiere girar. **De tiempo fijo:** `--semaforos=fijos`.
- **Onda verde:** los semáforos de cada arteria comparten un ciclo de 90 s, con un desfase igual al tiempo que tarda en llegar a cada uno, a
  48 km/h, un vehículo que salió del primero; las arterias paralelas alternan el sentido de la onda.

### Guardias de tráfico (`Trafico/Guardia`, `ServicioGuardias`)

- Acuden solo a los **atascos graves** (por defecto, al menos 8 vehículos durante 3 minutos). Los cruces atascados cercanos forman una
  **zona** y se envía un guardia a cada uno (el libre más cercano); los que sobran amplían la zona a los cruces contiguos.
- Actúan en grupo y sincronizados: empiezan juntos y sus turnos cambian a la vez, en pulsos comunes. En cada turno dan paso a la
  aproximación con más cola y espera a la que le cabe salir, con preferencia a los movimientos que sacan vehículos de la zona; si nadie puede
  salir, ordenan **paso forzado** y los primeros de la cola pueden dar la **vuelta en U**. Si ni esa aproximación ni la de enfrente pueden
  girar a la izquierda, dan paso a las dos a la vez.
- Se van cuando las colas de la zona llevan un rato vacías (entre 2 y 30 minutos de actuación). No actúan en las rotondas.
- Por defecto hay cuatro por km², con sus bases repartidas por la ciudad.

### Atascos (`Trafico/DetectorAtascos`)

Cada 5 s se mide la velocidad media de cada calle y sentido. Un atasco empieza cuando, con al menos 4 vehículos, la media queda por debajo de
10 km/h durante 60 s (sin contar el tiempo en rojo del semáforo del final) y termina cuando supera 20 km/h durante 30 s. El detector mantiene
también el **factor de congestión** de cada calle que usan los navegadores.

### Peatones (`Trafico/ServicioPeatones`)

Hay pasos de peatones en las calles que llegan a los cruces (salvo autovía, ramales, túneles y rotondas), pegados al cruce; la línea de
detención queda 1 m antes de la franja. Los peatones llegan al azar (más en el centro que en las afueras, según la hora y menos con lluvia) y
cruzan a 1,35 m/s de media. Con semáforo cruzan con el verde de las calles paralelas a ellos; sin semáforo tienen preferencia, salvo ante un
vehículo que ya no puede frenar. Los vehículos no pasan por un paso con alguien en su carril o a punto de llegar a él.

### Radares

Uno por cada 3 km² (como mucho 100; parámetro), en arterias, avenida y autovía, a mitad de calle y lo más repartidos posible. Registran
todos los vehículos de todos los carriles y sentidos.

## 7. Paso de simulación y reproducibilidad

- **Paso en dos fases:** en cada paso, todos los vehículos **publican** su estado, **deciden** a la vez (en paralelo, cada uno solo escribe en
  sí mismo y solo lee de los demás lo publicado) y después **aplican** lo decidido uno tras otro, en orden (moverse de vía, cambiar de
  carril, calcular rutas). El resultado no depende del número de hilos (`--hilos=N`).
- **Azar propio** (`Nucleo/Aleatorio`, xoshiro256**): cada vehículo tiene su propia secuencia, derivada de la semilla y de su número, así que
  sus decisiones no dependen de las de los demás ni del orden en que se procesen.
- **Huella:** el modo sin pantalla imprime una huella del estado final; dos ejecuciones con los mismos datos dan la misma.

## 8. Dibujo e interfaz

- **Mapa por trozos** de 500 m con capas; Godot solo dibuja los que ve la cámara. Niveles de detalle según el zoom: de lejos, manzanas y
  arterias; al acercarse, calles locales y, de cerca, marcas viales, flechas del suelo, edificios y árboles.
- **Vehículos con MultiMesh:** un MultiMesh por modelo cuando se ven los dibujos y uno de puntos de color de lejos; solo se escriben los
  visibles. Semáforos, pasos de peatones, puertas y paradas se buscan en la parte visible con un índice espacial.
- **Flechas del suelo:** antes de cada cruce, las salidas que se pueden tomar desde ese carril; a lo largo de la calle, el sentido.
- **Luz:** con la ciudad del mundo y la fecha elegidas se calculan la salida y la puesta del sol; de noche el mapa se oscurece.
- **Interfaz:** pantalla inicial, barra del reloj (pausa, parar, menú, velocidad de 1× a 60×, hora y sol), tablas de la ciudad y del vehículo
  seguido, minimapa, nombre y datos de la calle o el cruce bajo el ratón y modo «solo datos» con una barra de avance.

## 9. Datos

El sistema solo escribe **datos en bruto**: ficheros TSV de elementos y hechos, sin medias, totales, gráficas ni mapas de calor. Con ellos
se puede obtener fuera del programa el tiempo de viaje y la velocidad por vehículo y motivo, las paradas y retrasos por cruce, las colas,
el flujo por calle, la ocupación de los lugares o la congestión. El contenido de cada fichero está en [Datos.md](Datos.md).

## 10. Pruebas

`Pruebas/` es un proyecto xUnit aparte (`dotnet test` desde esa carpeta): generador aleatorio, configuración, índice espacial, cotas y
rutas (contra Dijkstra en las ciudades fijas), central de tráfico, semáforos, conexiones, sorteo y preferencia en los cruces. La
reproducibilidad completa se comprueba con la huella del modo sin pantalla.

## 11. Créditos y licencia

- Programación y test: Claude, https://claude.ai
- Diseño vehículos: Gpt, https://chatgpt.com
- FDE, arnés y dirección: Vicente Simón, https://www.linkedin.com/in/vicentesimon

Licencia Apache 2.0 (`LICENSE`), con el fichero `NOTICE`, que debe conservarse en las copias y versiones derivadas.

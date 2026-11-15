# Ficheros de datos de un estudio

Referencia de los ficheros que escribe cada estudio: qué contiene cada fichero, qué significa cada columna, en qué unidades está y qué
valores puede tomar. Está pensada para que una persona o un agente pueda leer y analizar los datos sin consultar el código.

## 1. Convenciones comunes

- **Ubicación:** cada estudio crea la carpeta `Estudios/<nombre>/`. Si ya existe, añade un número: `<nombre>_2`, `<nombre>_3`...
- **Formato:** TSV (valores separados por tabuladores), UTF-8 con marca de orden de bytes (BOM), una cabecera en la primera línea, punto
  decimal y sin comillas. Los decimales llevan como mucho tres cifras. Un campo vacío significa «no aplica» o «sin valor».
- **Datos en bruto:** cada fila es un hecho o un elemento. No hay medias, totales ni resúmenes: el análisis se hace fuera del programa.
- **Tiempo:**
  - `Hora` (y las columnas `Hora...`): hora del día `HH:MM:SS` (en `conductores.tsv`, `HH:MM`). Si la simulación pasa de medianoche,
    vuelve a `00:00:00`.
  - `T_s` (y las columnas `T_...`): segundos desde el inicio de la simulación (la `HoraInicio` de `parametros.tsv`), con decimales. Es la
    columna que hay que usar para ordenar y calcular duraciones. **Incluye el calentamiento:** los minutos de calentamiento
    (`Calentamiento_min`) se simulan pero no se registran, así que los primeros hechos registrados tienen `T_s` ≥ 60 · `Calentamiento_min`.
- **Distancias** en metros y **velocidades** en km/h (salvo que la columna diga otra cosa).
- **Coordenadas** (`X`, `Y`, `X1`...): metros, con el origen en el centro de la ciudad, `X` hacia el este e `Y` hacia el **sur** (crece hacia
  abajo en el mapa). La ciudad ocupa de `-ancho/2` a `+ancho/2` y de `-alto/2` a `+alto/2` (`Ciudad_ancho_m`, `Ciudad_alto_m`).
- **Booleanos:** `1` sí, `0` no.
- **Listas** dentro de un campo: separadas por comas (`Entradas`, `Salidas`) o por punto y coma (`Recorrido`, `CarrilesCortados`).

## 2. Identificadores

Son estables: las ciudades son fijas y los nombres se guardan con ellas, así que el mismo nombre es el mismo elemento en todos los estudios
de una ciudad.

| Elemento | Forma | Ejemplos |
|---|---|---|
| Calle de la cuadrícula | `H-fila-columna` (horizontal) o `V-fila-columna` (vertical); fila de arriba abajo y columna de izquierda a derecha | `H-8-15`, `V-11-16` |
| Autovía de circunvalación | `C-n`, en sentido horario desde la esquina de arriba a la izquierda | `C-1`, `C-24` |
| Enlace de la autovía | ramales `E-n-salida-k` y `E-n-entrada-k` | `E-8-salida-1` |
| Carretera de acceso al exterior | `EXT-n` | `EXT-2` |
| Anillo de una rotonda | `R-fila-columna-k` | `R-5-7-2` |
| Calle sin nombre | `T<número interno>` | `T812` |
| Cruce de la cuadrícula | `X-fila-columna` | `X-10-16` |
| Cruce de la circunvalación | `CX-n` | `CX-10` |
| Cruce de un enlace | `E-n-1` (lado de la autovía) y `E-n-2` (lado de la calle) | `E-8-1` |
| Cruce del anillo de una rotonda | `RX-fila-columna-k` | `RX-5-7-1` |
| Final de una carretera de acceso (borde del mapa) | `PX-n` | `PX-2` |
| Cruce sin nombre | `N<número interno>` | `N231` |
| Lugar | `<Tipo>-n` | `Vivienda-27`, `Oficinas-1`, `Fabrica-4` |
| Parada de autobús | `Parada-L<línea>-<ida o vuelta>-<n>` | `Parada-L1-ida-5` |
| Puerta de la ciudad | `Exterior-n` | `Exterior-3` |
| Semáforo (una aproximación) | `S-<cruce>-<calle>` | `S-X-4-4-V-3-4` |
| Radar | `RD-n` | `RD-3` |
| Paso de peatones | `P-<cruce>-<calle>` | `P-X-2-4-V-2-4` |
| Vehículo | número entero (`Coche`) | `14430` |

**Vehículos:** los números van de `0` a `VehiculosMaximos - 1` (la flota). Los autobuses de línea llevan los números siguientes a la flota.

**Sentido de circulación** (`Direccion`):

| Valor | Significado |
|---|---|
| `OE` | de oeste a este |
| `EO` | de este a oeste |
| `NS` | de norte a sur |
| `SN` | de sur a norte |
| `horario`, `antihorario` | en tramos curvos: esquinas de la circunvalación, ramales y anillos de rotonda (las rotondas son siempre `antihorario`) |
| `curva` | tramo con otra forma (poco frecuente) |

Una calle de doble sentido aparece en los datos con su nombre y uno de los dos sentidos; para identificar un sentido concreto hay que usar
el par `Calle` + `Direccion`.

**Carriles:** se numeran desde `1` = el carril izquierdo (el más interior) en el sentido de circulación.

**Tipos de lugar** (`Tipo`, `TipoOrigen`, `TipoDestino`): `Vivienda`, `Oficinas`, `CentroComercial`, `Gimnasio`, `Colegio`, `Hospital`,
`Restaurante`, `Gasolinera`, `Parking`, `Fabrica`, `Parada` (parada de autobús, solo para autobuses) y `Exterior` (puerta de la ciudad: el
vehículo sale del mapa o entra en él por una carretera de acceso).

**Tipo de conductor** (`Tipo`, `Conductor`, `Conductor1`, `Conductor2`), según su agresividad `a` (de −2 a +2):

| Valor | Agresividad |
|---|---|
| `temeroso` | a ≤ −1,2 |
| `prudente` | −1,2 < a ≤ −0,4 |
| `normal` | −0,4 < a < 0,4 |
| `agresivo` | 0,4 ≤ a < 1,2 |
| `temerario` | a ≥ 1,2 |

## 3. Ficheros

| Fichero | Una fila por | Se escribe |
|---|---|---|
| `parametros.tsv` | parámetro del estudio | al empezar |
| `calles.tsv` | calle y sentido | al empezar |
| `giros_prohibidos.tsv` | giro prohibido | al empezar |
| `conductores.tsv` | vehículo | al empezar |
| `viajes.tsv` | viaje terminado | durante el estudio |
| `lugares.tsv` | hecho en un lugar (llegar, aparcar, salir...) | durante el estudio |
| `semaforos.tsv` | ciclo completo de una aproximación con semáforo | durante el estudio |
| `radares.tsv` | paso de un vehículo por un radar | durante el estudio |
| `peatones.tsv` | persona que cruza | durante el estudio |
| `accidentes.tsv` | hecho de un accidente (inicio, suma, fin) | durante el estudio |
| `guardias.tsv` | hecho de un guardia de tráfico | durante el estudio |
| `atascos.tsv` | atasco | al terminar |

### 3.1 `parametros.tsv`

Columnas: `Parametro`, `Valor`. Recoge todo lo necesario para repetir el estudio.

| Parámetro | Significado |
|---|---|
| `Ciudad` | ciudad fija, con su tamaño (`Ciudad 1 (3 x 2 km)`) |
| `Ciudad_ancho_m`, `Ciudad_alto_m` | tamaño del mapa en metros |
| `Semilla` | semilla de la simulación: con la misma semilla y los mismos parámetros, los datos son idénticos |
| `HoraInicio` | hora del día en que empieza la simulación (`HH:MM`); `T_s = 0` corresponde a ella |
| `VehiculosMaximos` | vehículos que existen en total (flota más autobuses) |
| `Semaforos` | `adaptativos` o `fijos` |
| `FasesGiroIzquierda` | 1 si los semáforos tienen fase propia para girar a la izquierda |
| `OndaVerde` | 1 si los semáforos de las arterias están coordinados en onda verde |
| `Lluvia` | 1 si llueve durante todo el estudio |
| `ProporcionMotos`, `ProporcionCamiones` | parte de la flota que son motos y camiones de reparto |
| `LineasAutobus`, `Autobuses` | líneas de autobús y autobuses en servicio |
| `PuertasCiudad` | carreteras de acceso al exterior (puertas `Exterior-n`) |
| `ProporcionDeFuera` | parte de la flota que vive fuera y entra a trabajar |
| `ProporcionTrabajaFuera` | parte de los trabajadores de la ciudad que trabaja fuera |
| `ProporcionDePaso` | parte de la flota que es tráfico de paso (entra por una puerta y sale por otra) |
| `ProporcionCamionesDeFuera` | parte de los camiones de reparto con la base fuera |
| `Peatones` | 1 si hay peatones |
| `FactorPlazas` | multiplicador de las plazas de aparcamiento de todos los lugares |
| `HorasSimuladas` | horas registradas (después del calentamiento) |
| `Calentamiento_min` | minutos iniciales simulados sin registrar |
| `Sol_ciudad`, `Sol_fecha`, `Sol_salida`, `Sol_puesta` | ciudad del mundo y fecha usadas para el sol, y horas de salida y puesta (solo si se eligieron) |
| `FactorEstancia` | multiplicador del tiempo que pasan los vehículos dentro de los lugares |
| `ProporcionNavegador` | parte de los conductores que usa navegador (evita las calles atascadas) |
| `Guardias`, `Radares`, `PasosPeatones` | número de guardias de tráfico, radares y pasos de peatones |
| `Peatones_porHora_centro`, `Peatones_porHora_afueras` | peatones por hora y paso en hora punta, en el centro y en las afueras |
| `Nodos`, `Tramos`, `Carriles`, `Lugares` | tamaño del grafo de la ciudad |
| `Atasco_velocidadMaxima_kmh`, `Atasco_duracionMinima_s`, `Atasco_cochesMinimos` | condiciones para empezar un atasco (ver `atascos.tsv`) |
| `Atasco_velocidadFin_kmh`, `Atasco_duracionFin_s` | condiciones para darlo por terminado |
| `Guardias_cochesMinimosAtascoGrave`, `Guardias_duracionMinimaAtascoGrave_s` | condiciones para que acudan los guardias |
| `Porcentaje_indicado_hHH` | porcentaje de la flota que debe empezar un viaje en la hora `HH`, según la curva elegida |
| `Porcentaje_aplicado_hHH` | el mismo porcentaje con la variación al azar que aplica la simulación (la que se usa) |

### 3.2 `calles.tsv`

Una fila por calle **y sentido** (una calle de doble sentido son dos filas).

| Columna | Significado |
|---|---|
| `Nombre` | nombre de la calle |
| `LimiteKmh` | límite de velocidad |
| `Tipo` | `Local`, `Colectora`, `Arteria`, `Autovia`, `Rotonda` (anillo), `Puente` o `Tunel` |
| `Direccion` | sentido de esta fila |
| `Carriles` | carriles en este sentido |
| `LongitudM` | longitud del eje de la calle, de cruce a cruce |
| `CruceInicio`, `CruceFin` | cruce donde empieza y donde acaba en este sentido |
| `Entradas` | calles desde las que se puede entrar en esta (las que llegan a `CruceInicio`, sin las de giro prohibido) |
| `Salidas` | calles a las que se puede ir desde esta (las que salen de `CruceFin`, sin las de giro prohibido) |
| `X1`, `Y1`, `X2`, `Y2` | coordenadas de `CruceInicio` y de `CruceFin` |

### 3.3 `giros_prohibidos.tsv`

| Columna | Significado |
|---|---|
| `Cruce` | cruce |
| `Desde` | calle por la que se llega |
| `Hasta` | calle a la que no se puede ir |
| `Giro` | `Izquierda`, `Recto` o `Derecha` |

### 3.4 `conductores.tsv`

Una fila por vehículo, con su personalidad y su rutina. Los valores de conducción son **en seco y de día**: con lluvia o de noche, el
conductor los ajusta durante la simulación (más separación, menos velocidad, más reacción...).

| Columna | Significado |
|---|---|
| `Coche` | número del vehículo |
| `Clase` | `Coche`, `Moto`, `Camion` o `Autobus` |
| `Modelo` | dibujo del vehículo: letra de la clase (`A` automóvil, `M` moto, `C` camión, `B` autobús) y número |
| `LargoM` | longitud del vehículo |
| `Tipo` | tipo de conductor (ver la sección 2) |
| `Agresividad` | de −2 (temeroso) a +2 (temerario); 0 es un conductor normal. Desplaza a la vez todos los parámetros siguientes |
| `ExcesoLimite` | fracción sobre el límite a la que quiere circular (−0,15 = un 15 % por debajo; 0,2 = un 20 % por encima) |
| `TiempoSeparacionS` | tiempo de separación con el vehículo de delante en marcha (s) |
| `DistanciaMinimaM` | distancia con el de delante cuando está parado |
| `AceleracionMax` | aceleración máxima (m/s²) |
| `FrenadaComoda` | frenada normal (m/s²) |
| `AceleracionLateral` | aceleración lateral que tolera en los giros (m/s²): fija la velocidad en las curvas |
| `TiempoReaccionS` | tiempo de reacción al arrancar (s) |
| `HuecoCriticoBaseS` | hueco mínimo que exige para cruzar o incorporarse delante de otro (s) |
| `Cortesia` | cuánto le importa perjudicar a los demás al cambiar de carril: 0 nada, 1 mucho |
| `UmbralCambioCarril` | ventaja mínima que exige para cambiar de carril sin necesidad (m/s²) |
| `UsaNavegador` | 1 si evita las calles atascadas al elegir ruta |
| `PacienciaS` | segundos de espera en un cruce tras los que acepta huecos más pequeños |
| `Perfil` | `Trabajador`, `TrabajadorConColegio` (lleva antes a los niños al colegio), `SinTrabajo`, `Reparto` (camión de reparto), `Autobus` o `Transito` (tráfico de paso) |
| `Casa` | lugar donde vive (una `Exterior-n` si vive fuera); en los camiones, su base; en los autobuses, su primera parada |
| `Trabajo` | lugar de trabajo (vacío si no trabaja; `Exterior-n` si trabaja fuera) |
| `Colegio` | colegio al que lleva a los niños (vacío si no) |
| `HoraSalida` | hora a la que sale hacia el trabajo (`HH:MM`; vacía si no trabaja) |
| `HoraFinJornada` | hora a la que termina su jornada (`HH:MM`; vacía si no trabaja) |
| `ComeFuera` | 1 si sale a comer durante la jornada (vacío si no trabaja) |

### 3.5 `viajes.tsv`

Una fila por viaje **terminado** (el vehículo ha llegado a la puerta de su destino). Los viajes que siguen en curso al acabar el estudio no
aparecen.

| Columna | Significado |
|---|---|
| `Coche` | número del vehículo |
| `Conductor` | tipo de conductor |
| `Origen`, `TipoOrigen` | lugar de donde salió y su tipo |
| `Destino`, `TipoDestino` | lugar al que llegó y su tipo |
| `Motivo` | `Trabajo`, `Colegio` (dejar a los niños), `Comida`, `Casa` (volver a casa), `Otro` (compras, ocio, recados), `Reparto` y `Base` (camiones), `Linea` (autobuses, de parada a parada) o `Transito` (tráfico de paso) |
| `HoraSalida`, `HoraLlegada` | hora del día de salida a la calle y de llegada a la puerta del destino |
| `T_salida`, `T_llegada` | lo mismo en segundos de simulación |
| `DistanciaM` | distancia recorrida |
| `Paradas` | veces que se detuvo (velocidad por debajo de 0,1 m/s), contando la de la salida |
| `ParadoS` | segundos que estuvo detenido |
| `Recalculos` | veces que recalculó la ruta durante el viaje |
| `Choques` | accidentes en los que se vio implicado durante el viaje |
| `Recorrido` | calles por las que pasó, en orden, con el momento de entrada: `Calle/Direccion@T_s;Calle/Direccion@T_s;...` |

`Recorrido` está vacío en los viajes que empezaron durante el calentamiento (antes de que el estudio empezara a registrar); en ellos
`T_salida` es anterior al inicio del registro.

### 3.6 `lugares.tsv`

Una fila por hecho en un lugar. Al llegar a la puerta, el vehículo sale de la calle; si hay plaza, maniobra hasta ella (fuera de la calzada)
y aparca; si no, espera dentro en una cola, sin estorbar al tráfico.

| Columna | Significado |
|---|---|
| `Hora`, `T_s` | momento del hecho |
| `Lugar`, `Tipo` | lugar y su tipo |
| `Evento` | `Llega` (llega a la puerta y sale de la calle), `SinPlaza` (no hay plaza: pasa a la cola), `ConPlaza` (se libera una plaza para el primero de la cola), `Aparca` (termina la maniobra y empieza su estancia) o `Sale` (sale a la calle para empezar un viaje) |
| `Coche` | número del vehículo |
| `DentroDespues` | vehículos con plaza en el lugar después del hecho |
| `Capacidad` | plazas del lugar (las puertas `Exterior` no tienen límite: llevan un número muy grande) |
| `Esperando` | vehículos en la cola de espera de plaza |

### 3.7 `semaforos.tsv`

Una fila por **ciclo completo** de cada calle que llega a un cruce con semáforo: desde que se pone en verde hasta que se pone en rojo. Solo
se registran los ciclos cuyo verde empezó con el estudio en marcha, y no se registran mientras un guardia controla el cruce.

| Columna | Significado |
|---|---|
| `Nombre` | semáforo: `S-<cruce>-<calle>` |
| `Calle`, `Direccion` | calle y sentido que llegan al cruce |
| `HoraVerde` | hora a la que se puso en verde |
| `CochesVerde` | vehículos que entraron en el cruce desde esa calle durante el verde |
| `HoraNaranja` | hora a la que se puso en ámbar |
| `CochesNaranja` | vehículos que entraron durante el ámbar |
| `HoraRojo` | hora a la que se puso en rojo |

### 3.8 `radares.tsv`

Una fila por cada vehículo que pasa por un radar. Cada radar está a mitad de una calle y controla todos sus carriles y sentidos.

| Columna | Significado |
|---|---|
| `Nombre` | radar |
| `Calle`, `Direccion`, `Carril` | calle, sentido y carril por el que pasa |
| `Hora`, `T_s` | momento del paso |
| `VelocidadKmh` | velocidad del vehículo al pasar |
| `Coche` | número del vehículo |

### 3.9 `peatones.tsv`

Una fila por persona que termina de cruzar.

| Columna | Significado |
|---|---|
| `Paso` | paso de peatones |
| `Cruce`, `Calle` | cruce junto al que está y calle que atraviesa |
| `ConSemaforo` | 1 si el cruce tiene semáforo (cruza con el verde de las calles paralelas a ella); 0 si no (tiene preferencia) |
| `Hora` | hora a la que llegó a la acera |
| `T_llegada`, `T_inicio`, `T_fin` | segundos de simulación en que llegó a la acera, empezó a cruzar y terminó |
| `EsperaS` | segundos que esperó en la acera (`T_inicio − T_llegada`) |
| `LargoM` | anchura de la calzada que cruzó |

### 3.10 `accidentes.tsv`

Un accidente es un golpe entre dos vehículos que se solapan lo bastante y con velocidad relativa suficiente (los umbrales son parámetros de
la línea de comandos). Los implicados se quedan detenidos un tiempo fijo y sus carriles quedan cortados mientras tanto. Cada accidente
tiene una fila `Inicio`, una fila `Suma` por cada vehículo que choca después contra uno de los accidentados, y una fila `Fin`.

| Columna | Significado |
|---|---|
| `Hora`, `T_s` | momento del hecho |
| `Evento` | `Inicio`, `Suma` o `Fin` |
| `Accidente` | número del accidente |
| `Lugar` | cruce con su control (`X-9-4 (Semaforo)`; el control es `Semaforo`, `Stop`, `Ceda`, `Rotonda` o `Ninguno`) o calle y sentido (`H-8-15 OE`) |
| `X`, `Y` | coordenadas del accidente |
| `Coche1`, `Coche2` | vehículos del golpe. En `Suma`, `Coche1` es el que se suma y `Coche2` el accidentado contra el que choca |
| `Velocidad1Kmh`, `Velocidad2Kmh` | velocidad de cada uno justo antes del golpe |
| `Via1`, `Via2` | dónde estaba cada uno: `Calle/Direccion carril N`, o dentro de un cruce `cruce <cruce>: <calle de entrada> a <calle de salida> (<giro>)` con giro `Izquierda`, `Recto` o `Derecha` |
| `Conductor1`, `Conductor2` | tipo de conductor de cada uno |
| `SolapeM` | cuánto se solaparon los dos vehículos |
| `VelRelativaKmh` | velocidad relativa del golpe |
| `CochesImplicados` | solo en `Fin`: vehículos implicados en total |
| `CarrilesCortados` | carriles cortados por el accidente: `Calle/Direccion carril N;...` |

En `Fin` solo se rellenan `Hora`, `T_s`, `Evento`, `Accidente`, `Lugar`, `X`, `Y`, `CochesImplicados` y `CarrilesCortados`.

### 3.11 `guardias.tsv`

Los guardias de tráfico acuden en grupo (una **zona** de cruces cercanos) a los atascos graves. En su cruce sustituyen al semáforo, al stop o
al ceda: dan paso a una calle cada vez (un **turno**) y paran a las demás.

| Columna | Significado |
|---|---|
| `Evento` | `LLEGADA` (llega al cruce), `INICIO` (la zona empieza a actuar), `TURNO` (termina un turno de paso) o `FIN` (deja el cruce) |
| `Hora`, `T_s` | momento del hecho |
| `Guardia` | número del guardia |
| `Zona` | número de la zona (−1 si no tiene) |
| `Cruce` | cruce que controla |
| `Calle`, `Direccion` | solo en `TURNO`: calle y sentido a los que dio paso |
| `Orden` | solo en `TURNO`: `Pasar` (paso libre si caben al otro lado) o `PasarForzado` (paso aunque no quepan; los primeros de la cola pueden dar la vuelta). Si dio paso a la vez a la calle de enfrente, añade ` con <calle>` |
| `DuracionS` | en `TURNO`, duración del turno; en `FIN`, duración de toda la actuación |
| `CochesPasaron` | en `TURNO`, vehículos que entraron en el cruce durante el turno; en `FIN`, el total de la actuación |
| `VueltasU` | en `TURNO`, vueltas en U ordenadas durante el turno; en los demás, el total del guardia |
| `ColaInicio`, `ColaFin` | solo en `TURNO`: vehículos en cola en la calle con paso al empezar y al acabar el turno |
| `GuardiasZona`, `CrucesZona` | guardias y cruces de su zona |

### 3.12 `atascos.tsv`

Se escribe al terminar el estudio. Un atasco empieza en una calle y sentido cuando, con al menos `Atasco_cochesMinimos` vehículos, la
velocidad media queda por debajo de `Atasco_velocidadMaxima_kmh` durante `Atasco_duracionMinima_s` segundos (y el semáforo del final no está
en rojo). Termina cuando la velocidad media supera `Atasco_velocidadFin_kmh` durante `Atasco_duracionFin_s` segundos.

| Columna | Significado |
|---|---|
| `Calle`, `Direccion` | calle y sentido |
| `HoraIni`, `HoraFin` | hora de inicio y de fin (vacía si seguía al acabar el estudio) |
| `Coches` | máximo de vehículos en el atasco |
| `T_ini`, `T_fin` | inicio y fin en segundos de simulación (`T_fin` vacío si seguía) |

## 4. Lotes

Un lote (`--lote=fichero.tsv`) crea un estudio por fila, cada uno en su carpeta, y además `Estudios/_lotes/`:

- `<Nombre>.log`: lo que el estudio escribió por la consola.
- `resumen_lote.tsv`: columnas `Estudio`, `Carpeta`, `Codigo` (código de salida del proceso: 0 si terminó bien) y `SegundosProceso`
  (segundos reales que tardó). No contiene resultados de la simulación.

## 5. Relaciones entre ficheros

- `Coche` enlaza `viajes`, `lugares`, `radares`, `accidentes` (`Coche1`, `Coche2`) con `conductores`.
- `Calle` + `Direccion` enlaza `radares`, `semaforos`, `atascos`, `guardias`, `Recorrido` de `viajes` y `CarrilesCortados` de `accidentes`
  con `calles` (`Nombre` + `Direccion`).
- `Cruce` enlaza `giros_prohibidos`, `guardias`, `peatones` y el `Lugar` de `accidentes` con `CruceInicio` y `CruceFin` de `calles`.
- `Lugar`, `Origen` y `Destino` enlazan `lugares`, `viajes` y la rutina de `conductores` (`Casa`, `Trabajo`, `Colegio`).
- `T_s` (y `T_...`) permite ordenar en el tiempo los hechos de todos los ficheros.

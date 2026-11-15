# Simulación de tráfico: instrucciones de uso

## 1. Requisitos

- **Godot 4.7.2 o posterior, versión «mono» (con C#)**, para Windows. No hace falta instalarlo: basta con descomprimirlo en una carpeta. Trae dos ejecutables: `Godot_v<versión>-stable_mono_win64.exe` (con ventana) y `Godot_v<versión>-stable_mono_win64_console.exe` (igual, pero muestra los mensajes en la consola; es el que se usa sin pantalla y en los lotes).
- **.NET SDK 9 o posterior** (para compilar el código C#). Se comprueba con `dotnet --version`. Los paquetes de Godot se descargan solos de nuget.org al compilar la primera vez.

Todos los ejemplos se escriben **desde la carpeta del proyecto** (la que tiene `project.godot`). En ellos, `godot` es el ejecutable con ventana y `godot-consola` el de consola: escribe su ruta completa, o pon la carpeta de Godot en el `PATH` y crea esos dos nombres como alias o lanzadores.

## 2. Compilar

Solo la primera vez y cada vez que cambie el código:

```
dotnet build
```

Debe terminar con `0 Errores` (o `0 Error(s)`).

## 3. Ejecutar con la pantalla inicial (lo normal)

```
godot --path .
```

En la pantalla inicial se elige:

| Opción | Qué es |
|---|---|
| Ciudad | Una de las cinco ciudades fijas (de 3 × 2 km a 10 × 10 km) |
| Hora de inicio, horas a simular | Desde qué hora del día y cuántas horas (máximo 24) |
| Calentamiento | Minutos iniciales que se simulan sin guardar datos |
| Vehículos (flota de la ciudad) | Todos los vehículos de la ciudad (se proponen unos 3000 por km²): 86 % coches, 9 % motos y 5 % camiones de reparto; los autobuses de línea se añaden aparte |
| Semilla | Con la misma semilla y los mismos datos, la simulación es idéntica |
| Conductores con navegador | Porcentaje que evita los atascos al calcular su ruta |
| Radares | Cuántos radares hay (de 0 a 100; se propone uno por cada 3 km²) |
| Sol: ciudad del mundo y fecha | Para la hora de salida y puesta del sol (de noche se oscurece el mapa); por defecto, Teruel y la fecha de hoy |
| Estudio | Nombre de la carpeta donde se guardan los datos (en `Estudios\`) |
| Semáforos | Adaptativos (se ajustan al tráfico) o de tiempo fijo; en los dos casos, con fase propia para girar a la izquierda y onda verde en las arterias |
| Condiciones | Lluvia durante todo el estudio (frenadas, velocidad y distancias de mojado) y peatones en los pasos de los cruces |
| Exterior (%) | Parte de la flota que vive fuera y entra a trabajar, de los trabajadores que trabajan fuera y de tráfico de paso (entra por una puerta de la ciudad y sale por otra) |
| Modo | «Solo datos»: sin gráficos, lo más rápido posible (ver el apartado 5) |
| Viajes por hora | Qué parte de la flota empieza un viaje en cada hora (la curva por defecto suma unos 3 viajes por vehículo y día; el sistema varía cada hora hasta un 15 %) |

Después, **Iniciar simulación**.

## 4. Mientras se ve la simulación

**Barra del reloj** (arriba a la derecha): pausa / reanudar, parar (cierra el estudio y deja los ficheros), menú (vuelve a la pantalla inicial sin cerrar el programa) y la velocidad, de tiempo real (1×) a 60×.

| Tecla o ratón | Acción |
|---|---|
| Rueda | Zoom (hacia donde apunta el ratón) |
| Arrastrar, o W A S D / flechas | Mover el mapa (con Mayúsculas, más rápido) |
| Clic en el minimapa | Saltar a ese punto |
| C | Seguir a un coche (cada pulsación, al siguiente) |
| Esc | Dejar de seguir |
| A | Ir al siguiente accidente en curso |
| 1, 2, 3, 4 | Velocidad 1×, 10×, 30×, 60× |
| Espacio | Pausa |
| M | Terminar y volver al menú |
| Q | Terminar y salir del programa (el estudio se guarda) |
| Ratón sobre una calle o cruce | Muestra su nombre y sus datos |

## 5. Generar datos sin gráficos

### Desde la ventana: modo «solo datos»

Marca **Modo: Solo datos** en la pantalla inicial (hace falta marcar también «Guardar datos como»). No se dibuja nada: solo una barra de avance. Al terminar se puede abrir la carpeta de datos o volver al menú. También se puede cancelar: se guarda lo hecho hasta ese momento.

### Desde la consola: un estudio

```
godot-consola --headless --path . -- --ciudad=1 --coches=18000 --hora=6 --horas=12 --calentamiento=15 --estudio=mi_estudio
```

Todo lo que va después de `--` son opciones de la simulación (lista completa en el apartado 7). Sin `--horas` se simula un día entero.

### Desde la consola: varios estudios a la vez (lote)

1. Crea un fichero de texto separado por tabuladores, por ejemplo `Estudios\mi_lote.tsv`:

   ```
   Nombre	Ciudad	Semilla	Vehiculos	HoraInicio	Horas	Calentamiento	Navegador	Lugar	Fecha	Curva
   manana	1	1-4	18000	7	3	15	60	Teruel	6-21
   tarde	2	5	72000	17	4	15	60
   ```

   Solo `Nombre` es obligatoria; las columnas vacías toman el valor por defecto. `Semilla` admite un rango (`1-4` crea cuatro estudios: `manana_s1` ... `manana_s4`). `Curva` son 24 porcentajes separados por comas, o uno solo para todas las horas. Las líneas que empiezan por `#` se ignoran.

2. Lánzalo:

   ```
   godot-consola --headless --path . -- --lote=Estudios\mi_lote.tsv --paralelo=3
   ```

   Cada estudio va en su propio proceso; `--paralelo` dice cuántos a la vez (por defecto, los núcleos del equipo menos uno). La salida de cada uno queda en `Estudios\_lotes\<Nombre>.log` y la lista de estudios en `Estudios\_lotes\resumen_lote.tsv`.

## 6. Dónde quedan los datos

Cada estudio crea `Estudios\<nombre>\` (si ya existe, añade un número: `<nombre>_2`...). Todos los ficheros son TSV (separados por tabuladores, UTF-8, punto decimal, con cabecera) y contienen **datos en bruto**: el análisis lo haces tú. Cada columna, sus unidades y sus valores posibles están en [Datos.md](Datos.md).

| Fichero | Contenido |
|---|---|
| `parametros.tsv` | Todo lo necesario para repetir el estudio |
| `calles.tsv` | Una fila por calle y sentido, con coordenadas y calles de entrada y salida |
| `giros_prohibidos.tsv` | Giros que no se pueden hacer en cada cruce |
| `conductores.tsv` | Un vehículo por fila: clase (coche, moto, camión, autobús), modelo, personalidad (valores en seco y de día) y rutina: perfil, casa o base, trabajo, colegio, hora de salida y de fin de jornada |
| `viajes.tsv` | Un viaje por fila, con su motivo (Trabajo, Colegio, Comida, Casa, Otro; Reparto y Base los camiones; Linea los autobuses) y su recorrido calle a calle |
| `lugares.tsv` | Llegadas, esperas de plaza, aparcamientos y salidas de los lugares |
| `semaforos.tsv` | Cada ciclo de cada semáforo y los coches que pasaron |
| `radares.tsv` | Cada coche que pasa por un radar, con su velocidad |
| `atascos.tsv` | Atascos detectados |
| `guardias.tsv` | Actuación de los guardias de tráfico |
| `accidentes.tsv` | Inicio, coches que se suman y fin de cada accidente, con los carriles cortados |
| `peatones.tsv` | Una persona que cruza por fila: paso, cruce, calle, si hay semáforo, llegada, inicio y fin del cruce, espera |

## 7. Todas las opciones de línea de comandos

Van después de `--` (por ejemplo, `godot --path . -- --ciudad=1`). Las de uso normal:

| Opción | Qué hace |
|---|---|
| `--ciudad=N` | Ciudad fija 1 a 5 |
| `--coches=N` | Vehículos de la flota (sin contar los autobuses) |
| `--hora=H` | Hora de inicio (por ejemplo `8` o `7.5`) |
| `--horas=N` | Horas a simular |
| `--calentamiento=MIN` | Minutos iniciales sin guardar datos |
| `--semilla=N` | Semilla |
| `--estudio=NOMBRE` | Guarda los datos en `Estudios\NOMBRE\` |
| `--curva=a,b,...` | 24 porcentajes de la flota que empiezan un viaje en cada hora (o uno para todas) |
| `--porcentaje=P` | Mismo porcentaje de viajes todas las horas, sin variación al azar |
| `--navegador=0.6` | Proporción de conductores con navegador (0 a 1) |
| `--lugar=Teruel`, `--fecha=6-21` | Ciudad del mundo y fecha para el sol |
| `--semaforos=adaptativos` / `--semaforos=fijos` | Tipo de semáforos |
| `--lluvia` | Llueve durante todo el estudio |
| `--sinpeatones` | Sin peatones |
| `--motos=0.09`, `--camiones=0.05` | Proporción de motos y de camiones de reparto en la flota |
| `--sinautobuses` | Sin autobuses de línea |
| `--de-fuera=0.15`, `--trabajan-fuera=0.05`, `--de-paso=0.05` | Tráfico con el exterior: flota que vive fuera, trabajadores de la ciudad que trabajan fuera y tráfico de paso |
| `--guardias=N` | Número de guardias de tráfico (por defecto, 4 por km²) |
| `--radares=N` | Número de radares (0 a 100) |
| `--plazas=1` | Multiplica las plazas de aparcamiento de todos los lugares |
| `--estancia=1` | Multiplica el tiempo que pasan los coches dentro de los lugares |
| `--solo-datos` | Modo «solo datos» con ventana (barra de avance) |
| `--lote=fichero.tsv`, `--paralelo=N` | Lote de estudios en paralelo |
| `--hilos=N` | Hilos con que se mueven los coches (por defecto, los núcleos hasta 6). No cambia los resultados: con cualquier número de hilos salen idénticos |
| `--sinmenu`, `--menu` | Saltar o forzar la pantalla inicial |

Para experimentos y comparaciones:

| Opción | Qué hace |
|---|---|
| `--sinaccidentes` | No detecta choques |
| `--sinfasegiro`, `--sinonda` | Semáforos sin fase de giro a la izquierda o sin onda verde |
| `--probar-rutas` | Comprueba que hay ruta entre los lugares de la ciudad y sale |
| `--accidente-s=300` | Duración de un accidente, en segundos |
| `--choque-vel=29`, `--choque-solape=0.7` | Umbrales para contar un golpe como accidente (km/h y m) |
| `--sinforzado`, `--sinvuelta`, `--sinopuesta` | Desactivan órdenes de los guardias |
| `--lateral=2.2`, `--lateral-rotonda=3.5` | Aceleración lateral en giros y en rotondas (m/s²) |
| `--rotondas=1` | Al generar una ciudad nueva, multiplica la cantidad de rotondas |
| `--tam=2000` | Usa una ciudad generada al momento, de ese tamaño en metros, en vez de una fija |
| `--hasta=SEGUNDOS` | Modo sin pantalla hasta ese segundo |
| `--diagnostico` | Mensajes de depuración por la consola (medias, frenadas, arranques...) |
| `--sim=10` | Velocidad inicial de la simulación |
| `--zoom=4`, `--pos=x,y`, `--seguir` | Zoom, posición inicial y seguir un coche |
| `--captura=ruta.png`, `--espera=8` | Hace una captura a los N segundos y cierra |
| `--bench=5` | Mide los fotogramas por segundo |
| `--generar-ciudades` | Vuelve a generar las cinco ciudades fijas (**las sobrescribe**) |

## 8. Problemas frecuentes

- **No arranca o falta algo de C#:** compila antes (apartado 2) y usa el Godot «mono».
- **«Falta el fichero de esa ciudad»:** genera las ciudades una vez con `godot-consola --headless --path . -- --generar-ciudades`.
- **El modo «solo datos» no empieza:** marca también «Guardar datos como».
- **La barra dice «el equipo llega a x...»:** el ordenador no da para la velocidad pedida; la simulación es la misma, solo va más lenta.
- **Más detalle:** el diseño del simulador está en [Proyecto.md](Proyecto.md) y el contenido de los ficheros de datos en [Datos.md](Datos.md).

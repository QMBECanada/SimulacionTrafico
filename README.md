# Simulación de tráfico

![Simulación de tráfico](Assets/portada/portada.png)

Simulador de tráfico urbano visto desde arriba, hecho con Godot 4 y C#. Su objetivo es **producir datos de circulación** de ciudades fijas: cada estudio guarda en ficheros TSV, en bruto, todo lo que pasa (viajes, semáforos, radares, atascos, accidentes...), y el análisis lo hace quien los usa.

Cada coche es un agente autónomo con su propia forma de conducir: quiere llegar a su destino cuanto antes según su carácter, intenta por todos los medios no chocar y respeta la física (aceleraciones, frenadas y giros realistas). La ciudad, con semáforos adaptativos y guardias de tráfico, intenta mantener el tráfico fluido. Los atascos forman parte del resultado; lo que no debe ocurrir es un bloqueo permanente.

## Qué incluye

- **Cinco ciudades fijas** (de 3 × 2 km a 10 × 10 km) con calles de uno y varios carriles, autovía, rotondas, un río con puentes y túneles, giros prohibidos, radares y lugares de destino con plazas de aparcamiento.
- **Conductores con personalidad**: de temerosos a temerarios, con su velocidad respecto al límite (del −15 % al +20 %), distancia de seguridad, tiempo de reacción, aceleración y frenada.
- **Conducción realista**: modelo de seguimiento IDM, cambios de carril, prioridades en cruces, percepción de los demás coches para anticiparse y frenadas dentro de valores reales.
- **Semáforos adaptativos** (o de tiempo fijo), **guardias de tráfico** que intervienen en los atascos graves y **accidentes** que cortan carriles durante un tiempo.
- **Vehículos**: coches, motos (arrancan y frenan más fuerte), camiones de reparto y autobuses de línea con paradas (arrancan y frenan más despacio y tienen la velocidad limitada, también en la autovía).
- **Ciudad con usos**: distrito de negocios, polígonos industriales, parques comerciales, zonas de ocio, centros médicos y barrios residenciales.
- **Viajes según la hora**: cada conductor tiene su rutina (casa, trabajo, colegio, comida, ocio) y la demanda marca cuántos viajes empiezan cada hora.
- **Peatones, lluvia y noche**: pasos de peatones en los cruces, y conducción en mojado y de noche.
- **Modos de uso**: con ventana (viendo la ciudad, siguiendo coches), «solo datos» con una barra de avance, sin pantalla desde la consola y lotes de estudios en paralelo.
- **Reproducible**: con la misma semilla y las mismas opciones, la simulación es idéntica.

## Requisitos

Para el ejecutable de Windows, ninguno. Para ejecutarlo desde el código fuente:

- [Godot 4.7.2](https://godotengine.org/download) o posterior, **versión .NET («mono»)**.
- [.NET SDK 9](https://dotnet.microsoft.com/download) o posterior.

## Uso rápido

**Sin instalar nada (Windows de 64 bits):** descarga el ZIP de la última versión en *Releases*, descomprímelo y abre
`SimulacionTrafico.exe`. Los datos se guardan en `Estudios\`, junto al ejecutable.

**Desde el código fuente,** en la carpeta del proyecto (`godot` es el ejecutable de Godot «mono»; `godot-consola`, su versión `_console.exe`):

```
dotnet build
godot --path .
```

Se abre la pantalla inicial, donde se elige la ciudad, las horas, los vehículos y el resto de opciones.

Sin ventana, guardando los datos en `Estudios/mi_estudio/`:

```
godot-consola --headless --path . -- --ciudad=1 --coches=18000 --hora=6 --horas=12 --calentamiento=15 --estudio=mi_estudio
```

Las instrucciones completas (teclas, todas las opciones y lotes) están en [Documentacion/Instrucciones.md](Documentacion/Instrucciones.md); el diseño del modelo, en [Documentacion/Proyecto.md](Documentacion/Proyecto.md), y el contenido de cada fichero de datos, en [Documentacion/Datos.md](Documentacion/Datos.md).

## Datos que produce

Cada estudio crea una carpeta en `Estudios/` con ficheros TSV (UTF-8, separados por tabuladores, con cabecera): `parametros`, `calles`, `giros_prohibidos`, `conductores`, `viajes`, `lugares`, `semaforos`, `radares`, `atascos`, `guardias`, `accidentes` y `peatones`. Son datos en bruto, sin medias ni resúmenes.

## Estructura

```
Codigo/       código C#: Mapa (ciudad, generador, semáforos, rutas), Vehiculos (coches, conductores, simulación),
              Trafico (atascos, guardias, accidentes, percepción), Datos (estudios y ficheros TSV), UI, Camara, Nucleo
Assets/       dibujos de los coches, flechas del suelo e iconos
Ciudades/     las cinco ciudades fijas (JSON)
Escenas/      escena principal de Godot
Pruebas/      pruebas automáticas (xUnit): dotnet test desde esa carpeta
Documentacion/ instrucciones, diseño del proyecto y ficheros de datos
```

## Créditos

- Programación y test: Claude, https://claude.ai
- Diseño vehículos: Gpt, https://chatgpt.com
- FDE, arnés y dirección: Vicente Simón, https://www.linkedin.com/in/vicentesimon

Hecho con inteligencia artificial y con [Godot Engine](https://godotengine.org) (licencia MIT, © Godot Engine contributors).

## Licencia

[Apache 2.0](LICENSE). Se puede copiar, modificar y redistribuir libremente, pero toda copia o versión derivada debe conservar el fichero
[NOTICE](NOTICE), con la referencia a este proyecto como original y sus créditos, e indicar qué se ha modificado.

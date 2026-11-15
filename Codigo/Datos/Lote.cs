using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Godot;

namespace SimulacionTrafico.Datos;

/// <summary>
/// Lote de estudios: ejecuta varios estudios sin pantalla a la vez, cada uno en su propio proceso (una simulación es
/// secuencial, pero estudios distintos no dependen entre sí). Se describe en un TSV con una fila por estudio.
/// Columnas (todas opcionales salvo Nombre; vacío = valor por defecto):
/// Nombre, Ciudad (1-5), Semilla (un número o un rango 1-4: genera un estudio por semilla), Vehiculos, HoraInicio, Horas,
/// Calentamiento (minutos), Navegador (0-100), Lugar, Fecha (mes-día), Curva (24 porcentajes separados por comas, o uno solo).
/// Cada estudio guarda sus ficheros en Estudios/&lt;Nombre&gt;/ y su salida en Estudios/_lotes/&lt;Nombre&gt;.log; al terminar
/// se escribe Estudios/_lotes/resumen_lote.tsv con la lista de estudios y su carpeta.
/// </summary>
public static class Lote
{
    private sealed class Trabajo
    {
        public string Nombre = "";
        public List<string> Argumentos = new();
        public Process? Proceso;
        public StreamWriter? Registro;
        public string? Carpeta;
        public Stopwatch Reloj = new();
    }

    public static int Ejecutar(string ruta, int paralelo)
    {
        if (!File.Exists(ruta))
        {
            GD.PrintErr($"No existe el fichero de lote {ruta}");
            return 1;
        }
        var trabajos = Leer(ruta);
        if (trabajos.Count == 0)
        {
            GD.PrintErr("El lote no tiene ningún estudio.");
            return 1;
        }
        paralelo = Math.Clamp(paralelo > 0 ? paralelo : System.Environment.ProcessorCount - 1, 1, trabajos.Count);

        string carpetaProyecto = ProjectSettings.GlobalizePath("res://").TrimEnd('/', '\\');
        string carpetaLotes = Path.Combine(carpetaProyecto, "Estudios", "_lotes");
        Directory.CreateDirectory(carpetaLotes);
        string ejecutable = OS.GetExecutablePath();

        GD.Print($"Lote: {trabajos.Count} estudios, hasta {paralelo} a la vez.");
        var pendientes = new Queue<Trabajo>(trabajos);
        var enMarcha = new List<Trabajo>();
        var terminados = new List<Trabajo>();
        var reloj = Stopwatch.StartNew();

        while (pendientes.Count > 0 || enMarcha.Count > 0)
        {
            while (pendientes.Count > 0 && enMarcha.Count < paralelo)
            {
                var t = pendientes.Dequeue();
                Arrancar(t, ejecutable, carpetaProyecto, carpetaLotes);
                enMarcha.Add(t);
                GD.Print($"  empieza {t.Nombre}");
            }
            Thread.Sleep(400);
            foreach (var t in enMarcha.Where(t => t.Proceso!.HasExited).ToList())
            {
                t.Proceso!.WaitForExit();   // vacía la salida pendiente
                t.Reloj.Stop();
                t.Registro!.Dispose();
                enMarcha.Remove(t);
                terminados.Add(t);
                GD.Print($"  [{terminados.Count}/{trabajos.Count}] terminado {t.Nombre} en {t.Reloj.Elapsed.TotalSeconds:0} s (código {t.Proceso.ExitCode})");
            }
        }

        EscribirResumen(Path.Combine(carpetaLotes, "resumen_lote.tsv"), trabajos);
        GD.Print($"Lote terminado en {reloj.Elapsed.TotalSeconds:0} s. Resumen en {Path.Combine(carpetaLotes, "resumen_lote.tsv")}");
        return trabajos.Any(t => t.Proceso!.ExitCode != 0) ? 2 : 0;
    }

    private static void Arrancar(Trabajo t, string ejecutable, string carpetaProyecto, string carpetaLotes)
    {
        var inicio = new ProcessStartInfo(ejecutable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var a in new[] { "--headless", "--path", carpetaProyecto, "--" }.Concat(t.Argumentos)) inicio.ArgumentList.Add(a);

        t.Registro = new StreamWriter(Path.Combine(carpetaLotes, $"{t.Nombre}.log"), false, new UTF8Encoding(false)) { AutoFlush = true };
        t.Proceso = new Process { StartInfo = inicio };
        void Linea(string? s)
        {
            if (s == null) return;
            lock (t)
            {
                t.Registro!.WriteLine(s);
                var m = Regex.Match(s, @"los datos se guardan en (.+)$");
                if (m.Success) t.Carpeta = m.Groups[1].Value.Trim();
            }
        }
        t.Proceso.OutputDataReceived += (_, e) => Linea(e.Data);
        t.Proceso.ErrorDataReceived += (_, e) => Linea(e.Data);
        t.Reloj.Start();
        t.Proceso.Start();
        t.Proceso.BeginOutputReadLine();
        t.Proceso.BeginErrorReadLine();
    }

    private static List<Trabajo> Leer(string ruta)
    {
        var lineas = File.ReadAllLines(ruta, Encoding.UTF8).Where(l => l.Trim().Length > 0 && !l.StartsWith('#')).ToList();
        var trabajos = new List<Trabajo>();
        if (lineas.Count < 2) return trabajos;
        var cabecera = lineas[0].Split('\t').Select(c => c.Trim().TrimStart('﻿')).ToList();

        foreach (var linea in lineas.Skip(1))
        {
            var celdas = linea.Split('\t');
            string Celda(string nombre)
            {
                int i = cabecera.FindIndex(c => c.Equals(nombre, StringComparison.OrdinalIgnoreCase));
                return i >= 0 && i < celdas.Length ? celdas[i].Trim() : "";
            }

            string nombreBase = Celda("Nombre");
            if (nombreBase == "") continue;

            var semillas = new List<int?>();
            string sem = Celda("Semilla");
            var rango = Regex.Match(sem, @"^(\d+)-(\d+)$");
            if (rango.Success)
                for (int s = int.Parse(rango.Groups[1].Value); s <= int.Parse(rango.Groups[2].Value); s++) semillas.Add(s);
            else if (int.TryParse(sem, out int unica)) semillas.Add(unica);
            else semillas.Add(null);

            foreach (var semilla in semillas)
            {
                var t = new Trabajo { Nombre = semillas.Count > 1 ? $"{nombreBase}_s{semilla}" : nombreBase };
                void Arg(string clave, string valor)
                {
                    if (valor != "") t.Argumentos.Add($"--{clave}={valor}");
                }
                Arg("ciudad", Celda("Ciudad") == "" ? "1" : Celda("Ciudad"));
                if (semilla != null) Arg("semilla", semilla.Value.ToString(CultureInfo.InvariantCulture));
                Arg("coches", Celda("Vehiculos"));
                Arg("hora", Celda("HoraInicio"));
                Arg("horas", Celda("Horas"));
                Arg("calentamiento", Celda("Calentamiento"));
                if (Celda("Navegador") != "") Arg("navegador", (float.Parse(Celda("Navegador"), CultureInfo.InvariantCulture) / 100f).ToString(CultureInfo.InvariantCulture));
                Arg("lugar", Celda("Lugar"));
                Arg("fecha", Celda("Fecha"));
                Arg("curva", Celda("Curva"));
                t.Argumentos.Add($"--estudio={t.Nombre}");
                trabajos.Add(t);
            }
        }
        return trabajos;
    }

    /// <summary>Lista los estudios del lote con su carpeta, el código de salida del proceso y lo que tardó (sin resultados de la simulación).</summary>
    private static void EscribirResumen(string ruta, List<Trabajo> trabajos)
    {
        using var w = new StreamWriter(ruta, false, new UTF8Encoding(true));
        w.WriteLine("Estudio\tCarpeta\tCodigo\tSegundosProceso");
        foreach (var t in trabajos)
            w.WriteLine($"{t.Nombre}\t{t.Carpeta}\t{t.Proceso!.ExitCode}\t{t.Reloj.Elapsed.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)}");
    }
}

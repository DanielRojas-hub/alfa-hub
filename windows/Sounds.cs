using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AlfaHub
{
    /// Sonidos incluidos: se sintetizan al vuelo (no hay archivos de audio). Es la misma receta que
    /// la versión de macOS (macos/Sounds.swift), así que suenan igual en las dos.
    static class Sounds
    {
        /// Una nota: frecuencia (o barrido), duración, caída exponencial por segundo y armónicos.
        sealed class Tone
        {
            public double Start, Duration, Freq, EndFreq, Decay;
            public double[][] Partials = { new[] { 1.0, 1.0 } };  // (múltiplo, ganancia)
            public bool Square;

            public Tone(double start, double duration, double freq, double decay = 0, double endFreq = 0, bool square = false, double[][] partials = null)
            {
                Start = start; Duration = duration; Freq = freq; Decay = decay; Square = square;
                EndFreq = endFreq > 0 ? endFreq : freq;
                if (partials != null) Partials = partials;
            }
        }

        public static readonly string[] Ids =
            { "beep", "doble", "triple", "ding", "campana", "carillon", "jingle", "moneda", "alarma", "sirena", "burbuja", "descenso", "gong", "tic" };

        public static string Title(string id)
        {
            switch (Normalize(id))
            {
                case "doble": return "Doble beep";
                case "triple": return "Triple beep";
                case "ding": return "Ding";
                case "campana": return "Campana";
                case "carillon": return "Carillón";
                case "jingle": return "Jingle";
                case "moneda": return "Moneda";
                case "alarma": return "Alarma";
                case "sirena": return "Sirena";
                case "burbuja": return "Burbuja";
                case "descenso": return "Descenso";
                case "gong": return "Gong";
                case "tic": return "Tic";
                default: return "Beep";
            }
        }

        public static string Normalize(string id) => Ids.Contains(id) ? id : "beep";

        static double[][] P(params double[] pairs)
        {
            var result = new double[pairs.Length / 2][];
            for (int i = 0; i < result.Length; i++) result[i] = new[] { pairs[i * 2], pairs[i * 2 + 1] };
            return result;
        }

        static List<Tone> Tones(string id)
        {
            var bell = P(1, 1, 2, 0.3, 3, 0.1);
            var t = new List<Tone>();
            switch (Normalize(id))
            {
                case "doble":
                    foreach (double s in new[] { 0, 0.17 }) t.Add(new Tone(s, 0.11, 1000, 6));
                    break;
                case "triple":
                    foreach (double s in new[] { 0, 0.14, 0.28 }) t.Add(new Tone(s, 0.08, 1250, 8));
                    break;
                case "ding":
                    t.Add(new Tone(0, 0.9, 1318.5, 5, partials: P(1, 1, 2, 0.3, 3, 0.08)));
                    break;
                case "campana":
                    t.Add(new Tone(0, 1.6, 784, 2.6, partials: P(1, 1, 2, 0.55, 2.76, 0.35, 5.4, 0.15)));
                    break;
                case "carillon":
                    double[] chime = { 1568, 1318.5, 1046.5 };
                    for (int i = 0; i < chime.Length; i++) t.Add(new Tone(i * 0.2, 1.0, chime[i], 4.5, partials: bell));
                    break;
                case "jingle":
                    double[] notes = { 1046.5, 1318.5, 1568, 2093 };
                    for (int i = 0; i < notes.Length; i++)
                    {
                        bool last = i == 3;
                        t.Add(new Tone(i * 0.1, last ? 0.7 : 0.3, notes[i], last ? 4 : 8, partials: P(1, 1, 2, 0.2)));
                    }
                    break;
                case "moneda":
                    t.Add(new Tone(0, 0.08, 987.8, square: true));
                    t.Add(new Tone(0.08, 0.45, 1318.5, 6, square: true));
                    break;
                case "alarma":
                    for (int i = 0; i < 4; i++)
                    {
                        t.Add(new Tone(i * 0.3, 0.14, 880, 2, square: true));
                        t.Add(new Tone(i * 0.3 + 0.15, 0.14, 660, 2, square: true));
                    }
                    break;
                case "sirena":
                    for (int i = 0; i < 4; i++)
                        t.Add(new Tone(i * 0.45, 0.45, i % 2 == 0 ? 600 : 1200, endFreq: i % 2 == 0 ? 1200 : 600));
                    break;
                case "burbuja":
                    t.Add(new Tone(0, 0.16, 400, 6, endFreq: 1400));
                    break;
                case "descenso":
                    t.Add(new Tone(0, 0.4, 1400, 3, endFreq: 350));
                    break;
                case "gong":
                    t.Add(new Tone(0, 2.4, 140, 1.6, partials: P(1, 1, 1.47, 0.7, 2.09, 0.5, 2.56, 0.35, 3.39, 0.25, 4.1, 0.15)));
                    break;
                case "tic":
                    t.Add(new Tone(0, 0.05, 2200, 50, partials: P(1, 1, 2.7, 0.4)));
                    break;
                default:
                    t.Add(new Tone(0, 0.25, 880, 18.42));
                    break;
            }
            return t;
        }

        const int Rate = 44100;

        /// WAV mono de 16 bits del sonido incluido.
        public static byte[] Wav(string id)
        {
            var tones = Tones(id);
            return Wav(Render(tones, tones.Any(x => x.Square) ? 0.55 : 0.9));
        }

        /// Beep simple (los de los últimos segundos), con la misma caída que el sitio original.
        public static byte[] Beep(double freq, double duration) =>
            Wav(Render(new List<Tone> { new Tone(0, duration, freq, -Math.Log(0.01) / duration) }, 0.98));

        static double[] Render(List<Tone> tones, double peak)
        {
            double length = tones.Max(x => x.Start + x.Duration) + 0.02;
            var buffer = new double[(int)(length * Rate)];
            foreach (var tone in tones)
            {
                int first = (int)(tone.Start * Rate);
                int count = (int)(tone.Duration * Rate);
                for (int i = 0; i < count && first + i < buffer.Length; i++)
                {
                    double t = (double)i / Rate;
                    // Fase integrada: el barrido de frecuencia es continuo.
                    double phase = 2 * Math.PI * (tone.Freq * t + (tone.EndFreq - tone.Freq) * t * t / (2 * tone.Duration));
                    double envelope = Math.Min(1, t / 0.004) * Math.Exp(-tone.Decay * t) * Math.Min(1, (tone.Duration - t) / 0.01);
                    double value = 0;
                    foreach (var p in tone.Partials)
                    {
                        double x = phase * p[0];
                        value += p[1] * (tone.Square
                            ? Math.Sin(x) + Math.Sin(3 * x) / 3 + Math.Sin(5 * x) / 5 + Math.Sin(7 * x) / 7
                            : Math.Sin(x));
                    }
                    buffer[first + i] += value * envelope;
                }
            }
            double max = buffer.Max(Math.Abs);
            if (max > 0) for (int i = 0; i < buffer.Length; i++) buffer[i] = buffer[i] / max * peak;
            return buffer;
        }

        static byte[] Wav(double[] samples)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + samples.Length * 2);
                w.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
                w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data")); w.Write(samples.Length * 2);
                foreach (double s in samples) w.Write((short)(Math.Max(-1, Math.Min(1, s)) * 32000));
                return ms.ToArray();
            }
        }
    }
}

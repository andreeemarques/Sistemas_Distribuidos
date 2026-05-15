using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sensor
{
    internal class CsvConfig
    {
        private static readonly string Caminho = "sensores.csv";
        private static readonly string Cabecalho = "id,zona,parametros";

        public static List<SensorConfig> LerTodos()
        {
            if (!File.Exists(Caminho))
            {
                File.WriteAllText(Caminho, Cabecalho + Environment.NewLine);
                return new List<SensorConfig>();
            }

            return File.ReadAllLines(Caminho)
                .Skip(1)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(ParseLinha)
                .ToList();
        }

        public static SensorConfig LerPorId(string id)
        {
            return LerTodos().FirstOrDefault(s => s.Id == id);
        }

        public static bool Criar(SensorConfig config)
        {
            if (LerPorId(config.Id) != null)
            {
                Console.WriteLine($"[CSV] Sensor '{config.Id}' já existe.");
                return false;
            }

            if (!File.Exists(Caminho))
                File.WriteAllText(Caminho, Cabecalho + Environment.NewLine);

            string parametros = $"\"{string.Join(",", config.Parametros)}\"";
            string linha = $"{config.Id},{config.Zona},{parametros};";
            File.AppendAllText(Caminho, linha + Environment.NewLine);

            Console.WriteLine($"[CSV] Sensor '{config.Id}' guardado.");
            return true;
        }

        private static SensorConfig ParseLinha(string linha)
        {
            // split simples que respeita campos entre aspas
            var partes = new List<string>();
            bool dentroAspas = false;
            var atual = new System.Text.StringBuilder();

            foreach (char c in linha)
            {
                if (c == '"')
                {
                    dentroAspas = !dentroAspas;
                }
                else if (c == ',' && !dentroAspas)
                {
                    partes.Add(atual.ToString());
                    atual.Clear();
                }
                else
                {
                    atual.Append(c);
                }
            }
            partes.Add(atual.ToString());

            return new SensorConfig
            {
                Id = partes[0].Trim(),
                Zona = partes[1].Trim(),
                Parametros = partes[2].Trim().Split(',').Select(p => p.Trim()).ToList()
            };
        }
    }
}

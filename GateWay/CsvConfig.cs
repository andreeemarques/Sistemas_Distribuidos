using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Gateway
{
    internal class CsvConfig
    {
        private static readonly string Caminho =
            Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\gateways.csv"));
        private static readonly string Cabecalho = "id,zona";

        public static List<GatewayConfig> LerTodos()
        {
            if (!File.Exists(Caminho))
            {
                File.WriteAllText(Caminho, Cabecalho + Environment.NewLine);
                return new List<GatewayConfig>();
            }

            return File.ReadAllLines(Caminho)
                .Skip(1)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(ParseLinha)
                .ToList();
        }

        public static GatewayConfig LerPorId(string id)
        {
            return LerTodos().FirstOrDefault(g => g.Id == id);
        }

        private static GatewayConfig ParseLinha(string linha)
        {
            var partes = linha.Split(',');
            return new GatewayConfig
            {
                Id = partes[0].Trim(),
                Zona = partes[1].Trim()
            };
        }
    }
}
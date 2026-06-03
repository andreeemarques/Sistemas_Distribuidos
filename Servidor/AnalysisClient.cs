using AnalysisService;
using Grpc.Core;
using System;
using System.Collections.Generic;

public class AnalysisClient
{
    private readonly Analyzer.AnalyzerClient _client;

    public AnalysisClient()
    {
        var channel = new Channel("localhost", 50052, ChannelCredentials.Insecure);
        _client = new Analyzer.AnalyzerClient(channel);
    }

    public StatisticsResponse GetStatistics(string sensorId, string tipo, List<double> valores)
    {
        try
        {
            var request = new StatisticsRequest
            {
                SensorId = sensorId,
                Tipo = tipo,
            };
            request.Valores.AddRange(valores);

            var response = _client.GetStatistics(request);
            Console.WriteLine($"[RPC] Estatísticas | Média: {response.Media:F2} | Min: {response.Minimo} | Max: {response.Maximo}");
            return response;
        }
        catch (Exception e)
        {
            Console.WriteLine("[RPC] Erro GetStatistics: " + e.Message);
            return null;
        }
    }

    public AnomalyResponse DetectAnomalies(string sensorId, string tipo, List<double> valores)
    {
        try
        {
            var request = new AnomalyRequest
            {
                SensorId = sensorId,
                Tipo = tipo,
            };
            request.Valores.AddRange(valores);

            var response = _client.DetectAnomalies(request);
            Console.WriteLine($"[RPC] Anomalia: {response.AnomaliaDetetada} | {response.Descricao}");
            return response;
        }
        catch (Exception e)
        {
            Console.WriteLine("[RPC] Erro DetectAnomalies: " + e.Message);
            return null;
        }
    }

    public HealthRiskResponse PredictHealthRisk(string sensorId, List<string> tipos, List<double> valores)
    {
        try
        {
            var request = new HealthRiskRequest
            {
                SensorId = sensorId,
            };
            request.Tipos.AddRange(tipos);
            request.Valores.AddRange(valores);

            var response = _client.PredictHealthRisk(request);
            Console.WriteLine($"[RPC] Risco: {response.NivelRisco} | {response.Descricao}");
            return response;
        }
        catch (Exception e)
        {
            Console.WriteLine("[RPC] Erro PredictHealthRisk: " + e.Message);
            return null;
        }
    }
}
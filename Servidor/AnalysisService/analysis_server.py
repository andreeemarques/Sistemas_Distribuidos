# analysis_server.py
# -*- coding: cp1252 -*-
import grpc
from concurrent import futures
import statistics
import analysis_pb2
import analysis_pb2_grpc

class AnalyzerServicer(analysis_pb2_grpc.AnalyzerServicer):

   def GetStatistics(self, request, context):
    valores = list(request.valores)
    return analysis_pb2.StatisticsResponse(
        valor=valores[0] if valores else 0
    )

    def DetectAnomalies(self, request, context):
        valores = list(request.valores)
        tipo = request.tipo.lower()

        # Limites aceitáveis por tipo de sensor
        limites = {
            "temperatura": (0, 45),
            "humidade":    (10, 95),
            "pm2.5":       (0, 75),
            "no2":         (0, 200),
            "ruido":       (0, 85),
        }

        anomalos = []
        if tipo in limites:
            low, high = limites[tipo]
            anomalos = [v for v in valores if v < low or v > high]

        if anomalos:
            return analysis_pb2.AnomalyResponse(
                anomalia_detetada=True,
                valores_anomalos=anomalos,
                descricao=f"Valores fora do intervalo aceitável para {request.tipo}: {anomalos}"
            )

        return analysis_pb2.AnomalyResponse(
            anomalia_detetada=False,
            valores_anomalos=[],
            descricao="Sem anomalias detetadas."
        )

    def PredictHealthRisk(self, request, context):
        tipos = list(request.tipos)
        valores = list(request.valores)

        nivel = "BAIXO"
        descricao = "Sem risco significativo para a saúde pública."
        recomendacoes = []

        for tipo, valor in zip(tipos, valores):
            t = tipo.lower()

            if t == "pm2.5" and valor > 55:
                nivel = "ALTO"
                descricao = f"PM2.5 elevado ({valor} µg/m³): risco respiratório."
                recomendacoes.append("Evitar exposição prolongada ao ar livre.")
                recomendacoes.append("Usar máscara de proteção respiratória.")

            elif t == "no2" and valor > 150:
                nivel = "ALTO"
                descricao = f"NO2 elevado ({valor} µg/m³): risco para grupos vulneráveis."
                recomendacoes.append("Grupos vulneráveis devem permanecer em casa.")
                recomendacoes.append("Evitar zonas de tráfego intenso.")

            elif t == "temperatura" and valor > 40:
                if nivel != "ALTO":
                    nivel = "MEDIO"
                descricao = f"Temperatura elevada ({valor}°C): risco de golpe de calor."
                recomendacoes.append("Manter-se hidratado.")
                recomendacoes.append("Evitar exposição ao sol nas horas de maior calor.")

            elif t == "ruido" and valor > 80:
                if nivel != "ALTO":
                    nivel = "MEDIO"
                descricao = f"Ruído elevado ({valor} dB): risco auditivo."
                recomendacoes.append("Usar proteção auditiva em zonas afetadas.")

        if not recomendacoes:
            recomendacoes.append("Nenhuma ação necessária.")

        return analysis_pb2.HealthRiskResponse(
            nivel_risco=nivel,
            descricao=descricao,
            recomendacoes=recomendacoes
        )


    def serve():
        server = grpc.server(futures.ThreadPoolExecutor(max_workers=10))
        analysis_pb2_grpc.add_AnalyzerServicer_to_server(AnalyzerServicer(), server)
        server.add_insecure_port('[::]:50052')
        server.start()
        print("[ANALYSIS] Serviço de análise iniciado na porta 50052...")
        server.wait_for_termination()

    if __name__ == '__main__':
        serve()
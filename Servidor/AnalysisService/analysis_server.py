# -*- coding: cp1252 -*-
import grpc
from concurrent import futures
import statistics
import analysis_pb2
import analysis_pb2_grpc

class AnalyzerServicer(analysis_pb2_grpc.AnalyzerServicer):

    def GetStatistics(self, request, context):
        valores = list(request.valores)
        if not valores:
            return analysis_pb2.StatisticsResponse(minimo=0, maximo=0, media=0)
        return analysis_pb2.StatisticsResponse(
            minimo=min(valores),
            maximo=max(valores),
            media=statistics.mean(valores)
        )

    def DetectAnomalies(self, request, context):
        valores = list(request.valores)
        tipo = request.tipo.lower()

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
                descricao=f"Valores fora do intervalo aceitavel para {request.tipo}: {anomalos}"
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
        descricao = "Sem risco significativo para a saude publica."
        recomendacoes = []

        for tipo, valor in zip(tipos, valores):
            t = tipo.lower()

            if t == "pm2.5" and valor > 55:
                nivel = "ALTO"
                descricao = f"PM2.5 elevado ({valor} ug/m3): risco respiratorio."
                recomendacoes.append("Evitar exposicao prolongada ao ar livre.")
                recomendacoes.append("Usar mascara de protecao respiratoria.")

            elif t == "no2" and valor > 150:
                nivel = "ALTO"
                descricao = f"NO2 elevado ({valor} ug/m3): risco para grupos vulneraveis."
                recomendacoes.append("Grupos vulneraveis devem permanecer em casa.")
                recomendacoes.append("Evitar zonas de trafego intenso.")

            elif t == "temperatura" and valor > 40:
                if nivel != "ALTO":
                    nivel = "MEDIO"
                descricao = f"Temperatura elevada ({valor} C): risco de golpe de calor."
                recomendacoes.append("Manter-se hidratado.")
                recomendacoes.append("Evitar exposicao ao sol nas horas de maior calor.")

            elif t == "ruido" and valor > 80:
                if nivel != "ALTO":
                    nivel = "MEDIO"
                descricao = f"Ruido elevado ({valor} dB): risco auditivo."
                recomendacoes.append("Usar protecao auditiva em zonas afetadas.")

        if not recomendacoes:
            recomendacoes.append("Nenhuma acao necessaria.")

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
    print("[ANALYSIS] Servico de analise iniciado na porta 50052...")
    server.wait_for_termination()


if __name__ == '__main__':
    serve()

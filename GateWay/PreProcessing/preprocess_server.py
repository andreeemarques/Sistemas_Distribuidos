# -*- coding: utf-8 -*-
import grpc
from concurrent import futures
import preprocess_pb2
import preprocess_pb2_grpc

# Gamas válidas por tipo de sensor
RANGES = {
    "TEMP":  (-30, 60),
    "HUM":   (0, 100),
    "RUIDO": (0, 140),
    "PM2.5": (0, 500),
    "PM10":  (0, 600),
    "LUM":   (0, 100000),
    "AR":    (0, 500),
}

# Unidades por tipo
UNITS = {
    "TEMP":  "C",
    "HUM":   "%",
    "RUIDO": "dB",
    "PM2.5": "ug/m3",
    "PM10":  "ug/m3",
    "LUM":   "lux",
    "AR":    "AQI",
}

class PreProcessingServicer(preprocess_pb2_grpc.PreProcessingServiceServicer):

    def ProcessData(self, request, context):
        print(f"[RPC] Recebido: sensor={request.sensor_id} tipo={request.type} valor={request.value}")

        # 1. Validar se o valor é numérico
        try:
            value = float(request.value)
        except ValueError:
            print(f"[RPC] ERRO: valor nao numerico '{request.value}'")
            return preprocess_pb2.ProcessedData(
                valid=False,
                error_message=f"Valor nao numerico: '{request.value}'"
            )

        # 2. Verificar gama válida
        if request.type in RANGES:
            min_v, max_v = RANGES[request.type]
            if not (min_v <= value <= max_v):
                print(f"[RPC] ERRO: {request.type}={value} fora de [{min_v}, {max_v}]")
                return preprocess_pb2.ProcessedData(
                    valid=False,
                    error_message=f"{request.type} fora do intervalo [{min_v}, {max_v}]"
                )

        # 3. Normalizar: arredondar a 2 casas decimais
        normalized = round(value, 2)
        unit = UNITS.get(request.type, "")

        print(f"[RPC] OK: {request.type} = {normalized} {unit}")
        return preprocess_pb2.ProcessedData(
            valid=True,
            normalized_value=str(normalized),
            unit=unit
        )


def serve():
    server = grpc.server(futures.ThreadPoolExecutor(max_workers=10))
    preprocess_pb2_grpc.add_PreProcessingServiceServicer_to_server(
        PreProcessingServicer(), server
    )
    server.add_insecure_port("[::]:50051")
    server.start()
    print("[gRPC] Servico de pre-processamento na porta 50051...")
    server.wait_for_termination()


if __name__ == "__main__":
    serve()
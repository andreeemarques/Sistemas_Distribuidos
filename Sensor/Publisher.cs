using System;
using System.Text;
using RabbitMQ.Client;
namespace Sensor
{
    internal class Publisher : IDisposable
    {
        private readonly IConnection _connection;
        private readonly IModel _channel;
        private const string Exchange = "sensor_data";
        public Publisher(string host = "localhost")
        {
            var factory = new ConnectionFactory() { HostName = host };
            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();
            _channel.ExchangeDeclare(
                exchange: Exchange,
                type: "topic",
                durable: true
            );
        }
        public void Publicar(string sensorId, string zona, string parametro, int valor)
        {
            string routingKey = $"{zona}.{parametro}";

            var timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
            string mensagem =$"{timestamp};{sensorId};{zona};{parametro};{valor}";
            byte[] body = Encoding.UTF8.GetBytes(mensagem);
            _channel.BasicPublish(
                exchange: Exchange,
                routingKey: routingKey,
                basicProperties: null,
                body: body
            );
            Console.WriteLine($"[{sensorId}] {routingKey} -> {valor}");
        }
        public void Dispose()
        {
            _channel?.Close();
            _connection?.Close();
        }
    }
}

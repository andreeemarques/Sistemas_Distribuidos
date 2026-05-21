using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Text;
using System.Threading;

namespace Gateway
{
    internal class Subscriber : IDisposable
    {
        private readonly IConnection _connection;
        private readonly IModel _channel;
        private const string Exchange = "sensor_data";
        private readonly string _queueName;

        public Subscriber(string routingPattern, string host = "localhost")
        {
            var factory = new ConnectionFactory() { HostName = host };
            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();

            _channel.ExchangeDeclare(
                exchange: Exchange,
                type: "topic",
                durable: true
            );

            _queueName = _channel.QueueDeclare(
                queue: "",
                durable: false,
                exclusive: true,
                autoDelete: true,
                arguments: null
            ).QueueName;

            _channel.QueueBind(
                queue: _queueName,
                exchange: Exchange,
                routingKey: routingPattern
            );

            Console.WriteLine($"[Subscriber] A ouvir padrão: '{routingPattern}'");
        }

        public void Iniciar(Action<string> onMensagem)
        {
            var consumer = new EventingBasicConsumer(_channel);

            consumer.Received += (model, ea) =>
            {
                string mensagem = Encoding.UTF8.GetString(ea.Body.ToArray());
                onMensagem(mensagem);
                _channel.BasicAck(ea.DeliveryTag, multiple: false);
            };

            _channel.BasicConsume(
                queue: _queueName,
                autoAck: false,
                consumer: consumer
            );

            Thread.Sleep(Timeout.Infinite);
        }


        public void Dispose()
        {
            _channel?.Close();
            _connection?.Close();
        }
    }
}
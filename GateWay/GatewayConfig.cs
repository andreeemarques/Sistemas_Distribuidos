using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gateway
{
    internal class GatewayConfig
    {
        public string Id { get; set; }
        public string Zona { get; set; }
        public string RoutingPattern => $"sensor.{Zona}.*";
    }
}

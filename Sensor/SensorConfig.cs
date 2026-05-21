using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sensor
{
    internal class SensorConfig
    {
        public string Id { get; set; }
        public string Zona { get; set; }
        public List<string> Parametros { get; set; }

        public int Intervalo { get; set; } = 5;
    }
}

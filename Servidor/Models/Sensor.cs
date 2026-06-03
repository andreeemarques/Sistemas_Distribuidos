using Servidor.Models;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Servidor.Models
{
    public class Sensor
    {
        [Key]
        [StringLength(50)]
        public string IdSensor { get; set; }

        public virtual ICollection<Leitura> Leituras { get; set; }
    }
}
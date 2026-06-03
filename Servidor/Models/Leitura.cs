using Servidor.Models;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Servidor.Models
{
    public class Leitura
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public DateTime DataHora { get; set; }

        [Required]
        [StringLength(50)]
        public string IdSensor { get; set; }

        [Required]
        [StringLength(10)]
        public string Tipo { get; set; }

        [Required]
        public double Valor { get; set; }

        [ForeignKey("IdSensor")]
        public virtual Sensor Sensor { get; set; }
    }
}
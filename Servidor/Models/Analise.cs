using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Servidor.Models
{
    public class ResultadoAnalise
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

        public double DesvioPadrao { get; set; }
        public double Valor { get; set; }

        public bool AnomaliaDetetada { get; set; }

        [StringLength(500)]
        public string DescricaoAnomalia { get; set; }

        [StringLength(10)]
        public string NivelRisco { get; set; }

        [StringLength(500)]
        public string DescricaoRisco { get; set; }

        [StringLength(1000)]
        public string Recomendacoes { get; set; }

        [ForeignKey("IdSensor")]
        public virtual Sensor Sensor { get; set; }
    }
}
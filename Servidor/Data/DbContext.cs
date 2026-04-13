using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Servidor.Models;


namespace Servidor.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext() : base("SD_DataBase") { }

        public DbSet<Sensor> Sensores { get; set; }
        public DbSet<Leitura> Leituras { get; set; }

    }
}

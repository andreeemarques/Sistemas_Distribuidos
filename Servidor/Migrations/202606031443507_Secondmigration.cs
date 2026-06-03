namespace Servidor.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class Secondmigration : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.ResultadoAnalises",
                c => new
                    {
                        Id = c.Int(nullable: false, identity: true),
                        DataHora = c.DateTime(nullable: false),
                        IdSensor = c.String(nullable: false, maxLength: 50),
                        Tipo = c.String(nullable: false, maxLength: 10),
                        Media = c.Double(nullable: false),
                        Minimo = c.Double(nullable: false),
                        Maximo = c.Double(nullable: false),
                        DesvioPadrao = c.Double(nullable: false),
                        AnomaliaDetetada = c.Boolean(nullable: false),
                        DescricaoAnomalia = c.String(maxLength: 500),
                        NivelRisco = c.String(maxLength: 10),
                        DescricaoRisco = c.String(maxLength: 500),
                        Recomendacoes = c.String(maxLength: 1000),
                    })
                .PrimaryKey(t => t.Id)
                .ForeignKey("dbo.Sensors", t => t.IdSensor, cascadeDelete: true)
                .Index(t => t.IdSensor);
            
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.ResultadoAnalises", "IdSensor", "dbo.Sensors");
            DropIndex("dbo.ResultadoAnalises", new[] { "IdSensor" });
            DropTable("dbo.ResultadoAnalises");
        }
    }
}

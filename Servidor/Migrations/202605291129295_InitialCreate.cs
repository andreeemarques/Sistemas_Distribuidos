namespace Servidor.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class InitialCreate : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.Leituras",
                c => new
                    {
                        Id = c.Int(nullable: false, identity: true),
                        DataHora = c.DateTime(nullable: false),
                        IdSensor = c.String(nullable: false, maxLength: 50),
                        Tipo = c.String(nullable: false, maxLength: 10),
                        Valor = c.Double(nullable: false),
                    })
                .PrimaryKey(t => t.Id)
                .ForeignKey("dbo.Sensors", t => t.IdSensor, cascadeDelete: true)
                .Index(t => t.IdSensor);
            
            CreateTable(
                "dbo.Sensors",
                c => new
                    {
                        IdSensor = c.String(nullable: false, maxLength: 50),
                    })
                .PrimaryKey(t => t.IdSensor);
            
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.Leituras", "IdSensor", "dbo.Sensors");
            DropIndex("dbo.Leituras", new[] { "IdSensor" });
            DropTable("dbo.Sensors");
            DropTable("dbo.Leituras");
        }
    }
}

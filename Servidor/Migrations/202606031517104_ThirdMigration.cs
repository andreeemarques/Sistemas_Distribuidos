namespace Servidor.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class ThirdMigration : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.ResultadoAnalises", "Valor", c => c.Double(nullable: false));
            DropColumn("dbo.ResultadoAnalises", "Media");
            DropColumn("dbo.ResultadoAnalises", "Minimo");
            DropColumn("dbo.ResultadoAnalises", "Maximo");
        }
        
        public override void Down()
        {
            AddColumn("dbo.ResultadoAnalises", "Maximo", c => c.Double(nullable: false));
            AddColumn("dbo.ResultadoAnalises", "Minimo", c => c.Double(nullable: false));
            AddColumn("dbo.ResultadoAnalises", "Media", c => c.Double(nullable: false));
            DropColumn("dbo.ResultadoAnalises", "Valor");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookingAPI.DAL.Migrations
{
    public partial class InicialBooking : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Asientos",
                columns: table => new
                {
                    AsientoId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Codigo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Precio = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Asientos", x => x.AsientoId);
                });

            migrationBuilder.CreateTable(
                name: "Eventos",
                columns: table => new
                {
                    EventoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FechaEventoUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Eventos", x => x.EventoId);
                });

            migrationBuilder.CreateTable(
                name: "Reservas",
                columns: table => new
                {
                    ReservaId = table.Column<Guid>(type: "uuid", nullable: false),
                    SolicitudId = table.Column<Guid>(type: "uuid", nullable: false),
                    AsientoId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventoId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<string>(type: "text", nullable: false),
                    Estado = table.Column<int>(type: "integer", nullable: false),
                    Motivo = table.Column<int>(type: "integer", nullable: false),
                    FechaCreacionUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FechaVencimientoUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reservas", x => x.ReservaId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Reservas_AsientoId_Estado",
                table: "Reservas",
                columns: new[] { "AsientoId", "Estado" });

            migrationBuilder.CreateIndex(
                name: "IX_Reservas_SolicitudId",
                table: "Reservas",
                column: "SolicitudId",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Asientos");

            migrationBuilder.DropTable(
                name: "Eventos");

            migrationBuilder.DropTable(
                name: "Reservas");
        }
    }
}

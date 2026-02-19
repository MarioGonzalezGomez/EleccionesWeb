using Elecciones.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elecciones.Infrastructure.Data;

public sealed class EleccionesDbContext : DbContext
{
    public EleccionesDbContext(DbContextOptions<EleccionesDbContext> options) : base(options)
    {
    }

    public DbSet<CircunscripcionEntity> Circunscripciones => Set<CircunscripcionEntity>();
    public DbSet<PartidoEntity> Partidos => Set<PartidoEntity>();
    public DbSet<CircunscripcionPartidoEntity> CircunscripcionPartidos => Set<CircunscripcionPartidoEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<CircunscripcionEntity>(entity =>
        {
            entity.ToTable("circunscripciones");
            entity.HasKey(x => x.Codigo);

            entity.Property(x => x.Codigo).HasColumnName("CIRCUNSCRIPCION");
            entity.Property(x => x.Nombre).HasColumnName("descripcion");
            entity.Property(x => x.Escrutado).HasColumnName("escrutado");
            entity.Property(x => x.Escanios).HasColumnName("escanos");
            entity.Property(x => x.Avance1).HasColumnName("avance1");
            entity.Property(x => x.Avance2).HasColumnName("avance2");
            entity.Property(x => x.Avance3).HasColumnName("avance3");
            entity.Property(x => x.Participacion).HasColumnName("participacion");
            entity.Property(x => x.Votantes).HasColumnName("votantes");
            entity.Property(x => x.ParticipacionHist).HasColumnName("participacion_hist");
            entity.Property(x => x.Comunidad).HasColumnName("COMUNIDAD");
        });

        modelBuilder.Entity<PartidoEntity>(entity =>
        {
            entity.ToTable("partidos");
            entity.HasKey(x => x.Codigo);

            entity.Property(x => x.Codigo).HasColumnName("PARTIDO");
            entity.Property(x => x.Siglas).HasColumnName("sigla");
            entity.Property(x => x.Nombre).HasColumnName("descripcion");
            entity.Property(x => x.Padre).HasColumnName("padre");
        });

        modelBuilder.Entity<CircunscripcionPartidoEntity>(entity =>
        {
            entity.ToTable("circunscripcion_partido");
            entity.HasKey(x => new { x.CodCircunscripcion, x.CodPartido });

            entity.Property(x => x.CodCircunscripcion).HasColumnName("COD_CIRCUNSCRIPCION");
            entity.Property(x => x.CodPartido).HasColumnName("COD_PARTIDO");
            entity.Property(x => x.EscaniosHasta).HasColumnName("escanos_hasta");
            entity.Property(x => x.EscaniosDesdeSondeo).HasColumnName("escanos_desde_sondeo");
            entity.Property(x => x.EscaniosHastaSondeo).HasColumnName("escanos_hasta_sondeo");
            entity.Property(x => x.EscaniosHistoricos).HasColumnName("escanos_hasta_hist");
            entity.Property(x => x.Votos).HasColumnName("votos");
            entity.Property(x => x.Votantes).HasColumnName("votantes");

            entity
                .HasOne(x => x.Circunscripcion)
                .WithMany(x => x.Partidos)
                .HasForeignKey(x => x.CodCircunscripcion)
                .OnDelete(DeleteBehavior.Restrict);

            entity
                .HasOne(x => x.Partido)
                .WithMany(x => x.Circunscripciones)
                .HasForeignKey(x => x.CodPartido)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}

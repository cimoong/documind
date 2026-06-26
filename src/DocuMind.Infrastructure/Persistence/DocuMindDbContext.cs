using DocuMind.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace DocuMind.Infrastructure.Persistence;

/// <summary>
/// EF Core context for DocuMind. Maps <see cref="Document"/> and
/// <see cref="DocumentChunk"/>, including the pgvector embedding column and its
/// HNSW similarity index.
/// </summary>
public class DocuMindDbContext(DbContextOptions<DocuMindDbContext> options) : DbContext(options)
{
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // The "vector" extension is created by the Docker init script, but
        // declaring it here keeps the model self-describing and lets EF emit
        // CREATE EXTENSION in the migration as well.
        modelBuilder.HasPostgresExtension("vector");

        modelBuilder.Entity<Document>(entity =>
        {
            entity.HasKey(d => d.Id);
            entity.Property(d => d.FileName).HasMaxLength(512).IsRequired();
            entity.Property(d => d.ContentType).HasMaxLength(255).IsRequired();
            entity.Property(d => d.UploadedAtUtc);

            entity.HasMany(d => d.Chunks)
                  .WithOne(c => c.Document)
                  .HasForeignKey(c => c.DocumentId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DocumentChunk>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Content).IsRequired();
            entity.Property(c => c.ChunkIndex);

            // Store the embedding as vector(768).
            entity.Property(c => c.Embedding)
                  .HasColumnType($"vector({DocumentChunk.EmbeddingDimensions})");

            // Speed up "find a document" lookups by FK.
            entity.HasIndex(c => c.DocumentId);

            // Approximate-nearest-neighbour index for cosine similarity search.
            // HNSW + vector_cosine_ops matches how we query embeddings.
            entity.HasIndex(c => c.Embedding)
                  .HasMethod("hnsw")
                  .HasOperators("vector_cosine_ops");
        });
    }
}

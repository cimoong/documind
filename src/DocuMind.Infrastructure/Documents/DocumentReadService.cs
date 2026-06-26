using DocuMind.Core.Documents;
using DocuMind.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DocuMind.Infrastructure.Documents;

public sealed class DocumentReadService(DocuMindDbContext db) : IDocumentReadService
{
    public async Task<IReadOnlyList<DocumentSummary>> GetDocumentsAsync(CancellationToken cancellationToken = default)
    {
        return await db.Documents
            .AsNoTracking()
            .OrderByDescending(d => d.UploadedAtUtc)
            .Select(d => new DocumentSummary(d.Id, d.FileName, d.UploadedAtUtc, d.TotalChunks))
            .ToListAsync(cancellationToken);
    }
}

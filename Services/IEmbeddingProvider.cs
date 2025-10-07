namespace dataAccess.Services
{
    public interface IEmbeddingProvider
    {
        Task<float[]> EmbedAsync(string text, CancellationToken ct = default);
    }
}

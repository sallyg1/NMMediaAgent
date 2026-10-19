using Moq;
using NmTrendIngestion.Dedup;
using Xunit;

namespace NmTrendIngestion.Tests;

public class DedupServiceTests
{
    [Fact]
    public void ComputeUrlHash_IsDeterministic_ForSameUrl()
    {
        var hash1 = DedupService.ComputeUrlHash("https://example.com/article-1");
        var hash2 = DedupService.ComputeUrlHash("https://example.com/article-1");

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ComputeUrlHash_DiffersForDifferentUrls()
    {
        var hash1 = DedupService.ComputeUrlHash("https://example.com/article-1");
        var hash2 = DedupService.ComputeUrlHash("https://example.com/article-2");

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void ComputeUrlHash_ReturnsLowercaseHex64Chars()
    {
        var hash = DedupService.ComputeUrlHash("https://example.com/article-1");

        Assert.Equal(64, hash.Length);
        Assert.Equal(hash.ToLowerInvariant(), hash);
        Assert.Matches("^[0-9a-f]{64}$", hash);
    }

    [Fact]
    public async Task TryMarkAsSeenAsync_ReturnsTrue_WhenStoreInsertsNewEntry()
    {
        var storeMock = new Mock<IDedupStore>();
        storeMock
            .Setup(s => s.TryInsertAsync(
                "BingNews",
                DedupService.ComputeUrlHash("https://example.com/article-1"),
                "https://example.com/article-1",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = new DedupService(storeMock.Object);

        var result = await sut.TryMarkAsSeenAsync("BingNews", "https://example.com/article-1", CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task TryMarkAsSeenAsync_ReturnsFalse_WhenStoreReportsDuplicate()
    {
        var storeMock = new Mock<IDedupStore>();
        storeMock
            .Setup(s => s.TryInsertAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var sut = new DedupService(storeMock.Object);

        var result = await sut.TryMarkAsSeenAsync("BingNews", "https://example.com/article-1", CancellationToken.None);

        Assert.False(result);
    }
}

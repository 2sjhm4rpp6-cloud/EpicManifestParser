using System.Buffers;

using EpicManifestParser.UE;

using OffiUtils;

namespace EpicManifestParser.Tests;

public class UncompressTests
{
	[Fact]
	public async Task Uncompress_Chunk_Default()
	{
		var chunkBuffer = await File.ReadAllBytesAsync("files/chunk_compressed.bin", TestContext.Current.CancellationToken);
		var uncompressPoolBuffer = ArrayPool<byte>.Shared.Rent(10000000);
		FChunkInfo.TestDecompress(uncompressPoolBuffer, chunkBuffer, DecompressorBuilder.Default.Build());
		ArrayPool<byte>.Shared.Return(uncompressPoolBuffer);
	}
}

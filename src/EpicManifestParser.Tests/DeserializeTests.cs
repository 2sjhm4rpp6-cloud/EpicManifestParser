using EpicManifestParser.UE;

namespace EpicManifestParser.Tests;

public class DeserializeTests
{
	private const string BinaryPath = "files/manifest.bin";
	private const string JsonPath = "files/manifest.json";

	[Fact]
	public async Task Deserialize_Binary_Manifest_Buffer()
	{
		var manifestBuffer = await File.ReadAllBytesAsync(BinaryPath, TestContext.Current.CancellationToken);
		var manifest = FBuildPatchAppManifest.Deserialize(manifestBuffer);
		Assert.NotNull(manifest);

		// TODO: more assertions
	}

	[Fact]
	public async Task Deserialize_Binary_Manifest_File()
	{
		var manifest = await FBuildPatchAppManifest.DeserializeFileAsync(BinaryPath, cancellationToken: TestContext.Current.CancellationToken);
		Assert.NotNull(manifest);

		// TODO: more assertions
	}

	[Fact]
	public async Task Deserialize_Json_Manifest_Buffer()
	{
		var manifestBuffer = await File.ReadAllBytesAsync(JsonPath, TestContext.Current.CancellationToken);
		var manifest = FBuildPatchAppManifest.Deserialize(manifestBuffer);
		Assert.NotNull(manifest);

		// TODO: more assertions
	}

	[Fact]
	public async Task Deserialize_Json_Manifest_File()
	{
		var manifest = await FBuildPatchAppManifest.DeserializeFileAsync(JsonPath, cancellationToken: TestContext.Current.CancellationToken);
		Assert.NotNull(manifest);

		// TODO: more assertions
	}
}

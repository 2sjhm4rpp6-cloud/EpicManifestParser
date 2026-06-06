using System.Text.Json;
using System.Text.Json.Nodes;

namespace EpicManifestParser.Json;

internal static class JsonNodeExtensions
{
	private static readonly JsonSerializerOptions SerializerOptions = new()
	{
		Converters =
		{
			new FGuidConverter(),
			new FHashConverter<FSHAHash>(),
			new BlobStringConverter<uint8>(),
			new BlobStringConverter<int32>(),
			new BlobStringConverter<uint32>(),
			new BlobStringConverter<int64>(),
			new BlobStringConverter<uint64>(),
			new BlobStringConverter<EFeatureLevel>(),
			new BlobStringConverter<FSHAHash>(),
		}
	};

	extension(JsonNode? node)
	{
		public T GetBlob<T>(T defaultValue = default) where T : struct
		{
			return node.Deserialize<BlobString<T>?>(SerializerOptions)?.Value ?? defaultValue;
		}

		public FGuid GetFGuid()
		{
			return node.Deserialize<FGuid>(SerializerOptions);
		}

		public FSHAHash GetSha()
		{
			return node.Deserialize<FSHAHash>(SerializerOptions);
		}

		public string GetString(string defaultValue = "")
		{
			return node?.GetValue<string>() ?? defaultValue;
		}

		public T Get<T>(T defaultValue = default!)
		{
			return node is null ? defaultValue : node.GetValue<T>();
		}

		public T Parse<T>(T defaultValue = default!)
		{
			return node is null ? defaultValue : node.Deserialize<T>() ?? defaultValue;
		}
	}
}

using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace EpicManifestParser.UE;

// https://github.com/NotOfficer/UnrealEngine/blob/1436d646b9b11ffe9a46b04e9617dba689b56d35/Engine/Source/Runtime/Online/BuildPatchServices/Private/Common/Crypto.h?plain=1#L9C1-L21C4
[InlineArray(AES256_GCM_AuthTagSizeInBytes)]
internal struct FAESAuthTag
{
	private byte _element;

	public const int AES256_GCM_InitializationVectorSizeInBytes = 12;
	public const int AES256_GCM_AuthTagSizeInBytes = 16;
	public const int AES256_GCM_KeySizeInBytes = 32;
}

// https://github.com/NotOfficer/UnrealEngine/blob/1436d646b9b11ffe9a46b04e9617dba689b56d35/Engine/Source/Runtime/Online/BuildPatchServices/Private/Data/ManifestData.h?plain=1#L63C16-L63C16
internal sealed class FManifestHeader
{
	// https://github.com/NotOfficer/UnrealEngine/blob/1436d646b9b11ffe9a46b04e9617dba689b56d35/Engine/Source/Runtime/Online/BuildPatchServices/Private/Data/ManifestData.cpp?plain=1#L22C25-L22C25
	public const uint32 Magic = 0x44BEC00C;

	/// <summary>
	/// The version of this header and manifest data format, driven by the feature level.
	/// </summary>
	public readonly EFeatureLevel Version;
	/// <summary>
	/// The size of this header.
	/// </summary>
	public readonly int32 HeaderSize;
	/// <summary>
	/// The size of this data compressed.
	/// </summary>
	public readonly int32 DataSizeCompressed;
	/// <summary>
	/// The size of this data uncompressed.
	/// </summary>
	public readonly int32 DataSizeUncompressed;
	/// <summary>
	/// How the chunk data is stored.
	/// </summary>
	public readonly EManifestStorageFlags StoredAs;
	/// <summary>
	/// The SHA1 hash for the manifest data that follows.
	/// </summary>
	public readonly FSHAHash SHAHash;
	/// <summary>
	/// An ID that identifies the encryption secret required to decrypt certain manifest fields.
	/// </summary>
	public readonly FGuid? EncryptionSecretId;
	/// <summary>
	/// Without having the associated key, only chunk use, file hashes, and sizes can be understood.
	/// </summary>
	public readonly FAESAuthTag? EncryptionAuthTag;

	// https://github.com/NotOfficer/UnrealEngine/blob/1436d646b9b11ffe9a46b04e9617dba689b56d35/Engine/Source/Runtime/Online/BuildPatchServices/Private/Data/ManifestData.cpp?plain=1#L401C61-L401C61
	internal FManifestHeader(ref ManifestReader reader)
	{
		var magic = reader.Read<uint32>();
		if (magic != Magic)
			throw new FileLoadException($"Invalid manifest header magic: 0x{magic:X}");

		HeaderSize = reader.Read<int32>();
		DataSizeUncompressed = reader.Read<int32>();
		DataSizeCompressed = reader.Read<int32>();
		SHAHash = reader.Read<FSHAHash>();
		StoredAs = reader.Read<EManifestStorageFlags>();
		Version = HeaderSize > ManifestHeaderVersionSizes[(int32)EFeatureLevel.Original]
			? reader.Read<EFeatureLevel>()
			: EFeatureLevel.StoredAsCompressedUClass;

		if (Version >= EFeatureLevel.ChunkEncryptionSupport)
		{
			EncryptionSecretId = reader.Read<FGuid>();
			EncryptionAuthTag = reader.Read<FAESAuthTag>();
		}

		Debug.Assert(reader.Position == HeaderSize);
		reader.SetPosition(HeaderSize);
	}

	// https://github.com/NotOfficer/UnrealEngine/blob/1436d646b9b11ffe9a46b04e9617dba689b56d35/Engine/Source/Runtime/Online/BuildPatchServices/Private/Data/ManifestData.cpp?plain=1#L27C1-L40C4
	// The constant minimum sizes for each version of a header struct. Must be updated.
	// If new member variables are added the version MUST be bumped and handled properly here,
	// and these values must never change.
	private static ReadOnlySpan<uint32> ManifestHeaderVersionSizes =>
	[
		// EFeatureLevel::Original is 37B (32b Magic, 32b HeaderSize, 32b DataSizeUncompressed, 32b DataSizeCompressed, 160b SHA1, 8b StoredAs)
		// This remained the same all up to including EFeatureLevel::StoresPrerequisiteIds.
		37, 37, 37, 37, 37, 37, 37, 37, 37, 37, 37, 37, 37, 37,
		// EFeatureLevel::StoredAsBinaryData is 41B, (296b Original, 32b Version).
		// This remained the same all up to including EFeatureLevel::StoresUninstallActions.
		41, 41, 41, 41, 41, 41, 41, 41,
		// EFeatureLevel::ChunkEncryptionSupport added a 128b SecretId GUID, plus 128b AuthTag
		73, 73, 73
	];
}

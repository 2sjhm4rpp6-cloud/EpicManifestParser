namespace EpicManifestParser.UE;

// https://github.com/NotOfficer/UnrealEngine/blob/1436d646b9b11ffe9a46b04e9617dba689b56d35/Engine/Source/Runtime/Online/BuildPatchServices/Public/BuildPatchFeatureLevel.h?plain=1#L8C1-L11C34

/// <summary>
/// UE EFeatureLevel enum
/// </summary>
public enum EFeatureLevel
{
	/// <summary>
	/// The original version.
	/// </summary>
	Original = 0,
	/// <summary>
	/// Support for custom fields.
	/// </summary>
	CustomFields,
	/// <summary>
	/// Started storing the version number.
	/// </summary>
	StartStoringVersion,
	/// <summary>
	/// Made after data files where renamed to include the hash value, these chunks now go to ChunksV2.
	/// </summary>
	DataFileRenames,
	/// <summary>
	/// Manifest stores whether build was constructed with chunk or file data.
	/// </summary>
	StoresIfChunkOrFileData,
	/// <summary>
	/// Manifest stores group number for each chunk/file data for reference so that external readers don't need to know how to calculate them.
	/// </summary>
	StoresDataGroupNumbers,
	/// <summary>
	/// Added support for chunk compression, these chunks now go to ChunksV3. NB: Not File Data Compression yet.
	/// </summary>
	ChunkCompressionSupport,
	/// <summary>
	/// Manifest stores product prerequisites info.
	/// </summary>
	StoresPrerequisitesInfo,
	/// <summary>
	/// Manifest stores chunk download sizes.
	/// </summary>
	StoresChunkFileSizes,
	/// <summary>
	/// Manifest can optionally be stored using UObject serialization and compressed.
	/// </summary>
	StoredAsCompressedUClass,
	/// <summary>
	/// Removed and never used.
	/// </summary>
	UNUSED_0,
	/// <summary>
	/// Removed and never used.
	/// </summary>
	UNUSED_1,
	/// <summary>
	/// Manifest stores chunk data SHA1 hash to use in place of data compare, for faster generation.
	/// </summary>
	StoresChunkDataShaHashes,
	/// <summary>
	/// Manifest stores Prerequisite Ids.
	/// </summary>
	StoresPrerequisiteIds,
	/// <summary>
	/// The first minimal binary format was added. UObject classes will no longer be saved out when binary selected.
	/// </summary>
	StoredAsBinaryData,
	/// <summary>
	/// Temporary level where manifest can reference chunks with dynamic window size, but did not serialize them. Chunks from here onwards are stored in ChunksV4.
	/// </summary>
	VariableSizeChunksWithoutWindowSizeChunkInfo,
	/// <summary>
	/// Manifest can reference chunks with dynamic window size, and also serializes them.
	/// </summary>
	VariableSizeChunks,
	/// <summary>
	/// Manifest uses a build id generated from its metadata.
	/// </summary>
	UsesRuntimeGeneratedBuildId,
	/// <summary>
	/// Manifest uses a build id generated unique at build time, and stored in manifest.
	/// </summary>
	UsesBuildTimeGeneratedBuildId,
	/// <summary>
	/// Manifests generated with this feature level onwards will store the MD5 hash and the calculated MIME type of each file.
	/// </summary>
	StoresFileMD5HashesAndMIMEType,
	/// <summary>
	/// Manifests generated with this feature level onwards will store the SHA256 hash of each file.
	/// </summary>
	StoresFileSHA256Hashes,
	/// <summary>
	/// Added support for Uninstall Actions.
	/// </summary>
	StoresUninstallActions,
	/// <summary>
	/// Added full support for chunk encryption, additionally stores encyption secrets, as well as the CompressedDataSize and AuthTag
	/// for each chunk. Chunks from here onwards are stored in ChunksV5.
	/// </summary>
	ChunkEncryptionSupport,
	/// <summary>
	/// Added the secretID as part of the chunk pathing within a CloudDir
	/// </summary>
	ChunksStoredBySecret,
	/// <summary>
	/// Completed full support for BPS data encryption, if a manifest got encrypted, it will additionally store encryption data
	/// and most fields will have been replaced by empty or functional but not accurate data.
	/// </summary>
	ManifestEncryptionSupport,

	/// <summary>
	/// !! Always after the latest version entry, signifies the latest version plus 1 to allow the following Latest alias.
	/// </summary>
	LatestPlusOne,
	/// <summary>
	/// An alias for the actual latest version value.
	/// </summary>
	Latest = (LatestPlusOne - 1),
	/// <summary>
	/// An alias to provide the latest version of a manifest supported by file data (nochunks).
	/// </summary>
	LatestNoChunks = StoresChunkFileSizes,
	/// <summary>
	/// An alias to provide the latest version of a manifest supported by a json serialized format.
	/// </summary>
	LatestJson = StoresPrerequisiteIds,
	/// <summary>
	/// An alias to provide the latest version of a manifest supported by generation runs on platforms without OpenSSL module support.
	/// </summary>
	LatestNoOpenSSL = StoresFileMD5HashesAndMIMEType,
	/// <summary>
	/// An alias to provide the latest version of a manifest with unencrypted chunks.
	/// </summary>
	LatestUnencryptedChunks = StoresUninstallActions,
	/// <summary>
	/// An alias to provide the first available version of optimised delta manifest saving.
	/// </summary>
	FirstOptimisedDelta = UsesRuntimeGeneratedBuildId,

	/// <summary>
	/// More aliases, but this time for values that have been renamed
	/// </summary>
	StoresUniqueBuildId = UsesRuntimeGeneratedBuildId,

	/// <summary>
	/// JSON manifests were stored with a version of 255 during a certain CL range due to a bug.
	/// We will treat this as being StoresChunkFileSizes in code.
	/// </summary>
	BrokenJsonVersion = 255,
	/// <summary>
	/// This is for UObject default, so that we always serialize it.
	/// </summary>
	Invalid = -1
}

namespace EpicManifestParser.UE;

// https://github.com/NotOfficer/UnrealEngine/blob/1436d646b9b11ffe9a46b04e9617dba689b56d35/Engine/Source/Runtime/Online/BuildPatchServices/Private/Data/ManifestData.cpp?plain=1#L43C1-L56C4

internal enum EManifestMetaVersion : uint8
{
	Original = 0,
	SerialisesBuildId,
	SerialisesUnistallActions,
	SerialisesManifestEncryptionSecretId,

	// Always after the latest version, signifies the latest version plus 1 to allow initialization simplicity.
	LatestPlusOne,
	Latest = (LatestPlusOne - 1)
}

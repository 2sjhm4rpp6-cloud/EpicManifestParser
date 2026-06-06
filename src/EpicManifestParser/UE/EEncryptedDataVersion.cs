namespace EpicManifestParser.UE;

// https://github.com/NotOfficer/UnrealEngine/blob/1436d646b9b11ffe9a46b04e9617dba689b56d35/Engine/Source/Runtime/Online/BuildPatchServices/Private/Data/ManifestData.cpp?plain=1#L87C1-L97C4

internal enum EEncryptedDataVersion : uint8
{
	Original = 0,

	// Always after the latest version, signifies the latest version plus 1 to allow initialization simplicity.
	LatestPlusOne,
	Latest = (LatestPlusOne - 1)
}

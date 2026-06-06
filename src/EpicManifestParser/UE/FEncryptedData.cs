namespace EpicManifestParser.UE;

internal sealed class FEncryptedData
{
    public uint8[] Data { get; }
    public FEncryptedData(uint8[] data) => Data = data;

    internal static FEncryptedData ReadEncryptedData(ref ManifestReader reader)
    {
        int startPos = reader.Position;
        int32 dataSize = reader.Read<int32>();
        EEncryptedDataVersion dataVersion = reader.Read<EEncryptedDataVersion>();

        FEncryptedData result;

        if (dataVersion >= EEncryptedDataVersion.Original)
        {
            uint8[] data = reader.ReadArray<uint8>();
            result = new FEncryptedData(data);
        }
        else
        {
            result = new FEncryptedData([]);
        }

        reader.Position = startPos + dataSize;
        return result;
    }
}

namespace EpicManifestParser.UE;

/// <summary>
/// UE FCustomField struct
/// </summary>
public sealed class FCustomField
{
    /// <summary>
    /// Field name
    /// </summary>
    public string Name { get; internal set; } = "";
    /// <summary>
    /// Field value
    /// </summary>
    public string Value { get; internal set; } = "";

    internal FCustomField() { }

    internal static FCustomField[] ReadCustomFields(ref ManifestReader reader)
    {
        int startPos = reader.Position;
        int32 dataSize = reader.Read<int32>();
        EChunkDataListVersion dataVersion = reader.Read<EChunkDataListVersion>();
        int32 elementCount = reader.Read<int32>();

        var fields = new FCustomField[elementCount];
        Span<FCustomField> fieldsSpan = fields.AsSpan();

        if (dataVersion >= EChunkDataListVersion.Original)
        {
            for (int i = 0; i < elementCount; i++)
            {
                var field = new FCustomField();
                field.Name = reader.ReadFString();
                fieldsSpan[i] = field;
            }
            for (int i = 0; i < elementCount; i++)
                fieldsSpan[i].Value = reader.ReadFString();
        }
        else
        {
            var defaultField = new FCustomField();
            fieldsSpan.Fill(defaultField);
        }

        reader.Position = startPos + dataSize;
        return fields;
    }
}

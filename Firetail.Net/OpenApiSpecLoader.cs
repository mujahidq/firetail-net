using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Reader;


namespace Firetail;

internal class OpenApiSpecLoader
{
    public (OpenApiDocument, OpenApiDiagnostic) Load(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        using var memoryStream = new MemoryStream();
        stream.CopyTo(memoryStream);
        memoryStream.Position = 0; // Reset position to start for reading

        var reader = new OpenApiJsonReader();
        var result = reader.Read(memoryStream, new OpenApiReaderSettings
        {
            LeaveStreamOpen = false,
        });

        return (result.Document, result.Diagnostic);
    }
}
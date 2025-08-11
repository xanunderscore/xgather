using Newtonsoft.Json;
using System.IO;

namespace xgather;

public static class Json
{
    public static T Deserialize<T>(string path)
    {
        using var fstream = File.OpenRead(path);
        return Deserialize<T>(fstream);
    }

    public static T Deserialize<T>(Stream inputStream)
    {
        using var reader = new StreamReader(inputStream);
        using var js = new JsonTextReader(reader);
        return new JsonSerializer().Deserialize<T>(js) ?? throw new InvalidDataException("malformed json");
    }

    public static void Serialize<T>(string path, T val)
    {
        using var fstream = File.OpenWrite(path);
        using var writer = new StreamWriter(fstream);
        using var js = new JsonTextWriter(writer);
        JsonSerializer.Create(new JsonSerializerSettings()
        {
            Formatting = Formatting.Indented,
        }).Serialize(writer, val);
    }
}

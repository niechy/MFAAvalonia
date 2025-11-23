using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace MFAAvalonia.Extensions.MaaFW;

public class MaaToken
{
    private List<Dictionary<string, JToken>> Tokens = [];


    public void Merge(Dictionary<string, JToken> token)
    {
        Tokens.Add(token);
    }

    public static MaaToken FromDictionary(Dictionary<string, JToken> token)
    {
        MaaToken result = new MaaToken();
        result.Merge(token);
        return result;
    }

    public override string ToString()
    {
        var settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Ignore
        };
        return JsonConvert.SerializeObject(Tokens, settings);
    }
}

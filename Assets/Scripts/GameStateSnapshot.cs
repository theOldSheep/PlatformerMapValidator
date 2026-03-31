using System;
using System.Collections.Generic;
using System.Linq;

public struct FeatureSnapshot
{
    public object Raw;
    public int Encoded;

    public FeatureSnapshot(object raw, int encoded)
    {
        Raw = raw;
        Encoded = encoded;
    }
}

public struct GameStateSnapshot
{
    // Now stores the dual-representation
    public readonly FeatureSnapshot[] Data;
    private readonly int _cachedHash;

    public GameStateSnapshot(FeatureSnapshot[] data)
    {
        Data = data;
        
        // Hash based on Encoded values only for state comparison consistency
        int hash = 17;
        int prime = 97;
        int modulo = (int)1e9;
        foreach (var val in Data)
        {
            hash = (hash * prime + val.Encoded) % modulo;
        }
        _cachedHash = hash;
    }

    public override int GetHashCode() => _cachedHash;

    public override bool Equals(object obj)
    {
        if (!(obj is GameStateSnapshot other)) return false;
        if (Data.Length != other.Data.Length) return false;
        
        for (int i = 0; i < Data.Length; i++)
        {
            // Equality is usually determined by the encoded state
            if (Data[i].Encoded != other.Data[i].Encoded) return false;
        }
        return true;
    }

    public List<object> GetPlayerFeatures()
    {
        var result = new List<object>();
        int i = 0;
        while (i < Data.Length)
        {
            // The TypeID is stored in the 'Encoded' field of the header entry
            int typeId = Data[i].Encoded;
            Type valueType = StateTypeRegistry.GetTypeById(typeId);
            
            var features = (List<IStateFeature>)valueType.GetMethod("GetFeatures").Invoke(null, null);
            
            if (valueType == typeof(PlayerMovement))
            {
                for (int j = 0; j < features.Count; j++)
                {
                    // Return the original Raw values
                    result.Add(Data[i + j + 1].Raw);
                }
                return result;
            }
            
            i += 1 + features.Count; // Move to next object header
        }
        return result;
    }
}
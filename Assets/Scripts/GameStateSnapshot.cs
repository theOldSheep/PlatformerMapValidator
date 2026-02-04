using System;
using System.Collections.Generic;
using System.Linq;

public struct GameStateSnapshot
{
    public readonly int[] Data;
    private readonly int _cachedHash;

    public GameStateSnapshot(int[] data)
    {
        Data = data;
        
        // Rolling Hash (DJB2 or similar logic) to combine array elements
        int hash = 17;
        foreach (int val in Data)
        {
            unchecked { hash = hash * 31 + val; }
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
            if (Data[i] != other.Data[i]) return false;
        }
        return true;
    }

    public override string ToString()
    {
        if (Data == null || Data.Length == 0) return "Empty Snapshot";
        
        // Return a preview (e.g., "[10, 45, 12...]") to avoid massive strings in logs
        var preview = string.Join(", ", Data.Take(5));
        var suffix = Data.Length > 5 ? "..." : "";
        return $"Snapshot Hash: {_cachedHash:X} | Data: [{preview}{suffix}]";
    }
    
    public List<object> GetPlayerFeatures()
    {
        var result = new List<object>();

        for (int i = 0; i < Data.Length; i++)
        {
            Type valueType = StateTypeRegistry.GetTypeById(Data[i]);
            // TODO - maybe get this right, or just leave this validation for robustness...
            // if (! typeof(IStateFeature).IsAssignableFrom(valueType)) continue;

            List<IStateFeature> features = (List<IStateFeature>)valueType.GetMethod("GetFeatures").Invoke(null, null);
            // Decode the player element
            if (valueType == typeof(PlayerMovement))
            {
                for (int j = 0; j < features.Count; j ++)
                {
                    result.Add( features[j].Decode(Data[i + j + 1]) );
                }
                return result;
            }
            // Skip the current element
            else
            {
                i += features.Count;
            }

        }

        return result;
    }
}
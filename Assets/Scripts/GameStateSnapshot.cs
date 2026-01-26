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
}
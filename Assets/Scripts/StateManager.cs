using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class StateManager : MonoBehaviour
{
    // Internal struct to hold data while we sort
    private struct ObjectStateData
    {
        public int TypeID;
        public int[] Data;
    }

    private void Awake()
    {
        // Ensure registry is ready
        StateTypeRegistry.Initialize();
    }

    public GameStateSnapshot CaptureState()
    {
        // 1. Gather all providers
        var providers = FindObjectsOfType<MonoBehaviour>().OfType<IStateComponent>();
        
        List<ObjectStateData> capturedObjects = new List<ObjectStateData>();

        // 2. Process features into integers immediately
        foreach (var provider in providers)
        {
            int typeId = StateTypeRegistry.GetTypeId(provider);
            List<int> intFeatures = new List<int>();

            foreach (var feature in provider.GetFeatures())
            {
                if (feature.Type == FeatureType.Discrete)
                {
                    intFeatures.Add(Mathf.RoundToInt(feature.Value));
                }
                else
                {
                    // Continuous binning
                    int binned = Mathf.FloorToInt(feature.Value / feature.Granularity);
                    intFeatures.Add(binned);
                }
            }

            capturedObjects.Add(new ObjectStateData 
            { 
                TypeID = typeId, 
                Data = intFeatures.ToArray() 
            });
        }

        // 3. Sort: First by TypeID, then by the content of the Data array
        capturedObjects.Sort((a, b) => 
        {
            // Primary Sort: Class ID
            int typeCompare = a.TypeID.CompareTo(b.TypeID);
            if (typeCompare != 0) return typeCompare;

            // Secondary Sort: Lexicographical comparison of data values
            // This ensures consistent order for identical objects based on their state
            int minLength = Mathf.Min(a.Data.Length, b.Data.Length);
            for (int i = 0; i < minLength; i++)
            {
                int dataCompare = a.Data[i].CompareTo(b.Data[i]);
                if (dataCompare != 0) return dataCompare;
            }
            
            // If one array is shorter but matches so far, it comes first
            return a.Data.Length.CompareTo(b.Data.Length);
        });

        // 4. Flatten: [TypeID, Data..., TypeID, Data...]
        List<int> flatState = new List<int>();
        foreach (var obj in capturedObjects)
        {
            flatState.Add(obj.TypeID); // The Header
            flatState.AddRange(obj.Data); // The Payload
        }

        return new GameStateSnapshot(flatState.ToArray());
    }
    
    void Update()
    {
        // For debug visualization
        var snapshot = CaptureState();
        string debugStr = string.Join(",", snapshot.Data);
        // Debug.Log($"State: [{debugStr}]"); 
    }
}
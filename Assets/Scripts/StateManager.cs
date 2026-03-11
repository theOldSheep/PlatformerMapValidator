using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class StateManager : MonoBehaviour
{
    private struct ObjectStateData
    {
        public int TypeID;
        public List<FeatureSnapshot> FeaturePairs;
    }

    public GameStateSnapshot CaptureState()
    {
        var providers = FindObjectsOfType<MonoBehaviour>(true).OfType<IStateComponent>();
        List<ObjectStateData> capturedObjects = new List<ObjectStateData>();

        foreach (var provider in providers)
        {
            int typeId = StateTypeRegistry.GetTypeId(provider);
            var stateFeatures = (List<IStateFeature>)provider.GetType().GetMethod("GetFeatures").Invoke(null, null);
            var rawValues = provider.GetFeaturesRawValue();

            var pairs = new List<FeatureSnapshot>();
            for (int i = 0; i < stateFeatures.Count; i++)
            {
                int encoded = stateFeatures[i].Encode(rawValues[i]);
                pairs.Add(new FeatureSnapshot(rawValues[i], encoded));
            }

            capturedObjects.Add(new ObjectStateData { TypeID = typeId, FeaturePairs = pairs });
        }

        // Sorting remains based on Encoded values for determinism
        capturedObjects.Sort((a, b) => {
            int typeCompare = a.TypeID.CompareTo(b.TypeID);
            if (typeCompare != 0) return typeCompare;

            int minLength = Mathf.Min(a.FeaturePairs.Count, b.FeaturePairs.Count);
            for (int i = 0; i < minLength; i++)
            {
                int dataCompare = a.FeaturePairs[i].Encoded.CompareTo(b.FeaturePairs[i].Encoded);
                if (dataCompare != 0) return dataCompare;
            }
            return a.FeaturePairs.Count.CompareTo(b.FeaturePairs.Count);
        });

        List<FeatureSnapshot> flatState = new List<FeatureSnapshot>();
        foreach (var obj in capturedObjects)
        {
            // Header: TypeID (Raw is null or Type name, Encoded is ID)
            flatState.Add(new FeatureSnapshot(null, obj.TypeID));
            // Payload
            flatState.AddRange(obj.FeaturePairs);
        }

        return new GameStateSnapshot(flatState.ToArray());
    }
    
    public void RestoreState(GameStateSnapshot snapshot)
    {
        // Find all providers (including inactive ones so we can re-enable 'collected' items)
        var providers = FindObjectsOfType<MonoBehaviour>(true)
                        .OfType<IStateComponent>()
                        .ToList();

        // Sort providers identically to how they were sorted during CaptureState
        providers.Sort((a, b) => {
            int typeIdA = StateTypeRegistry.GetTypeId(a);
            int typeIdB = StateTypeRegistry.GetTypeId(b);
            return typeIdA.CompareTo(typeIdB);
            // Note: For perfect matching with multiple instances, 
            // a more complex stable sort/ID system is recommended.
        });

        int dataIndex = 0;
        int providerIndex = 0;

        while (dataIndex < snapshot.Data.Length && providerIndex < providers.Count)
        {
            int typeIdInSnapshot = snapshot.Data[dataIndex].Encoded;
            var provider = providers[providerIndex];

            if (StateTypeRegistry.GetTypeId(provider) == typeIdInSnapshot)
            {
                // Get feature definitions to know how many fields to read
                var features = (List<IStateFeature>)provider.GetType()
                            .GetMethod("GetFeatures").Invoke(null, null);

                List<object> rawValues = new List<object>();
                for (int j = 0; j < features.Count; j++)
                {
                    rawValues.Add(snapshot.Data[dataIndex + 1 + j].Raw);
                }

                provider.RestoreFeaturesRawValue(rawValues);
                
                dataIndex += 1 + features.Count; // Move past Header + Payload
                providerIndex++;
            }
            else
            {
                providerIndex++; // Skip scene objects not in snapshot
            }
        }
    }

    // void Update()
    // {
    //     // For debug visualization
    //     var snapshot = CaptureState();
    //     string debugStr = string.Join(",", snapshot.Data);
    //     string debugPlyFeatStr = string.Join(",", snapshot.GetPlayerFeatures());
    //     Debug.Log($"State: [{debugStr}]"); 
    //     Debug.Log($"Ply: [{debugPlyFeatStr}]"); 
    // }
}